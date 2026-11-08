using NUnit.Framework;

namespace MapleStory2Offline.Tests;

/// <summary>UG-01: Setup's swap deletes the old program folder, so what the old web server wrote goes along to the new one (BT-15).
/// Golden: a portrait path of an rc-1.2.2 install and the shop banner the release ships.</summary>
[TestFixture]
public class CarryWebDataTests {
    private const string Data = "Maple2/Maple2.Server.Web/Data/";
    private const string Portrait = Data + "profiles/20000000000/f1f21ca3-d786-472f-b2ef-e84bb4daea29.png";
    private const string GuildBanner = Data + "guildmark/1280523264/banner/8.png";
    private const string ShopBanner = Data + "system/banner/homeproduct_1.png";

    private string root = null!;
    private string oldDir = null!;
    private string newDir = null!;

    [SetUp]
    public void SetUp() {
        root = Path.Combine(Path.GetTempPath(), "CarryWebDataTests_" + Guid.NewGuid().ToString("N"));
        oldDir = Path.Combine(root, "program");
        newDir = Path.Combine(root, "program.new");
        Write(oldDir, Portrait, "my portrait");
        Write(oldDir, GuildBanner, "my guild's banner");
        Write(oldDir, ShopBanner, "old shop banner");
        Write(newDir, ShopBanner, "new shop banner");
    }

    [TearDown]
    public void TearDown() {
        Directory.Delete(root, true);
    }

    private static void Write(string folder, string relative, string text) {
        string path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static string Read(string folder, string relative) => File.ReadAllText(Path.Combine(folder, relative));

    [Test]
    public void ThePlayersFilesGoAlongAndTheNewShopBannerStays() {
        InstallInfo.CarryWebData(oldDir, newDir);
        Assert.Multiple(() => {
            Assert.That(Read(newDir, Portrait), Is.EqualTo("my portrait"));
            Assert.That(Read(newDir, GuildBanner), Is.EqualTo("my guild's banner"));
            Assert.That(Read(newDir, ShopBanner), Is.EqualTo("new shop banner"));
            Assert.That(Read(oldDir, Portrait), Is.EqualTo("my portrait"), "the old folder is the swap's to remove");
        });
    }

    [Test]
    public void AFileAlreadyThereStays() {
        // The program folder went first: what .old holds fills in and changes nothing that came from it.
        Write(newDir, Portrait, "from the program folder");
        InstallInfo.CarryWebData(oldDir, newDir);
        Assert.Multiple(() => {
            Assert.That(Read(newDir, Portrait), Is.EqualTo("from the program folder"));
            Assert.That(Read(newDir, GuildBanner), Is.EqualTo("my guild's banner"));
        });
    }

    [Test]
    public void NoOldFolderIsNoProblem() {
        Assert.DoesNotThrow(() => InstallInfo.CarryWebData(Path.Combine(root, "no such folder"), newDir));
    }

    [Test]
    public void AFileThatCannotBeCopiedStopsSetupWithTheOldFolderIntact() {
        Write(newDir, Data + "profiles", "a file where the folder must go");
        Assert.Throws<IOException>(() => InstallInfo.CarryWebData(oldDir, newDir));
        Assert.That(Read(oldDir, Portrait), Is.EqualTo("my portrait"));
    }
}
