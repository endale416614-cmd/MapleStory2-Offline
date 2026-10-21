using System.Text;
using System.Text.RegularExpressions;

namespace MapleStory2Offline;

/// <summary>Known failure signatures of the server logs (R12-21): pattern, category, section of TROUBLESHOOTING.txt.</summary>
internal static class Triage {
    public sealed record Signature(Regex Pattern, string Category, string Section);

    public static readonly Signature[] Known = [
        new(new Regex(@"Packet handler failed for", RegexOptions.IgnoreCase), "Server error in a game action", "[T1]"),
        new(new Regex(@"Unhandled client packet", RegexOptions.IgnoreCase), "Game feature the server does not have yet", "[T2]"),
        new(new Regex(@"Trigger \S+ not found in", RegexOptions.IgnoreCase), "Map script missing", "[T3]"),
        new(new Regex(@"Navmeshes[\\/][^\s'""]+\.navmesh", RegexOptions.IgnoreCase), "Map without server navigation data", "[T4]"),
        new(new Regex(@"database is locked|SQLite Error 5\b", RegexOptions.IgnoreCase), "Save file in use by another program", "[T5]"),
        new(new Regex(@"written by a newer version of the game", RegexOptions.IgnoreCase), "Save from a newer version", "[T6]"),
        new(new Regex(@"Only one usage of each socket address|address already in use", RegexOptions.IgnoreCase), "A server port is taken by another program", "[T7]"),
    ];

    /// <summary>The first known signature found in the text, or null.</summary>
    public static Signature? Classify(string text) => Known.FirstOrDefault(signature => signature.Pattern.IsMatch(text));

    /// <summary>The known problems in the lines (a report or a server log): one row per category with its line count.</summary>
    public static string Describe(IEnumerable<string> lines) {
        var counts = new Dictionary<Signature, int>();
        foreach (string line in lines) {
            if (Classify(line) is { } signature) {
                counts[signature] = counts.GetValueOrDefault(signature) + 1;
            }
        }
        var text = new StringBuilder();
        text.AppendLine(counts.Count == 0 ? "No known problem found." : "Known problems (see TROUBLESHOOTING.txt):");
        foreach ((Signature signature, int count) in counts.OrderBy(entry => entry.Key.Section)) {
            text.AppendLine($"{signature.Section} {signature.Category}: {count} line(s)");
        }
        return text.ToString();
    }
}