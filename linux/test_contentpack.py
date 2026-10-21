import os
import sys
import tempfile
import unittest
from pathlib import Path

# Add the current directory to sys.path so we can import ms2offline
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

# Mock fcntl for Windows compatibility
import types
sys.modules.setdefault("fcntl", types.ModuleType("fcntl"))

# Import the module
import ms2offline

class TestContentPack(unittest.TestCase):
    def setUp(self):
        self.root = tempfile.mkdtemp(prefix="ms2-contentpack-")
        self.client = os.path.join(self.root, "client")
        self.pack_dir = os.path.join(self.root, "pack")
        os.makedirs(os.path.join(self.client, "Data"))
        os.makedirs(os.path.join(self.client, "GameData"))
        os.makedirs(self.pack_dir)

        # Create test data using a simple approach to avoid random sampling issues
        import random
        random.seed(1)
        self.global_data = bytes([random.randint(0, 255) for _ in range(4096)])
        self.tail = bytes([random.randint(0, 255) for _ in range(1500)])
        self.orig_m2h = bytes([random.randint(0, 255) for _ in range(300)])
        self.new_m2h = bytes([random.randint(0, 255) for _ in range(400)])

        # Write files
        with open(os.path.join(self.client, "Data", "Xml.m2d"), "wb") as f:
            f.write(self.global_data)
        with open(os.path.join(self.client, "Data", "Xml.m2h"), "wb") as f:
            f.write(self.orig_m2h)
        with open(os.path.join(self.pack_dir, "Xml.tail"), "wb") as f:
            f.write(self.tail)
        with open(os.path.join(self.pack_dir, "Xml.m2h"), "wb") as f:
            f.write(self.new_m2h)

        # Write pack.json
        self._write_pack_json()

    def tearDown(self):
        import shutil
        shutil.rmtree(self.root)

    def _sha(self, data):
        """Calculate SHA256 hash."""
        import hashlib
        return hashlib.sha256(data).hexdigest().lower()

    def _write_pack_json(self):
        """Write pack.json with test data."""
        import json
        content = {
            "version": "1.2.0",
            "archives": [{
                "path": "Data/Xml.m2d",
                "globalSize": len(self.global_data),
                "globalSha256": self._sha(self.global_data),
                "tailSize": len(self.tail),
                "tailSha256": self._sha(self.tail),
                "m2hSha256": self._sha(self.new_m2h),
                "origM2hSha256": self._sha(self.orig_m2h)
            }]
        }
        with open(os.path.join(self.pack_dir, ms2offline.ContentPack.FILE_NAME), "w") as f:
            json.dump(content, f)

    def _state(self):
        """Get state of the test archive."""
        pack = ms2offline.ContentPack.load(self.pack_dir)
        return pack.state(self.client)[0]

    def test_fresh_apply_appends_tail_and_replaces_header(self):
        """Test that fresh apply appends tail and replaces header."""
        # Initial state should be NotApplied
        state = self._state()
        self.assertEqual(state.status, ms2offline.ContentPack.Status.NotApplied)

        # Apply the pack
        result = ms2offline.ContentPack.apply(self.client, self.pack_dir)
        self.assertTrue(result)

        # Verify file contents
        with open(os.path.join(self.client, "Data", "Xml.m2d"), "rb") as f:
            content = f.read()
        expected_content = self.global_data + self.tail
        self.assertEqual(content, expected_content)

        # Verify header was replaced
        with open(os.path.join(self.client, "Data", "Xml.m2h"), "rb") as f:
            header = f.read()
        self.assertEqual(header, self.new_m2h)

        # Verify original header was saved
        with open(os.path.join(self.client, "Data", "Xml.orig.m2h"), "rb") as f:
            orig_header = f.read()
        self.assertEqual(orig_header, self.orig_m2h)

        # Verify state is now Applied
        state = self._state()
        self.assertEqual(state.status, ms2offline.ContentPack.Status.Applied)

        # Check journal
        journal_path = os.path.join(self.pack_dir, "apply-journal.json")
        self.assertTrue(os.path.exists(journal_path))

    def test_reapply_is_noop(self):
        """Test that reapply is a no-op."""
        # Apply once
        ms2offline.ContentPack.apply(self.client, self.pack_dir)

        # Get timestamps before reapply
        archive_path = os.path.join(self.client, "Data", "Xml.m2d")
        header_path = os.path.join(self.client, "Data", "Xml.m2h")
        archive_mtime = os.path.getmtime(archive_path)
        header_mtime = os.path.getmtime(header_path)

        # Reapply
        result = ms2offline.ContentPack.apply(self.client, self.pack_dir)
        self.assertTrue(result)

        # Verify no changes occurred
        self.assertEqual(os.path.getmtime(archive_path), archive_mtime)
        self.assertEqual(os.path.getmtime(header_path), header_mtime)

        # Verify content is still correct
        with open(archive_path, "rb") as f:
            content = f.read()
        expected_content = self.global_data + self.tail
        self.assertEqual(content, expected_content)

    def test_half_written_tail_is_refused_and_reported(self):
        """Test that half-written tail is refused."""
        # Write partial tail to archive
        partial_tail = self.tail[:700]
        with open(os.path.join(self.client, "Data", "Xml.m2d"), "wb") as f:
            f.write(self.global_data + partial_tail)

        # State should be Foreign
        state = self._state()
        self.assertEqual(state.status, ms2offline.ContentPack.Status.Foreign)
        self.assertIn("unexpected size", state.reason)

        # Apply should fail with RuntimeError
        with self.assertRaises(RuntimeError) as context:
            ms2offline.ContentPack.apply(self.client, self.pack_dir)

        self.assertIn("Nothing was changed", str(context.exception))

        # Verify file is unchanged
        with open(os.path.join(self.client, "Data", "Xml.m2d"), "rb") as f:
            content = f.read()
        self.assertEqual(content, self.global_data + partial_tail)

        # Verify header is unchanged
        with open(os.path.join(self.client, "Data", "Xml.m2h"), "rb") as f:
            header = f.read()
        self.assertEqual(header, self.orig_m2h)

    def test_wrong_global_hash_is_refused(self):
        """Test that wrong global hash is refused."""
        # Modify pack.json to have wrong global hash
        import json
        with open(os.path.join(self.pack_dir, ms2offline.ContentPack.FILE_NAME), "r") as f:
            data = json.load(f)

        # Change globalSha256 to wrong value
        data["archives"][0]["globalSha256"] = self._sha(self.tail)  # Wrong hash

        with open(os.path.join(self.pack_dir, ms2offline.ContentPack.FILE_NAME), "w") as f:
            json.dump(data, f)

        # State should be Foreign
        state = self._state()
        self.assertEqual(state.status, ms2offline.ContentPack.Status.Foreign)

        # Apply should fail with RuntimeError
        with self.assertRaises(RuntimeError):
            ms2offline.ContentPack.apply(self.client, self.pack_dir)

        # Verify file is unchanged
        with open(os.path.join(self.client, "Data", "Xml.m2d"), "rb") as f:
            content = f.read()
        self.assertEqual(content, self.global_data)

        # Verify header is unchanged
        with open(os.path.join(self.client, "Data", "Xml.m2h"), "rb") as f:
            header = f.read()
        self.assertEqual(header, self.orig_m2h)

    def test_tampered_tail_is_refused(self):
        """Test that tampered tail is refused."""
        # Modify the tail file to be tampered
        tampered_tail = bytearray(self.tail)
        tampered_tail[10] ^= 0xFF  # Flip a bit

        with open(os.path.join(self.pack_dir, "Xml.tail"), "wb") as f:
            f.write(tampered_tail)

        # Apply should fail with RuntimeError
        with self.assertRaises(RuntimeError) as context:
            ms2offline.ContentPack.apply(self.client, self.pack_dir)

        self.assertIn("Xml.tail", str(context.exception))

        # Verify file is unchanged
        with open(os.path.join(self.client, "Data", "Xml.m2d"), "rb") as f:
            content = f.read()
        self.assertEqual(content, self.global_data)

        # Verify header is unchanged
        with open(os.path.join(self.client, "Data", "Xml.m2h"), "rb") as f:
            header = f.read()
        self.assertEqual(header, self.orig_m2h)

    def test_restore_returns_byte_identical_files(self):
        """Test that restore returns byte-identical files."""
        # Apply first
        ms2offline.ContentPack.apply(self.client, self.pack_dir)

        # Restore
        result = ms2offline.ContentPack.restore(self.client, self.pack_dir)
        self.assertTrue(result)

        # Verify file contents
        with open(os.path.join(self.client, "Data", "Xml.m2d"), "rb") as f:
            content = f.read()
        self.assertEqual(content, self.global_data)

        # Verify header was restored
        with open(os.path.join(self.client, "Data", "Xml.m2h"), "rb") as f:
            header = f.read()
        self.assertEqual(header, self.orig_m2h)

        # Verify original header file is gone
        orig_path = os.path.join(self.client, "Data", "Xml.orig.m2h")
        self.assertFalse(os.path.exists(orig_path))

        # Verify state is NotApplied
        state = self._state()
        self.assertEqual(state.status, ms2offline.ContentPack.Status.NotApplied)

        # Restore again should be no-op
        result = ms2offline.ContentPack.restore(self.client, self.pack_dir)
        self.assertTrue(result)

    def test_unexpected_size_is_foreign(self):
        """Test that unexpected size is marked as Foreign."""
        # Write file with wrong size
        with open(os.path.join(self.client, "Data", "Xml.m2d"), "wb") as f:
            f.write(self.global_data + b"\x01")  # One extra byte

        # State should be Foreign
        state = self._state()
        self.assertEqual(state.status, ms2offline.ContentPack.Status.Foreign)
        self.assertIn("unexpected size", state.reason)

        # Restore should fail with RuntimeError
        with self.assertRaises(RuntimeError):
            ms2offline.ContentPack.restore(self.client, self.pack_dir)

    def test_pack_json_is_strict(self):
        """Test that pack.json is strict."""
        # Test missing archives array
        with open(os.path.join(self.pack_dir, ms2offline.ContentPack.FILE_NAME), "w") as f:
            f.write('{"version": "1.2.0"}')

        with self.assertRaises(ValueError):
            ms2offline.ContentPack.load(self.pack_dir)

        # Test empty archives array
        with open(os.path.join(self.pack_dir, ms2offline.ContentPack.FILE_NAME), "w") as f:
            f.write('{"version": "1.2.0", "archives": []}')

        with self.assertRaises(ValueError):
            ms2offline.ContentPack.load(self.pack_dir)

        # Test invalid SHA256
        with open(os.path.join(self.pack_dir, ms2offline.ContentPack.FILE_NAME), "w") as f:
            f.write('{"version": "1.2.0", "archives": [{"path": "Data/Xml.m2d", "globalSize": 4096, "globalSha256": "nothex", "tailSize": 1500, "tailSha256": "abcd", "m2hSha256": "abcd", "origM2hSha256": "abcd"}]}')

        with self.assertRaises(ValueError):
            ms2offline.ContentPack.load(self.pack_dir)

    def test_applied_version_returns_null_for_fresh_client(self):
        """Test that applied_version returns null for fresh client."""
        pack = ms2offline.ContentPack.load(self.pack_dir)
        result = pack.applied_version(self.client)
        self.assertIsNone(result)

    def test_applied_version_returns_version_for_applied_pack(self):
        """Test that applied_version returns version for applied pack."""
        # Apply first
        ms2offline.ContentPack.apply(self.client, self.pack_dir)

        pack = ms2offline.ContentPack.load(self.pack_dir)
        result = pack.applied_version(self.client)
        self.assertEqual(result, "1.2.0")

    def test_applied_version_one_byte_change_is_foreign_with_reason(self):
        """Test that one byte change is marked as Foreign."""
        # Modify global data to make it appear foreign
        modified_global = bytearray(self.global_data)
        modified_global[10] ^= 0xFF

        with open(os.path.join(self.client, "Data", "Xml.m2d"), "wb") as f:
            f.write(modified_global)

        pack = ms2offline.ContentPack.load(self.pack_dir)
        with self.assertRaises(RuntimeError) as context:
            pack.applied_version(self.client)

        self.assertIn("Data/Xml.m2d", str(context.exception))
        self.assertIn("unknown client build", str(context.exception))

    def test_read_data_build_pack_handles_missing_file(self):
        """Test that ReadDataBuildPack handles missing file."""
        result = ms2offline.ContentPack.read_data_build_pack(os.path.join(self.client, "GameData", "data-build.json"))
        self.assertIsNone(result)

    def test_read_data_build_pack_handles_no_content_pack_field(self):
        """Test that ReadDataBuildPack handles no contentPack field."""
        with open(os.path.join(self.client, "GameData", "data-build.json"), "w") as f:
            f.write('{"someOtherField": "value"}')

        result = ms2offline.ContentPack.read_data_build_pack(os.path.join(self.client, "GameData", "data-build.json"))
        self.assertIsNone(result)

    def test_read_data_build_pack_handles_valid_content_pack(self):
        """Test that ReadDataBuildPack handles valid contentPack."""
        with open(os.path.join(self.client, "GameData", "data-build.json"), "w") as f:
            f.write('{"contentPack": "test-version-123"}')

        result = ms2offline.ContentPack.read_data_build_pack(os.path.join(self.client, "GameData", "data-build.json"))
        self.assertEqual(result, "test-version-123")

    def test_read_data_build_pack_throws_for_invalid_json(self):
        """Test that ReadDataBuildPack throws for invalid JSON."""
        with open(os.path.join(self.client, "GameData", "data-build.json"), "w") as f:
            f.write("{invalid json}")

        with self.assertRaises(ValueError) as context:
            ms2offline.ContentPack.read_data_build_pack(os.path.join(self.client, "GameData", "data-build.json"))

        self.assertIn("is not valid JSON", str(context.exception))

    def test_read_data_build_pack_handles_null_content_pack(self):
        """Test that ReadDataBuildPack handles null contentPack."""
        with open(os.path.join(self.client, "GameData", "data-build.json"), "w") as f:
            f.write('{"contentPack": null}')

        result = ms2offline.ContentPack.read_data_build_pack(os.path.join(self.client, "GameData", "data-build.json"))
        self.assertIsNone(result)

    def test_read_data_build_pack_handles_empty_content_pack(self):
        """Test that ReadDataBuildPack handles empty contentPack."""
        with open(os.path.join(self.client, "GameData", "data-build.json"), "w") as f:
            f.write('{"contentPack": "   "}')

        with self.assertRaises(ValueError) as context:
            ms2offline.ContentPack.read_data_build_pack(os.path.join(self.client, "GameData", "data-build.json"))

        self.assertIn("contentPack is empty", str(context.exception))

    def test_read_data_build_pack_handles_non_object_root(self):
        """Test that ReadDataBuildPack handles non-object root."""
        with open(os.path.join(self.client, "GameData", "data-build.json"), "w") as f:
            f.write("[]")

        with self.assertRaises(ValueError) as context:
            ms2offline.ContentPack.read_data_build_pack(os.path.join(self.client, "GameData", "data-build.json"))

        self.assertIn("the root is not a JSON object", str(context.exception))

    def test_read_data_build_pack_handles_non_string_content_pack(self):
        """Test that ReadDataBuildPack handles non-string contentPack."""
        with open(os.path.join(self.client, "GameData", "data-build.json"), "w") as f:
            f.write('{"contentPack": 123}')

        with self.assertRaises(ValueError) as context:
            ms2offline.ContentPack.read_data_build_pack(os.path.join(self.client, "GameData", "data-build.json"))

        self.assertIn("contentPack is not a string", str(context.exception))

    def test_data_lock_error_matrix_tests(self):
        """Test DataLockError matrix tests."""
        # (null,null) ok
        result = ms2offline.ContentPack.data_lock_error(None, None)
        self.assertIsNone(result)

        # ("v1","v1") ok
        result = ms2offline.ContentPack.data_lock_error("v1", "v1")
        self.assertIsNone(result)

        # ("v1",null) error
        result = ms2offline.ContentPack.data_lock_error("v1", None)
        self.assertIsNotNone(result)
        self.assertIn("pack v1", result)
        self.assertIn("Global data", result)

        # (null,"v1") error
        result = ms2offline.ContentPack.data_lock_error(None, "v1")
        self.assertIsNotNone(result)
        self.assertIn("server data was built for the unified content", result)
        self.assertIn("v1", result)

        # ("v1","v2") error
        result = ms2offline.ContentPack.data_lock_error("v1", "v2")
        self.assertIsNotNone(result)
        self.assertIn("game has content pack v1", result)
        self.assertIn("server data was built for pack v2", result)

    def test_describe_no_pack_json(self):
        """Test Describe with no pack.json."""
        # Remove the pack directory to simulate no pack.json
        import shutil
        shutil.rmtree(self.pack_dir)
        os.makedirs(self.pack_dir)

        result = ms2offline.ContentPack.describe(self.client, self.pack_dir)
        self.assertTrue(result.startswith("Content pack  : none"))

    def test_describe_fresh_client(self):
        """Test Describe with fresh client."""
        result = ms2offline.ContentPack.describe(self.client, self.pack_dir)
        self.assertIn("Content pack  : 1.2.0", result)
        self.assertIn("Data/Xml.m2d: Global size, Global header", result)
        self.assertIn("Last journal  : none", result)

    def test_describe_after_apply(self):
        """Test Describe after apply."""
        ms2offline.ContentPack.apply(self.client, self.pack_dir)
        result = ms2offline.ContentPack.describe(self.client, self.pack_dir)
        self.assertIn("Content pack  : 1.2.0", result)
        self.assertIn("Data/Xml.m2d: applied size, pack header", result)
        self.assertIn("Last journal  : done", result)

    def test_recover_interrupted_interrupted_append_repaired(self):
        """Test RecoverInterrupted with interrupted append."""
        # Create a half-appended archive
        partial_tail = self.tail[:700]
        with open(os.path.join(self.client, "Data", "Xml.m2d"), "wb") as f:
            f.write(self.global_data + partial_tail)

        # Create journal file with "applying" state to simulate this
        import json
        journal_content = {
            "Version": "1.2.0",
            "State": "applying",
            "Archive": "Data/Xml.m2d",
            "Step": "tail",
            "Utc": "2023-01-01T00:00:00.000000Z"
        }
        with open(os.path.join(self.pack_dir, "apply-journal.json"), "w") as f:
            json.dump(journal_content, f)

        # Call RecoverInterrupted
        result = ms2offline.ContentPack.recover_interrupted(self.client, self.pack_dir)

        # Should return a message about the repair
        self.assertIsNotNone(result)
        self.assertIn("Data/Xml.m2d", result)

        # Archive should be truncated back to global size
        with open(os.path.join(self.client, "Data", "Xml.m2d"), "rb") as f:
            archive_bytes = f.read()
        self.assertEqual(archive_bytes, self.global_data)

        # Journal state should be updated to "recovered"
        with open(os.path.join(self.pack_dir, "apply-journal.json"), "r") as f:
            journal_content_after = json.load(f)
        self.assertIn("recovered", journal_content_after["State"])

    def test_recover_interrupted_no_journal_returns_null(self):
        """Test RecoverInterrupted with no journal returns null."""
        # Create a half-appended archive without a journal file
        partial_tail = self.tail[:700]
        with open(os.path.join(self.client, "Data", "Xml.m2d"), "wb") as f:
            f.write(self.global_data + partial_tail)

        # Call RecoverInterrupted - should return null since no journal exists
        result = ms2offline.ContentPack.recover_interrupted(self.client, self.pack_dir)
        self.assertIsNone(result)

        # Archive should remain unchanged
        with open(os.path.join(self.client, "Data", "Xml.m2d"), "rb") as f:
            archive_bytes = f.read()
        self.assertEqual(archive_bytes, self.global_data + partial_tail)

    def _write_journal(self, version, state):
        import json
        with open(os.path.join(self.pack_dir, "apply-journal.json"), "w") as f:
            json.dump({"Version": version, "State": state, "Archive": "Data/Xml.m2d", "Step": "tail",
                       "Utc": "2023-01-01T00:00:00.000000Z"}, f)

    def _archive_bytes(self):
        with open(os.path.join(self.client, "Data", "Xml.m2d"), "rb") as f:
            return f.read()

    def test_applied_version_mixed_archives_is_partly_applied(self):
        """Archive 1 applied with the one-archive pack from setUp; then the pack grows a second, untouched archive."""
        import json, random
        self.assertTrue(ms2offline.ContentPack.apply(self.client, self.pack_dir))
        random.seed(2)
        other_global = bytes(random.randint(0, 255) for _ in range(2048))
        other_tail = bytes(random.randint(0, 255) for _ in range(1000))
        other_orig_m2h = bytes(random.randint(0, 255) for _ in range(200))
        other_new_m2h = bytes(random.randint(0, 255) for _ in range(300))
        for path, data in ((os.path.join(self.client, "Data", "Other.m2d"), other_global),
                           (os.path.join(self.client, "Data", "Other.m2h"), other_orig_m2h),
                           (os.path.join(self.pack_dir, "Other.tail"), other_tail),
                           (os.path.join(self.pack_dir, "Other.m2h"), other_new_m2h)):
            with open(path, "wb") as f:
                f.write(data)
        content = {"version": "1.2.0", "archives": [
            {"path": "Data/Xml.m2d", "globalSize": len(self.global_data), "globalSha256": self._sha(self.global_data),
             "tailSize": len(self.tail), "tailSha256": self._sha(self.tail), "m2hSha256": self._sha(self.new_m2h),
             "origM2hSha256": self._sha(self.orig_m2h)},
            {"path": "Data/Other.m2d", "globalSize": len(other_global), "globalSha256": self._sha(other_global),
             "tailSize": len(other_tail), "tailSha256": self._sha(other_tail), "m2hSha256": self._sha(other_new_m2h),
             "origM2hSha256": self._sha(other_orig_m2h)}]}
        with open(os.path.join(self.pack_dir, ms2offline.ContentPack.FILE_NAME), "w") as f:
            json.dump(content, f)
        pack = ms2offline.ContentPack.load(self.pack_dir)
        self.assertEqual([s.status for s in pack.state(self.client)],
                         [ms2offline.ContentPack.Status.Applied, ms2offline.ContentPack.Status.NotApplied])
        with self.assertRaises(RuntimeError) as context:
            pack.applied_version(self.client)
        self.assertIn("partly applied", str(context.exception))
        self.assertIn("Data/Other.m2d", str(context.exception))

    def test_describe_half_apply(self):
        """A half-appended archive and an 'applying' journal show in the report."""
        with open(os.path.join(self.client, "Data", "Xml.m2d"), "wb") as f:
            f.write(self.global_data + self.tail[:700])
        self._write_journal("1.2.0-test", "applying")
        result = ms2offline.ContentPack.describe(self.client, self.pack_dir)
        self.assertIn("Content pack  : 1.2.0", result)
        self.assertIn("UNEXPECTED size", result)
        self.assertIn("applying 1.2.0-test Data/Xml.m2d tail", result)

    def test_recover_interrupted_apply_after_recovery_succeeds(self):
        """After the repair, apply succeeds and the archive is Applied."""
        with open(os.path.join(self.client, "Data", "Xml.m2d"), "wb") as f:
            f.write(self.global_data + self.tail[:700])
        self._write_journal("1.2.0", "applying")
        self.assertIsNotNone(ms2offline.ContentPack.recover_interrupted(self.client, self.pack_dir))
        self.assertTrue(ms2offline.ContentPack.apply(self.client, self.pack_dir))
        self.assertEqual(self._state().status, ms2offline.ContentPack.Status.Applied)

    def test_recover_interrupted_journal_done_returns_null(self):
        """A 'done' journal means nothing to repair."""
        with open(os.path.join(self.client, "Data", "Xml.m2d"), "wb") as f:
            f.write(self.global_data + self.tail)
        self._write_journal("1.2.0", "done")
        self.assertIsNone(ms2offline.ContentPack.recover_interrupted(self.client, self.pack_dir))

    def test_recover_interrupted_no_repair_without_correct_prefix_returns_null(self):
        """A changed byte inside the Global prefix: no repair, archive untouched."""
        modified_global = bytearray(self.global_data)
        modified_global[10] ^= 0xFF
        damaged = bytes(modified_global) + self.tail[:700]
        with open(os.path.join(self.client, "Data", "Xml.m2d"), "wb") as f:
            f.write(damaged)
        self._write_journal("1.2.0", "applying")
        self.assertIsNone(ms2offline.ContentPack.recover_interrupted(self.client, self.pack_dir))
        self.assertEqual(self._archive_bytes(), damaged)

    def test_recover_interrupted_power_loss_case_repaired(self):
        """Applied size but zero bytes where the tail should be: truncated back to Global, journal 'recovered'."""
        with open(os.path.join(self.client, "Data", "Xml.m2d"), "wb") as f:
            f.write(self.global_data + bytes(len(self.tail)))
        self._write_journal("1.2.0", "applying")
        result = ms2offline.ContentPack.recover_interrupted(self.client, self.pack_dir)
        self.assertIsNotNone(result)
        self.assertIn("Data/Xml.m2d", result)
        self.assertEqual(self._archive_bytes(), self.global_data)
        with open(os.path.join(self.pack_dir, "apply-journal.json"), "r") as f:
            self.assertIn('"recovered"', f.read())


