"""Give the content pack's client the scene text its trigger scripts name (QF-09, BT-188). The server sends a script's
$key$ unchanged and the client looks it up in string/en/stringtrigger.xml, where Global lacks 1,118 of the keys. From
Global's file, the nightly's file and trigger scripts, and tools/data/scene-text.tsv it writes Global's file into a pack
overlay folder, with a row for each key the scripts name outside a comment and Global has no text for:
- the nightly's row where it holds English, without its feature gate: 11 of its 12 features are off on the NA client and
  no id has two rows, so the gate can only hide the line;
- otherwise the table's row, a line translated or, where no client holds one, written new (D-16). A Global row with a
  blank or placeholder name takes the table's name and keeps everything else.
LEFT_BLANK stays blank, and every other Global row stays as it is. A key with no text, Hangul in the table or a table row
nothing needs stops the build and nothing is written.
Usage: scene_text.py --global <dir> --nightly <dir> --out <overlay dir> | --selftest"""
import argparse
import os
import re
import sys
import tempfile
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape

TABLE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "scene-text.tsv")
HEAD = ["id", "source", "npc", "type", "name"]
SOURCES = ("translated", "invented", "copy")
STRINGS = "string/en/stringtrigger.xml"
END = "</ms2>"
TOKEN = re.compile(r'\$([^$"\s<>]+)\$')
COMMENT = re.compile(r"<!--.*?-->", re.S)
FILLED_IN = re.compile(r"MyPCName$|(?:npc|npcName|NpcName|map|pp):")  # the client's own substitutions, not keys
HANGUL = re.compile("[ᄀ-ᇿ㄰-㆏가-힯]")
GATE = re.compile(r'\sfeature="[^"]*"')
NAME = re.compile(r"""(\sname=)(?:"[^"]*"|'[^']*')""")
# Blank in every client, on purpose: an illustration slot (the first) and caption lines whose other line carries the text.
LEFT_BLANK = {"52000108_QD__52000108__45", "52000109_QD__52000109__18", "52000139_QD__MAIN__23",
              "52000146_QD__52000146_MAIN__1", "52020036_QD__MAIN__1"}


def read(root, path):
    with open(os.path.join(root, path), encoding="utf-8", newline="") as f:
        return f.read()


def has_text(name):
    return bool(name) and not HANGUL.search(name) and not name.startswith("STRINGTRIGGER_")


def rows(text):
    """id -> (name, line): in these files a row is one line."""
    found = {}
    for line in text.split("\r\n"):
        if line.startswith("\t<key "):
            key = ET.fromstring(line.strip())
            found[key.get("id")] = (key.get("name"), line)
    return found


def keys_named(nightly_dir):
    """Every key the trigger scripts name outside a comment."""
    keys = set()
    for folder, _, names in os.walk(os.path.join(nightly_dir, "trigger")):
        for name in names:
            with open(os.path.join(folder, name), encoding="utf-8-sig", newline="") as f:
                keys.update(k for k in TOKEN.findall(COMMENT.sub("", f.read())) if not FILLED_IN.match(k))
    return keys


def read_table(text):
    """id -> row of a scene text table. Split on LF only: str.splitlines() also splits on characters a name may hold."""
    lines = [line.split("\t") for line in text.replace("\r\n", "\n").split("\n") if line]
    bad = [r for r in lines[1:] if len(r) != len(HEAD) or r[1] not in SOURCES or not has_text(r[4]) or
           any(not v or v != v.strip() or HANGUL.search(v) for v in r)]
    if not lines or lines[0] != HEAD or bad:
        raise SystemExit(f"not a scene text table, or a row without English: {(bad or lines)[:1]}")
    return {r[0]: dict(zip(HEAD, r)) for r in lines[1:]}


