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
}
