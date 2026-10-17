namespace MapleStory2Offline;

/// <summary>
/// launcher.ini files from before the game modes (1.1.0-1.1.8, and every Linux install, whose installer copied the example)
/// list the gameplay options as "=true". Read as overrides they would keep Legacy Mode adapted, so on the first start of
/// 1.1.9 those lines are commented out: under Adapted nothing changes ("true" is its default), Legacy then applies.
/// Lines set to false stay (a deliberate opt-out), and a file that names GameMode is never touched.
/// </summary>
internal static class LauncherIni {
    public static readonly string[] ModeOptions =
        ["GrindRates", "DungeonAllDrops", "UnlimitedDungeonClears", "ClassDropsOnly", "PetTaming", "DungeonEpicGear"];

    /// <summary>The lines with those options commented out, or null when nothing changes.</summary>
    public static List<string>? WithoutPreModeOptions(IReadOnlyList<string> lines) {
        var result = new List<string>(lines.Count);
        bool changed = false;
        foreach (string line in lines) {
            int index = line.IndexOf('=');
            if (index <= 0 || line.TrimStart().StartsWith('#')) {
                result.Add(line);
                continue;
            }
            string key = line[..index].Trim();
            if (key.Equals("GameMode", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            if (ModeOptions.Contains(key, StringComparer.OrdinalIgnoreCase) && bool.TryParse(line[(index + 1)..].Trim(), out bool on) && on) {
                result.Add($"#{line.Trim()}  (1.1.9: now set by the game mode you choose at start)");
                changed = true;
                continue;
            }
            result.Add(line);
        }
        return changed ? result : null;
    }

    /// <summary>Applies <see cref="WithoutPreModeOptions"/> to the file; a file that cannot be read or written is left as it is.</summary>
    public static void Migrate(string path) {
        try {
            if (File.Exists(path) && WithoutPreModeOptions(File.ReadAllLines(path)) is { } lines) {
                File.WriteAllLines(path, lines);
            }
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            // the options then still win over the mode, as in 1.1.8
        }
    }
}
