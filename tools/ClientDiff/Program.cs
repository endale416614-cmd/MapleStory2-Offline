// Read-only diff of Xml.m2d archives. Usage: ClientDiff <outDir> <label=path-to-Xml.m2d>...
using System.Security.Cryptography;
using System.Text;
using Maple2.File.IO;

const string usage = "Usage: ClientDiff <outDir> <label=path-to-Xml.m2d>... | ClientDiff --extract <Xml.m2d> <outDir> <pathPrefix>...";
if (args.Length < 2 || (args[0] == "--extract" && args.Length < 3) || (args[0] != "--extract" && args.Skip(1).Any(a => !a.Contains('=')))) {
    Console.Error.WriteLine(usage);
    return 2;
}

// Extract mode: ClientDiff --extract <Xml.m2d> <outDir> <pathPrefix>...
if (args[0] == "--extract") {
    using var src = new M2dReader(args[1]);
    string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[2])) + Path.DirectorySeparatorChar;
    foreach (var entry in src.Files) {
        string name = entry.Name.Replace((char)92, '/');
        if (!args.Skip(3).Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
        string dest = Path.Combine(args[2], name);
        if (!Path.GetFullPath(dest).StartsWith(root, StringComparison.OrdinalIgnoreCase)) { Console.WriteLine("skipped (outside outDir): " + name); continue; }
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.WriteAllBytes(dest, src.GetBytes(entry));
    }
    return 0;
}

string outDir = args[0];
Directory.CreateDirectory(outDir);
var data = new Dictionary<string, Dictionary<string, (long Size, string Hash)>>();
var errors = new Dictionary<string, string>();

foreach (var arg in args.Skip(1)) {
    int eq = arg.IndexOf('=');
    string label = arg[..eq], path = arg[(eq + 1)..];
    try {
        using var reader = new M2dReader(path);
        var rows = new Dictionary<string, (long, string)>();
        foreach (var entry in reader.Files) {
            byte[] bytes = reader.GetBytes(entry);
            rows[entry.Name.Replace((char)92, '/')] = (bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
        }
        data[label] = rows;
        File.WriteAllLines(Path.Combine(outDir, label + ".tsv"),
            rows.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"{r.Key}\t{r.Value.Item1}\t{r.Value.Item2}"));
        Console.WriteLine($"{label}: {rows.Count} entries");
    } catch (Exception ex) {
        errors[label] = $"{ex.GetType().Name}: {ex.Message}";
        Console.WriteLine($"{label}: FAILED {ex.GetType().Name}: {ex.Message}");
    }
}

string Top(string p) { int i = p.IndexOf('/'); return i < 0 ? "(root)" : p[..i]; }
var sb = new StringBuilder("# Client diff summary\n\n## Totals\n\n| label | entries | bytes |\n|---|---|---|\n");
foreach (var (l, d) in data) sb.AppendLine($"| {l} | {d.Count} | {d.Values.Sum(v => v.Size)} |");
foreach (var (l, e) in errors) sb.AppendLine($"| {l} | FAILED | {e.Split('\n')[0]} |");

var labels = data.Keys.ToList();
// presence per path across all successfully read labels
var all = data.Values.SelectMany(d => d.Keys).Distinct().ToList();
string Cls(string p) {
    var has = labels.Where(l => data[l].ContainsKey(p)).ToList();
    if (has.Count == 1) return "only:" + has[0];
    if (has.Count == labels.Count) return has.Select(l => data[l][p].Hash).Distinct().Count() > 1 ? "diff" : "same";
    return "some:" + string.Join("+", has);
}
var cls = all.ToDictionary(p => p, Cls);

sb.AppendLine("\n## Per top-level folder\n");
var cats = cls.Values.Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();
sb.AppendLine("| folder | " + string.Join(" | ", cats) + " |\n|---|" + string.Join("", cats.Select(_ => "---|")));
foreach (var g in cls.GroupBy(kv => Top(kv.Key)).OrderBy(g => g.Key, StringComparer.Ordinal))
    sb.AppendLine($"| {g.Key} | " + string.Join(" | ", cats.Select(c => g.Count(kv => kv.Value == c))) + " |");
sb.AppendLine("| **TOTAL** | " + string.Join(" | ", cats.Select(c => cls.Values.Count(v => v == c))) + " |");

sb.AppendLine("\n## Example paths (up to 30 per folder)\n");
foreach (var c in cats.Where(c => c.StartsWith("only:")))
    foreach (var g in cls.Where(kv => kv.Value == c).GroupBy(kv => Top(kv.Key)).OrderBy(g => g.Key, StringComparer.Ordinal)) {
        sb.AppendLine($"### {c} / {g.Key} ({g.Count()})");
        foreach (var kv in g.OrderBy(k => k.Key, StringComparer.Ordinal).Take(30)) sb.AppendLine("- " + kv.Key);
        sb.AppendLine();
    }

sb.AppendLine("## Locale folders (second-level dirs under string/ and other dirs named like language codes)\n");
foreach (var l in labels) {
    var locs = data[l].Keys.Select(p => p.Split('/')).Where(s => s.Length > 2 && s[0].StartsWith("string", StringComparison.OrdinalIgnoreCase))
        .GroupBy(s => s[0] + "/" + s[1]).Select(g => $"{g.Key} ({g.Count()})").OrderBy(x => x, StringComparer.Ordinal);
    sb.AppendLine($"- {l}: " + string.Join(", ", locs));
}
sb.AppendLine("\n## Top-level folders per label\n");
foreach (var l in labels) sb.AppendLine($"- {l}: " + string.Join(", ", data[l].Keys.GroupBy(Top).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} ({g.Count()})")));
if (errors.Count > 0) { sb.AppendLine("\n## Failures\n"); foreach (var (l, e) in errors) sb.AppendLine($"### {l}\n```\n{e}\n```"); }
File.WriteAllText(Path.Combine(outDir, "summary.md"), sb.ToString());
return 0;
