#!/usr/bin/env python3
"""
Content registry generation tool.

Generates a Markdown registry of content that exists in the merged database but not in the global database.
"""

import argparse
import os
import re
import sqlite3
import sys
import tempfile

# The four statuses of a record. A status and its Test cell survive a regeneration (existing_cells) and are set with --mark (CR-02).
STATUSES = {
    'in data': 'ingested, not tested',
    'checked in client': 'spawned or worn in the real client and looked right; the Test cell names the sample',
    'played': 'played in the game in a session; the Test cell names the session',
    'unsupported': 'listed, not restored; the Test cell says why',
}
# kind (the table name) -> registry section
SECTIONS = {'quest': 'Quests', 'npc': 'NPCs', 'map': 'Maps', 'item': 'Items'}


def get_table_data(conn, table_name):
    """Get all Id and Name from a table, ordered by Id."""
    cursor = conn.cursor()
    cursor.execute(f'SELECT Id, Name FROM "{table_name}" ORDER BY Id')
    return cursor.fetchall()


def compare_databases(global_db_path, merged_db_path):
    """
    Compare databases and return added content.
    
    Returns:
        dict: Dictionary with tables as keys and lists of added rows as values
    """
    # Connect to databases
    global_conn = sqlite3.connect(f"file:{global_db_path}?mode=ro", uri=True)
    merged_conn = sqlite3.connect(f"file:{merged_db_path}?mode=ro", uri=True)
    
    try:
        # Get table names
        global_cursor = global_conn.cursor()
        global_cursor.execute("SELECT name FROM sqlite_master WHERE type='table'")
        tables = [row[0] for row in global_cursor.fetchall() if row[0] in ['quest', 'npc', 'map', 'item']]
        
        results = {}
        
        for table in tables:
            # Get all rows from both databases
            global_rows = get_table_data(global_conn, table)
            merged_rows = get_table_data(merged_conn, table)
            
            # Create set of Ids from global database for comparison
            global_ids = {row[0] for row in global_rows}
            
            # Find added rows (in merged but not in global) - only check by Id
            added_rows = sorted((row for row in merged_rows if row[0] not in global_ids), key=lambda x: x[0])
            
            results[table] = added_rows
        
        return results
    
    finally:
        global_conn.close()
        merged_conn.close()


def escape_markdown(text):
    """Escape pipe characters in text for Markdown table cells."""
    if text is None:
        return ""
    return text.replace('|', '\\|')


def existing_cells(out_path):
    """(section, id) -> (status, test) from the rows of an existing registry file; {} when there is none. Sections are
    the '## ' headings (Quests, NPCs, Maps, Items)."""
    if not os.path.exists(out_path):
        return {}

    result = {}
    current_section = None

    with open(out_path, 'r', encoding='utf-8') as f:
        for line in f:
            line = line.strip()

            # Check for section headers
            if line.startswith('## '):
                current_section = line[3:]  # Remove '## '
                continue

            # Look for table rows with digits in first cell (Id). Empty cells count (an empty Test cell is common);
            # an escaped pipe inside a name is not a separator.
            if line.startswith('| ') and line.endswith('|'):
                cells = [cell.strip() for cell in re.split(r'(?<!\\)\|', line[1:-1])]
                if len(cells) >= 4 and cells[0].isdigit():
                    result[(current_section, cells[0])] = (cells[2], cells[3])

    return result


def table_row(row_id, name, status, test):
    """One registry row; an empty Test cell stays '| |' as the registry has always written it."""
    return f"| {row_id} | {name} | {status} | {test} |\n" if test else f"| {row_id} | {name} | {status} | |\n"


