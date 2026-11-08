"""Open the Lv 75 story in the content pack (QF-08, BT-20). The server reads the pack's quest files and the client reads
them and its own strings, so one overlay opens the story and gives it its text together. From Global's files and the
nightly's English it writes into a pack overlay folder:
- quest/10003399.xml without its prerequisite 10003390: neither DB holds that quest, and it and the 28 quests before it
  (10003120 on, chapter 5030) need maps and NPCs the data lacks.
- quest/10003440.xml to 10003443.xml and script/quest/questscript_epic.xml with those four quests behind EpicApocalypse01:
  the server has UnlimitedEnchant on and the NA client has it off, which the pack keeps, as the client's code reads it.
- string/en/questdescription_epic.xml, scriptquest_epic.xml and chapterdescription_epic.xml with the story's rows, which
  Global lacks: the nightly's, whose names the server already shows. A row that is missing, blank, a key or not English
  stops the build and nothing is written (D-16).
Usage: lv75_quests.py --global <dir> --nightly <dir> --out <overlay dir> | --selftest"""
import argparse
import os
import re
import sys
import tempfile

ROOT_QUEST = 10003399
MISSING = 10003390
STORY = [*range(10003399, 10003434), *range(10003440, 10003449)]
MOVED = range(10003440, 10003444)
CHAPTERS = range(5031, 5035)
OLD_GATE, NEW_GATE = 'feature="UnlimitedEnchant"', 'feature="EpicApocalypse01"'
SCRIPT = "script/quest/questscript_epic.xml"
QUESTS = "string/en/questdescription_epic.xml"
LINES = "string/en/scriptquest_epic.xml"
TITLES = "string/en/chapterdescription_epic.xml"
NIGHTLY_QUESTS = "string/en/questdescription_final.xml"
NIGHTLY_LINES = "string/en/scriptquest.xml"
END = "</ms2>"
NOT_ENGLISH = re.compile("[ᄀ-ᇿ㄰-㆏가-힯]|^QUESTDESCRIPTION_|^SCRIPTQUEST_")
SCRIPT_QUEST = re.compile(r'<quest id="(\d+)">(.*?)</quest>', re.S)


def read(root, path):
    with open(os.path.join(root, path), encoding="utf-8", newline="") as f:
        return f.read()


def rows_by_id(text, tag, key):
    """id -> the row's line: in these files a row is one line."""
    pattern = re.compile(rf'\t<{tag} {key}="(\d+)"')
    return {int(m.group(1)): line for line in text.split("\r\n") if (m := pattern.match(line))}


def story_row(rows, row_id, *names):
    """The row with its gate moved, when each named attribute holds English."""
    row = rows.get(int(row_id), "")
    for name in names:
        found = re.search(rf'\s{name}="([^"]*)"', row)
        if not found or not found.group(1) or NOT_ENGLISH.search(found.group(1)):
            raise SystemExit(f"row {row_id}: no English {name}")
    return row.replace(OLD_GATE, NEW_GATE)


def with_rows(global_dir, path, tag, key, rows):
    """Global's file with the rows before its end; Global must not hold them already."""
    text = read(global_dir, path)
    held = rows_by_id(text, tag, key).keys() & {int(re.search(rf'{key}="(\d+)"', row).group(1)) for row in rows}
    if held or not text.endswith(END):
        raise SystemExit(f"{path}: holds {sorted(held)} or does not end with {END}")
    return text[:-len(END)] + "".join(row + "\r\n" for row in rows) + END


def build(global_dir, nightly_dir):
    """Overlay path -> text."""
    files = {}
    root = read(global_dir, f"quest/{ROOT_QUEST}.xml")
    if root.count(f'quest="{MISSING}"') != 1:
        raise SystemExit(f"quest {ROOT_QUEST}: does not require {MISSING} once")
    files[f"quest/{ROOT_QUEST}.xml"] = root.replace(f'quest="{MISSING}"', 'quest=""')
    for quest in MOVED:
        text = read(global_dir, f"quest/{quest}.xml")
        if text.count(OLD_GATE) != 1:
            raise SystemExit(f"quest {quest}: not behind UnlimitedEnchant once")
        files[f"quest/{quest}.xml"] = text.replace(OLD_GATE, NEW_GATE)

    script = read(global_dir, SCRIPT)
    bodies = {int(quest): body for quest, body in SCRIPT_QUEST.findall(script)}
    gated = {quest for quest, body in bodies.items() if OLD_GATE in body}
    if gated != set(MOVED) or not set(STORY) <= bodies.keys():
        raise SystemExit(f"{SCRIPT}: {sorted(gated)} behind UnlimitedEnchant, or a story quest has no script")
    files[SCRIPT] = script.replace(OLD_GATE, NEW_GATE)

    # Global's script writes 16 digits; the nightly's rows drop a leading zero.
    lines = [line for quest in STORY for line in re.findall(r"\$script:(\d+)\$", bodies[quest])]
    nightly_lines = rows_by_id(read(nightly_dir, NIGHTLY_LINES), "key", "id")
    files[LINES] = with_rows(global_dir, LINES, "key", "id",
                             [re.sub(r'<key id="\d+"', f'<key id="{line}"', story_row(nightly_lines, line, "name"), count=1) for line in lines])
    nightly_quests = rows_by_id(read(nightly_dir, NIGHTLY_QUESTS), "quest", "questID")
    files[QUESTS] = with_rows(global_dir, QUESTS, "quest", "questID",
                              [story_row(nightly_quests, quest, "name", "desc", "manual", "complete") for quest in STORY])
    nightly_titles = rows_by_id(read(nightly_dir, TITLES), "chapter", "id")
    files[TITLES] = with_rows(global_dir, TITLES, "chapter", "id", [story_row(nightly_titles, chapter, "name") for chapter in CHAPTERS])
    return files


