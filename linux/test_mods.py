import os
import sys
import tempfile
import types
import unittest

# Import the launcher module from this folder (fcntl mocked for Windows, as in test_contentpack.py).
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.modules.setdefault("fcntl", types.ModuleType("fcntl"))
import ms2offline

FEATURE = os.path.join("Data", "Xml", "table", "feature.xml")


class TestModFile(unittest.TestCase):
    """D-4: offline-shop's feature.xml is set aside while the unified content is applied, and restored without it."""

    def test_set_mod_file(self):
        with tempfile.TemporaryDirectory() as mods_dir:
            on = os.path.join(mods_dir, "offline-shop", FEATURE)
            off = on + ".off"
            os.makedirs(os.path.dirname(on))
            with open(on, "w", encoding="utf-8") as f:
                f.write("shop features")

            ms2offline.set_mod_file(mods_dir, "offline-shop", FEATURE, False)
            self.assertFalse(os.path.exists(on))
            with open(off, encoding="utf-8") as f:
                self.assertEqual(f.read(), "shop features")

            ms2offline.set_mod_file(mods_dir, "offline-shop", FEATURE, False)  # again: nothing changes
            self.assertFalse(os.path.exists(on))
            self.assertTrue(os.path.exists(off))

            ms2offline.set_mod_file(mods_dir, "offline-shop", FEATURE, True)
            self.assertTrue(os.path.exists(on))
            self.assertFalse(os.path.exists(off))

            os.remove(on)
            ms2offline.set_mod_file(mods_dir, "offline-shop", FEATURE, False)  # neither file: nothing happens
            ms2offline.set_mod_file(mods_dir, "offline-shop", FEATURE, True)
            self.assertFalse(os.path.exists(on) or os.path.exists(off))


if __name__ == "__main__":
    unittest.main()
