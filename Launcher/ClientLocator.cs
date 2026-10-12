using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MapleStory2Offline;

/// <summary>
/// Finds the MapleStory 2 client (Steam libraries, or a folder the player picks once) and installs the client patch.
/// </summary>
internal static class ClientLocator {
    public const string SteamAppId = "560380";
    public const string ClientDownloadPage = "https://github.com/shuabritze/mushroom-launcher/releases";
    private const string GameExe = "MapleStory2.exe";
    private const string Proxy = "NxCharacter64.dll";
    private const string Backup = "NxCharacter64.dll.bak";

    /// <summary>The client's x64 folder, or null. Order: the saved choice, then every Steam library.</summary>
    public static string? Find(string? saved) {
        if (Normalize(saved) is { } chosen) {
            return chosen;
        }
        foreach (string library in SteamLibraries()) {
            if (Normalize(Path.Combine(library, "steamapps", "common", "MapleStory 2")) is { } found) {
                return found;
            }
        }
        return null;
    }

    /// <summary>Accepts the install folder, its x64 folder or the Data folder; returns the x64 folder.</summary>
    public static string? Normalize(string? folder) {
        if (string.IsNullOrWhiteSpace(folder)) {
            return null;
        }
        foreach (string candidate in new[] { folder, Path.Combine(folder, "x64"), Path.Combine(folder, "..", "x64") }) {
            string full = Path.GetFullPath(candidate);
            if (File.Exists(Path.Combine(full, GameExe)) && Directory.Exists(Path.Combine(full, "..", "Data"))) {
                return full;
            }
        }
        return null;
    }

    /// <summary>
    /// Asks the player where the game is. Returns the x64 folder, or null when they cancel.
    /// </summary>
    public static string? Ask(IWin32Window? owner) {
        while (true) {
            var browse = new TaskDialogCommandLinkButton("Choose the game folder...", "The folder that contains MapleStory2.exe (or its x64 folder).");
            var download = new TaskDialogCommandLinkButton("Get the game client",
                "Opens the community downloader (Mushroom Launcher). In it, click \"Install client\", then come back and choose that folder.");
            var steam = new TaskDialogCommandLinkButton("Install it with Steam",
                "Only works if MapleStory 2 is still in your Steam library.");
            var page = new TaskDialogPage {
                Caption = "MapleStory 2 (Offline)",
                Heading = "MapleStory 2 was not found",
                Text = "This offline edition needs the MapleStory 2 game client (about 14 GB).",
                Icon = TaskDialogIcon.Information,
                Buttons = { browse, download, steam, TaskDialogButton.Cancel },
                AllowCancel = true,
            };

            TaskDialogButton result = owner == null ? TaskDialog.ShowDialog(page) : TaskDialog.ShowDialog(owner, page);
            if (result == browse) {
                using var dialog = new FolderBrowserDialog {
                    Description = "Choose the MapleStory 2 folder (the one with MapleStory2.exe, or its x64 folder)",
                    UseDescriptionForTitle = true,
                };
                if (dialog.ShowDialog(owner) == DialogResult.OK) {
                    if (Normalize(dialog.SelectedPath) is { } found) {
                        return found;
                    }
                    MessageBox.Show(owner, "MapleStory2.exe was not found in that folder.", "MapleStory 2 (Offline)", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            } else if (result == download) {
                Open(ClientDownloadPage);
            } else if (result == steam) {
                Open($"steam://install/{SteamAppId}");
            } else {
                return null;
            }
        }
    }

    /// <summary>
    /// Installs or refreshes the client patch: keeps the original proxy DLL as .bak, copies the patched DLLs.
    /// Returns false when Windows refused access (the caller retries elevated).
    /// </summary>
    public static bool InstallPatch(string clientDir, string patchDir) {
        try {
            string proxy = Path.Combine(clientDir, Proxy), backup = Path.Combine(clientDir, Backup);
            string patchedProxy = Path.Combine(patchDir, Proxy);
            if (!File.Exists(backup) && File.Exists(proxy) && Hash(proxy) != Hash(patchedProxy)) {
                File.Copy(proxy, backup); // the game's original, restored by uninstalling the patch
            }
            foreach (string dll in new[] { "AgarciumClient.dll", Proxy }) {
                string source = Path.Combine(patchDir, dll), target = Path.Combine(clientDir, dll);
                if (File.Exists(source) && (!File.Exists(target) || Hash(source) != Hash(target))) {
                    File.Copy(source, target, true);
                }
            }
            return true;
        } catch (UnauthorizedAccessException) {
            return false;
        }
    }

    /// <summary>Runs "--install-patch" as administrator (game folders under Program Files are write-protected).</summary>
    public static bool InstallPatchElevated(string clientDir) {
        try {
            using Process? process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) {
                UseShellExecute = true,
                Verb = "runas",
                ArgumentList = { "--install-patch", clientDir },
            });
            process?.WaitForExit();
            return process?.ExitCode == 0;
        } catch (System.ComponentModel.Win32Exception) {
            return false; // the player said no to the Windows prompt
        }
    }

    public static bool PatchInstalled(string clientDir, string patchDir) {
        return new[] { "AgarciumClient.dll", Proxy }.All(dll =>
            File.Exists(Path.Combine(clientDir, dll)) && Hash(Path.Combine(clientDir, dll)) == Hash(Path.Combine(patchDir, dll)));
    }

    private static IEnumerable<string> SteamLibraries() {
        var steamRoots = new List<string>();
        foreach ((RegistryKey hive, string key, string value) in new[] {
                     (Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                     (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                     (Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
                 }) {
            try {
                using RegistryKey? steam = hive.OpenSubKey(key);
                if (steam?.GetValue(value) is string path && Directory.Exists(path)) {
                    steamRoots.Add(Path.GetFullPath(path));
                }
            } catch (Exception) {
                // no access
            }
        }

        var libraries = new List<string>();
        foreach (string steamRoot in steamRoots.Distinct(StringComparer.OrdinalIgnoreCase)) {
            libraries.Add(steamRoot);
            string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) {
                continue;
            }
            foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"(?<path>[^\"]+)\"")) {
                libraries.Add(match.Groups["path"].Value.Replace(@"\\", @"\"));
            }
        }
        return libraries.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void Open(string target) {
        try {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        } catch (Exception) {
            MessageBox.Show($"Could not open:\n{target}", "MapleStory 2 (Offline)", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string Hash(string path) {
        if (!File.Exists(path)) {
            return "";
        }
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
