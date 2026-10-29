"""Name the KMS copies after the item they copy (NM-03, maintainer D-12, BT-97): a KMS item that is a copy of another
item but carries the Global name of a different item takes its twin's English name.
Twins are items with the same KMS (Korean) name that the selection calls the same look (a verdict duplicate-of or
duplicate-of-global names the other) or whose item file's icon or look preset (the selection's fingerprint preset:<id>)
is the other item's; twins of twins are twins too (a group). A group with a Global item takes that item's Global name,
and every item of the group that Global does not ship gets it. In a group without one, an item whose name is Global's
string for its id (Global names another item there and ships none) takes the name of the group's other items when they
all have that one name. Groups whose names disagree keep their names. A kept or held item is renamed when it has no
name or one of its names (server or client string file) differs. The table (id, twin, old name, new name, batch,
reason) is applied by --apply to the server stage's and the client's itemname.xml, so
both show the same name.
Taiwan names (maintainer D-13): a kept item the twins do not name, one of whose names is Global's string for its id
while the Taiwan English client's names for it differ (Global names another item there), takes the Taiwan client's
one English name. Look and server names (maintainer D-14): such an item without an English Taiwan name takes the
Global name of its look preset (the fingerprint's preset:<id>, an item Global ships) with its own gender mark (the
fingerprint's g0 (M), g1 (F)), or else its one English server name that is not Global's string. Such an item with
none of these names takes a translated or invented name from tools/data/shown-names.tsv (NM-05, D-16).
Global fixes (NM-04, maintainer D-15, BT-103): FIXES, a reviewed list, corrects Global's own name of a Global item
where it names another slot. The twin rule uses the fixed name, and the item itself gets a row (batch -).
--apply renames every key line of a listed id, single- or double-quoted, splitting lines on LF only; it writes nothing
when a listed id has no key element, or has one that does not carry the new name afterwards (no single key line).
Usage: copy_names.py --selection <tsv> --overlay <dir> --server-names <xml> --client-names <xml> --global-names <xml>
       --kms-names <xml> --taiwan-names <xml> --global-db <db> [--out <tsv>] | --apply <tsv> <itemname.xml> --out <file>
       | --selftest"""
import argparse
import collections
import csv
import html
import os
import re
import sqlite3
import sys
import xml.etree.ElementTree as ET

TABLE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "copy-names.tsv")
HEAD = ["id", "twin", "old name", "new name", "batch", "reason"]
PLACEHOLDER = re.compile(r"^ITEMNAME_\d+_NAME")
ICON = re.compile(rb'slotIcon="[^"]*?([0-9]{8})\.png"')
PRESET = re.compile(r"preset:(\d+)")
GENDER = re.compile(r"\|g([01])\|")
MARK = re.compile(r" \((?:M|F)\)$")
KEY = re.compile(r"""^(\s*)<key id=(["'])(\d+)\2(.*?)\s*/>\s*$""")
NAME = re.compile(r"""\sname=(?:"[^"]*"|'[^']*')""")
# Reviewed fixes of Global's own names (D-15): id -> (Global's name, fixed name, and the evidence: the item file's
# slot, the string files' class, the KMS name). 11620059 and 11620060 are the Street Rapper gloves (KMS jang-gap,
# gloves); Global's name is that of the Street Rapper earrings 11220059 and 11220060.
FIXES = {
    11620059: ("Street Rapper Earrings (M)", "Street Rapper Gloves (M)", "GL", "gloves", "스트리트 래퍼 장갑 (남)"),
    11620060: ("Street Rapper Earrings (F)", "Street Rapper Gloves (F)", "GL", "gloves", "스트리트 래퍼 장갑 (여)"),
}


def norm(name):
    """Name for comparing: lower case, letters and digits only (unified_selection.py's norm)."""
    return re.sub(r"[^0-9a-z]+", "", (name or "").lower())


