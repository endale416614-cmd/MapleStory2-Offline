#!/usr/bin/env python3
"""
Content pack builder tool.

Turns pairs (Global archive, built archive) into a content pack folder
(pack.json, <stem>.tail, <stem>.m2h) exactly in the format Launcher/ContentPack.cs reads,
refusing any pair whose Global prefix is not byte-identical.
"""

import argparse
import hashlib
import os
import shutil
import sys
import tempfile
import sqlite3


def sha256_file(path, chunk_size=16*1024*1024):
    """Calculate SHA256 hash of a file in chunks to avoid loading whole file into memory."""
    sha256 = hashlib.sha256()
    with open(path, 'rb') as f:
        while True:
            data = f.read(chunk_size)
            if not data:
                break
            sha256.update(data)
    return sha256.hexdigest()


def build_content_pack(global_root, built_root, version, out_dir, archives, data_db=None):
    """Build content pack from global and built archives."""
    # Check that the data DB file exists (before doing anything else)
    if data_db is not None and not os.path.isfile(data_db):
        print(f"Error: Data DB file does not exist: {data_db}", file=sys.stderr)
        sys.exit(2)

    # Check that output directory doesn't already contain files
    if os.path.exists(out_dir):
        if any(os.path.isfile(os.path.join(out_dir, f)) for f in os.listdir(out_dir)):
            print(f"Error: Output directory {out_dir} already contains files", file=sys.stderr)
            sys.exit(1)
    else:
        os.makedirs(out_dir, exist_ok=True)

    # Validate all archives first
    archive_info = []
    stems = set()
    
    for rel_path in archives:
        # Construct full paths
        global_m2d = os.path.join(global_root, rel_path)
        built_m2d = os.path.join(built_root, rel_path)
        
        # Check if both files exist
        if not os.path.exists(global_m2d):
            print(f"Error: Global file does not exist: {global_m2d}", file=sys.stderr)
            sys.exit(2)
        if not os.path.exists(built_m2d):
            print(f"Error: Built file does not exist: {built_m2d}", file=sys.stderr)
            sys.exit(2)
        
        # Get stem (filename without extension)
        stem = os.path.splitext(os.path.basename(rel_path))[0]
        if stem in stems:
            print(f"Error: Duplicate stem '{stem}' found", file=sys.stderr)
            sys.exit(1)
        stems.add(stem)
        
        # Get file sizes
        global_size = os.path.getsize(global_m2d)
        built_size = os.path.getsize(built_m2d)
        
        # Check that built archive is larger than global
        if built_size <= global_size:
            print(f"refused: {rel_path}: built archive not larger", file=sys.stderr)
            sys.exit(1)
        
        # Check byte-identical prefix
        with open(global_m2d, 'rb') as f_global:
            with open(built_m2d, 'rb') as f_built:
                global_bytes = f_global.read(global_size)
                built_prefix = f_built.read(global_size)
                
                if global_bytes != built_prefix:
                    print(f"refused: {rel_path}: Global prefix not byte-identical", file=sys.stderr)
                    sys.exit(1)
        
        # Calculate sizes and hashes
        tail_size = built_size - global_size
        
        # Get the tail bytes for SHA256 calculation
        with open(built_m2d, 'rb') as f_built:
            f_built.seek(global_size)
            tail_bytes = f_built.read()
            tail_sha256 = hashlib.sha256(tail_bytes).hexdigest()
        
        # Get the m2h file path
        m2h_path = os.path.splitext(rel_path)[0] + '.m2h'
        built_m2h = os.path.join(built_root, m2h_path)
        global_m2h = os.path.join(global_root, m2h_path)
        
        # Check if both .m2h files exist
        if not os.path.exists(built_m2h):
            print(f"Error: Built .m2h file does not exist: {built_m2h}", file=sys.stderr)
            sys.exit(2)
        if not os.path.exists(global_m2h):
            print(f"Error: Global .m2h file does not exist: {global_m2h}", file=sys.stderr)
            sys.exit(2)
        
        # Calculate hashes
        global_m2h_sha256 = sha256_file(global_m2h)
        built_m2h_sha256 = sha256_file(built_m2h)
        
        archive_info.append({
            'path': rel_path.replace('\\', '/'),
            'globalSize': global_size,
            'globalSha256': sha256_file(global_m2d),
            'tailSize': tail_size,
            'tailSha256': tail_sha256,
            'm2hSha256': built_m2h_sha256,
            'origM2hSha256': global_m2h_sha256
        })
        
        # Write tail file
        tail_file_path = os.path.join(out_dir, f"{stem}.tail")
        with open(built_m2d, 'rb') as f_built:
            f_built.seek(global_size)
            with open(tail_file_path, 'wb') as f_tail:
                shutil.copyfileobj(f_built, f_tail)
        
        # Write m2h file
        m2h_file_path = os.path.join(out_dir, f"{stem}.m2h")
        shutil.copy2(built_m2h, m2h_file_path)
        
        print(f"{rel_path}: tail {tail_size} bytes")
    
    # Write pack.json
    pack_json = {
        'version': version,
        'archives': archive_info
    }
    
    pack_json_path = os.path.join(out_dir, 'pack.json')
    with open(pack_json_path, 'w', encoding='utf-8') as f:
        import json
        json.dump(pack_json, f, indent=1, ensure_ascii=False)
        f.write('\n')
    
    # Print final summary
    total_bytes = sum(info['tailSize'] for info in archive_info)
    print(f"pack {version}: {len(archives)} archives, {total_bytes} bytes")
    
    # Handle data-db option if provided
    if data_db:
        # Create GameData directory and copy the data files
        game_data_dir = os.path.join(out_dir, 'GameData')
        os.makedirs(game_data_dir, exist_ok=True)
        
        # Copy the database using VACUUM INTO to create a compact copy
        data_db_dest = os.path.join(game_data_dir, 'maple-data.db')
        source = sqlite3.connect(f"file:{data_db}?mode=ro", uri=True)
        source.execute("VACUUM INTO ?", (data_db_dest,))
        source.close()
        
        # Read checksum table from the written copy and create data-build.json
        dest = sqlite3.connect(data_db_dest)
        cursor = dest.cursor()
        cursor.execute("SELECT TableName, Crc32C FROM checksum ORDER BY TableName")
        tables = {}
        for row in cursor.fetchall():
            tables[row[0]] = row[1]
        dest.close()
        
        # Write data-build.json
        data_build_path = os.path.join(game_data_dir, 'data-build.json')
        with open(data_build_path, 'w', encoding='utf-8') as f:
            import json
            json.dump({
                "contentPack": version,
                "tables": tables
            }, f, indent=1, sort_keys=True)
            f.write('\n')
        
        # Print summary of data DB copy
        table_count = len(tables)
        file_size = os.path.getsize(data_db_dest)
        print(f"GameData: {table_count} tables, {file_size} bytes")


