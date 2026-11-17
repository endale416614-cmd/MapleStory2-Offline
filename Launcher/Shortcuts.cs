namespace MapleStory2Offline;

/// <summary>The Start menu shortcuts of an installed copy. PA-05 (BT-234): an install updated by a zip keeps the shortcuts of the
/// Setup that made it, so the launcher adds the ones a later version brought (Verify Game Files, 1.2.3).</summary>
internal static class Shortcuts {
    /// <summary>File name, launcher arguments and description of each Start menu shortcut.</summary>
    public static (string Name, string Arguments, string Description)[] StartMenu(string product) => [
        ($"{product}.lnk", "", "Play MapleStory 2 offline"),
        ($"{product} - Export Diagnostics.lnk", "--export-diagnostics", "Save a diagnostics file on the desktop to send with a bug report"),
        ($"{product} - Report a Problem.lnk", "--report-problem", "Send the developer a problem report with your own words"),
        ($"{product} - Install Update.lnk", "--install-update", "Install a downloaded update zip"),
        ($"{product} - Verify Game Files.lnk", "--verify-files", "Check the game files the unified content needs and repair them"),
        ($"Uninstall {product}.lnk", "--uninstall", "Remove MapleStory 2 Offline"),
    ];

    /// <summary>The Start menu shortcuts the folder lacks; none when the folder itself is gone (the player removed it).</summary>
    public static List<(string Name, string Arguments, string Description)> Missing(string startMenuDir, string product) {
        if (!Directory.Exists(startMenuDir)) {
            return [];
        }
        return StartMenu(product).Where(shortcut => !File.Exists(Path.Combine(startMenuDir, shortcut.Name))).ToList();
    }
}
