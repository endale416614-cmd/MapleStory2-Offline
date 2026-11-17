using System.Diagnostics;
using NUnit.Framework;

namespace MapleStory2Offline.Tests;

/// <summary>PG-01 (BT-291): whose server a listener is, by the final paths of the program and of the server folder.</summary>
[TestFixture]
public class PortGuardTests {
    private string folder = null!;
    private string server = null!;
    private string other = null!;

    [SetUp]
    public void SetUp() {
        folder = Path.Combine(Path.GetTempPath(), "PortGuardTests_" + Guid.NewGuid().ToString("N"));
        server = Path.Combine(folder, "Maple2");
        other = Path.Combine(folder, "Copy", "Maple2");
        foreach (string dir in new[] { server, other }) {
            Directory.CreateDirectory(Path.Combine(dir, "Maple2.Server.World"));
            File.WriteAllText(Path.Combine(dir, "Maple2.Server.World", "Maple2.Server.World.exe"), "");
            File.WriteAllText(Path.Combine(dir, "Maple2.Server.World", "other.exe"), "");
        }
    }

    [TearDown]
    public void TearDown() {
        // A junction is removed on its own: a recursive delete must not reach its target.
        string link = Path.Combine(folder, "Link");
        if (Directory.Exists(link)) {
            Directory.Delete(link);
        }
        Directory.Delete(folder, true);
    }

    private string Exe(string dir) => Path.Combine(dir, "Maple2.Server.World", "Maple2.Server.World.exe");

    [Test]
    public void OwnServerIsOwn() {
        Assert.That(PortGuard.Classify(Exe(server), PortGuard.FinalPath(server)), Is.EqualTo(PortGuard.Owner.Own));
        Assert.That(PortGuard.Classify(Exe(server).ToUpperInvariant(), PortGuard.FinalPath(server)), Is.EqualTo(PortGuard.Owner.Own));
    }

    [Test]
    public void ServerFolderThroughAJunctionIsOwn() {
        string link = Path.Combine(folder, "Link");
        var make = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{server}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
        make.WaitForExit();
        Assert.That(Directory.Exists(link), "this machine refused to make the junction (mklink /J), so the junction case could not be tested");
        // The listener's path is the final one; the settings name the junction.
        Assert.That(PortGuard.Classify(Exe(server), PortGuard.FinalPath(link)), Is.EqualTo(PortGuard.Owner.Own));
        Assert.That(PortGuard.Classify(Exe(link), PortGuard.FinalPath(server)), Is.EqualTo(PortGuard.Owner.Own));
        Assert.That(PortGuard.Classify(Exe(other), PortGuard.FinalPath(link)), Is.EqualTo(PortGuard.Owner.OtherCopy));
    }

    [Test]
    public void ServerOfAnotherCopyIsOtherCopy() {
        Assert.That(PortGuard.Classify(Exe(other), PortGuard.FinalPath(server)), Is.EqualTo(PortGuard.Owner.OtherCopy));
    }

    [Test]
    public void FolderWithTheSamePrefixIsOtherCopy() {
        string old = Path.Combine(folder, "Maple2-old");
        Directory.CreateDirectory(Path.Combine(old, "Maple2.Server.World"));
        Assert.That(PortGuard.Classify(Exe(old), PortGuard.FinalPath(server)), Is.EqualTo(PortGuard.Owner.OtherCopy));
    }

    private static PortGuard.Listener Server(int port, int pid, string exe) => new(port, pid, exe, Path.GetFileName(exe));

    private (PortGuard.Listener, PortGuard.Owner) Entry(PortGuard.Listener listener) =>
        (listener, PortGuard.Classify(listener.ExePath, PortGuard.FinalPath(server)));

