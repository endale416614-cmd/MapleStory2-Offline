r"""Organizes the offline Meret Market so it is easy to browse (docs/ADAPTATIONS.md, A21).

  python -I tools/organize_meret_market.py [--db SaveData/maple-data.db] --emote-strings <kordynamicaction.xml> [--check]

Rewrites the tab of each entry in Maple2/offline-meret-catalog.json from the game data, adds the Meret furniture that no
shop sells, and writes the matching tab list into the client mod client-mods/offline-shop (Xml/table/
meratmarketcategory.xml, section 100000). Running it again changes nothing. --check only reports what would change (exit 1 when anything would).
Two rules keep every box sellable: the Kirin and Sun Wukong (Combat Pet) boxes become their pet items, and any other box that opens to nothing is removed.
A maid whose function cube spawns no NPC the data has (Maple2/summon-npcs.json CubeSpawns) is removed too: placed, she would never appear.
So is an emote whose skill the client's Emotes window does not list: it lists the skills of the client's string/en/kordynamicaction.xml
(--emote-strings: the content pack's file when the pack ships one, else the NA client's), so a learned one would show no tile.
Rows of tools/data/meret-new.tsv not yet in the catalog become entries in the tab where most entries of their item category sit (for an
equipment piece: of its equip slots), at the row's kms_price, else 380 Meret for a gacha capsule, else the catalog's RarityPrices. Rows the
server cannot use with the data are left out with their reason (unusable). The server shows the New ids in tab 11 while the file keeps their
own tab, and the next release's list replaces this one. Rows of tools/data/meret-capsule-prizes.tsv, the prizes of the sold capsules that
the catalog does not sell otherwise (A7), become entries the same way, in their own tab only.

  New (tab 11)                 this release's additions, tools/data/meret-new.tsv (the catalog's New ids)
  Featured (tab 10)            gacha capsules and the other "Capsule"/"Crate" boxes
  Packages (40000)             packages and set packs (capsules moved out)
  Design Templates (40700)     every design template, mount template, template voucher and gift box (by name)
  Outfits (40100)              unchanged sub-tabs; Crate Spotlight (40110) removed, its coins go to Functional
  Summons (40200)              mounts and pets, the pet capsules (category PetPack) with the pets
  Furniture (40800)            maids whose assistant the data has, plus Meret furniture the housing shop does not sell
  Functional, Expression, Badges, Lifestyle   unchanged apart from the moves above

The client pages Featured and New itself, 5 pages of 18 tiles, so each holds at most 90 entries (BT-123); the server sends
them in OfflineEconomy.FeaturedOrder. Tab names reuse strings the client already has (categoryname.xml: 2074 Design Templates,
9007 Furniture). The server side of the tab list is OfflineEconomy.Apply(MeretMarketCategoryTable).
"""
import argparse
import collections
import csv
import json
import os
import re
import sqlite3
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CATALOG = os.path.join(ROOT, "Maple2", "offline-meret-catalog.json")
CUBE_SPAWNS = os.path.join(ROOT, "Maple2", "summon-npcs.json")
# The client requests regional tables without the locale folder (it adds "na/" below the mod hook), so the mod serves table/<name>.xml.
MOD_XML = os.path.join(ROOT, "client-mods", "offline-shop", "Data", "Xml", "table", "meratmarketcategory.xml")

