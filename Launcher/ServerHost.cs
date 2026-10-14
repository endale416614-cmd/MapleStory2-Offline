using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace MapleStory2Offline;

/// <summary>
/// Runs the four Maple2 servers without console windows. They are placed in a job object that
/// Windows kills together with the launcher, so a crash never leaves servers running.
/// </summary>
internal sealed class ServerHost : IDisposable {
    private readonly Settings settings;
    private readonly List<(string Name, Process Process)> servers = [];
    private readonly IntPtr job;

    public ServerHost(Settings settings) {
        this.settings = settings;
        job = CreateKillOnCloseJob();
    }

    // Reads the listener table instead of connecting, so the servers do not log probe sessions.
    public static bool IsListening(int port) {
        return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint => endpoint.Port == port);
    }

    public string LogPath(string name) => Path.Combine(settings.LogDir, $"{name.ToLowerInvariant()}.log");

    private const long MaxLogBytes = 50L * 1024 * 1024;
    private const int SessionsKept = 5;

    /// <summary>Moves the last session's server logs to logs\history\&lt;time&gt; (newest 5 kept).</summary>
    public void PrepareLogs() {
        Directory.CreateDirectory(settings.LogDir);
        string history = Path.Combine(settings.LogDir, "history");
        string[] last = ServerNames.Select(LogPath).Where(File.Exists).ToArray();
        if (last.Length > 0) {
            string target = Path.Combine(history, File.GetLastWriteTime(last[0]).ToString("yyyy-MM-dd_HH-mm-ss"));
            Directory.CreateDirectory(target);
            foreach (string log in last) {
                File.Move(log, Path.Combine(target, Path.GetFileName(log)), true);
            }
        }
        if (Directory.Exists(history)) {
            foreach (DirectoryInfo old in new DirectoryInfo(history).GetDirectories().OrderByDescending(dir => dir.Name).Skip(SessionsKept)) {
                old.Delete(true);
            }
        }
    }

    public static readonly string[] ServerNames = ["World", "Login", "Web", "Game"];

    /// <summary>The save-status file the game server writes when a character enters the world or is saved on leaving.</summary>
    public string SaveStatusFile => Path.Combine(settings.SaveDir, "save-status.json");

    public void Start(string name) {
        string projectDir = Path.Combine(settings.ServerDir, $"Maple2.Server.{name}");
        string exe = Path.Combine(projectDir, "bin", settings.ServerConfiguration, "net8.0", $"Maple2.Server.{name}.exe");
        if (!File.Exists(exe)) {
            throw new FileNotFoundException($"Server program not found: {exe}");
        }

        var info = new ProcessStartInfo(exe) {
            WorkingDirectory = projectDir, // servers read appsettings.json from the current directory
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.Environment["DOTNET_ROOT"] = settings.DotnetDir; // ASP.NET runtime lives in the local dotnet
        info.Environment["DOTNET_ENVIRONMENT"] = "Production";
        // Offline edition: every address on this PC, whatever the player's environment inherited.
        foreach (string variable in new[] { "BIND_IP", "LOGIN_IP", "GAME_IP", "GRPC_GAME_IP", "GRPC_WORLD_IP", "GRPC_LOGIN_IP", "WEB_IP" }) {
            info.Environment[variable] = "127.0.0.1";
        }
        foreach (string variable in new[] { "ASPNETCORE_URLS", "DOTNET_URLS", "ASPNETCORE_HTTP_PORTS", "ASPNETCORE_HTTPS_PORTS" }) {
            info.Environment.Remove(variable);
        }
        info.Environment["OFFLINE_MODE"] = "true";
        info.Environment["DB_PROVIDER"] = "sqlite";
        info.Environment["OFFLINE_SAVE_STATUS_FILE"] = SaveStatusFile;
        info.Environment["OFFLINE_LAUNCH_ID"] = EventLog.SessionId;
        // Saves next to the launcher; release builds keep the game data in GameData so updates can replace it.
        info.Environment["SQLITE_DIR"] = settings.SaveDir;
        if (File.Exists(Path.Combine(settings.GameDataDir, "maple-data.db"))) {
            info.Environment["SQLITE_DATA_DIR"] = settings.GameDataDir;
        }
        info.Environment["OFFLINE_TESTKIT"] = settings.TestKit ? "true" : "false";
        info.Environment["OFFLINE_TRIGGER_TRACE"] = settings.TriggerTrace ? "true" : "false";
        info.Environment["OFFLINE_DUNGEON_ALL_DROPS"] = settings.DungeonAllDrops ? "true" : "false";
        info.Environment["OFFLINE_UNLIMITED_DUNGEON_CLEARS"] = settings.UnlimitedDungeonClears ? "true" : "false";
        info.Environment["OFFLINE_GRIND"] = settings.GrindRates ? "true" : "false";
        info.Environment["OFFLINE_MAIL_NEVER_EXPIRES"] = settings.MailNeverExpires ? "true" : "false";
        info.Environment["OFFLINE_CLASS_DROPS"] = settings.ClassDropsOnly ? "true" : "false";
        info.Environment["OFFLINE_PET_TAMING"] = settings.PetTaming ? "true" : "false";
        info.Environment["OFFLINE_CHEATS"] = settings.CheatCommands ? "true" : "false";

        var log = new StreamWriter(new FileStream(LogPath(name), FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) {
            AutoFlush = true,
        };
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        long written = 0;
        void Append(string? line) {
            if (line == null) {
                return;
            }
            lock (log) {
                if (written > MaxLogBytes) {
                    return;
                }
                written += line.Length + 2;
                log.WriteLine(written > MaxLogBytes ? "... log size limit reached, further lines not written ..." : line);
            }
        }
        process.OutputDataReceived += (_, e) => Append(e.Data);
        process.ErrorDataReceived += (_, e) => Append(e.Data);
        process.Exited += (_, _) => { lock (log) log.Dispose(); };

        process.Start();
        AssignProcessToJobObject(job, process.Handle);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        servers.Add((name, process));
    }

    /// <summary>Waits until <paramref name="ready"/> holds; fails fast if a server exits.</summary>
    public async Task WaitAsync(Func<bool> ready, string what, TimeSpan timeout, CancellationToken token) {
        var stopwatch = Stopwatch.StartNew();
        while (!ready()) {
            foreach ((string name, Process process) in servers) {
                if (process.HasExited) {
                    throw new InvalidOperationException($"The {name} server stopped unexpectedly.\n\nLog: {LogPath(name)}");
                }
            }
            if (stopwatch.Elapsed > timeout) {
                throw new TimeoutException($"Timed out waiting for {what}.\n\nLogs: {settings.LogDir}");
            }
            await Task.Delay(250, token);
        }
    }

    public bool LogContains(string name, string text) {
        try {
            using var stream = new FileStream(LogPath(name), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Contains(text, StringComparison.Ordinal);
        } catch (IOException) {
            return false;
        }
    }

    /// <summary>Servers this launcher started that are no longer running.</summary>
    public IEnumerable<string> StoppedServers() {
        foreach ((string name, Process process) in servers) {
            bool exited;
            try {
                exited = process.HasExited;
            } catch (InvalidOperationException) {
                exited = true;
            }
            if (exited) {
                yield return name;
            }
        }
    }

    public void StopAll() {
        // Game first so it stops writing, World last since the others report to it.
        foreach ((string _, Process process) in Enumerable.Reverse(servers)) {
            try {
                if (!process.HasExited) {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            } catch (InvalidOperationException) {
                // already gone
            }
        }
        servers.Clear();
    }

    public void Dispose() {
        StopAll();
        if (job != IntPtr.Zero) {
            CloseHandle(job);
        }
    }

    #region Job object
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    private static IntPtr CreateKillOnCloseJob() {
        IntPtr handle = CreateJobObject(IntPtr.Zero, null);
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION { LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE },
        };
        SetInformationJobObject(handle, JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>());
        return handle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll")]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int length);

    [DllImport("kernel32.dll")]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
    #endregion
}
