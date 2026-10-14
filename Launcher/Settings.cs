namespace MapleStory2Offline;

/// <summary>
/// Paths and options for the offline setup. Values come from defaults.ini (shipped, replaced by updates) and then
/// launcher.ini (the player's own choices, never touched by updates), both "Key=Value" files next to the exe.
/// </summary>
internal sealed class Settings {
    public const string DefaultsFile = "defaults.ini";
    public const string UserFile = "launcher.ini";

    /// <summary>Program folder (the exe, servers, game data). Replaced by updates.</summary>
    public string Root { get; private init; } = "";
    /// <summary>
    /// Player data folder (save, logs, reports, launcher.ini, update backups). Installed copies use
    /// %LOCALAPPDATA%\MapleStory 2 Offline (named in install.json); portable/dev copies use the program folder.
    /// </summary>
    public string DataRoot { get; private init; } = "";
    public bool Installed { get; private init; }
    public string InstallId { get; private init; } = "";
    public string Version { get; set; } = "dev";
    public string ServerDir => Path.Combine(Root, "Maple2");
    public string DotnetDir => Path.Combine(Root, "dotnet");
    public string LogDir => Path.Combine(DataRoot, "logs");
    public string ReportDir => Path.Combine(DataRoot, "reports");
    public string PatchDir => Path.Combine(Root, "client-patch");
    /// <summary>Client data mods (XML replacements) loaded by the client patch, one folder per mod.</summary>
    public string ModsDir => Path.Combine(Root, "client-mods");
    public string SaveDir => Path.Combine(DataRoot, "SaveData");
    public string SaveFile => Path.Combine(SaveDir, "game-server.db");
    public string BackupDir => Path.Combine(SaveDir, "backups");
    /// <summary>Game data built from the client; in releases it lives apart from the save so updates can replace it.</summary>
    public string GameDataDir => Path.Combine(Root, "GameData");
    public string UpdateDir => Path.Combine(DataRoot, "updates");
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
    /// <summary>Monsters and dungeon rewards only drop gear the player's class can wear.</summary>
    public bool ClassDropsOnly { get; private init; }
    /// <summary>Wild pets 3x as often, pet candy captures on a full gauge, taming items usable from level 10.</summary>
    public bool PetTaming { get; private init; }
    /// <summary>Weapons, armor and accessories dropped in dungeons are at least Epic where the item has Epic stats.</summary>
    public bool DungeonEpicGear { get; private init; }
    /// <summary>Mail stays in the mailbox until deleted instead of expiring after 30 days.</summary>
    public bool MailNeverExpires { get; private init; }
    /// <summary>Chat commands /godmode, /killall, /unstuck and /freebies for getting past stuck quests (on unless CheatCommands=false).</summary>
    public bool CheatCommands { get; private init; } = true;
    /// <summary>
    /// Online update check when Play is pressed: "ask" (offer the newer version), "always" (install it without asking)
    /// or "off" (only manual updates). Skipped silently without internet.
    /// </summary>
    public string AutoUpdate { get; private init; } = "off";
    /// <summary>Writes a plain-text report of every session to the desktop for bug reports (on unless DebugReports=false).</summary>
    public bool DebugReports { get; private init; } = true;

    /// <summary>Folder of MapleStory2.exe (the client's x64 folder); found automatically when not set.</summary>
    public string ClientDir { get; set; } = "";
    public string Username { get; private init; } = "offline";
    public string Password { get; private init; } = "offline";

    public static Settings Load() {
        string root = Path.GetFullPath(AppContext.BaseDirectory);
        (string dataRoot, bool installed, string installId) = InstallInfo.Read(root);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ReadInto(values, Path.Combine(root, DefaultsFile));
        ReadInto(values, Path.Combine(dataRoot, UserFile));
        root = values.GetValueOrDefault("Root", root);

        string versionFile = Path.Combine(root, "VERSION");
        var defaults = new Settings();
        return new Settings {
            Root = root,
            DataRoot = dataRoot,
            Installed = installed,
            InstallId = installId,
            Version = File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : defaults.Version,
            ClientDir = values.GetValueOrDefault("ClientDir", defaults.ClientDir),
            Username = values.GetValueOrDefault("Username", defaults.Username),
            Password = values.GetValueOrDefault("Password", defaults.Password),
            ServerConfiguration = values.GetValueOrDefault("ServerConfiguration", defaults.ServerConfiguration),
            TestKit = Flag(values, "TestKit", false),
            TriggerTrace = Flag(values, "TriggerTrace", false),
            DungeonAllDrops = Flag(values, "DungeonAllDrops", false),
            UnlimitedDungeonClears = Flag(values, "UnlimitedDungeonClears", false),
            GrindRates = Flag(values, "GrindRates", false),
            MailNeverExpires = Flag(values, "MailNeverExpires", false),
            CheatCommands = Flag(values, "CheatCommands", true),
            ClassDropsOnly = Flag(values, "ClassDropsOnly", false),
            PetTaming = Flag(values, "PetTaming", false),
            DungeonEpicGear = Flag(values, "DungeonEpicGear", false),
            AutoUpdate = (values.GetValueOrDefault("AutoUpdate") ?? defaults.AutoUpdate).Trim().ToLowerInvariant() switch {
                "ask" or "true" or "yes" or "on" => "ask",
                "always" or "auto" or "silent" => "always",
                _ => "off",
            },
            DebugReports = Flag(values, "DebugReports", true),
            BackupsToKeep = int.TryParse(values.GetValueOrDefault("BackupsToKeep"), out int keep) ? keep : defaults.BackupsToKeep,
        };
    }

    /// <summary>Stores one choice in the player's launcher.ini (adds or replaces the line, keeps the rest).</summary>
    public void SaveUserValue(string key, string value) {
        Directory.CreateDirectory(DataRoot);
        string path = Path.Combine(DataRoot, UserFile);
        List<string> lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : ["# Your own settings. They override defaults.ini and are kept by updates."];
        int index = lines.FindIndex(line => line.TrimStart().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
        if (index >= 0) {
            lines[index] = $"{key}={value}";
        } else {
            lines.Add($"{key}={value}");
        }
        File.WriteAllLines(path, lines);
    }

    private static void ReadInto(Dictionary<string, string> values, string path) {
        if (!File.Exists(path)) {
            return;
        }
        foreach (string line in File.ReadAllLines(path)) {
            int index = line.IndexOf('=');
            if (index > 0 && !line.TrimStart().StartsWith('#')) {
                values[line[..index].Trim()] = line[(index + 1)..].Trim();
            }
        }
    }

    private static bool Flag(Dictionary<string, string> values, string key, bool fallback) =>
        bool.TryParse(values.GetValueOrDefault(key), out bool value) ? value : fallback;
}