FEATURED, PACKAGES, OUTFITS, CRATE_SPOTLIGHT, SUMMONS, PETS, FUNCTIONAL = 10, 40000, 40100, 40110, 40200, 40203, 40300
TEMPLATES, FURNITURE = 40700, 40800
OPEN_GACHA_BOX, OPEN_ITEM_BOX, OPEN_ITEM_BOX_WITH_KEY, SELECT_ITEM_BOX = 36, 38, 41, 52
BOX_FUNCTIONS = {36, 38, 39, 40, 41, 52}  # gacha, item boxes (plain, Lulu, Lulu simple, with key) and select boxes
# Kirin and Sun Wukong (Combat Pet) boxes drop only from KR/CN rows; the pet items are sold instead
PET_ITEMS = {20301385: 62000011, 20301386: 62000013}
FURNITURE_PRICE = 30  # Meret furniture without a price in the data gets the most common furniture price
FEATURED_MAX = NEW_TAB_MAX = 90  # Featured (10) and New (11): the client pages them itself, 5 pages of 18 tiles (BT-123)
NEW_LIST = os.path.join(ROOT, "tools", "data", "meret-new.tsv")
PRIZE_LIST = os.path.join(ROOT, "tools", "data", "meret-capsule-prizes.tsv")
CAPSULE_PRICE = 380  # what every gacha capsule of the catalog costs
EQUIPMENT, AIR_MOUNT, GROUND_MOUNT = 1, 5, 9
EMOTE = (2, 14)  # Property Type and SubType (ModelExtensions.IsEmote)
PREMIUM_SECTION = "100000"
# Top-level order of section 100000: (tab id, name string id, symbol)
LAYOUT = [
    (10, "1001", "recommand"), (11, "1015", "NewItem"), (PACKAGES, "40000", "special"),
    (TEMPLATES, "2074", "clothes"), (OUTFITS, "40100", "clothes"), (SUMMONS, "40200", "pet"),
    (FURNITURE, "9007", "build"), (FUNCTIONAL, "40300", "etc"), (40400, "40400", "action"),
    (40500, "40500", "badge"), (40600, "40600", "instrument"),
]


def load_items(db):
    items = {}
    for item_id, name, prop, func, option, skill, slots, install in db.execute("select Id, Name, Property, Function, Option, Skill, SlotNames, Install from item"):
        prop = json.loads(prop) if prop and prop != "None" else {}
        skill = json.loads(skill) if skill and skill.startswith("{") else {}
        install = json.loads(install) if install and install.startswith("{") else {}
        try:
            function = json.loads(func) if func and func.startswith("{") else {}
        except ValueError:
            function = {}
        try:
            rarity = json.loads(option).get("ConstantId", 1) if option and option.startswith("{") else 1
        except ValueError:
            rarity = 1
        items[item_id] = {"name": name or "", "category": prop.get("Category", ""), "function": function.get("Type"),
                          "parameters": function.get("Parameters") or "", "rarity": rarity, "type": prop.get("Type", 0),
                          "ride": prop.get("Ride", 0), "skill": skill.get("Id", 0), "slots": slots if slots and slots != "[]" else "",
                          "cube": install.get("ObjectCubeId", 0), "subtype": prop.get("SubType", 0)}
    return items


def load_drops(db):
    """The drop data a box opens against: individual drop boxes, global drop groups and gacha_info."""
    def load(source, name):
        return json.loads(db.execute(f"select \"Table\" from '{source}' where Name=?", (name,)).fetchone()[0])
    with_data = {item_id for (item_id,) in db.execute("select Id from item")}
    individual = load("server-table", "individualItemDrop.xml")["Entries"]
    for box_id, box in individual.items():
        # ItemDropManager.CanGiveItem (DR-01): an entry counts only when one of its ids has item data (0 pads the id lists)
        if not any(i > 0 and i in with_data for group in box.values() for entry in group["Items"] for i in entry["Ids"]):
            individual[box_id] = {}  # so box_opens treats it as a box without rows, as the server does
    return individual, load("server-table", "globalItemDrop*.xml")["DropGroups"], load("table", "gacha_info.xml")["Entries"]


def parse_parameters(text):
    """ItemBoxManager.ParseParameters: an empty or non-numeric field is 0 (20301245 has "1,,330001788")."""
    return [int(field) if re.fullmatch(r"\s*[+-]?\d+\s*", field) else 0 for field in text.split(",")]


