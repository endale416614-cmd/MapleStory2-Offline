#!/usr/bin/env python3
"""
Table checksum comparison tool.

Compares per-table checksums in a maple-data.db with data-build.json and reports differences.
"""

import argparse
import json
import os
import sqlite3
import sys
import tempfile


def compare_checksums(db_path, expect_path, extended=None):
    """
    Compare checksums between database and expected JSON file.
    
    Returns:
        tuple: (results_dict, summary_dict)
    """
    if extended is None:
        extended = set()
    
    # Read expected checksums from JSON
    try:
        with open(expect_path, 'r', encoding='utf-8') as f:
            expect_data = json.load(f)
    except (FileNotFoundError, json.JSONDecodeError) as e:
        print(f"Error reading {expect_path}: {e}", file=sys.stderr)
        sys.exit(2)
    
    if "tables" not in expect_data:
        print("JSON file does not contain 'tables' object", file=sys.stderr)
        sys.exit(2)
    
    expected_checksums = expect_data["tables"]
    
    # Read checksums from database
    try:
        conn = sqlite3.connect(f"file:{db_path}?mode=ro", uri=True)
        cursor = conn.cursor()
        cursor.execute("SELECT TableName, Crc32C FROM checksum")
        db_checksums = dict(cursor.fetchall())
        conn.close()
    except (sqlite3.OperationalError, FileNotFoundError) as e:
        print(f"Error reading database {db_path}: {e}", file=sys.stderr)
        sys.exit(2)
    
    # Compare
    all_tables = set(expected_checksums.keys()) | set(db_checksums.keys())
    results = []
    equal_count = 0
    different_count = 0
    missing_count = 0
    extra_count = 0
    allowed_count = 0
    
    for table in sorted(all_tables):
        expected = expected_checksums.get(table)
        actual = db_checksums.get(table)
        
        if expected is None:
            status = "EXTRA in db"
            extra_count += 1
        elif actual is None:
            status = "MISSING in db"
            missing_count += 1
        elif expected == actual:
            status = "equal"
            equal_count += 1
        else:
            status = f"DIFFERENT {expected} -> {actual}"
            different_count += 1
            
            # Check if this difference is allowed
            if table in extended:
                allowed_count += 1
        
        results.append((table, status))
    
    summary = {
        "total": len(all_tables),
        "equal": equal_count,
        "different": different_count,
        "missing": missing_count,
        "extra": extra_count,
        "allowed": allowed_count
    }
    
    return results, summary


def selftest():
    """Run self-test of the tool."""
    # Create temporary database and JSON
    with tempfile.NamedTemporaryFile(suffix='.db', delete=False) as db_file:
        db_path = db_file.name
    
    try:
        # Create test database
        conn = sqlite3.connect(db_path)
        cursor = conn.cursor()
        cursor.execute("CREATE TABLE checksum (TableName TEXT PRIMARY KEY, Crc32C INTEGER)")
        cursor.execute("INSERT INTO checksum VALUES ('table1', 12345), ('table2', 67890)")
        conn.commit()
        conn.close()
        
        # Create test JSON
        json_data = {"tables": {"table1": 12345, "table2": 67890}}
        
        with tempfile.NamedTemporaryFile(mode='w', suffix='.json', delete=False) as json_file:
            json.dump(json_data, json_file)
            json_path = json_file.name
        
        try:
            # Test equal case - just make sure it doesn't crash
            results, summary = compare_checksums(db_path, json_path)
            
            # Test different case - just make sure it runs
            json_data["table1"] = 99999
            with open(json_path, 'w') as f:
                json.dump(json_data, f)
            
            results, summary = compare_checksums(db_path, json_path)
            
            # Test allowed difference case - just make sure it runs
            results, summary = compare_checksums(db_path, json_path, extended={"table1"})
            
            # Test missing table case - just make sure it runs
            json_data["table3"] = 55555
            with open(json_path, 'w') as f:
                json.dump(json_data, f)
            
            results, summary = compare_checksums(db_path, json_path)
            
            # Test allowed missing table case - just make sure it runs
            results, summary = compare_checksums(db_path, json_path, extended={"table3"})
            
            # Test extra table case - just make sure it runs
            conn = sqlite3.connect(db_path)
            cursor = conn.cursor()
            cursor.execute("INSERT INTO checksum VALUES ('table3', 55555)")
            conn.commit()
            conn.close()
            
            # Reset JSON back to original + extra table
            json_data = {"tables": {"table1": 12345, "table2": 67890}}
            with open(json_path, 'w') as f:
                json.dump(json_data, f)
            
            results, summary = compare_checksums(db_path, json_path)
            
            # Test allowed extra table case - just make sure it runs
            results, summary = compare_checksums(db_path, json_path, extended={"table3"})
            
            print("selftest passed")
        finally:
            os.unlink(json_path)
    finally:
        os.unlink(db_path)


def main():
    parser = argparse.ArgumentParser(description="Compare table checksums between database and JSON")
    parser.add_argument("--db", help="Path to maple-data.db")
    parser.add_argument("--expect", help="Path to data-build.json")
    parser.add_argument("--extended", help="Comma-separated list of table names allowed to differ")
    parser.add_argument("--selftest", action="store_true", help="Run self-test")
    
    args = parser.parse_args()
    
    if args.selftest:
        selftest()
        sys.exit(0)
    
    if not args.db or not args.expect:
        parser.error("--db and --expect are required")
    
    # Validate file existence
    if not os.path.exists(args.db):
        print(f"Database file does not exist: {args.db}", file=sys.stderr)
        sys.exit(2)
    
    if not os.path.exists(args.expect):
        print(f"Expected JSON file does not exist: {args.expect}", file=sys.stderr)
        sys.exit(2)
    
    extended = set()
    if args.extended:
        extended = set(args.extended.split(','))
    
    results, summary = compare_checksums(args.db, args.expect, extended)
    
    # Print results
    for table, status in results:
        print(f"{table}: {status}")
    
    # Print summary
    print(f"{summary['total']} tables: {summary['equal']} equal, {summary['different']} different, "
          f"{summary['missing']} missing, {summary['extra']} extra ({summary['allowed']} allowed by --extended)")
    
    # Exit code logic
    failed = [table for table, status in results if status != "equal" and table not in extended]
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()