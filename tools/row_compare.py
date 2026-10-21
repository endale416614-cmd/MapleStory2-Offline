#!/usr/bin/env python3
"""
Row comparison tool for SQLite databases.

Compares two SQLite databases table by table on the rows whose primary key exists in a reference database.
"""

import argparse
import os
import sqlite3
import sys
import tempfile


def get_primary_key_columns(cursor, table_name):
    """Get primary key columns for a table, ordered by pk."""
    cursor.execute(f"PRAGMA table_info('{table_name}')")
    columns = cursor.fetchall()
    pk_columns = [col[1] for col in columns if col[5] > 0]  # pk > 0 means primary key
    return sorted(pk_columns, key=lambda col: next(c[5] for c in columns if c[1] == col))


def get_all_tables(cursor):
    """Get all table names from the database."""
    cursor.execute("SELECT name FROM sqlite_master WHERE type='table'")
    return [row[0] for row in cursor.fetchall()]


def compare_databases(ref_db_path, a_db_path, b_db_path, tables=None):
    """
    Compare databases and return comparison results.
    
    Returns:
        tuple: (results_dict, summary_dict)
    """
    # Connect to reference database
    ref_conn = sqlite3.connect(f"file:{ref_db_path}?mode=ro", uri=True)
    ref_cursor = ref_conn.cursor()
    
    # Connect to databases A and B
    a_conn = sqlite3.connect(f"file:{a_db_path}?mode=ro", uri=True)
    a_cursor = a_conn.cursor()
    
    b_conn = sqlite3.connect(f"file:{b_db_path}?mode=ro", uri=True)
    b_cursor = b_conn.cursor()
    
    try:
        # Get all table names
        all_tables = get_all_tables(ref_cursor)
        
        if tables is not None:
            all_tables = [t for t in all_tables if t in tables]
        
        results = []
        skipped_tables = []
        total_compared = 0
        total_with_diffs = 0
        
        for table in sorted(all_tables):
            # Skip the checksum table
            if table == "checksum":
                continue
                
            # Get primary key columns
            pk_columns = get_primary_key_columns(ref_cursor, table)
            
            # Skip tables without primary keys
            if not pk_columns:
                skipped_tables.append(table)
                continue
            
            # Build query for primary key values from reference database
            quoted_pk_columns = [f'"{col}"' for col in pk_columns]
            pk_query = f'SELECT {", ".join(quoted_pk_columns)} FROM "{table}"'
            
            ref_cursor.execute(pk_query)
            ref_keys = set(ref_cursor.fetchall())
            
            # Build query to get rows from A and B
            quoted_columns = [f'"{col}"' for col in pk_columns]
            quoted_columns.extend([f'"{col}"' for col in get_all_columns(ref_cursor, table) if col not in pk_columns])
            
            # For each key in reference, compare the row values
            same_count = 0
            diff_count = 0
            missing_a = 0
            missing_b = 0
            different_keys = []
            
            for key_tuple in ref_keys:
                # Get row from A
                a_query = f'SELECT {", ".join(quoted_columns)} FROM "{table}" WHERE {" AND ".join(f"{col} = ?" for col in quoted_pk_columns)}'
                a_cursor.execute(a_query, key_tuple)
                a_row = a_cursor.fetchone()
                
                # Get row from B
                b_cursor.execute(a_query, key_tuple)
                b_row = b_cursor.fetchone()
                
                if a_row is None:
                    missing_a += 1
                elif b_row is None:
                    missing_b += 1
                elif a_row == b_row:
                    same_count += 1
                else:
                    diff_count += 1
                    if len(different_keys) < 5:  # Only store first 5 different keys
                        different_keys.append(key_tuple)
            
            total_compared += 1
            if diff_count > 0 or missing_a > 0 or missing_b > 0:
                total_with_diffs += 1
            
            results.append((table, len(ref_keys), same_count, diff_count, missing_a, missing_b, different_keys))
        
        summary = {
            "tables_compared": total_compared,
            "tables_with_diffs": total_with_diffs,
            "skipped_tables": skipped_tables
        }
        
        return results, summary
        
    finally:
        ref_conn.close()
        a_conn.close()
        b_conn.close()


def get_all_columns(cursor, table_name):
    """Get all column names for a table."""
    cursor.execute(f"PRAGMA table_info('{table_name}')")
    return [col[1] for col in cursor.fetchall()]