def box_opens(item, drops):
    """Mirrors ItemBoxManager.Open for the four box types (anything else opens)."""
    if item["function"] not in (SELECT_ITEM_BOX, OPEN_ITEM_BOX, OPEN_ITEM_BOX_WITH_KEY, OPEN_GACHA_BOX):
        return True
    if not item["parameters"]:
        return False
    individual, global_groups, gacha = drops
    p = parse_parameters(item["parameters"])
    p += [-1] * (6 - len(p))  # a missing parameter reads as no box (the server throws)
    if item["function"] == SELECT_ITEM_BOX:
        return bool(individual.get(str(p[1])))
    if item["function"] == OPEN_ITEM_BOX_WITH_KEY:
        return bool(individual.get(str(p[5])))
    if item["function"] == OPEN_GACHA_BOX:
        return str(p[0]) in gacha and bool(individual.get(str(gacha[str(p[0])]["DropBoxId"])))
    box = individual.get(str(p[3]))
    groups = global_groups.get(str(p[0])) if p[0] > 0 else {}
    return box is not None and groups is not None and bool(box or groups)


def no_assistant(item, spawns, npcs):
    """A maid whose function cube spawns no NPC the data has (FieldManager.AddFieldFunctionInteract spawns the CubeSpawns NPC)."""
    return item["category"] == "Maid" and spawns.get(str(item["cube"])) not in npcs


def unlisted_emote(item, emotes):
    """An emote whose skill the client's Emotes window does not list (emotes: the skills it lists): learned, it shows no tile (BT-124)."""
    return (item["type"], item["subtype"]) == EMOTE and item["skill"] not in emotes


def target_tab(entry, item):
    if item is None:
        return entry["TabId"]
    if re.search(r"\bTemplates?\b", item["name"]):
        return TEMPLATES
    if item["category"] == "PetPack":
        return PETS  # the pet capsules, as in the NA catalog: with them Featured would hold more than the client pages (BT-123)
    if item["function"] == OPEN_GACHA_BOX or (item["function"] in BOX_FUNCTIONS and re.search(r"\b(Capsule|Crate)\b", item["name"])):
        return FEATURED
    if item["category"] == "Maid":
        return FURNITURE
    if entry["TabId"] == CRATE_SPOTLIGHT:
        return FUNCTIONAL  # the capsule coins
    return entry["TabId"]


def unsold_furniture(db, items, listed):
    table = json.loads(db.execute("select \"Table\" from 'table' where Name='na/shop_*.xml'").fetchone()[0])["Entries"]
    for entry in sorted(table.values(), key=lambda e: e["ItemId"]):
        item_id = entry["ItemId"]
        if entry.get("FurnishingTokenType") == 3 and not entry.get("Buyable") and item_id in items and item_id not in listed:
            yield item_id, entry.get("Price") or FURNITURE_PRICE


def load_new(path):
    with open(path, encoding="utf-8", newline="") as f:
        return [(int(row["id"]), int(row["kms_price"]) if row["kms_price"] else None) for row in csv.DictReader(f, delimiter="\t")]


def unusable(item_id, item, drops, rides, glides, emotes):
    """Why the server cannot use the item with this data (A6 leaves out what the server cannot create), or None."""
    if not 0 <= item["type"] <= 22 or item["type"] == 8:
        return "no inventory tab"  # ModelExtensions.HasTab
    if item["type"] == GROUND_MOUNT and item["ride"] not in rides:
        return "no ride row"  # RideManager.Mount
    if item["type"] == EQUIPMENT and not item["slots"]:
        return "no equip slot"  # EquipManager.ValidEquipSlotForItem
    if item["type"] == AIR_MOUNT and glides.get(item["skill"]) != item_id:
        # an air mount is its glide skill, and an NA glide skill names its own item in beginCondition (IM-03 gives KMS copies their own, BT-96)
        return "no glide skill of its own"
    if unlisted_emote(item, emotes):
        return "an emote the client does not list"
    if not box_opens(item, drops):
        return "box opens to nothing"
    return None


def tab_key(item):
    return item["slots"] if item["type"] == EQUIPMENT else item["category"]  # KMS files some outfit pieces under the category ETC


