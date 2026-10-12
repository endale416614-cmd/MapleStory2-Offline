using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace MapleStory2Offline;

/// <summary>
/// Automatic debug report for players without technical knowledge: after every play session the launcher writes one
/// plain-text file to "MapleStory 2 Offline Reports" on the desktop (newest few kept) that the player can send as-is.
/// It contains a timeline, the problems found in the server logs (grouped), the character, the PC, the install and
/// the ends of all logs. Personal details (Windows user name, PC name, machine id) are removed.
/// </summary>
internal sealed class SessionReport {
    public const string FolderName = "MapleStory 2 Offline Reports";
    private const int ReportsToKeep = 15;
    private const int LogTailLines = 150;
    private static readonly string[] ServerNames = ["Game", "World", "Login", "Web"];
    private static readonly Regex LogLine = new(@"^(?<time>\d\d:\d\d:\d\d\.\d{3})\s+(?<source>\S*)\s*\[(?<level>[A-Z]{3})\]\s*<\d+>\s*(?<message>.*)$");
    private static readonly Regex Numbers = new(@"\d+(\.\d+)?");

    private readonly Settings settings;
    private readonly DateTime started = DateTime.Now;
    private readonly List<string> timeline = [];
    private readonly List<string> problems = [];
    private int? gameExitCode;

    public SessionReport(Settings settings) {
        this.settings = settings;
        Step($"Launcher started (version {settings.Version})");
    }