class TestPrepareContent(unittest.TestCase):
    """prepare_content: one test per C# test of Launcher.Tests/ContentPackFlowTests.cs. Linux has no administrator
    retry, so the C# cases with an elevated callback check the refused apply/restore directly."""

    def setUp(self):
        import json, random
        self.root = tempfile.mkdtemp(prefix="ms2-contentpackflow-")
        self.client = os.path.join(self.root, "client")
        self.pack_dir = os.path.join(self.root, "pack")
        self.global_data_dir = os.path.join(self.root, "GameData")
        self.pack_data_dir = os.path.join(self.pack_dir, "GameData")
        os.makedirs(os.path.join(self.client, "Data"))
        os.makedirs(self.global_data_dir)
        os.makedirs(self.pack_data_dir)
        rng = random.Random(1)
        self.global_data, self.tail = rng.randbytes(4096), rng.randbytes(1500)
        self.orig_m2h, self.new_m2h = rng.randbytes(300), rng.randbytes(400)
        self.archive = os.path.join(self.client, "Data", "Xml.m2d")
        self.m2h = os.path.join(self.client, "Data", "Xml.m2h")
        self.orig_m2h_path = os.path.join(self.client, "Data", "Xml.orig.m2h")
        self._write(self.archive, self.global_data)
        self._write(self.m2h, self.orig_m2h)
        self._write(os.path.join(self.pack_dir, "Xml.tail"), self.tail)
        self._write(os.path.join(self.pack_dir, "Xml.m2h"), self.new_m2h)
        self._write(os.path.join(self.pack_data_dir, "maple-data.db"), b"\0")
        self._write_pack_json([self._entry("Data/Xml.m2d", self.global_data, self.tail, self.new_m2h, self.orig_m2h)])

    def tearDown(self):
        import shutil, stat
        for folder, _, files in os.walk(self.root):
            for name in files:
                os.chmod(os.path.join(folder, name), stat.S_IREAD | stat.S_IWRITE)  # a test may leave a file read-only
        shutil.rmtree(self.root)

    @staticmethod
    def _write(path, data):
        with open(path, "wb") as f:
            f.write(data)

    @staticmethod
    def _read(path):
        with open(path, "rb") as f:
            return f.read()

    @staticmethod
    def _sha(data):
        import hashlib
        return hashlib.sha256(data).hexdigest()

    def _entry(self, path, global_data, tail, new_m2h, orig_m2h):
        return {"path": path, "globalSize": len(global_data), "globalSha256": self._sha(global_data), "tailSize": len(tail),
                "tailSha256": self._sha(tail), "m2hSha256": self._sha(new_m2h), "origM2hSha256": self._sha(orig_m2h)}

    def _write_pack_json(self, archives, version="1.2.0-test"):
        import json
        with open(os.path.join(self.pack_dir, ms2offline.ContentPack.FILE_NAME), "w", encoding="utf-8") as f:
            json.dump({"version": version, "archives": archives}, f)

    def _write_pack_build(self, text):
        with open(os.path.join(self.pack_data_dir, "data-build.json"), "w", encoding="utf-8") as f:
            f.write(text)

    def _prepare(self, unified):
        return ms2offline.prepare_content(unified, self.client, self.pack_dir, self.global_data_dir)

    def _status(self):
        return ms2offline.ContentPack.load(self.pack_dir).state(self.client)[0].status

    @staticmethod
    def _read_only(path):
        import stat
        os.chmod(path, stat.S_IREAD)

    def test_no_pack_folder_global_data_no_error(self):
        import shutil
        shutil.rmtree(self.pack_dir)
        self.assertEqual(self._prepare(True), (self.global_data_dir, None, None, None))

    def test_fresh_client_with_unified_on_applied_data_dir_pack_game_data_no_error(self):
        self._write_pack_build('{"contentPack": "1.2.0-test"}')
        self.assertEqual(self._prepare(True), (self.pack_data_dir, "1.2.0-test", None, None))

    def test_applied_pack_with_missing_server_data_error(self):
        self.assertTrue(ms2offline.ContentPack.apply(self.client, self.pack_dir))
        os.remove(os.path.join(self.pack_data_dir, "maple-data.db"))
        data_dir, applied, warning, error = self._prepare(True)
        self.assertEqual((data_dir, applied, warning), (self.pack_data_dir, "1.2.0-test", None))
        self.assertIn("server data of the unified content is missing", error)

    def test_unified_off_with_open_archive_no_hashing_no_error(self):
        # C#: the archive is locked by another program; here any hashing at all fails the test.
        from unittest import mock
        with mock.patch.object(ms2offline.ContentPack, "_hash", side_effect=AssertionError("the shortcut must not hash")):
            self.assertEqual(self._prepare(False), (self.global_data_dir, None, None, None))

    def test_unified_off_after_apply_restored_byte_identical_global_data(self):
        self.assertEqual(self._prepare(True)[1], "1.2.0-test")
        self.assertEqual(self._prepare(False), (self.global_data_dir, None, None, None))
        self.assertEqual(self._read(self.archive), self.global_data)
        self.assertEqual(self._read(self.m2h), self.orig_m2h)
        self.assertFalse(os.path.exists(self.orig_m2h_path))

    def test_unified_on_but_pack_without_content_pack_field_error(self):
        self._write_pack_build('{"someOtherField": "value"}')
        data_dir, applied, warning, error = self._prepare(True)
        self.assertEqual((data_dir, applied, warning), (self.pack_data_dir, "1.2.0-test", None))
        self.assertIn("Global data", error)

    def test_foreign_client_warning_global_data_no_error_archive_unchanged(self):
        modified = bytearray(self.global_data)
        modified[10] ^= 0xFF
        self._write(self.archive, bytes(modified))
        data_dir, applied, warning, error = self._prepare(True)
        self.assertEqual((data_dir, applied, error), (self.global_data_dir, None, None))
        self.assertIn("does not fit", warning)
        self.assertEqual(self._read(self.archive), bytes(modified))

    def test_apply_refused_by_read_only_archive_warning_global_data(self):
        self._read_only(self.archive)
        data_dir, applied, warning, error = self._prepare(True)
        self.assertEqual((data_dir, applied, error), (self.global_data_dir, None, None))
        self.assertIn("could not be written", warning)
        self.assertEqual(self._read(self.archive), self.global_data)
        self.assertEqual(self._read(self.m2h), self.orig_m2h)

    def test_mixed_states_error(self):
        import random
        self.assertTrue(ms2offline.ContentPack.apply(self.client, self.pack_dir))
        rng = random.Random(2)
        other_global, other_tail, other_orig_m2h, other_new_m2h = rng.randbytes(2048), rng.randbytes(1000), rng.randbytes(200), rng.randbytes(300)
        other_archive = os.path.join(self.client, "Data", "Other.m2d")
        self._write(other_archive, other_global)
        self._write(os.path.join(self.client, "Data", "Other.m2h"), other_orig_m2h)
        self._write(os.path.join(self.pack_dir, "Other.tail"), other_tail)
        self._write(os.path.join(self.pack_dir, "Other.m2h"), other_new_m2h)
        self._write_pack_json([self._entry("Data/Xml.m2d", self.global_data, self.tail, self.new_m2h, self.orig_m2h),
                               self._entry("Data/Other.m2d", other_global, other_tail, other_new_m2h, other_orig_m2h)], version="1.2.0")
        self._read_only(other_archive)
        data_dir, applied, warning, error = self._prepare(True)
        self.assertEqual((data_dir, applied), (self.global_data_dir, None))
        self.assertIn("could not be written", warning)
        self.assertIn("partly applied", error)

    def test_apply_on_partly_applied_client_adds_only_the_missing_tail(self):
        # Regression (2026-10-20): apply appended the tail to every archive when any one lacked it, so an applied archive
        # got a second tail, failed its check and was cut back to the Global size under the pack header.
        import random
        self.assertTrue(ms2offline.ContentPack.apply(self.client, self.pack_dir))
        rng = random.Random(3)
        other_global, other_tail, other_orig_m2h, other_new_m2h = rng.randbytes(2048), rng.randbytes(1000), rng.randbytes(200), rng.randbytes(300)
        other_archive = os.path.join(self.client, "Data", "Other.m2d")
        self._write(other_archive, other_global)
        self._write(os.path.join(self.client, "Data", "Other.m2h"), other_orig_m2h)
        self._write(os.path.join(self.pack_dir, "Other.tail"), other_tail)
        self._write(os.path.join(self.pack_dir, "Other.m2h"), other_new_m2h)
        self._write_pack_json([self._entry("Data/Xml.m2d", self.global_data, self.tail, self.new_m2h, self.orig_m2h),
                               self._entry("Data/Other.m2d", other_global, other_tail, other_new_m2h, other_orig_m2h)])
        self.assertTrue(ms2offline.ContentPack.apply(self.client, self.pack_dir))
        self.assertEqual(self._read(self.archive), self.global_data + self.tail)
        self.assertEqual(self._read(other_archive), other_global + other_tail)
        self.assertEqual(self._read(self.m2h), self.new_m2h)

    def test_data_lock_error_when_applied_pack_does_not_match_server_data(self):
        self.assertTrue(ms2offline.ContentPack.apply(self.client, self.pack_dir))
        self._write_pack_build('{"contentPack": "other"}')
        data_dir, applied, warning, error = self._prepare(True)
        self.assertEqual((data_dir, applied, warning), (self.pack_data_dir, "1.2.0-test", None))
        self.assertIn("pack other", error)

    def test_unified_on_pack_without_content_pack_field_global_data_error(self):
        self._write_pack_build("{}")
        data_dir, applied, warning, error = self._prepare(True)
        self.assertEqual((data_dir, applied, warning), (self.pack_data_dir, "1.2.0-test", None))
        self.assertIn("Global data", error)

    def test_pack_with_damaged_json_error(self):
        with open(os.path.join(self.pack_dir, ms2offline.ContentPack.FILE_NAME), "w", encoding="utf-8") as f:
            f.write("{invalid json}")
        data_dir, applied, warning, error = self._prepare(True)
        self.assertEqual((data_dir, applied, warning), (self.global_data_dir, None, None))
        self.assertIn("damaged", error)

    def test_tail_with_global_header_unified_on_repairs_header(self):
        appended = self.global_data + self.tail
        self._write(self.archive, appended)
        self._write_pack_build('{"contentPack": "1.2.0-test"}')
        self.assertEqual(self._prepare(True), (self.pack_data_dir, "1.2.0-test", None, None))
        self.assertEqual(self._read(self.m2h), self.new_m2h)
        self.assertEqual(self._read(self.archive), appended)  # repaired, the tail is not appended twice

    def test_recover_interrupted_half_appended_archive_prepare_returns_warning(self):
        import json
        self._write(self.archive, self.global_data + self.tail[:700])
        with open(os.path.join(self.pack_dir, "apply-journal.json"), "w", encoding="utf-8") as f:
            json.dump({"Version": "1.2.0-test", "State": "applying", "Archive": "Data/Xml.m2d", "Step": "tail",
                       "Utc": "2026-10-20T00:00:00.0000000Z"}, f)
        self._write_pack_build('{"contentPack": "1.2.0-test"}')
        data_dir, applied, warning, error = self._prepare(True)
        self.assertIn("interrupted", warning)
        self.assertEqual((applied, error), ("1.2.0-test", None))
        self.assertEqual(self._read(self.archive), self.global_data + self.tail)  # truncated, then applied again

    def test_restore_refused_error(self):
        self._write_pack_build('{"contentPack": "1.2.0-test"}')
        self._prepare(True)
        self.assertEqual(self._status(), ms2offline.ContentPack.Status.Applied)
        self._read_only(self.archive)
        data_dir, applied, warning, error = self._prepare(False)
        self.assertEqual(data_dir, self.global_data_dir)
        self.assertIn("could not be removed", error)

    def test_foreign_client_unified_off_no_warning_nothing_written(self):
        foreign = bytearray(self.global_data)
        foreign[10] ^= 0xFF
        self._write(self.archive, bytes(foreign))
        self.assertEqual(self._prepare(False), (self.global_data_dir, None, None, None))
        self.assertEqual(self._read(self.archive), bytes(foreign))
        self.assertEqual(self._read(self.m2h), self.orig_m2h)
        self.assertFalse(os.path.exists(self.orig_m2h_path))

    def test_no_pack_folder_global_data_names_pack_lock_error(self):
        import shutil
        shutil.rmtree(self.pack_dir)
        with open(os.path.join(self.global_data_dir, "data-build.json"), "w", encoding="utf-8") as f:
            f.write('{"contentPack": "x"}')
        data_dir, applied, warning, error = self._prepare(True)
        self.assertEqual((data_dir, applied), (self.global_data_dir, None))
        self.assertIn("pack x", error)

    def test_restore_throws_warns_and_keeps_pack_data(self):
        self._write_pack_build('{"contentPack": "1.2.0-test"}')
        self._prepare(True)
        self.assertEqual(self._status(), ms2offline.ContentPack.Status.Applied)
        os.remove(self.orig_m2h_path)  # restore cannot put the Global header back and raises
        data_dir, applied, warning, error = self._prepare(False)
        self.assertEqual((data_dir, applied, error), (self.pack_data_dir, "1.2.0-test", None))
        self.assertIn("Xml.orig.m2h", warning)

    def test_apply_throws_tampered_tail_warns_and_starts_global(self):
        tampered = bytearray(self.tail)
        tampered[10] ^= 0xFF
        self._write(os.path.join(self.pack_dir, "Xml.tail"), bytes(tampered))
        data_dir, applied, warning, error = self._prepare(True)
        self.assertEqual((data_dir, applied, error), (self.global_data_dir, None, None))
        self.assertIn("Xml.tail", warning)
        self.assertEqual(self._read(self.archive), self.global_data)
        self.assertEqual(self._read(self.m2h), self.orig_m2h)


