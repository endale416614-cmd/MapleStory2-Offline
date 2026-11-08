"""Give the content pack's batch 4 quests their talk and their English text (BA-15b). The server closes an NPC talk
when the quest has no script (NpcTalkUtil.Talk, NpcTalkHandler.HandleContinue and HandleQuest), and only KMS's
script/quest/questscript_final.xml holds the scripts of the 9 batch 4 quests that talk (70000025 has none and no NPC).
Global's quest script files are byte-equal to the nightly's, so the pack build gives the server stage the overlay's.
From Global's files, KMS's script file, the nightly's talk lines and tools/data/batch4-quest-text.tsv it writes into a
pack overlay folder:
- script/quest/questscript_<file>.xml with the 9 scripts in Global's form: without KMS's illust, a portrait attribute
  no Global script has, and with each $script:<key>$ renumbered to a key of 16 digits, as every Global key is (KMS's
  have up to 20).
- string/en/scriptquest_<file>.xml with a row for each renumbered key: the nightly's English for KMS's key, or the
  table's line where the nightly has none.
- string/en/questdescription_<file>.xml and chapterdescription_<file>.xml with the table's rows for the 10 quests and
  their 3 chapters: translated from KMS, or the nightly's for 70000025 (D-16).
A file the overlay already has (lv75_quests.py's, QF-08) is the base, else Global's. A quest or key the base holds
already, a quest without its KMS script, a line with no English, Hangul in the table or a table row nothing needs
stops the build and nothing is written. --server-names gives the server stage's questdescription_final.xml a name row
for each of the 10 it lacks.
Usage: batch4_quests.py --global <dir> --kms <dir> --nightly <dir> --out <overlay dir>
       | --server-names <questdescription_final.xml> | --selftest"""
import argparse
import csv
import html
import io
import os
import re
import sys
import tempfile
import xml.etree.ElementTree as ET

TABLE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "batch4-quest-text.tsv")
HEAD = ["kind", "id", "field", "text", "source"]
FIELDS = {"quest": ("name", "desc", "manual", "complete", "count"), "line": ("name",),
          "chapter": ("name", "isTutorial")}
SOURCES = ("translated", "nightly", "copy", "kms")
# The Global file a quest's script and rows go to: the one its chapter has in KMS (world, epic, guide); levelguide for
# 70000025, as for 70000020 to 70000024. The Black Bean balloon quests 80000654 to 80000660 are held: their balloons
# are in no data. 10003455 is held too (BA-15f): its dispatch map 52100305 is in no data.
QUESTS = {10002003: "world", **{q: "epic" for q in range(10003449, 10003455)}, 70000025: "levelguide",
          90000815: "guide", 90000816: "guide"}
CHAPTERS = {5025: "world", 5035: "epic", 9086: "guide"}
SILENT = {70000025}
KMS_SCRIPT = "script/quest/questscript_final.xml"
NIGHTLY_LINES = "string/en/scriptquest.xml"
SCRIPTS = "script/quest/questscript_{}.xml"
LINES = "string/en/scriptquest_{}.xml"
ROWS = "string/en/questdescription_{}.xml"
TITLES = "string/en/chapterdescription_{}.xml"
END = "</ms2>"
HANGUL = re.compile("[ᄀ-ᇿ㄰-㆏가-힯]")
KEY = re.compile(r"\$script:(\d+)\$")
ILLUST = re.compile(r'\sillust="[^"]*"')
NIGHTLY_ROW = re.compile(r'<key id="(\d+)"[^>]*\sname="([^"]*)"')


def read(root, path):
    with open(os.path.join(root, path), encoding="utf-8-sig", newline="") as f:
        return f.read()


def attr(text):
    """Text as an attribute value, written as Global's rows write it."""
    for old, new in (("&", "&amp;"), ("<", "&lt;"), (">", "&gt;"), ('"', "&quot;"), ("'", "&apos;"), ("\n", "&#xA;")):
        text = text.replace(old, new)
    return text


