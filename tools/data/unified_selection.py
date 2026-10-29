"""M2c-04: which content-pack items stay in the 1.2.0 pack (maintainer 2026-10-20: drop the duplicates).

Input: the fingerprint report of content_fingerprint.py (one row per region-only item), the pack's maple-data.db and
the pack's client XML overlay (the per-id files appended to Xml.m2d). Only items in the pack are judged. A duplicate
has the same fingerprint AND the same name (lower case, letters and digits only; the db's Name): one look serves
several items (weapon skins of one model, mounts and furniture with a placeholder icon, badge tiers), so the look
alone is not enough (M2c-04 spot check: 153 of 882 look-only matches had another name, most of them other items).
- unique, without a fingerprint or without a name: stay;
- a duplicate of a Global item: dropped, Global has the item;
- duplicates among pack items: one of them stays (the referenced ones, else the lowest id), the rest are dropped.
Variants are no duplicates (maintainer 2026-10-20, Q-M2c-4, M2c-06): an item whose client XML shows another rarity
(<property rank>), duration (<life>) or trade setting (<limit> transfer and trade attributes) than its twin stays.
--global-xml (the Global client's extracted XML, e.g. _ingest/scan/GMS) gives the Global twins' files; without it a
Global twin counts as the same variant.
A duplicate is dropped only when nothing names its id: no db row except its own item row, no overlay quest, NPC,
skill or table file, no other item file's <basic originID> (item files otherwise name other ids only for their look),
no extra text file (--extra: the servers' offline-*.json, the client mods). Referenced duplicates stay for now;
a later step points their references to the kept twin. A duplicate without a file of its own in the overlay stays
too (its record comes from the base data, which the pack does not change).
Writes one TSV row per pack item (keep/drop, why, references) and, with --overlay-out, the overlay without the files
of the dropped items. The db references are what the ingest kept (locale NA, current events): re-run the selection
when the ingest's locale, feature or event handling changes (M2c-05 review F8).
Batches (BA-01, maintainer 2026-10-21: the unified content ships in weekly batches, safest first): every pack item
gets one by its db row's Property: 1 cosmetics (IsSkin), 2 gear (Type 1, equipment), 4 items named only by overlay
quest or NPC files (they come with the quests), 3 everything else; quest files are batch 4, string and table files are
in every batch. An NPC file is batch 4 unless a kept item of an earlier batch has its id (a pet's NPC comes with the
pet, BA-03). A skill file goes with the earliest batch whose files name it (an item file: that item's batch; a quest file:
4; an NPC file: that NPC's batch; a table file: 1; another skill file: that skill's batch); one no overlay file names
is in every batch, and so are the skills it names (BA-02: the boss skills of pack NPCs leave with them). --batches N
writes the overlay with batches 1..N only (default 4: all).
Hold (BA-08, maintainer D-9 2026-10-25: what has no data to work is held back, not shipped broken): the kept items of
the hold list (--hold, default unified-hold.tsv beside this file: id and reason; --hold "" none) get decision hold and
leave the overlay like a later batch's items; the pack gates count them as gone. A listed id that is not a pack item,
has no file of its own in the overlay (the nightly gives it) or that an --extra file names stops the run.
Usage: unified_selection.py --report <tsv> --db <maple-data.db> --overlay <dir> [--extra <path>...] --out <tsv>
       [--overlay-out <dir>] [--global-xml <dir>] [--batches N] [--hold <tsv>] | --selftest
"""
import argparse
import collections
import csv
import json
import os
import re
import shutil
import sqlite3
import sys
import xml.etree.ElementTree as ET

TOKEN = re.compile(rb"(?<![0-9])[0-9]{8}(?![0-9])")
HOLD = os.path.join(os.path.dirname(os.path.abspath(__file__)), "unified-hold.tsv")
# Binary or id-free tables: compressed map blocks, collision meshes, the ingest's bookkeeping, chat filter words.
SKIP_TABLES = {"checksum", "schema_version", "sqlite_sequence", "map-data", "nxs-mesh", "ban-word"}


def tokens(data):
    """Every 8-digit number in a byte string (item ids have 8 digits)."""
    return {int(m) for m in TOKEN.findall(data)}


def db_references(db_path, wanted):
    """id -> sources ("table:key") in the db that name it; an item's own row in table item does not count."""
    refs = collections.defaultdict(list)
    db = sqlite3.connect(f"file:{db_path}?mode=ro", uri=True)
    try:
        tables = [name for (name,) in db.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name")
                  if name not in SKIP_TABLES]
        for table in tables:
            for row in db.execute(f'SELECT * FROM "{table}"'):
                data = b"\0".join(v if isinstance(v, bytes) else str(v).encode("utf-8") for v in row if v is not None)
                found = tokens(data) & wanted
                if table == "item":
                    found.discard(row[0])
                for item_id in found:
                    refs[item_id].append(f"{table}:{row[0]}")
    finally:
        db.close()
    return refs