def real(name):
    """A name that is neither empty nor a string key placeholder (ITEMNAME_<id>_NAME...)."""
    return bool(name) and not PLACEHOLDER.match(name)


def names(text):
    """id -> the real names of its key lines, in file order."""
    out = collections.defaultdict(list)
    for key in ET.fromstring(text.encode("utf-8")):
        if real(key.get("name")):
            out[int(key.get("id"))].append(key.get("name"))
    return out


def decide(rows, icons, server, client, global_names, kms, taiwan, glob, fixes):
    """rows: id -> selection row (verdict, fingerprint, decision, batch); icons: id -> the id its item file's icon names;
    server, client, global_names, kms, taiwan: id -> names; glob: Global id -> Global name; fixes: as FIXES.
    Returns (rows of the table, groups that keep their names)."""
    for i, fix in fixes.items():
        if glob.get(i) != fix[0]:
            raise ValueError(f"fix list: Global's name for {i} is {glob.get(i)!r}, not {fix[0]!r}")
    glob = {**glob, **{i: fix[1] for i, fix in fixes.items()}}
    kname = lambda i: kms[i][0] if kms.get(i) else None
    parent = {}

    def find(i):
        parent.setdefault(i, i)
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    links = [(i, int(r["verdict"].split(":", 1)[1])) for i, r in rows.items() if ":" in r["verdict"]]
    links += [(i, t) for i, t in icons.items() if t != i]
    presets = {i: PRESET.search(r.get("fingerprint", "")) for i, r in rows.items()}
    links += [(i, int(m.group(1))) for i, m in presets.items() if m and int(m.group(1)) != i]
    for a, b in links:
        if kname(a) and kname(a) == kname(b):
            parent[find(a)] = find(b)
    groups = collections.defaultdict(set)
    for i in list(parent):
        groups[find(i)].add(i)
    own = lambda i: list(dict.fromkeys(server.get(i, []) + client.get(i, [])))
    global_named = lambda i: i not in glob and bool({norm(n) for n in own(i)} & {norm(n) for n in global_names.get(i, [])})
    renames, kept = [], []
    for group in groups.values():
        pool = sorted(i for i in group if i in glob and real(glob[i]))
        if pool:
            given = {i: glob[i] for i in pool}
            targets = [i for i in group if i not in glob]
            why = "same KMS name as Global item {}"
        else:
            pool = sorted(i for i in group if i not in glob and not global_named(i))
            given = {i: own(i)[0] if len({norm(n) for n in own(i)}) == 1 else None for i in pool}
            targets = [i for i in group if global_named(i)]
            why = "same KMS name as pack item {}; the old name is Global's name for another item"
        if not pool or not targets:
            continue
        if None in given.values() or len({norm(n) for n in given.values()}) != 1:
            kept.append(sorted(group))
            continue
        new = given[pool[0]]
        for i in sorted(targets):
            r = rows.get(i)
            if r and r["decision"] in ("keep", "hold") and (not own(i) or any(norm(n) != norm(new) for n in own(i))):
                renames.append((i, pool[0], " | ".join(own(i)), new, r["batch"], why.format(pool[0])))
    named = {r[0] for r in renames}
    # TODO: English means ASCII with a letter (no Hangul or CJK); widen it if a name needs accents.
    english = lambda ns: list({norm(n): n for n in ns if n.isascii() and re.search("[A-Za-z]", n)}.values())
    no_name = "the old name is Global's string for this id and the Taiwan client has no English name for it; "
    for i, r in sorted(rows.items()):
        if r["decision"] != "keep" or i in glob or i in named:
            continue
        foreign = {norm(n) for n in global_names.get(i, [])} - {norm(n) for n in taiwan.get(i, [])}
        if not any(norm(n) in foreign for n in own(i)):
            continue
        old, tw = " | ".join(own(i)), english(taiwan.get(i, []))
        look = PRESET.search(r.get("fingerprint", ""))
        partner = int(look.group(1)) if look else None
        mine = [n for n in english(server.get(i, [])) if norm(n) not in foreign]
        if len(tw) == 1:
            renames.append((i, "-", old, tw[0], r["batch"],
                            "taiwan-name: the old name is Global's string for this id; the Taiwan English client's name"))
        elif not tw and partner in glob and real(glob[partner]):
            gender = GENDER.search(r["fingerprint"])
            mark = (" (M)", " (F)")[int(gender.group(1))] if gender else ""
            renames.append((i, "-", old, MARK.sub("", glob[partner]) + mark, r["batch"],
                            f"look-name: {no_name}the Global name of its look preset {partner}, with its own gender mark"))
        elif not tw and len(mine) == 1:
            renames.append((i, "-", old, mine[0], r["batch"], f"server-name: {no_name}its one other English server name"))
    for i, fix in fixes.items():
        if not own(i) or any(norm(n) != norm(fix[1]) for n in own(i)):
            renames.append((i, "-", " | ".join(own(i)), fix[1], "-",
                            "global-fix: Global's own name for this item names another slot; the tool's reviewed fix list"))
    return sorted(renames), sorted(kept)


