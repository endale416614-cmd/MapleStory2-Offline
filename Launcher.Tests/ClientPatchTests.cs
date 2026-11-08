using System.Security.Cryptography;
using NUnit.Framework;

namespace MapleStory2Offline.Tests;

/// <summary>BT-216: the patch files copied by hand over the game's own NxCharacter64.dll.</summary>
[TestFixture]
public class ClientPatchTests {
    private string root = null!, client = null!, patch = null!;

    [SetUp]
    public void SetUp() {
        root = Path.Combine(Path.GetTempPath(), "ClientPatchTests_" + Guid.NewGuid().ToString("N"));
        client = Path.Combine(root, "x64");
        patch = Path.Combine(root, "client-patch");
        Directory.CreateDirectory(client);
        Directory.CreateDirectory(patch);
        File.WriteAllText(Path.Combine(patch, "NxCharacter64.dll"), "the patch's proxy");
        File.WriteAllText(Path.Combine(patch, "AgarciumClient.dll"), "the patch");
        Put("MapleStory2.exe", "the game");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(root, true);

    private void Put(string name, string text) => File.WriteAllText(Path.Combine(client, name), text);

    /// <summary>Every file of the game folder with its text, to show that nothing in it changed.</summary>
    private string Folder() => string.Join("; ", Directory.GetFiles(client).Order().Select(file => Path.GetFileName(file) + "=" + File.ReadAllText(file)));

    [Test]
    public void MissingOriginal_ThePatchWithoutTheGamesOwn_Stops() {
        Put("NxCharacter64.dll", "the patch's proxy");
        Put("AgarciumClient.dll", "the patch");
        string before = Folder();
        string? message = ClientPatch.MissingOriginal(client, patch);
        Assert.That(message, Does.StartWith("The game's own NxCharacter64.dll is missing from the game folder:\n" + client + "\n\n"));
        Assert.That(message, Does.EndWith("Never copy the patch files by hand. If it still fails, post the report on Discord."));
        Assert.That(Folder(), Is.EqualTo(before), "nothing in the game folder changes");
        Put("NxCharacter64.dll", "an earlier build of the patch, which loads NxCharacter64.dll.bak");
        Assert.That(ClientPatch.MissingOriginal(client, patch), Is.Not.Null, "an earlier build of the patch");
        File.Delete(Path.Combine(client, "NxCharacter64.dll"));
        Assert.That(ClientPatch.MissingOriginal(client, patch), Is.Not.Null, "no NxCharacter64.dll at all");
    }

    [Test]
    public void MissingOriginal_TheGamesOwnOrItsBackup_Starts() {
        Put("NxCharacter64.dll", "the game's own");
        Assert.That(ClientPatch.MissingOriginal(client, patch), Is.Null, "before the first start");
        Put("NxCharacter64.dll", "the patch's proxy");
        Put("NxCharacter64.dll.bak", "the game's own");
        Assert.That(ClientPatch.MissingOriginal(client, patch), Is.Null, "installed by the launcher");
        File.Delete(Path.Combine(client, "NxCharacter64.dll"));
        Assert.That(ClientPatch.MissingOriginal(client, patch), Is.Null, "the backup alone: installing puts the patch beside it");
        Assert.That(ClientPatch.MissingOriginal(root, patch), Is.Null, "not a game folder");
    }

    [Test]
    public void ReportLines_BeforeTheFirstStart_NotInstalledYet() {
        Put("NxCharacter64.dll", "the game's own");
        string own = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(client, "NxCharacter64.dll"))));
        Assert.That(ClientPatch.ReportLines(client, patch, own), Is.EqualTo(new[] {
            "Patch NxCharacter64.dll   : not installed yet (the game's own file)",
            "Patch AgarciumClient.dll  : not installed yet",
            "Original DLL backup : not yet (made on first start)",
        }));
        Put("NxCharacter64.dll", "another program's file");
        Assert.That(ClientPatch.ReportLines(client, patch, own).First(), Is.EqualTo("Patch NxCharacter64.dll   : DIFFERENT from the shipped patch"),
            "neither the game's own nor the patch");
    }

    [Test]
    public void ReportLines_ThePatchWithoutTheGamesOwn_NamesIt() {
        Put("NxCharacter64.dll", "the patch's proxy");
        Put("AgarciumClient.dll", "the patch");
        Assert.That(ClientPatch.ReportLines(client, patch), Is.EqualTo(new[] {
            "Patch NxCharacter64.dll   : up to date",
            "Patch AgarciumClient.dll  : up to date",
            "Original DLL backup : MISSING: the patch's NxCharacter64.dll took the place of the game's own (see TROUBLESHOOTING.txt [T9])",
        }));
        File.Delete(Path.Combine(client, "NxCharacter64.dll"));
        Assert.That(ClientPatch.ReportLines(client, patch).Last(), Is.EqualTo("Original DLL backup : MISSING, and NxCharacter64.dll too (see TROUBLESHOOTING.txt [T9])"));
        Put("NxCharacter64.dll.bak", "the game's own");
        File.Delete(Path.Combine(client, "AgarciumClient.dll"));
        Assert.That(ClientPatch.ReportLines(client, patch), Is.EqualTo(new[] {
            "Patch NxCharacter64.dll   : MISSING",
            "Patch AgarciumClient.dll  : MISSING",
            "Original DLL backup : present",
        }), "once the launcher installed the patch, a missing file is a fault");
    }
}
