r"""Organizes the offline Meret Market so it is easy to browse (docs/ADAPTATIONS.md, A21).

  python -I tools/organize_meret_market.py [--db SaveData/maple-data.db] [--check]

Rewrites the tab of each entry in Maple2/offline-meret-catalog.json from the game data, adds the Meret furniture that no
shop sells, and writes the matching tab list into the client mod client-mods/offline-shop (Xml/table/
meratmarketcategory.xml, section 100000). Running it again changes nothing. --check only reports what would change.

  Featured (tab 10)            every capsule: gacha capsules, random pet capsules, "Capsule"/"Crate" boxes
  Packages (40000)             packages and set packs (capsules moved out)
  Design Templates (40700)     every design template, mount template, template voucher and gift box (by name)
  Outfits (40100)              unchanged sub-tabs; Crate Spotlight (40110) removed, its coins go to Functional
  Summons (40200)              mounts and pets (pet capsules moved out)
  Furniture (40800)            maids, plus Meret furniture the housing shop does not sell
  Functional, Expression, Badges, Lifestyle   unchanged apart from the moves above

Tab names reuse strings the client already has (categoryname.xml: 2074 Design Templates, 9007 Furniture).
The server side of the tab list is OfflineEconomy.Apply(MeretMarketCategoryTable).
"""
import argparse
import json
import os
import re
import sqlite3
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CATALOG = os.path.join(ROOT, "Maple2", "offline-meret-catalog.json")
# The client requests regional tables without the locale folder (it adds "na/" below the mod hook), so the mod serves table/<name>.xml.
MOD_XML = os.path.join(ROOT, "client-mods", "offline-shop", "Data", "Xml", "table", "meratmarketcategory.xml")

FEATURED, PACKAGES, OUTFITS, CRATE_SPOTLIGHT, SUMMONS, PETS, FUNCTIONAL = 10, 40000, 40100, 40110, 40200, 40203, 40300
TEMPLATES, FURNITURE = 40700, 40800
OPEN_GACHA_BOX = 36
BOX_FUNCTIONS = {36, 38, 39, 40, 41, 52}  # gacha, item boxes (plain, Lulu, Lulu simple, with key) and select boxes
FURNITURE_PRICE = 30  # Meret furniture without a price in the data gets the most common furniture price
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
    for item_id, name, prop, func, option in db.execute("select Id, Name, Property, Function, Option from item"):
        prop = json.loads(prop) if prop and prop != "None" else {}
        try:
            func_type = json.loads(func).get("Type") if func and func.startswith("{") else None
        except ValueError:
            func_type = None
        try:
            rarity = json.loads(option).get("ConstantId", 1) if option and option.startswith("{") else 1
        except ValueError:
            rarity = 1
        items[item_id] = {"name": name or "", "category": prop.get("Category", ""), "function": func_type, "rarity": rarity}
    return items


def target_tab(entry, item):
    if item is None:
        return entry["TabId"]
    if re.search(r"\bTemplates?\b", item["name"]):
        return TEMPLATES
    if item["function"] == OPEN_GACHA_BOX or item["category"] == "PetPack" or (
            item["function"] in BOX_FUNCTIONS and re.search(r"\b(Capsule|Crate)\b", item["name"])):
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


def organize_catalog(catalog, items, db):
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
    return moves, added


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
    parser.add_argument("--check", action="store_true", help="report only, write nothing")
    args = parser.parse_args()

    db = sqlite3.connect(f"file:{args.db}?mode=ro", uri=True)
    items = load_items(db)
    with open(CATALOG, encoding="utf-8") as f:
        catalog = json.load(f)
    moves, added = organize_catalog(catalog, items, db)
    xml = organize_mod(MOD_XML)
    with open(MOD_XML, encoding="utf-8") as f:
        xml_changed = f.read() != xml

    counts = {}
    for entry in catalog["Entries"]:
        counts[entry["TabId"]] = counts.get(entry["TabId"], 0) + 1
    for (src, dst), n in sorted(moves.items()):
        print(f"move {n:5} entries: tab {src} -> {dst}")
    print(f"add  {added:5} furniture entries (tab {FURNITURE})")
    print("entries per tab:", dict(sorted(counts.items())))
    print("client tab list:", "changes" if xml_changed else "unchanged")
    # The Featured page is sent in one packet whose slot count is a byte.
    assert counts.get(FEATURED, 0) < 255, "too many Featured entries for one page"
    assert CRATE_SPOTLIGHT not in counts, "Crate Spotlight still has entries"
    if args.check:
        return 0
    if moves or added:
        with open(CATALOG, "w", encoding="utf-8", newline="\r\n") as f:
            json.dump(catalog, f, indent=1)
    if xml_changed:
        with open(MOD_XML, "w", encoding="utf-8", newline="\r\n") as f:
            f.write(xml)
    print("written" if moves or added or xml_changed else "nothing to change")
    return 0


if __name__ == "__main__":
    sys.exit(main())