def apply_marks(kept, marks, added_content):
    """Set (status, test) for the ids of each mark, a (kind, status, test, ids) tuple; an unknown kind, status or id is an error."""
    for kind, status, test, ids in marks:
        if kind not in SECTIONS:
            raise ValueError(f"unknown kind {kind!r}, use one of {', '.join(SECTIONS)}")
        if status not in STATUSES:
            raise ValueError(f"unknown status {status!r}, use one of {', '.join(repr(k) for k in STATUSES)}")
        known = {str(row[0]) for row in added_content[kind]}
        wanted = [i.strip() for i in ids.split(',') if i.strip()]
        missing = [i for i in wanted if i not in known]
        if missing or not wanted:
            raise ValueError(f"{kind} ids not in the registry: {', '.join(missing) if missing else '(none given)'}")
        for i in wanted:
            kept[(SECTIONS[kind], i)] = (status, escape_markdown(test))


def generate_registry(global_db_path, merged_db_path, source, out_path, marks=()):
    """Generate the content registry markdown file. Returns the (section, id, status) of kept cells whose record is gone."""
    # Get added content
    added_content = compare_databases(global_db_path, merged_db_path)

    # Read existing cells to preserve the status/test entries of an earlier run
    kept = existing_cells(out_path)
    apply_marks(kept, marks, added_content)

    # Generate content
    content = "# Content registry\n\n"
    content += "Generated by `tools/content_registry.py` from two ingests: records in the merged data that the Global data lacks.\n"
    content += f"Source: {source}. Status values: " + ", ".join(f"`{name}` ({meaning})" for name, meaning in STATUSES.items()) + ".\n"
    content += "Only this tool writes the table rows: a regeneration keeps the Status and Test cells of every record still in the data, and\n"
    content += "`--mark KIND STATUS TEST IDS` sets them (kind quest, npc, map or item; ids separated by commas); never edit a row by hand.\n\n"

    # Summary table
    content += "| Kind | Added |\n"
    content += "|---|---|\n"
    for table in SECTIONS:
        content += f"| {table} | {len(added_content[table])} |\n"

    content += "\n"

    used = set()
    for index, (table, section) in enumerate(SECTIONS.items()):
        content += f"## {section}\n\n"
        if added_content[table]:
            content += "| Id | Name | Status | Test |\n"
            content += "|---|---|---|---|\n"
            for row_id, name in added_content[table]:
                escaped_name = escape_markdown(name) if name else "(no name)"
                key = (section, str(row_id))
                used.add(key)
                status, test = kept.get(key, ("in data", ""))
                content += table_row(row_id, escaped_name, status, test)
        else:
            content += "None.\n" if index == len(SECTIONS) - 1 else "None.\n\n"

    # Write to file
    with open(out_path, 'w', encoding='utf-8') as f:
        f.write(content)

    for (section, row_id), (status, test) in sorted(kept.items()):
        if status not in STATUSES:
            print(f"warning: {section} {row_id} has the status {status!r}, which is not one of the four", file=sys.stderr)
    return [(section, row_id, cells[0]) for (section, row_id), cells in sorted(kept.items()) if (section, row_id) not in used and cells[0] != "in data"]


