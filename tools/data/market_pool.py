#!/usr/bin/env python3
"""
Builds offline-market.json for the offline Black Market (R12-23, BM-02): the gear pool, the gear pricing and level rule, and the essentials.
"""

import argparse
import json
import re
import sqlite3
import sys
from collections import defaultdict


def select(rows, min_level, max_level, per_band):
    """
    Gear pool: tradeable gear ('*_equip' categories) with a name from min_level to max_level;
    at most per_band items of each category in each 10-level band, evenly over its ids,
    so every level has gear.

    rows: (id, name, property, limit, option) JSON texts as in the item table.
    """
    # Filter items based on criteria
    filtered_items = []

    for row in rows:
        item_id, name, property_json, limit_json, option_json = row

        # Parse JSON fields
        try:
            property_data = json.loads(property_json) if property_json else {}
            limit_data = json.loads(limit_json) if limit_json else {}
            option_data = json.loads(option_json) if option_json else {}
        except json.JSONDecodeError:
            continue  # Skip invalid JSON

        # Check Black Market category - only gear categories ending in '_equip'
        black_market_category = property_data.get('BlackMarketCategory', '')
        if not black_market_category or black_market_category == 'None':
            continue

        if not black_market_category.endswith('_equip'):
            continue

        # Check transfer type (0, 3, 5, 6 are tradeable)
        transfer_type = limit_data.get('TransferType', -1)
        if transfer_type not in [0, 3, 5, 6]:
            continue

        # Check level range
        level = limit_data.get('Level', 0)
        if not (min_level <= level <= max_level):
            continue

        # Check name
        if not name or name.strip() == '':
            continue

        # Add item to filtered list with category and level band
        rarity = option_data.get('ConstantId', 0)
        item_dict = {'id': item_id}
        if 1 <= rarity <= 6:
            item_dict['rarity'] = rarity

        # Group by (category, level_band) where level_band is level // 10
        level_band = level // 10
        filtered_items.append((item_dict, (black_market_category, level_band)))

    # Group by category and level band and select items
    categorized_items = defaultdict(list)
    for item_dict, key in filtered_items:
        categorized_items[key].append(item_dict)

    # Select at most per_band items from each category-band combination
    selected_items = []
    for (category, level_band), items in categorized_items.items():
        if len(items) <= per_band:
            selected_items.extend(items)
        else:
            # Evenly over the band's ids in id order
            items = sorted(items, key=lambda x: x['id'])
            n = len(items)
            selected_items.extend(items[i * n // per_band] for i in range(per_band))

    # Sort by id
    selected_items.sort(key=lambda x: x['id'])

    return selected_items


# Black Market essentials (BM-02): listed at every World start, each in its own amount, at a price per unit.
MATERIALS = ['Onyx Crystal', 'Chaos Onyx Crystal', 'Crystal Fragment', 'Metacell',
             'Blue Gem Dust', 'Purple Gem Dust', 'Orange Gem Dust', 'White Gem Dust', 'Red Gem Dust', 'Green Gem Dust',
             'Cyan Gem Dust', 'Yellow Gem Dust', 'Fusion Stone Dust', 'Fusion Stone Shard', 'Polished Fusion Stone', 'Fusion Stone Block']
GEM_STATS = ['Accuracy', 'Destruction', 'Dexterity', 'Life', 'Luck', 'Offense', 'Power', 'Wisdom']
GEM_FAMILIES = ([f'{stat} Gemstone' for stat in GEM_STATS] + [f'Fantastic {stat} Gemstone' for stat in GEM_STATS if stat != 'Destruction']
                + ['Advanced Destruction Gemstone'])
# Which variant of a name is listed: Black Market only first, then tradeable, bound on equip, bound on trade, bound on loot, untradeable.
TRANSFER_RANK = {6: 0, 0: 1, 3: 2, 5: 3, 2: 4, 1: 5, 4: 6, 7: 7}
# Price factor of materials and potions by rarity, Normal to Ascendant.
RARITY_FACTOR = [1, 3, 10, 100, 300, 1000]
MATERIAL_PRICE = 300
GEM_PRICE, GEM_STEP = 5000, 2.5
POTION_PRICE, BUFF_PRICE, EXPERIENCE_PRICE = 1000, 5000, 20000
# Gear: price = perLevelSquared x level x level x rarity factor; Normal gear (weight 0) is never listed (maintainer 2026-10-22).
GEAR_PRICING = {'perLevelSquared': 20, 'rarity': [1, 2.5, 8, 50, 250, 1000], 'rarityWeights': [0, 25, 35, 30, 9, 1]}
# 60% of the gear near the level of the character played last: from 10 levels below it to 5 above.
LEVELS = {'below': 10, 'above': 5, 'share': 0.6}


def shop_meso_prices(table_json):
    """Lowest meso price of each item in shop_game.xml (a Cost without a Type, or Type 0, is in mesos)."""
    prices = {}

    def walk(node):
        if isinstance(node, dict):
            cost = node.get('Cost')
            if 'ItemId' in node and isinstance(cost, dict) and cost.get('Type', 0) == 0 and cost.get('Amount', 0) > 0:
                prices[node['ItemId']] = min(prices.get(node['ItemId'], cost['Amount']), cost['Amount'])
            for value in node.values():
                walk(value)
        elif isinstance(node, list):
            for value in node:
                walk(value)

    walk(json.loads(table_json))
    return prices


def essentials(rows, skill_names, shop_prices):
    """
    Upgrade materials, gemstones and potions, priced by rarity and power, never below an NPC shop's meso price.
    rows: (id, name, property, limit, option, function, skill) JSON texts as in the item table; skill_names: {skill id: name};
    shop_prices: {item id: lowest meso price in an NPC shop}. Returns offers {'id', 'amount', 'price'}, plus 'rarity' for
    materials and potions (gemstones take their default rarity in the game).
    """
    items = []
    for item_id, name, property_json, limit_json, option_json, function_json, skill_json in rows:
        try:
            property_data = json.loads(property_json) if property_json else {}
            limit_data = json.loads(limit_json) if limit_json else {}
            option_data = json.loads(option_json) if option_json else {}
            function_data = (json.loads(function_json) if function_json else None) or {}
            skill_data = (json.loads(skill_json) if skill_json else None) or {}
        except json.JSONDecodeError:
            continue
        constant = option_data.get('ConstantId', 0)
        items.append({'id': item_id, 'name': (name or '').strip(), 'category': str(property_data.get('BlackMarketCategory')),
                      'slot': max(1, property_data.get('SlotMax') or 1), 'transfer': limit_data.get('TransferType'),
                      'rarity': constant if 1 <= constant <= 6 else 1, 'function': function_data.get('Type'),
                      'skill': skill_data.get('Id') or 0})

    def pick(candidates):
        return min(candidates, key=lambda item: (TRANSFER_RANK.get(item['transfer'], 9), item['id']))

    def offer(item, amount, price, with_rarity=True):
        entry = {'id': item['id'], 'amount': min(item['slot'], amount), 'price': max(int(round(price)), shop_prices.get(item['id'], 0))}
        if with_rarity:
            entry['rarity'] = item['rarity']
        return entry

    result = []
    for name in MATERIALS:
        candidates = [item for item in items if item['name'] == name]
        if candidates:
            item = pick(candidates)
            result.append(offer(item, 9999, MATERIAL_PRICE * RARITY_FACTOR[item['rarity'] - 1]))

    gems = {}
    for item in items:
        match = re.match(r'^Tier (\d+) (.+ Gemstone)$', item['name'])
        if item['category'] == '40' and match and match.group(2) in GEM_FAMILIES:
            gems.setdefault((match.group(2), int(match.group(1))), []).append(item)
    for (family, tier), candidates in sorted(gems.items()):
        price = GEM_PRICE * GEM_STEP ** (tier - 1) * (2 if family.startswith(('Fantastic', 'Advanced')) else 1)
        result.append(offer(pick(candidates), 10, price, with_rarity=False))

    potions = {}
    for item in items:
        if (item['category'] in ('6', '31') and item['function'] is None and item['skill'] > 0 and item['name']
                and re.search(r'potion|elixir|tonic', item['name'], re.I) and not re.match(r'\(|test\b', item['name'], re.I)):
            potions.setdefault(item['name'], []).append(item)
    for name, candidates in sorted(potions.items()):
        item = pick(candidates)
        if 'Experience' in name:
            amount = re.search(r'\(([\d,]+)\)', name)
            price = EXPERIENCE_PRICE * max(1, int(amount.group(1).replace(',', '')) / 50000) if amount else EXPERIENCE_PRICE
        else:
            level = re.search(r'Lv\. ?(\d+)', skill_names.get(item['skill']) or '')
            power = max(1, int(level.group(1)) / 10) if level else 1
            price = (BUFF_PRICE if re.search(r'elixir|tonic', name, re.I) else POTION_PRICE) * power
        result.append(offer(item, 100, price * RARITY_FACTOR[item['rarity'] - 1]))
    return result


def selftest():
    row = lambda i, name, cat, level, transfer, option='{}': (i, name, '{"BlackMarketCategory": "%s"}' % cat, '{"Level": %d, "TransferType": %d}' % (level, transfer), option)
    rows = [
        row(1, 'None category', 'None', 50, 0),
        row(2, 'Untradeable', '113_equip', 50, 1),
        row(3, 'Below the levels', '113_equip', 9, 0),
        row(4, '', '113_equip', 50, 0),
        row(5, 'Rarity 4', '131_equip', 60, 3, '{"ConstantId": 4}'),
        row(6, 'Table id', '131_equip', 60, 5, '{"ConstantId": 11200001}'),
        row(7, 'Not gear', '23', 50, 0),
        row(8, 'Outfit', '113_skin', 50, 0),
    ] + [row(10 + i, f'Five {i}', '114_equip', 55, 6) for i in range(5)] + [row(20, 'Next band', '114_equip', 62, 0)]
    result = select(rows, 10, 70, 2)
    assert result == [{'id': 5, 'rarity': 4}, {'id': 6}, {'id': 10}, {'id': 12}, {'id': 20}], result

    shop = json.dumps({"Entries": {"101": {"1": {"ItemId": 7, "Cost": {"Amount": 900}}, "2": {"ItemId": 7, "Cost": {"Amount": 750}},
                                            "3": {"ItemId": 12, "Cost": {"Type": 1, "ItemId": 5, "Amount": 3}}}}})
    assert shop_meso_prices(shop) == {7: 750}, shop_meso_prices(shop)

    item = lambda i, name, cat, transfer, constant=1, function=None, skill=0: (
        i, name, '{"BlackMarketCategory": "%s", "SlotMax": 9999}' % cat, json.dumps({"TransferType": transfer}),
        '{"ConstantId": %d}' % constant, json.dumps(function) if function else None, json.dumps({"Id": skill}))
    erows = [
        item(1, 'Onyx Crystal', '25', 1), item(2, 'Onyx Crystal', '25', 6), item(3, 'Chaos Onyx Crystal', '25', 6, 4),
        item(4, 'Tier 1 Power Gemstone', '40', 6, 40200401), item(5, 'Tier 3 Fantastic Power Gemstone', '40', 6, 40201203),
        item(6, 'Tier 2 Rough Red Gemstone', '40', 1), item(7, 'Red Potion', '6', None, skill=900),
        item(8, 'Experience Potion (500,000)', '6', 1, skill=901), item(9, 'Lv. 50 Level-up Potion', '6', 2, 3, {"Type": 30}),
        item(10, 'Test Tonic - Pet Shares', '31', 1, skill=902), item(11, '(PC Cafe) Elixir', '6', 1, skill=903),
        item(12, 'Battle Frenzy Tonic', '31', 2, skill=904),
    ]
    got = {entry['id']: entry for entry in essentials(erows, {900: 'Lv. 30 Instant Recovery Potion'}, {12: 30000})}
    assert sorted(got) == [2, 3, 4, 5, 7, 8, 12], sorted(got)
    assert got[2] == {'id': 2, 'amount': 9999, 'price': 300, 'rarity': 1}, got[2]
    assert got[3] == {'id': 3, 'amount': 9999, 'price': 30000, 'rarity': 4}, got[3]
    assert got[4] == {'id': 4, 'amount': 10, 'price': 5000}, got[4]
    assert got[5] == {'id': 5, 'amount': 10, 'price': 62500}, got[5]
    assert got[7] == {'id': 7, 'amount': 100, 'price': 3000, 'rarity': 1}, got[7]
    assert got[8]['price'] == 200000, got[8]
    assert got[12]['price'] == 30000, got[12]  # never below the NPC shop's meso price
    print("selftest ok")


def main():
    if sys.argv[1:] == ["--selftest"]:
        selftest()
        return
    parser = argparse.ArgumentParser(description='Generate offline-market.json for the offline Black Market (R12-23, BM-02)')
    parser.add_argument('--db', required=True, help='Path to maple-data.db (opened read-only)')
    parser.add_argument('--out', required=True, help='Output file path')
    parser.add_argument('--min-level', type=int, default=1, help='Minimum level (default: 1)')
    parser.add_argument('--max-level', type=int, default=70, help='Maximum level (default: 70)')
    parser.add_argument('--per-band', type=int, default=2, help='Gear items per category and 10-level band (default: 2)')
    parser.add_argument('--count', type=int, default=60, help='Listings per World start (default: 60)')
    parser.add_argument('--price-multiplier', type=float, default=3.0, help='Price multiplier (default: 3.0)')
    args = parser.parse_args()

    conn = sqlite3.connect(f"file:{args.db}?mode=ro", uri=True)
    gear_rows = conn.execute("SELECT Id, Name, Property, `Limit`, Option FROM item").fetchall()
    essential_rows = conn.execute("SELECT Id, Name, Property, `Limit`, Option, Function, Skill FROM item").fetchall()
    skill_rows = conn.execute("SELECT Id, Name FROM skill").fetchall()
    shop_row = conn.execute("SELECT `Table` FROM `server-table` WHERE Name = 'shop_game.xml'").fetchone()
    conn.close()

    items = select(gear_rows, args.min_level, args.max_level, args.per_band)
    skill_names = {int(row[0]): row[1] for row in skill_rows}
    shop_prices = shop_meso_prices(shop_row[0]) if shop_row and shop_row[0] else {}
    listed = essentials(essential_rows, skill_names, shop_prices)

    with open(args.out, 'w', encoding='utf-8', newline='\n') as f:
        json.dump({"count": args.count, "priceMultiplier": args.price_multiplier, "pricing": GEAR_PRICING, "levels": LEVELS, "items": items, "essentials": listed}, f, indent=1, ensure_ascii=False)
        f.write('\n')
    print(f"{len(items)} gear items, {len(listed)} essentials")


if __name__ == '__main__':
    main()
