using System.Diagnostics.Eventing.Reader;

namespace MapleStory2Offline;

// The Windows-only half of CrashEvent: the tests link CrashEvent.cs alone (net8.0 has no event log reader).
internal static partial class CrashEvent {
    /// <summary>The newest MapleStory2.exe crash entry written within 2 minutes before <paramref name="exitTime"/> (plus 30 s), or null. Never throws.</summary>
    public static string? Read(DateTime exitTime) {
        try {
            var query = new EventLogQuery("Application", PathType.LogName, "*[System[Provider[@Name='Application Error'] and (EventID=1000)]]") { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            for (EventRecord? record = reader.ReadEvent(); record != null; record = reader.ReadEvent()) {
                using (record) {
                    if (record.TimeCreated is not { } created || created < exitTime.AddMinutes(-2)) {
                        return null;
                    }
                    if (created > exitTime.AddSeconds(30)) {
                        continue;
                    }
                    if (Describe(record.Properties.Select(property => property.Value?.ToString()).ToList()) is { } line) {
                        return line;
                    }
                }
            }
        } catch (Exception) {
            // no event log access: the report goes without this line
        }
        return null;
    }
}
