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

    public static bool IsOwnServer(Listener listener, Settings settings) {
        return listener.ExePath != null
               && listener.ExePath.StartsWith(Path.GetFullPath(settings.ServerDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               && Path.GetFileName(listener.ExePath).StartsWith("Maple2.Server.", StringComparison.OrdinalIgnoreCase);
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

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