def selftest():
    """Run self-test of the tool."""
    # Create temporary databases
    with tempfile.NamedTemporaryFile(suffix='.db', delete=False) as db1_file, \
         tempfile.NamedTemporaryFile(suffix='.db', delete=False) as db2_file:
        db1_path = db1_file.name
        db2_path = db2_file.name
    
    try:
        # Create test databases with the required tables
        conn1 = sqlite3.connect(db1_path)
        cursor1 = conn1.cursor()
        
        # Create tables with only Id and Name columns
        for table in ['quest', 'npc', 'map', 'item']:
            cursor1.execute(f"CREATE TABLE {table} (Id INTEGER PRIMARY KEY, Name TEXT)")
        
        # Insert test data - global database has some data
        cursor1.execute("INSERT INTO quest VALUES (1, 'Quest 1')")
        cursor1.execute("INSERT INTO npc VALUES (1, 'NPC 1')")
        cursor1.execute("INSERT INTO map VALUES (1, 'Map 1')")
        cursor1.execute("INSERT INTO item VALUES (1, 'Item 1')")
        
        # Insert data with same Id but different names to test the fix
        cursor1.execute("INSERT INTO quest VALUES (10001003, 'Quest 10001003 Old Name')")
        cursor1.execute("INSERT INTO npc VALUES (191, 'NPC 191 Old Name')")
        cursor1.execute("INSERT INTO map VALUES (16, 'Map 16 Old Name')")
        cursor1.execute("INSERT INTO item VALUES (4048, 'Item 4048 Old Name')")
        
        conn1.commit()
        conn1.close()
        
        # Merged database has:
        conn2 = sqlite3.connect(db2_path)
        cursor2 = conn2.cursor()
        
        # Create same tables
        for table in ['quest', 'npc', 'map', 'item']:
            cursor2.execute(f"CREATE TABLE {table} (Id INTEGER PRIMARY KEY, Name TEXT)")
        
        # Insert test data - merged has additional entries plus the same Ids with different names
        cursor2.execute("INSERT INTO quest VALUES (1, 'Quest 1'), (2, 'Quest 2'), (10001003, 'Quest 10001003 New Name')")
        cursor2.execute("INSERT INTO npc VALUES (1, 'NPC 1'), (2, 'NPC 2'), (191, 'NPC 191 New Name')")
        cursor2.execute("INSERT INTO map VALUES (1, 'Map 1'), (2, 'Map 2'), (16, 'Map 16 New Name')")
        cursor2.execute("INSERT INTO item VALUES (1, 'Item 1'), (2, 'Item 2'), (4048, 'Item 4048 New Name')")
        
        conn2.commit()
        conn2.close()
        
        # Run the tool with test data
        test_source = "test source"
        test_out_path = os.path.join(os.path.dirname(db1_path), "test_registry.md")
        
        generate_registry(db1_path, db2_path, test_source, test_out_path)
        
        # Check that file was created and contains expected content
        with open(test_out_path, 'r', encoding='utf-8') as f:
            content = f.read()
        
        # Verify the content structure - basic checks
        assert "# Content registry" in content
        assert "Source: test source" in content
        assert "| quest | 1 |" in content  # 1 added quest (Quest 2, not 10001003 which exists with different name)
        assert "| npc | 1 |" in content   # 1 added NPC (NPC 2, not 191 which exists with different name)
        assert "| map | 1 |" in content   # 1 added map (Map 2, not 16 which exists with different name)
        assert "| item | 1 |" in content  # 1 added item (Item 2, not 4048 which exists with different name)
        
        # Check for the added content rows (should be Quest 2, NPC 2, Item 2)
        assert "Quest 2" in content or "Quest 2" in content.replace('\\', '')
        assert "NPC 2" in content or "NPC 2" in content.replace('\\', '')
        assert "Item 2" in content or "Item 2" in content.replace('\\', '')
        
        # Verify that records with same Id but different names are NOT listed as added
        # The specific records mentioned in the task (10001003, 191, 16, 4048) should not appear
        assert "10001003" not in content or "Quest 10001003 New Name" not in content
        assert "191" not in content or "NPC 191 New Name" not in content
        assert "16" not in content or "Map 16 New Name" not in content
        assert "4048" not in content or "Item 4048 New Name" not in content
        
        # Test determinism - run again with same inputs
        test_out_path2 = os.path.join(os.path.dirname(db1_path), "test_registry2.md")
        generate_registry(db1_path, db2_path, test_source, test_out_path2)
        
        with open(test_out_path2, 'r', encoding='utf-8') as f:
            content2 = f.read()
        
        # Should be identical
        assert content == content2

        # A Status and Test set by hand survive a regeneration (M5-06), also with an empty Test cell
        quest_row = next(line for line in content.splitlines() if "Quest 2" in line)
        npc_row = next(line for line in content.splitlines() if "NPC 2" in line)
        edited = content.replace(quest_row, quest_row.replace("| in data | |", "| played | PC-1 2026-10-20 |"))
        edited = edited.replace(npc_row, npc_row.replace("| in data | |", "| unsupported | |"))
        assert edited != content
        with open(test_out_path, 'w', encoding='utf-8') as f:
            f.write(edited)
        generate_registry(db1_path, db2_path, test_source, test_out_path)
        with open(test_out_path, 'r', encoding='utf-8') as f:
            regenerated = f.read()
        assert regenerated == edited, "hand-set cells are kept"
        assert "| in data | |" in regenerated, "the other rows stay in data"

        # Every kind keeps its cells (CR-02): mark one record of each kind, regenerate, and the marks are still there
        marks = [('quest', 'played', 'PC-1 2026-11-16', '2'), ('npc', 'unsupported', '', '2'), ('map', 'checked in client', 'a | b', '2'), ('item', 'checked in client', 'sample', '2')]
        assert generate_registry(db1_path, db2_path, test_source, test_out_path, marks) == []
        with open(test_out_path, 'r', encoding='utf-8') as f:
            marked = f.read()
        cells = existing_cells(test_out_path)
        for section, status in (('Quests', 'played'), ('NPCs', 'unsupported'), ('Maps', 'checked in client'), ('Items', 'checked in client')):
            assert cells[(section, '2')][0] == status, section
        assert cells[('Maps', '2')][1] == 'a \\| b', "an escaped pipe stays escaped"
        generate_registry(db1_path, db2_path, test_source, test_out_path)
        with open(test_out_path, 'r', encoding='utf-8') as f:
            assert f.read() == marked, "a regeneration keeps the marks of all four kinds"
        for status in STATUSES:
            assert f"`{status}`" in marked, "the head defines every status"

        # A status of a record that left the data is reported, not silently lost
        conn = sqlite3.connect(db2_path)
        conn.execute("DELETE FROM item WHERE Id = 2")
        conn.commit()
        conn.close()
        assert generate_registry(db1_path, db2_path, test_source, test_out_path) == [('Items', '2', 'checked in client')]

        # A mark with an unknown kind, status or id is refused
        for bad in (('thing', 'played', '', '2'), ('quest', 'done', '', '2'), ('quest', 'played', '', '99'), ('quest', 'played', '', '')):
            try:
                generate_registry(db1_path, db2_path, test_source, test_out_path, [bad])
            except ValueError:
                continue
            raise AssertionError(f"mark {bad} was accepted")

        print("selftest passed")
        
    finally:
        os.unlink(db1_path)
        os.unlink(db2_path)
        try:
            os.unlink(test_out_path)
            os.unlink(test_out_path2)
        except:
            pass


