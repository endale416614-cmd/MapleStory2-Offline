"""Give every item, NPC, map and interact object the pack ships an English name (NM-05, maintainer D-16, BT-98, BT-102).
A shipped id needs a name when the client shows it none (no line under the pack's feature.xml, a placeholder, or no
English), when a pack item shows Global's string for another item (NM-03's test: the Taiwan client does not use it), or
when the server's name of an item or NPC (the pack DB, after tools/data/copy-names.tsv) is not English. Its name is, in
D-16's order: the client's English name (when only the server lacks one), the server's, an English key line that is not
shown (behind a feature or locale gate), the Taiwan English client's one name, or else a reviewed translated or
invented row. Developer labels (an underscore, "(Test)", a leading #) are no names.
The client's NPC titles, NPC action names and NPC script lines (npctitle.xml, actionname.xml, scriptnpcnam.xml: Global's
files, which the pack did not ship before NM-06) follow the same order when the client shows one as a key or not in
English: titles and action names of shipped NPCs, and every script line (BT-110). An empty title or action is none.
The table tools/data/shown-names.tsv (kind, file, id, batch, source, source text, name, note) keeps the reviewed rows;
this tool regenerates the others from a pack build that no name table has touched, and writes nothing when a shipped id
needs a reviewed row that the table lacks, or a reviewed row names an id that needs none or has an existing name.
The KMS copies of tools/data/kms-homes.tsv (IB-02: KMS records the pack ships under a home id) count as shipped items of
batch 3, so each needs a reviewed row (source copy).
--apply writes the names into one string file (the rows whose file is its name): every key line of a listed id takes the
name (copy_names.py's strict apply), and an id with no key line free of feature and locale gets one, so the client and
the server show the name whatever their features.
Usage: shown_names.py --build <pack build dir> --global <GMS scan> --taiwan <TMS scan> [--reviewed <tsv>] [--out <tsv>]
       | --apply <tsv> <string file> --out <file> | --selftest"""
import argparse
import collections
import os
import re
import sqlite3
import sys
import xml.etree.ElementTree as ET

DATA = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, DATA)
import copy_names  # noqa: E402
import shop_item_names as shop  # noqa: E402

TABLE = os.path.join(DATA, "shown-names.tsv")
HEAD = ["kind", "file", "id", "batch", "source", "source text", "name", "note"]
FILES = {"item": "itemname.xml", "npc": "npcname.xml", "map": "mapname.xml", "object": "stringinteractobjectaction.xml",
         "title": "npctitle.xml", "action": "actionname.xml", "script": "scriptnpcnam.xml"}
REVIEWED = ("translated", "invented", "copy")
PLACEHOLDER = re.compile(r"^[A-Z]+_\d+_NAME|^NPC \d+$")  # NPC 61100150: stage-119's name for a nameless NPC (BT-106)
SCRIPTS = ((0x0E00, 0x0E7F), (0x1100, 0x11FF), (0x3040, 0x30FF), (0x3130, 0x318F), (0x3400, 0x9FFF), (0xAC00, 0xD7AF),
           (0xF900, 0xFAFF))  # Thai, Hangul jamo, kana, compatibility jamo, CJK, Hangul syllables, CJK compatibility
SCRIPT = re.compile("[" + "".join(f"{chr(a)}-{chr(b)}" for a, b in SCRIPTS) + "]")
SYMBOL = re.compile(r"[0-9!?&+.#'\- ]{1,3}")  # letter-collection items are named by one character: "2", "!", "?"
LABEL = re.compile(r"_|^\(Test\)|^#")
GATE = re.compile(r"""\s(?:feature|locale)=(?:"[^"]*"|'[^']*')""")
norm = copy_names.norm


def english(name):
    """Not empty, no placeholder, no Hangul, kana, CJK or Thai, and a letter."""
    return bool(name) and not PLACEHOLDER.match(name) and not SCRIPT.search(name) and bool(re.search("[A-Za-z]", name))


