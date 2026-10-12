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

    public void PrepareLogs() {
        Directory.CreateDirectory(settings.LogDir);
        string previous = Path.Combine(settings.LogDir, "previous");
        Directory.CreateDirectory(previous);
        foreach (string log in Directory.GetFiles(settings.LogDir, "*.log")) {
            File.Copy(log, Path.Combine(previous, Path.GetFileName(log)), true);
            File.Delete(log);
        }
    }

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
        info.Environment["OFFLINE_TESTKIT"] = settings.TestKit ? "true" : "false";
        info.Environment["OFFLINE_TRIGGER_TRACE"] = settings.TriggerTrace ? "true" : "false";
        info.Environment["OFFLINE_DUNGEON_ALL_DROPS"] = settings.DungeonAllDrops ? "true" : "false";
        info.Environment["OFFLINE_UNLIMITED_DUNGEON_CLEARS"] = settings.UnlimitedDungeonClears ? "true" : "false";
        info.Environment["OFFLINE_GRIND"] = settings.GrindRates ? "true" : "false";

        var log = new StreamWriter(new FileStream(LogPath(name), FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) {
            AutoFlush = true,
        };
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (log) log.WriteLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (log) log.WriteLine(e.Data); };
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