def table_text(renames):
    return "".join("\t".join(map(str, row)) + "\n" for row in [HEAD] + renames)


def read_table(text):
    """id -> new name. Split on LF only: str.splitlines() also splits on characters a name may hold."""
    lines = text.replace("\r\n", "\n").split("\n")
    if lines[0].split("\t") != HEAD:
        raise ValueError(f"not a copy-names table: {lines[0]!r}")
    return {int(c[0]): c[3] for c in (line.split("\t") for line in lines[1:] if line)}


def apply(text, new):
    """(text with new[id] on every key line of each listed id, lines changed, listed ids without a key element, listed
    ids with a key element that does not carry the new name afterwards). Lines are split on LF only (str.splitlines()
    also splits inside a name, on U+2028 and the like), so CRLF and every other byte stay; the name is escaped as the
    server file writes it."""
    esc = lambda s: html.escape(s, quote=True).replace("&#x27;", "&apos;")
    lines = text.split("\n")
    changed = 0
    for index, line in enumerate(lines):
        match = KEY.match(line)
        if not match or int(match.group(3)) not in new:
            continue
        body = line.rstrip()
        end = line[len(body):]
        attribute = f' name="{esc(new[int(match.group(3))])}"'
        if NAME.search(body):
            body = NAME.sub(lambda _: attribute, body, 1)
        else:
            body = re.sub(r"\s*/>$", lambda _: attribute + " />", body)
        if body + end != line:
            changed += 1
            lines[index] = body + end
    text = "\n".join(lines)
    key_id = lambda k: int(k.get("id")) if re.fullmatch(r"\s*\+?\d+\s*", k.get("id") or "") else None
    keys = [k for k in ET.fromstring(text.encode("utf-8")).iter("key") if key_id(k) in new]
    stuck = sorted({key_id(k) for k in keys if k.get("name") != new[key_id(k)]})
    return text, changed, sorted(set(new) - {key_id(k) for k in keys}), stuck