def add_new(catalog, items, rows, drops, rides, glides, emotes):
    counts = collections.defaultdict(collections.Counter)
    for entry in catalog["Entries"]:
        if entry["ItemId"] in items:
            counts[tab_key(items[entry["ItemId"]])][entry["TabId"]] += 1
    ids = {entry["Id"] for entry in catalog["Entries"]}
    template = catalog["Entries"][0]
    added, left_out = 0, collections.Counter()
    for item_id, price in rows:
        if item_id in ids:
            continue
        reason = "not in the data" if item_id not in items else unusable(item_id, items[item_id], drops, rides, glides, emotes)
        if reason:
            left_out[reason] += 1
            continue
        item = items[item_id]
        if not counts[tab_key(item)]:
            raise ValueError(f"no catalog entry of slots or category {tab_key(item)!r} gives item {item_id} a tab")
        # A6: the original (KMS) price where there is one, else the price of the item's rarity; a gacha capsule costs what the catalog's do
        price = price or (CAPSULE_PRICE if item["function"] == OPEN_GACHA_BOX else catalog["RarityPrices"][str(item["rarity"])])
        catalog["Entries"].append(dict(template, Id=item_id, ParentId=0, TabId=counts[tab_key(item)].most_common(1)[0][0],
                                       ItemId=item_id, Rarity=item["rarity"], Quantity=1, Price=price, JobRequirement=4095))
        ids.add(item_id)
        added += 1
    return added, left_out


def new_ids(catalog, rows):
    ids = {entry["Id"] for entry in catalog["Entries"]}
    new = [item_id for item_id, _ in rows if item_id in ids]
    assert len(new) <= NEW_TAB_MAX, "too many New entries for one Featured page"
    return new


def organize_catalog(catalog, items, db, new_rows=(), prize_rows=(), spawns=None, emotes=frozenset()):
    replaced = 0
    for entry in catalog["Entries"]:
        if entry["ItemId"] in PET_ITEMS:
            entry.update(Id=PET_ITEMS[entry["ItemId"]], ItemId=PET_ITEMS[entry["ItemId"]], TabId=PETS)
            replaced += 1
    drops = load_drops(db)
    skills = {row[0] for row in db.execute("select Id from skill")}
    glides = {}
    for item_id in sorted(items):  # a glide skill belongs to the first air mount that names it
        if items[item_id]["type"] == AIR_MOUNT and items[item_id]["skill"] in skills:
            glides.setdefault(items[item_id]["skill"], item_id)
    rides = {row[0] for row in db.execute("select Id from ride")}
    new_added, left_out = add_new(catalog, items, new_rows, drops, rides, glides, emotes)
    prize_added, prize_left_out = add_new(catalog, items, prize_rows, drops, rides, glides, emotes)
    entries = [entry for entry in catalog["Entries"] if entry["ItemId"] not in items or box_opens(items[entry["ItemId"]], drops)]
    removed = len(catalog["Entries"]) - len(entries)
    npcs = {row[0] for row in db.execute("select Id from npc")}
    kept = [entry for entry in entries if entry["ItemId"] not in items or not no_assistant(items[entry["ItemId"]], spawns or {}, npcs)]
    maids = len(entries) - len(kept)
    shown = [entry for entry in kept if entry["ItemId"] not in items or not unlisted_emote(items[entry["ItemId"]], emotes)]
    unlisted = len(kept) - len(shown)
    catalog["Entries"] = shown
    moves = {}
    for entry in catalog["Entries"]:
        tab = target_tab(entry, items.get(entry["ItemId"]))
        if tab != entry["TabId"]:
            moves[(entry["TabId"], tab)] = moves.get((entry["TabId"], tab), 0) + 1
            entry["TabId"] = tab
    ids = {entry["Id"] for entry in catalog["Entries"]}
    listed = {entry["ItemId"] for entry in catalog["Entries"]}
    template = catalog["Entries"][0]
    added = 0
    for item_id, price in unsold_furniture(db, items, listed):
        if item_id in ids:
            continue
        catalog["Entries"].append(dict(template, Id=item_id, ParentId=0, TabId=FURNITURE, ItemId=item_id,
                                       Rarity=items[item_id]["rarity"], Quantity=1, Price=price, JobRequirement=4095))
        added += 1
    # The New entries count too: they go back to Featured when the next release's list replaces this one.
    shipped = json.loads(db.execute("select \"Table\" from 'server-table' where Name='shop_merat_custom.xml'").fetchone()[0])["Entries"]
    featured = sum(entry.get("TabId") == FEATURED for entry in catalog["Entries"] + list(shipped.values()))
    assert featured <= FEATURED_MAX, f"{featured} Featured entries with the shipped ones: the client pages only {FEATURED_MAX}"
    new = new_ids(catalog, new_rows)
    new_changed = catalog.get("New") != new
    catalog["New"] = new
    return moves, added, replaced, removed, new_added, left_out, new_changed, prize_added, prize_left_out, maids, unlisted