def main():
    parser = argparse.ArgumentParser(description="Generate content registry from two databases")
    parser.add_argument("--global-db", help="Path to global database")
    parser.add_argument("--merged-db", help="Path to merged database")
    parser.add_argument("--source", help="Source description for the registry")
    parser.add_argument("--out", help="Output Markdown file path")
    parser.add_argument("--mark", nargs=4, action="append", default=[], metavar=("KIND", "STATUS", "TEST", "IDS"),
                        help="Set the status and test of records: KIND quest|npc|map|item, STATUS one of the four, TEST text (may be empty), IDS comma-separated")
    parser.add_argument("--selftest", action="store_true", help="Run self-test")

    args = parser.parse_args()

    if args.selftest:
        selftest()
        sys.exit(0)
    
    # Check required arguments
    if not args.global_db or not args.merged_db or not args.source or not args.out:
        parser.error("--global-db, --merged-db, --source and --out are required")
    
    # Validate file existence
    for path in (args.global_db, args.merged_db):
        if not os.path.exists(path):
            print(f"Database file does not exist: {path}", file=sys.stderr)
            sys.exit(2)
    
    try:
        dropped = generate_registry(args.global_db, args.merged_db, args.source, args.out, [tuple(m) for m in args.mark])
        for section, row_id, status in dropped:
            print(f"warning: {section} {row_id} was {status!r} and is no longer in the data; its row is gone", file=sys.stderr)
    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        sys.exit(2)


if __name__ == "__main__":
    main()