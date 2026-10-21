#!/usr/bin/env python3
"""
Market pool generator for MapleStory2 offline Black Market.
"""

import argparse
import json
import sqlite3
import sys
from collections import defaultdict


def select(rows, min_level, max_level, per_category):
    """
    Select items based on criteria from the database rows.

    Args:
        rows: List of tuples (id, name, property, limit, option)
        min_level: Minimum level filter
        max_level: Maximum level filter
        per_category: Maximum items per category

    Returns:
        List of item dictionaries with id and rarity (if applicable)
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

        # Check Black Market category
        black_market_category = property_data.get('BlackMarketCategory', '')
        if not black_market_category or black_market_category == 'None':
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

        # Add item to filtered list with category
        rarity = option_data.get('ConstantId', 0)
        item_dict = {'id': item_id}
        if 1 <= rarity <= 6:
            item_dict['rarity'] = rarity

        filtered_items.append((item_dict, black_market_category))

    # Group by category and select items
    categorized_items = defaultdict(list)
    for item_dict, category in filtered_items:
        categorized_items[category].append(item_dict)

    # Select at most per_category items from each category
    selected_items = []
    for category, items in categorized_items.items():
        if len(items) <= per_category:
            selected_items.extend(items)
        else:
            # Evenly over the category's ids in id order
            items = sorted(items, key=lambda x: x['id'])
            n = len(items)
            selected_items.extend(items[i * n // per_category] for i in range(per_category))

    # Sort by id
    selected_items.sort(key=lambda x: x['id'])

    return selected_items


def selftest():
    row = lambda i, name, cat, level, transfer, option='{}': (i, name, '{"BlackMarketCategory": "%s"}' % cat, '{"Level": %d, "TransferType": %d}' % (level, transfer), option)
    rows = [
        row(1, 'None category', 'None', 50, 0),
        row(2, 'Untradeable', '7', 50, 1),
        row(3, 'Below the band', '7', 40, 0),
        row(4, '', '7', 50, 0),
        row(5, 'Rarity 4', '9', 60, 3, '{"ConstantId": 4}'),
        row(6, 'Table id', '9', 60, 5, '{"ConstantId": 11200001}'),
    ] + [row(10 + i, f'Five {i}', '23', 55, 6) for i in range(5)]
    result = select(rows, 50, 70, 2)
    assert result == [{'id': 5, 'rarity': 4}, {'id': 6}, {'id': 10}, {'id': 12}], result
    print("selftest ok")


def main():
    if sys.argv[1:] == ["--selftest"]:
        selftest()
        return
    parser = argparse.ArgumentParser(description='Generate the offline Black Market pool (R12-23) for offline-market.json')
    parser.add_argument('--db', required=True, help='Path to maple-data.db (opened read-only)')
    parser.add_argument('--out', required=True, help='Output file path')
    parser.add_argument('--min-level', type=int, default=50, help='Minimum level (default: 50)')
    parser.add_argument('--max-level', type=int, default=70, help='Maximum level (default: 70)')
    parser.add_argument('--per-category', type=int, default=3, help='Items per category (default: 3)')
    parser.add_argument('--count', type=int, default=60, help='Listings per World start (default: 60)')
    parser.add_argument('--price-multiplier', type=float, default=3.0, help='Price multiplier (default: 3.0)')
    args = parser.parse_args()

    conn = sqlite3.connect(f"file:{args.db}?mode=ro", uri=True)
    rows = conn.execute("SELECT Id, Name, Property, `Limit`, Option FROM item").fetchall()
    conn.close()
    items = select(rows, args.min_level, args.max_level, args.per_category)
    with open(args.out, 'w', encoding='utf-8', newline='\n') as f:
        json.dump({"count": args.count, "priceMultiplier": args.price_multiplier, "items": items}, f, indent=1, ensure_ascii=False)
        f.write('\n')
    print(f"{len(items)} items")


if __name__ == '__main__':
    main()