def named(name):
    return english(name) or bool(name and SYMBOL.fullmatch(name))


def usable(name):
    return english(name) and not LABEL.search(name)


def lines(text):
    """id -> [(name, feature, locale)] of every key element, in file order."""
    out = collections.defaultdict(list)
    for k in ET.fromstring(text.encode("utf-8")).iter("key"):
        out[int(k.get("id"))].append((k.get("name"), k.get("feature"), k.get("locale")))
    return out


def decide(kind, ids, client, server, check_server, sources, taiwan, foreign, reviewed, batch):
    """ids: shipped ids; client: id -> the name the client shows; server: id -> the server's name; check_server: a
    server name that is not English needs a name too; sources: [(tag, id -> lines)], in that order, last line first;
    taiwan: id -> Taiwan names; foreign: id -> Global's strings for another item (norm); reviewed: id -> table row.
    Returns (rows, ids that need a reviewed row, reviewed ids that need no name, have one or are not shipped)."""
    rows, need, stale = [], [], sorted(set(reviewed) - set(ids))
    for i in sorted(ids):
        fx = foreign.get(i, set())
        c, s = client.get(i), server.get(i)
        if named(c) and norm(c) not in fx and (not check_server or named(s)):
            stale += [i] if i in reviewed else []
            continue
        found = [("client", c.strip(), "client name")] if usable(c) and norm(c) not in fx else []
        found += [("server", s.strip(), "server name")] if usable(s) and norm(s) not in fx else []
        for tag, d in sources:
            for n, f, loc in reversed(d.get(i, [])):
                if usable(n) and norm(n) not in fx:
                    gate = " ".join(f"{a}={v}" for a, v in (("feature", f), ("locale", loc)) if v) or "ungated"
                    found.append(("gated", n.strip(), f"{tag} line {gate}"))
        tw = list({norm(n): n.strip() for n in taiwan.get(i, []) if usable(n) and norm(n) not in fx}.values())
        if not found and len(tw) == 1:
            found = [("taiwan", tw[0], "Taiwan English client")]
        if found:
            stale += [i] if i in reviewed else []
            rows.append([kind, FILES[kind], str(i), batch.get(i, "-"), found[0][0], "", found[0][1], found[0][2]])
        elif i in reviewed:
            rows.append(reviewed[i][:3] + [batch.get(i, "-")] + reviewed[i][4:])
        else:
            need.append(i)
    return rows, need, sorted(stale)


def table_text(rows):
    return "".join("\t".join(r) + "\n" for r in [HEAD] + rows)


def read_table(text):
    """Rows of a shown-names table. Split on LF only: str.splitlines() also splits on characters a name may hold."""
    rows = [line.split("\t") for line in text.replace("\r\n", "\n").split("\n") if line]
    bad = [r for r in rows[1:] if len(r) != len(HEAD) or FILES.get(r[0]) != r[1] or not r[2].isdigit() or
           not named(r[6]) or r[6] != r[6].strip() or not r[7]]
    if not rows or rows[0] != HEAD or bad:
        raise ValueError(f"not a shown-names table, or a row without an English name or a note: {(bad or rows)[:1]}")
    return rows[1:]


def add_lines(text, new):
    """(text with a key line free of feature and locale for each listed id that has none, lines added), before </ms2>
    in id order. The line is the id's first key line without its gates, or a bare key line; apply() then names it."""
    rows = text.split("\n")
    first, free = {}, set()
    for line in rows:
        m = copy_names.KEY.match(line)
        if m and int(m.group(3)) in new:
            first.setdefault(int(m.group(3)), line.strip())
            if not GATE.search(line):
                free.add(int(m.group(3)))
    end = "\r" if "\r\n" in text else ""
    added = ["\t" + GATE.sub("", first[i]) + end if i in first else f'\t<key id="{i:08d}" name="" />{end}'
             for i in sorted(set(new) - free)]
    if added:
        close = [n for n, line in enumerate(rows) if line.strip() == "</ms2>"]
        if not close:
            raise ValueError("no </ms2> line")
        rows[close[-1]:close[-1]] = added
    return "\n".join(rows), len(added)


