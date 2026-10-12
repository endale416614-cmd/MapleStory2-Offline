namespace MapleStory2Offline;

/// <summary>
/// Paths and login for the offline setup. Defaults assume the launcher sits in the offline folder; any value can be
/// overridden with "Key=Value" lines in launcher.ini next to the exe.
/// </summary>
internal sealed class Settings {
    public string Root { get; private init; } = "";
    public string ServerDir => Path.Combine(Root, "Maple2");
    public string DotnetDir => Path.Combine(Root, "dotnet");
    public string LogDir => Path.Combine(Root, "logs");
    public string PatchDir => Path.Combine(Root, "client-patch");
    /// <summary>Client data mods (XML replacements) loaded by the client patch, one folder per mod.</summary>
    public string ModsDir => Path.Combine(Root, "client-mods");
    public string SaveFile => Path.Combine(Root, "SaveData", "game-server.db");
    public string BackupDir => Path.Combine(Root, "SaveData", "backups");
    public int BackupsToKeep { get; private init; } = 10;
    /// <summary>Server build to run: Release (normal rules) or Debug (developer cheats).</summary>
    public string ServerConfiguration { get; private init; } = "Release";
    /// <summary>Play-testing aid: /testkit command + gear without level/job limits. Off for normal play.</summary>
    public bool TestKit { get; private init; }
    /// <summary>Logs every map-script state change and action to logs\game.log (quest/dungeon debugging).</summary>
    public bool TriggerTrace { get; private init; }
    /// <summary>In dungeons every item of every drop table drops (monsters, breakables, clear reward).</summary>
    public bool DungeonAllDrops { get; private init; }
    /// <summary>Dungeon clear rewards limited to 9999 per week/day instead of the original few.</summary>
    public bool UnlimitedDungeonClears { get; private init; }
    /// <summary>Grinding rates from offline-grind.json: scaled monster exp, more loot, upgrade-material drops.</summary>
    public bool GrindRates { get; private init; }
    /// <summary>Writes a plain-text report of every session to the desktop for bug reports (on unless DebugReports=false).</summary>
    public bool DebugReports { get; private init; } = true;

    public string ClientDir { get; private init; } = @"C:\Program Files (x86)\Steam\steamapps\common\MapleStory 2\x64";
    public string Username { get; private init; } = "offline";
    public string Password { get; private init; } = "offline";

    public static Settings Load() {
        string root = Path.GetFullPath(AppContext.BaseDirectory);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string ini = Path.Combine(root, "launcher.ini");
        if (File.Exists(ini)) {
            foreach (string line in File.ReadAllLines(ini)) {
                int index = line.IndexOf('=');
                if (index > 0 && !line.TrimStart().StartsWith('#')) {
                    values[line[..index].Trim()] = line[(index + 1)..].Trim();
                }
            }
        }

        var defaults = new Settings();
        return new Settings {
            Root = values.GetValueOrDefault("Root", root),
            ClientDir = values.GetValueOrDefault("ClientDir", defaults.ClientDir),
            Username = values.GetValueOrDefault("Username", defaults.Username),
            Password = values.GetValueOrDefault("Password", defaults.Password),
            ServerConfiguration = values.GetValueOrDefault("ServerConfiguration", defaults.ServerConfiguration),
            TestKit = bool.TryParse(values.GetValueOrDefault("TestKit"), out bool testKit) && testKit,
            TriggerTrace = bool.TryParse(values.GetValueOrDefault("TriggerTrace"), out bool trace) && trace,
            DungeonAllDrops = bool.TryParse(values.GetValueOrDefault("DungeonAllDrops"), out bool allDrops) && allDrops,
            UnlimitedDungeonClears = bool.TryParse(values.GetValueOrDefault("UnlimitedDungeonClears"), out bool clears) && clears,
            GrindRates = bool.TryParse(values.GetValueOrDefault("GrindRates"), out bool grind) && grind,
            DebugReports = !bool.TryParse(values.GetValueOrDefault("DebugReports"), out bool reports) || reports,
            BackupsToKeep = int.TryParse(values.GetValueOrDefault("BackupsToKeep"), out int keep) ? keep : defaults.BackupsToKeep,
        };
    }
}
