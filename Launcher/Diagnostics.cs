using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MapleStory2Offline;

/// <summary>
/// "Export Diagnostics": one zip on the desktop with everything needed to investigate a problem, made without
/// starting the game or the servers. Text is redacted (Windows user/PC name, machine id). Logs are size-capped;
/// missing, locked or unreadable files are listed instead of failing the export. The save is only included when the
/// player agrees. Nothing is uploaded.
/// </summary>
internal static class Diagnostics {
    private const long MaxFileBytes = 5L * 1024 * 1024;

    /// <summary>Returns the zip path, or null if it could not be written anywhere.</summary>
    public static string? Export(Settings settings, bool includeSave, string? folderOverride = null) {
        var notes = new StringBuilder();
        string name = $"MapleStory2-Offline-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
        string[] folders = folderOverride != null ? [folderOverride] : [Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), settings.ReportDir];
        foreach (string folder in folders) {
            try {
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, name);
                using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create)) {
                    AddText(zip, "summary.txt", Summary(settings, notes));
                    AddText(zip, "install-check.txt", InstallCheck(settings));
                    AddLogs(zip, settings, notes);
                    if (includeSave) {
                        AddSave(zip, settings, notes);
                    }
                    AddText(zip, "export-notes.txt", notes.Length == 0 ? "All files were readable." : notes.ToString());
                }
                EventLog.Write("diagnostics", $"exported {name} (save included: {includeSave})");
                return path;
            } catch (Exception ex) {
                notes.AppendLine($"Could not write to {folder}: {ex.Message}");
            }
        }
        return null;
    }

    public static string BuildId(Settings settings) {
        string manifest = Path.Combine(settings.Root, "files.json");
        return File.Exists(manifest) ? $"{settings.Version}+{Hash(manifest)[..12].ToLowerInvariant()}" : $"{settings.Version}+dev";
    }

    private static string Summary(Settings settings, StringBuilder notes) {
        var text = new StringBuilder();
        text.AppendLine("MapleStory 2 (Offline) - diagnostics");
        text.AppendLine($"Exported : {DateTime.Now:yyyy-MM-dd HH:mm:ss} (UTC {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss})");
        text.AppendLine($"Build id : {BuildId(settings)}");
        text.AppendLine();
        text.AppendLine("-- Identity ---------------------------------------------------------------");
        text.AppendLine($"Launcher        : {Identity(Environment.ProcessPath)}");
        text.AppendLine($"Game server     : {Identity(Path.Combine(settings.ServerDir, "Maple2.Server.Game", "bin", settings.ServerConfiguration, "net8.0", "Maple2.Server.Game.dll"))}");
        string? client = ClientLocator.Find(settings.ClientDir);
        text.AppendLine($"Game client     : {(client == null ? "NOT FOUND" : Identity(Path.Combine(client, "MapleStory2.exe")))}");
        foreach (string dll in new[] { "NxCharacter64.dll", "AgarciumClient.dll" }) {
            string shipped = Path.Combine(settings.PatchDir, dll);
            string installed = client == null ? "" : Path.Combine(client, dll);
            string state = client == null ? "client not found"
                : !File.Exists(installed) ? "NOT INSTALLED"
                : File.Exists(shipped) && Hash(shipped) == Hash(installed) ? "matches this release" : "DIFFERENT from this release";
            text.AppendLine($"Patch {dll,-18}: {state} ({Identity(installed)})");
        }
        text.AppendLine($"Game data       : {DatabaseIdentity(Path.Combine(settings.GameDataDir, "maple-data.db"), notes) ?? DatabaseIdentity(Path.Combine(settings.SaveDir, "maple-data.db"), notes) ?? "NOT FOUND"}");
        text.AppendLine($"Save            : {DatabaseIdentity(settings.SaveFile, notes) ?? "no save yet"}");
        text.AppendLine();

        try {
            var report = new SessionReport(settings);
            report.Step("Diagnostics export (no game started)");
            text.AppendLine(report.BuildText());
        } catch (Exception ex) {
            notes.AppendLine($"Session report part failed: {ex.Message}");
        }
        return SessionReport.Redact(text.ToString());
    }

    /// <summary>Files of the release that are missing or modified (compared with files.json).</summary>
    private static string InstallCheck(Settings settings) {
        string manifest = Path.Combine(settings.Root, "files.json");
        if (!File.Exists(manifest)) {
            return "No files.json (developer or portable copy without a release manifest).";
        }
        try {
            var expected = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifest)) ?? [];
            var lines = new List<string>();
            int ok = 0;
            foreach ((string relative, string hash) in expected.OrderBy(entry => entry.Key)) {
                string path = Path.Combine(settings.Root, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path)) {
                    lines.Add($"MISSING   {relative}");
                } else if (relative is "VERSION" or "defaults.ini" or "MapleStory 2 Offline.exe" || relative.StartsWith("client-mods/offline-testkit/") || relative.StartsWith("client-mods/offline-dungeons/mod.json") || relative.StartsWith("client-mods/offline-pets/mod.json")) {
                    ok++; // changed by updates or by the launcher itself
                } else if (!string.Equals(Hash(path), hash, StringComparison.OrdinalIgnoreCase)) {
                    lines.Add($"MODIFIED  {relative}");
                } else {
                    ok++;
                }
            }
            return $"{ok} of {expected.Count} release files unchanged.\n" + string.Join("\n", lines.Take(300)) +
                   (lines.Count > 300 ? $"\n... {lines.Count - 300} more" : "");
        } catch (Exception ex) {
            return "Install check failed: " + ex.Message;
        }
    }

    private static void AddLogs(ZipArchive zip, Settings settings, StringBuilder notes) {
        var files = new List<(string Source, string Entry)>();
        foreach (string log in Directory.Exists(settings.LogDir) ? Directory.GetFiles(settings.LogDir, "*.log*") : []) {
            files.Add((log, "logs/" + Path.GetFileName(log)));
        }
        string history = Path.Combine(settings.LogDir, "history");
        if (Directory.Exists(history)) {
            foreach (DirectoryInfo session in new DirectoryInfo(history).GetDirectories().OrderByDescending(dir => dir.Name).Take(2)) {
                foreach (FileInfo log in session.GetFiles("*.log")) {
                    files.Add((log.FullName, $"logs/history/{session.Name}/{log.Name}"));
                }
            }
        }
        foreach (string file in new[] { "journal.json", "last-result.txt" }) {
            files.Add((Path.Combine(settings.UpdateDir, file), "updates/" + file));
        }
        if (ClientLocator.Find(settings.ClientDir) is { } client) {
            files.Add((Path.Combine(client, "offline_guard.log"), "client/offline_guard.log"));
            string ms2log = Path.Combine(Path.GetDirectoryName(client) ?? client, "ms2log");
            if (Directory.Exists(ms2log)) {
                foreach (FileInfo log in new DirectoryInfo(ms2log).GetFiles("MS2Log*.txt").OrderByDescending(file => file.LastWriteTimeUtc).Take(2)) {
                    files.Add((log.FullName, "client/" + log.Name));
                }
            }
        }
        foreach ((string source, string entry) in files) {
            string? text = ReadBounded(source, notes);
            if (text != null) {
                AddText(zip, entry, SessionReport.Redact(text));
            }
        }
    }

    private static void AddSave(ZipArchive zip, Settings settings, StringBuilder notes) {
        if (!File.Exists(settings.SaveFile)) {
            notes.AppendLine("Save requested but there is no save yet.");
            return;
        }
        string temp = Path.Combine(Path.GetTempPath(), $"ms2offline-save-{Guid.NewGuid():N}.db");
        try {
            if (SaveBackup.Create(settings.SaveFile, temp) is { } problem) {
                notes.AppendLine("Save could not be copied: " + problem);
                return;
            }
            zip.CreateEntryFromFile(temp, "save/game-server.db");
        } finally {
            File.Delete(temp);
        }
    }

    /// <summary>Text of a file, at most the last 5 MB; null (with a note) when missing, locked or unreadable.</summary>
    private static string? ReadBounded(string path, StringBuilder notes) {
        try {
            if (!File.Exists(path)) {
                return null;
            }
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            string prefix = "";
            if (stream.Length > MaxFileBytes) {
                stream.Seek(-MaxFileBytes, SeekOrigin.End);
                prefix = $"... first {(stream.Length - MaxFileBytes) / 1024} KB not included (file is {stream.Length / 1024} KB) ...\n";
                notes.AppendLine($"{Path.GetFileName(path)}: only the last 5 MB included");
            }
            using var reader = new StreamReader(stream);
            return prefix + reader.ReadToEnd();
        } catch (Exception ex) {
            notes.AppendLine($"{Path.GetFileName(path)}: not readable ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
    }

    private static string? DatabaseIdentity(string path, StringBuilder notes) {
        if (!File.Exists(path)) {
            return null;
        }
        try {
            using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
            connection.Open();
            long Scalar(string sql) {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = sql;
                return Convert.ToInt64(command.ExecuteScalar() ?? 0);
            }
            using SqliteCommand check = connection.CreateCommand();
            check.CommandText = "PRAGMA quick_check";
            string quick = check.ExecuteScalar() as string ?? "?";
            return $"{new FileInfo(path).Length / 1024} KB, schema {Scalar("PRAGMA user_version")}, " +
                   $"{Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'")} tables, quick_check {quick}";
        } catch (Exception ex) {
            notes.AppendLine($"{Path.GetFileName(path)}: could not be inspected ({ex.Message})");
            return $"present but unreadable ({ex.Message})";
        } finally {
            SqliteConnection.ClearAllPools();
        }
    }

    private static string Identity(string? path) {
        if (path == null || !File.Exists(path)) {
            return "missing";
        }
        var info = new FileInfo(path);
        string version = FileVersionInfo.GetVersionInfo(path).FileVersion ?? "-";
        return $"{info.Name} {version}, {info.Length} bytes, sha256 {Hash(path)[..16].ToLowerInvariant()}";
    }

    private static string Hash(string path) {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void AddText(ZipArchive zip, string entry, string text) {
        using var writer = new StreamWriter(zip.CreateEntry(entry, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }
}
