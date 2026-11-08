using System.IO.Compression;
using System.Text.Json;
using NUnit.Framework;

namespace MapleStory2Offline.Tests;

/// <summary>Report d6a032e8: an update zip that is not newer than the installed version is moved aside, never a problem.</summary>
[TestFixture]
public class UpdatePendingTests {
    private string folder = null!;

    [SetUp]
    public void SetUp() {
        folder = Path.Combine(Path.GetTempPath(), "UpdatePendingTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
    }

    [TearDown]
    public void TearDown() {
        Directory.Delete(folder, true);
    }

    /// <summary>An update zip in the program folder with only its update.json (versions are checked before the signature).</summary>
    private string Zip(string version) {
        string path = Path.Combine(folder, $"MapleStory2-Offline-Update-{version}.zip");
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry("update.json").Open());
        writer.Write(JsonSerializer.Serialize(new { version, minVersion = "1.2.0" }));
        return path;
    }

    [TestCase("1.2.1", TestName = "An older update zip is moved aside without an error")]
    [TestCase("1.2.2", TestName = "An update zip of the installed version is moved aside without an error")]
    public void StaleZipIsSkipped(string version) {
        string zip = Zip(version);
        var settings = new Settings { Root = folder, DataRoot = folder, Version = "1.2.2" };

        Assert.That(new UpdateInstaller(settings).InstallPending(), Is.Empty);
        Assert.That(File.Exists(zip), Is.False);
        Assert.That(File.Exists(Path.Combine(folder, "updates", "skipped", Path.GetFileName(zip))), Is.True);
    }
}
