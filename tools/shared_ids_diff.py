#!/usr/bin/env python3
"""Shared-id comparison Global (GMS) vs KMS (backlog U-05). Python 3, standard library only.

  shared_ids_diff.py diff    --gms DIR --kms DIR --out FILE.tsv [--gms-strings DIR --kms-strings DIR] [--kinds item,npc,quest,skill]
  shared_ids_diff.py summary --diff FILE.tsv [--names FILE.names.tsv] [--ignore REGEX]
  shared_ids_diff.py selftest

diff: --gms and --kms hold the client per-id layout (item/1/02/10200001.xml, npc/11/00/11000001.xml, quest/10001000.xml,
skill/20/20000011.xml). --kms is the output of `KmsSplit <Xml.m2d> <dir>` run WITHOUT --na (--na skips shared ids).
Only ids present in both trees are compared, in parallel (one process per CPU).
Normalisation: comments, whitespace and attribute order are ignored; true/false are case-insensitive; numbers and comma
lists of numbers compare as numbers (tolerance 1e-3 absolute or 1e-4 relative); of the per-region <environment> variants
in a GMS file only the NA one (else the base one) is compared per feature.
Defaults: an attribute missing on one side means that side holds the default (most common GMS value for that element path
and attribute, learned from a sample of the files). It is ignored when the other side states the default (this removes the
values KmsSplit fills with defaults) and otherwise reported as a value difference with the default shown as ~value.
Output FILE.tsv: one row per difference (kind, id, path, attr, gms, kms, cls), plus one `identical` row for an id without
difference. cls: num (both values numbers), text (any other value difference), only_gms / only_kms (an element or an
identifying attribute exists on one side only), ext_gms / ext_kms (field or element the other edition never uses; not a
conflict, so `summary` does not count it when it classifies an id).
With --gms-strings / --kms-strings (directories holding en/ and, for KMS, kr/) also writes FILE.names.tsv: name per side and
a class. The KMS English table repeats the Global names for shared ids, so the Korean name is checked against the Global
English name through a word glossary learned from all shared pairs (a heuristic: `different` marks candidates for a manual
look). Classes: same, retranslation (English names differ but are alike), different, unverified (no Korean name or too few
known words), missing (no Global name, or placeholder text only).

summary: prints the markdown tables used by docs/engineering/SHARED_IDS.md. --ignore REGEX leaves out the fields whose
element@attribute path matches (to see what remains without a field group that KmsSplit cannot fill).
"""
import argparse, collections, difflib, glob, multiprocessing, os, re, sys
import xml.etree.ElementTree as ET

KINDS = ("item", "npc", "quest", "skill")
DISC = ("name", "id", "code", "questID", "value")  # attributes that identify repeated sibling elements
NAME_FILES = {"item": "itemname.xml", "npc": "npcname.xml", "skill": "skillname*.xml", "quest": "questdescription*.xml"}
PLACEHOLDER = re.compile(r"^[A-Z0-9_]+_(NAME|DESC)(:.*)?$")


def load(path, locales=None):
    """Parse a file. With `locales`, each feature keeps one <environment>: the first locale of the list that exists
    (GMS files carry one environment per region next to the base one; KMS files carry the base one)."""
    with open(path, "rb") as f:
        root = ET.fromstring(f.read().decode("utf-8-sig"))
    envs = [c for c in root if c.tag == "environment"]
    if locales and len(envs) > 1:
        keep = {}
        for c in envs:
            f, loc = c.get("feature"), c.get("locale") or ""
            if loc in locales and (f not in keep or locales.index(loc) < locales.index(keep[f].get("locale") or "")):
                keep[f] = c
        for c in envs:
            if c not in keep.values():
                root.remove(c)
    return root


def ids_in(root, kind):
    out = {}
    for dp, _, fs in os.walk(os.path.join(root, kind)):
        for f in fs:
            if f.endswith(".xml") and f[:-4].isdigit():
                out[int(f[:-4])] = os.path.join(dp, f)
    return out


def flatten(root):
    """-> {path: (generic path, {attr: value})}; path carries sibling discriminators, generic path does not."""
    out = {}

    def walk(e, path, gen):
        at = dict(e.attrib)
        t = (e.text or "").strip()
        if t and not len(e):
            at["#text"] = t
        out[path] = (gen, at)
        count = collections.Counter(c.tag for c in e)
        idx, used = collections.Counter(), collections.Counter()
        for c in e:
            seg = c.tag
            if count[c.tag] > 1:
                d = next((norm(c.get(k)) for k in DISC if c.get(k) is not None), None)
                if c.tag == "environment":
                    d = c.get("feature")
                seg += "[%s]" % (d if d is not None else idx[c.tag])
                idx[c.tag] += 1
                used[seg] += 1
                if used[seg] > 1:  # discriminator collision
                    seg += "#%d" % used[seg]
            walk(c, path + "/" + seg, gen + "/" + c.tag)

    walk(root, root.tag, root.tag)
    return out


