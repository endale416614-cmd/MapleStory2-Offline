using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace MapleStory2Offline;

/// <summary>
/// Manual updates. The player gets an update zip (made by tools/package_release.py) and either copies it, unopened,
/// into the program folder or the data "updates" folder, or picks it with "Install Update". On the next start:
///
/// 1. Checks: the zip has update.json (version, minVersion) and files.json (sha256 of every release file); the game
///    and this installation's servers are closed; enough free disk space; no file to replace is locked.
/// 2. Stage: every file is extracted into updates\staging-&lt;version&gt; (paths must stay inside the program folder, links
///    are refused) and must match its files.json hash. A corrupted zip is rejected before anything changes.
/// 3. Backup: the save is copied with SQLite online backup and checked (integrity_check) - no valid backup, no update.
/// 4. Apply: a journal (updates\journal.json) records every step; each replaced file is copied to the backup folder
///    first. Interrupted (crash, power loss): the next start restores the backups and reports it.
/// 5. After a successful start of the new version the update is marked verified; if the new version cannot start the
///    launcher offers a rollback that restores the program files and the pre-update save.
///
/// Authenticity: the hashes only prove the files are the ones the zip's own files.json names. Anyone who can make a
/// zip can make a matching files.json, so they detect corruption, not tampering. Get updates from the official
/// release page only. (No signing key exists yet; see docs/RELEASE-BLOCKERS.md.)
/// </summary>
internal sealed class UpdateInstaller {
    private const string ManifestName = "update.json";
    private const string HashesName = "files.json";
    private static readonly string[] Protected = ["SaveData/", "logs/", "reports/", "updates/", "launcher.ini", "install.json", "install-id"];

    private readonly Settings settings;

    public UpdateInstaller(Settings settings) {
        this.settings = settings;
    }

    public sealed record Manifest(string Version, string MinVersion, string[]? Delete, string? Notes);
    public sealed record Result(string Version, string? Notes, bool LauncherReplaced);

    public sealed class Journal {
        public string Version { get; set; } = "";
        public string PreviousVersion { get; set; } = "";
        public string State { get; set; } = "";   // applying, installed, rolledback
        public bool Verified { get; set; }
        public string BackupDir { get; set; } = "";
        public string? SaveBackup { get; set; }
        public List<string> Replaced { get; set; } = [];   // relative paths overwritten (backup exists)
        public List<string> Added { get; set; } = [];      // relative paths that did not exist before
        public List<string> Deleted { get; set; } = [];    // relative paths removed (backup exists)
        public string Utc { get; set; } = "";
    }

    private string JournalPath => Path.Combine(settings.UpdateDir, "journal.json");

    /// <summary>Update zips waiting in the program folder or the updates folder, oldest version first.</summary>
    public IEnumerable<(string Path, Manifest Manifest)> Pending() {
        var found = new List<(string, Manifest)>();
        foreach (string folder in new[] { settings.Root, settings.UpdateDir }.Distinct(StringComparer.OrdinalIgnoreCase)) {
            if (!Directory.Exists(folder)) {
                continue;
            }
            foreach (string zip in Directory.GetFiles(folder, "*.zip")) {
                if (ReadManifest(zip) is { } manifest) {
                    found.Add((zip, manifest));
                }
            }
        }
        return found.OrderBy(entry => ParseVersion(entry.Item2.Version));
    }

    /// <summary>Installs every applicable pending update in version order. Throws with a player-readable message.</summary>
    public List<Result> InstallPending(Action<string>? status = null) {
        var results = new List<Result>();
        CleanUpOldLauncher();
        foreach ((string zip, Manifest manifest) in Pending()) {
            Version current = ParseVersion(settings.Version), target = ParseVersion(manifest.Version);
            if (target == current) {
                Archive(zip, "skipped"); // repeated: already installed
                EventLog.Write("update", $"skipped {Path.GetFileName(zip)}: version {manifest.Version} already installed");
                continue;
            }
            if (target < current) {
                Archive(zip, "skipped");
                EventLog.Write("update", $"refused downgrade {Path.GetFileName(zip)} ({manifest.Version} < {settings.Version})");
                throw new InvalidOperationException(
                    $"\"{Path.GetFileName(zip)}\" is version {manifest.Version}, older than the installed version {settings.Version}. It was not installed.");
            }
            if (current < ParseVersion(manifest.MinVersion)) {
                EventLog.Write("update", $"refused {Path.GetFileName(zip)}: needs {manifest.MinVersion}, installed {settings.Version}");
                throw new InvalidOperationException(
                    $"The update \"{Path.GetFileName(zip)}\" (version {manifest.Version}) needs version {manifest.MinVersion} or newer, " +
                    $"but version {settings.Version} is installed.\n\nPlease download the full version {manifest.Version} instead.");
            }
            status?.Invoke($"Installing update {manifest.Version}...");
            results.Add(Install(zip, manifest));
            settings.Version = manifest.Version;
        }
        return results;
    }

