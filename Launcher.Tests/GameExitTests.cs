using System.Diagnostics;
using NUnit.Framework;

namespace MapleStory2Offline.Tests;

/// <summary>LC-01 (BT-233, BT-229, BT-231): closing a started or left-over game, and the report's naming of the game's exit.</summary>
[TestFixture]
public class GameExitTests {
    private string dir = null!;

    [SetUp]
    public void SetUp() {
        dir = Path.Combine(Path.GetTempPath(), "GameExitTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(dir, true);

    // A process that waits for input until it is ended, standing in for a game that is still running.
    private static Process Waiting() => Process.Start(new ProcessStartInfo("cmd.exe", "/c more") {
        RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true,
    })!;

    [Test]
    public void CloseEndsARunningProcessAndAnEndedOneIsNoProblem() {
        using Process game = Waiting();
        Assert.That(game.HasExited, Is.False);
        Assert.That(GameExit.Close([game]), Is.True);
        Assert.That(game.HasExited, Is.True, "a cancelled start or a left-over game is ended");
        Assert.That(GameExit.Close([game]), Is.True, "already gone");
    }

    private string Log(params string[] lines) {
        string path = Path.Combine(dir, "game.log");
        File.WriteAllLines(path, lines);
        return path;
    }

    private const string Quit = " Maple2.Server.Core.Network.Session [INF] <5> Disconnected Session at Handle in C:\\src\\PacketHandlers\\QuitHandler.cs on line 24";

    [Test]
    public void QuitAgeIsTheTimeSinceTheLastQuitLine() {
        var now = new DateTime(2026, 11, 16, 10, 20, 30);
        string log = Log("10:00:01.000" + Quit, "10:20:11.500 Other [INF] <5> Disconnected Session at Foo in C:\\src\\Other.cs on line 3", "10:20:11.250" + Quit);
        Assert.That(GameExit.QuitAge(log, now), Is.EqualTo(TimeSpan.FromMilliseconds(18750)));
        Assert.That(GameExit.QuitAge(Log("10.20.11.250" + Quit), now), Is.EqualTo(TimeSpan.FromMilliseconds(18750)), "a '.' time separator");
        Assert.That(GameExit.QuitAge(Log("23:59:50.000" + Quit), new DateTime(2026, 11, 16, 0, 0, 5)), Is.EqualTo(TimeSpan.FromSeconds(15)), "past midnight");
        Assert.That(GameExit.QuitAge(Log("10:20:11.250 Other [INF] <5> nothing"), now), Is.Null, "no Quit");
        Assert.That(GameExit.QuitAge(Path.Combine(dir, "missing.log"), now), Is.Null, "no log");
    }

    [Test]
    public void ANonZeroExitSoonAfterQuitReadsCrashedWhileClosing() {
        // Report 645542c6: 0xCFFFFFFF 19 s after the player's own Quit.
        string closing = GameExit.Describe(unchecked((int) 0xCFFFFFFF), TimeSpan.FromSeconds(19));
        Assert.That(closing, Does.StartWith("The game crashed while closing"));
        Assert.That(closing, Does.Contain("19 s after your Quit"));
        Assert.That(GameExit.Describe(unchecked((int) 0xC0000005), TimeSpan.FromSeconds(30)), Does.StartWith("The game crashed while closing"), "30 s is inside");
        Assert.That(GameExit.Describe(unchecked((int) 0xC0000005), TimeSpan.FromSeconds(31)), Does.StartWith("The game closed unexpectedly"));
        Assert.That(GameExit.Describe(unchecked((int) 0xC0000005), null), Does.StartWith("The game closed unexpectedly").And.Contain("access violation"));
    }

    [Test]
    public void ExitCodesOneAndCfffffffGetANeutralName() {
        foreach (int code in new[] { 1, unchecked((int) 0xCFFFFFFF) }) {
            string text = GameExit.Describe(code, null);
            Assert.That(text, Does.Contain("a code whose meaning is not known"), text);
            Assert.That(text, Does.Not.Contain("crash").IgnoreCase, "what code 1 means is not known (BT-231)");
        }
        Assert.That(GameExit.Describe(1, null), Does.Contain("exit code 1 = 0x00000001"));
    }
}
