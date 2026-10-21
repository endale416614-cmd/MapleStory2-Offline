// KMS consolidated XML -> NA per-id XML. Read-only on the clients.
//   KmsSplit <kmsXml.m2d> <outDir> [--only-ids <file>] [--na <naXml.m2d>] [--model <Maple2.Model.dll>]   convert (--na: KMS-only ids, NA-default fill, required-attribute fill, item slot rule, npc effectdummy)
//   KmsSplit --verify <kmsXml.m2d> <naXml.m2d> <reportDir> --sample 500      compare converted vs NA
//   KmsSplit --selftest                                                     comparer self-check
//   KmsSplit --fix-skin <dir>                                               converted item files under <dir>/item: skin="1" where skinType is set (in place)
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
if (args[0] == "--fix-skin") return args.Length < 2 ? Usage() : Conv.FixSkinFiles(args[1]);
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
    HashSet<long>? only = null; string? naPath = null, modelLists = null; Dictionary<string, Type>? model = null;
    var lossy = new SortedDictionary<string, int>();
    for (int i = 2; i < a.Length; i += 2) {
        if (i + 1 >= a.Length || a[i] is not ("--only-ids" or "--na" or "--model" or "--item-models")) { Console.Error.WriteLine("unknown option or missing value: " + a[i]); return 2; }
        if (a[i] == "--only-ids") only = File.ReadLines(a[i + 1]).Select(l => l.Trim()).Where(l => l.Length > 0).Select(long.Parse).ToHashSet();
        else if (a[i] == "--na") naPath = a[i + 1];
        else if (a[i] == "--item-models") modelLists = a[i + 1]; // comma list of path<TAB>.. listings of Model/Item archives (rule 5)
        else if (a[i] == "--model") model = EnumCheck.LoadEnums(a[i + 1]); // Maple2.Model.dll: values the ingest cannot parse become NA defaults
    }
    var k = new Kms(kmsPath);
    using var na = naPath == null ? null : new Na(naPath);
    var skel = na == null ? null : Skeleton.Learn(na, null);
    var rules = na == null ? null : NaRules.Learn(na.All()); // learned from ALL NA files of each kind
    if (rules != null && modelLists != null) rules.Models = NaRules.LoadModels(modelLists.Split((char)44));
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
            if (kind == "quest" && k.QuestFiles.TryGetValue(id, out var raw)) {
                var qd = rules == null ? null : Kms.Parse(raw);
                if (qd != null && rules!.Apply(kind, qd) > 0) qd.Save(dest); else File.WriteAllBytes(dest, raw); // untouched files stay byte-exact
                fromFile++; wrote++; continue;
            }
            var doc = Conv.Make(k, kind, id);
            if (doc == null) { skipped++; continue; }
            if (!doc.Root!.Elements().Any()) { empty++; continue; } // KMS keeps bare placeholder records such as <item id=".."/>
            skel?.Fill(kind, doc);
            Conv.Repair(kind, doc);
            if (kind == "item") rules?.Reissue(id, doc, b => {
                static XElement? Pick(XDocument d) => d.Descendants("slots").FirstOrDefault(x => x.Descendants("asset").Any());
                static long Next(XDocument d) => long.TryParse((string?)d.Descendants("tool").Select(t => t.Attribute("itemPreset")).FirstOrDefault(a => a != null), out long n) ? n : 0;
                var docs = new List<(XDocument D, string Src)>();
                if (na!.Ids["item"].Contains(b)) docs.Add((Kms.Parse(na.Get(Layout.Path("item", b))), "NA")); // NA preferred, KMS only if NA has no assets
                if (Conv.Make(k, "item", b) is { } bd) docs.Add((bd, "KMS"));
                foreach (var (d, src) in docs) if (Pick(d) is { } sl) return (sl, src, 0L);
                return (null, "", docs.Count > 0 ? Next(docs[0].D) : 0L);
            });
            if (kind == "item") rules?.ModelFill(id, doc);
            if (kind == "item" && model != null)
                foreach (var (el, attr, val) in EnumCheck.ItemEnumErrors(doc, model)) {
                    string key = $"{el.Name}@{attr}=\"{val}\" -> " + (el.Name == "slot" ? "slot removed" : "\"\"");
                    lossy[key] = lossy.GetValueOrDefault(key) + 1;
                    if (el.Name == "slot") el.Remove(); else el.SetAttributeValue(attr, "");
                }
            rules?.Apply(kind, doc); // after the enum check: fill, then item slot rule, then npc effectdummy
            doc.Save(dest); wrote++;
            if (kind == "quest") fromQd++;
        }
        sb.AppendLine($"{kind}\t{total}\t{sel}\t{wrote}\t{fromQd}\t{fromFile}\t{skipped}	{empty}");
    }
    foreach (var (kk, n) in lossy) sb.AppendLine($"lossy	{n}	{kk}");
    if (rules != null) foreach (var (kk, n) in rules.Counts) sb.AppendLine($"rule	{n}	{kk}");
    if (rules != null) foreach (var e in rules.Examples) sb.AppendLine("example	" + e);
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
    // every NA per-id file, ascending id per kind (deterministic "first file wins" choices)
    public IEnumerable<(string Kind, XDocument Doc)> All() {
        foreach (var kind in Layout.Kinds)
            foreach (long id in Ids[kind].OrderBy(x => x)) yield return (kind, Kms.Parse(Get(Layout.Path(kind, id))));
    }
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

