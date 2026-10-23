using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;

namespace MapleStory2Offline.Tests;

[TestFixture]
public class ContentPackFlowTests {
    private string root = null!, client = null!, packDir = null!, globalDataDir = null!;
    private byte[] global = null!, tail = null!, origM2h = null!, newM2h = null!;
    private const string ArchivePath = "Data/Xml.m2d";

    private string Archive => Path.Combine(client, "Data", "Xml.m2d");
    private string M2h => Path.Combine(client, "Data", "Xml.m2h");
    private string OrigM2h => Path.Combine(client, "Data", "Xml.orig.m2h");

    [SetUp]
    public void SetUp() {
        root = Path.Combine(Path.GetTempPath(), "ms2-contentpackflow-" + Guid.NewGuid().ToString("N"));
        client = Path.Combine(root, "client");
        packDir = Path.Combine(root, "pack");
        globalDataDir = Path.Combine(root, "GameData");
        Directory.CreateDirectory(Path.Combine(client, "Data"));
        Directory.CreateDirectory(globalDataDir);
        Directory.CreateDirectory(packDir);
        var random = new Random(1);
        global = Bytes(random, 4096);
        tail = Bytes(random, 1500);
        origM2h = Bytes(random, 300);
        newM2h = Bytes(random, 400);
        File.WriteAllBytes(Archive, global);
        File.WriteAllBytes(M2h, origM2h);
        File.WriteAllBytes(Path.Combine(packDir, "Xml.tail"), tail);
        File.WriteAllBytes(Path.Combine(packDir, "Xml.m2h"), newM2h);
        // Create the GameData directory and write maple-data.db for tests that expect applied packs
        Directory.CreateDirectory(Path.Combine(packDir, "GameData"));
        File.WriteAllText(Path.Combine(packDir, "GameData", "maple-data.db"), "\0");
        WritePackJson(Sha(global));
    }