def own_files(overlay):
    """item id -> its own file in the overlay (item/<a>/<bb>/<id>.xml), as a path relative to the overlay."""
    out = {}
    for dirpath, _, names in os.walk(os.path.join(overlay, "item")):
        for name in names:
            stem, ext = os.path.splitext(name)
            if ext.lower() == ".xml" and stem.isdigit():
                out[int(stem)] = os.path.relpath(os.path.join(dirpath, name), overlay).replace(os.sep, "/")
    return out


def file_references(root, wanted, skip_dirs=()):
    """id -> files under root (or the file root) that name it; folders named in skip_dirs directly under root are not
    read."""
    refs = collections.defaultdict(list)
    if os.path.isfile(root):
        paths = [(root, os.path.basename(root))]
    else:
        paths = []
        for dirpath, dirnames, names in os.walk(root):
            if os.path.relpath(dirpath, root) == ".":
                dirnames[:] = [d for d in dirnames if d not in skip_dirs]
            paths.extend((os.path.join(dirpath, n), os.path.relpath(os.path.join(dirpath, n), root).replace(os.sep, "/"))
                         for n in names)
    for path, rel in paths:
        with open(path, "rb") as f:
            found = tokens(f.read()) & wanted
        for item_id in found:
            refs[item_id].append(rel)
    return refs


ORIGIN = re.compile(rb'originID="([0-9]{8})"')


def origin_references(overlay, wanted, own):
    """id -> item files whose <basic originID> names it: an item derived from it (a look preset is art, not this)."""
    refs = collections.defaultdict(list)
    for item_id, rel in own.items():
        with open(os.path.join(overlay, rel), "rb") as f:
            for origin in {int(m) for m in ORIGIN.findall(f.read())} & wanted - {item_id}:
                refs[origin].append(rel)
    return refs


def variant(path):
    """What makes an item a variant of a twin with the same look and name: the rarity shown (<property rank>), the
    duration (<life> attributes) and the trade setting (<limit> transfer and trade attributes). () without a file."""
    if not path or not os.path.isfile(path):
        return ()
    root = ET.parse(path).getroot()
    prop, life, limit = root.find(".//property"), root.find(".//life"), root.find(".//limit")
    return ((prop.get("rank") if prop is not None else None) or "",
            tuple(sorted(life.attrib.items())) if life is not None else (),
            tuple(sorted((k, v) for k, v in (limit.attrib if limit is not None else {}).items()
                         if "trade" in k.lower() or "transfer" in k.lower())))


def item_file(root, item_id):
    """An item's file in an extracted client XML tree (item/<a>/<bb>/<id>.xml), or None."""
    text = str(item_id)
    path = os.path.join(root, "item", text[0], text[1:3], f"{text}.xml")
    return path if os.path.isfile(path) else None


def norm(name):
    """Name for the duplicate check: lower case, letters and digits only ("" when there is none)."""
    return re.sub(r"[^0-9a-z]+", "", (name or "").lower())


def batch(prop, refs):
    """An item's content batch: 1 cosmetics (a skin), 2 gear (other equipment), 4 named only by overlay quest or NPC
    files (it comes with them), 3 everything else. prop: the db row's Property (a dict); refs: the item's references.
    A skin has a SkinType (the data's own mark: outfits 2, faces 10); IsSkin is what the pack build's skin rule adds."""
    if prop.get("IsSkin") is True or (prop.get("SkinType") or 0) > 0:
        return 1
    if prop.get("Type") == 1:
        return 2
    if refs and all(r.startswith(("quest/", "npc/")) for r in refs):
        return 4
    return 3


def npc_batches(overlay, item_batches):
    """NPC file (relative path) -> its batch: 4, with the quests, unless a kept item has the NPC's id: a pet and its
    NPC share it, and the NPC ships with the pet (BA-03: pets 60000062 and 61100150). Other ids in item files are no
    sign (item 11501129's icon path names 11500001, a pack NPC's id)."""
    out = {}
    for dirpath, _, names in os.walk(os.path.join(overlay, "npc")):
        for name in names:
            stem, ext = os.path.splitext(name)
            if ext.lower() == ".xml" and stem.isdigit():
                rel = os.path.relpath(os.path.join(dirpath, name), overlay).replace(os.sep, "/")
                out[rel] = min(item_batches.get(int(stem), 4), 4)
    return out