def apply(text, new):
    """(text, key lines renamed (an added line too, when the line it copies has another name), lines added, listed
    ids it cannot rename)."""
    text, added = add_lines(text, new)
    text, changed, missing, stuck = copy_names.apply(text, new)
    return text, changed, added, sorted(set(missing) | set(stuck))


def copy_homes(text):
    """The copy ids of a kms-homes.tsv text (id, verdict, home, reason): the homes of its copy rows."""
    rows = [line.split("\t") for line in text.replace("\r\n", "\n").split("\n")[1:] if line]
    return {int(r[2]) for r in rows if r[1] == "copy"}


def measure(build, glob, taiwan, reviewed, selection, copies, restored, features, kms_homes):
    """(rows, ids that need a reviewed row, stale reviewed ids) from a pack build no name table has touched (its pack
    DB, strings-119 and overlay-fixed string files), the GMS and TMS scans, and tools/data's tables as texts."""
    rd = lambda *p: open(os.path.join(*p), encoding="utf-8", newline="").read()
    gms = lambda f: lines(rd(glob, "string", "en", f))
    tms = lambda f: {i: [n for n, *_ in v] for i, v in lines(rd(taiwan, "string", "en", f)).items()}
    client_text = lambda f: rd(build, "overlay-fixed", "string", "en", f)
    server_text = lambda f: rd(build, "strings-119", "string", "en", f)
    path = os.path.abspath(os.path.join(build, "pack", "GameData", "maple-data.db")).replace(os.sep, "/")
    db = sqlite3.connect("file:" + path + "?mode=ro", uri=True)
    q = lambda table: dict(db.execute(f'select Id, Name from "{table}"'))
    on = shop.features_on(features)
    new = copy_names.read_table(copies)
    items = copy_names.apply(client_text("itemname.xml"), new)[0]
    item_server = {i: new[i] if n and i in new else n for i, n in q("item").items()}
    pack = {int(r["id"]): r["batch"] for r in shop.table(selection)}
    homes = copy_homes(kms_homes)
    pack.update(dict.fromkeys(homes, "3"))
    item_gms, item_tms = gms("itemname.xml"), tms("itemname.xml")
    foreign = {i: {norm(n) for n, *_ in item_gms.get(i, []) if n} - {norm(n) for n in item_tms.get(i, []) if n}
               for i in pack}
    placed = {int(m) for (block,) in db.execute('select Block from "map-entity"')
              for m in re.findall(r'"InteractId":(\d+)', block)}
    objects = placed & {int(k.get("id")) for f in ("interactobject.xml", "interactobject_mastery.xml")
                        for k in ET.fromstring(rd(glob, "table", f).encode("utf-8"))}
    action, label = (shop.shown(rd(glob, "string", "en", f), on) for f in (FILES["object"], "interactname.xml"))
    labels, actions = tms("interactname.xml"), tms(FILES["object"])
    kinds = [
        ("item", set(q("item")) | homes, shop.shown(shop.build(items, shop.restored(restored)), on), item_server, True,
         [("client", lines(items)), ("server", lines(server_text("itemname.xml"))), ("GMS", item_gms)], item_tms,
         foreign, pack),
        ("npc", q("npc"), shop.shown(client_text("npcname.xml"), on), q("npc"), True,
         [("client", lines(client_text("npcname.xml"))), ("server", lines(server_text("npcname.xml"))),
          ("GMS", gms("npcname.xml"))], tms("npcname.xml"), {}, {}),
        ("map", q("map"), shop.shown(rd(glob, "string", "en", "mapname.xml"), on), q("map"), False,
         [("GMS", gms("mapname.xml"))], tms("mapname.xml"), {}, {}),
        ("object", objects, {i: label.get(i) or action.get(i) for i in objects}, {}, False,
         [("GMS", gms("interactname.xml")), ("GMS", gms(FILES["object"]))],
         {i: labels.get(i, []) + actions.get(i, []) for i in objects}, {}, {}),
    ]
    for kind in ("title", "action", "script"):  # NM-06: only a key or Hangul (or kana...) needs one; "... " is a line
        text = shop.shown(rd(glob, "string", "en", FILES[kind]), on)
        ids = [i for i, v in text.items() if (PLACEHOLDER.match(v) or SCRIPT.search(v)) and
               (kind == "script" or i in kinds[1][1])]
        kinds.append((kind, ids, text, {}, False, [("GMS", gms(FILES[kind]))], tms(FILES[kind]), {}, {}))
    rows, need, stale = [], [], []
    for kind, ids, client, server, check, sources, tw, fx, batch in kinds:
        r, n, s = decide(kind, ids, client, server, check, sources, tw, fx, reviewed.get(kind, {}), batch)
        rows, need, stale = rows + r, need + [f"{kind}:{i}" for i in n], stale + [f"{kind}:{i}" for i in s]
    return rows, need, stale


