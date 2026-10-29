// KMS consolidated XML -> NA per-id XML. Read-only on the clients.
//   KmsSplit <kmsXml.m2d> <outDir> [--only-ids <file>] [--na <naXml.m2d>] [--model <Maple2.Model.dll>]   convert (--na: KMS-only ids, NA-default fill, required-attribute fill, item slot rule, npc effectdummy)
//   KmsSplit --verify <kmsXml.m2d> <naXml.m2d> <reportDir> --sample 500      compare converted vs NA
//   KmsSplit --selftest                                                     comparer self-check
//   KmsSplit --fix-skin <dir>                                               converted item files under <dir>/item: skin="1" where skinType is set, ride/skill/pet id from param1, the NA OpenItemBox parameter (in place)
//   KmsSplit --fix-items <dir>                                              converted item files under <dir>/item: KMS-2024 type, instrument id, rank, icon and cap attributes as Global writes them (in place)
//   KmsSplit --add-records <dir> <kmsXml.m2d> <naXml.m2d> [<itemsDir>]      the KMS rides, glide skills and petproperty rows that the mounts and pets under <itemsDir>/item (default <dir>) name and NA lacks, written into <dir>; exit 1 if one has no KMS record; and each KMS mount's own summon or glide skill copy, the item in <dir> pointed at it (IM-03)
//                                                                           and the anikeytext.xml blocks of the NPC models under <itemsDir>/npc that NA lacks, and those KMS pets' string/en/petname.xml rows; exit 1 if a model has no KMS keys (IM-04)
//                                                                           and the KMS effect XMLs that the records under <dir> name and NA lacks, written under <dir>/effect (IT-08)
//   KmsSplit --add-records <dir> <kmsXml.m2d> <naXml.m2d> <itemsDir> <Server.m2d> <kms-homes.tsv>
//                                                                           also the KMS homes (IB-02): KMS's drop rows (Server.m2d's KMS2 Backup table) under a home id for each box the KMS items under
//                                                                           <itemsDir>/item reach whose pack rows are not KMS's, in <dir>.server/table/Server/individualItemDrop_Final.xml; the item functions
//                                                                           and <dir>/table/gacha_info.xml rows pointed at the homes; the prizes and keys that are other Global items as kms-homes.tsv says
//                                                                           (twin, copy under its home id, or the row dropped); exit 1 if a reached Global id needs a reviewed row or a home is taken
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
if (args[0] == "--fix-items") return args.Length < 2 ? Usage() : Conv.FixItemFiles(args[1]);
if (args[0] == "--add-records") return args.Length is < 4 or 6 or > 7 ? Usage() : AddRecords(args[1], args[2], args[3], args.Length > 4 ? args[4] : args[1], args.Length > 6 ? args[5] : null, args.Length > 6 ? args[6] : null);
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
            Finish(kind, id, doc, k, na, skel, rules, model, lossy);
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

// The convert step's per-record body after Conv.Make, shared by RunConvert and the KMS copies (IB-02): the NA-default fill, the
// repairs, an item's re-issue slots (NA's first, KMS's only if NA has no assets; a copy is the KMS record, so KMS's first), the
// model fill, the enum check (with --model), then the NA rules.
static void Finish(string kind, long id, XDocument doc, Kms k, Na? na, Skeleton? skel, NaRules? rules, Dictionary<string, Type>? model, SortedDictionary<string, int> lossy, bool kmsFirst = false) {
    skel?.Fill(kind, doc);
    Conv.Repair(kind, doc);
    if (kind == "item") rules?.Reissue(id, doc, b => {
        static XElement? Pick(XDocument d) => d.Descendants("slots").FirstOrDefault(x => x.Descendants("asset").Any());
        static long Next(XDocument d) => long.TryParse((string?)d.Descendants("tool").Select(t => t.Attribute("itemPreset")).FirstOrDefault(a => a != null), out long n) ? n : 0;
        var docs = new List<(XDocument D, string Src)>();
        if (na!.Ids["item"].Contains(b)) docs.Add((Kms.Parse(na.Get(Layout.Path("item", b))), "NA")); // NA preferred, KMS only if NA has no assets
        if (Conv.Make(k, "item", b) is { } bd) docs.Insert(kmsFirst ? 0 : docs.Count, (bd, "KMS"));
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
}

// ---------- add-records: the records the converted mounts and pets name (IM-02) ----------
// The param1 rule (IM-01) points ground mounts, air mounts and pets at a ride, glide skill or pet id; for the ids NA lacks
// the pack needs the KMS record too. The ids come from the overlay that ships (itemsDir), so a record ships exactly when an
// item naming it does, and a reference overlay written with the same itemsDir gets the same records.
static int AddRecords(string dir, string kmsPath, string naPath, string itemsDir, string? serverM2d, string? homesTable) {
    var k = new Kms(kmsPath);
    using var na = new Na(naPath);
    var skel = Skeleton.Learn(na, null);
    var rules = NaRules.Learn(na.All());
    var rideDefaults = Conv.RideDefaults(k, na);
    string naPets = Encoding.UTF8.GetString(na.Get("table/petproperty.xml")).TrimStart('﻿');
    var havePets = XDocument.Parse(naPets).Root!.Elements("pets").Select(e => (string?)e.Attribute("code")).ToHashSet();
    int rides = 0, passengers = 0, skills = 0;
    var pets = new List<XElement>();
    var missing = new SortedSet<long>();
    foreach (var (kind, id) in Conv.RecordRefs(itemsDir).Distinct().OrderBy(r => r.Id)) {
        if (kind == "pet") {
            if (havePets.Contains(id.ToString())) continue;
            if (k.Pets.TryGetValue(id, out var row)) pets.Add(row); else missing.Add(id);
            continue;
        }
        if (na.Ids[kind].Contains(id) || kind == "skill" && Conv.IsCopy(id)) continue; // a copy (IM-03) is written by Copies
        var doc = Conv.Make(k, kind, id);
        if (doc == null) { missing.Add(id); continue; }
        if (kind == "riding") Conv.FillRide(doc, rideDefaults); else skel.Fill(kind, doc);
        Conv.Repair(kind, doc);
        rules.Apply(kind, doc);
        Save(doc, Path.Combine(dir, Layout.Path(kind, id)));
        if (kind == "skill") { skills++; continue; }
        rides++;
        if (Conv.Passengers(k, id) is { } pd) { Save(pd, Path.Combine(dir, Layout.Passenger(id))); passengers++; }
    }
    if (pets.Count > 0) {
        Directory.CreateDirectory(Path.Combine(dir, "table"));
        File.WriteAllBytes(Path.Combine(dir, "table", "petproperty.xml"), Encoding.UTF8.GetBytes(Conv.AddPetRows(naPets, pets)));
    }
    Console.WriteLine($"add-records: {rides} riding ({passengers} with passengers), {skills} glide skill, {pets.Count} petproperty rows; no KMS record: {(missing.Count == 0 ? "none" : string.Join(" ", missing))}");
    bool copied = Copies(dir, k, na, itemsDir);
    bool keyed = AnimationKeys(dir, k, na, itemsDir, pets);
    bool homed = serverM2d == null || KmsHomes(dir, k, na, itemsDir, serverM2d, homesTable!, skel, rules);
    var (effects, noEffect) = Conv.CopyEffects(dir, na.Names.Contains, k.Effects); // after every record is written (BT-125)
    Console.WriteLine($"add-records: {effects.Count} effect XMLs; named but in neither client: {(noEffect.Count == 0 ? "none" : string.Join(" ", noEffect))}");
    return missing.Count == 0 && copied && keyed && homed ? 0 : 1;
}
static void Save(XDocument doc, string path) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); doc.Save(path); }

// ---------- copies: a KMS mount's own skill (IM-03) ----------
// The NA client casts the skill an item names. A KMS ground mount names none (the KMS client summons every mount with its
// skill 80000001), and the NA glide skill a KMS air mount names is gated to the Global item NA made it for (beginCondition
// and targetItem), so the client refuses both. Each such mount under itemsDir gets its own copy in <dir>, and its item file
// in <dir> names it: a ground mount Global's common summon body (the body most of the 301 gosSummon skills share; 80000118,
// Blake Body Pillow's), an air mount the gate-free KMS-v61 body that glided in the 1.2.2 client sample (80010151, 80010092)
// with NA's animations. The copy id follows the item id, so both pack-build passes and later selections give the same ids.
static bool Copies(string dir, Kms k, Na na, string itemsDir) {
    var taken = na.Ids["skill"].Concat(k.Skills.Keys).Concat(k.Glides.Keys).Where(Conv.IsCopy).ToList();
    if (taken.Count > 0) { Console.WriteLine($"add-records: copy skill ids taken in NA or KMS: {string.Join(" ", taken)}"); return false; }
    var summon = Kms.Parse(na.Get(Layout.Skill(80000118)));
    int summons = 0, glides = 0, pointed = 0;
    var bad = new SortedSet<long>();
    foreach (var path in Directory.EnumerateFiles(Path.Combine(itemsDir, "item"), "*.xml", SearchOption.AllDirectories)) {
        long item = long.Parse(Path.GetFileNameWithoutExtension(path));
        var prop = XDocument.Load(path).Descendants("property").FirstOrDefault();
        string type = (string?)prop?.Attribute("type") ?? "";
        if (type is not ("9" or "5") || na.Ids["item"].Contains(item)) continue;
        long nid = type == "9" ? Conv.SummonCopy(item) : Conv.GlideCopy(item);
        XDocument copy;
        if (type == "9") copy = Conv.Summon(summon, nid, item, (string?)prop!.Attribute("slotIcon") ?? "");
        else {
            long sid = k.Items.TryGetValue(item, out var ki) && long.TryParse((string?)ki.Descendants("property").FirstOrDefault()?.Attribute("param1"), out long p1) ? p1 : 0;
            if (!na.Ids["skill"].Contains(sid)) continue; // a KMS glide skill (IM-02) or none
            var naSkill = Kms.Parse(na.Get(Layout.Skill(sid)));
            if (!Conv.GatedElsewhere(naSkill, item)) continue;
            if (!k.Glides.TryGetValue(sid, out var row)) { bad.Add(item); continue; }
            copy = Conv.GlideFor(row, naSkill);
        }
        if (nid < 0) { bad.Add(item); continue; }
        Save(copy, Path.Combine(dir, Layout.Skill(nid)));
        if (type == "9") summons++; else glides++;
        string own = Path.Combine(dir, Layout.Item(item));
        if (File.Exists(own) && Conv.PointAt(own, nid)) pointed++;
    }
    Console.WriteLine($"add-records: {summons} summon and {glides} glide skill copies, {pointed} item files pointed at theirs; no copy: {(bad.Count == 0 ? "none" : string.Join(" ", bad))}");
    return bad.Count == 0;
}

// ---------- animation keys: the model records the shipped KMS NPCs need (IM-04) ----------
// Global keeps every model's sequence keys in one anikeytext.xml, a <kfm name> block per model, that the client and the
// server ingest read (AnimationMapper); an NPC whose model has no block gets no animation row, and the server never walks
// it (AnimationManager, MovementState.Walk). Each model of an NPC file under itemsDir that NA lacks gets the KMS anikeytext
// block, else its KMS anikeyinfo/<kfm>.xml converted; the blocks go before </ms2ani> and the rest stays NA's text. The KMS
// pets also get a string/en/petname.xml row with their item's name, as NA has for its pets (126 of 127 equal the item name).
static bool AnimationKeys(string dir, Kms k, Na na, string itemsDir, List<XElement> pets) {
    string naKeys = Encoding.UTF8.GetString(na.Get("anikeytext.xml")), kmsKeys = k.AniKeys.GetValueOrDefault("anikeytext.xml") ?? "";
    string eol = naKeys.Contains("\r\n") ? "\r\n" : "\n", npcDir = Path.Combine(itemsDir, "npc");
    var models = (Directory.Exists(npcDir) ? Directory.EnumerateFiles(npcDir, "*.xml", SearchOption.AllDirectories) : Enumerable.Empty<string>()).Order(StringComparer.Ordinal)
        .SelectMany(p => XDocument.Load(p).Descendants("model").Select(m => (string?)m.Attribute("kfm") ?? "")).Where(m => m != "").Distinct(StringComparer.OrdinalIgnoreCase);
    var blocks = new StringBuilder();
    int added = 0, fromText = 0;
    var none = new List<string>();
    foreach (var kfm in models) {
        if (naKeys.Contains($"<kfm name=\"{kfm}\"", StringComparison.OrdinalIgnoreCase)) continue;
        string? block = Conv.KfmBlock(kmsKeys, kfm);
        if (block != null) fromText++;
        else if (k.AniKeys.TryGetValue($"anikeyinfo/{kfm}.xml", out var info)) block = Conv.AniKeyBlock(kfm.ToLowerInvariant(), info, eol);
        else { none.Add(kfm); continue; }
        blocks.Append(block); added++;
    }
    if (added > 0) { Directory.CreateDirectory(dir); File.WriteAllBytes(Path.Combine(dir, "anikeytext.xml"), Encoding.UTF8.GetBytes(Conv.InsertBefore(naKeys, "</ms2ani>", blocks.ToString()))); }
    string naNames = Encoding.UTF8.GetString(na.Get("string/en/petname.xml")), itemNames = Path.Combine(itemsDir, "string", "en", "itemname.xml");
    var haveNames = XDocument.Parse(naNames).Root!.Elements("key").Select(e => (string?)e.Attribute("id")).ToHashSet();
    var names = new Dictionary<string, string>();
    if (File.Exists(itemNames)) foreach (var e in XDocument.Load(itemNames).Root!.Elements("key")) names.TryAdd((string?)e.Attribute("id") ?? "", (string?)e.Attribute("name") ?? "");
    var rows = new StringBuilder();
    int named = 0;
    // TODO: the name is that of the item whose id is the pet code (both KMS pets); a KMS pet sold under another item id gets no row: name it from its item when one ships
    foreach (var code in pets.Select(p => (string?)p.Attribute("code") ?? ""))
        if (!haveNames.Contains(code) && names.GetValueOrDefault(code, "") != "") { rows.Append($"\t<key {new XAttribute("id", code)} {new XAttribute("name", names[code])} />{eol}"); named++; }
    if (named > 0) {
        Directory.CreateDirectory(Path.Combine(dir, "string", "en"));
        File.WriteAllBytes(Path.Combine(dir, "string", "en", "petname.xml"), Encoding.UTF8.GetBytes(Conv.InsertBefore(naNames, "</ms2>", rows.ToString())));
    }
    Console.WriteLine($"add-records: {added} animation key blocks ({fromText} from KMS anikeytext, {added - fromText} from anikeyinfo), {named} pet names; no KMS animation keys: {(none.Count == 0 ? "none" : string.Join(" ", none))}");
    return none.Count == 0;
}