def selftest():
    row = lambda verdict, decision="keep", batch="1", look="": {"verdict": verdict, "decision": decision, "batch": batch,
                                                                 "fingerprint": look}
    rows = {2: row("unique"), 3: row("duplicate-of:2", batch="3"), 4: row("unique"), 10: row("unique"),
            11: row("duplicate-of:10"), 20: row("unique"), 21: row("duplicate-of:20"), 32: row("duplicate-of-global:31"),
            40: row("unique"), 41: row("unique", "drop"), 42: row("unique"), 43: row("unique"), 44: row("unique", "hold"),
            51: row("duplicate-of-global:50"), 60: row("unique", look="1|16|g1|lv1|jall|-|preset:61"),
            62: row("unique", look="1|16|g0|lv1|jall|-|preset:61")}
    icons = {2: 1, 4: 1, 32: 30, 40: 1, 41: 1, 42: 1, 43: 1, 44: 1, 60: 63}
    glob = {1: "Right", 30: "X1", 31: "X2", 50: "ITEMNAME_50_NAME:[F]Event", 61: "Look (F)", 63: "Look (M)"}
    kms = {i: ["K1"] for i in (1, 2, 3, 40, 41, 42, 43, 44)}
    kms.update({4: ["K2"], 10: ["K3"], 11: ["K3"], 20: ["K4"], 21: ["K4"], 30: ["K5"], 31: ["K5"], 32: ["K5"],
                50: ["K6"], 51: ["K6"], 60: ["K7"], 61: ["K7"], 62: ["K8"], 63: ["K8"]})
    server = {1: ["Right"], 2: ["Wrong"], 3: ["Wrong Too"], 4: ["Other"], 10: ["Other Item"], 11: ["Real"], 20: ["A"],
              21: ["B"], 32: ["Y"], 40: ["Right"], 41: ["Wrong"], 43: ["Right"], 44: ["Wrong"], 51: ["Z"],
              60: ["Wrong Look"], 62: ["Other Look"]}
    client = {2: ["Wrong"], 42: ["Right"], 43: ["Wrong"], 51: ["Z2"]}
    placeholder = '<ms2><key id="42" name="ITEMNAME_42_NAME:[F]Develop" feature="Develop" /><key id="42" name="" /></ms2>'
    assert names(placeholder) == {}, "placeholders and empty names are no names"
    global_names = {10: ["Other Item"], 2: ["Wrong"]}
    renames, kept = decide(rows, icons, server, client, global_names, kms, {}, glob, {})
    got = {r[0]: r for r in renames}
    assert sorted(got) == [2, 3, 10, 43, 44, 60], sorted(got)
    assert got[60][1:4] == (61, "Wrong Look", "Look (F)"), "a look preset links a Global twin; its icon has another KMS name"
    assert 62 not in got, "a look preset whose item has another KMS name links nothing"
    assert got[2] == (2, 1, "Wrong", "Right", "1", "same KMS name as Global item 1"), "an icon links a Global twin"
    assert got[3][3] == "Right" and got[3][4] == "3", "twins of twins (verdict, then icon) are one group"
    assert got[43][2] == "Right | Wrong", "server names first, then client names; any differing name renames"
    assert got[10][1:4] == (11, "Other Item", "Real") and got[10][5].startswith("same KMS name as pack item 11; "), \
        "a Global-named item takes its pack twin's name"
    assert not {4, 40, 41, 42} & set(got), "another KMS name, already right, dropped, placeholder only"
    assert 20 not in got and 21 not in got, "no Global twin and no Global-named item: names stay"
    assert [30, 31, 32] in kept and 32 not in got, "Global twins with different names: the group keeps its names"
    assert 51 not in got, "a Global twin with a placeholder name gives no name"
    # Taiwan names (D-13): 70-77 show Global's string for their id, 72's Taiwan name is that string too; 77's server
    # name is right, its client name is Global's string; 71's is half Korean. Global's 1 and the twin row 2 take none.
    rows.update({1: row("unique"), 70: row("unique", batch="3"), 71: row("unique"), 72: row("unique"),
                 73: row("unique", "hold"), 74: row("unique", "drop"), 75: row("unique"), 76: row("unique"),
                 77: row("unique")})
    foreign = {i: [f"Another Item {i}"] for i in range(70, 78)}
    server.update(foreign)
    server[77] = ["Right Server"]
    client[77] = foreign[77]
    global_names.update(foreign)
    global_names[1] = ["Right"]
    taiwan = {1: ["Not Used"], 2: ["Not Used"], 70: ["Taiwan Name"], 71: ["(PC) " + chr(0xB2E4) + chr(0xB984)],
              72: ["Another Item 72"], 73: ["Held Name"], 74: ["Dropped Name"], 76: ["One", "Two"], 77: ["Right Server"]}
    renames2, _ = decide(rows, icons, server, client, global_names, kms, taiwan, glob, {})
    tw = {r[0]: r for r in renames2 if r[1] == "-"}
    assert [r for r in renames2 if r[1] != "-"] == renames, "twin rows stay; a twin-named item takes no Taiwan name"
    assert tw[70] == (70, "-", "Another Item 70", "Taiwan Name", "3", "taiwan-name: the old name is Global's string "
                      "for this id; the Taiwan English client's name"), tw[70]
    assert tw[77][2:4] == ("Right Server | Another Item 77", "Right Server"), "any name that is Global's string renames"
    assert sorted(tw) == [70, 77], "not Korean only, Taiwan's own string, held, dropped, no line, two names, Global's item"
    # Look and server names (D-14): 80-87 show Global's string for their id. 80 (a Korean Taiwan line), 81 (another
    # English server name too) and 82 take their look preset's Global name with their own gender mark (g1, g0, g2); 83's
    # preset is no Global item; 84 has no preset (its icon is Global's 1, not a look) and one other English server name,
    # 85 two; 86 is held; 87 and 76 have Taiwan names.
    look = lambda g, p: f"1|12|g{g}|lv1|jall|-|preset:{p}"
    rows.update({80: row("unique", look=look(1, 64)), 81: row("unique", look=look(0, 65)),
                 82: row("unique", look=look(2, 64)), 83: row("unique", look=look(2, 66)),
                 84: row("unique", batch="3", look="25|0|g2|lv1|jall|-|icon:icon/00000001.png"), 85: row("unique"),
                 86: row("unique", "hold", look=look(1, 64)), 87: row("unique", look=look(1, 64)),
                 76: row("unique", look=look(1, 64))})
    glob.update({64: "Partner Look (M)", 65: "Tail"})
    icons[84] = 1
    foreign = {i: [f"Another Item {i}"] for i in range(80, 88)}
    server.update(foreign)
    server.update({81: ["Another Item 81", "Own Tail"], 84: ["Another Item 84", "(PC) Own Name"],
                   85: ["Another Item 85", "Own A", "Own B"]})
    global_names.update(foreign)
    taiwan.update({80: [chr(0xB2E4)], 84: ["(PC) " + chr(0xB2E4)], 87: ["Taiwan Wins"]})
    renames3, _ = decide(rows, icons, server, client, global_names, kms, taiwan, glob, {})
    got = {r[0]: r for r in renames3 if r[0] >= 76}
    assert got[80] == (80, "-", "Another Item 80", "Partner Look (F)", "1", "look-name: the old name is Global's string "
                       "for this id and the Taiwan client has no English name for it; the Global name of its look preset "
                       "64, with its own gender mark"), got[80]
    assert (got[81][3], got[82][3]) == ("Tail (M)", "Partner Look"), "a mark added, none for both; the look comes first"
    assert got[84][2:] == ("Another Item 84 | (PC) Own Name", "(PC) Own Name", "3", "server-name: the old name is "
                           "Global's string for this id and the Taiwan client has no English name for it; its one other "
                           "English server name"), got[84]
    assert got[87][3:] == ("Taiwan Wins", "1", tw[70][5]), "a Taiwan name comes before the look preset"
    assert sorted(got) == [77, 80, 81, 82, 84, 87], "no Global preset, two server names, held, two Taiwan names"
    assert [r for r in renames3 if r[0] < 76] == [r for r in renames2 if r[0] < 76], "the other rows stay"
    text = table_text(renames)
    assert text.startswith("id\ttwin\told name\tnew name\tbatch\treason\n2\t1\tWrong\tRight\t1\t") and text.endswith("\n")
    assert read_table(text.replace("\n", "\r\n")) == {2: "Right", 3: "Right", 10: "Real", 43: "Right", 44: "Right",
                                                      60: "Look (F)"}
    # Global fixes (D-15): Global 90's own name names another slot. Its copy 91 (same KMS name, preset 90) and 90 itself
    # take the fixed name; a fix whose Global name is not the listed one stops the run.
    fix = {90: ("Rapper Earrings (M)", "Rapper Gloves (M)", "GL", "gloves", "K9")}
    rows9, kms9, glob9 = {91: row("unique", look="1|16|g0|lv1|jall|-|preset:90")}, {90: ["K9"], 91: ["K9"]}, {90: fix[90][0]}
    server9 = {90: ["Rapper Earrings (M)"], 91: ["Rapper Earrings (M)"]}
    assert decide(rows9, {}, server9, {91: ["Rapper Gloves (M)"]}, {}, kms9, {}, glob9, fix)[0] == [
        (90, "-", "Rapper Earrings (M)", "Rapper Gloves (M)", "-", "global-fix: Global's own name for this item names "
         "another slot; the tool's reviewed fix list"),
        (91, 90, "Rapper Earrings (M) | Rapper Gloves (M)", "Rapper Gloves (M)", "1", "same KMS name as Global item 90")]
    assert decide(rows9, {}, server9, {}, {}, kms9, {}, glob9, {})[0] == [], "without the fix 91 keeps 90's Global name"
    assert decide(rows9, {}, {90: ["Rapper Gloves (M)"], 91: ["Rapper Gloves (M)"]}, {}, {}, kms9, {}, glob9, fix)[0] == []
    try:
        decide(rows9, {}, server9, {}, {}, kms9, {}, {90: "Rapper Hat (M)"}, fix)
        raise AssertionError("a fix whose Global name is not the listed one must stop the run")
    except ValueError:
        pass
    xml = ('<?xml version="1.0" encoding="utf-8"?>\r\n<ms2>\r\n\t<key id="2" class="ride" name="Wrong" />\r\n'
           "\t<key id='3' name='x' />\r\n\t<key id=\"2\" name='Old &apos;1&apos;' feature=\"Wedding\" />\r\n"
           '\t<key id="3" class="hat" />\r\n\t<key id="5" name="Keep &amp; Stay" />\r\n'
           '\t<key id="6" name="Line\u2028Break\x85Next" />\r\n\t<key id="7" name="Para\u2029graph" />  \r\n</ms2>\r\n')
    out, changed, missing, stuck = apply(xml, {2: "Q & A's", 3: "Right", 7: "Seven", 99: "None"})
    assert '\t<key id="2" class="ride" name="Q &amp; A&apos;s" />\r\n' in out, "double-quoted name, escaped"
    assert '\t<key id="2" name="Q &amp; A&apos;s" feature="Wedding" />\r\n' in out, "single-quoted name, every key line"
    assert '\t<key id="3" class="hat" name="Right" />\r\n' in out, "a key line without a name gets one"
    assert "\t<key id='3' name=\"Right\" />\r\n" in out, "a single-quoted id is renamed too"
    assert '\t<key id="6" name="Line\u2028Break\x85Next" />\r\n' in out, "U+2028 and NEL in a name split no line"
    assert '\t<key id="7" name="Seven" />  \r\n' in out, "a name with U+2029 is renamed; trailing blanks and CRLF stay"
    assert '\t<key id="5" name="Keep &amp; Stay" />\r\n' in out and out.count("\r\n") == xml.count("\r\n")
    assert out.count("\n") == xml.count("\n") and (changed, missing, stuck) == (5, [99], []), (changed, missing, stuck)
    assert apply(out, {2: "Q & A's", 3: "Right", 7: "Seven"})[1:] == (0, [], []), "a second run changes nothing"
    # A key element of a listed id that is no single key line is not renamed, even when another key line of the id is.
    for bad in ('\t<key id="8"\r\n\t\tname="Split" />\r\n', '\t<key id="8" name="Open"></key>\r\n',
                '\t<key id="5" name="A" /><key id="8" name="Second" />\r\n', '\t<key  id="8" name="Two Blanks" />\r\n',
                '\t<key id=" 8" name="Blank" />\r\n', '\t<key id="+8" name="Sign" />\r\n'):
        assert apply(xml.replace("</ms2>", '\t<key id="8" />\r\n' + bad + "</ms2>"), {8: "Eight"})[2:] == ([], [8]), bad
    ET.fromstring(out.split("\n", 1)[1])
    print("selftest ok")