def selftest():
    kr = chr(0xD574) + chr(0xB9C8)  # Hangul
    client = {1: "Right", 2: None, 3: "ITEMNAME_3_NAME", 4: "Right", 5: "?", 6: "Lotus", 7: None, 8: None, 9: None,
              10: None, 11: None, 12: kr, 13: None, 14: "Kept"}
    server = {1: "Right", 2: "Server Name", 3: "", 4: None, 5: "?", 6: "Lotus", 7: "", 8: "", 9: "", 10: "", 11: "",
              12: "", 13: "Pink Bean_Rolling Attack", 14: "Kept"}
    sources = [("client", {2: [("Stale Develop", "Develop", None)], 3: [("Old Gated", "Off", None),
                                                                        ("Last Gated", "Off", None)],
                           8: [("x_y", "Off", None)], 14: [("Other", "Off", None)]}),
               ("server", {7: [("Server Gated", None, "CN")], 13: [("Pink Bean_Rolling Attack", None, None)]})]
    taiwan = {9: ["Taiwan Name", kr], 10: ["One", "Two"], 12: ["Apple"], 11: ["Lotus"]}
    foreign = {6: {"lotus"}, 11: {"lotus"}}
    row = lambda i, src, text, name: ["item", "itemname.xml", str(i), "3", src, text, name, "note"]
    reviewed = {6: row(6, "translated", kr, "Seahorse"), 8: row(8, "invented", "dev: x_y", "Made Up"),
                13: row(13, "invented", "Pink Bean_Rolling Attack", "Pink Bean"), 11: row(11, "translated", "k", "K"),
                10: row(10, "invented", "", "One Two"), 14: row(14, "invented", "", "Stale")}
    rows, need, stale = decide("item", range(1, 15), client, server, True, sources, taiwan, foreign, reviewed, {2: "1"})
    got = {int(r[2]): r for r in rows}
    assert got[2] == ["item", "itemname.xml", "2", "1", "server", "", "Server Name", "server name"], "server first"
    assert got[3][4:] == ["gated", "", "Last Gated", "client line feature=Off"], "the last gated line first"
    assert got[4][4:] == ["client", "", "Right", "client name"], "the client's name when only the server lacks one"
    assert 1 not in got and 5 not in got and 14 not in got, "named on both sides (a one-character name counts)"
    assert got[6][3:7] == ["-", "translated", kr, "Seahorse"], "Global's string for another item; batch from the data"
    assert got[7][4:] == ["gated", "", "Server Gated", "server line locale=CN"]
    assert got[8][6] == "Made Up" and got[13][6] == "Pink Bean", "a developer label is no name: the reviewed row"
    assert got[9][4:] == ["taiwan", "", "Taiwan Name", "Taiwan English client"], "one English Taiwan name"
    assert got[10][6] == "One Two" and got[11][6] == "K", "two Taiwan names, or Taiwan's is the foreign string"
    assert got[12][4:] == ["taiwan", "", "Apple", "Taiwan English client"], "not English: Hangul"
    assert need == [] and stale == [14], (need, stale)
    extra = {8: reviewed[8], 99: row(99, "invented", "", "X")}
    _, need, stale = decide("item", [8, 15], {}, {}, False, [], {}, {}, extra, {})
    assert need == [15] and stale == [99], "a nameless id needs a row; a row for an id that is not shipped is stale"
    _, need, stale = decide("map", [2], {2: None}, {2: "Map"}, False, [], {}, {}, {2: reviewed[8]}, {})
    assert stale == [2], "a reviewed row for an id with an existing name"
    assert decide("map", [4], {4: "Shown"}, {4: ""}, False, [], {}, {}, {}, {})[0] == [], "map server names unchecked"
    text = table_text([got[2], got[6]])
    assert read_table(text.replace("\n", "\r\n")) == [got[2], got[6]]
    no_note = text.replace("Seahorse\tnote", "Seahorse\t")
    for bad in (text.replace("Seahorse", ""), text.replace("Seahorse", kr), no_note,
                text.replace("itemname.xml\t6", "npcname.xml\t6"), "id\tname\n"):
        try:
            read_table(bad)
            raise AssertionError(bad)
        except ValueError:
            pass
    xml = ('<?xml version="1.0" encoding="utf-8"?>\r\n<ms2>\r\n\t<key id="1" class="hat" name="Old" feature="Off" />'
           "\r\n\t<key id='1' name='Older' locale=\"CN\" />\r\n\t<key id=\"2\" name=\"Open\" />\r\n"
           '\t<key id="2" name="Gated" feature="Off" />\r\n\t<key id="4" name="Keep &amp; Stay" feature="Off" />\r\n'
           '</ms2>\r\n')
    new = {1: "One & Only", 2: "Two", 3: "Three"}
    out, changed, added, stuck = apply(xml, new)
    assert '\t<key id="1" class="hat" name="One &amp; Only" />\r\n\t<key id="00000003" name="Three" />\r\n</ms2>' in out
    assert '\t<key id="1" class="hat" name="One &amp; Only" feature="Off" />\r\n' in out, "gated lines are renamed too"
    assert "\t<key id='1' name=\"One &amp; Only\" locale=\"CN\" />\r\n" in out and out.count('name="Two"') == 2
    assert '\t<key id="4" name="Keep &amp; Stay" feature="Off" />\r\n' in out and "\n" not in out.replace("\r\n", "")
    assert (changed, added, stuck) == (6, 2, []), (changed, added, stuck)
    assert shop.shown(out, {"On"}) == new, "every listed id is shown, whatever its gates"
    assert apply(out, new) == (out, 0, 0, []), "a second run changes nothing"
    assert apply(xml.replace("\r\n", "\n"), {3: "Three"})[0].endswith('\t<key id="00000003" name="Three" />\n</ms2>\n')
    split = xml.replace('<key id="2" name="Gated"', '<key id="2"\r\n name="Gated"')
    assert apply(split, {2: "Two"})[3] == [2], "a key element it cannot rename"
    assert not named("NPC 61100150") and named("NPC Guard"), "stage-119's placeholder is no name (BT-106)"
    line = '\t<key id="0603204607014948" feature="Event" locale="" npc="pc" name="SCRIPTNPCNAM_0603_NAME" />'
    out = apply(f"<ms2>\n{line}\n</ms2>\n", {603204607014948: 'A <font color="#909090">"line"</font>'})[0]
    assert '\t<key id="0603204607014948" npc="pc" name="A &lt;font color=&quot;#909090&quot;&gt;&quot;line&quot;' \
           '&lt;/font&gt;" />\n</ms2>' in out, "a script line keeps its 16 digits and its markup"
    xml = '<ms2><key id="1" feature="" locale="" name="Line" /><key id="2" locale="KR" name="Kr" /></ms2>'
    assert shop.shown(xml, set()) == {1: "Line"}, "an empty feature and locale are unset (scriptnpcnam.xml)"
    homes = copy_homes("id\tverdict\thome\treason\r\n20303049\tcopy\t20393049\tKMS\r\n11320293\ttwin\t11300308\tre-issue\r\n")
    copy = ["item", "itemname.xml", "20393049", "3", "copy", "", "Instant Gathering Voucher (50)", "copy of 20303049"]
    rows, need, stale = decide("item", homes | {20393045}, {}, {}, True, [], {}, {}, {20393049: copy},
                               dict.fromkeys(homes | {20393045}, "3"))
    assert homes == {20393049} and rows == [copy] and need == [20393045] and stale == [], "a copy needs a name (D-16)"
    print("selftest ok")


