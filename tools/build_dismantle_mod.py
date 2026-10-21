#!/usr/bin/env python3
"""
Builds the client mod client-mods/offline-dismantle: the client's own item XML of every "(Navigator)" gear item
with <limit enableBreak="1">, so the client lets them into the dismantler (the server allows them in
ItemDismantleHandler.CanDismantle). The client refuses an item whose data says enableBreak="0"
("This item cannot be dismantled.", s_itembreak_invalid_item) before it sends anything to the server.

Usage:
    ClientDiff --extract <client>/Data/Xml.m2d <xmlDir> item/1/
    python tools/build_dismantle_mod.py --xml <xmlDir> [--db SaveData/maple-data.db] [--out client-mods/offline-dismantle]
    python tools/build_dismantle_mod.py --selftest
"""
import argparse
import json
import os
import re
import sqlite3
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PREFIX = "(Navigator)"  # same rule as ItemDismantleHandler.CanDismantle
LIMIT_BREAK = re.compile(r'(<limit\b[^>]*?\benableBreak=")0(")')
MOD_JSON = {
    "id": "offline-dismantle",
    "name": "Offline edition: dismantle Navigator gear",
    "description": "(Navigator) gear from the level reward boxes can be dismantled (enableBreak=1).",
}


def navigator_gear(db_path):
    db = sqlite3.connect(f"file:{db_path}?mode=ro", uri=True)
    rows = db.execute("SELECT Id, Name, Property FROM item").fetchall()
    return sorted(i for i, name, prop in rows if (name or "").startswith(PREFIX) and json.loads(prop).get("Type") == 1)


def patch(text):
    """Every <limit enableBreak="0"> becomes "1"; returns (text, count)."""
    return LIMIT_BREAK.subn(r"\g<1>1\g<2>", text)


def rel(item_id):
    s = str(item_id)
    return os.path.join("item", s[0], s[1:3], s + ".xml")


def build(xml_dir, db_path, out_dir):
    ids = navigator_gear(db_path)
    if not ids:
        sys.exit("no (Navigator) gear in " + db_path)
    for item_id in ids:
        with open(os.path.join(xml_dir, rel(item_id)), encoding="utf-8", newline="") as f:
            text, count = patch(f.read())
        if count == 0:
            sys.exit(f"{item_id}: no <limit enableBreak=\"0\"> to change")
        dest = os.path.join(out_dir, "Data", "Xml", rel(item_id))
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        with open(dest, "w", encoding="utf-8", newline="") as f:
            f.write(text)
    with open(os.path.join(out_dir, "mod.json"), "w", encoding="utf-8") as f:
        json.dump(MOD_JSON, f, indent=2)
    print(f"{len(ids)} item files written to {out_dir}")


def selftest():
    text, n = patch('<limit levelLimit="35" transferType="2" shopSell="0" enableBreak="0" exceptEnchant="0" />')
    assert n == 1 and 'enableBreak="1"' in text and 'enableBreak="0"' not in text, text
    text, n = patch('<limit enableBreak="0" /><limit enableBreak="1" />')  # one per <environment>
    assert n == 1 and text.count('enableBreak="1"') == 2, text
    assert patch('<limit enableBreak="1" />')[1] == 0
    assert patch('<property enableBreak="0" />')[1] == 0, "only <limit> is changed"
    assert rel(11800101) == os.path.join("item", "1", "18", "11800101.xml")
    print("selftest ok")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--xml")
    parser.add_argument("--db", default=os.path.join(ROOT, "SaveData", "maple-data.db"))
    parser.add_argument("--out", default=os.path.join(ROOT, "client-mods", "offline-dismantle"))
    parser.add_argument("--selftest", action="store_true")
    args = parser.parse_args()
    if args.selftest:
        selftest()
    elif args.xml:
        build(args.xml, args.db, args.out)
    else:
        parser.error("--xml or --selftest")