def skill_batches(overlay, item_batches, own, npc_batch=None):
    """skill file (relative path) -> its batch: the earliest batch of the overlay files that name its id (an item
    file: the item's batch, the dropped items' files do not count; a quest file: 4; an NPC file: that NPC's batch
    (npc_batch, else 4); another skill file: that skill's batch, so a boss's sub-skills leave with the boss); 1 when no
    file names it."""
    npc_batch = npc_batch or {}
    skills = {}
    for dirpath, _, names in os.walk(os.path.join(overlay, "skill")):
        for name in names:
            stem, ext = os.path.splitext(name)
            if ext.lower() == ".xml" and stem.isdigit():
                skills[int(stem)] = os.path.relpath(os.path.join(dirpath, name), overlay).replace(os.sep, "/")
    item_of = {rel: item_id for item_id, rel in own.items()}
    skill_of = {rel: skill_id for skill_id, rel in skills.items()}
    users = file_references(overlay, set(skills), skip_dirs=("string",))
    batch, callers = {}, collections.defaultdict(set)
    for skill_id in skills:
        found = []
        for user in users.get(skill_id, []):
            if user in skill_of:
                if skill_of[user] != skill_id:
                    callers[skill_id].add(skill_of[user])
            elif user.startswith(("quest/", "npc/")):
                found.append(npc_batch.get(user, 4))
            elif not user.startswith("item/") or item_of.get(user) in item_batches:
                found.append(item_batches.get(item_of.get(user), 1))  # a table file ships in every batch: 1
        # No user at all: in every batch, and so are the skills it names (review 3: an unused skill passes 1 on).
        batch[skill_id] = min(found, default=None if callers[skill_id] else 1)
    while True:
        changed = True
        while changed:  # a skill named by skills takes their earliest batch; values only fall, so this ends
            changed = False
            for skill_id, names in callers.items():
                via = [batch[name] for name in names if batch[name] is not None]
                if via and (batch[skill_id] is None or min(via) < batch[skill_id]):
                    batch[skill_id] = min(via)
                    changed = True
        stuck = [skill_id for skill_id, n in batch.items() if n is None]  # cycles of skills nothing else names
        if not stuck:
            return {skills[skill_id]: n for skill_id, n in batch.items()}
        for skill_id in stuck:
            batch[skill_id] = 1


def decide(rows, refs, has_file, names, variants=None):
    """rows: in-pack report rows (dicts with id, fingerprint, verdict). refs: id -> sources. has_file: ids with a file of
    their own in the overlay. names: id -> name (pack and Global items). variants: id -> variant() (pack items, Global
    twins; a Global twin without one counts as the same variant). Returns id -> (decision, reason)."""
    variants = variants or {}
    key = lambda row: (row["fingerprint"], norm(names.get(int(row["id"]))), variants.get(int(row["id"]), ()))
    pack_ids = {int(row["id"]) for row in rows}
    groups = collections.defaultdict(list)
    for row in rows:
        if row["fingerprint"] != "-" and key(row)[1]:
            groups[key(row)].append(int(row["id"]))
    out = {}
    for row in rows:
        item_id, verdict = int(row["id"]), row["verdict"]
        if row["fingerprint"] == "-":
            out[item_id] = ("keep", "no fingerprint")
            continue
        if not key(row)[1]:
            out[item_id] = ("keep", "no name")
            continue
        members = sorted(groups[key(row)])
        global_id = verdict.split(":", 1)[1] if verdict.startswith("duplicate-of-global:") else None
        same_name_global = bool(global_id) and norm(names.get(int(global_id))) == key(row)[1]
        if same_name_global and variants.get(int(global_id), key(row)[2]) == key(row)[2]:
            twin = f"Global {global_id}"
            keepers = []
        elif len(members) == 1:
            twin_id = int(verdict.split(":", 1)[1]) if ":" in verdict else None
            other = "other name"
            if twin_id is not None and (same_name_global or not global_id and norm(names.get(twin_id)) == key(row)[1]):
                other = "other rarity, duration or trade setting"
            out[item_id] = ("keep", "unique" if twin_id is None else
                            f"twin {twin_id} not in the pack" if not global_id and twin_id not in pack_ids else
                            f"same look as {'Global' if global_id else 'pack item'} {twin_id}, {other}")
            continue
        else:
            keepers = [m for m in members if refs.get(m)] or members[:1]
            twin = f"pack item {keepers[0]}"
        if item_id in keepers:
            out[item_id] = ("keep", f"kept, referenced ({len(members)} same items)" if refs.get(item_id) else
                            f"kept, lowest id ({len(members)} same items, none referenced)")
        elif refs.get(item_id):
            out[item_id] = ("keep", f"referenced; same as {twin}")
        elif item_id not in has_file:
            out[item_id] = ("keep", f"no file of its own in the pack; same as {twin}")
        else:
            out[item_id] = ("drop", f"same as {twin}")
    return out