// ---------- in-game verified client fixes, learned from ALL NA files (no sampling) ----------
//  1 fill:  an attribute present in >= 98% of the NA elements at an element path (names only, no sibling index) is added
//           when missing, with the most common NA value; the NA client crashes at start-up (0xC000000D) on missing ones.
//  2 slots: item <slot name=X><asset replace ani> = the dominant NA combination for slot X (rigid attachments 0/0,
//           skinned clothing 1/1, HR 1/0); runs after the fill so the fill cannot bring back a wrong value.
//  3 dummy: npc <effectdummy> missing -> copy from the NA npc with the same <model kfm>, else the NA npc whose
//           <capsule height> is closest.
sealed class NaRules {
    const double Threshold = 0.98;
    sealed class Cnt { public int C; public Dictionary<string, int> Vals = new(); }
    sealed class El { public int N; public Dictionary<string, Cnt> Attrs = new(); }
    readonly Dictionary<string, List<(string Attr, string Val)>> required = new(); // "kind|path" -> attributes to add
    readonly Dictionary<string, (string Replace, string Ani)> slotRule = new();
    readonly Dictionary<string, XElement> dummyByKfm = new();
    readonly List<(double Height, XElement Block)> dummies = new();
    public readonly SortedDictionary<string, int> Counts = new();
    void Count(string key, int n) { if (n > 0) Counts[key] = Counts.GetValueOrDefault(key) + n; }

