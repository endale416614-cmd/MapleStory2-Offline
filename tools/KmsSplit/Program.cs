// KMS consolidated XML -> NA per-id XML. Read-only on the clients.
//   KmsSplit <kmsXml.m2d> <outDir> [--only-ids <file>] [--na <naXml.m2d>]   convert (--na: KMS-only ids)
//   KmsSplit --verify <kmsXml.m2d> <naXml.m2d> <reportDir> --sample 500      compare converted vs NA
//   KmsSplit --selftest                                                     comparer self-check
//   KmsSplit --paths <Xml.m2d> <prefix>...                                  element@attr frequency (dev aid)
using System.Text;
using System.Xml.Linq;
using Maple2.File.IO;

static int Usage() { Console.Error.WriteLine("usage: see header of Program.cs"); return 2; }
if (args.Length == 0) return Usage();
if (args[0] == "--selftest") return Cmp.SelfTest() ? 0 : 1;
if (args[0] == "--validate") return args.Length < 2 ? Usage() : Validate(args[1], args.Length > 3 && args[2] == "--model" ? args[3] : null);
if (args[0] == "--paths") return args.Length < 2 ? Usage() : Paths(args[1], args.Skip(2).ToArray());
if (args[0] == "--verify") return args.Length < 4 ? Usage() : Verify(args[1], args[2], args[3], args.Length > 5 && args[4] == "--sample" ? int.Parse(args[5]) : 500);
if (args.Length < 2) return Usage();
return RunConvert(args);

