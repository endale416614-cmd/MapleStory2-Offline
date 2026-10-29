using NUnit.Framework;

namespace MapleStory2Offline.Tests;

[TestFixture]
public class LauncherIniTests {
    // The gameplay part of the 1.1.8 launcher.ini.example (Linux installs copied the example as launcher.ini).
    private static readonly string[] Ini118 = [
        "# Gameplay options",
        "GrindRates=true",
        "DungeonAllDrops=true",
        "UnlimitedDungeonClears=true",
        "MailNeverExpires=true",
        "ClassDropsOnly=true",
        "CheatCommands=true",
        "AutoUpdate=ask",
    ];

    [Test]
    public void PreModeFile_TrueOptionsAreCommentedOut() {
        List<string>? lines = LauncherIni.WithoutPreModeOptions(Ini118);

        Assert.That(lines, Is.Not.Null);
        string[] active = lines!.Where(line => !line.TrimStart().StartsWith('#')).ToArray();
        Assert.That(active, Is.EqualTo(new[] { "MailNeverExpires=true", "CheatCommands=true", "AutoUpdate=ask" }),
            "options outside the 1.1.9 mode list and non-gameplay settings stay");
        Assert.That(lines, Has.Count.EqualTo(Ini118.Length), "lines are commented out, not deleted");
        Assert.That(lines, Has.Some.StartsWith("#GrindRates=true"));
    }

    [Test]
    public void DeliberateOptOut_Stays() {
        List<string>? lines = LauncherIni.WithoutPreModeOptions(["GrindRates=false", "PetTaming = TRUE"]);

        Assert.That(lines, Is.EqualTo(new[] { "GrindRates=false", "#PetTaming = TRUE  (1.1.9: now set by the game mode you choose at start)" }));
    }

    [TestCase("GameMode=Legacy")]
    [TestCase("gamemode = Adapted")]
    public void FileWithGameMode_IsNeverChanged(string modeLine) {
        Assert.That(LauncherIni.WithoutPreModeOptions(["GrindRates=true", modeLine]), Is.Null);
    }

    [Test]
    public void NothingToChange_ReturnsNull() {
        Assert.That(LauncherIni.WithoutPreModeOptions(["# GrindRates=true", "AutoUpdate=off", @"ClientDir=C:\Games\x=y"]), Is.Null);
    }

    [Test]
    public void Migrate_RewritesTheFileOnce() {
        string path = Path.GetTempFileName();
        try {
            File.WriteAllLines(path, Ini118);
            LauncherIni.Migrate(path);
            string[] once = File.ReadAllLines(path);
            LauncherIni.Migrate(path);
            Assert.That(File.ReadAllLines(path), Is.EqualTo(once));
            Assert.That(once, Has.None.EqualTo("GrindRates=true"));
        } finally {
            File.Delete(path);
        }
    }

    [Test]
    public void FirstStart_WritesGameMode_SoLaterTrueLinesStay() {
        string root = Path.Combine(Path.GetTempPath(), "ms2-launcherini-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            File.WriteAllLines(Path.Combine(root, Settings.DefaultsFile), ["GameMode=Adapted"]);
            Settings first = Settings.Load(root);
            Assert.That(first.GameModeSet, Is.False, "defaults.ini's GameMode is not the player's choice");
            first.SaveUserValue("GameMode", first.GameMode); // what LauncherForm.ChooseGameMode does while GameModeSet is false
            File.AppendAllLines(Path.Combine(root, Settings.UserFile), ["GrindRates=true"]);

            Settings second = Settings.Load(root);

            Assert.That(second.GameModeSet, Is.True);
            Assert.That(second.GrindRates, Is.True, "a line the player adds after the first start stays on (BT-90)");
            Assert.That(File.ReadAllLines(Path.Combine(root, Settings.UserFile)), Does.Contain("GrindRates=true"));
        } finally {
            Directory.Delete(root, true);
        }
    }
}