def read_table(text):
    """(kind, id, field) -> text; a row out of form stops the build."""
    rows = list(csv.reader(io.StringIO(text), delimiter="\t", quoting=csv.QUOTE_NONE))
    if not rows or rows[0] != HEAD:
        raise SystemExit(f"table: the header is not {HEAD}")
    table = {}
    for row in rows[1:]:
        if (len(row) != 5 or row[0] not in FIELDS or row[2] not in FIELDS[row[0]] or row[4] not in SOURCES
                or not row[1].isdigit()):
            raise SystemExit(f"table: a row out of form: {row}")
        key = (row[0], int(row[1]), row[2])
        if key in table or HANGUL.search(row[3]) or (row[2] == "name" and not row[3]):
            raise SystemExit(f"table: {key} twice, with Hangul or without a name")
        table[key] = row[3].replace("\\n", "\n")
    return table


def with_rows(text, path, rows, tag, key):
    """The base file with the rows before its end; it must hold none of them."""
    ids = [re.search(rf'{key}="(\d+)"', row).group(1) for row in rows]
    held = [i for i in ids if re.search(rf'<{tag} {key}="0*{i}"', text)]
    if held or not text.endswith(END):
        raise SystemExit(f"{path}: holds {held[:5]} or does not end with {END}")
    return text[:-len(END)] + "".join(row + "\r\n" for row in rows) + END