def catalog_changed(result):
    moves, added, replaced, removed, new_added, _, new_changed, prize_added, _, maids, unlisted = result
    return bool(moves or added or replaced or removed or new_added or new_changed or prize_added or maids or unlisted)


def selftest():
    def e(entry_id, tab):
        return {"Id": entry_id, "ParentId": 0, "TabId": tab, "ItemId": entry_id, "Rarity": 1, "Quantity": 1, "Price": 300,
                "JobRequirement": 4095, "CurrencyType": 1}

    def it(category, rarity=1, type_=2, ride=0, skill=0, function=None, slots="", parameters="", subtype=0):
        return {"name": "", "category": category, "rarity": rarity, "type": type_, "ride": ride, "skill": skill,
                "function": function, "parameters": parameters, "slots": slots, "subtype": subtype}
    catalog = {"RarityPrices": {"1": 300, "4": 1200}, "Entries": [dict(e(1, 40400), ParentId=9, JobRequirement=1), e(2, 40400), e(3, 40500)]}
    items = {1: it("Action"), 2: it("Action"), 10: it("Action"), 3: it("SYSTEM", type_=7), 12: it("SYSTEM", 4, 7), 14: it("DE", type_=6),
             15: it("Action", type_=24), 16: it("Ride", type_=9), 17: it("SKB", type_=5, skill=8), 18: it("SKB", type_=5, skill=8),
             19: it("Package", type_=10, function=OPEN_ITEM_BOX)}
    rides, glides, emotes = {7}, {8: 18}, {9}
    rows = [(10, None), (12, 75), (13, None), (1, None), (15, None), (16, None), (17, None), (19, None)]
    assert add_new(catalog, items, rows, None, rides, glides, emotes) == (2, {"not in the data": 1, "no inventory tab": 1, "no ride row": 1,
                                                                              "no glide skill of its own": 1, "box opens to nothing": 1})
    by_id = {entry["Id"]: entry for entry in catalog["Entries"]}
    assert by_id[10]["TabId"] == 40400 and by_id[10]["Price"] == 300 and by_id[10]["Rarity"] == 1 and by_id[10]["ParentId"] == 0
    assert by_id[10]["ItemId"] == 10 and by_id[10]["JobRequirement"] == 4095 and by_id[12]["TabId"] == 40500 and by_id[12]["Price"] == 75
    assert 13 not in by_id and not {15, 16, 17, 19} & set(by_id) and len(catalog["Entries"]) == 5
    assert add_new(catalog, items, [(10, None), (12, 75)], None, rides, glides, emotes) == (0, {})
    assert new_ids(catalog, rows) == [10, 12, 1]
    assert unusable(16, it("Ride", type_=9, ride=7), None, rides, glides, emotes) is None and unusable(18, items[18], None, rides, glides, emotes) is None
    assert unusable(20, it("Action", type_=8), None, rides, glides, emotes) == "no inventory tab"
    try:
        add_new(catalog, items, [(14, None)], None, rides, glides, emotes)
        raise AssertionError("a category without entries must raise")
    except ValueError:
        pass
    # a gacha capsule costs CAPSULE_PRICE, another box its rarity's price; an equipment piece goes where most pieces of its
    # slots sit, whatever its category
    catalog["Entries"] += [e(4, 40109), e(6, 40000), e(7, 40109), e(8, 40300)]
    items.update({4: it("EA", type_=1, slots="[14]"), 6: it("Package"), 7: it("EA", type_=1, slots="[14]"), 8: it("ETC", type_=1, slots="[14]"),
                  21: it("Action", type_=10, function=OPEN_GACHA_BOX, parameters="7"), 22: it("ETC", 4, 1, slots="[14]"),
                  23: it("ETC", type_=1), 24: it("Package", type_=10, function=OPEN_ITEM_BOX, parameters="0,0,0,5"), 25: it("Action", slots="[14]")})
    drops = ({"5": {"1": {}}}, {}, {"7": {"DropBoxId": 5}})
    assert add_new(catalog, items, [(21, None), (22, None), (23, None), (24, None), (25, None)], drops, rides, glides, emotes) == (4, {"no equip slot": 1})
    by_id = {entry["Id"]: entry for entry in catalog["Entries"]}
    assert by_id[21]["Price"] == CAPSULE_PRICE and by_id[21]["TabId"] == 40400 and by_id[24]["Price"] == 300 and by_id[24]["TabId"] == 40000
    assert by_id[25]["TabId"] == 40400
    assert by_id[22]["TabId"] == 40109 and by_id[22]["Price"] == 1200 and by_id[22]["Rarity"] == 4 and 23 not in by_id
    # an emote (type 2, subtype 14) whose skill the client does not list is left out; an item of another subtype is no emote
    items.update({26: it("Action", skill=9, subtype=14), 27: it("Action", skill=11, subtype=14), 28: it("Action", skill=11)})
    assert add_new(catalog, items, [(26, None), (27, None), (28, None)], drops, rides, glides, emotes) == (2, {"an emote the client does not list": 1})
    assert 26 in {entry["Id"] for entry in catalog["Entries"]} and unlisted_emote(items[27], emotes) and not unlisted_emote(items[28], emotes)
    try:
        new_ids({"Entries": [e(k, 40400) for k in range(NEW_TAB_MAX + 1)]}, [(k, None) for k in range(NEW_TAB_MAX + 1)])
    except AssertionError:
        pass
    else:
        raise AssertionError("more New ids than one Featured page holds must fail")
    # organize_catalog: new entries come before the tab moves (a capsule ends in Featured), prize rows never join New, and a
    # run that only adds prize entries is a change
    db = sqlite3.connect(":memory:")
    db.executescript('create table item (Id); create table skill (Id); create table ride (Id); create table npc (Id); '
                     'create table "server-table" (Name, "Table"); create table "table" (Name, "Table");')
    db.executemany("insert into item values (?)", [(31,), (32,), (33,)])
    db.execute("insert into npc values (11000512)")
    for kind, name, table in (("server-table", "individualItemDrop.xml", {"Entries": {"5": {"1": {"Items": [{"Ids": [33]}]}}}}),
                              ("server-table", "globalItemDrop*.xml", {"DropGroups": {}}),
                              ("table", "gacha_info.xml", {"Entries": {"7": {"DropBoxId": 5}}}), ("table", "na/shop_*.xml", {"Entries": {}}),
                              ("server-table", "shop_merat_custom.xml", {"Entries": {"1": {"Id": 1, "TabId": FEATURED, "ItemId": 31},
                                                                                     "2": {"Id": 2, "ParentId": 1, "TabId": FEATURED, "ItemId": 31}}})):
        db.execute(f'insert into "{kind}" values (?, ?)', (name, json.dumps(table)))
    catalog = {"RarityPrices": {"1": 300}, "Entries": [e(30, 40000), e(34, 40400)]}
    items = {30: it("Package"), 31: it("Package", type_=10, function=OPEN_GACHA_BOX, parameters="7"), 32: it("Action"), 33: it("Action"),
             34: it("Action")}
    result = organize_catalog(catalog, items, db, [(31, None)], [(32, None)])
    by_id = {entry["Id"]: entry for entry in catalog["Entries"]}
    assert result[4] == 1 and result[7] == 1 and catalog["New"] == [31] and by_id[31]["TabId"] == FEATURED and by_id[32]["TabId"] == 40400
    result = organize_catalog(catalog, items, db, [(31, None)], [(32, None), (33, None)])
    assert result[7] == 1 and catalog_changed(result) and catalog["New"] == [31]
    assert not catalog_changed(organize_catalog(catalog, items, db, [(31, None)], [(32, None), (33, None)]))
    # a maid stays when the data has the NPC her cube spawns, and leaves when it lacks it or her cube spawns none
    catalog["Entries"] += [e(35, FURNITURE), e(36, FURNITURE), e(37, FURNITURE)]
    items.update({35: dict(it("Maid"), cube=80000000), 36: dict(it("Maid"), cube=80000001), 37: dict(it("Maid"), cube=80000099)})
    result = organize_catalog(catalog, items, db, [(31, None)], [(32, None), (33, None)], {"80000000": 11000512, "80000001": 11000766})
    assert result[9] == 2 and catalog_changed(result) and {entry["Id"] for entry in catalog["Entries"]} == {30, 31, 32, 33, 34, 35}
    assert not catalog_changed(organize_catalog(catalog, items, db, [(31, None)], [(32, None), (33, None)], {"80000000": 11000512}))
    # a pet capsule goes to Pets, not Featured; an emote entry the client does not list leaves, and its list row does not bring it back
    catalog["Entries"] += [e(38, FEATURED), e(39, 40400), e(40, 40400)]
    items.update({38: dict(it("PetPack", type_=10, function=OPEN_ITEM_BOX, parameters="0,0,0,5"), name="Pengy Capsule"),
                  39: it("Action", skill=9, subtype=14), 40: it("Action", skill=11, subtype=14)})
    result = organize_catalog(catalog, items, db, [(31, None), (40, None)], [(32, None), (33, None)], {"80000000": 11000512}, emotes)
    by_id = {entry["Id"]: entry for entry in catalog["Entries"]}
    assert result[0] == {(FEATURED, PETS): 1} and by_id[38]["TabId"] == PETS and result[10] == 1 and 39 in by_id and 40 not in by_id
    result = organize_catalog(catalog, items, db, [(31, None), (40, None)], [(32, None), (33, None)], {"80000000": 11000512}, emotes)
    assert not catalog_changed(result) and result[5] == {"an emote the client does not list": 1} and catalog["New"] == [31]
    # Featured holds at most FEATURED_MAX entries with the shipped ones (here 2)
    organize_catalog({"RarityPrices": {"1": 300}, "Entries": [e(k, FEATURED) for k in range(100, 100 + FEATURED_MAX - 2)]}, items, db)
    try:
        organize_catalog({"RarityPrices": {"1": 300}, "Entries": [e(k, FEATURED) for k in range(100, 100 + FEATURED_MAX - 1)]}, items, db)
    except AssertionError:
        pass
    else:
        raise AssertionError("more Featured entries than the client pages must fail")
    print("selftest ok")
    return 0