def build(global_dir, nightly_dir, table_text):
    """Global's file with the rows. Raises SystemExit, having written nothing, when a key has no text."""
    text = read(global_dir, STRINGS)
    if not text.endswith(END):
        raise SystemExit(f"{STRINGS}: does not end with {END}")
    held, nightly, table = rows(text), rows(read(nightly_dir, STRINGS)), read_table(table_text)
    added, used, missing = [], set(), []
    for key in sorted(keys_named(nightly_dir)):
        name = held[key][0] if key in held else None
        if has_text(name) or (key in LEFT_BLANK and name == ""):
            continue
        if key not in held and key in nightly and has_text(nightly[key][0]):
            # A line break in the form Global's rows use: 127 write \n, none &#xA;.
            added.append(GATE.sub("", nightly[key][1]).replace("&#xA;", "\\n"))
        elif key in table:
            used.add(key)
            shown = escape(table[key]["name"], {"'": "&apos;", '"': "&quot;"})
            if key in held:
                text = text.replace(held[key][1], NAME.sub(lambda m: f'{m.group(1)}"{shown}"', held[key][1], count=1))
            else:
                added.append(f'\t<key id="{key}" name="{shown}" npc="{table[key]["npc"]}" type="{table[key]["type"]}" />')
        else:
            missing.append(key)
    if missing or table.keys() - used:
        raise SystemExit(f"no English for {missing[:10]} ({len(missing)}), or table rows nothing needs: {sorted(table.keys() - used)[:10]}")
    text = text[:-len(END)] + "".join(row + "\r\n" for row in added) + END
    ET.fromstring(text.encode("utf-8"))  # the client must still parse it
    return text


