namespace MapleStory2Offline;

/// <summary>
/// One line per launcher event (start-up, save, update, recovery, errors) in logs\launcher-events.log, tagged with a
/// per-launch session id so a diagnostics export can correlate them with the server logs. Rotated at 1 MB, 3 kept.
/// </summary>
internal static class EventLog {
    private const long MaxBytes = 1024 * 1024;
    private const int Keep = 3;
    private static readonly object Gate = new();
    private static string? path;

    public static string SessionId { get; } = Guid.NewGuid().ToString("N")[..8];

    public static void Init(Settings settings) {
        if (path != null) {
            return; // already started by Program
        }
        try {
            Directory.CreateDirectory(settings.LogDir);
            path = Path.Combine(settings.LogDir, "launcher-events.log");
            Rotate();
            Write("launch", $"version={settings.Version} install={settings.InstallId} installed={settings.Installed}");
        } catch (Exception) {
            path = null; // logging must never stop the game
        }
    }

    public static void Write(string kind, string text) {
        if (path == null) {
            return;
        }
        lock (Gate) {
            try {
                File.AppendAllText(path, $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} session={SessionId} {kind}: {text.ReplaceLineEndings(" | ")}{Environment.NewLine}");
            } catch (Exception) {
                // disk full or locked: keep playing
            }
        }
    }

    private static void Rotate() {
        if (path == null || !File.Exists(path) || new FileInfo(path).Length < MaxBytes) {
            return;
        }
        for (int i = Keep - 1; i >= 1; i--) {
            string from = $"{path}.{i}", to = $"{path}.{i + 1}";
            if (File.Exists(from)) {
                File.Move(from, to, true);
            }
        }
        File.Move(path, $"{path}.1", true);
    }
}