def organize_mod(path):
    tree = ET.parse(path)
    section = next(c for c in tree.getroot() if c.get("id") == PREMIUM_SECTION)
    old = {int(tab.get("id")): tab for tab in section.findall("tab")}
    for tab in list(section):
        section.remove(tab)
    for priority, (tab_id, name, symbol) in enumerate(LAYOUT, 1):
        tab = old.get(tab_id)
        if tab is None:
            tab = ET.Element("tab", {"name": name, "id": str(tab_id), "static": "1"})
        tab.set("priority", str(priority))
        tab.set("symbol", "UIMeratMarketDialog_symbol_" + symbol)
        if tab_id == TEMPLATES:
            tab.set("sortGender", "true")
        for sub in tab.findall("tab"):
            if sub.get("id") == str(CRATE_SPOTLIGHT):
                tab.remove(sub)
        section.append(tab)
    ET.indent(tree, "\t")
    return '<?xml version="1.0" encoding="utf-8"?>\n' + ET.tostring(tree.getroot(), encoding="unicode") + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--db", default=os.path.join(ROOT, "SaveData", "maple-data.db"))
    parser.add_argument("--emote-strings", help="the client's string/en/kordynamicaction.xml, whose skills the Emotes window lists")
    parser.add_argument("--check", action="store_true", help="report only, write nothing")
    parser.add_argument("--selftest", action="store_true", help="run the self-test (fixtures only: no data file, no catalog)")
    args = parser.parse_args()
    if args.selftest:
        return selftest()
    if not args.emote_strings:
        parser.error("--emote-strings is required")

    db = sqlite3.connect(f"file:{args.db}?mode=ro", uri=True)
    items = load_items(db)
    emotes = {int(key.get("skillID")) for key in ET.parse(args.emote_strings).getroot().iter("key")}
    with open(CATALOG, encoding="utf-8") as f:
        catalog = json.load(f)
    with open(CUBE_SPAWNS, encoding="utf-8") as f:
        spawns = json.load(f)["CubeSpawns"]
    result = organize_catalog(catalog, items, db, load_new(NEW_LIST), load_new(PRIZE_LIST), spawns, emotes)
    moves, added, replaced, removed, new_added, left_out, new_changed, prize_added, prize_left_out, maids, unlisted = result
    xml, xml_changed = None, False
    if os.path.exists(MOD_XML):
        xml = organize_mod(MOD_XML)
        with open(MOD_XML, encoding="utf-8") as f:
            xml_changed = f.read() != xml

    counts = {}
    for entry in catalog["Entries"]:
        counts[entry["TabId"]] = counts.get(entry["TabId"], 0) + 1
    for (src, dst), n in sorted(moves.items()):
        print(f"move {n:5} entries: tab {src} -> {dst}")
    print(f"add  {added:5} furniture entries (tab {FURNITURE})")
    print(f"replaced {replaced:5} pet boxes by their pet items (tab {PETS})")
    print(f"removed  {removed:5} boxes that open to nothing")
    print(f"removed  {maids:5} maids whose assistant the data lacks")
    print(f"removed  {unlisted:5} emotes the client does not list")
    print(f"add  {new_added:5} new entries; New tab: {len(catalog['New'])} entries", "(changed)" if new_changed else "(unchanged)")
    for reason, n in sorted(left_out.items()):
        print(f"left out {n:5} new rows: {reason}")
    print(f"add  {prize_added:5} capsule prize entries (A7)")
    for reason, n in sorted(prize_left_out.items()):
        print(f"left out {n:5} capsule prize rows: {reason}")
    print("entries per tab:", dict(sorted(counts.items())))
    print("client tab list:", "not found, skipped" if xml is None else "changes" if xml_changed else "unchanged")
    assert CRATE_SPOTLIGHT not in counts, "Crate Spotlight still has entries"
    changed = catalog_changed(result)
    if args.check:
        return 1 if changed or xml_changed else 0
    if changed:
        with open(CATALOG, "w", encoding="utf-8", newline="\r\n") as f:
            json.dump(catalog, f, indent=1)
            f.write("\n")  # the shipped catalog ends with a newline
    if xml_changed:
        with open(MOD_XML, "w", encoding="utf-8", newline="\r\n") as f:
            f.write(xml)
    print("written" if changed or xml_changed else "nothing to change")
    return 0


if __name__ == "__main__":
    sys.exit(main())
