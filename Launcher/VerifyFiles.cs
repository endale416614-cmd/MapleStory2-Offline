using System.Diagnostics;

namespace MapleStory2Offline;

/// <summary>
/// Start menu "Verify Game Files" (--verify-files): the content pack's archives against pack.json. After one Yes it repairs
/// the ones that still hold the Global game (<see cref="ContentPack.Repair"/>); Steam's verify opens only when the player
/// answers Yes for the rest. The next Play applies the unified content again. Last, the program's own files against
/// files.json, as the diagnostics' install check sees them.
/// </summary>
internal static class VerifyFiles {
    private const string Title = "MapleStory 2 (Offline)";

    public static int Run(Settings settings) {
        int code = GameFiles(settings);
        string[] changed = Diagnostics.InstallCheck(settings).Split('\n')
            .Where(line => line.StartsWith("MISSING ") || line.StartsWith("MODIFIED ")).ToArray();
        if (changed.Length == 0) {
            return code;
        }
        MessageBox.Show($"These program files are missing or changed:\n{string.Join("\n", changed.Take(20))}" +
            (changed.Length > 20 ? $"\n... {changed.Length - 20} more" : "") +
            "\n\nRun MapleStory2-Offline-Setup.exe again to repair the program. It keeps your saves and settings.", Title,
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return 1;
    }

    private static int GameFiles(Settings settings) {
        if (Process.GetProcessesByName("MapleStory2").Length > 0) {
            MessageBox.Show("Close MapleStory 2 first.", Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
        if (ClientLocator.Find(settings.ClientDir) is not { } client) {
            MessageBox.Show("MapleStory 2 was not found. Start the game once from the launcher to find or install it.", Title,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
        settings.ClientDir = client;
        if (!File.Exists(Path.Combine(settings.ContentPackDir, ContentPack.FileName))) {
            MessageBox.Show("This installation has no content pack, so the launcher has no game files to check. Steam can verify them.",
                Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        try {
            string? recovered = ContentPack.RecoverInterrupted(settings.ClientRoot, settings.ContentPackDir);
            List<string> foreign = Foreign(settings);
            if (foreign.Count == 0) {
                MessageBox.Show((recovered == null ? "" : recovered + "\n\n") + "The game files fit the content pack. Nothing needs a repair.",
                    Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            if (MessageBox.Show($"These game files do not fit the content pack:\n{List(foreign)}\n\nRepair them? Only files that still hold the " +
                    "Global game are changed; Steam's verify can repair the rest.", Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) {
                return 1;
            }
            List<string>? left = ContentPack.Repair(settings.ClientRoot, settings.ContentPackDir);
            if (left == null) {
                // The game folder is write-protected: the same repair as administrator, then what it left.
                ContentPack.ApplyElevated(settings.ClientRoot, settings.ContentPackDir, "--repair-content-pack");
                left = Foreign(settings);
            }
            if (left.Count == 0) {
                MessageBox.Show("Repaired. Press Play: the launcher applies the unified content again.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            if (MessageBox.Show($"These game files need Steam's verify:\n{List(left)}\n\nOpen Steam to verify the game files now?", Title,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) {
                try {
                    Process.Start(new ProcessStartInfo($"steam://validate/{ClientLocator.SteamAppId}") { UseShellExecute = true });
                } catch (System.ComponentModel.Win32Exception) {
                    MessageBox.Show("Steam could not be opened. In Steam, right-click MapleStory 2, then Properties, Installed Files, " +
                        "Verify integrity of game files.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            return 1;
        } catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) {
            MessageBox.Show($"The game files could not be checked: {ex.Message}", Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
    }

    private static List<string> Foreign(Settings settings) => ContentPack.Load(settings.ContentPackDir).State(settings.ClientRoot)
        .Where(state => state.Status == ContentPack.Status.Foreign).Select(state => state.Reason).ToList();

    private static string List(IEnumerable<string> reasons) => string.Join("\n", reasons.Select(reason => "- " + reason));
}