    [TearDown]
    public void TearDown() {
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) {
            File.SetAttributes(file, FileAttributes.Normal); // a test may leave an archive read-only, which blocks the delete
        }
        Directory.Delete(root, true);
    }

    private static byte[] Bytes(Random random, int count) {
        byte[] bytes = new byte[count];
        random.NextBytes(bytes);
        return bytes;
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private void WritePackJson(string globalSha) => File.WriteAllText(Path.Combine(packDir, ContentPack.FileName), JsonSerializer.Serialize(new {
        version = "1.2.0-test",
        archives = new[] { new {
            path = ArchivePath, globalSize = global.Length, globalSha256 = globalSha, tailSize = tail.Length, tailSha256 = Sha(tail),
            m2hSha256 = Sha(newM2h), origM2hSha256 = Sha(origM2h),
        } },
    }));

    private ContentPack.ArchiveState State() => ContentPack.Load(packDir).State(client).Single();

    [Test]
    public void NoPackFolder_GlobalDataNoError() {
        Directory.Delete(packDir, true);
        var result = ContentPackFlow.Prepare(true, client, packDir, globalDataDir, (a, b, c) => false);
        Assert.That(result.DataDir, Is.EqualTo(globalDataDir));
        Assert.That(result.AppliedVersion, Is.Null);
        Assert.That(result.Warning, Is.Null);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public void FreshClientWithUnifiedOn_AppliedDataDirPackGameDataNoError() {
        // First we need to make sure the pack's GameData directory exists and has a data-build.json file
        Directory.CreateDirectory(Path.Combine(packDir, "GameData"));
        File.WriteAllText(Path.Combine(packDir, "GameData", "data-build.json"), "{\"contentPack\": \"1.2.0-test\"}");

        var result = ContentPackFlow.Prepare(true, client, packDir, globalDataDir, (a, b, c) => false);
        Assert.That(result.DataDir, Is.EqualTo(Path.Combine(packDir, "GameData")));
        Assert.That(result.AppliedVersion, Is.EqualTo("1.2.0-test"));
        Assert.That(result.Warning, Is.Null);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public void AppliedPackWithMissingServerData_Error() {
        // First apply the pack
        ContentPack.Apply(client, packDir);

        // Delete the maple-data.db file to simulate missing server data
        File.Delete(Path.Combine(packDir, "GameData", "maple-data.db"));

        var result = ContentPackFlow.Prepare(true, client, packDir, globalDataDir, (a, b, c) => false);
        Assert.That(result.DataDir, Is.EqualTo(Path.Combine(packDir, "GameData")));
        Assert.That(result.AppliedVersion, Is.EqualTo("1.2.0-test"));
        Assert.That(result.Warning, Is.Null);
        Assert.That(result.Error, Does.Contain("server data of the unified content is missing"));
    }

    [Test]
    public void UnifiedOffWithOpenArchive_NoHashing_NoError() {
        // Fresh Global client, archive locked by another program: hashing it would throw, the shortcut must not hash.
        using var locked = new FileStream(Archive, FileMode.Open, FileAccess.Read, FileShare.None);
        var result = ContentPackFlow.Prepare(false, client, packDir, globalDataDir, (a, b, c) => false);
        Assert.That(result.DataDir, Is.EqualTo(globalDataDir));
        Assert.That(result.AppliedVersion, Is.Null);
        Assert.That(result.Warning, Is.Null);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public void UnifiedOffAfterApply_RestoredByteIdenticalGlobalData() {
        // First apply the pack
        var applied = ContentPackFlow.Prepare(true, client, packDir, globalDataDir, (a, b, c) => false);
        Assert.That(applied.AppliedVersion, Is.EqualTo("1.2.0-test"));

        // Then prepare with unified content off
        var result = ContentPackFlow.Prepare(false, client, packDir, globalDataDir, (a, b, c) => false);

        Assert.That(result.DataDir, Is.EqualTo(globalDataDir));
        Assert.That(result.AppliedVersion, Is.Null);
        Assert.That(result.Warning, Is.Null);
        Assert.That(result.Error, Is.Null);
        Assert.That(File.ReadAllBytes(Archive), Is.EqualTo(global));
        Assert.That(File.ReadAllBytes(M2h), Is.EqualTo(origM2h));
        Assert.That(File.Exists(OrigM2h), Is.False);
    }

    [Test]
    public void UnifiedOnButPackWithoutContentPackField_Error() {
        // First we need to make sure the pack's GameData directory exists and has a data-build.json file without contentPack field
        Directory.CreateDirectory(Path.Combine(packDir, "GameData"));
        File.WriteAllText(Path.Combine(packDir, "GameData", "data-build.json"), "{\"someOtherField\": \"value\"}");

        var result = ContentPackFlow.Prepare(true, client, packDir, globalDataDir, (a, b, c) => false);
        Assert.That(result.DataDir, Is.EqualTo(Path.Combine(packDir, "GameData")));
        Assert.That(result.AppliedVersion, Is.EqualTo("1.2.0-test"));
        Assert.That(result.Warning, Is.Null);
        Assert.That(result.Error, Does.Contain("Global data"));
    }

    [Test]
    public void ForeignClient_WarningGlobalDataNoErrorArchiveUnchanged() {
        // Modify one byte of the Global archive to make it appear foreign
        byte[] modifiedGlobal = (byte[])global.Clone();
        modifiedGlobal[10] ^= 0xFF;
        File.WriteAllBytes(Archive, modifiedGlobal);

        var result = ContentPackFlow.Prepare(true, client, packDir, globalDataDir, (a, b, c) => false);
        Assert.That(result.DataDir, Is.EqualTo(globalDataDir));
        Assert.That(result.AppliedVersion, Is.Null);
        Assert.That(result.Warning, Does.Contain("does not fit"));
        Assert.That(result.Error, Is.Null);

        // Verify the archive is still unchanged
        Assert.That(File.ReadAllBytes(Archive), Is.EqualTo(modifiedGlobal));
    }

    [Test]
    public void ApplyRefusedByReadOnlyArchive_ElevatedCalledFalse_WarningGlobalData() {
        // Make the archive read-only
        File.SetAttributes(Archive, FileAttributes.ReadOnly);

        try {
            var modes = new List<string>();
            var result = ContentPackFlow.Prepare(true, client, packDir, globalDataDir, (a, b, mode) => { modes.Add(mode); return false; });

            Assert.That(result.DataDir, Is.EqualTo(globalDataDir));
            Assert.That(result.AppliedVersion, Is.Null);
            Assert.That(result.Warning, Does.Contain("could not be written"));
            Assert.That(result.Error, Is.Null);
            Assert.That(modes, Is.EqualTo(new[] { "--apply-content-pack" }));
            Assert.That(File.ReadAllBytes(Archive), Is.EqualTo(global));
            Assert.That(File.ReadAllBytes(M2h), Is.EqualTo(origM2h));
        }
        finally {
            // Clean up read-only attribute
            File.SetAttributes(Archive, FileAttributes.Normal);
        }
    }

    [Test]
    public void MixedStates_Error() {
        // First apply the pack to make one archive applied
        ContentPack.Apply(client, packDir);

        // Now modify the second archive in the client to be read-only so it can't be applied
        var random = new Random(2);
        byte[] otherGlobal = Bytes(random, 2048), otherTail = Bytes(random, 1000), otherOrigM2h = Bytes(random, 200), otherNewM2h = Bytes(random, 300);
        File.WriteAllBytes(Path.Combine(client, "Data", "Other.m2d"), otherGlobal);
        File.WriteAllBytes(Path.Combine(client, "Data", "Other.m2h"), otherOrigM2h);
        File.WriteAllBytes(Path.Combine(packDir, "Other.tail"), otherTail);
        File.WriteAllBytes(Path.Combine(packDir, "Other.m2h"), otherNewM2h);
        WriteTwoArchivePackJson(otherGlobal, otherTail, otherNewM2h, otherOrigM2h);

        // Make the Other.m2d file read-only to simulate a failure
        string otherArchive = Path.Combine(client, "Data", "Other.m2d");
        File.SetAttributes(otherArchive, FileAttributes.ReadOnly);

        try {
            var result = ContentPackFlow.Prepare(true, client, packDir, globalDataDir, (a, b, c) => false);

            Assert.That(result.DataDir, Is.EqualTo(globalDataDir));
            Assert.That(result.AppliedVersion, Is.Null);
            Assert.That(result.Warning, Does.Contain("could not be written"));
            Assert.That(result.Error, Does.Contain("partly applied"));
        }
        finally {
            // Clean up read-only attribute
            File.SetAttributes(otherArchive, FileAttributes.Normal);
        }
    }

    private void WriteTwoArchivePackJson(byte[] otherGlobal, byte[] otherTail, byte[] otherNewM2h, byte[] otherOrigM2h) =>
        File.WriteAllText(Path.Combine(packDir, ContentPack.FileName), JsonSerializer.Serialize(new {
            version = "1.2.0",
            archives = new[] {
                new { path = ArchivePath, globalSize = global.Length, globalSha256 = Sha(global), tailSize = tail.Length, tailSha256 = Sha(tail),
                    m2hSha256 = Sha(newM2h), origM2hSha256 = Sha(origM2h) },
                new { path = "Data/Other.m2d", globalSize = otherGlobal.Length, globalSha256 = Sha(otherGlobal), tailSize = otherTail.Length,
                    tailSha256 = Sha(otherTail), m2hSha256 = Sha(otherNewM2h), origM2hSha256 = Sha(otherOrigM2h) },
            },
        }));

    [Test]
    public void DataLockError_WhenAppliedPackDoesNotMatchServerData() {
        // First apply the pack
        ContentPack.Apply(client, packDir);

        // Create the pack's GameData directory with data-build.json that has a different contentPack value
        Directory.CreateDirectory(Path.Combine(packDir, "GameData"));
        File.WriteAllText(Path.Combine(packDir, "GameData", "data-build.json"), "{\"contentPack\": \"other\"}");

        var result = ContentPackFlow.Prepare(true, client, packDir, globalDataDir, (a, b, c) => false);
        Assert.That(result.DataDir, Is.EqualTo(Path.Combine(packDir, "GameData")));
        Assert.That(result.AppliedVersion, Is.EqualTo("1.2.0-test"));
        Assert.That(result.Warning, Is.Null);
        Assert.That(result.Error, Does.Contain("pack other"));
    }

    [Test]
    public void UnifiedOn_PackWithoutContentPackField_GlobalData_Error() {
        // Create the pack's GameData directory with data-build.json that has no contentPack field
        Directory.CreateDirectory(Path.Combine(packDir, "GameData"));
        File.WriteAllText(Path.Combine(packDir, "GameData", "data-build.json"), "{}");

        var result = ContentPackFlow.Prepare(true, client, packDir, globalDataDir, (a, b, c) => false);
        Assert.That(result.DataDir, Is.EqualTo(Path.Combine(packDir, "GameData")));
        Assert.That(result.AppliedVersion, Is.EqualTo("1.2.0-test"));
        Assert.That(result.Warning, Is.Null);
        Assert.That(result.Error, Does.Contain("Global data"));
    }

    [Test]
    public void TestPackWithDamagedJson_Error() {
        // Write a damaged pack.json file
        File.WriteAllText(Path.Combine(packDir, ContentPack.FileName), "{invalid json}");

        var result = ContentPackFlow.Prepare(true, client, packDir, globalDataDir, (a, b, c) => false);
        Assert.That(result.DataDir, Is.EqualTo(globalDataDir));
        Assert.That(result.AppliedVersion, Is.Null);
        Assert.That(result.Warning, Is.Null);
        Assert.That(result.Error, Does.Contain("damaged"));
    }

    [Test]
    public void TailWithGlobalHeader_UnifiedOn_RepairsHeader() {
        // An apply interrupted between the tail and the header step: the tail is appended, the Global header is still there.
        byte[] appended = global.Concat(tail).ToArray();
        File.WriteAllBytes(Archive, appended);
        Directory.CreateDirectory(Path.Combine(packDir, "GameData"));
        File.WriteAllText(Path.Combine(packDir, "GameData", "data-build.json"), "{\"contentPack\": \"1.2.0-test\"}");

        var result = ContentPackFlow.Prepare(true, client, packDir, globalDataDir, (a, b, c) => false);
        Assert.That(result.DataDir, Is.EqualTo(Path.Combine(packDir, "GameData")));
        Assert.That(result.AppliedVersion, Is.EqualTo("1.2.0-test"));
        Assert.That(result.Warning, Is.Null);
        Assert.That(result.Error, Is.Null);
        Assert.That(File.ReadAllBytes(M2h), Is.EqualTo(newM2h));
        Assert.That(File.ReadAllBytes(Archive), Is.EqualTo(appended)); // repaired, the tail is not appended twice
    }

    [Test]
    public void RecoverInterruptedHalfAppendedArchive_PrepareReturnsWarning() {
        // Create a half-appended archive (global + partial tail)
        byte[] partialTail = tail.Take(700).ToArray();
        File.WriteAllBytes(Archive, global.Concat(partialTail).ToArray());

        // Create journal file with "applying" state and "tail" step
        string journalPath = Path.Combine(packDir, "apply-journal.json");
        string journalContent = JsonSerializer.Serialize(new {
            Version = "1.2.0-test",
            State = "applying",
            Archive = "Data/Xml.m2d",
            Step = "tail",
            Utc = DateTime.UtcNow.ToString("O")
        });
        File.WriteAllText(journalPath, journalContent);

        // Create GameData directory with data-build.json
        Directory.CreateDirectory(Path.Combine(packDir, "GameData"));
        File.WriteAllText(Path.Combine(packDir, "GameData", "data-build.json"), "{\"contentPack\": \"1.2.0-test\"}");

        var result = ContentPackFlow.Prepare(true, client, packDir, globalDataDir, (a, b, c) => false);

        // Should return a warning about the recovery
        Assert.That(result.Warning, Is.Not.Null);
        Assert.That(result.Warning, Does.Contain("interrupted"));

        // Should still be applied with correct version
        Assert.That(result.AppliedVersion, Is.EqualTo("1.2.0-test"));
        Assert.That(result.Error, Is.Null);

        // Archive should have been truncated back to global size and then applied again: Global + the whole tail
        byte[] archiveBytes = File.ReadAllBytes(Archive);
        Assert.That(archiveBytes, Is.EqualTo(global.Concat(tail).ToArray()));
    }

    [Test]
    public void RestoreRefused_Error() {
        Directory.CreateDirectory(Path.Combine(packDir, "GameData"));
        File.WriteAllText(Path.Combine(packDir, "GameData", "data-build.json"), "{\"contentPack\": \"1.2.0-test\"}");
        ContentPackFlow.Prepare(true, client, packDir, globalDataDir, (a, b, c) => false);
        Assert.That(State().Status, Is.EqualTo(ContentPack.Status.Applied));
        File.SetAttributes(Archive, FileAttributes.ReadOnly); // TearDown clears it

        var modes = new List<string>();
        var result = ContentPackFlow.Prepare(false, client, packDir, globalDataDir, (a, b, mode) => { modes.Add(mode); return false; });
        Assert.That(result.DataDir, Is.EqualTo(globalDataDir));
        Assert.That(result.Error, Does.Contain("could not be removed"));
        Assert.That(modes, Is.EqualTo(new[] { "--restore-content-pack" }));
    }

    [Test]
    public void ForeignClient_UnifiedOff_NoWarningNothingWritten() {
        byte[] foreign = (byte[])global.Clone();
        foreign[10] ^= 0xFF;
        File.WriteAllBytes(Archive, foreign);

        var modes = new List<string>();
        var result = ContentPackFlow.Prepare(false, client, packDir, globalDataDir, (a, b, mode) => { modes.Add(mode); return false; });
        Assert.That(result.DataDir, Is.EqualTo(globalDataDir));
        Assert.That(result.AppliedVersion, Is.Null);
        Assert.That(result.Warning, Is.Null);
        Assert.That(result.Error, Is.Null);
        Assert.That(File.ReadAllBytes(Archive), Is.EqualTo(foreign));
        Assert.That(File.ReadAllBytes(M2h), Is.EqualTo(origM2h));
        Assert.That(File.Exists(OrigM2h), Is.False);
        Assert.That(modes, Is.Empty);
    }

    [Test]
    public void NoPackFolder_GlobalDataNamesPack_LockError() {
        Directory.Delete(packDir, true);
        File.WriteAllText(Path.Combine(globalDataDir, "data-build.json"), "{\"contentPack\": \"x\"}");

        var result = ContentPackFlow.Prepare(true, client, packDir, globalDataDir, (a, b, c) => false);
        Assert.That(result.DataDir, Is.EqualTo(globalDataDir));
        Assert.That(result.AppliedVersion, Is.Null);
        Assert.That(result.Error, Does.Contain("pack x"));
    }

    [Test]
    public void RestoreThrows_WarnsAndKeepsPackData() {
        Directory.CreateDirectory(Path.Combine(packDir, "GameData"));
        File.WriteAllText(Path.Combine(packDir, "GameData", "data-build.json"), "{\"contentPack\": \"1.2.0-test\"}");
        ContentPackFlow.Prepare(true, client, packDir, globalDataDir, (a, b, c) => false);
        Assert.That(State().Status, Is.EqualTo(ContentPack.Status.Applied));
        File.Delete(OrigM2h); // Restore cannot put the Global header back and throws InvalidOperationException

        var modes = new List<string>();
        var result = ContentPackFlow.Prepare(false, client, packDir, globalDataDir, (a, b, mode) => { modes.Add(mode); return false; });
        Assert.That(result.DataDir, Is.EqualTo(Path.Combine(packDir, "GameData")));
        Assert.That(result.AppliedVersion, Is.EqualTo("1.2.0-test"));
        Assert.That(result.Warning, Does.Contain("Xml.orig.m2h"));
        Assert.That(result.Error, Is.Null);
        Assert.That(modes, Is.Empty);
    }

    // A client with an earlier pack (1.2.0's on a 1.2.1 update): another tail and header, the Global header backed up.
    private byte[] EarlierPack() {
        byte[] earlier = global.Concat(Bytes(new Random(3), 900)).ToArray();
        File.WriteAllBytes(Archive, earlier);
        File.WriteAllBytes(M2h, Bytes(new Random(4), 350));
        File.WriteAllBytes(OrigM2h, origM2h);
        return earlier;
    }

    [Test]
    public void EarlierPack_UnifiedOn_ReplacedByThisPack() {
        EarlierPack();
        File.WriteAllText(Path.Combine(packDir, "GameData", "data-build.json"), "{\"contentPack\": \"1.2.0-test\"}");

        var result = ContentPackFlow.Prepare(true, client, packDir, globalDataDir, (a, b, c) => false);
        Assert.That(result.DataDir, Is.EqualTo(Path.Combine(packDir, "GameData")));
        Assert.That(result.AppliedVersion, Is.EqualTo("1.2.0-test"));
        Assert.That(result.Warning, Is.Null);
        Assert.That(result.Error, Is.Null);
        Assert.That(File.ReadAllBytes(Archive), Is.EqualTo(global.Concat(tail).ToArray()));
        Assert.That(File.ReadAllBytes(M2h), Is.EqualTo(newM2h));
        Assert.That(File.ReadAllBytes(OrigM2h), Is.EqualTo(origM2h));
    }

    [Test]
    public void EarlierPack_UnifiedOff_RestoresGlobal() {
        EarlierPack();

        var result = ContentPackFlow.Prepare(false, client, packDir, globalDataDir, (a, b, c) => false);
        Assert.That(result.DataDir, Is.EqualTo(globalDataDir));
        Assert.That(result.AppliedVersion, Is.Null);
        Assert.That(result.Warning, Is.Null);
        Assert.That(result.Error, Is.Null);
        Assert.That(File.ReadAllBytes(Archive), Is.EqualTo(global));
        Assert.That(File.ReadAllBytes(M2h), Is.EqualTo(origM2h));
        Assert.That(File.Exists(OrigM2h), Is.False);
    }

    [Test]
    public void ApplyThrows_TamperedTail_WarnsAndStartsGlobal() {
        byte[] tampered = (byte[])tail.Clone();
        tampered[10] ^= 0xFF;
        File.WriteAllBytes(Path.Combine(packDir, "Xml.tail"), tampered);

        var modes = new List<string>();
        var result = ContentPackFlow.Prepare(true, client, packDir, globalDataDir, (a, b, mode) => { modes.Add(mode); return false; });
        Assert.That(result.DataDir, Is.EqualTo(globalDataDir));
        Assert.That(result.AppliedVersion, Is.Null);
        Assert.That(result.Warning, Does.Contain("Xml.tail"));
        Assert.That(result.Error, Is.Null);
        Assert.That(File.ReadAllBytes(Archive), Is.EqualTo(global));
        Assert.That(File.ReadAllBytes(M2h), Is.EqualTo(origM2h));
        Assert.That(modes, Is.Empty);
    }
}