def selftest():
    """Run self-test of the tool."""
    # Create temporary directories
    with tempfile.TemporaryDirectory() as temp_dir:
        global_root = os.path.join(temp_dir, 'global')
        built_root = os.path.join(temp_dir, 'built')
        out_dir = os.path.join(temp_dir, 'out')
        
        # Create test files
        os.makedirs(global_root, exist_ok=True)
        os.makedirs(built_root, exist_ok=True)
        
        # Create a fake global file
        global_m2d = os.path.join(global_root, 'Data', 'Resource', 'Image.m2d')
        built_m2d = os.path.join(built_root, 'Data', 'Resource', 'Image.m2d')
        global_m2h = os.path.join(global_root, 'Data', 'Resource', 'Image.m2h')
        built_m2h = os.path.join(built_root, 'Data', 'Resource', 'Image.m2h')
        
        # Create directories
        os.makedirs(os.path.dirname(global_m2d), exist_ok=True)
        os.makedirs(os.path.dirname(built_m2d), exist_ok=True)
        os.makedirs(os.path.dirname(global_m2h), exist_ok=True)
        os.makedirs(os.path.dirname(built_m2h), exist_ok=True)
        
        # Create global file with known content
        global_content = b'A' * 1000  # 1000 bytes of 'A'
        with open(global_m2d, 'wb') as f:
            f.write(global_content)
        
        # Create built file with global prefix + extra content
        built_content = global_content + b'B' * 500  # 500 bytes of 'B' appended
        with open(built_m2d, 'wb') as f:
            f.write(built_content)
            
        # Create .m2h files
        with open(global_m2h, 'wb') as f:
            f.write(b'Global M2H content')
        with open(built_m2h, 'wb') as f:
            f.write(b'Built M2H content')
        
        # Run the tool with self-test
        build_content_pack(global_root, built_root, 'test', out_dir, ['Data/Resource/Image.m2d'])
        
        # Verify output files exist and have correct content
        tail_path = os.path.join(out_dir, 'Image.tail')
        m2h_path = os.path.join(out_dir, 'Image.m2h')
        pack_json_path = os.path.join(out_dir, 'pack.json')
        
        assert os.path.exists(tail_path), "Tail file should exist"
        assert os.path.exists(m2h_path), "M2H file should exist"
        assert os.path.exists(pack_json_path), "Pack JSON should exist"
        
        # Verify tail content
        with open(tail_path, 'rb') as f:
            tail_content = f.read()
        assert tail_content == b'B' * 500, "Tail content should match appended bytes"
        
        # Verify pack.json content
        with open(pack_json_path, 'r', encoding='utf-8') as f:
            import json
            pack_data = json.load(f)
        
        assert pack_data['version'] == 'test'
        assert len(pack_data['archives']) == 1
        archive = pack_data['archives'][0]
        assert archive['path'] == 'Data/Resource/Image.m2d'
        assert archive['globalSize'] == 1000
        assert archive['tailSize'] == 500
        
        # Test that a modified prefix is refused
        # Modify built file to have different content in the prefix
        modified_built_content = b'B' * 1000  # Different content
        with open(built_m2d, 'wb') as f:
            f.write(modified_built_content)
        
        try:
            build_content_pack(global_root, built_root, 'test', out_dir + '_2', ['Data/Resource/Image.m2d'])
            assert False, "Should have refused the modified prefix"
        except SystemExit as e:
            assert e.code == 1, "Should exit with code 1 for refusal"
        
        # Restore the built file to its original content for later tests
        with open(built_m2d, 'wb') as f:
            f.write(built_content)
        
        # Test duplicate stems: two archives with the same file name in different folders
        for root_dir, content in ((global_root, global_content), (built_root, built_content)):
            os.makedirs(os.path.join(root_dir, 'Data'), exist_ok=True)
            with open(os.path.join(root_dir, 'Data', 'Image.m2d'), 'wb') as f:
                f.write(content)

        try:
            build_content_pack(global_root, built_root, 'test', out_dir + '_3',
                                ['Data/Resource/Image.m2d', 'Data/Image.m2d'])
            assert False, "Should have refused duplicate stem"
        except SystemExit as e:
            assert e.code == 1, "Should exit with code 1 for duplicate stems"
        
        # Test --data-db functionality
        # Create a temporary SQLite database with checksum table
        data_db_path = os.path.join(temp_dir, 'test-data.db')
        conn = sqlite3.connect(data_db_path)
        cursor = conn.cursor()
        cursor.execute('''CREATE TABLE checksum (
            TableName TEXT,
            Crc32C INTEGER
        )''')
        cursor.execute("INSERT INTO checksum VALUES ('Table1', 123456)")
        cursor.execute("INSERT INTO checksum VALUES ('Table2', 789012)")
        # Add an extra table as required
        cursor.execute('''CREATE TABLE item (
            id INTEGER
        )''')
        cursor.execute("INSERT INTO item VALUES (1)")
        conn.commit()
        conn.close()
        
        # Test with --data-db option (should succeed)
        build_content_pack(global_root, built_root, 'test-data', out_dir + '_4', ['Data/Resource/Image.m2d'], data_db_path)
        
        # Verify GameData directory was created
        game_data_dir = os.path.join(out_dir + '_4', 'GameData')
        assert os.path.exists(game_data_dir), "GameData directory should exist"
        
        # Verify files were copied correctly
        data_build_path = os.path.join(game_data_dir, 'data-build.json')
        data_db_copy_path = os.path.join(game_data_dir, 'maple-data.db')
        assert os.path.exists(data_build_path), "data-build.json should exist"
        assert os.path.exists(data_db_copy_path), "maple-data.db should exist"
        
        # Verify content of data-build.json
        with open(data_build_path, 'r') as f:
            import json
            data_build = json.load(f)
            assert data_build['contentPack'] == 'test-data'
            assert len(data_build['tables']) == 2
            assert data_build['tables']['Table1'] == 123456
            assert data_build['tables']['Table2'] == 789012
        
        # Verify the copied database has correct content
        conn = sqlite3.connect(data_db_copy_path)
        cursor = conn.cursor()
        cursor.execute("SELECT TableName, Crc32C FROM checksum ORDER BY TableName")
        rows = cursor.fetchall()
        assert len(rows) == 2
        assert rows[0] == ('Table1', 123456)
        assert rows[1] == ('Table2', 789012)
        cursor.execute("SELECT COUNT(*) FROM item")
        count = cursor.fetchone()[0]
        assert count == 1
        conn.close()
        
        # Test missing --data-db file (should exit with code 2)
        try:
            build_content_pack(global_root, built_root, 'test-missing', out_dir + '_5', ['Data/Resource/Image.m2d'], 'nonexistent.db')
            assert False, "Should have exited with code 2 for missing data DB"
        except SystemExit as e:
            assert e.code == 2, "Should exit with code 2 for missing data DB file"
        
        # Make sure no files were written when data DB is missing
        assert not os.path.exists(out_dir + '_5'), "nothing may be written when the data DB is missing"
        
        print("selftest passed")


def main():
    parser = argparse.ArgumentParser(description="Build content pack from global and built archives")
    parser.add_argument("--global-root", help="Path to global client root directory")
    parser.add_argument("--built-root", help="Path to built client root directory")
    parser.add_argument("--version", help="Version string for the content pack")
    parser.add_argument("--out", help="Output directory for the content pack")
    parser.add_argument("--archive", action="append", help="Archive path (repeatable)")
    parser.add_argument("--data-db", help="Path to data database to include in GameData/")
    parser.add_argument("--selftest", action="store_true", help="Run self-test")
    
    args = parser.parse_args()
    
    if args.selftest:
        selftest()
        sys.exit(0)
    
    # Check required arguments after selftest branch
    if not args.global_root or not args.built_root or not args.version or not args.out or not args.archive:
        parser.error("--global-root, --built-root, --version, --out and --archive are required")
    
    build_content_pack(args.global_root, args.built_root, args.version, args.out, args.archive, args.data_db)


if __name__ == "__main__":
    main()