// ---------- homes: KMS boxes and capsules give their KMS contents (IB-02, BT-114, D-20) ----------
// The pack keeps Global's record of an id both clients use (D-9), so a KMS box whose drop box id Global uses for another box
// gave Global's contents (20802020's 320000392: a beauty voucher), and a KMS prize id that is another Global item gave that item
// (capsule 22004504's 20303049: the Wayward Soul Accessory Box). The rule: a KMS reference resolves to KMS's record.
// - The walk starts at the KMS items under itemsDir that NA lacks and follows their boxes, capsules (the KMS gacha_info row),
//   keys, and the prizes that are KMS box items the pack ships or copies; it reads KMS records only, never an overlay file, so
//   both build passes and a second run agree.
// - KMS's rows of a box are the Backup table in stage-119's Server.m2d (the KMS2 server table, with weights; the KMS client table
//   has none): its KR and unlocalized groups and rows, the KR mark removed, since the server keeps unlocalized and NA rows only.
// - A box goes under its home (box + 90000) when a row names a twin, a copy or a dropped prize, or when the pack's rows (Final,
//   unlocalized and NA) hold other ids; the homes go before </ms2> in a copy of Final in <dir>.server, outside dir (the client).
// - A prize or key that is a Global id resolves by tools/data/kms-homes.tsv: keep, twin (the Global item the KMS one re-issues),
//   copy (KMS's record under the pinned home id) or drop (the row and its weight). A reached Global id whose KMS look, function
//   family or box contents differ from NA's needs a row (the gate), so a later selection cannot ship an unreviewed collision.
//   A prize the pack lacks goes with its row, unless a twin row names the item the selection ships in its place (a dropped
//   duplicate); a twin is for the KMS item's gender or for both.
// - A smart-drop group (a class box) that loses an id and keeps other rows stops the build: the server gives a class without
//   a row the group's first entry, another class's item.
// - The KMS items, the copies and the IB-01 gacha rows in dir name the homes; a Global gacha a copy names gets a home row.
// Global's records and rows stay as they are, so Global items open exactly as before.
static bool KmsHomes(string dir, Kms k, Na na, string itemsDir, string serverM2d, string tablePath, Skeleton skel, NaRules rules) {
    var table = Homes.ReadTable(File.ReadAllText(tablePath));
    byte[] finalBytes; string backup;
    using (var r = new M2dReader(serverM2d)) {
        var entries = r.Files.ToDictionary(f => Layout.Norm(f.Name), StringComparer.OrdinalIgnoreCase);
        finalBytes = r.GetBytes(entries[Homes.FinalPath]);
        backup = Encoding.UTF8.GetString(r.GetBytes(entries["table/Server/individualItemDrop_Final_Backup.xml"])).TrimStart('﻿');
    }
    bool bom = finalBytes.Length >= 3 && finalBytes[0] == 0xEF && finalBytes[1] == 0xBB && finalBytes[2] == 0xBF;
    string final = Encoding.UTF8.GetString(finalBytes, bom ? 3 : 0, finalBytes.Length - (bom ? 3 : 0));
    var kbox = Homes.Boxes(backup);
    var fbox = Homes.Boxes(final);
    var kcli = Homes.Boxes(k.Tables["table/individualitemdrop_final.xml"]);
    var kgacha = Homes.GachaBoxes(k.Tables["table/gacha_info.xml"]);
    var ngacha = Homes.GachaBoxes(Encoding.UTF8.GetString(na.Get("table/gacha_info.xml")).TrimStart('﻿'));
    var naItems = na.Ids["item"];
    // a copy's home must have no NA record and no NA name line (Global names 11990119-11990123 for ItemExtraction, with no record)
    var naNames = Kms.Parse(na.Get("string/en/itemname.xml")).Root!.Elements("key").Select(e => long.TryParse((string?)e.Attribute("id"), out long v) ? v : 0).ToHashSet();
    var shipped = Directory.EnumerateFiles(Path.Combine(itemsDir, "item"), "*.xml", SearchOption.AllDirectories).Select(p => long.Parse(Path.GetFileNameWithoutExtension(p))).ToHashSet();
    (string Name, long Box, long Key, long Gacha) Kfn(long id) => Homes.Fn(k.Items[id].Elements("environment").Select(e => e.Element("function")).FirstOrDefault(f => f != null));
    long KmsBox(long id) { var f = Kfn(id); return f.Name == "OpenGachaBox" ? kgacha.GetValueOrDefault(f.Gacha) : f.Box; }
    bool BoxItem(long id) => k.Items.ContainsKey(id) && Homes.BoxFns.Contains(Kfn(id).Name);
    // a Global item: an NA record with an unlocalized or NA environment (some NA files hold only a JP one, which the server skips)
    var global = new Dictionary<long, bool>();
    bool Global(long p) => naItems.Contains(p) && (global.TryGetValue(p, out bool g) ? g : global[p] = Homes.NaLocal(Kms.Parse(na.Get(Layout.Item(p)))));
    string Verdict(long p) => Homes.Verdict(Global(p), shipped.Contains(p), table.GetValueOrDefault(p));
    long Map(long p) => Verdict(p) switch { "twin" or "copy" => table[p].Home, "drop" or "unshipped" => 0, _ => p };

    var owners = new SortedSet<long>(shipped.Where(i => !naItems.Contains(i) && BoxItem(i)));
    var queue = new Queue<long>(owners);
    var walked = new HashSet<long>();
    var reached = new SortedSet<long>(); // the ids met as prizes or keys that resolve by the table: Global items, and ids the pack lacks with a row
    var home = new SortedDictionary<long, long>();
    var bad = new List<string>();
    string Meet(long p) {
        string v = Verdict(p);
        if (v is not ("kms" or "unshipped")) reached.Add(p);
        if (v is "kms" or "copy" && BoxItem(p) && owners.Add(p)) queue.Enqueue(p);
        return v;
    }
    while (queue.Count > 0) {
        long owner = queue.Dequeue();
        var f = Kfn(owner);
        if (f.Key > 0 && Meet(f.Key) is "drop" or "unshipped") bad.Add($"key {f.Key} of {owner} is dropped or not shipped");
        long b = KmsBox(owner);
        if (b <= 0 || !walked.Add(b)) continue;
        if (!kbox.TryGetValue(b, out var kb)) { bad.Add($"box {b} of {owner} has no Backup element"); continue; }
        var ids = Homes.Ids(Homes.Kept(kb));
        bool changed = false;
        foreach (long p in ids.Order()) changed |= Meet(p) is "twin" or "copy" or "drop";
        if (changed || !ids.SetEquals(Homes.NaIds(fbox.GetValueOrDefault(b)))) home[b] = Homes.BoxHome(b, h => fbox.ContainsKey(h) || kbox.ContainsKey(h) || kcli.ContainsKey(h));
    }

    // the gate: a reached Global id whose KMS record differs from NA's needs a reviewed row
    bool Flagged(long p) {
        if (!k.Items.TryGetValue(p, out var ki)) return false;
        var nd = Kms.Parse(na.Get(Layout.Item(p)));
        var nf = Homes.Fn(nd.Descendants("function").FirstOrDefault());
        long naBox = nf.Name == "OpenGachaBox" ? ngacha.GetValueOrDefault(nf.Gacha) : nf.Box;
        return Homes.Flag(Homes.KmsLook(ki, p), Homes.NaLook(nd, p), Kfn(p).Name, nf.Name,
            kbox.TryGetValue(KmsBox(p), out var kb) ? Homes.Ids(Homes.Kept(kb)) : new(), Homes.NaIds(fbox.GetValueOrDefault(naBox)));
    }
    var unreviewed = Homes.Unreviewed(reached, table, Flagged);
    if (unreviewed.Count > 0) bad.Add("reached Global ids without a kms-homes.tsv row: " + string.Join(" ", unreviewed));
    foreach (long p in reached.Where(p => Verdict(p) == "refused")) bad.Add($"{p} is not a pack item, so only a twin row applies to it");
    var copies = reached.Where(p => Verdict(p) == "copy").ToList();
    var twins = reached.Where(p => Verdict(p) == "twin").ToList();
    // a twin is an item the pack ships (a Global item, or the shipped KMS item the selection kept in its place), for the KMS
    // item's gender or both: KMS leaves genderLimit out for both genders, and the server reads a missing one as male
    XElement? PackItem(long h) => File.Exists(Path.Combine(itemsDir, Layout.Item(h))) ? XDocument.Load(Path.Combine(itemsDir, Layout.Item(h))).Root : Kms.Parse(na.Get(Layout.Item(h))).Root;
    foreach (long t in twins) {
        long h = table[t].Home;
        if (Verdict(h) is not ("keep" or "kms")) bad.Add($"twin {h} of {t} is not an item the pack ships");
        else if (!Homes.GenderOk(k.Items.TryGetValue(t, out var kt) ? Homes.Gender(Homes.KrEnv(kt), 2) : 2, Homes.Gender(PackItem(h), 0))) bad.Add($"twin {h} of {t} is not for the KMS item's gender");
    }
    foreach (long c in copies.Where(c => !k.Items.ContainsKey(c) || !Homes.ItemHomeOk(c, table[c].Home, h => naItems.Contains(h) || naNames.Contains(h) || k.Items.ContainsKey(h) || copies.Count(o => table[o].Home == h) > 1)))
        bad.Add($"copy home {table[c].Home} of {c} is taken or outside its block");
    foreach (var (b, h) in home) {
        if (h < 0) bad.Add($"box home of {b} is taken or outside its block");
        if (!Homes.Ids(Homes.Kept(kbox[b])).SetEquals(kcli.TryGetValue(b, out var c) ? Homes.Ids(Homes.Kept(c)) : new())) bad.Add($"box {b}: the Backup's KR ids are not the KMS client table's");
    }

    // capsules: an IB-01 row names its box's home; a Global gacha a KMS item names gets a home row when its box moves
    var gachaBox = new SortedDictionary<long, long>();
    var gachaHome = new SortedDictionary<long, (long Home, long Box)>();
    foreach (long o in owners.Where(o => Kfn(o).Name == "OpenGachaBox")) {
        long g = Kfn(o).Gacha, b = kgacha.GetValueOrDefault(g), hb = home.GetValueOrDefault(b, b);
        if (ngacha.TryGetValue(g, out long nb) ? hb != b || nb != b : hb != b) {
            long gh = ngacha.ContainsKey(g) ? Homes.BoxHome(g, h => kgacha.ContainsKey(h) || ngacha.ContainsKey(h)) : 0;
            if (gh < 0) bad.Add($"gacha home of {g} is taken or outside its block");
            else if (gh == 0) gachaBox[g] = hb;
            else gachaHome[g] = (gh, hb);
        }
    }
    long BoxTo(long b) => home.GetValueOrDefault(b, b);
    long GachaTo(long g) => gachaHome.TryGetValue(g, out var gh) ? gh.Home : g;

    // the home boxes: KMS's rows, the prizes mapped, the dropped and unshipped rows left out with their weight
    string eol = final.Contains("\r\n") ? "\r\n" : "\n";
    var blocks = new StringBuilder();
    int dropped = 0;
    foreach (var (b, h) in home) {
        var el = Homes.HomeBox(kbox[b], h, Map, out int removed, out bool classGap);
        dropped += removed;
        if (classGap) bad.Add($"box {b}: a smart-drop group loses an id and keeps other rows, so a class would get another class's item");
        if (el == null) { bad.Add($"box home {h} of {b} has no rows"); continue; }
        blocks.Append($"  <!-- IB-02: KMS box {b} (KMS2 server table) under its home id -->{eol}").Append(Homes.Block(el, eol));
    }
    if (bad.Count > 0) { Console.WriteLine("add-records: no KMS homes written: " + string.Join("; ", bad)); return false; }
    if (home.Count > 0) {
        string server = Path.Combine(dir.TrimEnd(Path.DirectorySeparatorChar, (char)47) + ".server", "table", "Server", "individualItemDrop_Final.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(server)!);
        File.WriteAllBytes(server, (bom ? new byte[] { 0xEF, 0xBB, 0xBF } : Array.Empty<byte>()).Concat(Encoding.UTF8.GetBytes(Conv.InsertBefore(final, "</ms2>", blocks.ToString()))).ToArray());
    }
    int pointed = 0;
    foreach (long o in owners.Where(o => !naItems.Contains(o))) {
        string path = Path.Combine(dir, Layout.Item(o));
        if (File.Exists(path) && Homes.PointFile(path, BoxTo, Map, GachaTo)) pointed++;
    }
    var lossy = new SortedDictionary<string, int>();
    foreach (long c in copies) {
        var doc = Conv.Make(k, "item", c)!;
        Finish("item", c, doc, k, na, skel, rules, null, lossy, kmsFirst: true);
        Homes.SelfRefs(doc, c, table[c].Home);
        Homes.Point(doc, BoxTo, Map, GachaTo);
        Save(doc, Path.Combine(dir, Layout.Item(table[c].Home)));
    }
    int gachaRows = 0;
    string gachaPath = Path.Combine(dir, "table", "gacha_info.xml");
    if (gachaBox.Count + gachaHome.Count > 0) {
        byte[] raw = File.ReadAllBytes(gachaPath); // IB-01's table, copied from tools/data/pack-overlay by the build
        int skip = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF ? 3 : 0;
        string text = Encoding.UTF8.GetString(raw, skip, raw.Length - skip), done;
        (done, gachaRows) = Homes.GachaRows(text, gachaBox, gachaHome);
        if (done != text) File.WriteAllBytes(gachaPath, raw.Take(skip).Concat(Encoding.UTF8.GetBytes(done)).ToArray());
    }
    int twinRows = home.Keys.SelectMany(b => Homes.Ids(Homes.Kept(kbox[b]))).Distinct().Count(p => Verdict(p) == "twin");
    Console.WriteLine($"add-records: {home.Count} box homes, {copies.Count} copies, {twinRows} twins, {dropped} rows dropped, {pointed} functions pointed, {gachaRows} gacha rows");
    return true;
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
    public readonly HashSet<string> Names; // every entry path, in any case (IT-08: the effect XMLs NA has)
    public readonly Dictionary<string, HashSet<long>> Ids = Layout.Kinds.ToDictionary(x => x, _ => new HashSet<long>());
    public Na(string path) {
        r = new M2dReader(path);
        var entries = r.Files.ToDictionary(f => Layout.Norm(f.Name));
        Get = n => r.GetBytes(entries[n]);
        Names = entries.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        int bad = 0, total = 0;
        foreach (var n in entries.Keys) {
            string? kind = Layout.Kinds.FirstOrDefault(x => n.StartsWith(x + "/"));
            if (kind == null || !n.EndsWith(".xml") || !long.TryParse(Path.GetFileNameWithoutExtension(n), out long id)) continue;
            if (kind == "riding" && n.Count(c => c == '/') > 1) continue; // riding/passenger and riding/effectdummy hold other records
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
// TODO: statistics are per element-name path (sibling elements of one name are merged) and learned from a
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
    public Dictionary<long, XElement> Ridings = new(), Glides = new(), Pets = new(); // riding/<id>.xml roots, glidedata and petproperty rows
    public Dictionary<string, string> AniKeys = new(StringComparer.OrdinalIgnoreCase); // anikeytext.xml and anikeyinfo/<kfm>.xml texts (IM-04)
    public Dictionary<string, string> Tables = new(StringComparer.OrdinalIgnoreCase); // the gacha_info.xml and individualitemdrop_final.xml texts (IB-02)
    public Dictionary<string, byte[]> Effects = new(StringComparer.OrdinalIgnoreCase); // effect/*.xml by entry path (IT-08)
    public Dictionary<long, byte[]> QuestFiles = new();

    public Kms(string m2d) {
        using var r = new M2dReader(m2d);
        foreach (var f in r.Files) {
            string n = Layout.Norm(f.Name);
            if (!n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
            bool multi = n.StartsWith("itemdata/") || n.StartsWith("itemmodel/") || n.StartsWith("npcdata/") || n.StartsWith("skilldata/") || n.StartsWith("questdata/");
            bool quest = n.StartsWith("quest/") && n.Count(c => c == '/') == 1;
            if (n.StartsWith("riding/") && n.Count(c => c == '/') == 1 && long.TryParse(Path.GetFileNameWithoutExtension(n), out long ride)) { Ridings[ride] = Parse(r.GetBytes(f)).Root!; continue; }
            if (n == "anikeytext.xml" || n.StartsWith("anikeyinfo/")) { AniKeys[n] = Encoding.UTF8.GetString(r.GetBytes(f)).TrimStart('﻿'); continue; }
            if (n.ToLowerInvariant() is "table/gacha_info.xml" or "table/individualitemdrop_final.xml") { Tables[n] = Encoding.UTF8.GetString(r.GetBytes(f)).TrimStart('﻿'); continue; }
            if (n.StartsWith("effect/", StringComparison.OrdinalIgnoreCase)) { Effects[n] = r.GetBytes(f); continue; }
            if (n is "table/glidedata.xml" or "table/petproperty.xml") {
                bool glide = n == "table/glidedata.xml";
                foreach (var e in Parse(r.GetBytes(f)).Root!.Elements(glide ? "glide" : "pets"))
                    // TODO: a code with two KMS rows keeps the last (61100110-130, 62900002), fine while no pack item names one; pick the row the ingest's feature filter keeps when one does
                    if (long.TryParse((string?)e.Attribute(glide ? "id" : "code"), out long rid)) (glide ? Glides : Pets)[rid] = e;
                continue;
            }
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
    public static string Riding(long id) => $"riding/{id:D8}.xml";
    public static string Passenger(long id) => $"riding/passenger/{id:D8}.xml";
    public static readonly string[] Kinds = { "item", "npc", "quest", "skill", "riding" };
    public static string Path(string kind, long id) => kind switch { "item" => Item(id), "npc" => Npc(id), "quest" => Quest(id), "riding" => Riding(id), _ => Skill(id) };
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

    // KMS keeps a ground mount's ride, an air mount's glide skill and a pet's pet id in property@param1 and has no ride, pet
    // or skill element for them; NA keeps them in <ride rideMonster>, <skill skillID> and <pet petID>. On shared ids
    // param1 is the NA value for 316 of 325 ground mounts (type 9), 140 of 141 air mounts (type 5), 203 of 203 pets (type 11).
    static readonly Dictionary<string, (string El, string Attr)> RefTargets = new() { ["9"] = ("ride", "rideMonster"), ["5"] = ("skill", "skillID"), ["11"] = ("pet", "petID") };
    static bool RefOf(string type, string param1, out (string El, string Attr) target, out int id) {
        id = 0;
        return RefTargets.TryGetValue(type, out target) && int.TryParse(param1, out id) && id > 0;
    }
    public static void RefRule(XElement env) {
        if (env.Element("property") is { } prop && RefOf(A(prop, "type"), A(prop, "param1"), out var t, out int id)) Sub(env, t.El).SetAttributeValue(t.Attr, id);
    }

    // The same rule on converted XML text: in each environment, a 0 in the target attribute becomes param1.
    public static string FixRefsText(string xml) => System.Text.RegularExpressions.Regex.Replace(xml, "<environment\\b.*?</environment>", env => {
        string prop = System.Text.RegularExpressions.Regex.Match(env.Value, "<property\\b[^>]*>").Value;
        string Get(string attr) => System.Text.RegularExpressions.Regex.Match(prop, $"(?<=\\s){attr}=\"([^\"]*)\"").Groups[1].Value;
        if (!RefOf(Get("type"), Get("param1"), out var t, out int id)) return env.Value;
        return System.Text.RegularExpressions.Regex.Replace(env.Value, $"<{t.El}\\b[^>]*>",
            e => System.Text.RegularExpressions.Regex.Replace(e.Value, $"(?<=\\s){t.Attr}=\"0\"", $"{t.Attr}=\"{id}\""));
    }, System.Text.RegularExpressions.RegexOptions.Singleline);

    // KMS writes OpenItemBox as param1 (the individual drop box) and param2 (boxes used per open); NA writes
    // parameter="0,1,0,<box>[,<count>]" for 2,113 of the 2,243 item ids both clients have, and 47 more differ only in
    // fields the server ignores. The parser reads only parameter and the NA-default fill writes "", so the box did not open.
    public static string BoxParameter(string param1, string param2) => "0,1,0," + param1 + (param2.Length > 0 ? "," + param2 : "");
    public static void BoxRule(XElement fn) {
        if (A(fn, "name") == "OpenItemBox" && A(fn, "param1").Length > 0 && A(fn, "parameter").Length == 0)
            fn.SetAttributeValue("parameter", BoxParameter(A(fn, "param1"), A(fn, "param2")));
    }

    // The same rule on converted XML text: only parameter="" inside an OpenItemBox <function> tag that has param1 changes.
    public static string FixBoxText(string xml) => System.Text.RegularExpressions.Regex.Replace(xml, "<function\\b[^>]*>", m => {
        var p1 = System.Text.RegularExpressions.Regex.Match(m.Value, "\\sparam1=\"([^\"]+)\"");
        var p2 = System.Text.RegularExpressions.Regex.Match(m.Value, "\\sparam2=\"([^\"]+)\"");
        if (!m.Value.Contains(" name=\"OpenItemBox\"") || !p1.Success) return m.Value;
        string parameter = BoxParameter(p1.Groups[1].Value, p2.Success ? p2.Groups[1].Value : "");
        return System.Text.RegularExpressions.Regex.Replace(m.Value, "(?<=\\s)parameter=\"\"", $"parameter=\"{parameter}\"");
    });

    // FixSkinText, FixRefsText and FixBoxText on every converted item file under <dir>/item, keeping each file's bytes otherwise
    // (byte-order mark, line ends). Returns 0.
    public static int FixSkinFiles(string dir) {
        int files = 0, changed = 0;
        foreach (var path in Directory.EnumerateFiles(System.IO.Path.Combine(dir, "item"), "*.xml", SearchOption.AllDirectories)) {
            files++;
            byte[] bytes = File.ReadAllBytes(path);
            bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            string text = Encoding.UTF8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
            string fixedText = FixRefsText(FixBoxText(FixSkinText(text)));
            if (fixedText == text) continue;
            File.WriteAllBytes(path, (bom ? new byte[] { 0xEF, 0xBB, 0xBF } : Array.Empty<byte>()).Concat(Encoding.UTF8.GetBytes(fixedText)).ToArray());
            changed++;
        }
        Console.WriteLine($"fix-skin: {changed} of {files} item files changed");
        return 0;
    }

    // ----- items: itemdata (+ itemmodel slots/customize) -----
    // KMS-2024 item encoding as Global writes it (IT-01), from the item ids both clients have:
    //  - property type/subtype: KMS-2024 types Global does not have, to the Global type of the same kind: 8 fishing rod
    //    10/20 (64 of 64), 23 instrument 10/20 (94/94), 24 action 2/14 (262/262), 25 package 10/17 (2,419/2,448),
    //    26 package 10/18 (426/432), 27 other 10/0 (564/604). The server threw on the KMS numbers.
    //    TODO: a 27 without a function is 4/0 in Global (24 of 24), Misc like 10/0; none is converted yet, split 27 by function when one is.
    //  - OpenInstrument: KMS keeps the instrument id in property@param1, Global in function@parameter (94/94).
    //  - grade: Global keeps it in option@constant, KMS in property@rank (21,621 of 22,148 ids without a KMS constantID);
    //    KMS has no constant and the NA-default fill wrote 1. An item with a KMS constantID (gear option table) is left as is.
    static readonly Dictionary<string, (string Type, string SubType)> GlobalTypes = new() {
        ["8"] = ("10", "20"), ["23"] = ("10", "20"), ["24"] = ("2", "14"), ["25"] = ("10", "17"), ["26"] = ("10", "18"), ["27"] = ("10", "0"),
    };

    // The rules on one <environment>; true when they changed something.
    public static bool GlobalItemRules(XElement env) {
        var prop = env.Element("property");
        if (prop == null) return false;
        bool changed = false;
        void Set(XElement e, string name, string value) {
            if ((string?)e.Attribute(name) == value) return;
            e.SetAttributeValue(name, value);
            changed = true;
        }
        if (GlobalTypes.TryGetValue(A(prop, "type"), out var global)) {
            Set(prop, "type", global.Type);
            Set(prop, "subtype", global.SubType);
        }
        // Global gives every type 2 item of these categories subtype 2, the Consumable tab (831 of 831; Action is 14, SKB 8).
        // 21 KMS potions, lures, vouchers and snares come without one and went to the Misc tab (BT-112).
        if (A(prop, "type") == "2" && A(prop, "subtype") is "" or "0" && A(prop, "category") is "PS" or "FUN" or "ETC" or "Cash" or "PetTaming" or "PetTrap" or "PetEx")
            Set(prop, "subtype", "2");
        if (env.Element("function") is { } fn && A(fn, "name") == "OpenInstrument" && A(fn, "parameter") == "" && A(prop, "param1") != "")
            Set(fn, "parameter", A(prop, "param1"));
        string rank = A(prop, "rank");
        if (rank is "1" or "2" or "3" or "4" or "5" or "6" && env.Element("option")?.Attribute("constantID") == null)
            Set(Sub(env, "option"), "constant", rank);
        return changed;
    }

    // An item's look as Global writes it (IT-08), from Global's item files:
    //  - slotIcon: every Global cosmetic names an icon file (8,842 of 8,842), and a named icon is ./Data/Resource/Image/item/icon/<name>.png.
    //    KMS leaves a cosmetic's icon out (the NA-default fill wrote icon0.png) and keeps some as a bare name such as
    //    newGacha_capsule_ducky, so their tiles and slots were blank (BT-127). A cosmetic without one gets the file of its id (a KMS
    //    copy is converted from its source id, so it gets its source's), a bare name its file.
    //  - customize: no Global item has capAttach or rotation (38,383 customize elements); both go. The client ignores them: the
    //    hats that had them were still drawn at the feet without them (fifth client sample), for the reason below.
    //  - userrotation marks a hat the player can turn. Every Global hat with it has customize CP xrotation, yrotation and
    //    zrotation "1" (584 of 584; attach "1" on 553). KMS leaves those flags "0", and the client draws such a hat at the feet
    //    (BT-126: 104 hats, with Global and KMS models alike). With the four flags "1" it sits on the head, with or without its
    //    userrotation (hat test HATX2); dropping userrotation alone did not move it (sixth client sample). Such a hat gets
    //    the three flags and attach "1", as Global writes its turning hats.
    public static bool LookRules(XElement env, long id) {
        bool changed = false;
        if (env.Element("property") is { } prop) {
            string icon = A(prop, "slotIcon").Trim(), name = "";
            if (icon is "" or "icon0.png") { if (prop.Attribute("skinType") != null) name = id.ToString(); }
            else if (!icon.Contains('/') && !icon.Contains('.')) name = icon;
            if (name != "") { prop.SetAttributeValue("slotIcon", $"./Data/Resource/Image/item/icon/{name}.png"); changed = true; }
        }
        if (env.Element("customize") is { } cu && cu.Attribute("capAttach") is { } cap) { cap.Remove(); cu.Attribute("rotation")?.Remove(); changed = true; }
        var cp = env.Element("customize")?.Element("CP");
        if (env.Element("slots")?.Descendants("userrotation").Any() == true
            && !new[] { "xrotation", "yrotation", "zrotation" }.Any(a => (string?)cp?.Attribute(a) == "1")) {
            cp ??= Sub(Sub(env, "customize"), "CP");
            foreach (string a in new[] { "xrotation", "yrotation", "zrotation", "attach" }) cp.SetAttributeValue(a, "1");
            changed = true;
        }
        return changed;
    }

    // GlobalItemRules and LookRules on every converted item file under <dir>/item. A changed file is saved as the converter saves it
    // (XDocument.Save), which keeps the rest of its bytes; a file without a change is not written. Returns 0.
    public static int FixItemFiles(string dir) {
        int files = 0, changed = 0;
        foreach (var path in Directory.EnumerateFiles(System.IO.Path.Combine(dir, "item"), "*.xml", SearchOption.AllDirectories)) {
            files++;
            var doc = XDocument.Load(path);
            bool any = false;
            long id = long.Parse(System.IO.Path.GetFileNameWithoutExtension(path));
            foreach (var env in doc.Root!.Elements("environment")) any |= GlobalItemRules(env) | LookRules(env, id);
            if (!any) continue;
            doc.Save(path);
            changed++;
        }
        Console.WriteLine($"fix-items: {changed} of {files} item files changed");
        return 0;
    }

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
            RefRule(env);
            if (env.Element("function") is { } fn) BoxRule(fn);
            var tool = env.Element("tool");
            if (tool != null) tool.SetAttributeValue("itemPresetPath", PresetDir(A(tool, "itemPreset")));
            var opt = env.Element("option");
            if (opt != null) {
                if (opt.Attribute("optionLevelFactor") == null) Mv(opt, "optionLevel", opt, "optionLevelFactor");
                else opt.Attribute("optionLevel")?.Remove();
            }
            // before the NA-default fill, which keeps values that are set
            GlobalItemRules(env);
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
            LookRules(env, id); // after the model's customize, before the NA-default fill
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
        "riding" => k.Ridings.TryGetValue(id, out var ride) ? Riding(ride) : null,
        _ => k.Skills.ContainsKey(id) ? Skill(k, id) : k.Glides.TryGetValue(id, out var glide) ? Glide(glide) : null,
    };
    // rides and glide skills follow the items that name them (--add-records), not ids: the convert mode writes none
    public static IEnumerable<long> Ids(Kms k, string kind) => kind switch { "item" => k.Items.Keys, "npc" => k.Npcs.Keys, "quest" => k.QuestData.Keys.Union(k.QuestFiles.Keys), "riding" => Enumerable.Empty<long>(), _ => k.Skills.Keys };

    // ----- rides: KMS-EoS riding/<id>.xml has NA's elements but leaves out attributes and elements at their default (walkSpeed
    // in 609 of 615 files). NA's most common value is not always that default (KMS writes collision height 130 in 185 files;
    // where it has no collision NA has 90 on 102 of 102 shared ids), so FillRide writes NA's value where KMS leaves one out,
    // learned on the main riding elements of the ride ids both have, in place of the NA-default fill. On those 354 ids the
    // fields the ingest maps then equal NA except 1 kfm and 2 rideSummonCastTime.
    public static XDocument Riding(XElement kmsRoot) { var root = Clone(kmsRoot); root.Descendants("passengers").Remove(); return Doc(root); }
    static XElement? MainRide(XElement root) => root.Elements("riding").FirstOrDefault(e => e.Attribute("locale") == null && e.Attribute("feature") == null);
    // TODO: a default needs 90% of the shared cases however few; rideBone, runSpeed, runXSpeed and shadow scale rest on 1 to 5 (no shipped ride uses them), add a minimum count when one does
    public static Dictionary<string, string> RideDefaults(Kms k, Na na) {
        var t = new Dictionary<string, Dictionary<string, int>>();
        void Walk(XElement n, XElement? km, string p) {
            foreach (var a in n.Attributes().Where(a => km?.Attribute(a.Name) == null)) {
                if (!t.TryGetValue(p + "@" + a.Name.LocalName, out var d)) t[p + "@" + a.Name.LocalName] = d = new();
                d[a.Value] = d.GetValueOrDefault(a.Value) + 1;
            }
            foreach (var c in n.Elements()) Walk(c, km?.Element(c.Name), p + "/" + c.Name.LocalName);
        }
        foreach (long id in na.Ids["riding"].OrderBy(x => x))
            if (k.Ridings.TryGetValue(id, out var kr) && MainRide(kr) is { } km && MainRide(Kms.Parse(na.Get(Layout.Path("riding", id))).Root!) is { } n) Walk(n, km, "");
        return t.Where(e => e.Value.Values.Max() >= 0.9 * e.Value.Values.Sum()).ToDictionary(e => e.Key, e => e.Value.MaxBy(v => v.Value).Key);
    }
    public static void FillRide(XDocument doc, Dictionary<string, string> defaults) {
        foreach (var r in doc.Root!.Elements("riding"))
            foreach (var (key, v) in defaults) {
                int at = key.IndexOf('@');
                var e = key[..at].Split('/', StringSplitOptions.RemoveEmptyEntries).Aggregate(r, Sub);
                if (e.Attribute(key[(at + 1)..]) == null) e.SetAttributeValue(key[(at + 1)..], v);
            }
    }
    // KMS keeps passengers inline, NA in riding/passenger/<id>.xml rows that always carry these attributes; a missing one is
    // Ride_Idle_A for rideAniPC (as on the ride), "0,0,0" for a position, else "". Then 27 of the 29 NA passenger files equal
    // the KMS rows of the main riding element; 2 differ only in NA's "0,0,00".
    static readonly string[] PassengerAttrs = { "passengerid", "rideBone", "rideTranslation", "rideRotation", "rideTranslation2", "rideAniPC", "rideAniPC2",
        "rideAniPC_Idle", "rideAniPC_Run", "rideAniPC_Jump", "rideAniPC_SP_Idle", "rideAniPC_SP_Run", "rideAniPC_SP_Jump", "nameTagOffsetY" };
    public static XDocument? Passengers(Kms k, long id) => k.Ridings.TryGetValue(id, out var r) ? Passengers(r, id) : null;
    public static XDocument? Passengers(XElement kmsRoot, long id) {
        var rows = (MainRide(kmsRoot)?.Descendants("passenger") ?? Enumerable.Empty<XElement>()).Select(p => new XElement("ridepassenger", new XAttribute("id", id),
            PassengerAttrs.Select(a => new XAttribute(a, (string?)p.Attribute(a) ?? (a == "rideAniPC" ? "Ride_Idle_A" : a.Contains("Translation") || a.Contains("Rotation") ? "0,0,0" : ""))),
            p.Attributes().Where(a => !PassengerAttrs.Contains(a.Name.LocalName)).Select(a => new XAttribute(a)))).ToList();
        return rows.Count == 0 ? null : Doc(new XElement("ms2", rows));
    }

    // ----- glide skills: KMS-EoS keeps an air mount's flight in a table/glidedata.xml row; NA and KMS-v61 keep a
    // skill/80/<id>.xml per glide. 146 of KMS-v61's 149 glide skills share GlideBody; their glideProperty is the glidedata row
    // scaled (equal on 151 of the 154 ids both have; 3 have newer animation names), and a row's nameTagOffset is the
    // skill's offsetNameTag and offsetHPBar (2 of the other 3; the third adds allowMapleSurvival). NA's 148 8001xxxx glide
    // skills name their NA item in beginCondition (rank 3 or 4) and in targetItem (count 0), and 34 of them are this body
    // plus those item elements; the KMS body has no item.
    static readonly (string Attr, double Scale)[] GlideScale = { ("gravity", 10), ("heightLimit", 1), ("horizontalAccelerate", 100), ("horizontalVelocity", 100),
        ("verticalAccelerate", 100), ("verticalVelocity", 100), ("verticalVibrateAmplitude", 100), ("verticalVibrateFrequency", 1000), ("glideHorizontalAccelerateLimit", 100) };
    const string GlideBody = """
        <ms2 feature="">
            <basic>
                <ui icon="" movie="" exceptUI="0" attackType="0" />
                <kinds type="3" subType="7" rangeType="0" state="gosGlide" continue="0" spRecoverySkill="0" element="0" offsetNameTag="0" immediateActive="0" weaponDependency="0" unrideOnUse="1" releaseObjectWeapon="1" releaseStunState="0" />
                <stateAttr battle="0" speedRank="0" superArmor="0" autoTargeting="1" autoTargetingType="0" moveType="0" notKeepEffect="0" keyIsDown="0" useDefaultSkill="0" useInGameTime="0" cooldownGroupID="0" clearCooldownFromPVPZone="0" ignoreReduceCooldown="1" />
            </basic>
            <level value="1">
                <beginCondition level="1" gender="2" target="0" probability="1" invokeEffectFactor="1" cooldownTime="0" useCoolDownShowUI="0" allowDeadState="0" requireDurationWithoutDamage="0" requireDurationWithoutMove="0" useTargetCountFactor="0" onlyShadowWorld="0" onlyFlyableMap="1" applyPvPZoneType="0" allowBattleRidingState="0" onlyBattleRidingState="0">
                    <stat hp="1" />
                </beginCondition>
                <changeSkill changeSkillCheckEffectID="" changeSkillCheckEffectLevel="" changeSkillCheckEffectOverlapCount="" changeSkillID="" changeSkillLevel="" originSkillID="0" originSkillLevel="1" />
                <autoTargeting autoTargetingMaxDegree="210" autoTargetingMaxDistance="0" autoTargetingMaxHeight="0" autoTargetUseMove="0" />
                <push up="0" back="0" />
                <consume money="0">
                    <stat />
                </consume>
                <targetItem />
                <upgrade />
                <detectProperty includeCaster="0" rangeType="" distance="0" rangeAdd="0.000000,0.000000,0.000000" rangeOffset="0.000000,0.000000,0.000000" rangeZRotateDegree="0" height="0" width="0" endWidth="0" applyTarget="0" castTarget="0" sensorStartDelay="0" sensorSplashStartDelay="0" />
                <sensorProperty includeCaster="0" rangeType="" distance="0" rangeAdd="0.000000,0.000000,0.000000" rangeOffset="0.000000,0.000000,0.000000" rangeZRotateDegree="0" height="0" width="0" endWidth="0" applyTarget="0" castTarget="0" sensorStartDelay="0" sensorSplashStartDelay="0" />
                <glideProperty />
                <combo comboSkill="0" chargingSkill="0" comboOriginSkill="0" inputSkill="" outputSkill="" />
                <recoveryProperty spValue="0" spRate="0" />
                <motion>
                    <motionProperty sequenceName="" selfInputCombo="" sequenceSpeed="1" motionEffect="" loopEffect="" endEffect="" splashInvokeEffect="" splashEndEffect="" strTagEffects="" IgnoreOptimalHideCastEffect="false" IgnoreOptimalHideEndEffect="false" IgnoreOptimalHideSplashInvokeEffect="false" IgnoreOptimalHideSplashEndEffect="false" weaponSequenceName="" keyMove="0" movetype="0" moveangle="0" movevangle="0" movedistance="0" moveEaseType="0" moveMid="0" moveHeight="0" hide="0" ignoreCollisionGroups="20" holdWeapon="1" loopCount="0" pushCylinderVelH="0" pushCylinderVelV="0" pushCylinderOuter="0" pushCylinderInner="0" pushCylinderHeight="0" faceTarget="1" doesPlayIdleOnSkillEnd="true" startNoneBlendDelay="0" />
                </motion>
            </level>
        </ms2>
        """;
    public static XDocument Glide(XElement row) {
        var root = XElement.Parse(GlideBody);
        var gp = root.Descendants("glideProperty").Single();
        foreach (var (a, scale) in GlideScale)
            if (a != "glideHorizontalAccelerateLimit" || row.Attribute(a) != null)
                gp.SetAttributeValue(a, (double.Parse((string?)row.Attribute(a) ?? "0", Inv) * scale).ToString("0.######", Inv));
        gp.SetAttributeValue("bEffect", "1");
        foreach (var a in new[] { "effectRun", "effectIdle", "aniIdle", "aniLeft", "aniRight", "aniRun" }) gp.SetAttributeValue(a, A(row, a));
        gp.SetAttributeValue("onGroundDisable", (string?)row.Attribute("onGroundDisable") ?? "0");
        if (row.Attribute("nameTagOffset") is { } off)
            foreach (var a in new[] { "offsetNameTag", "offsetHPBar" }) root.Descendants("kinds").Single().SetAttributeValue(a, off.Value);
        return Doc(root);
    }

    // ----- pets: KMS-EoS petproperty rows have no npcID and no skill child; NA writes npcID = code (126 of the 127 codes both
    // have) after isCollectPreview and <skill id="0" level="0" /> (127 of 127), the rest as KMS (122 of 127; the other 5 are
    // NA locale rows). The NA table text stays byte-identical; the rows go before </ms2>.
    public static string AddPetRows(string naTable, IEnumerable<XElement> kmsRows) {
        string eol = naTable.Contains("\r\n") ? "\r\n" : "\n";
        var add = new StringBuilder();
        foreach (var r in kmsRows) {
            var attrs = r.Attributes().Where(a => a.Name != "npcID").Select(a => a.ToString()).ToList();
            int at = attrs.FindIndex(a => a.StartsWith("isCollectPreview=")) + 1;
            attrs.Insert(at > 0 ? at : 1, new XAttribute("npcID", A(r, "code")).ToString());
            add.Append($"\t<pets {string.Join(" ", attrs)}>{eol}\t\t<skill id=\"0\" level=\"0\" />{eol}\t</pets>{eol}");
        }
        int end = naTable.LastIndexOf("</ms2>", StringComparison.Ordinal);
        return naTable[..end] + add + naTable[end..];
    }

    // ----- animation keys (IM-04): a model's <kfm name> block of an anikeytext.xml text, found in any case, with its line end.
    public static string? KfmBlock(string text, string kfm) {
        var m = System.Text.RegularExpressions.Regex.Match(text, "\t<kfm name=\"" + System.Text.RegularExpressions.Regex.Escape(kfm) + "\">.*?\t</kfm>\r?\n",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? m.Value : null;
    }
    // KMS anikeyinfo/<kfm>.xml as NA writes the block: a start key at 0, then the end key, then the other KMS keys in their order
    // (NA keeps the ' -GlobalRatio' export keys, 205 of 205), times with 6 decimals (KMS writes 4). On the 2,256 models both
    // have, this gives NA's seq ids, names, keys and times (within 0.00005) on 2,232, and NA's key order on 1,018.
    public static string AniKeyBlock(string name, string anikeyinfo, string eol) {
        var sb = new StringBuilder($"\t<kfm {new XAttribute("name", name)}>{eol}");
        foreach (var seq in XDocument.Parse(anikeyinfo).Root!.Elements("seq")) {
            sb.Append($"\t\t<seq {new XAttribute("id", A(seq, "id"))} {new XAttribute("name", A(seq, "name"))}>{eol}\t\t\t<key name=\"start\" time=\"0.000000\" />{eol}");
            foreach (var key in seq.Elements("key").Where(e => A(e, "name") != "start").OrderBy(e => A(e, "name") == "end" ? 0 : 1))
                sb.Append($"\t\t\t<key {new XAttribute("name", A(key, "name"))} time=\"{double.Parse(A(key, "time"), Inv).ToString("F6", Inv)}\" />{eol}");
            sb.Append($"\t\t</seq>{eol}");
        }
        return sb.Append($"\t</kfm>{eol}").ToString();
    }
    public static string InsertBefore(string text, string closing, string add) { int end = text.LastIndexOf(closing, StringComparison.Ordinal); return text[..end] + add + text[end..]; }

    // (kind, id) of the record each converted mount or pet under <dir>/item names (the param1 rule): riding, skill or pet.
    public static IEnumerable<(string Kind, long Id)> RecordRefs(string dir) {
        foreach (var path in Directory.EnumerateFiles(System.IO.Path.Combine(dir, "item"), "*.xml", SearchOption.AllDirectories))
            foreach (var prop in XDocument.Load(path).Descendants("property"))
                if (RefOf(A(prop, "type"), A(prop, "param1"), out var t, out int id))
                    yield return (t.El == "ride" ? "riding" : t.El, id);
    }

    // ----- effect XMLs (IT-08, BT-125): the client draws a glide, a ride's effects, a cape's or a skill's effect from the effect
    // XML a record names, in an attribute or a part of a tag list (strTagEffects="e0=CH/....xml,e1=..."); a bare name is a ride's,
    // under Item/Ride (NA's riding files: 28 of 28). An NPC's aiInfo path is the server's AI file.
    public static IEnumerable<string> EffectRefs(XElement root) => root.DescendantsAndSelf().Where(e => e.Name != "aiInfo").Attributes()
        .SelectMany(a => a.Value.Split(',')).Select(p => System.Text.RegularExpressions.Regex.Replace(p.Trim(), "^[A-Za-z]+[0-9]+=", ""))
        .Where(p => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).Select(p => Layout.Norm(p).TrimStart('/').ToLowerInvariant())
        .Select(p => "effect/" + (p.Contains('/') ? p : "item/ride/" + p));
    // The KMS effect XMLs that the records under <dir> (item, npc, quest, skill, riding) name and NA lacks, written under <dir>/effect
    // with KMS's bytes (no effect XML of either client names another XML); returns the paths written and those neither client has.
    public static (List<string> Written, List<string> Missing) CopyEffects(string dir, Func<string, bool> naHas, IReadOnlyDictionary<string, byte[]> kms) {
        var named = Layout.Kinds.Select(kind => System.IO.Path.Combine(dir, kind)).Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.xml", SearchOption.AllDirectories)).SelectMany(p => EffectRefs(XDocument.Load(p).Root!))
            .Where(x => !naHas(x)).Distinct().Order(StringComparer.Ordinal).ToList();
        var written = named.Where(kms.ContainsKey).ToList();
        foreach (var x in written) {
            string to = System.IO.Path.Combine(dir, x);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(to)!);
            File.WriteAllBytes(to, kms[x]);
        }
        return (written, named.Except(written).ToList());
    }

    // ----- copies (IM-03): a KMS mount's own skill. The id is the item id's block and last three digits: ground mounts
    // 506b0xxx -> 800(b+1)xxx, air mounts 404b0xxx -> 801(b+1)xxx; -1 outside. NA, KMS and the 1.2.2 pack use 80000001-80000343,
    // 80008001-80008006 and 80010001-80010155 of these blocks.
    public static long SummonCopy(long item) => item is >= 50600000 and < 50670000 && item % 10000 < 1000 ? 80001000 + (item - 50600000) / 10000 * 1000 + item % 1000 : -1;
    public static long GlideCopy(long item) => item is >= 40400000 and < 40490000 && item % 10000 < 1000 ? 80011000 + (item - 40400000) / 10000 * 1000 + item % 1000 : -1;
    public static bool IsCopy(long skill) => skill is >= 80001000 and <= 80007999 or >= 80011000 and <= 80019999;
    // A ground mount's summon skill: the Global body with the item's icon (as Global writes it), its own id as jump (as in
    // each of the 299 Global gosSummon skills that has one) and the item as targetItem.
    public static XDocument Summon(XDocument global, long id, long item, string icon) {
        var doc = new XDocument(global);
        doc.Descendants("ui").Single().SetAttributeValue("icon", icon);
        doc.Descendants("kinds").Single().SetAttributeValue("jump", id);
        doc.Descendants("targetItem").Single().Element("item")!.SetAttributeValue("id", item);
        return doc;
    }
    // The NA glide skill names another item in beginCondition: the client refuses it to a player without that item.
    public static bool GatedElsewhere(XDocument naSkill, long item) => naSkill.Descendants("beginCondition").Elements("item").Any(e => A(e, "id") != item.ToString());
    // An air mount's glide skill: the KMS-v61 body (Glide) with NA's animations. Of the 29 gated NA skills the KMS mounts
    // name, the glidedata row equals NA's glideProperty but leaves aniLeft empty on 24, and 80010067's FlyType029 names
    // are not in the NA client (NA: FlyType001_Idle_A, FlyType001_Run_A).
    public static XDocument GlideFor(XElement row, XDocument naSkill) {
        var doc = Glide(row);
        var gp = doc.Descendants("glideProperty").Single();
        var na = naSkill.Descendants("glideProperty").Single();
        foreach (var a in new[] { "aniIdle", "aniLeft", "aniRight", "aniRun" }) gp.SetAttributeValue(a, A(na, a));
        return doc;
    }
    // The item file names its copy as Global's mounts name their skill: a ground mount skill@skillID and subtype 12 (all 329
    // Global ground mounts), an air mount skill@skillID and param1 (IM-01's link). Saved as FixItemFiles saves; true when
    // the file changed, so a second run writes nothing.
    public static bool PointAt(string path, long skill) {
        var doc = XDocument.Load(path);
        bool changed = false;
        void Set(XElement e, string name, string value) {
            if ((string?)e.Attribute(name) == value) return;
            e.SetAttributeValue(name, value);
            changed = true;
        }
        foreach (var env in doc.Root!.Elements("environment")) {
            if (env.Element("property") is not { } prop || A(prop, "type") is not ("9" or "5")) continue;
            if (A(prop, "type") == "9") Set(prop, "subtype", "12"); else Set(prop, "param1", skill.ToString());
            Set(Sub(env, "skill"), "skillID", skill.ToString());
        }
        if (changed) doc.Save(path);
        return changed;
    }
}

// ---------- homes (IB-02): the parts of KmsHomes the self-test checks ----------
static class Homes {
    public const string FinalPath = "table/Server/individualItemDrop_Final.xml";
    public static readonly string[] BoxFns = { "OpenItemBox", "OpenItemBoxWithKey", "SelectItemBox", "OpenGachaBox" };
    public sealed record Row(string Verdict, long Home, string Reason);
    static long L(XElement e, string a) => long.TryParse((string?)e.Attribute(a), out long v) ? v : 0;
    static string Locale(XElement e) => (string?)e.Attribute("locale") ?? "";
    static bool KrOrNone(XElement e) => Locale(e) is "" or "KR";
    static bool NoneOrNa(XElement e) => Locale(e) is "" or "NA";

    // tools/data/kms-homes.tsv: id, verdict (keep, twin, copy or drop), home (the twin's item, Global or a shipped KMS one, or the
    // copy's id), reason
    public static Dictionary<long, Row> ReadTable(string text) {
        var lines = text.Replace("\r\n", "\n").Split('\n').Where(l => l.Length > 0).Select(l => l.Split('\t')).ToList();
        if (lines.Count == 0 || string.Join("\t", lines[0]) != "id\tverdict\thome\treason") throw new InvalidDataException("kms-homes.tsv: the header is not id, verdict, home, reason");
        var rows = new Dictionary<long, Row>();
        foreach (var r in lines.Skip(1)) {
            long id = 0, home = 0;
            bool ok = r.Length == 4 && long.TryParse(r[0], out id) && r[1] is "keep" or "twin" or "copy" or "drop" && r[3].Trim().Length > 0
                && (r[1] is "twin" or "copy" ? long.TryParse(r[2], out home) && home > 0 : r[2] == "");
            if (!ok || !rows.TryAdd(id, new Row(r[1], home, r[3]))) throw new InvalidDataException("kms-homes.tsv: a bad or repeated row: " + string.Join("\t", r));
        }
        return rows;
    }
    // dropBoxID -> dropBox element of a drop table (the first, should an id repeat)
    public static Dictionary<long, XElement> Boxes(string text) {
        var boxes = new Dictionary<long, XElement>();
        foreach (var e in XDocument.Parse(text).Root!.Elements("dropBox")) boxes.TryAdd(L(e, "dropBoxID"), e);
        return boxes;
    }
    // KMS's rows of a box: the KR and unlocalized groups and rows
    public static IEnumerable<XElement> Kept(XElement box) => box.Elements("group").Where(KrOrNone).SelectMany(g => g.Elements("v").Where(KrOrNone));
    public static HashSet<long> Ids(IEnumerable<XElement> rows) => rows.SelectMany(v => new[] { L(v, "itemID"), L(v, "itemID2") }).Where(x => x > 0).ToHashSet();
    // the pack's ids of a box, as the server reads Final: unlocalized and NA groups and rows
    public static HashSet<long> NaIds(XElement? box) => box == null ? new() : Ids(box.Elements("group").Where(NoneOrNa).SelectMany(g => g.Elements("v").Where(NoneOrNa)));
    // randomBoxID -> individualDropBoxID, a KR row (locale KR or a KR season) over the others
    public static Dictionary<long, long> GachaBoxes(string text) {
        var rows = new Dictionary<long, (long Box, bool Kr)>();
        foreach (var e in XDocument.Parse(text).Root!.Elements("randomBox")) {
            bool kr = Locale(e) == "KR" || ((string?)e.Attribute("randomBoxSeasonKR") ?? "") != "";
            if (!rows.TryGetValue(L(e, "randomBoxID"), out var have) || kr && !have.Kr) rows[L(e, "randomBoxID")] = (L(e, "individualDropBoxID"), kr);
        }
        return rows.ToDictionary(r => r.Key, r => r.Value.Box);
    }
    // an item function as the server reads it: OpenItemBox's box from KMS's param1, else parameter field 4; SelectItemBox field 2;
    // OpenItemBoxWithKey field 1 (the key) and 6; OpenGachaBox field 1 (the gacha)
    public static (string Name, long Box, long Key, long Gacha) Fn(XElement? fn) {
        string name = (string?)fn?.Attribute("name") ?? "";
        var p = ((string?)fn?.Attribute("parameter") ?? "").Split(',');
        long F(int i) => i < p.Length && long.TryParse(p[i], out long v) ? v : 0;
        return name switch {
            "OpenItemBox" => (name, L(fn!, "param1") is > 0 and var b ? b : F(3), 0, 0),
            "SelectItemBox" => (name, F(1), 0, 0),
            "OpenItemBoxWithKey" => (name, F(5), F(0), 0),
            "OpenGachaBox" => (name, 0, 0, F(0)),
            _ => (name, 0, 0, 0),
        };
    }
    // a met id's verdict: kms for a KMS record the pack ships; else its table row (an id the pack lacks takes only a twin row, the
    // item the selection ships in its place, else refused); else keep for a Global item and unshipped for an id the pack lacks
    // (the row goes, as the server skips it, DR-01)
    public static string Verdict(bool global, bool shipped, Row? row) =>
        !global && shipped ? "kms" : row == null ? (global ? "keep" : "unshipped") : global || row.Verdict == "twin" ? row.Verdict : "refused";
    // a record's genderLimit (0 male, 1 female, 2 both), absent when it has none; a twin must be for the KMS item's gender or both
    public static int Gender(XElement? record, int absent) =>
        int.TryParse(record?.Descendants("limit").Select(l => (string?)l.Attribute("genderLimit")).FirstOrDefault(g => g != null), out int v) ? v : absent;
    public static bool GenderOk(int kms, int pack) => pack == 2 || pack == kms;
    // a KMS item's KR environment, else its first
    public static XElement? KrEnv(XElement item) => item.Elements("environment").FirstOrDefault(e => Locale(e) == "KR") ?? item.Elements("environment").FirstOrDefault();
    // an item's look: its preset, else its icon, else its id (KMS: the KR environment, else the first; NA: the icon file's id)
    public static long KmsLook(XElement item, long id) {
        var env = KrEnv(item);
        long preset = env?.Element("tool") is { } t ? L(t, "itemPreset") : 0, icon = env?.Element("property") is { } p ? L(p, "slotIcon") : 0;
        return preset > 0 ? preset : icon > 0 ? icon : id;
    }
    public static long NaLook(XDocument doc, long id) {
        long preset = doc.Descendants("tool").Select(t => L(t, "itemPreset")).FirstOrDefault(x => x > 0);
        var icon = System.Text.RegularExpressions.Regex.Match((string?)doc.Descendants("property").FirstOrDefault()?.Attribute("slotIcon") ?? "", "([0-9]{8})[.]png");
        return preset > 0 ? preset : icon.Success ? long.Parse(icon.Groups[1].Value) : id;
    }
    public static bool NaLocal(XDocument doc) => doc.Root!.Elements("environment").Any(NoneOrNa);
    static string Family(string fn) => BoxFns.Contains(fn) ? "box" : fn;
    // the gate's test: KMS's record of a Global id is another item when its look or function family differs, or its box gives other ids
    public static bool Flag(long kmsLook, long naLook, string kmsFn, string naFn, HashSet<long> kmsBox, HashSet<long> naBox) =>
        kmsLook != naLook || Family(kmsFn) != Family(naFn) || Family(kmsFn) == "box" && !kmsBox.SetEquals(naBox);
    public static List<long> Unreviewed(IEnumerable<long> reached, IReadOnlyDictionary<long, Row> table, Func<long, bool> flagged) => reached.Where(p => !table.ContainsKey(p) && flagged(p)).ToList();
    // a box's (or a gacha's) home: + 90000, when the id is below 10000 in its block of 100000 and the home is free; else -1
    public static long BoxHome(long box, Func<long, bool> taken) => box % 100000 < 10000 && !taken(box + 90000) ? box + 90000 : -1;
    // a copy's home keeps the first three digits (ItemType and the badge kind read them), has 9 as its fourth and is free
    public static bool ItemHomeOk(long src, long home, Func<long, bool> taken) => home != src && home / 100000 == src / 100000 && home / 10000 % 10 == 9 && !taken(home);
    // KMS's box under its home: the KR and unlocalized groups and rows without the KR mark, the ids mapped; a row whose ids all
    // map to 0 goes, and its weight with it, so the other rows keep KMS's odds; empty groups go; null when no row is left.
    // The ingest keys a box's groups by id and a group's rows by itemID (the last one stays), so groups of one id become the
    // first (a KMS weapon box writes one group per class; it then gives the class weapon, not the last group's), a pair whose
    // itemID goes keeps its itemID2 as itemID, and a row equal to an earlier one of its group goes (the M and F re-issues of
    // one unisex Global item, which keeps KMS's odds per gender). classGap: a smart-drop group lost an id and kept other rows.
    public static XElement? HomeBox(XElement kms, long home, Func<long, long> map, out int removed, out bool classGap) {
        var box = new XElement(kms);
        box.SetAttributeValue("dropBoxID", home);
        removed = 0;
        classGap = false;
        var first = new Dictionary<string, XElement>();
        foreach (var g in box.Elements("group").ToList()) {
            if (!KrOrNone(g)) { g.Remove(); continue; }
            g.Attribute("locale")?.Remove();
            if (first.TryGetValue((string?)g.Attribute("dropGroupID") ?? "", out var into)) { into.Add(g.Elements("v")); g.Remove(); }
            else first[(string?)g.Attribute("dropGroupID") ?? ""] = g;
        }
        foreach (var g in box.Elements("group").ToList()) {
            bool lost = false;
            foreach (var v in g.Elements("v").ToList()) {
                if (!KrOrNone(v)) { v.Remove(); continue; }
                v.Attribute("locale")?.Remove();
                var ids = new[] { "itemID", "itemID2" }.Where(a => L(v, a) > 0).Select(a => map(L(v, a))).ToList();
                lost |= ids.Contains(0);
                if (ids.Count > 0 && ids.All(x => x == 0)) { v.Remove(); removed++; continue; }
                ids.RemoveAll(x => x == 0);
                if (ids.Count > 0) { v.SetAttributeValue("itemID", ids[0]); v.SetAttributeValue("itemID2", ids.Count > 1 ? ids[1] : null); }
                if (v.ElementsBeforeSelf("v").Any(e => XNode.DeepEquals(e, v))) v.Remove();
            }
            if (!g.Elements("v").Any()) g.Remove();
            else if (lost && L(g, "smartDropRate") > 0) classGap = true;
        }
        return box.Elements("group").Any() ? box : null;
    }
    // a box element as Final writes its boxes: two spaces in, two per level, the file's line end
    public static string Block(XElement e, string eol) {
        var sb = new StringBuilder();
        using (var w = System.Xml.XmlWriter.Create(sb, new System.Xml.XmlWriterSettings { OmitXmlDeclaration = true, Indent = true, IndentChars = "  ", NewLineChars = eol })) e.WriteTo(w);
        return string.Concat(sb.ToString().Split(eol).Select(l => "  " + l + eol));
    }
    // the functions of a record name the homes (fields as in Fn, and KMS's param1); true on a change
    public static bool Point(XDocument doc, Func<long, long> box, Func<long, long> key, Func<long, long> gacha) {
        bool changed = false;
        foreach (var fn in doc.Root!.Elements("environment").Select(e => e.Element("function")).OfType<XElement>()) {
            string name = (string?)fn.Attribute("name") ?? "";
            var to = name switch {
                "OpenItemBox" => new[] { (3, box) }, "SelectItemBox" => new[] { (1, box) },
                "OpenItemBoxWithKey" => new[] { (0, key), (5, box) }, "OpenGachaBox" => new[] { (0, gacha) }, _ => Array.Empty<(int, Func<long, long>)>(),
            };
            var p = ((string?)fn.Attribute("parameter") ?? "").Split(',');
            foreach (var (i, f) in to)
                if (i < p.Length && long.TryParse(p[i], out long id) && id > 0 && f(id) != id) { p[i] = f(id).ToString(); fn.SetAttributeValue("parameter", string.Join(",", p)); changed = true; }
            if (name == "OpenItemBox" && L(fn, "param1") is > 0 and var p1 && box(p1) != p1) { fn.SetAttributeValue("param1", box(p1)); changed = true; }
        }
        return changed;
    }
    // Point on an item file, saved as PointAt saves (only on a change, so a second run writes nothing)
    public static bool PointFile(string path, Func<long, long> box, Func<long, long> key, Func<long, long> gacha) {
        var doc = XDocument.Load(path);
        if (!Point(doc, box, key, gacha)) return false;
        doc.Save(path);
        return true;
    }
    // a copy's references to its own source (tool@itemPreset, basic@originID; the Cupcake Puff set has both) name the copy
    public static void SelfRefs(XDocument doc, long src, long home) {
        foreach (var a in doc.Descendants("tool").Select(t => t.Attribute("itemPreset")).Concat(doc.Descendants("basic").Select(b => b.Attribute("originID"))))
            if (a != null && a.Value == src.ToString()) a.Value = home.ToString();
    }
    // gacha_info.xml text: the rows of boxOf name their box's home; each Global gacha of homes gets a home row, its own line with
    // the home ids, before </ms2> (in place when a run already added it). Returns the text and the rows changed or added.
    public static (string Text, int Rows) GachaRows(string text, IReadOnlyDictionary<long, long> boxOf, IReadOnlyDictionary<long, (long Home, long Box)> homes) {
        static string Set(string line, string a, long v) => System.Text.RegularExpressions.Regex.Replace(line, $"(?<=\\s){a}=\"[0-9]*\"", $"{a}=\"{v}\"");
        var lines = text.Split('\n').ToList();
        var at = new Dictionary<long, int>();
        for (int i = 0; i < lines.Count; i++)
            if (System.Text.RegularExpressions.Regex.Match(lines[i], "<randomBox\\s[^>]*?randomBoxID=\"([0-9]+)\"") is { Success: true } m) at.TryAdd(long.Parse(m.Groups[1].Value), i);
        int rows = 0;
        var added = new List<string>();
        foreach (var (g, b) in boxOf) {
            if (!at.TryGetValue(g, out int i)) throw new InvalidDataException($"gacha_info.xml has no row {g}");
            if (Set(lines[i], "individualDropBoxID", b) is var s && s != lines[i]) { lines[i] = s; rows++; }
        }
        foreach (var (g, (h, b)) in homes) {
            if (!at.TryGetValue(g, out int i)) throw new InvalidDataException($"gacha_info.xml has no row {g}");
            string row = Set(Set(lines[i], "randomBoxID", h), "individualDropBoxID", b);
            if (!at.TryGetValue(h, out int j)) { added.Add(row); rows++; }
            else if (lines[j] != row) { lines[j] = row; rows++; }
        }
        int close = lines.FindLastIndex(l => l.Trim() == "</ms2>");
        if (added.Count > 0 && close < 0) throw new InvalidDataException("gacha_info.xml has no </ms2> line");
        lines.InsertRange(Math.Max(close, 0), added);
        return (string.Join("\n", lines), rows);
    }
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
        string boxFile = System.IO.Path.Combine(tmp, "item", "1", "10", "3.xml");
        File.WriteAllBytes(boxFile, Encoding.UTF8.GetBytes("<ms2>\r\n<function name=\"OpenItemBox\" param1=\"330000924\" parameter=\"\" />\r\n</ms2>"));
        // param1 rule (IM-01): ride, glide skill and pet id per item type; other types and a 0 or missing param1 untouched
        var refs = XElement.Parse("<r><environment><property type=\"9\" param1=\"50600118\"/></environment><environment><property type=\"5\" param1=\"80010023\"/><skill skillID=\"0\" skillLevel=\"1\"/></environment>"
            + "<environment><property type=\"11\" param1=\"60000010\"/></environment><environment><property type=\"1\" param1=\"7\"/></environment><environment><property type=\"9\" param1=\"0\"/></environment></r>");
        foreach (var env in refs.Elements()) Conv.RefRule(env);
        var envs = refs.Elements().ToList();
        Check("ref rule: type 9 ride, 5 skill, 11 pet from param1; others untouched",
            (string?)envs[0].Element("ride")?.Attribute("rideMonster") == "50600118" && (string?)envs[1].Element("skill")?.Attribute("skillID") == "80010023"
            && (string?)envs[1].Element("skill")?.Attribute("skillLevel") == "1" && (string?)envs[2].Element("pet")?.Attribute("petID") == "60000010"
            && envs[3].Elements().Count() == 1 && envs[4].Elements().Count() == 1);
        const string RefXml = "<ms2>\r\n<environment feature=\"\" locale=\"\">\r\n<property rank=\"3\" type=\"9\" param1=\"50600118\" category=\"Ride\">\r\n</property>\r\n<pet petID=\"0\" />\r\n<ride rideBuildScale=\"1\" rideMonster=\"0\" />\r\n<skill skillID=\"0\" skillLevel=\"1\" />\r\n</environment>\r\n"
            + "<environment feature=\"X\" locale=\"\">\r\n<property type=\"11\" param1=\"60000062\" />\r\n<pet petID=\"0\" />\r\n<ride rideMonster=\"0\" />\r\n</environment>\r\n</ms2>";
        Check("ref text fix: each environment's own target only, set values and other types kept",
            Conv.FixRefsText(RefXml) == RefXml.Replace("rideBuildScale=\"1\" rideMonster=\"0\"", "rideBuildScale=\"1\" rideMonster=\"50600118\"").Replace("param1=\"60000062\" />\r\n<pet petID=\"0\"", "param1=\"60000062\" />\r\n<pet petID=\"60000062\"")
            && Conv.FixRefsText("<environment><property type=\"5\" param1=\"80010044\"/><skill skillID=\"80010001\"/></environment>") == "<environment><property type=\"5\" param1=\"80010044\"/><skill skillID=\"80010001\"/></environment>"
            && Conv.FixRefsText("<environment><property type=\"1\" param1=\"7\"/><skill skillID=\"0\"/></environment>") == "<environment><property type=\"1\" param1=\"7\"/><skill skillID=\"0\"/></environment>");
        Conv.FixSkinFiles(tmp);
        byte[] fixedBytes = File.ReadAllBytes(withBom);
        Check("fix-skin keeps the byte-order mark and line ends",
            fixedBytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF })
            && Encoding.UTF8.GetString(fixedBytes, 3, fixedBytes.Length - 3) == "<ms2>\r\n<property skinType=\"2\" skin=\"1\" />\r\n</ms2>");
        Check("fix-skin leaves other files byte-identical", File.ReadAllBytes(noBom).SequenceEqual(plainBytes));
        Check("fix-skin also applies the box rule",
            File.ReadAllText(boxFile) == "<ms2>\r\n<function name=\"OpenItemBox\" param1=\"330000924\" parameter=\"0,1,0,330000924\" />\r\n</ms2>");
        Directory.Delete(tmp, recursive: true);

        // box rule (IB-01): OpenItemBox param1 (drop box) and param2 (boxes per open) become parameter="0,1,0,<box>[,<count>]"
        var boxOne = XElement.Parse("<function name=\"OpenItemBox\" param1=\"330000924\" />");
        var boxTwo = XElement.Parse("<function name=\"OpenItemBox\" param1=\"330002801\" param2=\"5\" parameter=\"\" />");
        var gachaFn = XElement.Parse("<function name=\"OpenGachaBox\" parameter=\"1001025,22101003\" />");
        Conv.BoxRule(boxOne);
        Conv.BoxRule(boxTwo);
        Conv.BoxRule(gachaFn);
        Check("box rule: param1 (and param2) become the parameter, OpenGachaBox untouched",
            (string?)boxOne.Attribute("parameter") == "0,1,0,330000924" && (string?)boxTwo.Attribute("parameter") == "0,1,0,330002801,5"
            && (string?)gachaFn.Attribute("parameter") == "1001025,22101003");
        Check("box text fix: parameter filled in OpenItemBox tags, other attributes stay in place",
            Conv.FixBoxText("<function name=\"OpenItemBox\" param1=\"330000924\" parameter=\"\" onlyShadowContinent=\"0\" />")
                == "<function name=\"OpenItemBox\" param1=\"330000924\" parameter=\"0,1,0,330000924\" onlyShadowContinent=\"0\" />"
            && Conv.FixBoxText("<function name=\"OpenItemBox\" param1=\"330002801\" param2=\"5\" parameter=\"\" />")
                == "<function name=\"OpenItemBox\" param1=\"330002801\" param2=\"5\" parameter=\"0,1,0,330002801,5\" />");
        Check("box text fix: a set parameter and other functions stay",
            Conv.FixBoxText("<function name=\"OpenItemBox\" parameter=\"0,1,0,330000216\" />") == "<function name=\"OpenItemBox\" parameter=\"0,1,0,330000216\" />"
            && Conv.FixBoxText("<function name=\"SelectItemBox\" param1=\"1\" parameter=\"\" />") == "<function name=\"SelectItemBox\" param1=\"1\" parameter=\"\" />");

        // KMS-2024 item encoding (IT-01)
        XElement E(string s) => XElement.Parse("<environment>" + s + "</environment>");
        string V(XElement env, string el, string at) => (string?)env.Element(el)?.Attribute(at) ?? "-";
        var act = E("<property rank=\"4\" type=\"24\" subtype=\"0\"/><option constant=\"1\"/>");
        var ins = E("<property rank=\"2\" type=\"23\" param1=\"58\"/><function name=\"OpenInstrument\"/>");
        var etc = E("<property rank=\"3\" type=\"27\"/>");
        var gear = E("<property rank=\"4\" type=\"1\" subtype=\"0\"/><option constantID=\"11150032\" constant=\"1\"/>");
        var naRec = E("<property type=\"10\" subtype=\"20\"/><function name=\"OpenInstrument\" parameter=\"7\"/><option constant=\"2\"/>");
        Check("item rules: Global type and subtype, instrument id, rank as constant",
            Conv.GlobalItemRules(act) && V(act, "property", "type") == "2" && V(act, "property", "subtype") == "14" && V(act, "option", "constant") == "4"
            && Conv.GlobalItemRules(ins) && V(ins, "property", "type") == "10" && V(ins, "property", "subtype") == "20" && V(ins, "function", "parameter") == "58" && V(ins, "option", "constant") == "2"
            && Conv.GlobalItemRules(etc) && V(etc, "property", "type") == "10" && V(etc, "property", "subtype") == "0" && V(etc, "option", "constant") == "3");
        Check("item rules leave gear option ids and Global records alone, and change nothing twice",
            !Conv.GlobalItemRules(gear) && V(gear, "option", "constant") == "1" && !Conv.GlobalItemRules(naRec) && !Conv.GlobalItemRules(act) && !Conv.GlobalItemRules(ins));
        var psItem = E("<property type=\"2\" subtype=\"0\" category=\"PS\"/>");
        var tamingItem = E("<property type=\"2\" category=\"PetTaming\"/>");
        var actionItem = E("<property type=\"2\" subtype=\"14\" category=\"Action\"/>");
        var skbItem = E("<property type=\"2\" subtype=\"8\" category=\"SKB\"/>");
        Check("consumable subtype: a type 2 potion or snare without a subtype gets subtype 2 once; emotes and skill books keep theirs",
            Conv.GlobalItemRules(psItem) && V(psItem, "property", "subtype") == "2" && Conv.GlobalItemRules(tamingItem) && V(tamingItem, "property", "subtype") == "2"
            && !Conv.GlobalItemRules(psItem) && !Conv.GlobalItemRules(actionItem) && V(actionItem, "property", "subtype") == "14" && !Conv.GlobalItemRules(skbItem) && V(skbItem, "property", "subtype") == "8");
        string tmpItems = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kmssplit-items-" + Guid.NewGuid().ToString("N"));
        string itemDir = System.IO.Path.Combine(tmpItems, "item", "2", "02");
        Directory.CreateDirectory(itemDir);
        XDocument Converted(string type, string subtype, string constant) => new(new XDeclaration("1.0", "utf-8", null), new XElement("ms2",
            new XElement("environment", new XAttribute("feature", ""), new XAttribute("locale", ""),
                new XElement("property", new XAttribute("rank", "4"), new XAttribute("type", type), new XAttribute("slotMax", "999"), new XAttribute("subtype", subtype)),
                new XElement("option", new XAttribute("static", "0"), new XAttribute("constant", constant)))));
        string action = System.IO.Path.Combine(itemDir, "20201080.xml"), emote = System.IO.Path.Combine(itemDir, "20200001.xml"), expected = System.IO.Path.Combine(tmpItems, "expected.xml");
        Converted("24", "0", "1").Save(action);
        Converted("2", "14", "4").Save(expected);
        byte[] emoteBytes = Encoding.UTF8.GetBytes("<ms2><environment><property type=\"2\" subtype=\"14\"/></environment></ms2>");
        File.WriteAllBytes(emote, emoteBytes);
        Conv.FixItemFiles(tmpItems);
        Check("fix-items saves a changed file as the converter does", File.ReadAllBytes(action).SequenceEqual(File.ReadAllBytes(expected)));
        Check("fix-items does not write a file without a change", File.ReadAllBytes(emote).SequenceEqual(emoteBytes));
        Directory.Delete(tmpItems, recursive: true);

        // an item's look and the effect XMLs as Global ships them (IT-08): fix-items on a KMS cosmetic left at icon0.png, a capsule
        // with a bare icon name, a hat with capAttach and a hat with userrotation whose CP flags do not let it turn (IT-10), next to
        // files it must leave; the effect XMLs the records name
        string looks = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kmssplit-looks-" + Guid.NewGuid().ToString("N"));
        string Look(long id, string env) {
            string p = System.IO.Path.Combine(looks, Layout.Item(id));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
            File.WriteAllText(p, "<ms2><environment>" + env + "</environment></ms2>");
            return p;
        }
        string cosmeticLook = Look(11220349, "<property skinType=\"2\" slotIcon=\"icon0.png\" slotIconCustom=\"icon0.png\" />");
        string capsuleLook = Look(22001025, "<property type=\"10\" slotIcon=\"newGacha_capsule_ducky\" />");
        string hatLook = Look(11390088, "<property skinType=\"2\" slotIcon=\"./Data/Resource/Image/item/icon/11320083.png\" /><customize color=\"1\" defaultColorIndex=\"7\" rotation=\"1\" capAttach=\"1\" ch0=\"0\" />");
        string turnLook = Look(11320415, "<slots><slot name=\"CP\"><asset name=\"Data/Resource/Model/Item/1/13/11320415_C_CPAutumn02_F.nif\"><userrotation axis=\"x\" default=\"0\" min=\"0\" max=\"0\" /><userrotation axis=\"z\" min=\"-180\" max=\"180\" default=\"0\" /><physx action=\"\" /></asset></slot></slots><customize><CP xrotation=\"0\" yrotation=\"0\" zrotation=\"0\" scale=\"0\" attach=\"0\" /></customize>");
        string[] keptLooks = { Look(11220055, "<property skinType=\"2\" slotIcon=\"./Data/Resource/Image/item/icon/11220055.png\" />"), Look(10100001, "<property type=\"1\" slotIcon=\"icon0.png\" />"),
            Look(10400217, "<property skinType=\"10\" slotIcon=\"./Data/Resource/Image/item/icon/10400217.png\" /><customize scale=\"1\" rotation=\"1\" translation=\"1\" />"),
            Look(11300000, "<slots><slot name=\"CP\"><asset name=\"Data/Resource/Model/Item/1/13/11300004_C_SilverCrown_F.nif\"><userrotation axis=\"z\" min=\"-180\" max=\"180\" default=\"0.000000\" /></asset></slot></slots><customize><CP xrotation=\"1\" yrotation=\"1\" zrotation=\"1\" scale=\"0\" attach=\"1\" /></customize>"),
            Look(11300002, "<slots><slot name=\"CP\"><asset name=\"Data/Resource/Model/Item/1/13/11300002_C_Crown_F.nif\"><userrotation axis=\"z\" min=\"-180\" max=\"180\" default=\"0.000000\" /></asset></slot></slots><customize><CP xrotation=\"1\" yrotation=\"1\" zrotation=\"1\" scale=\"0\" attach=\"0\" /></customize>"),
            Look(11320373, "<slots><slot name=\"CP\"><asset name=\"Data/Resource/Model/Item/1/13/11320373_C_CPUniform01_C.nif\"><physx action=\"\" /></asset></slot></slots><customize><CP xrotation=\"0\" yrotation=\"0\" zrotation=\"0\" scale=\"0\" attach=\"0\" /></customize>") };
        string Snap(IEnumerable<string> files) => string.Join(" ", files.Select(p => Convert.ToBase64String(File.ReadAllBytes(p))));
        string keptBefore = Snap(keptLooks);
        Conv.FixItemFiles(looks);
        string fixedOnce = Snap(new[] { cosmeticLook, capsuleLook, hatLook, turnLook });
        Conv.FixItemFiles(looks);
        bool looksOnce = Snap(new[] { cosmeticLook, capsuleLook, hatLook, turnLook }) == fixedOnce && Snap(keptLooks) == keptBefore;
        string Icon(string p) => (string?)XDocument.Load(p).Descendants("property").Single().Attribute("slotIcon") ?? "-";
        string Cap(string p) => XDocument.Load(p).Descendants("customize").Single().ToString(SaveOptions.DisableFormatting);
        Check("icons: fix-items gives a KMS cosmetic left at icon0.png the icon file of its id and a bare icon name its file, once; a set icon and a plain icon0.png item stay",
            Icon(cosmeticLook) == "./Data/Resource/Image/item/icon/11220349.png" && (string?)XDocument.Load(cosmeticLook).Descendants("property").Single().Attribute("slotIconCustom") == "icon0.png"
            && Icon(capsuleLook) == "./Data/Resource/Image/item/icon/newGacha_capsule_ducky.png" && Icon(hatLook) == "./Data/Resource/Image/item/icon/11320083.png" && looksOnce);
        Check("caps: fix-items drops a hat's capAttach and rotation, which no Global item has, once; a face paint's customize stays",
            Cap(hatLook) == "<customize color=\"1\" defaultColorIndex=\"7\" ch0=\"0\" />" && looksOnce);
        var turned = XDocument.Load(turnLook);
        Check("turns: fix-items gives a hat with userrotation whose CP flags do not let it turn Global's flags, once, and keeps its userrotation and scale; turning hats and a hat without userrotation stay",
            Cap(turnLook) == "<customize><CP xrotation=\"1\" yrotation=\"1\" zrotation=\"1\" scale=\"0\" attach=\"1\" /></customize>"
            && turned.Descendants("userrotation").Count() == 2 && turned.Descendants("physx").Count() == 1 && looksOnce);
        Directory.Delete(looks, recursive: true);
        string fx = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kmssplit-effects-" + Guid.NewGuid().ToString("N"));
        foreach (var (rel, xml) in new[] {
            ("skill/80/80010092.xml", "<ms2><level><glideProperty effectRun=\"CH/Fly/Eff_FlyTypeC_BlackBean01_Run.xml\" effectIdle=\"CH\\Fly\\NA_Idle.xml\" /><motion><motionProperty strTagEffects=\"e0=CH/Skill/A.xml,  e1=CH/Skill/Gone.xml\" /></motion></level></ms2>"),
            ("riding/50600308.xml", "<ms2><riding><basic pressXEffectRun=\"Eff_Item_Ride_50600308_R_Carrier_SpRun.xml\" /></riding></ms2>"),
            ("npc/60/00/60000062.xml", "<ms2><environment><aiInfo path=\"Pet/AI_DefaultPetTaming.xml\" /></environment></ms2>") }) {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.Combine(fx, rel))!);
            File.WriteAllText(System.IO.Path.Combine(fx, rel), xml);
        }
        byte[] runFx = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("<ms2>\r\n  <effect type=\"nif\" filePath=\"CH/Fly/FlyTypeC_BlackBean_Run_A.nif\" />\r\n</ms2>")).ToArray(), stubFx = Encoding.UTF8.GetBytes("<ms2 />");
        var kmsFx = new Dictionary<string, byte[]> { ["effect/ch/fly/eff_flytypec_blackbean01_run.xml"] = runFx, ["effect/ch/fly/na_idle.xml"] = stubFx, ["effect/ch/skill/a.xml"] = stubFx,
            ["effect/item/ride/eff_item_ride_50600308_r_carrier_sprun.xml"] = stubFx };
        var (fxWritten, fxMissing) = Conv.CopyEffects(fx, p => p == "effect/ch/fly/na_idle.xml", kmsFx);
        Check("effect XMLs: the ones a record names and NA lacks are written from KMS byte for byte (each part of a tag list, a bare ride name under Item/Ride, backslashes); NA's own and an aiInfo path not, one neither client has reported",
            fxWritten.SequenceEqual(new[] { "effect/ch/fly/eff_flytypec_blackbean01_run.xml", "effect/ch/skill/a.xml", "effect/item/ride/eff_item_ride_50600308_r_carrier_sprun.xml" })
            && File.ReadAllBytes(System.IO.Path.Combine(fx, "effect/ch/fly/eff_flytypec_blackbean01_run.xml")).SequenceEqual(runFx)
            && !File.Exists(System.IO.Path.Combine(fx, "effect/ch/fly/na_idle.xml")) && fxMissing.SequenceEqual(new[] { "effect/ch/skill/gone.xml" }));
        Directory.Delete(fx, recursive: true);

        // records (IM-02): rides, passengers, glide skills, pet rows, and which ids the mounts and pets name
        Check("riding path scheme", Layout.Path("riding", 50600349) == "riding/50600349.xml" && Layout.Passenger(50600349) == "riding/passenger/50600349.xml");
        var kmsRide = XElement.Parse("<ms2><riding><basic id=\"7\" kfm=\"k\" runSpeed=\"5.25\" /><collision height=\"130\" /><passengers>"
            + "<passenger passengerid=\"0\" rideBone=\"Ride_B\" rideAniPC=\"Ride_Idle_026\" /><passenger passengerid=\"1\" rideBone=\"Ride_C\" rideRotation=\"0,0,90\" /></passengers></riding>"
            + "<riding locale=\"CN\"><basic id=\"7\" /><passengers><passenger passengerid=\"0\" /></passengers></riding></ms2>");
        var pass = Conv.Passengers(kmsRide, 7)!.Root!.Elements("ridepassenger").ToList();
        Check("riding: passengers of the main riding element leave the ride file and become riding/passenger rows with NA's attributes, a missing one Ride_Idle_A, 0,0,0 or empty",
            !Conv.Riding(kmsRide).Descendants("passengers").Any() && Conv.Riding(kmsRide).Descendants("basic").First().Attribute("runSpeed")?.Value == "5.25"
            && pass.Count == 2 && pass.All(p => p.Attributes().Count() == 15 && (string?)p.Attribute("id") == "7" && (string?)p.Attribute("rideTranslation") == "0,0,0" && (string?)p.Attribute("nameTagOffsetY") == "")
            && (string?)pass[0].Attribute("rideAniPC") == "Ride_Idle_026" && (string?)pass[1].Attribute("rideRotation") == "0,0,90" && (string?)pass[1].Attribute("rideAniPC") == "Ride_Idle_A"
            && Conv.Passengers(XElement.Parse("<ms2><riding><basic id=\"8\" /></riding></ms2>"), 8) == null);
        var filled = new XDocument(XElement.Parse("<ms2><riding><basic id=\"7\" runSpeed=\"5.25\" /></riding></ms2>"));
        Conv.FillRide(filled, new() { ["/collision@height"] = "90", ["/basic@walkSpeed"] = "1", ["/basic@runSpeed"] = "0" });
        Check("riding fill: NA's value where KMS leaves it out, the KMS value kept",
            filled.Descendants("collision").Count() == 1 && (string?)filled.Descendants("collision").Single().Attribute("height") == "90"
            && (string?)filled.Descendants("basic").Single().Attribute("walkSpeed") == "1" && (string?)filled.Descendants("basic").Single().Attribute("runSpeed") == "5.25");
        var glide = Conv.Glide(XElement.Parse("<glide id=\"80010151\" gravity=\"0.5\" heightLimit=\"1300\" horizontalAccelerate=\"2\" horizontalVelocity=\"1\" verticalVelocity=\"1.7\" verticalVibrateAmplitude=\"1.5\" "
            + "verticalVibrateFrequency=\"2\" glideHorizontalAccelerateLimit=\"5.5\" effectRun=\"CH/Fly/R.xml\" effectIdle=\"CH/Fly/I.xml\" aniIdle=\"FlyTypeS_Idle_A\" aniRun=\"FlyTypeS_Run_A\" />"));
        var tagged = Conv.Glide(XElement.Parse("<glide id=\"80010033\" gravity=\"0.5\" horizontalVelocity=\"2.5\" nameTagOffset=\"-145\" />"));
        Check("glide: the glidedata row scaled into the KMS-v61 body, no item condition, nameTagOffset on the kinds",
            glide.Descendants("glideProperty").Single().ToString() == "<glideProperty gravity=\"5\" heightLimit=\"1300\" horizontalAccelerate=\"200\" horizontalVelocity=\"100\" verticalAccelerate=\"0\" verticalVelocity=\"170\" "
                + "verticalVibrateAmplitude=\"150\" verticalVibrateFrequency=\"2000\" glideHorizontalAccelerateLimit=\"550\" bEffect=\"1\" effectRun=\"CH/Fly/R.xml\" effectIdle=\"CH/Fly/I.xml\" aniIdle=\"FlyTypeS_Idle_A\" aniLeft=\"\" aniRight=\"\" aniRun=\"FlyTypeS_Run_A\" onGroundDisable=\"0\" />"
            && !glide.Descendants("item").Any() && (string?)glide.Descendants("kinds").Single().Attribute("offsetNameTag") == "0"
            && tagged.Descendants("glideProperty").Single().Attribute("glideHorizontalAccelerateLimit") == null && (string?)tagged.Descendants("glideProperty").Single().Attribute("horizontalVelocity") == "250"
            && (string?)tagged.Descendants("kinds").Single().Attribute("offsetNameTag") == "-145" && (string?)tagged.Descendants("kinds").Single().Attribute("offsetHPBar") == "-145");
        const string NaPets = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<ms2>\r\n\t<pets code=\"1\" npcID=\"1\">\r\n\t\t<skill id=\"0\" level=\"0\" />\r\n\t</pets>\r\n</ms2>";
        Check("pet rows: npcID = code after isCollectPreview and the NA skill child, appended to the NA table, its text kept",
            Conv.AddPetRows(NaPets, new[] { XElement.Parse("<pets code=\"60000062\" isCollectPreview=\"0\" slotNum=\"14\" />"), XElement.Parse("<pets code=\"61100150\" type=\"1\" feature=\"Pet_70Lv_Boss\" />") })
                == NaPets.Replace("</ms2>", "\t<pets code=\"60000062\" isCollectPreview=\"0\" npcID=\"60000062\" slotNum=\"14\">\r\n\t\t<skill id=\"0\" level=\"0\" />\r\n\t</pets>\r\n"
                    + "\t<pets code=\"61100150\" npcID=\"61100150\" type=\"1\" feature=\"Pet_70Lv_Boss\">\r\n\t\t<skill id=\"0\" level=\"0\" />\r\n\t</pets>\r\n</ms2>"));
        string recs = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kmssplit-records-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(System.IO.Path.Combine(recs, "item", "5", "06"));
        File.WriteAllText(System.IO.Path.Combine(recs, "item", "5", "06", "50600349.xml"), "<ms2><environment><property type=\"9\" param1=\"50600349\" /></environment></ms2>");
        File.WriteAllText(System.IO.Path.Combine(recs, "item", "5", "06", "50600315.xml"), "<ms2><environment><property type=\"5\" param1=\"80010155\" /></environment></ms2>");
        File.WriteAllText(System.IO.Path.Combine(recs, "item", "5", "06", "61100150.xml"), "<ms2><environment><property type=\"11\" param1=\"61100150\" /></environment></ms2>");
        File.WriteAllText(System.IO.Path.Combine(recs, "item", "5", "06", "20300291.xml"), "<ms2><environment><property type=\"1\" param1=\"7\" /></environment><environment><property type=\"9\" param1=\"0\" /></environment></ms2>");
        var named = Conv.RecordRefs(recs).OrderBy(r => r.Id).ToList();
        Directory.Delete(recs, recursive: true);
        Check("record refs: the ride, glide skill and pet id of each mount and pet under item/, other types and param1 0 ignored",
            named.SequenceEqual(new[] { ("riding", 50600349L), ("pet", 61100150L), ("skill", 80010155L) }));

        // copies (IM-03): a KMS mount's own summon or glide skill, and its item pointed at it
        Check("copy ids: the item's block and last three digits, 800xxxxx for ground and 801xxxxx for air mounts, -1 outside",
            Conv.SummonCopy(50600281) == 80001281 && Conv.SummonCopy(50610013) == 80002013 && Conv.SummonCopy(50620238) == 80003238
            && Conv.GlideCopy(40400055) == 80011055 && Conv.GlideCopy(40410034) == 80012034 && Conv.GlideCopy(40420027) == 80013027
            && Conv.SummonCopy(50601000) == -1 && Conv.SummonCopy(50670000) == -1 && Conv.SummonCopy(40400055) == -1 && Conv.GlideCopy(40490000) == -1 && Conv.GlideCopy(50600281) == -1
            && Conv.IsCopy(80001281) && Conv.IsCopy(80013027) && !Conv.IsCopy(80000343) && !Conv.IsCopy(80008001) && !Conv.IsCopy(80010155) && !Conv.IsCopy(80000001));
        var summonSrc = XDocument.Parse("<?xml version=\"1.0\" encoding=\"utf-8\"?><ms2 feature=\"\"><basic><ui icon=\"./Data/Resource/Image/item/icon/50600118.png\" movie=\"\" />"
            + "<kinds type=\"3\" jump=\"80000118\" state=\"gosSummon\" continue=\"0\" /></basic><level value=\"1\"><beginCondition level=\"1\"><stat hp=\"1\" /></beginCondition>"
            + "<targetItem><item id=\"50600118\" count=\"0\" /></targetItem></level></ms2>");
        var summonCopy = Conv.Summon(summonSrc, 80001281, 50600281, "./Data/Resource/Image/item/icon/50600281.png");
        Check("summon copy: the Global body with the item's icon, its own id as jump and the item as targetItem, the source kept",
            (string?)summonCopy.Descendants("ui").Single().Attribute("icon") == "./Data/Resource/Image/item/icon/50600281.png"
            && (string?)summonCopy.Descendants("kinds").Single().Attribute("jump") == "80001281"
            && (string?)summonCopy.Descendants("targetItem").Single().Element("item")?.Attribute("id") == "50600281"
            && summonCopy.ToString() == summonSrc.ToString().Replace("50600118.png", "50600281.png").Replace("jump=\"80000118\"", "jump=\"80001281\"").Replace("id=\"50600118\"", "id=\"50600281\"")
            && (string?)summonSrc.Descendants("kinds").Single().Attribute("jump") == "80000118");
        var naGlide = XDocument.Parse("<ms2><basic><kinds type=\"3\" subType=\"7\" jump=\"80010067\" state=\"gosGlide\" continue=\"1\" /></basic><level value=\"1\"><beginCondition level=\"1\"><stat hp=\"1\" />"
            + "<item id=\"40410016\" count=\"1\" rank=\"4\" /></beginCondition><targetItem><item id=\"40410016\" count=\"0\" /></targetItem>"
            + "<glideProperty gravity=\"5\" aniIdle=\"FlyType001_Idle_A\" aniLeft=\"FlyType001_Run_A\" aniRight=\"\" aniRun=\"FlyType001_Run_A\" /></level></ms2>");
        var glideCopy = Conv.GlideFor(XElement.Parse("<glide id=\"80010067\" gravity=\"0.5\" aniIdle=\"FlyType029_Idle\" aniRun=\"FlyType029_Run\" />"), naGlide);
        var gcp = glideCopy.Descendants("glideProperty").Single();
        Check("glide copy: the KMS-v61 body with NA's animations, no item, continue 0 and no jump; gated to another item only when NA names one",
            (string?)gcp.Attribute("gravity") == "5" && (string?)gcp.Attribute("aniIdle") == "FlyType001_Idle_A" && (string?)gcp.Attribute("aniLeft") == "FlyType001_Run_A"
            && (string?)gcp.Attribute("aniRight") == "" && (string?)gcp.Attribute("aniRun") == "FlyType001_Run_A" && (string?)gcp.Attribute("bEffect") == "1"
            && !glideCopy.Descendants("item").Any() && (string?)glideCopy.Descendants("kinds").Single().Attribute("continue") == "0" && glideCopy.Descendants("kinds").Single().Attribute("jump") == null
            && Conv.GatedElsewhere(naGlide, 40410034) && !Conv.GatedElsewhere(naGlide, 40410016) && !Conv.GatedElsewhere(glideCopy, 40410034));
        string pts = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kmssplit-copies-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pts);
        string ground = System.IO.Path.Combine(pts, "50600281.xml"), air = System.IO.Path.Combine(pts, "40400055.xml"), other = System.IO.Path.Combine(pts, "20000284.xml");
        File.WriteAllText(ground, "<ms2><environment><property type=\"9\" param1=\"50600281\" subtype=\"0\" /><ride rideMonster=\"50600281\" /><skill skillID=\"0\" skillLevel=\"1\" /></environment></ms2>");
        File.WriteAllText(air, "<ms2><environment><property type=\"5\" param1=\"80010023\" /><skill skillID=\"80010023\" skillLevel=\"1\" /></environment></ms2>");
        File.WriteAllText(other, "<ms2><environment><property type=\"2\" param1=\"7\" /><skill skillID=\"0\" /></environment></ms2>");
        byte[] otherBytes = File.ReadAllBytes(other);
        bool firstGround = Conv.PointAt(ground, 80001281), firstAir = Conv.PointAt(air, 80011055);
        byte[] groundBytes = File.ReadAllBytes(ground), airBytes = File.ReadAllBytes(air);
        var gd = XDocument.Load(ground); var ad = XDocument.Load(air);
        Check("point at the copy: a ground mount's skill and subtype 12, an air mount's skill and param1, other types untouched; a second run changes nothing",
            firstGround && firstAir && (string?)gd.Descendants("property").Single().Attribute("subtype") == "12" && (string?)gd.Descendants("property").Single().Attribute("param1") == "50600281"
            && (string?)gd.Descendants("skill").Single().Attribute("skillID") == "80001281" && (string?)gd.Descendants("ride").Single().Attribute("rideMonster") == "50600281"
            && (string?)ad.Descendants("property").Single().Attribute("param1") == "80011055" && (string?)ad.Descendants("skill").Single().Attribute("skillID") == "80011055"
            && (string?)ad.Descendants("skill").Single().Attribute("skillLevel") == "1" && ad.Descendants("property").Single().Attribute("subtype") == null
            && !Conv.PointAt(other, 80001284) && File.ReadAllBytes(other).SequenceEqual(otherBytes)
            && !Conv.PointAt(ground, 80001281) && !Conv.PointAt(air, 80011055) && File.ReadAllBytes(ground).SequenceEqual(groundBytes) && File.ReadAllBytes(air).SequenceEqual(airBytes));
        Directory.Delete(pts, recursive: true);

        // animation keys (IM-04): the anikeyinfo conversion, the KMS block lookup, the insertion, the ingest parser's reading
        const string Info = "<ms2><seq id=\"1\" name=\"Attack_01_A\"><key name=\"emo_Skill_A\" time=\"0.0000\" /><key name=\" -GlobalRatio 100\" time=\"0.0000\" />"
            + "<key name=\"p0\" time=\"0.5000\" /><key name=\"end\" time=\"1.4000\" /></seq><seq id=\"3\" name=\"Idle_A\"><key name=\"end\" time=\"1.3333\" /></seq></ms2>";
        Check("anikey info: a start key at 0, then end, then the other KMS keys in order, export keys kept, 6 decimals",
            Conv.AniKeyBlock("60000062_nao", Info, "\r\n") == "\t<kfm name=\"60000062_nao\">\r\n\t\t<seq id=\"1\" name=\"Attack_01_A\">\r\n\t\t\t<key name=\"start\" time=\"0.000000\" />\r\n"
                + "\t\t\t<key name=\"end\" time=\"1.400000\" />\r\n\t\t\t<key name=\"emo_Skill_A\" time=\"0.000000\" />\r\n\t\t\t<key name=\" -GlobalRatio 100\" time=\"0.000000\" />\r\n"
                + "\t\t\t<key name=\"p0\" time=\"0.500000\" />\r\n\t\t</seq>\r\n\t\t<seq id=\"3\" name=\"Idle_A\">\r\n\t\t\t<key name=\"start\" time=\"0.000000\" />\r\n"
                + "\t\t\t<key name=\"end\" time=\"1.333300\" />\r\n\t\t</seq>\r\n\t</kfm>\r\n");
        const string Keys = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<ms2ani>\r\n\t<kfm name=\"a\">\r\n\t\t<seq id=\"0\" name=\"Idle_A\" />\r\n\t</kfm>\r\n"
            + "\t<kfm name=\"61120150_blackbean\">\r\n\t\t<seq id=\"6\" name=\"Walk_A\" />\r\n\t</kfm>\r\n\t<kfm name=\"b\" />\r\n</ms2ani>";
        Check("anikey text: a model's block in any case with its line end, none for a model without one",
            Conv.KfmBlock(Keys, "61120150_Blackbean") == "\t<kfm name=\"61120150_blackbean\">\r\n\t\t<seq id=\"6\" name=\"Walk_A\" />\r\n\t</kfm>\r\n"
            && Conv.KfmBlock(Keys, "b") == null && Conv.KfmBlock(Keys, "60000062_Nao") == null);
        Check("anikey insert: the blocks go before the closing tag, the rest of the text kept",
            Conv.InsertBefore(Keys, "</ms2ani>", "X\r\n") == Keys[..^9] + "X\r\n</ms2ani>");
        var parsed = (Maple2.File.Parser.Xml.AnimationData)new System.Xml.Serialization.XmlSerializer(typeof(Maple2.File.Parser.Xml.AnimationData))
            .Deserialize(new StringReader(Conv.InsertBefore(Keys, "</ms2ani>", Conv.AniKeyBlock("60000062_nao", Info, "\r\n"))))!;
        Check("anikey parse: the server ingest's own type reads the inserted block after the others, with its seq ids, keys and end time",
            parsed.kfm.Select(m => m.name).SequenceEqual(new[] { "a", "61120150_blackbean", "b", "60000062_nao" })
            && parsed.kfm[3].seq.Select(s => s.id).SequenceEqual(new[] { 1, 3 }) && parsed.kfm[3].seq[0].key.Count == 5
            && parsed.kfm[3].seq[1].key.Single(x => x.name == "end").time == 1.3333);

        // KMS homes (IB-02): the box writer, the function and gacha row edits, the home rules, the gate, a copy's own references
        const string KmsBox = "<dropBox dropBoxID=\"320000392\" comment=\"c\"><group dropGroupID=\"1\" locale=\"KR\"><v itemID=\"20800019\" minCount=\"10\" weight=\"100\" locale=\"KR\" />"
            + "<v itemID=\"11320293\" itemID2=\"11320294\" weight=\"50\" /><v itemID=\"20303049\" weight=\"40\" locale=\"CN\" /><v itemID=\"20301788\" weight=\"30\" /><v itemID=\"20899999\" weight=\"20\" /><v itemID=\"20899999\" itemID2=\"14120191\" weight=\"5\" /></group>"
            + "<group dropGroupID=\"2\" locale=\"CN\"><v itemID=\"20303049\" /></group><group dropGroupID=\"3\"><v itemID=\"20303049\" weight=\"10\" /></group><group dropGroupID=\"4\"><v itemID=\"20301788\" /></group>"
            + "<group dropGroupID=\"3\"><v itemID=\"11320295\" weight=\"10\" /><v itemID=\"11320296\" weight=\"10\" /></group></dropBox>";
        var homeMap = new Dictionary<long, long> { [11320293] = 11300308, [11320294] = 11300308, [11320295] = 11300314, [11320296] = 11300314, [20303049] = 20393049, [20301788] = 0, [20899999] = 0 };
        var homeBox = Homes.HomeBox(XElement.Parse(KmsBox), 320090392, p => homeMap.GetValueOrDefault(p, p), out int gone, out bool plainGap);
        Check("home box: KR rows kept without the mark, CN rows left out, twins and copies mapped, dropped and unshipped rows gone with their weight, empty groups gone, groups of one id merged, a pair keeps its shipped id as itemID, a row equal to an earlier one gone",
            homeBox?.ToString(SaveOptions.DisableFormatting) == "<dropBox dropBoxID=\"320090392\" comment=\"c\"><group dropGroupID=\"1\"><v itemID=\"20800019\" minCount=\"10\" weight=\"100\" />"
                + "<v itemID=\"11300308\" itemID2=\"11300308\" weight=\"50\" /><v itemID=\"14120191\" weight=\"5\" /></group><group dropGroupID=\"3\"><v itemID=\"20393049\" weight=\"10\" /><v itemID=\"11300314\" weight=\"10\" /></group></dropBox>" && gone == 3 && !plainGap
            && Homes.HomeBox(XElement.Parse("<dropBox dropBoxID=\"7\"><group dropGroupID=\"1\"><v itemID=\"5\" /></group></dropBox>"), 90007, _ => 0, out _, out _) == null);
        const string FinalText = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<ms2>\r\n  <dropBox dropBoxID=\"7\">\r\n    <group dropGroupID=\"1\">\r\n      <v itemID=\"5\" />\r\n    </group>\r\n  </dropBox>\r\n</ms2>\r\n";
        string withHome = Conv.InsertBefore(FinalText, "</ms2>", Homes.Block(homeBox!, "\r\n"));
        var drops = (Maple2.File.Parser.Xml.Table.Server.IndividualItemDropRoot)new System.Xml.Serialization.XmlSerializer(typeof(Maple2.File.Parser.Xml.Table.Server.IndividualItemDropRoot)).Deserialize(new StringReader(withHome))!;
        Check("home box text: Final's text kept as the start, the box two spaces in with Final's line ends, read by the server parser's own type",
            withHome.StartsWith(FinalText[..FinalText.LastIndexOf("</ms2>")]) && withHome.EndsWith("  </dropBox>\r\n</ms2>\r\n") && !withHome.Replace("\r\n", "").Contains('\n')
            && withHome.Contains("\r\n  <dropBox dropBoxID=\"320090392\" comment=\"c\">\r\n    <group dropGroupID=\"1\">\r\n      <v itemID=\"20800019\" minCount=\"10\" weight=\"100\" />\r\n")
            && drops.dropBox.Select(b => b.dropBoxID).SequenceEqual(new[] { 7, 320090392 }) && drops.dropBox[1].group[0].v[0].minCount == 10 && drops.dropBox[1].group[1].v[0].itemID == 20393049);
        var fns = XDocument.Parse("<ms2><environment><function name=\"OpenItemBox\" param1=\"320000392\" parameter=\"0,1,0,320000392,2\" /></environment>"
            + "<environment><function name=\"SelectItemBox\" parameter=\"1,340000207\" /></environment><environment><function name=\"OpenItemBoxWithKey\" parameter=\"20000985,1,0,0,0,330001527\" /></environment>"
            + "<environment><function name=\"OpenGachaBox\" parameter=\"1003001,22103001\" /></environment><environment><function name=\"OpenItemBox\" parameter=\"0,1,0,330000216\" /></environment></ms2>");
        var boxHomes = new Dictionary<long, long> { [320000392] = 320090392, [340000207] = 340090207, [330001527] = 330091527 };
        long BoxTo(long b) => boxHomes.GetValueOrDefault(b, b);
        long KeyTo(long key) => key == 20000985 ? 20090985 : key;
        long GachaTo(long g) => g == 1003001 ? 1093001 : g;
        bool pointedOnce = Homes.Point(fns, BoxTo, KeyTo, GachaTo);
        Check("point at the homes: OpenItemBox field 4 and param1, SelectItemBox field 2, OpenItemBoxWithKey's key and box, the gacha; another box kept; a second run changes nothing",
            pointedOnce && fns.Descendants("function").Select(f => (string?)f.Attribute("parameter") + "|" + (string?)f.Attribute("param1"))
                .SequenceEqual(new[] { "0,1,0,320090392,2|320090392", "1,340090207|", "20090985,1,0,0,0,330091527|", "1093001,22103001|", "0,1,0,330000216|" })
            && !Homes.Point(fns, BoxTo, KeyTo, GachaTo)
            && Homes.Fn(XElement.Parse("<function name=\"OpenItemBox\" param1=\"320000392\" />")) == ("OpenItemBox", 320000392, 0, 0)
            && Homes.Fn(XElement.Parse("<function name=\"OpenItemBoxWithKey\" parameter=\"20000985,1,0,0,0,330001527\" />")) == ("OpenItemBoxWithKey", 330001527, 20000985, 0)
            && Homes.Fn(XElement.Parse("<function name=\"OpenGachaBox\" parameter=\"1004504,22104004\" />")) == ("OpenGachaBox", 0, 0, 1004504));
        const string Gacha = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<ms2>\r\n\t<randomBox randomBoxID=\"1003001\" randomBoxGroup=\"3\" ui=\"3\" individualDropBoxID=\"530000001\" shopID=\"\" />\r\n"
            + "\t<randomBox randomBoxID=\"1004504\" individualDropBoxID=\"545000004\" coinItemID=\"22204504\" />\r\n</ms2>";
        var rowBoxes = new Dictionary<long, long> { [1004504] = 545090004 };
        var rowHomes = new Dictionary<long, (long, long)> { [1003001] = (1093001, 530090001) };
        var (gachaText, gachaRows) = Homes.GachaRows(Gacha, rowBoxes, rowHomes);
        Check("gacha rows: a KMS row names its box's home, a Global gacha gets a home row (its line with the home ids) before </ms2>, the rest kept; a second run changes nothing",
            gachaText == Gacha.Replace("545000004", "545090004").Replace("</ms2>", "\t<randomBox randomBoxID=\"1093001\" randomBoxGroup=\"3\" ui=\"3\" individualDropBoxID=\"530090001\" shopID=\"\" />\r\n</ms2>")
            && gachaRows == 2 && Homes.GachaRows(gachaText, rowBoxes, rowHomes) == (gachaText, 0)
            && Homes.GachaBoxes(Gacha.Replace("</ms2>", "\t<randomBox randomBoxID=\"1004504\" locale=\"KR\" individualDropBoxID=\"545000005\" />\r\n</ms2>"))[1004504] == 545000005);
        Check("homes: a box or gacha + 90000 when free and below 10000 in its block of 100000; a copy keeps its first three digits, has 9 as its fourth and is free",
            Homes.BoxHome(320000392, _ => false) == 320090392 && Homes.BoxHome(1003001, _ => false) == 1093001 && Homes.BoxHome(330012345, _ => false) == -1
            && Homes.BoxHome(320000392, h => h == 320090392) == -1 && Homes.ItemHomeOk(20303049, 20393049, _ => false) && Homes.ItemHomeOk(11320083, 11390088, _ => false)
            && !Homes.ItemHomeOk(20303049, 20493049, _ => false) && !Homes.ItemHomeOk(20303049, 20383049, _ => false) && !Homes.ItemHomeOk(20303049, 20393049, h => h == 20393049));
        var homeRows = Homes.ReadTable("id\tverdict\thome\treason\r\n20303049\tcopy\t20393049\tKMS production coupon box\r\n11320293\ttwin\t11300308\tre-issue\r\n20301788\tdrop\t\ttitle\r\n20301962\tkeep\t\tsame item\r\n");
        bool refused = false;
        try { Homes.ReadTable("id\tverdict\thome\treason\n1\tcopy\t\tno home\n"); } catch (InvalidDataException) { refused = true; }
        Check("gate: a reached Global id whose look, function family or box contents differ from NA's needs a row; the table's rows, and a copy without a home is refused",
            !Homes.Flag(1, 1, "OpenItemBox", "SelectItemBox", new() { 5 }, new() { 5 }) && Homes.Flag(1, 2, "", "", new(), new()) && Homes.Flag(1, 1, "OpenItemBox", "TitleScroll", new(), new())
            && Homes.Flag(1, 1, "OpenGachaBox", "OpenGachaBox", new() { 5 }, new() { 6 }) && !Homes.Flag(1, 1, "Hide", "Hide", new() { 5 }, new() { 6 })
            && Homes.Unreviewed(new long[] { 20303049, 20301962, 30000001, 30000002 }, homeRows, p => p != 30000002).SequenceEqual(new long[] { 30000001 })
            && homeRows[20303049] == new Homes.Row("copy", 20393049, "KMS production coupon box") && homeRows[20301788].Home == 0 && refused
            && Homes.NaLocal(XDocument.Parse("<ms2><environment locale=\"NA\" /></ms2>")) && !Homes.NaLocal(XDocument.Parse("<ms2><environment feature=\"Event\" locale=\"JP\" /></ms2>")));
        var self = XDocument.Parse("<ms2><environment><basic originID=\"11220083\" /><tool itemPreset=\"11220083\" itemPresetPath=\"1/12/\" /></environment></ms2>");
        Homes.SelfRefs(self, 11220083, 11290083);
        var otherPreset = XDocument.Parse("<ms2><environment><tool itemPreset=\"11900001\" /></environment></ms2>");
        Homes.SelfRefs(otherPreset, 11900120, 11990120);
        Check("copy: its own preset and origin name the copy, another preset stays",
            self.ToString(SaveOptions.DisableFormatting) == "<ms2><environment><basic originID=\"11290083\" /><tool itemPreset=\"11290083\" itemPresetPath=\"1/12/\" /></environment></ms2>"
            && (string?)otherPreset.Descendants("tool").Single().Attribute("itemPreset") == "11900001");
        var dupRow = new Homes.Row("twin", 13100329, "same as pack item 13100329");
        Check("twins: a shipped KMS record is kms; a Global item takes its row, else keep; an id the pack lacks takes only a twin row (the item shipped in its place), else unshipped; a twin is for the KMS item's gender or both, KMS's missing genderLimit both and the server's male",
            Homes.Verdict(false, true, dupRow) == "kms" && Homes.Verdict(false, false, dupRow) == "twin" && Homes.Verdict(false, false, null) == "unshipped"
            && Homes.Verdict(false, false, new Homes.Row("keep", 0, "k")) == "refused" && Homes.Verdict(false, false, new Homes.Row("copy", 13190338, "c")) == "refused"
            && Homes.Verdict(true, false, null) == "keep" && Homes.Verdict(true, true, new Homes.Row("copy", 20393049, "c")) == "copy" && Homes.Verdict(true, false, new Homes.Row("drop", 0, "d")) == "drop"
            && Homes.GenderOk(1, 2) && Homes.GenderOk(1, 1) && Homes.GenderOk(2, 2) && !Homes.GenderOk(1, 0) && !Homes.GenderOk(2, 0)
            && Homes.KrEnv(XElement.Parse("<item><environment locale=\"CN\"><limit genderLimit=\"0\" /></environment><environment locale=\"KR\"><limit genderLimit=\"1\" /></environment></item>")) is { } kr
            && Homes.Gender(kr, 2) == 1 && Homes.Gender(XElement.Parse("<environment><limit transferType=\"1\" /></environment>"), 2) == 2 && Homes.Gender(XElement.Parse("<ms2><environment><limit /></environment></ms2>"), 0) == 0);
        const string ClassBox = "<dropBox dropBoxID=\"320000181\"><group dropGroupID=\"1\" smartDropRate=\"100\"><v itemID=\"13100338\" weight=\"10000\" /></group>"
            + "<group dropGroupID=\"1\" smartDropRate=\"100\"><v itemID=\"13200333\" itemID2=\"14100303\" weight=\"10000\" /></group><group dropGroupID=\"2\" smartDropRate=\"100\"><v itemID=\"20302407\" /></group>"
            + "<group dropGroupID=\"3\"><v itemID=\"20302407\" /><v itemID=\"20303208\" /></group></dropBox>";
        var keptFor = new Dictionary<long, long> { [13100338] = 13100329, [13200333] = 13200324, [14100303] = 14100294, [20302407] = 0 };
        var whole = Homes.HomeBox(XElement.Parse(ClassBox), 320090181, p => keptFor.GetValueOrDefault(p, p), out _, out bool wholeGap);
        Homes.HomeBox(XElement.Parse(ClassBox), 320090181, p => p is 13100338 or 20302407 ? 0 : p, out _, out bool rowGap);
        Homes.HomeBox(XElement.Parse(ClassBox), 320090181, p => p is 14100303 or 20302407 ? 0 : p, out _, out bool idGap);
        Check("smart drop: a smart-drop group that loses a row or an id and keeps other rows is a class gap; one that loses all its rows, and a plain group that loses a row, are not",
            !wholeGap && rowGap && idGap && whole?.ToString(SaveOptions.DisableFormatting) == "<dropBox dropBoxID=\"320090181\"><group dropGroupID=\"1\" smartDropRate=\"100\"><v itemID=\"13100329\" weight=\"10000\" />"
                + "<v itemID=\"13200324\" itemID2=\"14100294\" weight=\"10000\" /></group><group dropGroupID=\"3\"><v itemID=\"20303208\" /></group></dropBox>");

        Console.WriteLine(fail == 0 ? "selftest passed" : $"selftest FAILED ({fail})");
        return fail == 0;
    }
}