    // rule 5 templates: per slot name the most common asset (minus name) and the most common non-asset slot children
    readonly Dictionary<string, Dictionary<string, int>> assetTpl = new(), extraTpl = new();
    static void Bump(Dictionary<string, Dictionary<string, int>> d, string slot, string key) {
        if (!d.TryGetValue(slot, out var t)) d[slot] = t = new();
        t[key] = t.GetValueOrDefault(key) + 1;
    }
    public Dictionary<long, List<string>> Models = new(); // item id -> model paths relative to Data/Resource/Model/Item/
    readonly Dictionary<long, List<long>> chains = new();  // item id -> preset chain walked by Reissue
    public readonly List<string> Examples = new();
    public static Dictionary<long, List<string>> LoadModels(IEnumerable<string> tsvs) {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var d = new Dictionary<long, List<string>>();
        foreach (var t in tsvs)
            foreach (var line in File.ReadLines(t)) {
                string p = line.Split((char)9)[0].Trim().Replace((char)92, (char)47);
                string f = p[(p.LastIndexOf((char)47) + 1)..];
                if (!p.EndsWith(".nif", StringComparison.OrdinalIgnoreCase) || f.IndexOf((char)95) < 1 || !long.TryParse(f[..f.IndexOf((char)95)], out long id) || !seen.Add(p)) continue;
                if (!d.TryGetValue(id, out var l)) d[id] = l = new();
                l.Add(p);
            }
        foreach (var l in d.Values) l.Sort(StringComparer.Ordinal);
        return d;
    }
    static string Top(Dictionary<string, int> t) => t.OrderByDescending(v => v.Value).First().Key;
    // gender marker of a model file name after the id: _m_ / _f_ / trailing _m / _f (any case); null = none
    static char? Marker(string path) {
        var m = System.Text.RegularExpressions.Regex.Match(path[(path.LastIndexOf((char)47) + 1)..], "^[0-9]+_([mfMF])(?=_|[.])");
        return m.Success ? char.ToLowerInvariant(m.Groups[1].Value[0]) : null;
    }
    // 5 model files: slots still without asset -> assets from <id>_*.nif, else <preset>_*.nif along the preset chain.
    // Slot name = property@category, templates (asset attributes, userrotation/physx children, decal) = dominant NA ones.
    // Gender: genderLimit 0 keeps unmarked + _m_, 1 keeps unmarked + _f_, otherwise all; if that leaves nothing, all files.
    public void ModelFill(long id, XDocument doc) {
        foreach (var env in doc.Root!.Elements("environment")) {
            if (env.Element("slots") is not { } sl || sl.Descendants("asset").Any()) continue;
            string cat = (string?)env.Element("property")?.Attribute("category") ?? "";
            if (cat.Length == 0 || !assetTpl.ContainsKey(cat)) continue;
            string? from = null; List<string>? files = null;
            var cands = new List<long> { id }; if (chains.TryGetValue(id, out var ch)) cands.AddRange(ch);
            for (int i = 0; i < cands.Count && files == null; i++) if (Models.TryGetValue(cands[i], out var l)) { files = l; from = i == 0 ? "own-id" : "base"; }
            if (files == null) { Count($"item model fill: none found (kept empty): {id}", 1); continue; }
            string? gl = (string?)env.Element("limit")?.Attribute("genderLimit");
            char? want = gl == "0" ? (char)109 : gl == "1" ? (char)102 : null;
            var pick = files.Where(f => want == null || Marker(f) == null || Marker(f) == want).ToList();
            if (pick.Count == 0) pick = files;
            var slot = new XElement("slot", new XAttribute("name", cat));
            foreach (var f in pick) {
                var asset = XElement.Parse(Top(assetTpl[cat])); asset.SetAttributeValue("name", "Data/Resource/Model/Item/" + f);
                slot.Add(asset);
            }
            foreach (var e in XElement.Parse("<x>" + Top(extraTpl[cat]) + "</x>").Elements()) slot.Add(e);
            sl.RemoveNodes(); sl.Add(slot);
            Count($"item model fill from {from} file", 1);
            if (Examples.Count < 10) Examples.Add($"{id} ({from}) -> " + string.Join(", ", pick));
        }
    }
    static string Kfm(XDocument d) => d.Descendants("model").Select(m => (string?)m.Attribute("kfm")).FirstOrDefault(v => v != null) ?? "";
    static double Height(XDocument d) => double.TryParse(d.Descendants("capsule").Select(c => (string?)c.Attribute("height")).FirstOrDefault(v => v != null),
        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h) ? h : 0;