    private Result Install(string zip, Manifest manifest) {
        EventLog.Write("update", $"installing {Path.GetFileName(zip)} {settings.Version} -> {manifest.Version}");
        string staging = Path.Combine(settings.UpdateDir, $"staging-{manifest.Version}");
        string backupDir = Path.Combine(settings.UpdateDir, $"backup-before-{manifest.Version}-{DateTime.Now:yyyyMMdd-HHmmss}");
        if (Directory.Exists(staging)) {
            Directory.Delete(staging, true);
        }

        // 1. Checks that need no changes.
        PreflightProcesses();
        List<(string Relative, long Length)> files;
        Dictionary<string, string> hashes;
        using (ZipArchive archive = ZipFile.OpenRead(zip)) {
            hashes = ReadHashes(archive) ?? throw new InvalidOperationException(
                "This update has no file list (files.json), so it cannot be checked. It was not installed; download it again from the release page.");
            files = [];
            foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => entry.Name.Length > 0 && entry.FullName is not ManifestName)) {
                CheckEntry(entry);
                files.Add((entry.FullName.Replace('\\', '/'), entry.Length));
            }
            foreach (string path in manifest.Delete ?? []) {
                CheckAllowed(path);
            }
            PreflightSpace(files.Sum(file => file.Length));
            PreflightLocks(files.Select(file => file.Relative).Concat(manifest.Delete ?? []));