def run(report, db_path, overlay, extra, out, overlay_out=None, global_xml=None, max_batch=4, hold=None):
    with open(report, encoding="utf-8", newline="") as f:
        rows = [r for r in csv.DictReader(f, delimiter="\t") if r["in_pack"] == "true"]
    held = {}
    if hold:
        with open(hold, encoding="utf-8", newline="") as f:
            held = {int(r["id"]): r["reason"] for r in csv.DictReader(f, delimiter="\t")}
    # Every pack item: a group's first item (verdict unique) can be the one a shop sells (M2c-05 db diff, shop_game).
    wanted = {int(r["id"]) for r in rows}
    own = own_files(overlay)
    refs = collections.defaultdict(list)
    # Item files name other ids only for their look (preset, icon, model paths: art, not the record); what an item
    # does with another item is in its db row. String files name every item.
    sources = [db_references(db_path, wanted), file_references(overlay, wanted, ("item", "string")),
               origin_references(overlay, wanted, own)]
    sources += [file_references(path, wanted) for path in extra]
    for problem, ids in (("not pack items", set(held) - wanted),
                         ("no file of their own in the overlay (the nightly gives them)", set(held) - set(own)),
                         ("named by an --extra file", set(held) & set().union(*sources[3:]))):
        if ids:
            raise SystemExit(f"hold list {hold}: {len(ids)} {problem}: {sorted(ids)[:8]}")
    for found in sources:
        for item_id, where in found.items():
            refs[item_id].extend(where)
    db = sqlite3.connect(f"file:{db_path}?mode=ro", uri=True)
    try:
        names = dict(db.execute('SELECT "Id", "Name" FROM "item"'))
        properties = {}
        for item_id, text in db.execute('SELECT "Id", "Property" FROM "item"'):
            try:
                parsed = json.loads(text) if text else {}
            except (json.JSONDecodeError, TypeError):
                parsed = {}
            properties[int(item_id)] = parsed if isinstance(parsed, dict) else {}
    finally:
        db.close()
    variants = {item_id: variant(os.path.join(overlay, rel)) for item_id, rel in own.items()}
    if global_xml:
        for row in rows:
            if row["verdict"].startswith("duplicate-of-global:"):
                global_id = int(row["verdict"].split(":", 1)[1])
                found = item_file(global_xml, global_id)
                if found:
                    variants[global_id] = variant(found)
    decisions = decide(rows, refs, set(own), names, variants)
    decisions.update({i: ("hold", reason) for i, reason in held.items() if decisions[i][0] == "keep"})
    batches = {int(row["id"]): batch(properties.get(int(row["id"]), {}), refs.get(int(row["id"]), [])) for row in rows}
    with open(out, "w", encoding="utf-8", newline="") as f:
        writer = csv.writer(f, delimiter="\t", lineterminator="\n")
        writer.writerow(["id", "name", "fingerprint", "verdict", "decision", "reason", "references", "referenced by", "batch"])
        for row in sorted(rows, key=lambda r: int(r["id"])):
            item_id = int(row["id"])
            decision, reason = decisions[item_id]
            where = refs.get(item_id, [])
            writer.writerow([item_id, names.get(item_id) or "", row["fingerprint"], row["verdict"], decision, reason,
                             len(where), " ".join(where[:3]) or "-", batches[item_id]])
    dropped = sorted(i for i, (d, _) in decisions.items() if d == "drop")
    kept = [i for i, (d, _) in decisions.items() if d == "keep"]
    on_hold = [i for i, (d, _) in decisions.items() if d == "hold"]
    if overlay_out:
        # Not copied: the dropped and held items' files, the files of kept items of a later batch and, below batch 4,
        # the quest files and the NPC and skill files of later batches (a held pet's NPC file leaves with it).
        skip = {own[i] for i in dropped + on_hold} | {own[i] for i in kept if batches[i] > max_batch and i in own}
        if max_batch < 4:
            kept_batches = {i: batches[i] for i in kept}
            npc_batch = npc_batches(overlay, kept_batches)
            skill_batch = skill_batches(overlay, kept_batches, own, npc_batch)
            for kind, batch_of in (("NPC", npc_batch), ("skill", skill_batch)):
                skip |= {rel for rel, n in batch_of.items() if n > max_batch}
                print(f"{kind} files: {sum(n <= max_batch for n in batch_of.values())} of {len(batch_of)} in batches 1 to {max_batch}")
        for dirpath, _, files in os.walk(overlay):
            for name in files:
                rel = os.path.relpath(os.path.join(dirpath, name), overlay).replace(os.sep, "/")
                if rel not in skip and not (max_batch < 4 and rel.startswith("quest/")):
                    os.makedirs(os.path.join(overlay_out, os.path.dirname(rel)), exist_ok=True)
                    shutil.copy2(os.path.join(dirpath, name), os.path.join(overlay_out, rel))
    counts = collections.Counter((d, re.sub(r" [0-9]+|\(.*|; same.*", "", r).strip()) for d, r in decisions.values())
    print(f"pack items {len(rows)}: keep {len(kept)}, drop {len(dropped)}" + (f", hold {len(on_hold)}" if on_hold else ""))
    for (decision, reason), n in sorted(counts.items()):
        print(f"  {decision:4} {n:5}  {reason}")
    for number, n in sorted(collections.Counter(batches[i] for i in kept).items()):
        print(f"batch {number}: {n} items")
    print(f"--batches {max_batch}: the overlay holds batches 1 to {max_batch}")
    print(f"TSV: {out}")
    return dropped