def main():
    if sys.argv[1:] == ["--selftest"]:
        selftest()
        return
    parser = argparse.ArgumentParser()
    for option in ("--build", "--taiwan", "--out"):
        parser.add_argument(option)
    parser.add_argument("--global", dest="glob")
    parser.add_argument("--reviewed", default=TABLE)
    parser.add_argument("--apply", nargs=2, metavar=("TABLE", "STRING_FILE"))
    args = parser.parse_args()
    read = lambda path: open(path, encoding="utf-8", newline="").read()
    if args.apply:
        if not args.out:
            parser.error("--apply needs --out")
        target = os.path.basename(args.apply[1])
        new = {int(r[2]): r[6] for r in read_table(read(args.apply[0])) if r[1] == target}
        text = read(args.apply[1])
        if not new or not any(line.strip() == "<ms2>" for line in text.split("\n")):
            print(f"{args.apply[1]}: no <ms2> root line, or no row of the table names {target}")
            sys.exit(1)
        text, changed, added, stuck = apply(text, new)
        if stuck:
            print(f"{args.apply[1]}: a key element it cannot rename for " + " ".join(map(str, stuck)) +
                  "; nothing written")
            sys.exit(1)
        with open(args.out, "w", encoding="utf-8", newline="") as f:
            f.write(text)
        print(f"{args.out}: {len(new)} ids, {changed} key lines changed, {added} lines added")
        return
    if not (args.build and args.glob and args.taiwan):
        parser.error("the table needs --build, --global and --taiwan")
    reviewed = collections.defaultdict(dict)
    for r in read_table(read(args.reviewed)) if os.path.exists(args.reviewed) else []:
        if r[4] in REVIEWED:
            reviewed[r[0]][int(r[2])] = r
    tools = lambda name: read(os.path.join(DATA, name)).replace("\r\n", "\n")  # a CRLF checkout (NM-06)
    rows, need, stale = measure(args.build, args.glob, args.taiwan, reviewed, tools("unified-selection.tsv"),
                                tools("copy-names.tsv"), tools("shop-item-names.tsv"),
                                tools(os.path.join("pack-overlay", "table", "feature.xml")), tools("kms-homes.tsv"))
    if need or stale:
        for what, ids in (("need a translated or invented row", need), ("have a reviewed row they do not need", stale)):
            if ids:
                print(f"{len(ids)} shipped ids {what}: " + " ".join(ids))
        print("nothing written")
        sys.exit(1)
    out = args.out or TABLE
    with open(out, "w", encoding="utf-8", newline="") as f:
        f.write(table_text(rows))
    kinds, sources = collections.Counter(r[0] for r in rows), collections.Counter(r[4] for r in rows)
    print(f"{out}: {len(rows)} names (" + ", ".join(f"{k} {kinds[k]}" for k in FILES) + "; " +
          ", ".join(f"{s} {sources[s]}" for s in ("client", "server", "gated", "taiwan") + REVIEWED) + ")")


if __name__ == "__main__":
    main()