def selftest():
    head = '<?xml version="1.0" encoding="utf-8"?>\r\n<ms2>\r\n'
    with tempfile.TemporaryDirectory() as tmp:
        g, n, out = (os.path.join(tmp, name) for name in "gno")

        def put(root, path, text):
            os.makedirs(os.path.dirname(os.path.join(root, path)), exist_ok=True)
            with open(os.path.join(root, path), "w", encoding="utf-8", newline="") as f:
                f.write(text)

        held = '\t<key id="1_BF__MAIN__0" name=\'Say &quot;hi&quot;\' npc="하렌" type="SideNpcTalk" feature="CBT" />\r\n'
        blank = '\t<key feature="Quest2017_2" id="1_BF__MAIN__1" name="" npc="하렌" type="AddCinematicTalk" />\r\n'
        placeholder = '\t<key id="1_BF__MAIN__2" name="STRINGTRIGGER_1_BF__MAIN__2_NAME:[F]Event" npc="player" type="AddBalloonTalk" feature="Event" />\r\n'
        left = '\t<key id="52000139_QD__MAIN__23" name="" npc="systemMSG" type="ShowCaption" />\r\n'
        other = '\t<key id="02000410_BF__BARRICADE_GIVEUP_0" name="Closing" npc="systemMSG" />\r\n'
        put(g, STRINGS, head + held + blank + placeholder + left + other + END)
        put(n, STRINGS, head + '\t<key id="1_BF__MAIN__3" type="SetEventUI" name="One&#xA;Two" npc="systemMSG" feature="DarkTear" />\r\n'
            '\t<key id="1_BF__MAIN__9" name="Not named" npc="player" />\r\n' + END)
        put(n, "trigger/1_bf/main.xml", '<ms2><!-- $1_BF__MAIN__9$ $1_BF__MAIN__10$ -->\r\n'
            '<action msg="$1_BF__MAIN__0$" /><action msg="$1_BF__MAIN__1$" /><action msg="$1_BF__MAIN__2$" /><action msg="$1_BF__MAIN__3$" />'
            '<action msg="$1_BF__MAIN__4$" /><action msg="$52000139_QD__MAIN__23$" /><action arg2="$02000410_BF__BARRICADE_GIVEUP_0$" />'
            '<action msg="$npcName:11$ $NpcName:11$ $npc:11$ $map:2$ $pp:를,을$ $MyPCName$" /></ms2>')
        table = "\t".join(HEAD) + "\n" + "".join(f"{k}\t{s}\tNotUse\tAddBalloonTalk\t{t}\n" for k, s, t in (
            ("1_BF__MAIN__1", "invented", "..."), ("1_BF__MAIN__2", "translated", "I won!"), ("1_BF__MAIN__4", "invented", "It's me")))

        lines = build(g, n, table).split("\r\n")
        assert lines[2] == held.rstrip(), "a row with text stays byte for byte"
        assert lines[3] == blank.rstrip().replace('name=""', 'name="..."'), "a blank row takes the name and keeps the rest"
        assert lines[4] == placeholder.rstrip().replace("STRINGTRIGGER_1_BF__MAIN__2_NAME:[F]Event", "I won!"), "so does a placeholder"
        assert lines[5] == left.rstrip() and lines[6] == other.rstrip(), "left blank, and a key of another form with text"
        assert lines[7] == '\t<key id="1_BF__MAIN__3" type="SetEventUI" name="One\\nTwo" npc="systemMSG" />', "the nightly's row, no gate, Global's line break"
        assert lines[8] == '\t<key id="1_BF__MAIN__4" name="It&apos;s me" npc="NotUse" type="AddBalloonTalk" />', "the table's row in Global's form"
        assert lines[9] == END and len(lines) == 10, "a key only in a comment gets no row, and no substitution is a key"

        def refused(root, path, old, new, with_table=table):
            text = read(root, path)
            assert old in text, old
            put(root, path, text.replace(old, new))
            try:
                build(g, n, with_table)
                raise AssertionError(f"built with {new}")
            except SystemExit:
                pass
            put(root, path, text)

        refused(n, "trigger/1_bf/main.xml", "$1_BF__MAIN__0$", "$1_BF__MAIN__77$")  # in no row
        refused(n, STRINGS, 'name="One&#xA;Two"', 'name=""')  # the nightly's row is blank
        refused(n, STRINGS, 'name="One&#xA;Two"', 'name="하렌"')  # or Hangul
        refused(n, STRINGS, 'name="One&#xA;Two"', 'name="STRINGTRIGGER_1_BF__MAIN__3_NAME"')  # or a placeholder
        refused(g, STRINGS, 'name=\'Say &quot;hi&quot;\'', 'name=""')  # a blank row with no table row
        refused(g, STRINGS, END, "")  # a file cut short
        refused(n, "trigger/1_bf/main.xml", "$1_BF__MAIN__4$", "$1_BF__MAIN__0$")  # a table row nothing needs
        refused(g, STRINGS, 'name="Closing"', 'name="Closing"', table + "1_BF__MAIN__3\tinvented\tNotUse\tSetEventUI\tThe nightly has it\n")  # so is one the nightly covers
        for bad in ("It's me", ""), ("It's me", "하렌"), ("It's me", "STRINGTRIGGER_X"), ("invented", "other"), ("NotUse", "하렌"):
            refused(g, STRINGS, 'name="Closing"', 'name="Closing"', table.replace(*bad))

        # The real table: no row is blank or Hangul. With these files all of it is stale, so the run stops and writes nothing.
        with open(TABLE, encoding="utf-8") as f:
            assert len(read_table(f.read())) == 148
        try:
            main(["--global", g, "--nightly", n, "--out", out])
            raise AssertionError("wrote with a stale table")
        except SystemExit:
            assert not os.path.exists(out)
    print("selftest ok")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--global", dest="global_dir")
    parser.add_argument("--nightly")
    parser.add_argument("--out")
    parser.add_argument("--selftest", action="store_true")
    args = parser.parse_args(argv)
    if args.selftest:
        selftest()
        return
    with open(TABLE, encoding="utf-8") as f:
        text = build(args.global_dir, args.nightly, f.read())
    target = os.path.join(args.out, STRINGS)
    os.makedirs(os.path.dirname(target), exist_ok=True)
    with open(target, "w", encoding="utf-8", newline="") as f:
        f.write(text)
    print(f"{target}: {text.count(chr(10)) - 2} rows")


if __name__ == "__main__":
    sys.exit(main())
