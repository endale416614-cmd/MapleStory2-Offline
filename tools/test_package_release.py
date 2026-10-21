import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import package_release


class TestPackageRelease(unittest.TestCase):
    def test_add_content_pack(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            # Create a content pack directory structure
            pack_dir = os.path.join(temp_dir, "pack")
            os.makedirs(os.path.join(pack_dir, "GameData"))

            # Create required files
            with open(os.path.join(pack_dir, "pack.json"), "w", encoding="utf-8") as f:
                f.write('{"version": "9.9.9", "archives": []}')

            with open(os.path.join(pack_dir, "Xml.tail"), "wb") as f:
                f.write(b"tail content")

            with open(os.path.join(pack_dir, "Xml.m2h"), "wb") as f:
                f.write(b"m2h content")

            with open(os.path.join(pack_dir, "GameData", "maple-data.db"), "wb") as f:
                f.write(b"data db content")

            with open(os.path.join(pack_dir, "GameData", "data-build.json"), "w", encoding="utf-8") as f:
                f.write('{"contentPack": "9.9.9", "tables": {}}')

            # Create files that should be ignored
            with open(os.path.join(pack_dir, "apply-journal.json"), "w") as f:
                f.write("{}")

            with open(os.path.join(pack_dir, "Xml.m2h.tmp"), "w") as f:
                f.write("temp content")

            # Create staging directory
            stage = os.path.join(temp_dir, "stage")
            os.makedirs(stage)

            # Test the function
            version = package_release.add_content_pack(stage, pack_dir)

            # Verify returned version
            self.assertEqual(version, "9.9.9")

            # Verify files were copied correctly
            expected_files = [
                "ContentPack/pack.json",
                "ContentPack/Xml.tail",
                "ContentPack/Xml.m2h",
                "ContentPack/GameData/maple-data.db",
                "ContentPack/GameData/data-build.json"
            ]

            for file in expected_files:
                self.assertTrue(os.path.exists(os.path.join(stage, file)), f"File {file} should exist")

            # Verify ignored files were not copied
            self.assertFalse(os.path.exists(os.path.join(stage, "ContentPack/apply-journal.json")))
            self.assertFalse(os.path.exists(os.path.join(stage, "ContentPack/Xml.m2h.tmp")))

    def test_add_content_pack_missing_db(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            pack_dir = os.path.join(temp_dir, "pack")
            os.makedirs(os.path.join(pack_dir, "GameData"))

            # Create required files but missing GameData/maple-data.db
            with open(os.path.join(pack_dir, "pack.json"), "w", encoding="utf-8") as f:
                f.write('{"version": "9.9.9", "archives": []}')

            with open(os.path.join(pack_dir, "GameData", "data-build.json"), "w", encoding="utf-8") as f:
                f.write('{"contentPack": "9.9.9", "tables": {}}')

            stage = os.path.join(temp_dir, "stage")
            os.makedirs(stage)

            # Should raise SystemExit
            with self.assertRaises(SystemExit):
                package_release.add_content_pack(stage, pack_dir)

    def test_add_content_pack_mismatched_version(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            pack_dir = os.path.join(temp_dir, "pack")
            os.makedirs(os.path.join(pack_dir, "GameData"))

            # Create files with mismatched versions
            with open(os.path.join(pack_dir, "pack.json"), "w", encoding="utf-8") as f:
                f.write('{"version": "9.9.9", "archives": []}')

            with open(os.path.join(pack_dir, "GameData", "maple-data.db"), "wb") as f:
                f.write(b"data db content")

            with open(os.path.join(pack_dir, "GameData", "data-build.json"), "w", encoding="utf-8") as f:
                f.write('{"contentPack": "1.0.0", "tables": {}}')

            stage = os.path.join(temp_dir, "stage")
            os.makedirs(stage)

            # Should raise SystemExit
            with self.assertRaises(SystemExit):
                package_release.add_content_pack(stage, pack_dir)

    def test_add_content_pack_missing_content_pack_field(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            pack_dir = os.path.join(temp_dir, "pack")
            os.makedirs(os.path.join(pack_dir, "GameData"))

            # Create files with missing contentPack field
            with open(os.path.join(pack_dir, "pack.json"), "w", encoding="utf-8") as f:
                f.write('{"version": "9.9.9", "archives": []}')

            with open(os.path.join(pack_dir, "GameData", "maple-data.db"), "wb") as f:
                f.write(b"data db content")

            with open(os.path.join(pack_dir, "GameData", "data-build.json"), "w", encoding="utf-8") as f:
                f.write('{"tables": {}}')

            stage = os.path.join(temp_dir, "stage")
            os.makedirs(stage)

            # Should raise SystemExit
            with self.assertRaises(SystemExit):
                package_release.add_content_pack(stage, pack_dir)

    def test_add_content_pack_writes_compatibility(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            # Create a content pack directory structure
            pack_dir = os.path.join(temp_dir, "pack")
            os.makedirs(os.path.join(pack_dir, "GameData"))

            # Create required files
            with open(os.path.join(pack_dir, "pack.json"), "w", encoding="utf-8") as f:
                f.write('{"version": "9.9.9", "archives": [{"path": "Data/Xml.m2d", "globalSize": 10, "globalSha256": "aa"}, {"path": "Data/Resource/Image.m2d", "globalSize": 20, "globalSha256": "bb"}]}')

            with open(os.path.join(pack_dir, "Xml.tail"), "wb") as f:
                f.write(b"tail content")

            with open(os.path.join(pack_dir, "Xml.m2h"), "wb") as f:
                f.write(b"m2h content")

            with open(os.path.join(pack_dir, "GameData", "maple-data.db"), "wb") as f:
                f.write(b"data db content")

            with open(os.path.join(pack_dir, "GameData", "data-build.json"), "w", encoding="utf-8") as f:
                f.write('{"contentPack": "9.9.9", "tables": {}}')

            # Create files that should be ignored
            with open(os.path.join(pack_dir, "apply-journal.json"), "w") as f:
                f.write("{}")

            with open(os.path.join(pack_dir, "Xml.m2h.tmp"), "w") as f:
                f.write("temp content")

            # Create staging directory
            stage = os.path.join(temp_dir, "stage")
            os.makedirs(stage)

            # Test the function
            version = package_release.add_content_pack(stage, pack_dir)

            # Verify returned version
            self.assertEqual(version, "9.9.9")

            # Verify files were copied correctly
            expected_files = [
                "ContentPack/pack.json",
                "ContentPack/Xml.tail",
                "ContentPack/Xml.m2h",
                "ContentPack/GameData/maple-data.db",
                "ContentPack/GameData/data-build.json"
            ]

            for file in expected_files:
                self.assertTrue(os.path.exists(os.path.join(stage, file)), f"File {file} should exist")

            # Verify ignored files were not copied
            self.assertFalse(os.path.exists(os.path.join(stage, "ContentPack/apply-journal.json")))
            self.assertFalse(os.path.exists(os.path.join(stage, "ContentPack/Xml.m2h.tmp")))

            # Verify COMPATIBILITY.txt was created with correct content
            compatibility_path = os.path.join(stage, "COMPATIBILITY.txt")
            self.assertTrue(os.path.exists(compatibility_path))

            with open(compatibility_path, "r", encoding="utf-8") as f:
                content = f.read()

            # Verify the content contains expected lines
            self.assertIn("MapleStory 2 Offline content pack 9.9.9", content)
            self.assertIn("  Data/Xml.m2d  10  aa", content)
            self.assertIn("  Data/Resource/Image.m2d  20  bb", content)

            # Verify order is preserved (first entry before second)
            lines = content.split('\n')
            xml_line_index = None
            image_line_index = None
            for i, line in enumerate(lines):
                if "Data/Xml.m2d" in line:
                    xml_line_index = i
                elif "Data/Resource/Image.m2d" in line:
                    image_line_index = i

            self.assertIsNotNone(xml_line_index)
            self.assertIsNotNone(image_line_index)
            self.assertLess(xml_line_index, image_line_index)


if __name__ == "__main__":
    unittest.main()