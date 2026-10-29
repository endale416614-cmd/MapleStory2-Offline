"""Build the offline-shop client mod's item names, client-mods/offline-shop/Data/Xml/string/itemname.xml (NM-02).
The mod's file replaces the client's whole string/en/itemname.xml, with or without the content pack, so it is the pack's
own name table (the pack build's overlay-fixed/string/en/itemname.xml) plus the names restored in
tools/data/shop-item-names.tsv: every key line of a restored id is left out and one key line with its restored name is
added before </ms2>, in id order; every other line stays as the pack's file has it, with LF line ends.
It then reads the result as the client does and checks that every kept item of batches 1 to N of the selection has a
name. It writes the file only when none is missing.
Usage: shop_item_names.py --pack-names <itemname.xml> --batches N [--selection <tsv>] [--out <file>] | --selftest"""
import argparse
import html
import os
import re
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
NAMES = os.path.join(ROOT, "tools", "data", "shop-item-names.tsv")
FEATURES = os.path.join(ROOT, "tools", "data", "pack-overlay", "table", "feature.xml")
SELECTION = os.path.join(ROOT, "tools", "data", "unified-selection.tsv")
OUT = os.path.join(ROOT, "client-mods", "offline-shop", "Data", "Xml", "string", "itemname.xml")
KEY = re.compile(r'\s*<key id="(\d+)"')


def table(text):
    """Rows of a tab-separated text with a header line, as dicts. Split on LF only: str.splitlines() also splits on
    characters such as \\x1c and \\u2028 that a name may hold."""
    lines = text.split("\n")
    head = lines[0].split("\t")
    return [dict(zip(head, line.split("\t"))) for line in lines[1:] if line]


def restored(text):
    """id -> (class, name) from shop-item-names.tsv."""
    return {int(r["item"]): (r["class"], r["name"]) for r in table(text)}


def build(pack_text, names):
    lines = pack_text.replace("\r\n", "\n").split("\n")
    esc = lambda s: html.escape(s, quote=True).replace("&#x27;", "&apos;")
    out = []
    for line in lines:
        match = KEY.match(line)
        if match and int(match.group(1)) in names:
            continue
        if line.strip() == "</ms2>":
            for key_id in sorted(names):
                cls, name = names[key_id]
                out.append(f'\t<key id="{key_id}"' + (f' class="{esc(cls)}"' if cls else "") + f' name="{esc(name)}" />')
        out.append(line)
    return "\n".join(out)


def features_on(text):
    """Feature names on for NA (a value below 99)."""
    on = set()
    for f in ET.fromstring(text):
        try:
            if int(f.get("NA", "99")) < 99:
                on.add(f.get("name"))
        except ValueError:
            pass
    return on


def shown(text, on):
    """id -> the name the client shows: the last key with a name whose feature is on and whose locale is NA or unset."""
    result = {}
    for k in ET.fromstring(text.encode("utf-8")):
        feature = k.get("feature")
        if (feature and feature not in on) or k.get("locale") not in (None, "", "NA") or not k.get("name"):
            continue
        result[int(k.get("id"))] = k.get("name")
    return result


def kept(text, batches):
    return sorted(int(r["id"]) for r in table(text) if r["decision"] == "keep" and int(r["batch"]) <= batches)


def selftest():
    pack = ('<?xml version="1.0" encoding="utf-8"?>\r\n<ms2>\r\n\t<key id="1" class="hat" />\r\n'
            '\t<key id="2" class="hat" name="Mine" />\r\n\t<key id="3" class="gem" name="Gated" feature="Off" />\r\n'
            '\t<key id="3" class="gem" name="Kr" locale="KR" />\r\n\t<key id="4" name="Shown" feature="On" />\r\n</ms2>\r\n')
    names = restored("item\tclass\tname\tsource\n1\thatx\tQ & A's\tmade up\n3\t\tTag\tserver db\n")
    text = build(pack, names)
    assert "\r" not in text and text.endswith("</ms2>\n"), "LF line ends, as the pack's file ends"
    assert '\t<key id="2" class="hat" name="Mine" />\n' in text, "an unrestored line stays"
    assert text.count('id="1"') == 1 and text.count('id="3"') == 1, "every key of a restored id is replaced by one"
    assert text.index('<key id="1" class="hatx" name="Q &amp; A&apos;s" />') > text.index('id="4"'), "restored keys go last"
    assert '\t<key id="3" name="Tag" />\n</ms2>' in text, "no class attribute when the row has none; before </ms2>"
    on = features_on('<ms2><feature name="On" NA="43" /><feature name="Off" NA="99" /><feature name="Bad" NA="x" /></ms2>')
    assert on == {"On"}
    assert shown(pack.replace("\r\n", "\n"), on) == {2: "Mine", 4: "Shown"}, "gated, KR-only and empty keys show nothing"
    assert shown(text, on) == {1: "Q & A's", 2: "Mine", 3: "Tag", 4: "Shown"}
    assert build(text, names) == text, "a second run changes nothing"
    selection = "id\tname\tdecision\tbatch\n1\t\tkeep\t1\n5\t\tdrop\t1\n3\t\tkeep\t3\n6\t\tkeep\t4\n"
    assert kept(selection, 3) == [1, 3] and kept(selection, 1) == [1]
    print("selftest ok")


def main():
    if sys.argv[1:] == ["--selftest"]:
        selftest()
        return
    ap = argparse.ArgumentParser()
    ap.add_argument("--pack-names", required=True, help="the pack's string/en/itemname.xml (build: overlay-fixed)")
    ap.add_argument("--batches", required=True, type=int, choices=range(1, 5))
    ap.add_argument("--selection", default=SELECTION)
    ap.add_argument("--out", default=OUT)
    a = ap.parse_args()
    read = lambda p: open(p, encoding="utf-8").read()
    names = restored(read(NAMES))
    text = build(read(a.pack_names), names)
    named = shown(text, features_on(read(FEATURES)))
    items = kept(read(a.selection), a.batches)
    blank = [i for i in items if i not in named]
    print(f"{a.out}: {len(named)} names, {len(names)} restored; kept items of batches 1-{a.batches}: {len(items)}, without a name: {len(blank)}")
    if blank:
        print("no name: " + " ".join(map(str, blank)) + "; nothing written")
        sys.exit(1)
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, "w", encoding="utf-8", newline="") as f:
        f.write(text)


if __name__ == "__main__":
    main()
