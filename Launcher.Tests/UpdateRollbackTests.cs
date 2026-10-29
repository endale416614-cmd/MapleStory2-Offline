using System.Text.Json;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace MapleStory2Offline.Tests;

/// <summary>BT-89: a rollback restores the save the update backed up, never the profile picked since then.</summary>
[TestFixture]
public class UpdateRollbackTests {
    private static readonly string[] Sides = ["", "-wal", "-shm"];
    private string folder = null!, backupDir = null!, backup = null!;

    [SetUp]
    public void SetUp() {
        folder = Path.Combine(Path.GetTempPath(), "UpdateRollbackTests_" + Guid.NewGuid().ToString("N"));
        backupDir = Path.Combine(folder, "updates", "backup-before-9.9.9-20261025-120000");
        backup = Path.Combine(backupDir, "SaveData", "game-server.db");
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        using (var connection = new SqliteConnection($"Data Source={backup};Pooling=False")) {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE marker (value TEXT); INSERT INTO marker VALUES ('before the update');";
            command.ExecuteNonQuery();
        }
        foreach (string profile in new[] { "", "Second" }) {
            Directory.CreateDirectory(Settings.SaveDirFor(folder, profile));
            foreach (string side in Sides) {
                File.WriteAllText(Save(profile) + side, Content(profile, side));
            }
        }
    }

    [TearDown]
    public void TearDown() {
        SqliteConnection.ClearAllPools();
        Directory.Delete(folder, true);
    }

    private string Save(string profile) => Path.Combine(Settings.SaveDirFor(folder, profile), "game-server.db");

    private static string Content(string profile, string side) => $"save '{profile}'{side}";

    /// <summary>Writes the journal, then rolls back with the profile the player picked at this start.</summary>
    private (string Result, Settings Settings) RollBack(string picked, object journal) {
        File.WriteAllText(Path.Combine(folder, "updates", "journal.json"), JsonSerializer.Serialize(journal));
        var settings = new Settings { Root = folder, DataRoot = folder, Profile = picked, Version = "9.9.9" };
        return (new UpdateInstaller(settings).RollBackLastUpdate(), settings);
    }

    [TestCase("", "Second", TestName = "The rollback restores the backed-up main save, not the profile picked since")]
    [TestCase("Second", "", TestName = "The rollback restores the backed-up profile, not the main save picked since")]
    public void RestoresOnlyTheBackedUpSave(string backedUp, string picked) {
        (string result, _) = RollBack(picked, new UpdateInstaller.Journal {
            Version = "9.9.9", PreviousVersion = "9.9.8", State = "installed", BackupDir = backupDir, SaveBackup = backup, SaveFile = Save(backedUp),
        });

        Assert.That(File.ReadAllBytes(Save(backedUp)), Is.EqualTo(File.ReadAllBytes(backup)), "the backed-up save is restored");
        Assert.That(File.Exists(Save(backedUp) + "-wal") || File.Exists(Save(backedUp) + "-shm"), Is.False, "the restored save's -wal and -shm are removed");
        foreach (string side in Sides) {
            Assert.That(File.ReadAllText(Save(picked) + side), Is.EqualTo(Content(picked, side)), "the picked profile's file is unchanged: game-server.db" + side);
        }
        Assert.That(result, Does.Contain("Your save from before the update was restored."));
    }

    [Test]
    public void JournalWithoutSaveFile_ChangesNoSave() {
        // The journal of a launcher from before 1.2.2: no SaveFile.
        (string result, Settings settings) = RollBack("Second", new {
            Version = "9.9.9", PreviousVersion = "9.9.8", State = "installed", BackupDir = backupDir, SaveBackup = backup,
        });

        foreach (string profile in new[] { "", "Second" }) {
            foreach (string side in Sides) {
                Assert.That(File.ReadAllText(Save(profile) + side), Is.EqualTo(Content(profile, side)), $"save '{profile}' is unchanged: game-server.db{side}");
            }
        }
        Assert.That(result, Does.Contain("No save was changed"));
        Assert.That(result, Does.Contain(backup), "the message names the copy");
        Assert.That(settings.Version, Is.EqualTo("9.9.8"), "the program files still go back");
        Assert.That(File.ReadAllText(Path.Combine(folder, "VERSION")), Is.EqualTo("9.9.8"));
    }
}
