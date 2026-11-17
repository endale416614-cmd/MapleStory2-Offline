using NUnit.Framework;

namespace MapleStory2Offline.Tests;

/// <summary>PA-05 (BT-234): an install updated by a zip keeps the Start menu shortcuts of the Setup that made it. Golden: a 1.2.2
/// Start menu folder has five shortcuts and no "Verify Game Files", which 1.2.3 added.</summary>
[TestFixture]
public class ShortcutsTests {
    private const string Product = "MapleStory 2 Offline";
    private string dir = null!;

    [SetUp]
    public void SetUp() {
        dir = Path.Combine(Path.GetTempPath(), "ShortcutsTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(dir, true);

    [Test]
    public void AFolderOf122LacksOnlyVerifyGameFiles() {
        foreach (string name in new[] { $"{Product}.lnk", $"{Product} - Export Diagnostics.lnk", $"{Product} - Report a Problem.lnk",
                     $"{Product} - Install Update.lnk", $"Uninstall {Product}.lnk" }) {
            File.WriteAllText(Path.Combine(dir, name), "");
        }

        var missing = Shortcuts.Missing(dir, Product);
        Assert.That(missing.Select(shortcut => (shortcut.Name, shortcut.Arguments)),
            Is.EqualTo(new[] { ($"{Product} - Verify Game Files.lnk", "--verify-files") }));
    }

    [Test]
    public void ACompleteFolderLacksNoneAndAMissingFolderGetsNone() {
        foreach ((string name, _, _) in Shortcuts.StartMenu(Product)) {
            File.WriteAllText(Path.Combine(dir, name), "");
        }

        Assert.That(Shortcuts.Missing(dir, Product), Is.Empty);
        Assert.That(Shortcuts.Missing(Path.Combine(dir, "gone"), Product), Is.Empty, "the player removed the folder");
        Assert.That(Shortcuts.StartMenu(Product), Has.Length.EqualTo(6));
    }
}