    [Test]
    public void ClaimGoesOnWhenTheServersOfTheOtherCopyAreGone() {
        PortGuard.Listener a = Server(20001, 1, Exe(other)), b = Server(21000, 2, Exe(other));
        PortGuard.Claim first = PortGuard.Before([Entry(a), Entry(b), Entry(Server(4000, 3, Exe(server)))]);
        Assert.That(first.Step, Is.EqualTo(PortGuard.Step.Ask));
        Assert.That(first.Held, Is.EqualTo(new[] { a, b }));
        Assert.That(PortGuard.Answer(first.Held, true).Step, Is.EqualTo(PortGuard.Step.Go));
        Assert.That(PortGuard.Afterwards([]).Step, Is.EqualTo(PortGuard.Step.Go));
    }

    [Test]
    public void ClaimRefusesAndNamesAServerThatCouldNotBeEnded() {
        PortGuard.Listener a = Server(20001, 1, Exe(other)), b = Server(21000, 2, Exe(other));
        Assert.That(PortGuard.Answer(PortGuard.Before([Entry(a), Entry(b)]).Held, true).Step, Is.EqualTo(PortGuard.Step.Go));
        PortGuard.Claim after = PortGuard.Afterwards([b]);
        Assert.That(after.Step, Is.EqualTo(PortGuard.Step.Refuse));
        Assert.That(after.Held.Select(PortGuard.Describe), Is.EqualTo(new[] { PortGuard.Describe(b) }));
        Assert.That(PortGuard.Describe(b), Does.Contain(Path.GetDirectoryName(Exe(other))));
        Assert.That(PortGuard.Afterwards([Server(20002, 4, Exe(server))]).Step, Is.EqualTo(PortGuard.Step.Refuse), "an own leftover that survived");
    }

    [Test]
    public void ClaimRefusesOnNoAndAsksNoQuestionWithAForeignProgram() {
        PortGuard.Listener a = Server(20001, 1, Exe(other)), foreign = new(4000, 9, @"C:\Windows\System32\svchost.exe", "svchost.exe");
        PortGuard.Claim first = PortGuard.Before([Entry(a)]);
        Assert.That(PortGuard.Answer(first.Held, false), Is.EqualTo(new PortGuard.Claim(PortGuard.Step.Refuse, first.Held)));
        PortGuard.Claim mixed = PortGuard.Before([Entry(a), Entry(foreign)]);
        Assert.That(mixed.Step, Is.EqualTo(PortGuard.Step.Refuse));
        Assert.That(mixed.Held, Is.EqualTo(new[] { a, foreign }));
        Assert.That(PortGuard.Before([Entry(Server(4000, 3, Exe(server)))]).Step, Is.EqualTo(PortGuard.Step.Go), "own leftovers alone are not asked about");
    }

    [Test]
    public void NoServerExeIsForeign() {
        string serverDir = PortGuard.FinalPath(server);
        Assert.That(PortGuard.Classify(Path.Combine(other, "Maple2.Server.World", "other.exe"), serverDir), Is.EqualTo(PortGuard.Owner.Foreign));
        Assert.That(PortGuard.Classify(Path.Combine(server, "Maple2.Server.World", "other.exe"), serverDir), Is.EqualTo(PortGuard.Owner.Foreign));
        Assert.That(PortGuard.Classify(@"C:\Windows\System32\svchost.exe", serverDir), Is.EqualTo(PortGuard.Owner.Foreign));
        Assert.That(PortGuard.Classify(null, serverDir), Is.EqualTo(PortGuard.Owner.Foreign));
    }

    [Test]
    public void RefusalLineNamesTheFolder() {
        var listener = new PortGuard.Listener(20001, 12, Exe(other), "Maple2.Server.World.exe");
        Assert.That(PortGuard.Describe(listener), Is.EqualTo($"port 20001: Maple2.Server.World.exe (process 12) in {Path.GetDirectoryName(Exe(other))}"));
        Assert.That(PortGuard.Describe(new PortGuard.Listener(4000, 7, null, "process 7")), Is.EqualTo("port 4000: process 7 (process 7)"));
    }
}