class TestContentWiring(unittest.TestCase):
    """M2b-09b2: the settings and the server data folder that Play uses for the unified content."""

    def setUp(self):
        self.home = tempfile.mkdtemp(prefix="ms2-wiring-")

    def tearDown(self):
        import shutil
        shutil.rmtree(self.home)

    def _ini(self, text):
        with open(os.path.join(self.home, "launcher.ini"), "w", encoding="utf-8") as f:
            f.write(text)

    def test_unified_content_defaults_on_and_reads_the_ini(self):
        self.assertTrue(ms2offline.Settings(self.home).unified_content)
        self._ini("UnifiedContent=false\n")
        self.assertFalse(ms2offline.Settings(self.home).unified_content)

    def test_client_root_is_the_parent_of_the_x64_folder(self):
        settings = ms2offline.Settings(self.home)
        settings.client_dir = os.path.join(self.home, "MS2", "x64")
        self.assertEqual(settings.client_root, os.path.normpath(os.path.join(self.home, "MS2")))
        settings.client_dir = ""
        self.assertEqual(settings.client_root, "")

    def test_servers_use_the_chosen_data_folder(self):
        from unittest import mock
        settings = ms2offline.Settings(self.home)
        pack_data = os.path.join(self.home, "ContentPack", "GameData")
        for folder in (pack_data, settings.game_data_dir):
            os.makedirs(folder)
            with open(os.path.join(folder, "maple-data.db"), "wb") as f:
                f.write(b"\0")
        host = ms2offline.ServerHost(settings, mock.MagicMock())
        host.data_dir = pack_data
        self.assertEqual(host.environment()["SQLITE_DATA_DIR"], pack_data)
        host.data_dir = None
        self.assertEqual(host.environment()["SQLITE_DATA_DIR"], settings.game_data_dir)