static int Paths(string m2d, string[] prefixes) {
    using var r = new M2dReader(m2d);
    var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
    void Walk(XElement e, string p) {
        p += "/" + e.Name.LocalName;
        counts[p] = counts.GetValueOrDefault(p) + 1;
        foreach (var a in e.Attributes()) counts[p + "@" + a.Name.LocalName] = counts.GetValueOrDefault(p + "@" + a.Name.LocalName) + 1;
        foreach (var c in e.Elements()) Walk(c, p);
    }
    foreach (var f in r.Files) {
        if (!prefixes.Any(p => Layout.Norm(f.Name).StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
        try { Walk(XDocument.Parse(Encoding.UTF8.GetString(r.GetBytes(f)).TrimStart('﻿')).Root!, ""); } catch { }
    }
    foreach (var (k, v) in counts) Console.WriteLine($"{v}\t{k}");
    return 0;
}

// ---------- convert ----------
static int RunConvert(string[] a) {
    string kmsPath = a[0], outDir = a[1];
    HashSet<long>? only = null; string? naPath = null; Dictionary<string, Type>? model = null;
    var lossy = new SortedDictionary<string, int>();
    for (int i = 2; i < a.Length; i += 2) {
        if (i + 1 >= a.Length || a[i] is not ("--only-ids" or "--na" or "--model")) { Console.Error.WriteLine("unknown option or missing value: " + a[i]); return 2; }
        if (a[i] == "--only-ids") only = File.ReadLines(a[i + 1]).Select(l => l.Trim()).Where(l => l.Length > 0).Select(long.Parse).ToHashSet();
        else if (a[i] == "--na") naPath = a[i + 1];
        else if (a[i] == "--model") model = EnumCheck.LoadEnums(a[i + 1]); // Maple2.Model.dll: values the ingest cannot parse become NA defaults
    }
    var k = new Kms(kmsPath);
    using var na = naPath == null ? null : new Na(naPath);
    var skel = na == null ? null : Skeleton.Learn(na, null);
    var sb = new StringBuilder("kind\tkms_ids\tselected\twritten\tfrom_questdata\tfrom_kms_quest_file\tskipped\n");
    foreach (var kind in Layout.Kinds) {
        int total = 0, sel = 0, wrote = 0, fromQd = 0, fromFile = 0, skipped = 0, empty = 0;
        foreach (long id in Conv.Ids(k, kind).Distinct().OrderBy(x => x)) {
            total++;
            if (only != null && !only.Contains(id)) continue;
            if (na != null && na.Ids[kind].Contains(id)) continue;
            sel++;
            string dest = Path.Combine(outDir, Layout.Path(kind, id));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (kind == "quest" && k.QuestFiles.TryGetValue(id, out var raw)) { File.WriteAllBytes(dest, raw); fromFile++; wrote++; continue; }
            var doc = Conv.Make(k, kind, id);
            if (doc == null) { skipped++; continue; }
            if (!doc.Root!.Elements().Any()) { empty++; continue; } // KMS keeps bare placeholder records such as <item id=".."/>
            skel?.Fill(kind, doc);
            Conv.Repair(kind, doc);
            if (kind == "item" && model != null)
                foreach (var (el, attr, val) in EnumCheck.ItemEnumErrors(doc, model)) {
                    string key = $"{el.Name}@{attr}=\"{val}\" -> " + (el.Name == "slot" ? "slot removed" : "\"\"");
                    lossy[key] = lossy.GetValueOrDefault(key) + 1;
                    if (el.Name == "slot") el.Remove(); else el.SetAttributeValue(attr, "");
                }
            doc.Save(dest); wrote++;
            if (kind == "quest") fromQd++;
        }
        sb.AppendLine($"{kind}\t{total}\t{sel}\t{wrote}\t{fromQd}\t{fromFile}\t{skipped}	{empty}");
    }
    foreach (var (kk, n) in lossy) sb.AppendLine($"lossy	{n}	{kk}");
    Console.Write(sb);
    File.WriteAllText(outDir.TrimEnd(Path.DirectorySeparatorChar, (char)47) + ".summary.tsv", sb.ToString()); // outside outDir: it is packed as an overlay
    return 0;
}

// ---------- verify ----------
static int Verify(string kmsPath, string naPath, string reportDir, int sample) {
    Directory.CreateDirectory(reportDir);
    var k = new Kms(kmsPath);
    using var na = new Na(naPath);
    var rows = new (string Label, string Kind, Func<long, XDocument?> Make, IEnumerable<long> Ids)[] {
        ("item", "item", id => Conv.Make(k, "item", id), k.Items.Keys),
        ("npc", "npc", id => Conv.Make(k, "npc", id), k.Npcs.Keys),
        ("quest", "quest", id => Conv.Make(k, "quest", id), k.QuestData.Keys),
        ("quest-kmsfile", "quest", id => Kms.Parse(k.QuestFiles[id]), k.QuestFiles.Keys),
        ("skill", "skill", id => Conv.Make(k, "skill", id), k.Skills.Keys),
    };
    var picks = new Dictionary<string, List<long>>();
    foreach (var (label, kind, _, ids) in rows) {
        var rng = new Random(12345);
        picks[label] = ids.Where(na.Ids[kind].Contains).OrderBy(x => x).OrderBy(_ => rng.Next()).Take(sample).OrderBy(x => x).ToList();
    }
    // defaults are learned from NA files that are NOT in any sample
    var skel = Skeleton.Learn(na, picks.Values.SelectMany(x => x).ToHashSet());
    var summary = new StringBuilder();
    foreach (bool fill in new[] { false, true }) {
        summary.AppendLine(fill ? "\n### with NA-default fill (--na)\n" : "### plain structural conversion (KMS data only)\n");
        summary.AppendLine("| kind | KMS ids | shared with NA | sampled | identical | default-only | real diffs | avg real diffs per id | errors |\n|---|---|---|---|---|---|---|---|---|");
        foreach (var (label, kind, make, ids) in rows) {
            if (fill && label == "quest-kmsfile") continue; // file is taken as-is, never filled
            var picked = picks[label];
            int ident = 0, defOnly = 0, real = 0, err = 0; long realItems = 0;
            var sigs = new Dictionary<string, (int Count, long Example, string Detail)>();
            var examples = new List<string>();
            foreach (long id in picked) {
                try {
                    var doc = make(id)!;
                    if (fill) skel.Fill(kind, doc);
                    Conv.Repair(kind, doc);
                    var diffs = Cmp.Compare(doc.Root!, Kms.Parse(na.Get(Layout.Path(kind, id))).Root!);
                    realItems += diffs.Count(d => d.Real);
                    if (diffs.Count == 0) { ident++; continue; }
                    if (diffs.All(d => !d.Real)) defOnly++;
                    else {
                        real++;
                        if (examples.Count < 20) examples.Add($"- {id}: " + string.Join("; ", diffs.Where(d => d.Real).Take(6).Select(d => d.ToString())));
                    }
                    foreach (var d in diffs) {
                        string key = (d.Real ? "REAL " : "default ") + d.Sig;
                        sigs[key] = sigs.TryGetValue(key, out var s) ? (s.Count + 1, s.Example, s.Detail) : (1, id, d.Detail);
                    }
                } catch (Exception ex) { err++; if (err <= 3) Console.WriteLine($"{label} {id}: {ex.GetType().Name} {ex.Message}"); }
            }
            summary.AppendLine($"| {label} | {ids.Count()} | {na.Ids[kind].Intersect(ids).Count()} | {picked.Count} | {ident} | {defOnly} | {real} | {(picked.Count == 0 ? 0 : (double)realItems / picked.Count):0.0} | {err} |");
            var rep = new StringBuilder($"# {label} ({(fill ? "with NA-default fill" : "plain")})\n\nsampled {picked.Count} shared ids: identical {ident}, default-only {defOnly}, real {real}, errors {err}\n\n## Real-diff examples (up to 20 ids)\n\n");
            rep.AppendLine(string.Join("\n", examples));
            rep.AppendLine("\n## Difference signatures (sampled ids affected, example id, detail; kms side vs na side)\n");
            foreach (var kv in sigs.OrderBy(x => x.Key.StartsWith("REAL") ? 0 : 1).ThenByDescending(x => x.Value.Count).Take(400))
                rep.AppendLine($"- {kv.Value.Count}x `{kv.Key}` e.g. {kv.Value.Example}: {kv.Value.Detail}");
            File.WriteAllText(Path.Combine(reportDir, label + (fill ? "-filled" : "") + ".md"), rep.ToString());
        }
    }
    File.WriteAllText(Path.Combine(reportDir, "summary.md"), summary.ToString());
    Console.Write(summary);
    return 0;
}

// ---------- validate: deserialize with the parser's own XML root types ----------
static int Validate(string dir, string? modelDll) {
    System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US"); // the ingest does the same
    var asm = typeof(Maple2.File.Parser.ItemParser).Assembly;
    var roots = new Dictionary<string, Type> {
        ["item"] = asm.GetType("Maple2.File.Parser.Xml.Item.ItemDataRoot")!, ["npc"] = asm.GetType("Maple2.File.Parser.Xml.Npc.NpcDataRoot")!,
        ["quest"] = asm.GetType("Maple2.File.Parser.Xml.Quest.QuestDataRootRoot")!, ["skill"] = asm.GetType("Maple2.File.Parser.Xml.Skill.SkillData")!,
    };
    // enums the ingest parses strictly (Enum.Parse in ItemMapper); loaded from Maple2.Model.dll when --model is given
    var enums = modelDll == null ? null : EnumCheck.LoadEnums(modelDll);
    var san = asm.GetType("Maple2.File.Parser.Tools.Sanitizer")!;
    var fails = new Dictionary<string, List<string>>(); // "kind :: element@attr value=.. :: exception" -> files
    foreach (var (kind, type) in roots) {
        var ser = new System.Xml.Serialization.XmlSerializer(type);
        int ok = 0, bad = 0;
        string sub = Path.Combine(dir, kind);
        if (!Directory.Exists(sub)) continue;
        foreach (var f in Directory.EnumerateFiles(sub, "*.xml", SearchOption.AllDirectories)) {
            try {
                // same pre-processing as the parsers: quests and npcs go through Sanitizer first
                string text = File.ReadAllText(f);
                if (kind is "quest" or "npc") text = (string)san.GetMethod(kind == "quest" ? "SanitizeQuest" : "SanitizeNpc", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, new object[] { text })!;
                using var xr = System.Xml.XmlReader.Create(new StringReader(text)); var obj = ser.Deserialize(xr); if (kind == "skill") MapperNeeds.Skill(obj!); ok++;
                if (kind == "npc") foreach (var el in XDocument.Parse(text).Descendants()) { // NpcMapper indexes levels[i] per id/code
                    string? ids = el.Name == "skill" ? "ids" : el.Name == "additionalEffect" ? "codes" : null;
                    if (ids != null && (string?)el.Attribute(ids) is { Length: > 0 } iv && iv.Split((char)44).Length > ((string?)el.Attribute("levels") ?? "").Split((char)44, StringSplitOptions.RemoveEmptyEntries).Length) throw new ArgumentException($"{el.Name} shorter than {ids}");
                }
                if (kind == "item" && enums != null && EnumCheck.ItemEnumErrors(XDocument.Parse(text), enums) is { Count: > 0 } eb) throw new ArgumentException($"{eb[0].El.Name}@{eb[0].Attr} value=\"{eb[0].Value}\" is not a valid enum name");
            }
            catch (Exception ex) {
                bad++;
                var inner = ex; while (inner.InnerException != null) inner = inner.InnerException;
                var m = System.Text.RegularExpressions.Regex.Match(ex.Message, @"\((\d+), (\d+)\)");
                string where = Path.GetFileName(f), key = kind + " :: ? :: " + inner.GetType().Name + " " + inner.Message.Split('\n')[0];
                if (m.Success) {
                    int line = int.Parse(m.Groups[1].Value), col = int.Parse(m.Groups[2].Value);
                    string text = File.ReadLines(f).Skip(line - 1).FirstOrDefault() ?? "";
                    var tag = System.Text.RegularExpressions.Regex.Match(text, @"<(\w+)");
                    System.Text.RegularExpressions.Match? at = null;
                    foreach (System.Text.RegularExpressions.Match a in System.Text.RegularExpressions.Regex.Matches(text, "(\\w+)=\"([^\"]*)\"")) if (a.Index < col) at = a;
                    where = $"{Path.GetFileName(f)}({line},{col})";
                    key = $"{kind} :: {tag.Groups[1].Value}@{at?.Groups[1].Value} value=\"{at?.Groups[2].Value}\" :: {inner.GetType().Name} {inner.Message.Split((char)10)[0]}";
                }
                if (!fails.TryGetValue(key, out var l)) fails[key] = l = new();
                l.Add(where);
            }
        }
        Console.WriteLine($"{kind}: ok {ok}, failed {bad}");
    }
    // one line per kind/element@attribute (values collapsed), with the most common value as example
    foreach (var g in fails.GroupBy(kv => System.Text.RegularExpressions.Regex.Replace(kv.Key, " value=\"[^\"]*\"", ""))) {
        var top = g.OrderByDescending(kv => kv.Value.Count).First();
        Console.WriteLine($"{g.Sum(kv => kv.Value.Count)}x {g.Key}  e.g. {System.Text.RegularExpressions.Regex.Match(top.Key, "value=\"[^\"]*\"").Value} in {top.Value[0]}");
    }
    return fails.Count == 0 ? 0 : 1;
}

// Members SkillMapper dereferences without a null check (a missing XML element leaves them null).
static class MapperNeeds {
    // Required child members per skill element; shared by Skill (validate) and Conv.Repair (fix up).
    public static readonly Dictionary<string, string[]> SkillMembers = new() {
        ["level"] = new[] { "beginCondition", "consume", "recoveryProperty" },
        ["consume"] = new[] { "stat" }, ["beginCondition"] = new[] { "stat" },
        ["motion"] = new[] { "motionProperty" },
        ["attack"] = new[] { "rangeProperty", "arrowProperty", "damageProperty" },
    };
    static object? G(object? o, string n) => o?.GetType().GetField(n)?.GetValue(o) ?? o?.GetType().GetProperty(n)?.GetValue(o);
    static void Need(object? o, string path, params string[] names) { foreach (var n in names) if (G(o, n) == null) throw new ArgumentException($"missing element {path}/{n}"); }
    public static void Skill(object s) {
        if (G(s, "basic") == null) return; // mapper skips these
        foreach (var lv in (System.Collections.IEnumerable)G(s, "level")!) {
            Need(lv, "level", SkillMembers["level"]); Need(G(lv, "consume"), "consume", SkillMembers["consume"]); Need(G(lv, "beginCondition"), "beginCondition", SkillMembers["beginCondition"]);
            foreach (var mo in (System.Collections.IEnumerable)G(lv, "motion")!) {
                Need(mo, "motion", SkillMembers["motion"]);
                foreach (var at in (System.Collections.IEnumerable)G(mo, "attack")!) Need(at, "attack", SkillMembers["attack"]);
            }
        }
    }
}

static class EnumCheck {
// Enum-valued fields that ItemMapper parses strictly (Enum.Parse): ItemFunction, HousingCategory, ItemTag, EquipSlot.
// Returns the offending (element, attribute, value) triples; the type list comes from Maple2.Model.dll (--model).
public static readonly string[] EnumNames = { "ItemFunction", "HousingCategory", "EquipSlot", "ItemTag" };
public static Dictionary<string, Type> LoadEnums(string modelDll) {
    var asm = System.Reflection.Assembly.LoadFrom(modelDll);
    return EnumNames.ToDictionary(n => n, n => asm.GetType("Maple2.Model.Enum." + n)!);
}
public static List<(XElement El, string Attr, string Value)> ItemEnumErrors(XDocument d, Dictionary<string, Type> e) {
    var bad = new List<(XElement, string, string)>();
    void P(string enumName, XElement? el, string attr, bool ic, bool firstOfList = false) {
        string? v = (string?)el?.Attribute(attr);
        if (el == null || string.IsNullOrWhiteSpace(v)) return;
        if (firstOfList) v = v.Split(',')[0];
        if (!Enum.TryParse(e[enumName], v, ic, out _)) bad.Add((el, attr, v));
    }
    foreach (var env in d.Root!.Elements("environment")) {
        P("ItemFunction", env.Element("function"), "name", false);
        P("HousingCategory", env.Element("housing"), "categoryTag", false, true);
        P("ItemTag", env.Element("basic"), "stringTag", false);
        foreach (var s in env.Element("slots")?.Elements("slot") ?? Enumerable.Empty<XElement>()) P("EquipSlot", s, "name", true);
    }
    return bad;
}
}

// ---------- NA archive (read-only) ----------
sealed class Na : IDisposable {
    readonly M2dReader r;
    public readonly Func<string, byte[]> Get;
    public readonly Dictionary<string, HashSet<long>> Ids = Layout.Kinds.ToDictionary(x => x, _ => new HashSet<long>());
    public Na(string path) {
        r = new M2dReader(path);
        var entries = r.Files.ToDictionary(f => Layout.Norm(f.Name));
        Get = n => r.GetBytes(entries[n]);
        int bad = 0, total = 0;
        foreach (var n in entries.Keys) {
            string? kind = Layout.Kinds.FirstOrDefault(x => n.StartsWith(x + "/"));
            if (kind == null || !n.EndsWith(".xml") || !long.TryParse(Path.GetFileNameWithoutExtension(n), out long id)) continue;
            total++;
            if (Layout.Path(kind, id) != n) { bad++; continue; }
            Ids[kind].Add(id);
        }
        Console.WriteLine($"NA per-id entries: {total}, path scheme mismatches: {bad}");
    }
    public void Dispose() => r.Dispose();
}

// ---------- NA defaults: attribute/element statistics learned from NA files ----------
// shortcut: statistics are per element-name path (sibling elements of one name are merged) and learned from a
// 2500-file random sample per kind; fine for filling omitted defaults, not for exact per-record values.
sealed class Skeleton {
    sealed class Node { public int Files, Instances; public Dictionary<string, Dictionary<string, int>> Attr = new(); }
    readonly Dictionary<string, Dictionary<string, Node>> stats = new(); // kind -> path -> node
    readonly Dictionary<string, Dictionary<string, List<string>>> kids = new();

    public static Skeleton Learn(Na na, HashSet<long>? exclude) {
        var s = new Skeleton();
        foreach (var kind in Layout.Kinds) {
            var nodes = s.stats[kind] = new();
            var rng = new Random(777);
            var ids = na.Ids[kind].Where(i => exclude == null || !exclude.Contains(i)).OrderBy(_ => rng.Next()).Take(2500);
            foreach (long id in ids) {
                var root = Kms.Parse(na.Get(Layout.Path(kind, id))).Root!;
                foreach (var unit in Units(kind, root)) {
                    var seen = new HashSet<string>();
                    void Walk(XElement e, string p) {
                        if (!nodes.TryGetValue(p, out var n)) nodes[p] = n = new Node();
                        n.Instances++; seen.Add(p);
                        foreach (var at in e.Attributes()) {
                            if (!n.Attr.TryGetValue(at.Name.LocalName, out var d)) n.Attr[at.Name.LocalName] = d = new();
                            d[at.Value] = d.GetValueOrDefault(at.Value) + 1;
                        }
                        foreach (var c in e.Elements()) Walk(c, p + "/" + c.Name.LocalName);
                    }
                    Walk(unit, "");
                    foreach (var p in seen) nodes[p].Files++;
                }
            }
            var kd = s.kids[kind] = new();
            foreach (var p in nodes.Keys.Where(p => p.Length > 0)) {
                string parent = p[..p.LastIndexOf('/')];
                if (!kd.TryGetValue(parent, out var l)) kd[parent] = l = new();
                l.Add(p[(p.LastIndexOf('/') + 1)..]);
            }
        }
        return s;
    }
    // a unit is the element that holds the record content: the locale-less environment, or the root (skills)
    static IEnumerable<XElement> Units(string kind, XElement root) =>
        kind == "skill" ? new[] { root } : root.Elements("environment").Where(e => ((string?)e.Attribute("locale") ?? "") == "");

    public void Fill(string kind, XDocument doc) {
        foreach (var u in Units(kind, doc.Root!)) FillEl(kind, u, "");
    }
    void FillEl(string kind, XElement e, string path) {
        var nodes = stats[kind];
        if (!nodes.TryGetValue(path, out var n)) return;
        foreach (var (a, vals) in n.Attr) {
            if (a is "feature" or "locale" || e.Attribute(a) != null) continue;
            int total = vals.Values.Sum();
            var best = vals.MaxBy(v => v.Value);
            if (total >= 0.9 * n.Instances && best.Value >= 0.6 * total) e.SetAttributeValue(a, best.Key);
        }
        if (kids[kind].TryGetValue(path, out var names))
            foreach (var c in names) {
                var cn = nodes[path + "/" + c];
                if (!e.Elements(c).Any() && cn.Instances >= 0.9 * n.Instances && cn.Instances <= 1.05 * n.Instances) e.Add(new XElement(c));
                foreach (var ce in e.Elements(c)) FillEl(kind, ce, path + "/" + c);
            }
    }
}

// ---------- KMS side ----------
sealed class Kms {
    public Dictionary<long, XElement> Items = new(), Npcs = new(), QuestData = new(), Skills = new(), ItemModels = new();
    public Dictionary<long, byte[]> QuestFiles = new();

    public Kms(string m2d) {
        using var r = new M2dReader(m2d);
        foreach (var f in r.Files) {
            string n = Layout.Norm(f.Name);
            if (!n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
            bool multi = n.StartsWith("itemdata/") || n.StartsWith("itemmodel/") || n.StartsWith("npcdata/") || n.StartsWith("skilldata/") || n.StartsWith("questdata/");
            bool quest = n.StartsWith("quest/") && n.Count(c => c == '/') == 1;
            if (!multi && !quest) continue;
            byte[] b = r.GetBytes(f);
            if (quest) { if (long.TryParse(Path.GetFileNameWithoutExtension(n), out long q)) QuestFiles[q] = b; continue; }
            var root = Parse(b).Root!;
            var (dict, tag) = n.StartsWith("itemdata/") ? (Items, "item") : n.StartsWith("itemmodel/") ? (ItemModels, "ItemModel")
                : n.StartsWith("npcdata/") ? (Npcs, "npc") : n.StartsWith("skilldata/") ? (Skills, "skill") : (QuestData, "quest");
            foreach (var e in root.Elements(tag)) {
                if (!long.TryParse((string?)e.Attribute("id"), out long id)) { Console.WriteLine($"skipped <{tag}> without numeric id in {n}"); continue; }
                dict[id] = e;
            }
        }
    }
    public static XDocument Parse(byte[] b) => XDocument.Parse(Encoding.UTF8.GetString(b).TrimStart('﻿'));
}

static class Layout {
    public static string Norm(string n) => n.Replace((char)92, (char)47);
    // NA archive path scheme (verified against every NA entry in --verify)
    public static string Item(long id) { var s = id.ToString("D8"); return $"item/{s[0]}/{s.Substring(1, 2)}/{s}.xml"; }
    public static string Npc(long id) { var s = id.ToString("D8"); return $"npc/{s[..2]}/{s.Substring(2, 2)}/{s}.xml"; }
    public static string Quest(long id) => $"quest/{id:D8}.xml";
    public static string Skill(long id) { var s = id.ToString(id >= 100000000 ? "D9" : "D8"); return $"skill/{s[..(s.Length == 9 ? 3 : 2)]}/{s}.xml"; }
    public static readonly string[] Kinds = { "item", "npc", "quest", "skill" };
    public static string Path(string kind, long id) => kind switch { "item" => Item(id), "npc" => Npc(id), "quest" => Quest(id), _ => Skill(id) };
}

static class Conv {
    static XDocument Doc(XElement root) => new(new XDeclaration("1.0", "utf-8", null), root);
    static XElement Env(string feature, string locale = "") => new("environment", new XAttribute("feature", feature), new XAttribute("locale", locale));
    static string A(XElement e, string n) => (string?)e.Attribute(n) ?? "";
    static XElement Sub(XElement p, string n) { var e = p.Element(n); if (e == null) p.Add(e = new XElement(n)); return e; }
    static XElement Clone(XElement e) => new(e);
    // move attribute a of src to dst (as newName); no-op when absent
    static void Mv(XElement src, string a, XElement dst, string? newName = null) {
        var at = src.Attribute(a); if (at == null) return;
        dst.SetAttributeValue(newName ?? a, at.Value); at.Remove();
    }
    static string PresetDir(string id) => id.Length == 8 && id[0] == '1' && id.All(char.IsDigit) ? $"{id[0]}/{id.Substring(1, 2)}/" : "";

    // ----- items: itemdata (+ itemmodel slots/customize) -----
    public static XDocument Item(Kms k, long id) {
        var root = new XElement("ms2");
        k.ItemModels.TryGetValue(id, out var model);
        foreach (var src in k.Items[id].Elements("environment")) {
            var env = Env(A(src, "feature"));
            foreach (var c in src.Elements()) env.Add(Clone(c));
            var prop = env.Element("property");
            if (prop != null) {
                string icon = A(prop, "slotIcon");
                if (icon.Length == 0 && prop.Attribute("skinType") == null) prop.SetAttributeValue("slotIcon", $"./Data/Resource/Image/item/icon/{id}.png");
                else if (icon.Length > 0 && icon.All(char.IsDigit)) prop.SetAttributeValue("slotIcon", $"./Data/Resource/Image/item/icon/{icon}.png");
                else if (icon.Contains("/customize/")) { prop.SetAttributeValue("slotIconCustom", icon); prop.SetAttributeValue("slotIcon", "icon0.png"); }
                prop.SetAttributeValue("presetPath", PresetDir(id.ToString("D8")));
            }
            var tool = env.Element("tool");
            if (tool != null) tool.SetAttributeValue("itemPresetPath", PresetDir(A(tool, "itemPreset")));
            var opt = env.Element("option");
            if (opt != null) {
                if (opt.Attribute("optionLevelFactor") == null) Mv(opt, "optionLevel", opt, "optionLevelFactor");
                else opt.Attribute("optionLevel")?.Remove();
            }
            if (model != null) {
                if (model.Element("slots") is { } sl) env.Add(Clone(sl));
                if (model.Element("customize") is { } cu) {
                    var dst = Sub(env, "customize");
                    foreach (var at in cu.Attributes()) dst.SetAttributeValue(at.Name, at.Value);
                    foreach (var ch in cu.Elements()) dst.Add(Clone(ch));
                    // lossy: NA holds a single palette index; KMS colour-cycling items list several ("19,3"), keep the first
                    if (A(dst, "defaultColorIndex") is { } dci && dci.Contains(',')) dst.SetAttributeValue("defaultColorIndex", dci.Split(',')[0]);
                }
            }
            root.Add(env);
        }
        return Doc(root);
    }

    // ----- npcs: environment attributes are flat in KMS, grouped into basic/speed/... in NA -----
    public static XDocument Npc(Kms k, long id) {
        var root = new XElement("ms2");
        foreach (var src in k.Npcs[id].Elements("environment")) {
            var env = Env(A(src, "feature"), A(src, "locale"));
            var basic = new XElement("basic");
            foreach (var at in src.Attributes()) {
                string n = at.Name.LocalName;
                if (n is "feature" or "locale") continue;
                if (n == "customExp") Sub(env, "exp").SetAttributeValue(n, at.Value);
                else if (n is "lookAtMyPCWhenTalking" or "useTalkMotion") Sub(env, "lookattarget").SetAttributeValue(n, at.Value);
                else basic.SetAttributeValue(n, at.Value);
            }
            env.Add(basic);
            foreach (var c in src.Elements()) {
                var e = Clone(c);
                if (e.Name == "model") {
                    Mv(e, "rotationSpeed", Sub(env, "speed"), "rotation"); Mv(e, "walkSpeed", Sub(env, "speed"), "walk");
                    Mv(e, "runSpeed", Sub(env, "speed"), "run"); Mv(e, "shadowScale", Sub(env, "shadow"), "scale");
                }
                env.Add(e);
            }
            // NA keeps effectdummy next to environment, not inside it
            foreach (var ed in env.Elements("effectdummy").ToList()) { ed.Remove(); if (root.Element("effectdummy") == null) root.Add(ed); }
            root.Add(env);
        }
        return Doc(root);
    }

    // ----- quests: questdata record -> environment/quest -----
    public static XDocument Quest(Kms k, long id) {
        var src = k.QuestData[id];
        var q = new XElement("quest");
        foreach (var c in src.Elements()) q.Add(Clone(c));
        Sub(q, "basic").SetAttributeValue("questID", id.ToString());
        foreach (var v in q.Descendants("v").ToList()) v.Name = "essentialItem";
        var env = Env(A(src, "feature")); env.Add(q);
        return Doc(new XElement("ms2", env));
    }

    // ----- skills: basic-level blocks move into each level, *Property renames, motion attributes -> motionProperty -----
    static readonly Dictionary<string, string> AttackRename = new() { ["arrow"] = "arrowProperty", ["chain"] = "chainProperty", ["damage"] = "damageProperty",
        ["pause"] = "pauseProperty", ["range"] = "rangeProperty", ["actionSkill"] = "conditionSkill" };
    static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
    public static XDocument Skill(Kms k, long id) {
        var src = k.Skills[id];
        var sb = src.Element("basic");
        var root = new XElement("ms2", new XAttribute("feature", sb == null ? "" : A(sb, "feature")));
        if (sb != null) {
            var basic = new XElement("basic");
            foreach (var at in sb.Attributes()) if (at.Name != "feature") basic.SetAttributeValue(at.Name, at.Value);
            foreach (var n in new[] { "ui", "kinds", "stateAttr" }) if (sb.Element(n) is { } e) basic.Add(Clone(e));
            root.Add(basic);
        }
        foreach (var sl in src.Elements("level")) {
            var lv = new XElement("level", new XAttribute("value", A(sl, "value")));
            var bc = new XElement("beginCondition");
            if (sb != null) {
                foreach (var n in new[] { "autoTargeting", "changeSkill", "combo", "magicControl", "push" }) if (sb.Element(n) is { } e) lv.Add(Clone(e));
                if (sb.Element("sensor") is { } sn) { var sp = Clone(sn); sp.Name = "sensorProperty"; lv.Add(sp); }
                if (sb.Element("condition") is { } cd) {
                    foreach (var at in cd.Attributes()) if (at.Name != "sp") bc.SetAttributeValue(at.Name, at.Value);
                    foreach (var ch in cd.Elements()) bc.Add(Clone(ch));
                }
            }
            if (sl.Attribute("cooldown") is { } cdn && double.TryParse(cdn.Value, System.Globalization.NumberStyles.Float, Inv, out var ms))
                bc.SetAttributeValue("cooldownTime", (ms / 1000).ToString("0.######", Inv));
            var stat = new XElement("stat");
            if (sl.Attribute("sp") is { } spA) stat.SetAttributeValue("sp", spA.Value);
            if (sl.Attribute("ep") is { } epA) stat.SetAttributeValue("ep", epA.Value);
            if (stat.HasAttributes || sl.Attribute("meso") != null) {
                var cons = new XElement("consume", stat);
                if (sl.Attribute("meso") is { } me) cons.SetAttributeValue("money", me.Value);
                lv.Add(cons);
            }
            foreach (var at in sl.Attributes()) if (at.Name.LocalName is not ("value" or "cooldown" or "sp" or "ep" or "meso")) lv.SetAttributeValue(at.Name, at.Value);
            foreach (var c in sl.Elements()) {
                if (c.Name == "motion") lv.Add(Motion(c));
                else if (c.Name == "actionAdditional") lv.Add(ToCondition(c));
                else lv.Add(Clone(c));
            }
            if (bc.HasAttributes || bc.HasElements) lv.Add(bc);
            root.Add(lv);
        }
        return Doc(root);
    }
    static XElement ToCondition(XElement c) {
        var e = Clone(c); e.Name = "conditionSkill";
        if (e.Attribute("additionalID") is { } a) { e.SetAttributeValue("skillID", a.Value); a.Remove(); }
        return e;
    }
    static XElement Motion(XElement m) {
        var o = new XElement("motion"); var mp = new XElement("motionProperty");
        foreach (var at in m.Attributes()) mp.SetAttributeValue(at.Name, at.Value);
        o.Add(mp);
        foreach (var c in m.Elements()) {
            if (c.Name != "attack") { o.Add(Clone(c)); continue; }
            var at = new XElement("attack");
            foreach (var a in c.Attributes()) at.SetAttributeValue(a.Name, a.Value);
            foreach (var ch in c.Elements()) {
                if (ch.Name == "actionAdditional") { at.Add(ToCondition(ch)); continue; }
                var e = Clone(ch);
                if (AttackRename.TryGetValue(e.Name.LocalName, out var nn)) e.Name = nn;
                at.Add(e);
            }
            o.Add(at);
        }
        return o;
    }

    // The ingest indexes levels[i] for every id/code, so the lists must have equal length. KMS omits levels equal to 1
    // (lossy when a level above 1 was meant); runs after the NA-default fill, which would write a single value.
    public static void Repair(string kind, XDocument doc) {
        if (kind == "skill") { // SkillMapper dereferences these without null checks
            void Need(XElement p, params string[] names) { foreach (var n in names) Sub(p, n); }
            foreach (var lv in doc.Root!.Elements("level")) {
                var m = MapperNeeds.SkillMembers;
                Need(lv, m["level"]); Need(lv.Element("consume")!, m["consume"]); Need(lv.Element("beginCondition")!, m["beginCondition"]);
                foreach (var mo in lv.Elements("motion")) {
                    Need(mo, m["motion"]);
                    foreach (var at in mo.Elements("attack")) Need(at, m["attack"]);
                }
            }
            return;
        }
        if (kind != "npc") return;
        foreach (var (el, ids, lv) in doc.Descendants().Select(e => (e, e.Name == "skill" ? "ids" : e.Name == "additionalEffect" ? "codes" : "", "levels")).Where(t => t.Item2 != "")) {
            int n = A(el, ids).Split((char)44, StringSplitOptions.RemoveEmptyEntries).Length;
            var have = A(el, lv).Split((char)44, StringSplitOptions.RemoveEmptyEntries).ToList();
            while (have.Count < n) have.Add("1");
            if (n > 0 || have.Count > 0) el.SetAttributeValue(lv, string.Join(",", have.Take(Math.Max(n, 0))));
        }
    }

    public static XDocument? Make(Kms k, string kind, long id) => kind switch {
        "item" => k.Items.ContainsKey(id) ? Item(k, id) : null,
        "npc" => k.Npcs.ContainsKey(id) ? Npc(k, id) : null,
        "quest" => k.QuestData.ContainsKey(id) ? Quest(k, id) : null,
        _ => k.Skills.ContainsKey(id) ? Skill(k, id) : null,
    };
    public static IEnumerable<long> Ids(Kms k, string kind) => kind switch { "item" => k.Items.Keys, "npc" => k.Npcs.Keys, "quest" => k.QuestData.Keys.Union(k.QuestFiles.Keys), _ => k.Skills.Keys };
}

// ---------- semantic comparer ----------
readonly record struct Diff(bool Real, string Sig, string Detail) {
    public override string ToString() => Sig + " " + Detail;
}

static class Cmp {
    static bool IsZero(string v) => v == "" || v == "0" || (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) && d == 0);
    static bool Num(string s, out double d) => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d);
    // equal as text, as numbers, or as comma lists of numbers ("50,25" == "50.000000, 25.000000")
    static bool SameVal(string x, string y) {
        if (x.Trim() == y.Trim()) return true;
        var px = x.Split(','); var py = y.Split(',');
        return px.Length == py.Length && px.Zip(py).All(t => t.First.Trim() == t.Second.Trim() || Num(t.First, out var a) && Num(t.Second, out var b) && a == b);
    }
    static bool AllDefault(XElement e) => e.Attributes().All(a => IsDefault(a.Name.LocalName, a.Value)) && e.Elements().All(AllDefault) && (e.HasElements || e.Value.Trim() == "");
    static string Key(XElement e) => e.Name.LocalName == "environment" ? ((string?)e.Attribute("feature") ?? "") + "|" + ((string?)e.Attribute("locale") ?? "") : new[] { "value", "name", "id", "code" }.Select(n => (string?)e.Attribute(n)).FirstOrDefault(v => v != null) ?? "";
    // NA spells these non-zero defaults out; KMS omits them (learned from --verify output)
    static readonly Dictionary<string, string> NaDefaults = new() { ["tabIndex"] = "-1", ["defaultColorIndex"] = "-1" };
    static bool IsDefault(string attr, string v) => IsZero(v) || NaDefaults.TryGetValue(attr, out var d) && d == v;

    // a = converted (KMS) side, b = NA side
    public static List<Diff> Compare(XElement a, XElement b) { var l = new List<Diff>(); Rec(a, b, a.Name.LocalName, l); return l; }

    static void Rec(XElement a, XElement b, string path, List<Diff> o) {
        foreach (string n in a.Attributes().Select(x => x.Name.LocalName).Union(b.Attributes().Select(x => x.Name.LocalName))) {
            string? va = (string?)a.Attribute(n), vb = (string?)b.Attribute(n);
            if (va != null && vb != null) { if (!SameVal(va, vb)) o.Add(new(true, $"{path}@{n}", $"kms=\"{Cut(va)}\" na=\"{Cut(vb)}\"")); }
            else if (IsDefault(n, va ?? vb!)) o.Add(new(false, $"{path}@{n}", va == null ? $"absent in kms, na=\"{vb}\"" : $"kms=\"{va}\", absent in na"));
            else o.Add(new(true, $"{path}@{n}", va == null ? $"absent in kms, na=\"{Cut(vb!)}\"" : $"kms=\"{Cut(va)}\", absent in na"));
        }
        if (!a.HasElements && !b.HasElements && a.Value.Trim() != b.Value.Trim()) o.Add(new(true, path + "#text", $"kms=\"{Cut(a.Value.Trim())}\" na=\"{Cut(b.Value.Trim())}\""));
        var names = a.Elements().Select(x => x.Name.LocalName).Concat(b.Elements().Select(x => x.Name.LocalName)).Distinct();
        foreach (string n in names) {
            var la = a.Elements().Where(x => x.Name.LocalName == n).ToList();
            var lb = b.Elements().Where(x => x.Name.LocalName == n).ToList();
            bool keyed = la.Concat(lb).All(x => Key(x) != "") && la.Concat(lb).Select(Key).Distinct().Count() > 1;
            var pairs = new List<(XElement?, XElement?)>();
            if (keyed) {
                foreach (string key in la.Select(Key).Concat(lb.Select(Key)).Distinct())
                    pairs.Add((la.FirstOrDefault(x => Key(x) == key), lb.FirstOrDefault(x => Key(x) == key)));
            } else for (int i = 0; i < Math.Max(la.Count, lb.Count); i++) pairs.Add((i < la.Count ? la[i] : null, i < lb.Count ? lb[i] : null));
            foreach (var (x, y) in pairs) {
                string p = path + "/" + n + (n == "environment" ? "[locale=" + ((string?)(x ?? y)!.Attribute("locale") ?? "") + "]" : "");
                if (x != null && y != null) Rec(x, y, p, o);
                else {
                    var e = (x ?? y)!;
                    o.Add(new(!AllDefault(e), $"{p} only-in-{(x != null ? "kms" : "na")}", Cut(e.ToString(SaveOptions.DisableFormatting))));
                }
            }
        }
    }
    static string Cut(string s) => s.Length > 90 ? s[..90] + "..." : s;

    public static bool SelfTest() {
        int fail = 0;
        void Check(string name, bool ok) { Console.WriteLine((ok ? "ok   " : "FAIL ") + name); if (!ok) fail++; }
        XElement P(string s) => XElement.Parse(s);
        // 1 identical modulo order, whitespace, comments
        var d1 = Compare(P("<a x=\"1\" y=\"2\"><b/> <!-- c --><c k=\"v\"/></a>"), P("<a y=\"2\" x=\"1\">\n  <b/>\n  <c k=\"v\"/>\n</a>"));
        Check("identical ignoring order/whitespace/comments", d1.Count == 0);
        // 2 default-only: attribute absent vs 0 / empty, extra all-default element
        var d2 = Compare(P("<a x=\"1\"/>"), P("<a x=\"1\" y=\"0\" z=\"\"><e q=\"0\"/></a>"));
        Check("default-only differences", d2.Count == 3 && d2.All(d => !d.Real));
        // 3 real: differing value, attr absent vs non-default, extra non-default element
        var d3 = Compare(P("<a x=\"1\"><c v=\"1\"/></a>"), P("<a x=\"2\" y=\"5\"><c v=\"1\"/><e q=\"7\"/></a>"));
        Check("real differences", d3.Count == 3 && d3.All(d => d.Real));
        // 4 keyed matching of repeated siblings, numeric equality
        var d4 = Compare(P("<a><level value=\"2\" t=\"0.60\"/><level value=\"1\" t=\"1\"/></a>"), P("<a><level value=\"1\" t=\"1.0\"/><level value=\"2\" t=\"0.6\"/></a>"));
        Check("keyed siblings and numeric equality", d4.Count == 0);
        // 5 numeric lists
        Check("numeric comma lists", Compare(P("<a v=\"50,25,0\"/>"), P("<a v=\"50.000000, 25.000000,0\"/>")).Count == 0 && Compare(P("<a v=\"50,25\"/>"), P("<a v=\"50,26\"/>")).Count == 1);
        // 6 path scheme
        Check("path scheme", Layout.Item(11300001) == "item/1/13/11300001.xml" && Layout.Npc(2040998) == "npc/02/04/02040998.xml"
            && Layout.Skill(1) == "skill/00/00000001.xml" && Layout.Skill(100000000) == "skill/100/100000000.xml" && Layout.Quest(10001000) == "quest/10001000.xml");
        Console.WriteLine(fail == 0 ? "selftest passed" : $"selftest FAILED ({fail})");
        return fail == 0;
    }
}