def global_form(quest, block, keys):
    """A KMS quest block in Global's form: tabs, no illust, its keys renumbered (keys gets new key -> KMS key)."""
    def renumber(found):
        new = f"99{quest}{len(keys) + 1:06d}"
        keys[new] = int(found.group(1))
        return f"$script:{new}$"
    lines = []
    for line in block.split("\n"):
        body = line.lstrip(" ")
        lines.append("\t" * ((len(line) - len(body)) // 2) + KEY.sub(renumber, ILLUST.sub("", body)))
    return "\r\n".join(lines)


def build(global_dir, kms_dir, nightly_dir, out_dir, table):
    """Overlay path -> text."""
    script = read(kms_dir, KMS_SCRIPT).replace("\r\n", "\n")
    blocks = {int(m.group(1)): m.group(0)
              for m in re.finditer(r'^  <quest id="(\d+)">\n.*?^  </quest>$', script, re.S | re.M)}
    talking = sorted(set(QUESTS) - SILENT)
    if [q for q in talking if q not in blocks] or SILENT & blocks.keys():
        raise SystemExit(f"{KMS_SCRIPT}: a quest that talks has no script, or {sorted(SILENT)} has one")
    nightly = {int(m.group(1)): html.unescape(m.group(2))
               for m in NIGHTLY_ROW.finditer(read(nightly_dir, NIGHTLY_LINES))}
    used = set()

    def take(kind, row_id, field):
        if (kind, row_id, field) not in table:
            raise SystemExit(f"table: no {field} for {kind} {row_id}")
        used.add((kind, row_id, field))
        return table[(kind, row_id, field)]

    scripts, lines, rows, titles = {}, {}, {}, {}
    for quest in talking:
        keys = {}
        scripts.setdefault(QUESTS[quest], []).append(global_form(quest, blocks[quest], keys))
        for new, old in keys.items():
            english = nightly[old] if old in nightly else take("line", old, "name")
            if not english or HANGUL.search(english):
                raise SystemExit(f"line {old} of quest {quest}: no English")
            lines.setdefault(QUESTS[quest], []).append(f'\t<key id="{new}" feature="" locale="" name="{attr(english)}" />')
    for quest in sorted(QUESTS):
        name, desc, manual, complete, count = (attr(take("quest", quest, field)) for field in FIELDS["quest"])
        rows.setdefault(QUESTS[quest], []).append(
            f'\t<quest questID="{quest}" name="{name}" desc="{desc}" manual="{manual}" complete="{complete}" '
            f'count="{count}" locale="" locking="0" />')
    for chapter in sorted(CHAPTERS):
        name, tutorial = (attr(take("chapter", chapter, field)) for field in FIELDS["chapter"])
        titles.setdefault(CHAPTERS[chapter], []).append(
            f'\t<chapter id="{chapter}" name="{name}" isTutorial="{tutorial}" regionID="" />')
    if table.keys() - used:
        raise SystemExit(f"table: rows nothing needs: {sorted(table.keys() - used)[:5]}")

    files = {}
    for pattern, groups, tag, key in ((SCRIPTS, scripts, "quest", "id"), (LINES, lines, "key", "id"),
                                      (ROWS, rows, "quest", "questID"), (TITLES, titles, "chapter", "id")):
        for name, items in groups.items():
            path = pattern.format(name)
            base = out_dir if os.path.exists(os.path.join(out_dir, path)) else global_dir
            files[path] = with_rows(read(base, path), path, items, tag, key)
            ET.fromstring(files[path].encode("utf-8"))  # the client must still parse it
    return files


def server_names(path, table):
    """Adds a name row for each of the 10 the server's file lacks (QuestMapper reads only the name); returns the count."""
    with open(path, encoding="utf-8", newline="") as f:
        text = f.read()
    if not text.endswith(END):
        raise SystemExit(f"{path}: does not end with {END}")
    rows = [f'\t<quest questID="{quest}" locking="0" name="{attr(table[("quest", quest, "name")])}" />'
            for quest in sorted(QUESTS) if f'questID="{quest}"' not in text]
    with open(path, "w", encoding="utf-8", newline="") as f:
        f.write(text[:-len(END)] + "".join(row + "\r\n" for row in rows) + END)
    return len(rows)


def selftest():
    head = '<?xml version="1.0" encoding="utf-8"?>\r\n<ms2>\r\n'
    talking = sorted(set(QUESTS) - SILENT)
    shared, missing = 1234567890123456, 12345678901234567890  # a key two quests share; a key of 20 digits without a row
    with tempfile.TemporaryDirectory() as tmp:
        g, k, n, out = (os.path.join(tmp, name) for name in "gkno")

        def put(root, path, text):
            os.makedirs(os.path.dirname(os.path.join(root, path)), exist_ok=True)
            with open(os.path.join(root, path), "w", encoding="utf-8", newline="") as f:
                f.write(text)

        for name in set(QUESTS.values()) | set(CHAPTERS.values()):
            put(g, SCRIPTS.format(name), head + '\t<quest id="1">\r\n\t</quest>\r\n' + END)
            put(g, LINES.format(name), head + '\t<key id="0101010101010101" name="Held" />\r\n' + END)
            put(g, ROWS.format(name), head + '\t<quest questID="1" name="Held" />\r\n' + END)
            put(g, TITLES.format(name), head + '\t<chapter id="1" name="Held" />\r\n' + END)
        put(out, ROWS.format("epic"), head + '\t<quest questID="10003448" name="Lv 75 story" />\r\n' + END)  # lv75's
        blocks = "".join(f'  <quest id="{q}">\n    <script id="100" feature="F">\n'
                         f'      <content text="$script:{q}0001$" illust="P" speakerIllust="S" />\n'
                         f'      <content text="$script:{shared if q in (10002003, 10003449) else missing}$">\n'
                         f'        <distractor text="$script:{q}0002$" goto="101" />\n      </content>\n'
                         f'    </script>\n  </quest>\n' for q in talking)
        put(k, KMS_SCRIPT, "\ufeff" + head.replace("\r\n", "\n") + blocks + END)
        put(n, NIGHTLY_LINES, head + "".join(f'\t<key id="{q}0001" name="One {q}" />\r\n'
                                             f'\t<key id="{q}0002" name="Pick &apos;{q}&apos;" feature="F" />\r\n'
                                             for q in talking) + f'\t<key id="{shared}" name="Shared" />\r\n' + END)
        rows = ["\t".join(HEAD)] + [f"quest\t{q}\t{f}\t{f} {q}\ttranslated" for q in QUESTS for f in FIELDS["quest"]]
        rows += [f"chapter\t{c}\t{f}\t{f} {c}\tkms" for c in CHAPTERS for f in FIELDS["chapter"]]
        rows += [f"line\t{missing}\tname\tA line\\nwritten <new>\tcopy"]
        table = read_table("\r\n".join(rows) + "\r\n")

        files = build(g, k, n, out, table)
        assert len(files) == 13, sorted(files)
        epic = files[SCRIPTS.format("epic")]
        assert epic.count('<quest id="') == 7 and "illust=" not in epic.replace("speakerIllust=", ""), "illust goes"
        assert '\t\t\t<content text="$script:9910003449000001$" speakerIllust="S" />\r\n' in epic
        assert '\t\t\t\t<distractor text="$script:9910003449000003$" goto="101" />\r\n' in epic, "keys in their order"
        assert all(len(key) == 16 for key in KEY.findall(epic)) and len(KEY.findall(epic)) == 18
        world = files[LINES.format("world")]
        assert '<key id="9910002003000002" feature="" locale="" name="Shared" />' in world, "the nightly's line"
        assert '<key id="9910002003000003" feature="" locale="" name="Pick &apos;10002003&apos;" />' in world
        assert ('<key id="9990000816000002" feature="" locale="" name="A line&#xA;written &lt;new&gt;" />'
                in files[LINES.format("guide")]), "the table's line"
        epic_rows = files[ROWS.format("epic")]
        assert epic_rows.count("questID=") == 7 and 'questID="10003448"' in epic_rows, "lv75's file is the base"
        assert ('\t<quest questID="70000025" name="name 70000025" desc="desc 70000025" manual="manual 70000025" '
                'complete="complete 70000025" count="count 70000025" locale="" locking="0" />\r\n</ms2>'
                in files[ROWS.format("levelguide")])
        assert ('\t<chapter id="9086" name="name 9086" isTutorial="isTutorial 9086" regionID="" />\r\n</ms2>'
                in files[TITLES.format("guide")])
        assert all(text.count("\n") == text.count("\r\n") and text.endswith("\r\n" + END) for text in files.values())

        def refused(why, table_rows=rows, path=None, old=None, new=None, root=n):
            text = read(root, path) if path else None
            if path:
                assert old in text, old
                put(root, path, text.replace(old, new))
            try:
                build(g, k, n, out, read_table("\r\n".join(table_rows)))
                raise AssertionError(f"built with {why}")
            except SystemExit:
                pass
            if path:
                put(root, path, text)

        refused("a row nothing needs", rows + ["line\t1\tname\tUnused\tcopy"])
        refused("Hangul", [row.replace("name 9086", "힘") for row in rows])
        refused("no desc", [row for row in rows if not row.startswith("quest\t10003450\tdesc")])
        refused("a line without English", path=NIGHTLY_LINES, old='name="One 10003451"', new='name=""')
        refused("a quest the base holds", path=ROWS.format("guide"), old='questID="1"', new='questID="90000815"', root=g)
        refused("a quest without its script", path=KMS_SCRIPT, old='<quest id="90000816">', new='<quest id="90000817">',
                root=k)

        names = os.path.join(tmp, "questdescription_final.xml")
        put(tmp, "questdescription_final.xml",
            head + '\t<quest questID="70000025" locking="0" name="Reach Level 70" />\r\n' + END)
        assert server_names(names, table) == 9 and server_names(names, table) == 0
        text = read(tmp, "questdescription_final.xml")
        assert text.count("questID=") == 10 and 'name="Reach Level 70"' in text and "10003455" not in text, "BA-15f"
        assert '\t<quest questID="10003449" locking="0" name="name 10003449" />\r\n' in text

    with open(TABLE, encoding="utf-8", newline="") as f:
        real = read_table(f.read())
    need = ({("quest", q, f) for q in QUESTS for f in FIELDS["quest"]}
            | {("chapter", c, f) for c in CHAPTERS for f in FIELDS["chapter"]})
    assert need <= real.keys() and all(kind == "line" for kind, _, _ in real.keys() - need), "the table's rows"
    print("selftest ok")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--global", dest="global_dir")
    parser.add_argument("--kms")
    parser.add_argument("--nightly")
    parser.add_argument("--out")
    parser.add_argument("--server-names")
    parser.add_argument("--selftest", action="store_true")
    args = parser.parse_args()
    if args.selftest:
        selftest()
        return
    with open(TABLE, encoding="utf-8", newline="") as f:
        table = read_table(f.read())
    if args.server_names:
        print(f"{server_names(args.server_names, table)} quest names added to {args.server_names}")
        return
    files = build(args.global_dir, args.kms, args.nightly, args.out, table)
    for path, text in files.items():
        target = os.path.join(args.out, path)
        os.makedirs(os.path.dirname(target), exist_ok=True)
        with open(target, "w", encoding="utf-8", newline="") as f:
            f.write(text)
    print(f"{len(files)} files in {args.out}: {len(QUESTS) - len(SILENT)} quest scripts, {len(QUESTS)} quests, "
          f"{len(CHAPTERS)} chapters")


if __name__ == "__main__":
    sys.exit(main())