def main():
    if sys.argv[1:] == ["--selftest"]:
        selftest()
        return
    parser = argparse.ArgumentParser()
    for option in ("--selection", "--overlay", "--server-names", "--client-names", "--global-names", "--kms-names",
                   "--taiwan-names", "--global-db"):
        parser.add_argument(option)
    parser.add_argument("--apply", nargs=2, metavar=("TABLE", "ITEMNAME_XML"))
    parser.add_argument("--out")
    args = parser.parse_args()
    read = lambda path: open(path, encoding="utf-8", newline="").read()
    if args.apply:
        if not args.out:
            parser.error("--apply needs --out")
        new = read_table(read(args.apply[0]))
        text = read(args.apply[1])
        if not any(line.strip() == "<ms2>" for line in text.split("\n")):
            print(f"{args.apply[1]}: no <ms2> root line")
            sys.exit(1)
        text, changed, missing, stuck = apply(text, new)
        for what, ids in (("no key line for", missing), ("a key element it cannot rename for", stuck)):
            if ids:
                print(f"{args.apply[1]}: {what} " + " ".join(map(str, ids)) + "; nothing written")
                sys.exit(1)
        with open(args.out, "w", encoding="utf-8", newline="") as f:
            f.write(text)
        print(f"{args.out}: {len(new)} ids, {changed} key lines changed")
        return
    needed = ("selection", "overlay", "server_names", "client_names", "global_names", "kms_names", "taiwan_names",
              "global_db")
    if any(getattr(args, n) is None for n in needed):
        parser.error("the table needs " + ", ".join("--" + n.replace("_", "-") for n in needed))
    rows = {int(r["id"]): r for r in csv.DictReader(open(args.selection, encoding="utf-8", newline=""), delimiter="\t")}
    icons = {}
    for folder, _, files in os.walk(os.path.join(args.overlay, "item")):
        for name in files:
            if name[:-4].isdigit():
                match = ICON.search(open(os.path.join(folder, name), "rb").read())
                if match:
                    icons[int(name[:-4])] = int(match.group(1))
    db = sqlite3.connect("file:" + os.path.abspath(args.global_db).replace(os.sep, "/") + "?mode=ro", uri=True)
    glob = dict(db.execute("select Id, Name from item"))
    texts = [names(read(p)) for p in (args.server_names, args.client_names, args.global_names, args.kms_names,
                                       args.taiwan_names)]
    renames, kept = decide(rows, icons, *texts, glob, FIXES)
    out = args.out or TABLE
    with open(out, "w", encoding="utf-8", newline="") as f:
        f.write(table_text(renames))
    by = collections.Counter(r[4] for r in renames if r[4] != "-")
    kind = collections.Counter(r[5].split(":")[0] for r in renames if r[1] == "-")
    print(f"{out}: {len(renames)} renamed (" + ", ".join(f"batch {b}: {by[b]}" for b in sorted(by)) +
          f"), twins disagree: {len(kept)}, Taiwan names: {kind['taiwan-name']}, look names: {kind['look-name']}, "
          f"server names: {kind['server-name']}, Global fixes: {kind['global-fix']}")


if __name__ == "__main__":
    main()
