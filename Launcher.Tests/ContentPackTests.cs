using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;

namespace MapleStory2Offline.Tests;

[TestFixture]
public class ContentPackTests {
    private const string ArchivePath = "Data/Xml.m2d";
    private string root = null!, client = null!, packDir = null!;
    private byte[] global = null!, tail = null!, origM2h = null!, newM2h = null!;

    private string Archive => Path.Combine(client, "Data", "Xml.m2d");
    private string M2h => Path.Combine(client, "Data", "Xml.m2h");
    private string OrigM2h => Path.Combine(client, "Data", "Xml.orig.m2h");

    [SetUp]
    public void SetUp() {
        root = Path.Combine(Path.GetTempPath(), "ms2-contentpack-" + Guid.NewGuid().ToString("N"));
        client = Path.Combine(root, "client");
        packDir = Path.Combine(root, "pack");
        Directory.CreateDirectory(Path.Combine(client, "Data"));
        Directory.CreateDirectory(Path.Combine(client, "GameData")); // Added this line
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
        WritePackJson(Sha(global));
    }

    [TearDown]
    public void TearDown() => Directory.Delete(root, true);

    private static byte[] Bytes(Random random, int count) {
        byte[] bytes = new byte[count];
        random.NextBytes(bytes);
        return bytes;
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private void WritePackJson(string globalSha) => File.WriteAllText(Path.Combine(packDir, ContentPack.FileName), JsonSerializer.Serialize(new {
        version = "1.2.0",
        archives = new[] { new {
            path = ArchivePath, globalSize = global.Length, globalSha256 = globalSha, tailSize = tail.Length, tailSha256 = Sha(tail),
            m2hSha256 = Sha(newM2h), origM2hSha256 = Sha(origM2h),
        } },
    }));

    private ContentPack.ArchiveState State() => ContentPack.Load(packDir).State(client).Single();

    [Test]
    public void FreshApply_AppendsTailAndReplacesHeader() {
        Assert.That(State().Status, Is.EqualTo(ContentPack.Status.NotApplied));

        Assert.That(ContentPack.Apply(client, packDir), Is.True);

        Assert.That(File.ReadAllBytes(Archive), Is.EqualTo(global.Concat(tail).ToArray()));
        Assert.That(File.ReadAllBytes(M2h), Is.EqualTo(newM2h));
        Assert.That(File.ReadAllBytes(OrigM2h), Is.EqualTo(origM2h));
        Assert.That(State().Status, Is.EqualTo(ContentPack.Status.Applied));
        Assert.That(File.ReadAllText(Path.Combine(packDir, "apply-journal.json")), Does.Contain("\"done\""));
    }

    [Test]
    public void ReApply_IsNoOp() {
        ContentPack.Apply(client, packDir);
        DateTime archiveWritten = File.GetLastWriteTimeUtc(Archive), headerWritten = File.GetLastWriteTimeUtc(M2h);

        Assert.That(ContentPack.Apply(client, packDir), Is.True);

        Assert.That(File.GetLastWriteTimeUtc(Archive), Is.EqualTo(archiveWritten));
        Assert.That(File.GetLastWriteTimeUtc(M2h), Is.EqualTo(headerWritten));
        Assert.That(File.ReadAllBytes(Archive), Is.EqualTo(global.Concat(tail).ToArray()));
    }

    [Test]
    public void HalfWrittenTail_IsRefusedAndReported() {
        File.WriteAllBytes(Archive, global.Concat(tail.Take(700)).ToArray());

        ContentPack.ArchiveState state = State();
        Assert.That(state.Status, Is.EqualTo(ContentPack.Status.Foreign));
        Assert.That(state.Reason, Does.Contain("unexpected size"));
        var ex = Assert.Throws<InvalidOperationException>(() => ContentPack.Apply(client, packDir));
        Assert.That(ex!.Message, Does.Contain(ArchivePath).And.Contain("Nothing was changed"));
        Assert.That(new FileInfo(Archive).Length, Is.EqualTo(global.Length + 700));
        Assert.That(File.ReadAllBytes(M2h), Is.EqualTo(origM2h));
    }

    [Test]
    public void WrongGlobalHash_IsRefused() {
        WritePackJson(Sha(tail)); // the pack expects a different client build

        Assert.That(State().Status, Is.EqualTo(ContentPack.Status.Foreign));
        Assert.Throws<InvalidOperationException>(() => ContentPack.Apply(client, packDir));
        Assert.That(File.ReadAllBytes(Archive), Is.EqualTo(global));
        Assert.That(File.ReadAllBytes(M2h), Is.EqualTo(origM2h));
        Assert.That(File.Exists(OrigM2h), Is.False);
    }

    [Test]
    public void TamperedTail_IsRefused() {
        byte[] tampered = (byte[])tail.Clone();
        tampered[10] ^= 0xFF;
        File.WriteAllBytes(Path.Combine(packDir, "Xml.tail"), tampered);

        var ex = Assert.Throws<InvalidOperationException>(() => ContentPack.Apply(client, packDir));
        Assert.That(ex!.Message, Does.Contain("Xml.tail"));
        Assert.That(File.ReadAllBytes(Archive), Is.EqualTo(global));
        Assert.That(File.ReadAllBytes(M2h), Is.EqualTo(origM2h));
    }

    [Test]
    public void Restore_ReturnsByteIdenticalFiles() {
        ContentPack.Apply(client, packDir);

        Assert.That(ContentPack.Restore(client, packDir), Is.True);

        Assert.That(File.ReadAllBytes(Archive), Is.EqualTo(global));
        Assert.That(File.ReadAllBytes(M2h), Is.EqualTo(origM2h));
        Assert.That(File.Exists(OrigM2h), Is.False);
        Assert.That(State().Status, Is.EqualTo(ContentPack.Status.NotApplied));
        Assert.That(ContentPack.Restore(client, packDir), Is.True); // nothing applied: still a no-op
    }

    [Test]
    public void UnexpectedSize_IsForeign() {
        File.WriteAllBytes(Archive, global.Concat(new byte[1]).ToArray());

        ContentPack.ArchiveState state = State();
        Assert.That(state.Status, Is.EqualTo(ContentPack.Status.Foreign));
        Assert.That(state.Reason, Does.Contain(ArchivePath).And.Contain("unexpected size"));
        Assert.Throws<InvalidOperationException>(() => ContentPack.Restore(client, packDir));
    }

    // An earlier pack (1.2.0's on a 1.2.1 update): another tail and header on the Global archive, the Global header backed up.
    private void EarlierPack(byte[] prefix) {
        File.WriteAllBytes(Archive, prefix.Concat(Bytes(new Random(3), 900)).ToArray());
        File.WriteAllBytes(M2h, Bytes(new Random(4), 350));
        File.WriteAllBytes(OrigM2h, origM2h);
    }

    [Test]
    public void EarlierPack_IsReplaced() {
        EarlierPack(global);
        Assert.That(State().Status, Is.EqualTo(ContentPack.Status.EarlierPack));

        Assert.That(ContentPack.Apply(client, packDir), Is.True);

        Assert.That(File.ReadAllBytes(Archive), Is.EqualTo(global.Concat(tail).ToArray()));
        Assert.That(File.ReadAllBytes(M2h), Is.EqualTo(newM2h));
        Assert.That(File.ReadAllBytes(OrigM2h), Is.EqualTo(origM2h));
        Assert.That(State().Status, Is.EqualTo(ContentPack.Status.Applied));
    }

    [Test]
    public void EarlierPack_Restore_ReturnsGlobalFiles() {
        EarlierPack(global);

        Assert.That(ContentPack.Restore(client, packDir), Is.True);

        Assert.That(File.ReadAllBytes(Archive), Is.EqualTo(global));
        Assert.That(File.ReadAllBytes(M2h), Is.EqualTo(origM2h));
        Assert.That(File.Exists(OrigM2h), Is.False);
        Assert.That(State().Status, Is.EqualTo(ContentPack.Status.NotApplied));
    }

    [Test]
    public void EarlierPack_WithoutBackupOrGlobalPrefix_IsForeign() {
        EarlierPack(global);
        File.Delete(OrigM2h);
        Assert.That(State().Status, Is.EqualTo(ContentPack.Status.Foreign));

        byte[] changed = (byte[])global.Clone();
        changed[10] ^= 0xFF; // a Steam update or another build under the earlier pack
        EarlierPack(changed);
        Assert.That(State().Status, Is.EqualTo(ContentPack.Status.Foreign));
        Assert.Throws<InvalidOperationException>(() => ContentPack.Apply(client, packDir));
    }

    [Test]
    public void PackJson_IsStrict() {
        Assert.Throws<InvalidDataException>(() => ContentPack.Parse("{\"version\": \"1.2.0\"}"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => ContentPack.Parse("{\"version\": \"1.2.0\", \"archives\": []}"u8.ToArray()));
        WritePackJson("nothex");
        Assert.Throws<InvalidDataException>(() => ContentPack.Load(packDir));
    }

    [Test]
    public void AppliedVersion_ReturnsNullForFreshClient() {
        Assert.That(ContentPack.Load(packDir).AppliedVersion(client), Is.Null);
    }

    [Test]
    public void AppliedVersion_ReturnsVersionForAppliedPack() {
        ContentPack.Apply(client, packDir);
        Assert.That(ContentPack.Load(packDir).AppliedVersion(client), Is.EqualTo("1.2.0"));
    }

    [Test]
    public void AppliedVersion_OneByteChange_IsForeignWithReason() {
        // Modify one byte of the Global archive to make it appear foreign
        byte[] modifiedGlobal = (byte[])global.Clone();
        modifiedGlobal[10] ^= 0xFF;
        File.WriteAllBytes(Archive, modifiedGlobal);

        var ex = Assert.Throws<InvalidOperationException>(() => ContentPack.Load(packDir).AppliedVersion(client));
        Assert.That(ex!.Message, Does.Contain("Data/Xml.m2d"));
        Assert.That(ex.Message, Does.Contain("unknown client build"));
        Assert.That(ex.Message, Does.Contain("Verify the game files"));
        Assert.That(ex.Message, Does.Not.Contains("partly applied"));
    }

    [Test]
    public void AppliedVersion_MixedArchives_IsPartlyApplied() {
        // Archive 1 applied with the one-archive pack from SetUp; then the pack grows a second, untouched archive.
        Assert.That(ContentPack.Apply(client, packDir), Is.True);
        var random = new Random(2);
        byte[] otherGlobal = Bytes(random, 2048), otherTail = Bytes(random, 1000), otherOrigM2h = Bytes(random, 200), otherNewM2h = Bytes(random, 300);
        File.WriteAllBytes(Path.Combine(client, "Data", "Other.m2d"), otherGlobal);
        File.WriteAllBytes(Path.Combine(client, "Data", "Other.m2h"), otherOrigM2h);
        File.WriteAllBytes(Path.Combine(packDir, "Other.tail"), otherTail);
        File.WriteAllBytes(Path.Combine(packDir, "Other.m2h"), otherNewM2h);
        WriteTwoArchivePackJson(otherGlobal, otherTail, otherNewM2h, otherOrigM2h);

        ContentPack pack = ContentPack.Load(packDir);
        Assert.That(pack.State(client).Select(s => s.Status), Is.EqualTo(new[] { ContentPack.Status.Applied, ContentPack.Status.NotApplied }));
        var ex = Assert.Throws<InvalidOperationException>(() => pack.AppliedVersion(client));
        Assert.That(ex!.Message, Does.Contain("partly applied"));
        Assert.That(ex.Message, Does.Contain("Data/Other.m2d"));
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
    public void ReadDataBuildPack_HandlesMissingFile() {
        Assert.That(ContentPack.ReadDataBuildPack(Path.Combine(client, "GameData", "data-build.json")), Is.Null);
    }

    [Test]
    public void ReadDataBuildPack_HandlesNoContentPackField() {
        File.WriteAllText(Path.Combine(client, "GameData", "data-build.json"), "{\"someOtherField\": \"value\"}");
        Assert.That(ContentPack.ReadDataBuildPack(Path.Combine(client, "GameData", "data-build.json")), Is.Null);
    }

    [Test]
    public void ReadDataBuildPack_HandlesValidContentPack() {
        File.WriteAllText(Path.Combine(client, "GameData", "data-build.json"), "{\"contentPack\": \"test-version-123\"}");
        Assert.That(ContentPack.ReadDataBuildPack(Path.Combine(client, "GameData", "data-build.json")), Is.EqualTo("test-version-123"));
    }

    [Test]
    public void ReadDataBuildPack_ThrowsForInvalidJson() {
        File.WriteAllText(Path.Combine(client, "GameData", "data-build.json"), "{invalid json}");
        var ex = Assert.Throws<InvalidDataException>(() => ContentPack.ReadDataBuildPack(Path.Combine(client, "GameData", "data-build.json")));
        Assert.That(ex!.Message, Does.Contain("is not valid JSON"));
    }

    [Test]
    public void ReadDataBuildPack_HandlesNullContentPack() {
        File.WriteAllText(Path.Combine(client, "GameData", "data-build.json"), "{\"contentPack\": null}");
        Assert.That(ContentPack.ReadDataBuildPack(Path.Combine(client, "GameData", "data-build.json")), Is.Null);
    }

    [Test]
    public void ReadDataBuildPack_HandlesEmptyContentPack() {
        File.WriteAllText(Path.Combine(client, "GameData", "data-build.json"), "{\"contentPack\": \"   \"}");
        var ex = Assert.Throws<InvalidDataException>(() => ContentPack.ReadDataBuildPack(Path.Combine(client, "GameData", "data-build.json")));
        Assert.That(ex!.Message, Does.Contain("contentPack is empty"));
    }

    [Test]
    public void ReadDataBuildPack_HandlesNonObjectRoot() {
        File.WriteAllText(Path.Combine(client, "GameData", "data-build.json"), "[]");
        var ex = Assert.Throws<InvalidDataException>(() => ContentPack.ReadDataBuildPack(Path.Combine(client, "GameData", "data-build.json")));
        Assert.That(ex!.Message, Does.Contain("the root is not a JSON object"));
    }

    [Test]
    public void ReadDataBuildPack_HandlesNonStringContentPack() {
        File.WriteAllText(Path.Combine(client, "GameData", "data-build.json"), "{\"contentPack\": 123}");
        var ex = Assert.Throws<InvalidDataException>(() => ContentPack.ReadDataBuildPack(Path.Combine(client, "GameData", "data-build.json")));
        Assert.That(ex!.Message, Does.Contain("contentPack is not a string"));
    }

    [Test]
    public void DataLockError_MatrixTests() {
        // (null,null) ok
        Assert.That(ContentPack.DataLockError(null, null), Is.Null);

        // ("v1","v1") ok
        Assert.That(ContentPack.DataLockError("v1", "v1"), Is.Null);

        // ("v1",null) error
        string error = ContentPack.DataLockError("v1", null);
        Assert.That(error, Is.Not.Null);
        Assert.That(error, Does.Contain("pack v1"));
        Assert.That(error, Does.Contain("Global data"));
        Assert.That(error, Does.Contain("launcher.ini"));

        // (null,"v1") error
        string error2 = ContentPack.DataLockError(null, "v1");
        Assert.That(error2, Is.Not.Null);
        Assert.That(error2, Does.Contain("server data was built for the unified content"));
        Assert.That(error2, Does.Contain("v1"));
        Assert.That(error2, Does.Contain("launcher.ini"));

        // ("v1","v2") error
        string error3 = ContentPack.DataLockError("v1", "v2");
        Assert.That(error3, Is.Not.Null);
        Assert.That(error3, Does.Contain("game has content pack v1"));
        Assert.That(error3, Does.Contain("server data was built for pack v2"));
        Assert.That(error3, Does.Contain("launcher.ini"));
    }

    [Test]
    public void Describe_NoPackJson() {
        // Remove the pack directory to simulate no pack.json
        Directory.Delete(packDir, true);
        Directory.CreateDirectory(packDir);

        string result = ContentPack.Describe(client, packDir);
        Assert.That(result, Does.StartWith("Content pack  : none"));
    }

    [Test]
    public void Describe_FreshClient() {
        string result = ContentPack.Describe(client, packDir);
        Assert.That(result, Does.Contain("Content pack  : 1.2.0"));
        Assert.That(result, Does.Contain("Data/Xml.m2d: Global size, Global header"));
        Assert.That(result, Does.Contain("Last journal  : none"));
    }

    [Test]
    public void Describe_AfterApply() {
        ContentPack.Apply(client, packDir);
        string result = ContentPack.Describe(client, packDir);
        Assert.That(result, Does.Contain("Content pack  : 1.2.0"));
        Assert.That(result, Does.Contain("Data/Xml.m2d: applied size, pack header"));
        Assert.That(result, Does.Contain("Last journal  : done"));
    }

    [Test]
    public void Describe_HalfApply() {
        // Simulate a half-apply by appending only part of the tail to the archive
        byte[] partialTail = tail.Take(700).ToArray();
        File.WriteAllBytes(Archive, global.Concat(partialTail).ToArray());

        // Create a journal file with "applying" state to simulate this
        string journalPath = Path.Combine(packDir, "apply-journal.json");
        string journalContent = JsonSerializer.Serialize(new {
            Version = "1.2.0-test",
            State = "applying",
            Archive = "Data/Xml.m2d",
            Step = "tail",
            Utc = DateTime.UtcNow.ToString("O")
        });
        File.WriteAllText(journalPath, journalContent);

        string result = ContentPack.Describe(client, packDir);
        Assert.That(result, Does.Contain("Content pack  : 1.2.0"));
        Assert.That(result, Does.Contain("UNEXPECTED size"));
        Assert.That(result, Does.Contain("applying 1.2.0-test Data/Xml.m2d tail"));
    }

    [Test]
    public void RecoverInterrupted_InterruptedAppend_Repaired() {
        // Create a half-appended archive: global + first 700 bytes of tail
        byte[] partialTail = tail.Take(700).ToArray();
        File.WriteAllBytes(Archive, global.Concat(partialTail).ToArray());

        // Create journal file with "applying" state and "tail" step
        string journalPath = Path.Combine(packDir, "apply-journal.json");
        string journalContent = JsonSerializer.Serialize(new {
            Version = "1.2.0",
            State = "applying",
            Archive = "Data/Xml.m2d",
            Step = "tail",
            Utc = DateTime.UtcNow.ToString("O")
        });
        File.WriteAllText(journalPath, journalContent);

        // Call RecoverInterrupted
        string? result = ContentPack.RecoverInterrupted(client, packDir);

        // Should return a message about the repair
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("Data/Xml.m2d"));

        // Archive should be truncated back to global size
        byte[] archiveBytes = File.ReadAllBytes(Archive);
        Assert.That(archiveBytes, Is.EqualTo(global));

        // Journal state should be updated to "recovered"
        string journalContentAfter = File.ReadAllText(journalPath);
        Assert.That(journalContentAfter, Does.Contain("\"recovered\""));
    }

    [Test]
    public void RecoverInterrupted_PowerLossCase_Repaired() {
        // Create an archive with global + tail.Length zero bytes (power loss case)
        byte[] zeroTail = new byte[tail.Length];
        File.WriteAllBytes(Archive, global.Concat(zeroTail).ToArray());

        // Create journal file with "applying" state and "tail" step
        string journalPath = Path.Combine(packDir, "apply-journal.json");
        string journalContent = JsonSerializer.Serialize(new {
            Version = "1.2.0",
            State = "applying",
            Archive = "Data/Xml.m2d",
            Step = "tail",
            Utc = DateTime.UtcNow.ToString("O")
        });
        File.WriteAllText(journalPath, journalContent);

        // Call RecoverInterrupted
        string? result = ContentPack.RecoverInterrupted(client, packDir);

        // Should return a message about the repair
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("Data/Xml.m2d"));

        // Archive should be truncated back to global size
        byte[] archiveBytes = File.ReadAllBytes(Archive);
        Assert.That(archiveBytes, Is.EqualTo(global));

        // Journal state should be updated to "recovered"
        string journalContentAfter = File.ReadAllText(journalPath);
        Assert.That(journalContentAfter, Does.Contain("\"recovered\""));
    }

    [Test]
    public void RecoverInterrupted_NoJournal_ReturnsNull() {
        // Create a half-appended archive without a journal file
        byte[] partialTail = tail.Take(700).ToArray();
        File.WriteAllBytes(Archive, global.Concat(partialTail).ToArray());

        // Call RecoverInterrupted - should return null since no journal exists
        string? result = ContentPack.RecoverInterrupted(client, packDir);

        Assert.That(result, Is.Null);

        // Archive should remain unchanged
        byte[] archiveBytes = File.ReadAllBytes(Archive);
        Assert.That(archiveBytes, Is.EqualTo(global.Concat(partialTail).ToArray()));
    }

    [Test]
    public void RecoverInterrupted_NoRepairWithoutCorrectPrefix_ReturnsNull() {
        // Create a half-appended archive but change one byte in the global prefix
        byte[] modifiedGlobal = (byte[])global.Clone();
        modifiedGlobal[10] ^= 0xFF; // Modify one byte
        byte[] partialTail = tail.Take(700).ToArray();
        File.WriteAllBytes(Archive, modifiedGlobal.Concat(partialTail).ToArray());

        // Create journal file with "applying" state and "tail" step
        string journalPath = Path.Combine(packDir, "apply-journal.json");
        string journalContent = JsonSerializer.Serialize(new {
            Version = "1.2.0",
            State = "applying",
            Archive = "Data/Xml.m2d",
            Step = "tail",
            Utc = DateTime.UtcNow.ToString("O")
        });
        File.WriteAllText(journalPath, journalContent);

        // Call RecoverInterrupted - should return null since prefix doesn't match
        string? result = ContentPack.RecoverInterrupted(client, packDir);

        Assert.That(result, Is.Null);

        // Archive should remain unchanged
        byte[] archiveBytes = File.ReadAllBytes(Archive);
        Assert.That(archiveBytes, Is.EqualTo(modifiedGlobal.Concat(partialTail).ToArray()));
    }

    [Test]
    public void RecoverInterrupted_JournalDone_ReturnsNull() {
        // Create a fully applied archive (correct size)
        File.WriteAllBytes(Archive, global.Concat(tail).ToArray());

        // Create journal file with "done" state
        string journalPath = Path.Combine(packDir, "apply-journal.json");
        string journalContent = JsonSerializer.Serialize(new {
            Version = "1.2.0",
            State = "done",
            Archive = "Data/Xml.m2d",
            Step = "tail",
            Utc = DateTime.UtcNow.ToString("O")
        });
        File.WriteAllText(journalPath, journalContent);

        // Call RecoverInterrupted - should return null since journal state is not "applying"
        string? result = ContentPack.RecoverInterrupted(client, packDir);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void RecoverInterrupted_ApplyAfterRecovery_Succeeds() {
        // Create a half-appended archive
        byte[] partialTail = tail.Take(700).ToArray();
        File.WriteAllBytes(Archive, global.Concat(partialTail).ToArray());

        // Create journal file with "applying" state and "tail" step
        string journalPath = Path.Combine(packDir, "apply-journal.json");
        string journalContent = JsonSerializer.Serialize(new {
            Version = "1.2.0",
            State = "applying",
            Archive = "Data/Xml.m2d",
            Step = "tail",
            Utc = DateTime.UtcNow.ToString("O")
        });
        File.WriteAllText(journalPath, journalContent);

        // Call RecoverInterrupted
        string? result = ContentPack.RecoverInterrupted(client, packDir);
        Assert.That(result, Is.Not.Null);

        // Now apply should succeed and make the archive fully applied
        Assert.That(ContentPack.Apply(client, packDir), Is.True);

        // Verify archive is now fully applied
        ContentPack.ArchiveState state = State();
        Assert.That(state.Status, Is.EqualTo(ContentPack.Status.Applied));
    }
}