def selftest():
    """Run self-test of the tool."""
    # Create temporary databases with test data
    with tempfile.NamedTemporaryFile(suffix='.db', delete=False) as db1_file, \
         tempfile.NamedTemporaryFile(suffix='.db', delete=False) as db2_file, \
         tempfile.NamedTemporaryFile(suffix='.db', delete=False) as db3_file:
        db1_path = db1_file.name
        db2_path = db2_file.name
        db3_path = db3_file.name
    
    try:
        # Create test database 1 - single column primary key
        conn1 = sqlite3.connect(db1_path)
        cursor1 = conn1.cursor()
        cursor1.execute("CREATE TABLE test_table1 (id INTEGER PRIMARY KEY, value TEXT)")
        cursor1.execute("INSERT INTO test_table1 VALUES (1, 'a'), (2, 'b'), (3, 'c')")
        conn1.commit()
        conn1.close()
        
        # Create test database 2 - same structure, different values
        conn2 = sqlite3.connect(db2_path)
        cursor2 = conn2.cursor()
        cursor2.execute("CREATE TABLE test_table1 (id INTEGER PRIMARY KEY, value TEXT)")
        cursor2.execute("INSERT INTO test_table1 VALUES (1, 'a'), (2, 'x'), (3, 'c')")
        conn2.commit()
        conn2.close()
        
        # Create test database 3 - same structure, same values
        conn3 = sqlite3.connect(db3_path)
        cursor3 = conn3.cursor()
        cursor3.execute("CREATE TABLE test_table1 (id INTEGER PRIMARY KEY, value TEXT)")
        cursor3.execute("INSERT INTO test_table1 VALUES (1, 'a'), (2, 'b'), (3, 'c')")
        conn3.commit()
        conn3.close()
        
        # Test 1: Same databases should be equal
        results, summary = compare_databases(db1_path, db1_path, db1_path)
        assert len(results) == 1
        assert results[0][1] == 3  # 3 keys
        assert results[0][2] == 3  # all same
        assert results[0][3] == 0  # no differences
        
        # Test 2: Different databases should show differences
        results, summary = compare_databases(db1_path, db2_path, db1_path)
        assert len(results) == 1
        assert results[0][1] == 3  # 3 keys
        assert results[0][2] == 2  # 2 same
        assert results[0][3] == 1  # 1 different
        
        # Test 3: Missing in one database
        conn2 = sqlite3.connect(db2_path)
        cursor2 = conn2.cursor()
        cursor2.execute("DELETE FROM test_table1 WHERE id = 2")
        conn2.commit()
        conn2.close()
        
        results, summary = compare_databases(db1_path, db2_path, db1_path)
        assert len(results) == 1
        assert results[0][4] == 1  # missing in a
        
        # Test 4: Table with multi-column primary key
        conn1 = sqlite3.connect(db1_path)
        cursor1 = conn1.cursor()
        cursor1.execute("CREATE TABLE test_table2 (a INTEGER, b INTEGER, value TEXT, PRIMARY KEY (a, b))")
        cursor1.execute("INSERT INTO test_table2 VALUES (1, 1, 'x'), (1, 2, 'y')")
        conn1.commit()
        conn1.close()
        
        conn2 = sqlite3.connect(db2_path)
        cursor2 = conn2.cursor()
        cursor2.execute("CREATE TABLE test_table2 (a INTEGER, b INTEGER, value TEXT, PRIMARY KEY (a, b))")
        cursor2.execute("INSERT INTO test_table2 VALUES (1, 1, 'x'), (1, 2, 'z')")
        conn2.commit()
        conn2.close()
        
        results, summary = compare_databases(db1_path, db2_path, db1_path, tables=["test_table2"])
        assert len(results) == 1
        assert results[0][3] == 1  # 1 different
        
        # Test 5: Table without primary key should be skipped
        conn1 = sqlite3.connect(db1_path)
        cursor1 = conn1.cursor()
        cursor1.execute("CREATE TABLE no_pk_table (id INTEGER, value TEXT)")
        cursor1.execute("INSERT INTO no_pk_table VALUES (1, 'x'), (2, 'y')")
        conn1.commit()
        conn1.close()
        
        results, summary = compare_databases(db1_path, db2_path, db1_path, tables=["no_pk_table"])
        assert len(results) == 0  # Should be skipped
        assert len(summary["skipped_tables"]) == 1
        
        print("selftest passed")
        
    finally:
        os.unlink(db1_path)
        os.unlink(db2_path)
        os.unlink(db3_path)


def main():
    parser = argparse.ArgumentParser(description="Compare SQLite databases row by row")
    parser.add_argument("--ref", help="Reference database path")
    parser.add_argument("--a", help="First database to compare")
    parser.add_argument("--b", help="Second database to compare")
    parser.add_argument("--tables", help="Comma-separated list of tables to compare")
    parser.add_argument("--selftest", action="store_true", help="Run self-test")
    
    args = parser.parse_args()
    
    if args.selftest:
        selftest()
        sys.exit(0)
    
    # Check required arguments for non-selftest mode
    if not args.a or not args.b:
        parser.error("--a and --b are required")
    
    # Set reference to --a if not specified
    if not args.ref:
        args.ref = args.a
    
    # Validate file existence
    for path in [args.ref, args.a, args.b]:
        if not os.path.exists(path):
            print(f"Database file does not exist: {path}", file=sys.stderr)
            sys.exit(2)
    
    tables = None
    if args.tables:
        tables = set(args.tables.split(','))
    
    results, summary = compare_databases(args.ref, args.a, args.b, tables)
    
    # Print per-table results
    for table, total_keys, same, diff, missing_a, missing_b, different_keys in results:
        print(f"{table}: {total_keys} keys, {same} same, {diff} different, {missing_a} missing in a, {missing_b} missing in b")
        if different_keys:
            for key_tuple in different_keys[:5]:  # Show first 5 differences
                print(f"  diff {key_tuple}")
    
    # Print summary
    if summary["tables_compared"] > 0:
        print(f"{summary['tables_compared']} tables compared, {summary['tables_with_diffs']} with differences", end="")
        if summary["skipped_tables"]:
            print(f", skipped: {', '.join(summary['skipped_tables'])}")
        else:
            print()
    else:
        print("No tables to compare")
    
    # Exit codes
    # 0: no differences or missing keys
    # 1: differences or missing keys
    # 2: file errors
    
    # Check if any table has differences or missing keys
    has_differences = any(diff > 0 or missing_a > 0 or missing_b > 0 for _, _, _, diff, missing_a, missing_b, _ in results)
    
    sys.exit(0 if not has_differences else 1)


if __name__ == "__main__":
    main()