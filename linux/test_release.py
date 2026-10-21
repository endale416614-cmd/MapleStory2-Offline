#!/usr/bin/env python3
"""Tests for the release signature verification functionality."""

import base64
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import types
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.modules.setdefault("fcntl", types.ModuleType("fcntl"))
import ms2offline  # noqa: E402

OPENSSL = shutil.which("openssl")
HERE = os.path.dirname(os.path.abspath(__file__))


@unittest.skipUnless(OPENSSL, "needs openssl")
class SignedListTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory()
        cls.key = os.path.join(cls.tmp.name, "key.pem")
        pub = os.path.join(cls.tmp.name, "pub.der")
        subprocess.run([OPENSSL, "ecparam", "-name", "prime256v1", "-genkey", "-noout", "-out", cls.key], check=True, capture_output=True)
        subprocess.run([OPENSSL, "ec", "-in", cls.key, "-pubout", "-outform", "DER", "-out", pub], check=True, capture_output=True)
        with open(pub, "rb") as f:
            cls.public_key = base64.b64encode(f.read()).decode("ascii")

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    def sign(self, manifest):
        """The base64 text of a release.json.sig for these release.json bytes, signed with the test key."""
        message = os.path.join(self.tmp.name, "message")
        sig = os.path.join(self.tmp.name, "sig.der")
        with open(message, "wb") as f:
            f.write(ms2offline.RELEASE_HEADER + manifest)
        subprocess.run([OPENSSL, "dgst", "-sha256", "-sign", self.key, "-out", sig, message], check=True, capture_output=True)
        with open(sig, "rb") as f:
            return base64.b64encode(f.read())

    @staticmethod
    def manifest(files, version="1.2.0"):
        return json.dumps({"version": version, "files": files, "notes": ""}).encode("utf-8")

    def test_valid_list_gives_names_and_hashes(self):
        data = self.manifest([{"name": "a.zip", "size": 1, "sha256": "a" * 64}, {"name": "b.exe", "size": 2, "sha256": "B" * 64}])
        listed = ms2offline.Releases.verify_manifest(data, self.sign(data), public_key=self.public_key)
        self.assertEqual(listed, {"a.zip": "a" * 64, "b.exe": "b" * 64})

    def test_tampered_list_is_refused(self):
        data = self.manifest([{"name": "a.zip", "size": 1, "sha256": "a" * 64}])
        signature = self.sign(data)
        with self.assertRaises(ValueError):
            ms2offline.Releases.verify_manifest(data.replace(b"a" * 64, b"c" * 64), signature, public_key=self.public_key)

    def test_garbage_signature_is_refused(self):
        data = self.manifest([{"name": "a.zip", "size": 1, "sha256": "a" * 64}])
        with self.assertRaises(ValueError):
            ms2offline.Releases.verify_manifest(data, b"bm90IGEgc2lnbmF0dXJl", public_key=self.public_key)

    def test_other_key_is_refused(self):
        data = self.manifest([{"name": "a.zip", "size": 1, "sha256": "a" * 64}])
        with self.assertRaises(ValueError):
            ms2offline.Releases.verify_manifest(data, self.sign(data))  # signed by the test key, checked with the project key

    def test_malformed_list_is_refused(self):
        for files in ([{"name": "a.zip", "size": 1, "sha256": "a" * 10}], [{"name": "a.zip", "size": 1}]):
            data = self.manifest(files)
            with self.assertRaises(ValueError) as caught:
                ms2offline.Releases.verify_manifest(data, self.sign(data), public_key=self.public_key)
            self.assertIn("malformed", str(caught.exception))


class CheckListedTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.path = os.path.join(self.tmp.name, "MapleStory2-Offline-Update-1.2.0.zip")
        with open(self.path, "wb") as f:
            f.write(b"update package")

    def tearDown(self):
        self.tmp.cleanup()

    def test_matching_file_is_kept(self):
        listed = {os.path.basename(self.path): hashlib.sha256(b"update package").hexdigest()}
        ms2offline.Releases.check_listed(self.path, listed)
        self.assertTrue(os.path.exists(self.path))

    def test_different_file_is_deleted(self):
        with self.assertRaises(ValueError):
            ms2offline.Releases.check_listed(self.path, {os.path.basename(self.path): "0" * 64})
        self.assertFalse(os.path.exists(self.path))

    def test_unlisted_file_is_deleted(self):
        with self.assertRaises(ValueError):
            ms2offline.Releases.check_listed(self.path, {})
        self.assertFalse(os.path.exists(self.path))


class InstallTest(unittest.TestCase):
    def test_release_without_signed_list_is_refused(self):
        with tempfile.TemporaryDirectory() as home:
            with open(os.path.join(home, "launcher.ini"), "w", encoding="utf-8") as f:
                f.write("DebugReports=false\n")
            latest = {"version": "1.2.0", "notes": "", "page": "", "setup_url": "https://github.com/x/setup.exe",
                      "update_url": "https://github.com/x/update.zip", "update_name": "update.zip", "update_size": 1,
                      "manifest_url": None, "signature_url": None}
            with self.assertRaises(RuntimeError) as caught:
                ms2offline.Launcher(home).install_release(latest=latest)  # refused before any download
            self.assertIn("no signed file list", str(caught.exception))


class KeyAndProfileTest(unittest.TestCase):
    def test_public_key_matches_windows(self):
        with open(os.path.join(HERE, "..", "Launcher", "UpdateKey.cs"), encoding="utf-8") as f:
            windows = re.search(r'PublicKey\s*=\s*"([^"]+)"', f.read()).group(1)
        self.assertEqual(ms2offline.UPDATE_PUBLIC_KEY, windows)

    def test_profile_rules(self):
        for name in ("", "Second run", "a-b_c", "Café"):
            self.assertIsNone(ms2offline.profile_error(name), name)
        for name in ("x" * 33, "a/b", "..", "backups", "BACKUPS"):
            self.assertIsNotNone(ms2offline.profile_error(name), name)

    def test_profile_save_dir(self):
        with tempfile.TemporaryDirectory() as home:
            for value, expected in (("Second", os.path.join(home, "SaveData", "Second")), ("../x", os.path.join(home, "SaveData"))):
                with open(os.path.join(home, "launcher.ini"), "w", encoding="utf-8") as f:
                    f.write(f"Profile={value}\n")
                self.assertEqual(ms2offline.Settings(home).save_dir, expected)


if __name__ == "__main__":
    unittest.main()