    public static NaRules Learn(IEnumerable<(string Kind, XDocument Doc)> docs) {
        var r = new NaRules();
        var stats = new Dictionary<string, Dictionary<string, El>>();
        var slotTally = new Dictionary<string, Dictionary<(string, string), int>>();
        void Walk(Dictionary<string, El> st, XElement e, string p) {
            if (!st.TryGetValue(p, out var s)) st[p] = s = new();
            s.N++;
            foreach (var a in e.Attributes().Where(a => !a.IsNamespaceDeclaration)) {
                if (!s.Attrs.TryGetValue(a.Name.LocalName, out var c)) s.Attrs[a.Name.LocalName] = c = new();
                c.C++; c.Vals[a.Value] = c.Vals.GetValueOrDefault(a.Value) + 1;
            }
            foreach (var ch in e.Elements()) Walk(st, ch, p + "/" + ch.Name.LocalName);
        }
        foreach (var (kind, doc) in docs) {
            if (!stats.TryGetValue(kind, out var st)) stats[kind] = st = new();
            Walk(st, doc.Root!, doc.Root!.Name.LocalName);
            if (kind == "item")
                foreach (var s in doc.Descendants("slot").Where(s => s.Attribute("name") != null))
                    foreach (var a in s.Descendants("asset")) {
                        string slot = (string)s.Attribute("name")!;
                        if (!slotTally.TryGetValue(slot, out var t)) slotTally[slot] = t = new();
                        var k = (((string?)a.Attribute("replace")) ?? "0", ((string?)a.Attribute("ani")) ?? "0");
                        t[k] = t.GetValueOrDefault(k) + 1;
                        var at = new XElement(a); at.SetAttributeValue("name", null);
                        Bump(r.assetTpl, slot, at.ToString(SaveOptions.DisableFormatting));
                        Bump(r.extraTpl, slot, string.Concat(s.Elements().Where(e => e.Name != "asset").Select(e => e.ToString(SaveOptions.DisableFormatting))));
                    }
            if (kind == "npc" && doc.Descendants("effectdummy").FirstOrDefault(e => !e.IsEmpty) is { } ed) {
                var block = new XElement(ed);
                string kfm = Kfm(doc);
                if (kfm.Length > 0) r.dummyByKfm.TryAdd(kfm, block);
                r.dummies.Add((Height(doc), block));
            }
        }
        foreach (var (kind, st) in stats)
            foreach (var (path, s) in st) {
                var req = s.Attrs.Where(a => (double)a.Value.C / s.N >= Threshold).Select(a => (a.Key, a.Value.Vals.OrderByDescending(v => v.Value).First().Key)).ToList();
                if (req.Count > 0) r.required[kind + "|" + path] = req;
            }
        foreach (var (slot, t) in slotTally) { var best = t.OrderByDescending(v => v.Value).First().Key; r.slotRule[slot] = best; }
        return r;
    }

    // 4 re-issues: <tool itemPreset="B"> with empty <slots /> is invisible when worn; take B slots (NA file, else converted KMS).
    // Run before the enum check (so copied KMS slots are checked too); rule 2 then normalises them in Apply.
    // slotsOf(x) = (slots with assets or null, source label, x own itemPreset or 0); chains are followed up to 5 hops, cycle-safe.
    public void Reissue(long id, XDocument doc, Func<long, (XElement? Slots, string Src, long Next)> slotsOf) {
        foreach (var env in doc.Root!.Elements("environment")) {
            if (!long.TryParse((string?)env.Element("tool")?.Attribute("itemPreset"), out long b) || b == id) continue;
            if (env.Element("slots") is not { } sl || sl.Descendants("asset").Any()) continue;
            long first = b; XElement? src = null; string from = ""; var seen = new HashSet<long> { id };
            for (int hop = 0; hop < 5 && b != 0 && seen.Add(b); hop++) { (src, from, b) = slotsOf(b); if (src != null) break; }
            chains[id] = seen.Where(x => x != id).ToList(); // HashSet keeps insertion order here (no removals)
            if (src == null) { Count($"item re-issue slots: none available (kept empty): {id} -> {first}", 1); continue; }
            sl.ReplaceWith(new XElement(src)); Count($"item re-issue slots copied from {from}", 1);
        }
    }