NUM = re.compile(r"^-?\d+(\.\d+)?([eE][-+]?\d+)?$")


URN = re.compile(r"^(?:urn:|gamebryo-animation:)+")  # GMS asset names carry the prefix repeated up to 5 times


def norm(v):
    v = URN.sub("", " ".join(v.split()))
    if v.lower().endswith(".nif"):  # model path (KMS) and bare model name (GMS) are the same thing
        v = v.rsplit("/", 1)[-1][:-4]
    return v.lower() if v.lower() in ("true", "false") else v


def nums(v):
    parts = [p.strip() for p in v.split(",")]
    return [float(p) for p in parts] if parts and all(NUM.match(p) for p in parts) else None


def same(a, b):
    if a == b:
        return True
    x, y = nums(a), nums(b)
    return bool(x) and bool(y) and len(x) == len(y) and all(abs(p - q) <= max(1e-3, 1e-4 * abs(p)) for p, q in zip(x, y))


GMS_LOCALES, KMS_LOCALES = ("NA", ""), ("KR", "")


class Schema:
    """What one edition's files look like, learned from a sample: modal value, known attributes, known elements."""

    def __init__(self, files, locales, sample=3000):
        # TODO: a sample of the files, not all; raise `sample` if a default looks wrong
        step = max(1, len(files) // sample)
        c = collections.defaultdict(collections.Counter)
        self.els = set()
        for p in files[::step]:
            try:
                for gen, at in flatten(load(p, locales)).values():
                    self.els.add(gen)
                    for a, v in at.items():
                        c[(gen, a)][norm(v)] += 1
            except ET.ParseError:
                pass
        self.modal = {k: cnt.most_common(1)[0][0] for k, cnt in c.items()}
        self.attrs = set(c)


def compare(g, k, sch):
    """sch = {"gms": Schema, "kms": Schema}. An attribute missing on one side means that side holds the default (most
    common GMS value): ignored when the other side states the default, a value difference (default shown as ~value)
    otherwise. Fields the other edition never uses are ext_gms / ext_kms; elements present on one side only are only_*."""
    rows = []
    for path in sorted(set(g) | set(k)):
        ga, ka = g.get(path, (None, {}))[1], k.get(path, (None, {}))[1]
        gen = (g.get(path) or k.get(path))[0]
        both = path in g and path in k
        cur = []
        for attr in sorted(set(ga) | set(ka)):
            if gen == "ms2/environment" and attr == "locale":
                continue  # which region variant was picked, not content
            if attr in ga and attr in ka:
                gv, kv = norm(ga[attr]), norm(ka[attr])
                if not same(gv, kv):
                    cur.append((path, attr, gv, kv, "num" if nums(gv) and nums(kv) else "text"))
                continue
            s, o = ("gms", "kms") if attr in ga else ("kms", "gms")
            v = norm((ga if s == "gms" else ka)[attr])
            if v == "":
                continue
            row = lambda cls, ov="": (path, attr, v if s == "gms" else ov, v if s == "kms" else ov, cls)
            dflt = sch["gms"].modal.get((gen, attr))
            if dflt is not None and same(v, dflt):
                continue  # default-filled value
            elif (gen, attr) not in sch[o].attrs or (not both and gen not in sch[o].els):
                cur.append(row("ext_" + s))  # the other edition has no such field or element
            elif not both or dflt is None or attr in DISC:
                cur.append(row("only_" + s))
            else:  # the other side holds the default
                cur.append(row("num" if nums(v) and nums(dflt) else "text", "~" + dflt))
        if not both and not any(r[1] not in DISC for r in cur):
            cur = [r for r in cur if r[1] not in DISC and r[4].startswith("ext")]  # element with defaults only
        rows += cur
    return rows


def clip(s):
    return s.replace("\t", " ").replace("\n", " ")[:120]


def read_names(sdir, kind, lang):
    """-> {id: name} from <sdir>/<lang>/<table files of kind>; placeholders and empty names are dropped."""
    out = {}
    if not sdir:
        return out
    for p in sorted(glob.glob(os.path.join(sdir, lang, NAME_FILES[kind]))):
        try:
            root = load(p)
        except ET.ParseError:
            continue
        for e in root.iter():
            i, n = e.get("id") or e.get("questID"), e.get("name")
            if i and i.isdigit() and n and not PLACEHOLDER.match(n):
                out[int(i)] = n
    return out


def key_text(s):
    return re.sub(r"[^a-z0-9]+", " ", s.lower()).strip()


STOP = {"the", "of", "a", "an", "s", "and", "x"}


def en_words(s):
    return {w for w in key_text(s).split() if w not in STOP}


def kr_words(s):
    return re.findall(r"[가-힣]+|[A-Za-z0-9]+", s)


def glossary(pairs, min_count=3, min_jaccard=0.4):
    """Korean word -> English word with the highest Jaccard overlap over the (korean name, english name) pairs."""
    ck, ce, co = collections.Counter(), collections.Counter(), collections.Counter()
    for kr, en in pairs:
        ks, es = set(kr_words(kr)), en_words(en)
        ck.update(ks)
        ce.update(es)
        for k in ks:
            for e in es:
                co[(k, e)] += 1
    best = {}
    for (k, e), n in co.items():
        if ck[k] >= min_count and n >= 2:
            j = n / (ck[k] + ce[e] - n)
            if j >= min_jaccard and j > best.get(k, (0, ""))[0]:
                best[k] = (j, e)
    return {k: e for k, (j, e) in best.items()}


def name_class(gn, kn_en, kn_kr, gloss):
    if not gn:
        return "missing"
    if kn_en and key_text(gn) != key_text(kn_en):
        a, b = key_text(gn), key_text(kn_en)
        ta, tb = set(a.split()), set(b.split())
        alike = a in b or b in a or difflib.SequenceMatcher(None, a, b).ratio() >= 0.5 or (ta & tb and len(ta & tb) * 2 >= min(len(ta), len(tb)))
        return "retranslation" if alike else "different"
    words = kr_words(kn_kr or "")
    known = [gloss[w] for w in words if w in gloss]
    if not words or len(known) * 2 < len(words):
        return "unverified"
    return "same" if en_words(gn) & set(known) else "different"


_ctx = {}


def _init(sch):
    _ctx["sch"] = sch


def _work(a):
    kind, i, gp, kp = a
    try:
        rows = compare(flatten(load(gp, GMS_LOCALES)), flatten(load(kp, KMS_LOCALES)), _ctx["sch"])
    except ET.ParseError as ex:
        rows = [("", "", "", "", "parse_error:" + str(ex)[:60])]
    return "".join("%s\t%d\t%s\n" % (kind, i, "\t".join(clip(x) for x in r)) for r in rows or [("", "", "", "", "identical")])


def cmd_diff(a):
    kinds = [x for x in a.kinds.split(",") if x]
    shared_ids = {}
    with open(a.out, "w", encoding="utf-8", newline="") as out:
        out.write("kind\tid\tpath\tattr\tgms\tkms\tcls\n")
        for kind in kinds:
            gf, kf = ids_in(a.gms, kind), ids_in(a.kms, kind)
            shared_ids[kind] = shared = sorted(set(gf) & set(kf))
            print("%s: gms %d, kms %d, shared %d" % (kind, len(gf), len(kf), len(shared)), file=sys.stderr, flush=True)
            sch = {"gms": Schema([gf[i] for i in sorted(gf)], GMS_LOCALES), "kms": Schema([kf[i] for i in sorted(kf)], KMS_LOCALES)}
            with multiprocessing.Pool(initializer=_init, initargs=(sch,)) as pool:
                for text in pool.imap(_work, [(kind, i, gf[i], kf[i]) for i in shared], chunksize=64):
                    out.write(text)
    if not (a.gms_strings or a.kms_strings):
        return
    names = {k: (read_names(a.gms_strings, k, "en"), read_names(a.kms_strings, k, "en"), read_names(a.kms_strings, k, "kr")) for k in kinds}
    pairs = [(names[k][2][i], names[k][0][i]) for k in kinds for i in shared_ids[k] if i in names[k][0] and i in names[k][2]]
    gloss = glossary(pairs)
    print("glossary: %d words from %d name pairs" % (len(gloss), len(pairs)), file=sys.stderr)
    with open(os.path.splitext(a.out)[0] + ".names.tsv", "w", encoding="utf-8", newline="") as nf:
        nf.write("kind\tid\tgms_en\tkms_en\tkms_kr\tcls\n")
        for k in kinds:
            ge, ke, kk = names[k]
            for i in shared_ids[k]:
                nf.write("%s\t%d\t%s\t%s\t%s\t%s\n" % (k, i, clip(ge.get(i, "")), clip(ke.get(i, "")), clip(kk.get(i, "")), name_class(ge.get(i), ke.get(i), kk.get(i), gloss)))


def esc(s):
    return s.replace("|", "\\|") or "(empty)"


# fields that identify what a record is; a difference there that both sides state (icon and model compared by file
# name only) means the id may be a different thing
SIGNALS = {"item": ("asset@name", "tool@itemPreset", "property@slotIcon"), "npc": ("model@kfm",),
           "skill": ("ui@icon",), "quest": ("start@npc", "complete@npc")}


def identity_differs(gen, g, k):
    if g.startswith("~") or k.startswith("~") or not g or not k:
        return False
    if gen.endswith(("slotIcon", "ui@icon", "asset@name")):
        if gen.endswith("slotIcon") and not ("/" in g and "/" in k):  # "icon0.png" and bare names are placeholders
            return False
        base = lambda v: re.sub(r"\.(nif|png)$", "", v.rsplit("/", 1)[-1].lower())
        return base(g) != base(k)
    return True


def cmd_summary(a):
    ignore = re.compile(a.ignore) if a.ignore else None
    stated = set()  # (kind, id) where both sides state a different value (no implied default)
    signal = set()  # (kind, id) with an identity field that both sides state and that differs
    per = collections.defaultdict(lambda: collections.defaultdict(set))  # kind -> id -> classes
    fields = collections.defaultdict(lambda: collections.defaultdict(lambda: [set(), collections.Counter(), collections.Counter(), collections.Counter()]))
    with open(a.diff, encoding="utf-8") as f:
        next(f)
        for line in f:
            kind, i, path, attr, g, k, cls = line.rstrip("\n").split("\t")
            per[kind][i]
            if cls == "identical":
                per[kind][i].add(cls)
                continue
            gen = re.sub(r"\[[^\]]*\](#\d+)?", "", path) + "@" + attr
            if ignore and ignore.search(gen):
                continue
            per[kind][i].add(cls)
            if cls in ("num", "text") and g and k and g[0] != "~" and k[0] != "~":
                stated.add((kind, i))
            if cls in ("num", "text") and gen.endswith(SIGNALS.get(kind, ())) and identity_differs(gen, g, k):
                signal.add((kind, i))
            d = fields[kind][gen]
            d[0].add(i)
            d[1][cls] += 1
            if len(d[2]) < 300 or (g, k) in d[2]:
                d[2][(g, k)] += 1
            if cls == "num" and g and k and g[0] != "~" and k[0] != "~" and nums(g) and nums(k):
                x, y = nums(g)[0], nums(k)[0]  # first number of a list
                d[3]["KMS higher" if y > x else "KMS lower"] += 1
    print("## Counts per kind\n")
    print("| kind | shared ids compared | identical | balance only (numbers) | structure (elements, enum or text) | extension fields only | both sides state a different value |\n|---|---|---|---|---|---|---|")
    for kind in KINDS:
        if kind not in per:
            continue
        ident = bal = st = ext = 0
        for cl in per[kind].values():
            core = {c for c in cl if not c.startswith("ext_") and c != "identical"}
            if not core:
                ext += bool(cl & {"ext_gms", "ext_kms"})
                ident += not cl & {"ext_gms", "ext_kms"}
            elif core <= {"num"}:
                bal += 1
            else:
                st += 1
        print("| %s | %d | %d | %d | %d | %d | %d |" % (kind, len(per[kind]), ident, bal, st, ext, sum(1 for k, _ in stated if k == kind)))
    for kind in KINDS:
        if kind not in fields:
            continue
        print("\n## Most frequent differing attributes: %s\n" % kind)
        print("| ids | element@attribute | classes | numbers: KMS vs GMS | GMS example | KMS example |\n|---|---|---|---|---|---|")
        rank = sorted(((gen, d) for gen, d in fields[kind].items() if set(d[1]) - {"ext_gms", "ext_kms"}), key=lambda x: -len(x[1][0]))
        for gen, (ids, cls, pr, dr) in rank[:25]:
            g, k = next(((g, k) for (g, k), _ in pr.most_common() if g and k), pr.most_common(1)[0][0])
            print("| %d | `%s` | %s | %s | `%s` | `%s` |" % (len(ids), gen, ", ".join("%s %d" % c for c in cls.most_common()), ", ".join("%s %d" % c for c in dr.most_common()), esc(g[:60]), esc(k[:60])))
        ext = sorted(((gen, d) for gen, d in fields[kind].items() if set(d[1]) <= {"ext_gms", "ext_kms"}), key=lambda x: -len(x[1][0]))
        if ext:
            print("\nExtension fields, top 10: " + ", ".join("`%s` (%s, %d ids)" % (gen, "KMS only" if "ext_kms" in d[1] else "GMS only", len(d[0])) for gen, d in ext[:10]))
    if a.names:
        rows = collections.defaultdict(list)
        with open(a.names, encoding="utf-8") as f:
            next(f)
            for line in f:
                kind, i, g, ke, kk, cls = line.rstrip("\n").split("\t")
                rows[kind].append((i, g, ke, kk, cls))
        print("\n## Name comparison\n")
        cl = ["same", "retranslation", "different", "unverified", "missing"]
        print("| kind | " + " | ".join(cl) + " | identity field differs | name different and identity field differs |\n|---|" + "---|" * (len(cl) + 2))
        for kind in KINDS:
            if kind in rows:
                c = collections.Counter(r[4] for r in rows[kind])
                both = sum(1 for r in rows[kind] if r[4] == "different" and (kind, r[0]) in signal)
                print("| %s | %s | %d | %d |" % (kind, " | ".join(str(c[x]) for x in cl), sum(1 for k, _ in signal if k == kind), both))
        for kind in KINDS:
            ex = sorted((r for r in rows.get(kind, []) if r[4] == "different" or (kind, r[0]) in signal),
                        key=lambda r: not (r[4] == "different" and (kind, r[0]) in signal))[:30]
            if ex:
                print("\n### Different-meaning candidates: %s\n\n| id | GMS en | KMS en | KMS kr | name class | identity field differs |\n|---|---|---|---|---|---|" % kind)
                for i, g, ke, kk, c in ex:
                    print("| %s | %s | %s | %s | %s | %s |" % (i, esc(g), esc(ke), esc(kk), c, "yes" if (kind, i) in signal else "no"))


def cmd_selftest(_):
    assert same("0.2", "0.2000004") and same("1, 2", "1,2.0004") and not same("1", "2") and norm("TRUE") == "true"
    assert norm("urn:gamebryo-animation:urn:Data/x/a.nif") == "a" and norm("Data/Resource/Model/Item/a.nif") == "a"
    x = lambda t: flatten(ET.fromstring(t))
    sch = {"gms": Schema.__new__(Schema), "kms": Schema.__new__(Schema)}
    for e in sch.values():
        e.modal, e.attrs, e.els = {("r/a", "v"): "0", ("r/a", "w"): "5"}, {("r/a", "v"), ("r/a", "w")}, {"r", "r/a"}
    sch["kms"].attrs.add(("r/a", "ext"))
    c = lambda g, k: [(r[1], r[4]) for r in compare(x(g), x(k), sch)]
    assert c('<r><a v="0" w="5"/></r>', '<r><a/></r>') == []  # omitted side holds the default
    assert c('<r><a v="3"/></r>', '<r><a/></r>') == [("v", "num")]  # default against a real value
    assert c('<r><a v="3"/></r>', '<r><a v="4"/></r>') == [("v", "num")] and c('<r><a/></r>', '<r><a ext="1"/></r>') == [("ext", "ext_kms")]
    assert name_class("Ear", None, "귀", {"귀": "ear"}) == "same" and name_class("Ear", None, "손", {"손": "hand"}) == "different"
    assert name_class("Ear", None, "", {}) == "unverified" and name_class("", None, "귀", {}) == "missing"
    print("selftest ok")


def main():
    p = argparse.ArgumentParser(description="Shared-id comparison GMS vs KMS (U-05).")
    s = p.add_subparsers(dest="cmd", required=True)
    d = s.add_parser("diff", help="write the raw per-id diff tsv")
    d.add_argument("--gms", required=True, help="GMS per-id tree (contains item/ npc/ quest/ skill/)")
    d.add_argument("--kms", required=True, help="KmsSplit output tree (run without --na)")
    d.add_argument("--out", required=True, help="output tsv; names go to <out stem>.names.tsv")
    d.add_argument("--gms-strings", help="GMS string tree (contains en/)")
    d.add_argument("--kms-strings", help="KMS string tree (contains en/ and kr/)")
    d.add_argument("--kinds", default=",".join(KINDS))
    m = s.add_parser("summary", help="print markdown tables from the tsv")
    m.add_argument("--diff", required=True)
    m.add_argument("--names", help="the .names.tsv written by diff")
    m.add_argument("--ignore", help="regex on element@attribute (without sibling keys); matching fields are left out of every table")
    s.add_parser("selftest", help="run the built-in checks")
    a = p.parse_args()
    {"diff": cmd_diff, "summary": cmd_summary, "selftest": cmd_selftest}[a.cmd](a)


if __name__ == "__main__":
    main()
