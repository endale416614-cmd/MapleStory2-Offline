using NUnit.Framework;

namespace MapleStory2Offline.Tests;

/// <summary>Report b98bf5a7: a folder that holds only the changed files of an update zip must be told apart from a complete copy.</summary>
[TestFixture]
public class CopyCheckTests {
    private const string WorldDll = "Maple2/Maple2.Server.World/bin/Release/net8.0/Autofac.dll";
    private string folder = null!;

    [SetUp]
    public void SetUp() {
        folder = Path.Combine(Path.GetTempPath(), "CopyCheckTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(folder, true);

    private string? Missing() => new Settings { Root = folder }.MissingServerFile();

    private void WriteList(params string[] names) =>
        File.WriteAllText(Path.Combine(folder, "files.json"), "{" + string.Join(",", names.Select(name => $"\"{name}\":\"0\"")) + "}");

    private void WriteFile(string name) {
        string path = Path.Combine(folder, name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    [Test]
    public void MissingServerFile_ListedWorldFileAbsent_ReturnsIt() {
        WriteList(WorldDll);
        Assert.That(Missing(), Is.EqualTo(WorldDll));
    }

    [Test]
    public void MissingServerFile_AllPresent_ReturnsNull() {
        WriteList(WorldDll);
        WriteFile(WorldDll);
        Assert.That(Missing(), Is.Null);
    }

    [Test]
    public void MissingServerFile_NoFilesJson_ReturnsNull() {
        Assert.That(Missing(), Is.Null);
    }

    [Test]
    public void MissingServerFile_MalformedFilesJson_ReturnsNull() {
        File.WriteAllText(Path.Combine(folder, "files.json"), "{\"Maple2/Maple2.Server.World/a.dll\":");
        Assert.That(Missing(), Is.Null);
    }

    [Test]
    public void MissingServerFile_OnlyPdbOrOtherFolderAbsent_ReturnsNull() {
        WriteList("Maple2/Maple2.Server.World/bin/Release/net8.0/Maple2.Server.World.pdb",
            "client-mods/offline-shop/Data/Xml/table/feature.xml");
        Assert.That(Missing(), Is.Null);
    }
}
