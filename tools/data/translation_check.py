"""Translation tracking per language (TL-01): checks the text the client shows from its string files,
the content pack's file where the pack ships one and Global's otherwise, for each language the pack ships, and fails on
any problem that tools/data/translation-baseline.tsv does not list, and on any listed problem that is gone.
A problem is, in a text attribute (name and the description ones):
  encoding     U+FFFD, or UTF-8 read with a Western code page ("Ã©", "â€")
  script       Hangul, kana, CJK or Thai in a language that does not use them
  placeholder  a $ outside a $name$ or $name:value$ token, or braces that do not pair
  key          a string key instead of text (ITEMNAME_123_NAME, "NPC 123", or the id itself)
and, in the pack's own files of shipped names, an id whose every key line lacks a name (missing).
The baseline (language, file, id, attribute, problem, text) keeps what is known and tracked; --write-baseline writes the
current problems into it.
Usage: translation_check.py --pack <pack build>/overlay-fixed/string --global <GMS scan>/string [--baseline <tsv>]
       [--write-baseline] | --selftest"""
import argparse
import html
import os
import re
import sys
import tempfile

DATA = os.path.dirname(os.path.abspath(__file__))
BASELINE = os.path.join(DATA, "translation-baseline.tsv")
HEAD = ["language", "file", "id", "attribute", "problem", "text"]
TEXT = ("name", "tooltipDescription", "guideDescription", "uiDescription", "mainDescription", "value")
NAMED = ("itemname.xml", "npcname.xml", "mapname.xml", "petname.xml", "stringinteractobjectaction.xml")
OWN_SCRIPT = {"kr": "Hangul", "jp": "kana", "cn": "CJK", "tw": "CJK", "th": "Thai"}  # languages that use them
SCRIPTS = ((0x0E00, 0x0E7F), (0x1100, 0x11FF), (0x3040, 0x30FF), (0x3130, 0x318F), (0x3400, 0x9FFF), (0xAC00, 0xD7AF),
           (0xF900, 0xFAFF))  # Thai, Hangul jamo, kana, compatibility jamo, CJK, Hangul syllables, CJK compatibility
SCRIPT = re.compile("[" + "".join(f"{chr(a)}-{chr(b)}" for a, b in SCRIPTS) + "]")
MOJIBAKE = re.compile("�|[ÂÃìí][\u0080-¿]|â€")
TOKEN = re.compile(r"\$\w+(?::[^$]*)?\$")
KEY = re.compile(r"^[A-Z]+_\d+_NAME|^NPC \d+$")
LINE = re.compile(r"<key\b([^>]*)>")
ATTR = re.compile(r'\s(\w+)="([^"]*)"')


def problems(language, text, key_id):
    found = []
    if MOJIBAKE.search(text):
        found.append("encoding")
    if language not in OWN_SCRIPT and SCRIPT.search(text):
        found.append("script")
    if "$" in TOKEN.sub("", text) or text.count("{") != text.count("}"):
        found.append("placeholder")
    if KEY.search(text) or text == key_id:
        found.append("key")
    return found


def check_file(language, name, text, own):
    """Rows (language, file, id, attribute, problem, text) of one string file; own: the pack ships it."""
    rows, named, ids = [], set(), []
    for line in LINE.finditer(text):
        attrs = dict(ATTR.findall(line.group(1)))
        key_id = attrs.get("id", "")
        ids.append(key_id)
        for attribute in TEXT:
            value = html.unescape(attrs.get(attribute, ""))
            if not value.strip():
                continue
            if attribute == "name":
                named.add(key_id)
            rows += [[language, name, key_id, attribute, problem, value] for problem in problems(language, value, key_id)]
    if own and name in NAMED:
        rows += [[language, name, key_id, "name", "missing", ""] for key_id in sorted(set(ids) - named)]
    return rows


def measure(pack, glob):
    rows = []
    for language in sorted(os.listdir(pack)):
        own = set(os.listdir(os.path.join(pack, language)))
        shipped = set(os.listdir(os.path.join(glob, language))) if os.path.isdir(os.path.join(glob, language)) else set()
        for name in sorted(own | shipped):
            if not name.endswith(".xml"):
                continue
            path = os.path.join(pack if name in own else glob, language, name)
            with open(path, encoding="utf-8-sig", errors="replace") as f:
                rows += check_file(language, name, f.read(), name in own)
    return rows


def clean(value):
    return value.replace("\t", " ").replace("\r", " ").replace("\n", " ")


