using Microsoft.Data.Sqlite;

namespace MapleStory2Offline;

/// <summary>Validated SQLite backups of the save.</summary>
internal static class SaveBackup {
    /// <summary>Online backup + PRAGMA integrity_check of the copy. Returns null when good, else the problem (copy removed).</summary>
    public static string? Create(string source, string target) {
        try {
            using (var from = new SqliteConnection($"Data Source={source};Mode=ReadOnly;Pooling=False"))
            using (var to = new SqliteConnection($"Data Source={target};Pooling=False")) {
                from.Open();
                to.Open();
                from.BackupDatabase(to);
                using SqliteCommand single = to.CreateCommand();
                single.CommandText = "PRAGMA journal_mode=DELETE"; // the copy is one self-contained file
                single.ExecuteNonQuery();
            }
            string? problem = Check(target);
            if (problem != null) {
                File.Delete(target);
            }
            return problem;
        } catch (Exception ex) {
            try {
                File.Delete(target);
            } catch (Exception) {
                // nothing to clean
            }
            return ex.Message;
        } finally {
            SqliteConnection.ClearAllPools();
        }
    }

    /// <summary>
    /// After the servers are stopped (killed, so SQLite could not tidy up), folds the write-ahead log back into the save
    /// so game-server.db alone holds all progress. Returns null when done or not needed, else the problem.
    /// </summary>
    public static string? Checkpoint(string path) {
        if (!File.Exists(path) || !File.Exists(path + "-wal")) {
            return null;
        }
        try {
            using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
            using SqliteDataReader reader = command.ExecuteReader();
            return reader.Read() && reader.GetInt32(0) != 0 ? "the save is busy" : null;
        } catch (Exception ex) {
            return ex.Message;
        } finally {
            SqliteConnection.ClearAllPools();
        }
    }

    /// <summary>null when the database passes integrity_check, else the first problem.</summary>
    public static string? Check(string path) {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check";
        string result = command.ExecuteScalar() as string ?? "no result";
        return result == "ok" ? null : result;
    }
}