class TestPlayContentStep(unittest.TestCase):
    """M2b-09b2: the content step runs before the servers start, and its error never starts them."""

    def setUp(self):
        self.home = tempfile.mkdtemp(prefix="ms2-play-")
        with open(os.path.join(self.home, "launcher.ini"), "w", encoding="utf-8") as f:
            f.write("AutoUpdate=off\nDebugReports=false\nClientDir=" + os.path.join(self.home, "MS2", "x64") + "\n")

    def tearDown(self):
        import shutil
        shutil.rmtree(self.home, ignore_errors=True)

    def _play(self, prepared):
        from unittest import mock
        launcher = ms2offline.Launcher(self.home)
        seen = {}

        def start_servers():
            seen["data_dir"] = launcher.host.data_dir
            raise RuntimeError("the test stops here")

        with mock.patch.object(ms2offline, "prepare_content", return_value=prepared) as prepare, \
                mock.patch.object(ms2offline.Client, "running_pids", return_value=[]), \
                mock.patch.object(launcher, "take_lock"), mock.patch.object(launcher, "claim_ports"), \
                mock.patch.object(launcher, "backup_save"), \
                mock.patch.object(launcher, "start_servers", side_effect=start_servers) as start, \
                mock.patch.object(launcher.ui, "error"), mock.patch.object(launcher.ui, "notify"):
            launcher.play(servers_only=True)
        return prepare, start, seen

    def test_content_error_never_starts_the_servers(self):
        prepare, start, _ = self._play((os.path.join(self.home, "GameData"), None, None, "The content pack is damaged: x."))
        prepare.assert_called_once()
        start.assert_not_called()

    def test_servers_get_the_data_folder_prepare_chose(self):
        pack_data = os.path.join(self.home, "ContentPack", "GameData")
        _, start, seen = self._play((pack_data, "1.2.0", None, None))
        start.assert_called_once()
        self.assertEqual(seen["data_dir"], pack_data)


if __name__ == "__main__":
    unittest.main()