def selftest():
    head = '<?xml version="1.0" encoding="utf-8"?>\r\n<ms2>\r\n'
    with tempfile.TemporaryDirectory() as tmp:
        g, n, out = (os.path.join(tmp, name) for name in "gno")

        def put(root, path, text):
            os.makedirs(os.path.dirname(os.path.join(root, path)), exist_ok=True)
            with open(os.path.join(root, path), "w", encoding="utf-8", newline="") as f:
                f.write(text)

        def refused(root, path, old, new):
            text = read(root, path)
            assert old in text, old
            put(root, path, text.replace(old, new))
            try:
                build(g, n)
                raise AssertionError(f"built with {new}")
            except SystemExit:
                pass
            put(root, path, text)

        put(g, f"quest/{ROOT_QUEST}.xml", f'<require level="75" quest="{MISSING}" selectableQuest="" />\r\n')
        for quest in MOVED:
            put(g, f"quest/{quest}.xml", f'<environment {OLD_GATE} locale="">\r\n')
        put(g, SCRIPT, head + "".join(f'\t<quest id="{q}">\r\n\t\t<script {OLD_GATE if q in MOVED else NEW_GATE}>\r\n'
                                      f'\t\t\t<content text="$script:0{q}$" />\r\n\t\t</script>\r\n\t</quest>\r\n' for q in STORY) + END)
        put(g, QUESTS, head + '\t<quest questID="1" name="Held" />\r\n' + END)
        put(g, LINES, head + '\t<key id="01" name="Held" />\r\n' + END)
        put(g, TITLES, head + '\t<chapter id="1" name="Held" />\r\n' + END)
        put(n, NIGHTLY_QUESTS, head + "".join(f'\t<quest questID="{q}" desc="D{q}" manual="M" complete="C" count="0" name="N{q}" '
                                              f'{OLD_GATE if q in MOVED else NEW_GATE} />\r\n' for q in STORY) + END)
        put(n, NIGHTLY_LINES, head + "".join(f'\t<key id="{q}" name="L{q}" npc="pc" {OLD_GATE if q in MOVED else NEW_GATE} />\r\n' for q in STORY) + END)
        put(n, TITLES, head + "".join(f'\t<chapter id="{c}" name="T{c}" isTutorial="OK" {NEW_GATE} />\r\n' for c in CHAPTERS) + END)

        files = build(g, n)
        assert files[f"quest/{ROOT_QUEST}.xml"] == '<require level="75" quest="" selectableQuest="" />\r\n'
        assert files["quest/10003440.xml"] == f'<environment {NEW_GATE} locale="">\r\n'
        assert OLD_GATE not in files[SCRIPT] and files[SCRIPT].count("<quest id=") == 44
        for path, held, added in ((QUESTS, 'questID="1"', 44), (LINES, 'id="01"', 44), (TITLES, 'id="1"', 4)):
            text = files[path]
            assert OLD_GATE not in text and held in text and text.endswith("\r\n" + END), path
            assert text.count("\n") == text.count("\r\n") == 2 + 1 + added, path
        assert '<key id="010003440" name="L10003440"' in files[LINES], "the leading zero of the script's id stays"
        assert f'questID="10003440" desc="D10003440" manual="M" complete="C" count="0" name="N10003440" {NEW_GATE}' in files[QUESTS]

        refused(n, NIGHTLY_QUESTS, 'name="N10003403"', 'name=""')  # blank
        refused(n, NIGHTLY_QUESTS, 'name="N10003403"', 'name="트라이아"')  # Hangul
        refused(n, NIGHTLY_QUESTS, 'desc="D10003403"', 'desc="QUESTDESCRIPTION_10003403_DESC"')  # a key
        refused(n, NIGHTLY_LINES, 'id="10003403"', 'id="9"')  # missing
        refused(n, TITLES, 'name="T5033"', 'name=""')
        refused(g, QUESTS, 'questID="1"', 'questID="10003403"')  # Global holds it
        refused(g, f"quest/{ROOT_QUEST}.xml", f'quest="{MISSING}"', 'quest="1"')  # another prerequisite
        refused(g, SCRIPT, f'<quest id="10003403">\r\n\t\t<script {NEW_GATE}>', f'<quest id="10003403">\r\n\t\t<script {OLD_GATE}>')  # one more quest behind the gate

        for path, text in build(g, n).items():
            put(out, path, text)
        assert read(out, LINES) == files[LINES]
    print("selftest ok")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--global", dest="global_dir")
    parser.add_argument("--nightly")
    parser.add_argument("--out")
    parser.add_argument("--selftest", action="store_true")
    args = parser.parse_args()
    if args.selftest:
        selftest()
        return
    if not (args.global_dir and args.nightly and args.out):
        parser.error("--global, --nightly and --out are required (or --selftest)")
    files = build(args.global_dir, args.nightly)
    for path, text in files.items():
        target = os.path.join(args.out, path)
        os.makedirs(os.path.dirname(target), exist_ok=True)
        with open(target, "w", encoding="utf-8", newline="") as f:
            f.write(text)
    print(f"{len(files)} files in {args.out}: {len(STORY)} quests, {len(MOVED)} moved behind EpicApocalypse01")


if __name__ == "__main__":
    sys.exit(main())
