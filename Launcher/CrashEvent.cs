namespace MapleStory2Offline;

/// <summary>The Windows Application Error entry (event 1000) of a game crash: what crashed, never a path (DL-42).</summary>
internal static partial class CrashEvent {
    /// <summary>One line from event 1000's properties, or null when they are not those of MapleStory2.exe.</summary>
    public static string? Describe(IReadOnlyList<string?> properties) {
        if (properties.Count < 8 || !string.Equals(properties[0], "MapleStory2.exe", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }
        return $"{properties[0]} {properties[1]}: faulting module {properties[3]} {properties[4]}, exception {properties[6]} at offset {properties[7]}";
    }

    /// <summary>
    /// BT-217: the newest line the client patch wrote for a crash ("yyyy-MM-dd HH:mm:ss crash: ...") at or after
    /// <paramref name="since"/>, or null. The lines start with the local time, so they compare as text.
    /// </summary>
    public static string? FromGuardLog(IEnumerable<string> lines, DateTime since) {
        string from = since.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        return lines.LastOrDefault(line => line.Length > 27 && string.CompareOrdinal(line, 19, " crash: ", 0, 8) == 0
            && string.CompareOrdinal(line, 0, from, 0, 19) >= 0);
    }
}