def table_text(rows):
    return "\n".join("\t".join([clean(v) for v in row[:5]] + [clean(row[5])[:80]]) for row in [HEAD] + rows) + "\n"  # the text, for review, cut


def read_table(text):
    lines = text.splitlines()
    if not lines or lines[0].split("\t") != HEAD:
        raise SystemExit(f"translation baseline: the first line must be {HEAD}")
    return [line.split("\t") for line in lines[1:] if line]


def compare(rows, baseline):
    """(new, gone): problems the baseline does not list, listed problems that are gone (by all columns but the text)."""
    now = {tuple(row[:5]) for row in rows}
    known = {tuple(row[:5]) for row in baseline}
    return [row for row in rows if tuple(row[:5]) not in known], sorted(known - now)


def selftest():
    good = '<ms2><key id="1" name="Apple" /><key id="2" name="Talk to $npcName:11000001$." /><key id="3" name="{0} left" /></ms2>'
    assert check_file("en", "itemname.xml", good, True) == [], "good lines"
    kr = chr(0xD574) + chr(0xB9C8)
    cases = {
        '<key id="4" name="CafÃ©" />': "encoding",
        '<key id="5" name="' + kr + '" />': "script",
        '<key id="6" name="Talk to $npcName:11000001." />': "placeholder",
        '<key id="7" name="{0 left" />': "placeholder",
        '<key id="8" name="ITEMNAME_8_NAME" />': "key",
        '<key id="9" />': "missing",
    }
    for line, problem in cases.items():
        rows = check_file("en", "itemname.xml", "<ms2>" + line + "</ms2>", True)
        assert [row[4] for row in rows] == [problem], (line, rows)
    assert check_file("kr", "itemname.xml", '<key id="5" name="' + kr + '" />', True) == [], "Hangul is Korean's own"
    assert check_file("en", "npctitle.xml", '<key id="9" />', True) == [], "an empty title is none"
    assert check_file("en", "itemname.xml", '<key id="9" />', False) == [], "Global's own file: no missing check"
    with tempfile.TemporaryDirectory() as root:
        pack, glob = os.path.join(root, "pack", "en"), os.path.join(root, "global", "en")
        os.makedirs(pack)
        os.makedirs(glob)
        for folder, text in ((pack, good), (glob, '<key id="1" name="' + kr + '" />')):
            with open(os.path.join(folder, "itemname.xml"), "w", encoding="utf-8") as f:
                f.write(text)
        with open(os.path.join(glob, "itemnameplural.xml"), "w", encoding="utf-8") as f:
            f.write('<key id="1" name="' + kr + ':s" />')
        rows = measure(os.path.dirname(pack), os.path.dirname(glob))
        assert [row[1:5] for row in rows] == [["itemnameplural.xml", "1", "name", "script"]], rows  # the pack's file wins
        baseline = read_table(table_text(rows))
        assert compare(rows, baseline) == ([], []), "the baseline covers its own rows"
        added = rows + check_file("en", "itemname.xml", '<key id="4" name="CafÃ©" />', True)
        assert [row[4] for row in compare(added, baseline)[0]] == ["encoding"], "an added problem fails"
        assert compare([], baseline)[1] == [("en", "itemnameplural.xml", "1", "name", "script")], "a fixed problem is gone"
    print("selftest ok")


def main():
    if sys.argv[1:] == ["--selftest"]:
        selftest()
        return 0
    parser = argparse.ArgumentParser()
    parser.add_argument("--pack", required=True)
    parser.add_argument("--global", dest="glob", required=True)
    parser.add_argument("--baseline", default=BASELINE)
    parser.add_argument("--write-baseline", action="store_true")
    args = parser.parse_args()
    rows = measure(args.pack, args.glob)
    if args.write_baseline:
        with open(args.baseline, "w", encoding="utf-8", newline="\n") as f:
            f.write(table_text(rows))
        print(f"{len(rows)} problems written to {args.baseline}")
        return 0
    with open(args.baseline, encoding="utf-8") as f:
        new, gone = compare(rows, read_table(f.read()))
    for row in new:
        print("new: " + "\t".join(clean(v) for v in row))
    for row in gone:
        print("gone: " + "\t".join(row))
    print(f"{len(rows)} problems, {len(new)} new, {len(gone)} gone (run --write-baseline after a fix)")
    return 1 if new or gone else 0


if __name__ == "__main__":
    sys.exit(main())
