using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace MapleStory2Offline;

/// <summary>
/// Who is listening on the ports the servers need. A port held by this installation's own server is ours to stop;
/// one held by any other program is a conflict the player must resolve (it is never terminated).
/// </summary>
internal static class PortGuard {
    /// <summary>Web, login, game channels, gRPC login/world/channels.</summary>
    public static readonly int[] Ports = [4000, 20001, 20002, 20003, 21000, 21001, 21002, 21003];

    public sealed record Listener(int Port, int ProcessId, string? ExePath, string Name);

    public static List<Listener> Listeners() {
        var result = new List<Listener>();
        foreach ((int port, int pid) in TcpListeners().Where(entry => Ports.Contains(entry.Port)).Distinct()) {
            string? exe = ExePath(pid);
            string name = exe != null ? Path.GetFileName(exe) : $"process {pid}";
            result.Add(new Listener(port, pid, exe, name));
        }
        return result;
    }

    public enum Owner { Own, OtherCopy, Foreign }

    /// <summary>The folder the listener's program lives in (null when Windows would not tell its path).</summary>
    public static string? Folder(Listener listener) => listener.ExePath == null ? null : Path.GetDirectoryName(listener.ExePath);

    /// <summary>What a refusal and the session report say about a listener: port, program, process and folder.</summary>
    public static string Describe(Listener listener) =>
        $"port {listener.Port}: {listener.Name} (process {listener.ProcessId})" + (Folder(listener) is { } folder ? $" in {folder}" : "");

    public static bool IsOwnServer(Listener listener, Settings settings) => Classify(listener.ExePath, FinalPath(settings.ServerDir)) == Owner.Own;

    /// <summary>
    /// PG-01 (BT-291): Own = a Maple2.Server.* exe inside this installation's server folder; OtherCopy = the same exe in another
    /// folder (a second copy of the game); Foreign = anything else. <paramref name="serverDir"/> is a <see cref="FinalPath"/>.
    /// </summary>
    public static Owner Classify(string? exePath, string serverDir) {
        if (exePath == null || !Path.GetFileName(exePath).StartsWith("Maple2.Server.", StringComparison.OrdinalIgnoreCase)) {
            return Owner.Foreign;
        }
        return FinalPath(exePath).StartsWith(serverDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? Owner.Own : Owner.OtherCopy;
    }

    public enum Step { Go, Ask, Refuse }

    /// <summary>What the start does next: go on, ask the player, or refuse naming <see cref="Held"/>.</summary>
    public sealed record Claim(Step Step, List<Listener> Held);

    /// <summary>
    /// Before anything is ended: nothing foreign or of another copy = go on; only servers of another copy = ask; a foreign program
    /// among them = refuse at once, without a question (ending the other copy would not free the ports).
    /// </summary>
    public static Claim Before(IReadOnlyList<(Listener Listener, Owner Owner)> listeners) {
        List<(Listener Listener, Owner Owner)> held = listeners.Where(entry => entry.Owner != Owner.Own).ToList();
        List<Listener> lines = held.Select(entry => entry.Listener).ToList();
        return held.Count == 0 ? new Claim(Step.Go, lines)
            : held.All(entry => entry.Owner == Owner.OtherCopy) ? new Claim(Step.Ask, lines) : new Claim(Step.Refuse, lines);
    }

    /// <summary>The player's answer to the question: Yes ends the servers of the other copy, No keeps the refusal.</summary>
    public static Claim Answer(List<Listener> otherCopy, bool yes) => new(yes ? Step.Go : Step.Refuse, otherCopy);

    /// <summary>After the servers were ended: a listener still on a port (a server that could not be ended) refuses the start.</summary>
    public static Claim Afterwards(IReadOnlyList<Listener> stillListening) =>
        new(stillListening.Count == 0 ? Step.Go : Step.Refuse, stillListening.ToList());

    /// <summary>
    /// The path Windows itself reports for a file or folder, as QueryFullProcessImageName does: a junction, a symbolic link, a subst
    /// drive, an 8.3 short name and the case are resolved. A path that does not exist stays as written (made absolute).
    /// </summary>
    public static string FinalPath(string path) {
        string full = Path.GetFullPath(path);
        IntPtr handle = CreateFile(full, 0, 7, IntPtr.Zero, 3, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (handle == new IntPtr(-1)) {
            return full;
        }
        try {
            var buffer = new StringBuilder(1024);
            int length = GetFinalPathNameByHandle(handle, buffer, buffer.Capacity, 0);
            if (length <= 0 || length >= buffer.Capacity) {
                return full;
            }
            string final = buffer.ToString();
            return final.StartsWith(@"\\?\UNC\", StringComparison.Ordinal) ? @"\\" + final[8..]
                : final.StartsWith(@"\\?\", StringComparison.Ordinal) ? final[4..] : final;
        } finally {
            CloseHandle(handle);
        }
    }

    /// <summary>Stops leftover servers of this installation (exact exe path match only).</summary>
    public static void StopOwnServers(IEnumerable<Listener> listeners) {
        foreach (int pid in listeners.Select(listener => listener.ProcessId).Distinct()) {
            try {
                using Process process = Process.GetProcessById(pid);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            } catch (Exception) {
                // already gone
            }
        }
    }

    private static string? ExePath(int pid) {
        IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) {
            return null;
        }
        try {
            var buffer = new StringBuilder(1024);
            int size = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        } finally {
            CloseHandle(handle);
        }
    }

    private static IEnumerable<(int Port, int Pid)> TcpListeners() {
        foreach (int family in new[] { AF_INET, AF_INET6 }) {
            int size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, TCP_TABLE_OWNER_PID_LISTENER, 0);
            IntPtr table = Marshal.AllocHGlobal(size);
            try {
                if (GetExtendedTcpTable(table, ref size, false, family, TCP_TABLE_OWNER_PID_LISTENER, 0) != 0) {
                    continue;
                }
                int count = Marshal.ReadInt32(table);
                // MIB_TCPROW_OWNER_PID is 24 bytes, MIB_TCP6ROW_OWNER_PID 56 bytes; both after a 4-byte count.
                int rowSize = family == AF_INET ? 24 : 56;
                int portOffset = family == AF_INET ? 8 : 20;
                int pidOffset = family == AF_INET ? 20 : 52;
                for (int i = 0; i < count; i++) {
                    IntPtr row = table + 4 + i * rowSize;
                    int port = IPAddress.NetworkToHostOrder((short) Marshal.ReadInt32(row + portOffset)) & 0xFFFF;
                    yield return (port, Marshal.ReadInt32(row + pidOffset));
                }
            } finally {
                Marshal.FreeHGlobal(table);
            }
        }
    }

    private const int AF_INET = 2, AF_INET6 = 23, TCP_TABLE_OWNER_PID_LISTENER = 3, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int family, int tableClass, uint reserved);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(string name, int access, int share, IntPtr security, int disposition, int flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetFinalPathNameByHandle(IntPtr file, StringBuilder path, int size, int flags);

    private const int FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
