using System.IO.Compression;
using System.Text.Json;

namespace MapleStory2Offline;

/// <summary>
/// Manual updates: the player copies an update zip (as downloaded, unopened) into the game folder or its "updates"
/// folder. On the next start the launcher checks it fits the installed version, backs up every file it replaces,
/// installs program files only (never the save, logs, reports or launcher.ini) and rolls back if anything fails.
/// An update zip holds the changed files at their folder paths plus update.json:
/// { "version": "1.0.2", "minVersion": "1.0.0", "delete": ["old/file.dll"], "notes": "..." }
/// minVersion is the oldest installed version it applies to (cumulative patches use the last full release).
/// </summary>
internal sealed class UpdateInstaller {
    private const string ManifestName = "update.json";
    private static readonly string[] Protected = ["SaveData/", "logs/", "reports/", "updates/", "launcher.ini"];

    private readonly Settings settings;

    public UpdateInstaller(Settings settings) {
        this.settings = settings;
    }

    public sealed record Manifest(string Version, string MinVersion, string[]? Delete, string? Notes);
    public sealed record Result(string Version, string? Notes, bool LauncherReplaced);

    /// <summary>Update zips waiting in the game folder or its updates folder, oldest version first.</summary>
    public IEnumerable<(string Path, Manifest Manifest)> Pending() {
        var found = new List<(string, Manifest)>();
        foreach (string folder in new[] { settings.Root, settings.UpdateDir }) {
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
            if (target <= current) {
                Archive(zip, "skipped"); // already installed or older
                continue;
            }
            if (current < ParseVersion(manifest.MinVersion)) {
                throw new InvalidOperationException(
                    $"The update \"{Path.GetFileName(zip)}\" (version {manifest.Version}) needs version {manifest.MinVersion} or newer, " +
                    $"but version {settings.Version} is installed.\n\nPlease download the full version {manifest.Version} instead.");
            }
            status?.Invoke($"Installing update {manifest.Version}...");
            results.Add(Install(zip, manifest));
            // Later zips are checked against the version just installed.
            current = target;
            File.WriteAllText(Path.Combine(settings.Root, "VERSION"), manifest.Version);
            settings.Version = manifest.Version;
        }
        return results;
    }

    private Result Install(string zip, Manifest manifest) {
        string backupDir = Path.Combine(settings.UpdateDir, $"backup-before-{manifest.Version}-{DateTime.Now:yyyyMMdd-HHmmss}");
        var replaced = new List<(string Target, string? Backup)>();
        bool launcherReplaced = false;
        try {
            using ZipArchive archive = ZipFile.OpenRead(zip);
            var entries = archive.Entries.Where(entry => entry.Name.Length > 0 && entry.FullName != ManifestName).ToList();
            foreach (ZipArchiveEntry entry in entries) {
                CheckAllowed(entry.FullName);
            }
            foreach (string path in manifest.Delete ?? []) {
                CheckAllowed(path);
            }

            foreach (ZipArchiveEntry entry in entries) {
                string target = FullPath(entry.FullName);
                string? backup = BackUp(target, backupDir, entry.FullName);
                if (string.Equals(target, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) {
                    // A running exe cannot be overwritten, but it can be renamed out of the way.
                    File.Move(target, target + ".old", true);
                    launcherReplaced = true;
                }
                replaced.Add((target, backup));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, true);
            }
            foreach (string path in manifest.Delete ?? []) {
                string target = FullPath(path);
                if (File.Exists(target)) {
                    replaced.Add((target, BackUp(target, backupDir, path)));
                    File.Delete(target);
                }
            }
        } catch (Exception ex) {
            RollBack(replaced, launcherReplaced);
            throw new InvalidOperationException($"The update to version {manifest.Version} could not be installed and was undone.\n\n{ex.Message}", ex);
        }

        Archive(zip, "installed");
        return new Result(manifest.Version, manifest.Notes, launcherReplaced);
    }

    private void RollBack(List<(string Target, string? Backup)> replaced, bool launcherReplaced) {
        foreach ((string target, string? backup) in Enumerable.Reverse(replaced)) {
            try {
                if (backup != null) {
                    File.Copy(backup, target, true);
                } else if (File.Exists(target) && !string.Equals(target, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) {
                    File.Delete(target); // a file the update added
                }
            } catch (Exception) {
                // keep restoring the rest
            }
        }
        if (launcherReplaced && Environment.ProcessPath is { } exe && File.Exists(exe + ".old")) {
            try {
                File.Move(exe + ".old", exe, true);
            } catch (Exception) {
                // the backup copy is still in the updates folder
            }
        }
    }

    private static string? BackUp(string target, string backupDir, string relative) {
        if (!File.Exists(target)) {
            return null;
        }
        string backup = Path.Combine(backupDir, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        File.Copy(target, backup, true);
        return backup;
    }

    private void CheckAllowed(string relative) {
        string normalized = relative.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("..") || Path.IsPathRooted(relative)
            || Protected.Any(prefix => normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) {
            throw new InvalidOperationException($"The update tried to change a protected file: {relative}");
        }
    }

    private string FullPath(string relative) => Path.GetFullPath(Path.Combine(settings.Root, relative.Replace('/', Path.DirectorySeparatorChar)));

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
            return null; // not an update zip
        }
    }

    public static Version ParseVersion(string text) {
        string core = new string(text.Trim().TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        return System.Version.TryParse(core.Contains('.') ? core : core + ".0", out Version? version) ? version : new Version(0, 0);
    }
}