            // 2. Stage and verify.
            foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => entry.Name.Length > 0 && entry.FullName is not ManifestName)) {
                string relative = entry.FullName.Replace('\\', '/');
                string staged = Contained(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                entry.ExtractToFile(staged, true);
                if (relative != HashesName && (!hashes.TryGetValue(relative, out string? expected) || !string.Equals(expected, Hash(staged), StringComparison.OrdinalIgnoreCase))) {
                    Directory.Delete(staging, true);
                    EventLog.Write("update", $"rejected {Path.GetFileName(zip)}: hash mismatch {relative}");
                    throw new InvalidOperationException(
                        $"The update file is damaged ({relative} does not match its checksum). Nothing was changed; download the update again.");
                }
            }
        }

        // 3. Validated save backup.
        Directory.CreateDirectory(backupDir);
        string? saveBackup = null;
        if (File.Exists(settings.SaveFile)) {
            saveBackup = Path.Combine(backupDir, "SaveData", "game-server.db");
            Directory.CreateDirectory(Path.GetDirectoryName(saveBackup)!);
            if (SaveBackup.Create(settings.SaveFile, saveBackup) is { } problem) {
                Directory.Delete(staging, true);
                throw new InvalidOperationException($"Your save could not be backed up safely ({problem}), so the update was not installed.");
            }
        }

        // 4. Apply with a journal.
        var journal = new Journal {
            Version = manifest.Version,
            PreviousVersion = settings.Version,
            State = "applying",
            BackupDir = backupDir,
            SaveBackup = saveBackup,
            Utc = DateTime.UtcNow.ToString("O"),
        };
        WriteJournal(journal);
        bool launcherReplaced = false;
        try {
            foreach ((string relative, long _) in files) {
                string target = Contained(settings.Root, relative);
                if (File.Exists(target)) {
                    Copy(target, Contained(backupDir, relative));
                    journal.Replaced.Add(relative);
                } else {
                    journal.Added.Add(relative);
                }
                WriteJournal(journal);
                if (string.Equals(target, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) {
                    // A running exe cannot be overwritten, but it can be renamed out of the way.
                    File.Move(target, target + ".old", true);
                    launcherReplaced = true;
                }
                Copy(Contained(staging, relative), target);
            }
            foreach (string relative in manifest.Delete ?? []) {
                string target = Contained(settings.Root, relative);
                if (File.Exists(target)) {
                    Copy(target, Contained(backupDir, relative));
                    journal.Deleted.Add(relative);
                    WriteJournal(journal);
                    File.Delete(target);
                }
            }
            File.WriteAllText(Path.Combine(settings.Root, "VERSION"), manifest.Version);
            journal.State = "installed";
            WriteJournal(journal);
        } catch (Exception ex) {
            RestoreProgramFiles(journal);
            journal.State = "rolledback";
            WriteJournal(journal);
            EventLog.Write("update", $"FAILED {manifest.Version}, undone: {ex.Message}");
            throw new InvalidOperationException($"The update to version {manifest.Version} could not be installed and was undone.\n\n{ex.Message}", ex);
        } finally {
            if (Directory.Exists(staging)) {
                Directory.Delete(staging, true);
            }
        }

        Archive(zip, "installed");
        EventLog.Write("update", $"installed {manifest.Version} (backup {Path.GetFileName(backupDir)})");
        return new Result(manifest.Version, manifest.Notes, launcherReplaced);
    }

    /// <summary>An update interrupted mid-way (crash, power loss) is undone on the next start. Returns a message, or null.</summary>
    public string? RecoverInterrupted() {
        CleanUpOldLauncher();
        foreach (string staging in Directory.Exists(settings.UpdateDir) ? Directory.GetDirectories(settings.UpdateDir, "staging-*") : []) {
            try {
                Directory.Delete(staging, true);
            } catch (Exception) {
                // locked; next time
            }
        }
        Journal? journal = ReadJournal();
        if (journal is not { State: "applying" }) {
            return null;
        }
        RestoreProgramFiles(journal);
        journal.State = "rolledback";
        WriteJournal(journal);
        settings.Version = journal.PreviousVersion;
        EventLog.Write("recovery", $"interrupted update to {journal.Version} undone; back on {journal.PreviousVersion}");
        return $"The update to version {journal.Version} was interrupted last time and has been undone. You are on version {journal.PreviousVersion}; " +
               "your save was not changed. Install the update again.";
    }

    /// <summary>The last installed update while it has not yet started successfully, else null.</summary>
    public Journal? UnverifiedUpdate() => ReadJournal() is { State: "installed", Verified: false } journal ? journal : null;

    public void MarkVerified() {
        if (UnverifiedUpdate() is { } journal) {
            journal.Verified = true;
            WriteJournal(journal);
            EventLog.Write("update", $"version {journal.Version} verified (started and saved)");
        }
    }

    /// <summary>Restores the program files and the save from before the last update. Servers must be stopped.</summary>
    public string RollBackLastUpdate() {
        Journal journal = ReadJournal() ?? throw new InvalidOperationException("There is no update to undo.");
        RestoreProgramFiles(journal);
        string saveNote = "";
        if (journal.SaveBackup != null && File.Exists(journal.SaveBackup)) {
            if (SaveBackup.Check(journal.SaveBackup) is { } problem) {
                saveNote = $" The save backup is damaged ({problem}); your current save was kept.";
            } else {
                foreach (string side in new[] { "-wal", "-shm" }) {
                    File.Delete(settings.SaveFile + side);
                }
                File.Copy(journal.SaveBackup, settings.SaveFile, true);
                saveNote = " Your save from before the update was restored.";
            }
        }
        journal.State = "rolledback";
        WriteJournal(journal);
        settings.Version = journal.PreviousVersion;
        EventLog.Write("recovery", $"rolled back {journal.Version} -> {journal.PreviousVersion}.{saveNote}");
        return $"Went back to version {journal.PreviousVersion}.{saveNote}";
    }

    private void RestoreProgramFiles(Journal journal) {
        foreach (string relative in journal.Replaced.Concat(journal.Deleted)) {
            try {
                string backup = Contained(journal.BackupDir, relative), target = Contained(settings.Root, relative);
                if (File.Exists(backup)) {
                    if (string.Equals(target, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) {
                        File.Move(target, target + ".old", true);
                    }
                    Copy(backup, target);
                }
            } catch (Exception ex) {
                EventLog.Write("recovery", $"could not restore {relative}: {ex.Message}");
            }
        }
        foreach (string relative in journal.Added) {
            try {
                string target = Contained(settings.Root, relative);
                if (File.Exists(target) && !string.Equals(target, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) {
                    File.Delete(target);
                }
            } catch (Exception ex) {
                EventLog.Write("recovery", $"could not remove {relative}: {ex.Message}");
            }
        }
        File.WriteAllText(Path.Combine(settings.Root, "VERSION"), journal.PreviousVersion);
    }

    #region Checks
    private void PreflightProcesses() {
        if (Process.GetProcessesByName("MapleStory2").Length > 0) {
            throw new InvalidOperationException("Close MapleStory 2 before installing the update.");
        }
        if (PortGuard.Listeners().Any(listener => PortGuard.IsOwnServer(listener, settings))) {
            throw new InvalidOperationException("The game servers are still running. Close the game, wait a moment and try again.");
        }
    }

    /// <summary>Staging + backups need about twice the update's size on each drive.</summary>
    private void PreflightSpace(long bytes) {
        long needed = bytes * 2 + 64L * 1024 * 1024 + (File.Exists(settings.SaveFile) ? new FileInfo(settings.SaveFile).Length : 0);
        foreach (string folder in new[] { settings.Root, settings.DataRoot }) {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!);
            if (drive.AvailableFreeSpace < needed) {
                throw new InvalidOperationException(
                    $"Not enough free disk space on {drive.Name} to install the update: {needed / 1048576} MB needed, {drive.AvailableFreeSpace / 1048576} MB free. Nothing was changed.");
            }
        }
    }

    private void PreflightLocks(IEnumerable<string> relatives) {
        foreach (string relative in relatives) {
            string target = Contained(settings.Root, relative);
            if (!File.Exists(target) || string.Equals(target, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            try {
                using var _ = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            } catch (IOException) {
                throw new InvalidOperationException($"A file the update must replace is in use: {relative}\n\nClose other programs using it and try again. Nothing was changed.");
            } catch (UnauthorizedAccessException) {
                throw new InvalidOperationException($"No permission to replace {relative}. Nothing was changed.");
            }
        }
    }

    private void CheckEntry(ZipArchiveEntry entry) {
        CheckAllowed(entry.FullName);
        // Unix file type bits in the high 16 bits of the external attributes: 0xA000 = symbolic link.
        if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) {
            throw new InvalidOperationException($"The update contains a link ({entry.FullName}), which is not allowed. Nothing was changed.");
        }
    }

    private static void CheckAllowed(string relative) {
        string normalized = relative.Replace('\\', '/').TrimStart('/');
        if (normalized.Split('/').Any(part => part is ".." or "") || Path.IsPathRooted(relative) || normalized.Contains(':')
            || Protected.Any(prefix => normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) {
            throw new InvalidOperationException($"The update tried to change a protected or invalid path: {relative}. Nothing was changed.");
        }
    }

    /// <summary>The full path of a relative path inside a folder; refuses anything that would leave the folder.</summary>
    private static string Contained(string folder, string relative) {
        string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException($"Path outside the program folder refused: {relative}");
        }
        return full;
    }
    #endregion

    #region Helpers
    private static void Copy(string source, string target) {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, true);
    }

    private void WriteJournal(Journal journal) {
        Directory.CreateDirectory(settings.UpdateDir);
        string temp = JournalPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(journal, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, JournalPath, true);
    }

    private Journal? ReadJournal() {
        try {
            return File.Exists(JournalPath) ? JsonSerializer.Deserialize<Journal>(File.ReadAllText(JournalPath)) : null;
        } catch (Exception) {
            return null;
        }
    }

    private void Archive(string zip, string folder) {
        try {
            string target = Path.Combine(settings.UpdateDir, folder);
            Directory.CreateDirectory(target);
            File.Move(zip, Path.Combine(target, Path.GetFileName(zip)), true);
        } catch (Exception) {
            // leave it; it is skipped next time because its version is no longer newer
        }
    }

    private static void CleanUpOldLauncher() {
        try {
            if (Environment.ProcessPath is { } exe && File.Exists(exe + ".old")) {
                File.Delete(exe + ".old");
            }
        } catch (Exception) {
            // still in use; next start
        }
    }

    private static Manifest? ReadManifest(string zip) {
        try {
            using ZipArchive archive = ZipFile.OpenRead(zip);
            ZipArchiveEntry? entry = archive.GetEntry(ManifestName);
            if (entry == null) {
                return null;
            }
            using Stream stream = entry.Open();
            Manifest? manifest = JsonSerializer.Deserialize<Manifest>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return manifest is { Version.Length: > 0, MinVersion.Length: > 0 } ? manifest : null;
        } catch (Exception) {
            return null; // not an update zip (or damaged beyond reading; a damaged one is reported at install)
        }
    }

    private static Dictionary<string, string>? ReadHashes(ZipArchive archive) {
        ZipArchiveEntry? entry = archive.GetEntry(HashesName);
        if (entry == null) {
            return null;
        }
        using Stream stream = entry.Open();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
    }

    private static string Hash(string path) {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static Version ParseVersion(string text) {
        string core = new string(text.Trim().TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        return System.Version.TryParse(core.Contains('.') ? core : core + ".0", out Version? version) ? version : new Version(0, 0);
    }
    #endregion
}
