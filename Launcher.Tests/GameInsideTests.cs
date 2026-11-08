using NUnit.Framework;

namespace MapleStory2Offline.Tests;

/// <summary>Setup replaces or removes the whole program folder: a game kept inside it must be found first (BT-161).</summary>
[TestFixture]
public class GameInsideTests {
    private string folder = null!;

    [SetUp]
    public void SetUp() {
        folder = Path.Combine(Path.GetTempPath(), "GameInsideTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
    }

    [TearDown]
    public void TearDown() {
        Directory.Delete(folder, true);
    }

    private string Touch(string relative) {
        string path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    [TestCase("x64\\MapleStory2.exe", TestName = "The game's exe in the program folder's x64 folder is found")]
    [TestCase("4785291\\x64\\MapleStory2.exe", TestName = "The game's exe deeper in the program folder is found")]
    [TestCase("Data\\Xml.m2d", TestName = "The game's archives are found after x64 alone was moved out")]
    public void FindsTheGame(string relative) {
        string file = Touch(relative);

        Assert.That(Settings.GameInside(folder), Is.EqualTo(file));
    }

    [Test(Description = "The scan must not miss what the delete would remove")]
    public void HiddenFileCounts() {
        string file = Touch("x64\\MapleStory2.exe");
        File.SetAttributes(file, FileAttributes.Hidden);

        Assert.That(Settings.GameInside(folder), Is.EqualTo(file));
    }

    [Test(Description = "What the program and its content pack hold is not the game, so a normal repair is not refused")]
    public void NoGame() {
        Touch("MapleStory 2 Offline.exe");
        Touch("ContentPack\\Xml.m2h");
        Touch("ContentPack\\Xml.tail");

        Assert.That(Settings.GameInside(folder), Is.Null);
    }

    [Test(Description = "A first install: the folder is not there yet")]
    public void MissingFolder() {
        Assert.That(Settings.GameInside(Path.Combine(folder, "missing")), Is.Null);
    }
}