    public static string Version => typeof(SessionReport).Assembly.GetName().Version?.ToString() ?? "?";

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), FolderName);

    public bool HasProblems => problems.Count > 0;

    public void Step(string text) {
        lock (timeline) {
            timeline.Add($"{DateTime.Now:HH:mm:ss}  {text}");
        }
    }

    public void Problem(string text) {
        Step("PROBLEM: " + text);
        lock (problems) {
            problems.Add(text);
        }
    }

    public void GameExited(int? exitCode) {
        gameExitCode = exitCode;
        if (exitCode is null) {
            Step("Game closed");
        } else if (exitCode == 0) {
            Step("Game closed normally");
        } else {
            Problem($"The game closed unexpectedly (exit code {exitCode} = 0x{exitCode:X8}{DescribeExitCode(exitCode.Value)})");
        }
    }

    /// <summary>Writes the report; never throws. Returns the file path, or null if writing failed.</summary>
    public string? Write() {
        try {
            string text = BuildText();
            string name = $"Report {started:yyyy-MM-dd HH-mm-ss}{(HasProblems ? " - PROBLEM" : "")}.txt";
            string? saved = null;
            foreach (string folder in new[] { Folder, settings.ReportDir }) {
                try {
                    Directory.CreateDirectory(folder);
                    string path = Path.Combine(folder, name);
                    File.WriteAllText(path, text, new UTF8Encoding(true));
                    saved ??= path;
                    foreach (FileInfo old in new DirectoryInfo(folder).GetFiles("Report *.txt")
                                 .OrderByDescending(file => file.LastWriteTimeUtc).Skip(ReportsToKeep)) {
                        old.Delete();
                    }
                } catch (Exception) {
                    // try the next location (e.g. a redirected or read-only desktop)
                }
            }
            return saved;
        } catch (Exception ex) {
            // Last resort: leave the reason next to the launcher so the failure itself can be reported.
            try {
                Directory.CreateDirectory(settings.DataRoot);
                File.WriteAllText(Path.Combine(settings.DataRoot, "report-error.txt"), ex.ToString());
            } catch (Exception) {
                // nothing more we can do
            }
            return null;
        }
    }

    /// <summary>The report text, redacted (also used by the diagnostics export).</summary>
    public string BuildText() => Redact(Build());

    private string Build() {
        DateTime ended = DateTime.Now;
        List<LogProblem> logProblems = ServerNames.SelectMany(ScanServerLog).ToList();
        int problemCount = problems.Count + logProblems.Count(problem => problem.Level is "ERR" or "FTL");

        var text = new StringBuilder();
        text.AppendLine("MapleStory 2 (Offline) - play session report");
        text.AppendLine("=============================================");
        text.AppendLine($"If something went wrong, please post this whole file on our Discord: {Community.Discord}");
        text.AppendLine("Your Windows user name and PC name have been removed from it.");
        text.AppendLine();
        text.AppendLine($"Session : {started:yyyy-MM-dd HH:mm} - {ended:HH:mm} ({FormatDuration(ended - started)})");
        text.AppendLine($"Result  : {(problemCount == 0 ? "no problems detected" : $"{problemCount} problem(s) detected - see below")}");
        text.AppendLine($"Version : {settings.Version} (launcher build {Version}, servers {settings.ServerConfiguration})");
        text.AppendLine();

        Section(text, "What happened");
        lock (timeline) {
            foreach (string step in timeline) {
                text.AppendLine(step);
            }
        }

        Section(text, "Problems");
        if (problems.Count == 0 && logProblems.Count == 0) {
            text.AppendLine("None.");
        }
        foreach (string problem in problems) {
            text.AppendLine("* " + problem);
        }
        foreach (LogProblem problem in logProblems.OrderBy(p => p.Level switch { "FTL" => 0, "ERR" => 1, _ => 2 }).ThenByDescending(p => p.Count)) {
            text.AppendLine($"* [{problem.Server} {problem.Level}] x{problem.Count}, first at {problem.FirstTime}: {problem.Sample}");
            foreach (string detail in problem.Details) {
                text.AppendLine("      " + detail);
            }
        }

        Section(text, "Character");
        text.AppendLine(DescribeCharacter());

        Section(text, "Computer");
        AppendComputer(text);

        Section(text, "Game install");
        AppendInstall(text);

        foreach (string server in ServerNames) {
            Section(text, $"Server log: {server} (last {LogTailLines} lines)");
            text.AppendLine(Tail(Path.Combine(settings.LogDir, $"{server.ToLowerInvariant()}.log"), LogTailLines));
        }

        Section(text, "Game client log (last 80 lines)");
        string clientRoot = ClientLocator.Find(settings.ClientDir) ?? settings.ClientDir;
        string? clientLog = NewestFile(Path.Combine(Path.GetDirectoryName(clientRoot) ?? clientRoot, "ms2log"), "MS2Log*.txt");
        text.AppendLine(clientLog == null ? "(none)" : $"{Path.GetFileName(clientLog)}:\n{Tail(clientLog, 80)}");

        Section(text, "Offline guard log (last 20 lines)");
        text.AppendLine(Tail(Path.Combine(clientRoot, "offline_guard.log"), 20));

        text.AppendLine();
        text.AppendLine("-- end of report --");
        return text.ToString();
    }

    #region Server logs
    private sealed record LogProblem(string Server, string Level, string Key, string FirstTime, string Sample, List<string> Details) {
        public int Count { get; set; } = 1;
    }

    /// <summary>Errors/warnings of one server log, grouped by message with numbers ignored; the first stack trace is kept.</summary>
    private IEnumerable<LogProblem> ScanServerLog(string server) {
        string path = Path.Combine(settings.LogDir, $"{server.ToLowerInvariant()}.log");
        var groups = new Dictionary<string, LogProblem>();
        LogProblem? current = null;
        foreach (string line in ReadLines(path)) {
            Match match = LogLine.Match(line);
            if (!match.Success) {
                // Continuation (exception text / stack trace) of the previous problem line.
                if (current is { Count: 1 } && current.Details.Count < 25 && line.Trim().Length > 0) {
                    current.Details.Add(line.Trim());
                }
                continue;
            }

            current = null;
            string level = match.Groups["level"].Value;
            if (level is not ("WRN" or "ERR" or "FTL")) {
                continue;
            }
            string message = match.Groups["message"].Value;
            string key = level + match.Groups["source"].Value + Numbers.Replace(message, "#");
            if (groups.TryGetValue(key, out LogProblem? existing)) {
                existing.Count++;
                continue;
            }
            current = new LogProblem(server, level, key, match.Groups["time"].Value,
                $"{match.Groups["source"].Value}: {Shorten(message, 300)}", []);
            groups[key] = current;
        }
        return groups.Values;
    }
    #endregion

    #region Sections
    private string DescribeCharacter() {
        try {
            using var connection = new SqliteConnection($"Data Source={settings.SaveFile};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT Name, Level, Job, MapId, LastModified FROM \"character\" ORDER BY LastModified DESC LIMIT 1";
            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) {
                return "No character yet.";
            }
            return $"Last played: Lv {reader.GetValue(1)} {JobName(Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture))}, " +
                   $"map {reader.GetValue(3)}, saved {reader.GetValue(4)}";
        } catch (Exception ex) {
            return $"(could not read the save file: {ex.Message})";
        }
    }

    private void AppendComputer(StringBuilder text) {
        text.AppendLine($"Windows : {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        text.AppendLine($".NET    : {RuntimeInformation.FrameworkDescription}");
        text.AppendLine($"CPU     : {Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", "?")?.ToString()?.Trim()} ({Environment.ProcessorCount} threads)");
        if (GetMemory() is { } memory) {
            text.AppendLine($"Memory  : {memory.Total / 1024.0 / 1024 / 1024:0.0} GB total, {memory.Free / 1024.0 / 1024 / 1024:0.0} GB free at the end");
        }
        foreach (string gpu in GraphicsCards()) {
            text.AppendLine($"Graphics: {gpu}");
        }
        foreach (string drivePath in new[] { settings.Root, ClientLocator.Find(settings.ClientDir) ?? settings.ClientDir }
                     .Where(folder => !string.IsNullOrEmpty(folder))
                     .Select(folder => Path.GetPathRoot(folder))
                     .Where(rootPath => !string.IsNullOrEmpty(rootPath))
                     .Select(rootPath => rootPath!.ToUpperInvariant())
                     .Distinct()) {
            try {
                var drive = new DriveInfo(drivePath);
                text.AppendLine($"Disk {drive.Name,-4}: {drive.AvailableFreeSpace / 1024.0 / 1024 / 1024:0} GB free");
            } catch (Exception) {
                // drive not available
            }
        }
        Screen? screen = Screen.PrimaryScreen;
        if (screen != null) {
            text.AppendLine($"Screen  : {screen.Bounds.Width}x{screen.Bounds.Height}");
        }
    }

    private void AppendInstall(StringBuilder text) {
        text.AppendLine($"Offline folder: {settings.Root}");
        text.AppendLine($"Data folder   : {settings.DataRoot} ({(settings.Installed ? "installed" : "portable")}, install id {settings.InstallId})");
        string? detected = ClientLocator.Find(settings.ClientDir);
        text.AppendLine($"Game folder   : {(detected ?? "NOT FOUND")} ({(string.IsNullOrEmpty(settings.ClientDir) ? "found automatically" : "set in launcher.ini")})");
        string clientDir = detected ?? settings.ClientDir;
        foreach (string dll in new[] { "NxCharacter64.dll", "AgarciumClient.dll" }) {
            string installed = Path.Combine(clientDir, dll), shipped = Path.Combine(settings.PatchDir, dll);
            string state = !File.Exists(installed) ? "MISSING"
                : !File.Exists(shipped) ? "installed"
                : Hash(installed) == Hash(shipped) ? "up to date" : "DIFFERENT from the shipped patch";
            text.AppendLine($"Patch {dll,-20}: {state}");
        }
        text.AppendLine($"Original DLL backup : {(File.Exists(Path.Combine(clientDir, "NxCharacter64.dll.bak")) ? "present" : "not yet (made on first start)")}");
        text.AppendLine($"Save file           : {(File.Exists(settings.SaveFile) ? $"{new FileInfo(settings.SaveFile).Length / 1024} KB" : "MISSING")}");

        string ini = Path.Combine(settings.DataRoot, "launcher.ini");
        text.AppendLine("launcher.ini:");
        foreach (string line in ReadLines(ini)) {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) {
                continue;
            }
            // Never copy login details into a report.
            text.AppendLine(Regex.IsMatch(trimmed, @"^(username|password)\s*=", RegexOptions.IgnoreCase)
                ? "  " + trimmed[..trimmed.IndexOf('=')] + "=(hidden)"
                : "  " + trimmed);
        }
    }
    #endregion

    #region Helpers
    private static void Section(StringBuilder text, string title) {
        text.AppendLine();
        text.AppendLine($"-- {title} " + new string('-', Math.Max(3, 70 - title.Length)));
    }

    private static IEnumerable<string> ReadLines(string path) {
        if (!File.Exists(path)) {
            return [];
        }
        try {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var lines = new List<string>();
            while (reader.ReadLine() is { } line) {
                lines.Add(StripAnsi(line));
            }
            return lines;
        } catch (IOException) {
            return [];
        }
    }

    private static string Tail(string path, int count) {
        List<string> lines = ReadLines(path).ToList();
        if (lines.Count == 0) {
            return "(empty or missing)";
        }
        IEnumerable<string> tail = lines.Skip(Math.Max(0, lines.Count - count)).Select(line => Shorten(line, 400));
        return (lines.Count > count ? $"... {lines.Count - count} earlier lines not shown ...\n" : "") + string.Join("\n", tail);
    }

    private static string? NewestFile(string folder, string pattern) {
        try {
            return Directory.Exists(folder)
                ? new DirectoryInfo(folder).GetFiles(pattern).OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault()?.FullName
                : null;
        } catch (Exception) {
            return null;
        }
    }

    private static string StripAnsi(string line) => Regex.Replace(line, @"\x1B\[[0-9;]*m", "");

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..max] + " ...";

    private static string FormatDuration(TimeSpan span) => span.TotalHours >= 1 ? $"{(int) span.TotalHours}h {span.Minutes}m" : $"{span.Minutes}m {span.Seconds}s";

    private static string Hash(string path) {
        try {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        } catch (Exception) {
            return "";
        }
    }

    /// <summary>Removes the Windows user name, PC name and machine id.</summary>
    public static string Redact(string text) {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 3) {
            text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }
        foreach (string secret in new[] { Environment.UserName, Environment.MachineName }) {
            if (secret.Length >= 3) {
                text = Regex.Replace(text, $@"\b{Regex.Escape(secret)}\b", "<redacted>", RegexOptions.IgnoreCase);
            }
        }
        return Regex.Replace(text, @"Machine: [0-9a-fA-F-]{8,}", "Machine: <redacted>");
    }

    private static string JobName(int job) => job switch {
        10 => "Beginner", 100 or 101 => "Knight", 200 or 201 => "Berserker", 300 or 301 => "Wizard", 400 or 401 => "Priest",
        500 or 501 => "Archer", 600 or 601 => "Heavy Gunner", 700 or 701 => "Thief", 800 or 801 => "Assassin",
        900 or 901 => "Runeblade", 1000 or 1001 => "Striker", 1100 or 1101 => "Soul Binder", 999 => "Game Master",
        _ => $"job {job}",
    };

    private static string DescribeExitCode(int code) => unchecked((uint) code) switch {
        0xC0000005 => ", access violation (crash)",
        0xC0000409 => ", stack buffer overrun (crash)",
        0xC00000FD => ", stack overflow (crash)",
        0xC0000374 => ", heap corruption (crash)",
        0xC0000017 or 0xE06D7363 => ", out of memory or unhandled exception (crash)",
        0x40010004 => ", closed by Windows / Task Manager",
        _ => "",
    };

    private static IEnumerable<string> GraphicsCards() {
        var names = new List<string>();
        try {
            using RegistryKey? display = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (display != null) {
                foreach (string sub in display.GetSubKeyNames().Where(name => name.All(char.IsDigit))) {
                    using RegistryKey? card = display.OpenSubKey(sub);
                    if (card?.GetValue("DriverDesc") is string name) {
                        names.Add($"{name} (driver {card.GetValue("DriverVersion")})");
                    }
                }
            }
        } catch (Exception) {
            // no access
        }
        return names.Distinct();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    private static (ulong Total, ulong Free)? GetMemory() {
        var status = new MEMORYSTATUSEX { dwLength = (uint) Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref status) ? (status.ullTotalPhys, status.ullAvailPhys) : null;
    }
    #endregion
}