    // returns the number of changes made to doc
    public int Apply(string kind, XDocument doc) {
        int fill = Fill(kind, doc.Root!, doc.Root!.Name.LocalName), slots = 0, dummy = 0;
        if (kind == "item")
            foreach (var s in doc.Descendants("slot").Where(s => s.Attribute("name") != null && slotRule.ContainsKey((string)s.Attribute("name")!)))
                foreach (var a in s.Descendants("asset")) {
                    var (rep, ani) = slotRule[(string)s.Attribute("name")!];
                    if ((string?)a.Attribute("replace") != rep || (string?)a.Attribute("ani") != ani) { a.SetAttributeValue("replace", rep); a.SetAttributeValue("ani", ani); slots++; }
                }
        if (kind == "npc" && dummies.Count > 0 && !doc.Descendants("effectdummy").Any(e => !e.IsEmpty)) { // a bare <effectdummy /> counts as missing
            foreach (var e in doc.Descendants("effectdummy").ToList()) e.Remove();
            string kfm = Kfm(doc);
            if (dummyByKfm.TryGetValue(kfm, out var b)) Count("npc effectdummy from same model", 1);
            else { double h = Height(doc); b = dummies.MinBy(d => Math.Abs(d.Height - h)).Block; Count("npc effectdummy from nearest capsule height", 1); }
            doc.Root!.Add(new XElement(b)); dummy++;
        }
        Count("attributes added by fill (" + kind + ")", fill); Count("item slot assets rewritten", slots);
        return fill + slots + dummy;
    }
    int Fill(string kind, XElement e, string path) {
        int n = 0;
        if (required.TryGetValue(kind + "|" + path, out var list))
            foreach (var (a, v) in list) if (e.Attribute(a) == null) { e.SetAttributeValue(a, v); n++; }
        foreach (var c in e.Elements().ToList()) n += Fill(kind, c, path + "/" + c.Name.LocalName);
        return n;
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

    // NA writes skin="1" on cosmetics: KMS skinType set goes with NA skin=1 for 8,028 of 8,094 shared item ids. KMS has no
    // skin attribute and the NA-default fill writes 0, so the outfit window and the Outfit tab missed the item (PC2-F1).
    public static void SkinRule(XElement prop) {
        if (prop.Attribute("skinType") != null && (string?)prop.Attribute("skin") is null or "0") prop.SetAttributeValue("skin", "1");
    }

    // The same rule on converted XML text: only skin="0" inside a <property> tag that has skinType changes.
    public static string FixSkinText(string xml) => System.Text.RegularExpressions.Regex.Replace(xml, "<property\\b[^>]*>",
        m => m.Value.Contains(" skinType=\"") ? System.Text.RegularExpressions.Regex.Replace(m.Value, "(?<=\\s)skin=\"0\"", "skin=\"1\"") : m.Value);

    // FixSkinText on every converted item file under <dir>/item, keeping each file's bytes otherwise (byte-order mark,
    // line ends). Returns 0.
    public static int FixSkinFiles(string dir) {
        int files = 0, changed = 0;
        foreach (var path in Directory.EnumerateFiles(System.IO.Path.Combine(dir, "item"), "*.xml", SearchOption.AllDirectories)) {
            files++;
            byte[] bytes = File.ReadAllBytes(path);
            bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            string text = Encoding.UTF8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
            string fixedText = FixSkinText(text);
            if (fixedText == text) continue;
            File.WriteAllBytes(path, (bom ? new byte[] { 0xEF, 0xBB, 0xBF } : Array.Empty<byte>()).Concat(Encoding.UTF8.GetBytes(fixedText)).ToArray());
            changed++;
        }
        Console.WriteLine($"fix-skin: {changed} of {files} item files changed");
        return 0;
    }

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
                SkinRule(prop);
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
        // 7 NA rules: slot replace/ani follow the dominant NA combination per slot, applied after the fill
        XDocument D(string s) => XDocument.Parse(s);
        string ItemXml(string slot, string rep, string ani) => $"<ms2><environment><slots><slot name=\"{slot}\"><asset replace=\"{rep}\" ani=\"{ani}\"/></slot></slots></environment></ms2>";
        var naItems = new[] { ("item", D(ItemXml("CP", "0", "0"))), ("item", D(ItemXml("CP", "0", "0"))), ("item", D(ItemXml("CL", "1", "1"))), ("item", D(ItemXml("HR", "1", "0"))), ("item", D(ItemXml("HR", "1", "0"))), ("item", D(ItemXml("HR", "0", "0"))) };
        var ruleItems = NaRules.Learn(naItems);
        var kmsItem = D("<ms2><environment><slots><slot name=\"CP\"><asset replace=\"1\" ani=\"1\"/></slot><slot name=\"HR\"><asset replace=\"1\" ani=\"1\"/></slot><slot name=\"CL\"><asset/></slot><slot name=\"ZZ\"><asset replace=\"1\" ani=\"1\"/></slot></slots></environment></ms2>");
        ruleItems.Apply("item", kmsItem);
        string Rep(string slot) { var a = kmsItem.Descendants("slot").First(s => (string)s.Attribute("name")! == slot).Element("asset")!; return a.Attribute("replace")?.Value + "/" + a.Attribute("ani")?.Value; }
        Check("slot rule: CP 0/0, HR 1/0, CL 1/1, unknown slot untouched", Rep("CP") == "0/0" && Rep("HR") == "1/0" && Rep("CL") == "1/1" && Rep("ZZ") == "1/1");
        // 8 NA rules: effectdummy copied from same kfm, else nearest capsule height; npc with one is left alone
        string NpcXml(string kfm, string h, string dummy) => $"<ms2><environment><model kfm=\"{kfm}\"/><capsule height=\"{h}\"/></environment>{(dummy == "" ? "" : $"<effectdummy><dummy tag=\"{dummy}\"/></effectdummy>")}</ms2>";
        var ruleNpc = NaRules.Learn(new[] { ("npc", D(NpcXml("a", "10", "A"))), ("npc", D(NpcXml("b", "100", "B"))) });
        string Dummy(string kfm, string h, string dummy) { var d = D(NpcXml(kfm, h, dummy)); ruleNpc.Apply("npc", d); return string.Join(",", d.Descendants("dummy").Select(x => (string)x.Attribute("tag")!)); }
        Check("effectdummy: same model, nearest height, existing kept", Dummy("b", "10", "") == "B" && Dummy("zz", "30", "") == "A" && Dummy("zz", "90", "") == "B" && Dummy("a", "10", "KEEP") == "KEEP");
        var bare = D("<ms2><environment><model kfm=\"a\"/></environment><effectdummy /></ms2>"); ruleNpc.Apply("npc", bare);
        Check("effectdummy: bare <effectdummy /> counts as missing", bare.Descendants("effectdummy").Count() == 1 && bare.Descendants("dummy").Count() == 1);
        // 10 re-issue: empty slots take the preset item slots (NA first, else KMS), then rule 2 normalises them
        var rr = NaRules.Learn(new[] { ("item", D(ItemXml("CP", "0", "0"))) });
        string Tool(string slots) => $"<ms2><environment><tool itemPreset=\"7\"/>{slots}</environment></ms2>";
        var reis = D(Tool("<slots/>")); rr.Reissue(5, reis, b => (b == 7 ? D(ItemXml("CP", "1", "1")).Descendants("slots").First() : null, "NA", 0L)); rr.Apply("item", reis);
        var none = D(Tool("<slots/>")); rr.Reissue(5, none, _ => (null, "KMS", 0L));
        var own = D(Tool("<slots/>")); rr.Reissue(7, own, _ => (D(ItemXml("CP", "1", "1")).Descendants("slots").First(), "NA", 0L));
        var has = D(Tool(ItemXml("CL", "1", "1").Replace("<ms2><environment>", "").Replace("</environment></ms2>", ""))); rr.Reissue(5, has, _ => (D(ItemXml("CP", "1", "1")).Descendants("slots").First(), "NA", 0L));
        // 2-hop chain 5 -> 7 -> 9 (9 has assets); cycle 5 -> 8 -> 5
        (XElement?, string, long) Chain(long x) => x switch { 7 => (null, "NA", 9), 9 => (D(ItemXml("CP", "1", "1")).Descendants("slots").First(), "KMS", 0), 8 => (null, "NA", 5), _ => (null, "", 0) };
        var hop2 = D(Tool("<slots/>")); rr.Reissue(5, hop2, Chain);
        var cyc = D("<ms2><environment><tool itemPreset=\"8\"/><slots/></environment></ms2>"); rr.Reissue(5, cyc, Chain);
        // 11 rule 5: model files by own id, else preset chain; gender pick; asset attributes from the dominant NA ones
        var m5 = NaRules.Learn(new[] { ("item", D("<ms2><environment><slots><slot name=\"CP\"><asset name=\"x.nif\" selfnode=\"CP\" targetnode=\"Bip01 Head\" gender=\"2\" replace=\"0\" ani=\"0\"/><decal/></slot></slots></environment></ms2>")) });
        m5.Models = new() { [5] = new() { "0/00/00000005_f_a.nif", "0/00/00000005_m_a.nif", "0/00/00000005_b.nif" }, [7] = new() { "0/00/00000007_m_z.nif", "0/00/00000007_F_z.nif" } };
        string G(long id, string gl, string preset) { var d = D($"<ms2><environment><property category=\"CP\"/><limit genderLimit=\"{gl}\"/><tool itemPreset=\"{preset}\"/><slots/></environment></ms2>"); m5.Reissue(id, d, _ => (null, "", 0L)); m5.ModelFill(id, d); m5.Apply("item", d);
            return string.Join(",", d.Descendants("asset").Select(a => ((string)a.Attribute("name")!).Split((char)47)[^1])) + "|" + d.Descendants("asset").FirstOrDefault()?.Attribute("selfnode")?.Value + "|" + d.Descendants("decal").Count(); }
        Check("model fill: gender pick, unmarked kept, base fallback, NA template", G(5, "1", "5") == "00000005_f_a.nif,00000005_b.nif|CP|1" && G(5, "0", "5") == "00000005_m_a.nif,00000005_b.nif|CP|1"
            && G(5, "2", "5").Split((char)44).Length == 3 && G(6, "1", "7") == "00000007_F_z.nif|CP|1" && G(9, "0", "9") == "||0");
        Check("re-issue chain: 2 hops resolved, cycle ends empty", hop2.Descendants("asset").Count() == 1 && !cyc.Descendants("asset").Any());
        Check("re-issue slots: copied+rule2, none stays empty, self/non-empty untouched", reis.Descendants("asset").Count() == 1 && (string)reis.Descendants("asset").First().Attribute("replace")! == "0"
            && !none.Descendants("asset").Any() && !own.Descendants("asset").Any() && has.Descendants("slot").Single().Attribute("name")!.Value == "CL");
        // 9 NA rules: attribute fill at >= 98% presence, most common value; rarer attributes are not added
        var ruleFill = NaRules.Learn(Enumerable.Range(0, 100).Select(i => ("npc", D($"<ms2><environment><collision x=\"{(i < 60 ? 1 : 2)}\"{(i == 0 ? " rare=\"1\"" : "")}/></environment></ms2>"))));
        var fd = D("<ms2><environment><collision/></environment></ms2>"); ruleFill.Apply("npc", fd);
        Check("fill: x added with most common value, rare not", (string?)fd.Descendants("collision").First().Attribute("x") == "1" && fd.Descendants("collision").First().Attribute("rare") == null);
        // skin rule (KMS-02)
        var cosmetic = new XElement("property", new XAttribute("skinType", "2"));
        Conv.SkinRule(cosmetic);
        var plain = new XElement("property", new XAttribute("type", "1"));
        Conv.SkinRule(plain);
        Check("skin rule sets skin=1 where skinType is set", (string?)cosmetic.Attribute("skin") == "1" && plain.Attribute("skin") == null);
        Check("skin text fix touches only cosmetics",
            Conv.FixSkinText("<property skinType=\"2\" skin=\"0\" slotMax=\"1\">") == "<property skinType=\"2\" skin=\"1\" slotMax=\"1\">"
            && Conv.FixSkinText("<property type=\"1\" skin=\"0\">") == "<property type=\"1\" skin=\"0\">"
            && Conv.FixSkinText("<limit skin=\"0\" /><property skin=\"0\" skinType=\"1\" />") == "<limit skin=\"0\" /><property skin=\"1\" skinType=\"1\" />");
        string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kmssplit-skin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(System.IO.Path.Combine(tmp, "item", "1", "10"));
        string withBom = System.IO.Path.Combine(tmp, "item", "1", "10", "1.xml"), noBom = System.IO.Path.Combine(tmp, "item", "1", "10", "2.xml");
        File.WriteAllBytes(withBom, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("<ms2>\r\n<property skinType=\"2\" skin=\"0\" />\r\n</ms2>")).ToArray());
        byte[] plainBytes = Encoding.UTF8.GetBytes("<ms2><property type=\"1\" skin=\"0\" /></ms2>");
        File.WriteAllBytes(noBom, plainBytes);
        Conv.FixSkinFiles(tmp);
        byte[] fixedBytes = File.ReadAllBytes(withBom);
        Check("fix-skin keeps the byte-order mark and line ends",
            fixedBytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF })
            && Encoding.UTF8.GetString(fixedBytes, 3, fixedBytes.Length - 3) == "<ms2>\r\n<property skinType=\"2\" skin=\"1\" />\r\n</ms2>");
        Check("fix-skin leaves other files byte-identical", File.ReadAllBytes(noBom).SequenceEqual(plainBytes));
        Directory.Delete(tmp, recursive: true);

        Console.WriteLine(fail == 0 ? "selftest passed" : $"selftest FAILED ({fail})");
        return fail == 0;
    }
}
