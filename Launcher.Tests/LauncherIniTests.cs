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
            "options that are the same in both modes and non-gameplay settings stay");
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
}