def selftest():
    import tempfile
    with tempfile.TemporaryDirectory() as tmp:
        db_path = os.path.join(tmp, "maple-data.db")
        db = sqlite3.connect(db_path)
        db.execute('CREATE TABLE "item" ("Id" INTEGER PRIMARY KEY, "Name" TEXT, "Property" TEXT)')
        db.execute('CREATE TABLE "table" ("Name" TEXT PRIMARY KEY, "Table" TEXT)')
        db.execute('CREATE TABLE "checksum" ("TableName" TEXT PRIMARY KEY, "Crc32C" INTEGER)')
        names = {11000001: "Mask", 11000002: "Mask", 11000003: "Hat", 11000004: "hat!", 11000005: "Shoes", 11000006: "Shoes",
                 11000007: "Cape", 11000008: "Mask", 11000009: "Mask", 11000010: "Box", 11000011: "Other Hat",
                 11000012: "AR Scouter", 11000013: None, 11000014: "Gloves", 11000015: "Gloves",
                 11000016: "Ring", 11000017: "Ring", 11000018: "Ring Box",
                 11000019: "Blade", 11000020: "Blade", 11000021: "Blade",
                 12000001: "Mask"}  # 12000001: the Global twin
        for item_id, name in names.items():
            db.execute("INSERT INTO item VALUES (?, ?, ?)", (item_id, name, f'{{"ConstantId":{item_id}}}'))
        # Batches: a skin (1), other equipment (2), a package (3), a key only a pack quest names (4)
        db.execute("UPDATE item SET Property = '{\"IsSkin\": true, \"Type\": 1}' WHERE Id = 11000003")
        db.execute("UPDATE item SET Property = '{\"SkinType\": 10, \"Type\": 1}' WHERE Id = 11000019")  # a face: skin by its SkinType
        db.execute("UPDATE item SET Property = '{\"Type\": 1}' WHERE Id = 11000014")
        db.execute("UPDATE item SET Property = '{\"Type\": 25}' WHERE Id = 11000010")
        db.execute("INSERT INTO item VALUES (11000022, 'Prison Key', '{\"Type\": 3}')")
        db.execute("""INSERT INTO "table" VALUES ('individualitemdrop.xml', '{"Entries":{"1":{"ItemIds":[11000006]}}}')""")
        db.execute("""INSERT INTO "table" VALUES ('shop_game.xml', '{"Entries":{"7":{"ItemId":11000014}}}')""")
        db.execute("INSERT INTO checksum VALUES ('11000004', 11000004)")  # skipped table: not a reference
        db.commit()
        db.close()
        overlay = os.path.join(tmp, "overlay")
        for item_id in (11000001, 11000002, 11000003, 11000004, 11000005, 11000006, 11000007, 11000008, 11000010, 11000011,
                        11000012, 11000013, 11000014, 11000015, 11000016, 11000017, 11000018, 11000019, 11000020,
                        11000021, 11000022):
            path = os.path.join(overlay, "item", "1", "10", f"{item_id}.xml")
            os.makedirs(os.path.dirname(path), exist_ok=True)
            origin = '<basic originID="11000017"/>' if item_id == 11000018 else ""  # 11000018 derives from 11000017
            origin += {11000019: '<property rank="3"/>', 11000020: '<property rank="4"/>', 11000021: '<property rank="3"/>'}.get(item_id, "")
            origin += {11000003: '<skill id="50000002"/>', 11000014: '<skill id="50000004"/>'}.get(item_id, "")  # skill users
            origin += '<icon path="icon/11000090.png"/>' if item_id == 11000010 else ""  # a path naming an NPC's id: no sign
            with open(path, "w", encoding="utf-8") as f:
                f.write(f'<ms2><item id="{item_id}">{origin}<tool itemPreset="11000001"/></item></ms2>')
        os.makedirs(os.path.join(overlay, "quest"))
        with open(os.path.join(overlay, "quest", "10000001.xml"), "w", encoding="utf-8") as f:
            f.write('<ms2><quest id="10000001"><reward itemID="11000002"/><reward itemID="11000015"/></quest></ms2>')
        # Add the Prison Key to a quest file
        with open(os.path.join(overlay, "quest", "10000002.xml"), "w", encoding="utf-8") as f:
            f.write('<ms2><quest id="10000002"><reward itemID="11000022"/></quest></ms2>')
        # Skills: one only a pack NPC uses (batch 4), one a batch-1 item uses, one nothing uses, one a batch-2 item uses
        # and 50000005, a sub-skill only the NPC's skill 50000001 names (it leaves with it); 50000006, unused, names
        # 50000007, which the NPC names too (it stays with the unused one); 50000008 a table file and the NPC name.
        subs = {50000001: '<sub id="50000005"/>', 50000006: '<sub id="50000007"/>'}
        for skill_id in (50000001, 50000002, 50000003, 50000004, 50000005, 50000006, 50000007, 50000008):
            os.makedirs(os.path.join(overlay, "skill", "50"), exist_ok=True)
            with open(os.path.join(overlay, "skill", "50", f"{skill_id}.xml"), "w", encoding="utf-8") as f:
                f.write(f'<ms2><skill id="{skill_id}">{subs.get(skill_id, "")}</skill></ms2>')
        os.makedirs(os.path.join(overlay, "npc", "11", "00"))
        with open(os.path.join(overlay, "npc", "11", "00", "11000090.xml"), "w", encoding="utf-8") as f:
            f.write('<ms2><npc id="11000090"><skill ids="50000001,50000007,50000008"/></npc></ms2>')
        with open(os.path.join(overlay, "npc", "11", "00", "11000019.xml"), "w", encoding="utf-8") as f:
            f.write('<ms2><npc id="11000019"/></ms2>')  # the NPC of item 11000019 (a pet's, same id): batch 1 like the item
        os.makedirs(os.path.join(overlay, "table"))
        with open(os.path.join(overlay, "table", "feature.xml"), "w", encoding="utf-8") as f:
            f.write('<ms2><feature skill="50000008"/></ms2>')
        os.makedirs(os.path.join(overlay, "string", "en"))
        with open(os.path.join(overlay, "string", "en", "itemname.xml"), "w", encoding="utf-8") as f:
            f.write('<ms2><key id="11000003" name="names are not references"/></ms2>')
        mods = os.path.join(tmp, "offline-meret-catalog.json")
        with open(mods, "w", encoding="utf-8") as f:
            f.write('{"Featured":[11000008]}')
        report = os.path.join(tmp, "report.tsv")
        rows = [  # id, fingerprint, verdict
            (11000001, "1|0|preset:9", "duplicate-of-global:12000001"),  # own row and own file only: drop
            (11000002, "1|0|preset:9", "duplicate-of-global:12000001"),  # a pack quest rewards it: keep
            (11000003, "1|0|preset:3", "unique"),                        # one of two, lowest: keep
            (11000004, "1|0|preset:3", "duplicate-of:11000003"),         # its twin stays: drop
            (11000005, "1|0|preset:5", "unique"),                        # not referenced, its twin is: drop
            (11000006, "1|0|preset:5", "duplicate-of:11000005"),         # a drop table names it: keep
            (11000007, "1|0|preset:7", "duplicate-of:13000001"),         # twin outside the pack: keep
            (11000008, "1|0|preset:9", "duplicate-of-global:12000001"),  # the Meret catalog lists it: keep
            (11000009, "1|0|preset:9", "duplicate-of-global:12000001"),  # no file of its own: keep
            (11000010, "-", "no-fingerprint"),
            (11000011, "1|0|preset:3", "duplicate-of:11000003"),         # same look, other name: keep
            (11000012, "1|0|preset:9", "duplicate-of-global:12000001"),  # same look as Global, other name: keep
            (11000013, "1|0|preset:3", "duplicate-of:11000003"),         # no name to compare: keep
            (11000014, "1|0|preset:14", "unique"),                       # first of its group, a shop sells it: keep
            (11000015, "1|0|preset:14", "duplicate-of:11000014"),        # a quest rewards it too: keep both
            (11000016, "1|0|preset:16", "unique"),                       # its twin is another item's origin: drop
            (11000017, "1|0|preset:16", "duplicate-of:11000016"),        # 11000018's originID names it: keep
            (11000018, "1|0|preset:18", "unique"),
            (11000019, "1|0|preset:19", "unique"),                       # rank 3, lowest of its variant: keep
            (11000020, "1|0|preset:19", "duplicate-of:11000019"),        # rank 4: another variant, keep
            (11000021, "1|0|preset:19", "duplicate-of:11000019"),        # rank 3 again: drop
            (11000022, "-", "no-fingerprint"),                          # Prison Key - new item with Property Type 3
            (13000001, "1|0|preset:7", "unique"),                        # not in the pack: not judged
        ]
        with open(report, "w", encoding="utf-8", newline="") as f:
            writer = csv.writer(f, delimiter="\t", lineterminator="\n")
            writer.writerow(["kind", "id", "client", "in_pack", "fingerprint", "verdict"])
            for item_id, fp, verdict in rows:
                writer.writerow(["item", item_id, "KMS-EoS", "false" if item_id == 13000001 else "true", fp, verdict])
        out = os.path.join(tmp, "selection.tsv")
        overlay_out = os.path.join(tmp, "overlay-out")
        dropped = run(report, db_path, overlay, [mods], out, overlay_out)
        assert dropped == [11000001, 11000004, 11000005, 11000016, 11000021], dropped
        with open(out, encoding="utf-8", newline="") as f:
            got = {int(r["id"]): r for r in csv.DictReader(f, delimiter="\t")}
        assert 13000001 not in got and len(got) == 22, sorted(got)
        assert got[11000020]["decision"] == "keep", got[11000020]
        assert got[11000020]["reason"] == "same look as pack item 11000019, other rarity, duration or trade setting", got[11000020]
        assert got[11000021]["reason"] == "same as pack item 11000019", got[11000021]
        assert got[11000017]["decision"] == "keep" and got[11000017]["referenced by"] == "item/1/10/11000018.xml", got[11000017]
        assert got[11000014]["decision"] == "keep" and got[11000014]["referenced by"] == "table:shop_game.xml", got[11000014]
        assert got[11000015]["decision"] == "keep" and got[11000015]["referenced by"] == "quest/10000001.xml", got[11000015]
        assert got[11000004]["name"] == "hat!" and got[11000004]["reason"] == "same as pack item 11000003", got[11000004]
        assert got[11000011]["reason"] == "same look as pack item 11000003, other name", got[11000011]
        assert got[11000012]["reason"] == "same look as Global 12000001, other name", got[11000012]
        assert got[11000007]["reason"] == "twin 13000001 not in the pack", got[11000007]
        assert got[11000013]["reason"] == "no name", got[11000013]
        assert got[11000002]["reason"] == "referenced; same as Global 12000001", got[11000002]
        assert got[11000002]["referenced by"] == "quest/10000001.xml", got[11000002]
        assert got[11000006]["referenced by"] == "table:individualitemdrop.xml", got[11000006]
        assert got[11000005]["reason"] == "same as pack item 11000006", got[11000005]
        assert got[11000003]["decision"] == "keep" and got[11000003]["references"] == "0", got[11000003]
        assert got[11000008]["referenced by"] == "offline-meret-catalog.json", got[11000008]
        assert got[11000009]["reason"].startswith("no file of its own"), got[11000009]
        assert got[11000007]["decision"] == "keep" and got[11000010]["reason"] == "no fingerprint"
        # 11000001 is every overlay item's look preset: art, not a reference to the record
        assert got[11000001]["decision"] == "drop" and got[11000001]["references"] == "0", got[11000001]
        kept_files = sorted(os.path.relpath(os.path.join(d, n), overlay_out).replace(os.sep, "/")
                            for d, _, names in os.walk(overlay_out) for n in names)
        assert "item/1/10/11000001.xml" not in kept_files and "item/1/10/11000004.xml" not in kept_files
        assert "item/1/10/11000002.xml" in kept_files and "string/en/itemname.xml" in kept_files, kept_files
        assert len(kept_files) == 35 - 5, kept_files  # 21 item, 2 quest, 8 skill, 2 npc, 1 table, 1 string file; 5 dropped
        npc_batch = npc_batches(overlay, {i: int(r["batch"]) for i, r in got.items() if r["decision"] == "keep"})
        assert npc_batch == {"npc/11/00/11000090.xml": 4, "npc/11/00/11000019.xml": 1}, npc_batch
        assert [got[i]["batch"] for i in (11000003, 11000019, 11000014, 11000010, 11000022)] == ["1", "1", "2", "3", "4"], got
        batch_one = os.path.join(tmp, "overlay-batch-1")
        run(report, db_path, overlay, [mods], out, batch_one, max_batch=1)
        files_one = sorted(os.path.relpath(os.path.join(d, n), batch_one).replace(os.sep, "/")
                           for d, _, names in os.walk(batch_one) for n in names)
        assert "item/1/10/11000003.xml" in files_one and "string/en/itemname.xml" in files_one, files_one
        for later in ("item/1/10/11000014.xml", "item/1/10/11000010.xml", "item/1/10/11000022.xml", "quest/10000001.xml",
                      "quest/10000002.xml", "npc/11/00/11000090.xml", "skill/50/50000001.xml", "skill/50/50000004.xml",
                      "skill/50/50000005.xml"):
            assert later not in files_one, (later, files_one)
        for stays in ("skill/50/50000002.xml", "skill/50/50000003.xml", "skill/50/50000006.xml", "skill/50/50000007.xml",
                      "skill/50/50000008.xml", "table/feature.xml", "npc/11/00/11000019.xml"):
            assert stays in files_one, (stays, files_one)
        # Hold (BA-08): a box, an item a drop table names and the item whose NPC shares its id are held, the dropped
        # 11000001 stays dropped; held files leave the overlay, the NPC file leaves with its item.
        def hold_list(*ids):
            path = os.path.join(tmp, "hold.tsv")
            with open(path, "w", encoding="utf-8", newline="") as f:
                f.write("id\treason\n" + "".join(f"{i}\tno data to work (selftest)\n" for i in ids))
            return path
        held_three = os.path.join(tmp, "overlay-hold-3")
        run(report, db_path, overlay, [mods], out, held_three, max_batch=3, hold=hold_list(11000001, 11000006, 11000010, 11000019))
        with open(out, encoding="utf-8", newline="") as f:
            got = {int(r["id"]): r for r in csv.DictReader(f, delimiter="\t")}
        assert [got[i]["decision"] for i in (11000001, 11000006, 11000010, 11000019, 11000018)] == \
            ["drop", "hold", "hold", "hold", "keep"], got
        assert got[11000010]["reason"] == "no data to work (selftest)" and got[11000010]["batch"] == "3", got[11000010]
        files_three = sorted(os.path.relpath(os.path.join(d, n), held_three).replace(os.sep, "/")
                             for d, _, names in os.walk(held_three) for n in names)
        for gone in ("item/1/10/11000006.xml", "item/1/10/11000010.xml", "item/1/10/11000019.xml", "npc/11/00/11000019.xml"):
            assert gone not in files_three, (gone, files_three)
        for stays in ("item/1/10/11000018.xml", "item/1/10/11000003.xml"):
            assert stays in files_three, (stays, files_three)
        for bad in (13000001, 11000008, 11000009):  # not a pack item, named by an --extra file, no file of its own
            try:
                run(report, db_path, overlay, [mods], out, hold=hold_list(bad))
            except SystemExit as stop:
                assert str(bad) in str(stop), (bad, stop)
            else:
                raise AssertionError(f"hold {bad} accepted")

    print("selftest ok")


def main(argv):
    if argv[1:] == ["--selftest"]:
        selftest()
        return 0
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--report", required=True)
    parser.add_argument("--db", required=True)
    parser.add_argument("--overlay", required=True)
    parser.add_argument("--extra", action="append", default=[])
    parser.add_argument("--out", required=True)
    parser.add_argument("--overlay-out")
    parser.add_argument("--global-xml")
    parser.add_argument("--batches", type=int, choices=range(1, 5), default=4,
                        help="Maximum batch to include (1-4, default 4)")
    parser.add_argument("--hold", default=HOLD, help="the hold list (id, reason; default unified-hold.tsv beside this file; an empty value: none)")
    args = parser.parse_args(argv[1:])
    if args.overlay_out and os.path.exists(args.overlay_out) and os.listdir(args.overlay_out):
        print(f"--overlay-out must be empty: {args.overlay_out}", file=sys.stderr)
        return 2
    run(args.report, args.db, args.overlay, args.extra, args.out, args.overlay_out, args.global_xml, args.batches, args.hold)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
