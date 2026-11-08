#!/usr/bin/env python3
"""Tests for the release signature verification functionality."""

import base64
import hashlib
import importlib.util
import io
import json
import os
import re
import shutil
import sqlite3
import subprocess
import sys
import tempfile
import types
import unittest
import zipfile
from unittest import mock

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "tools"))
sys.modules.setdefault("fcntl", types.ModuleType("fcntl"))
import ms2offline  # noqa: E402
import package_release  # noqa: E402

OPENSSL = shutil.which("openssl")
HERE = os.path.dirname(os.path.abspath(__file__))
SETUP = "MapleStory2-Offline-Setup.exe"
SETUP_SAVED = "MapleStory2-Offline-Setup-1.2.2.exe"
UPDATE = "MapleStory2-Offline-Update-1.2.2.zip"


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

    def install_written_list(self, installed):
        """Release 1.2.2 as it is published (the Setup under its README name, an update zip for 1.2.1 on, the list from
        write_release, signed) through install_release over this VERSION; None is a new install. Only the network, the key
        and the .NET step are stubbed. Returns the names the launcher saved the files under, and the version it ends on."""
        with tempfile.TemporaryDirectory() as tmp:
            stage, published, home = (os.path.join(tmp, name) for name in ("stage", "published", "home"))
            for folder in (stage, published, home):
                os.makedirs(folder)
            with open(os.path.join(stage, "VERSION"), "w", encoding="utf-8") as f:
                f.write("1.2.2\n")
            package_release.manifest(stage)
            payload, launcher_exe = os.path.join(tmp, "payload.zip"), os.path.join(tmp, "launcher.exe")
            package_release.zip_folder(stage, payload)
            with open(launcher_exe, "wb") as f:
                f.write(b"launcher")
            setup, update = os.path.join(published, SETUP), os.path.join(published, UPDATE)
            package_release.make_setup(launcher_exe, payload, setup)
            package_release.zip_folder(stage, update, only=["VERSION", "files.json"],
                                       extra={"update.json": json.dumps({"version": "1.2.2", "minVersion": "1.2.1", "delete": []})})
            release = os.path.join(published, "release.json")
            package_release.write_release(release, "1.2.2", "", [setup, update])
            with open(release, "rb") as f:
                signature = self.sign(f.read())
            with open(release + ".sig", "wb") as f:
                f.write(signature)

            with open(os.path.join(home, "launcher.ini"), "w", encoding="utf-8") as f:
                f.write("DebugReports=false\n")
            if installed:
                with open(os.path.join(home, "VERSION"), "w", encoding="utf-8") as f:
                    f.write(installed + "\n")
            saved = []

            def download(url, target, ui, label, log=None):
                saved.append(os.path.basename(target))
                os.makedirs(os.path.dirname(target), exist_ok=True)
                shutil.copyfile(os.path.join(published, os.path.basename(url)), target)
                return target

            assets = [{"name": name, "browser_download_url": f"https://github.com/x/{name}", "size": 1} for name in os.listdir(published)]
            verify = ms2offline.Releases.verify_manifest
            launcher = ms2offline.Launcher(home)
            with mock.patch.multiple(ms2offline.Releases, download=download, install_dotnet=mock.DEFAULT,
                                     _get=lambda url: json.dumps({"tag_name": "v1.2.2", "assets": assets}).encode("utf-8"),
                                     verify_manifest=lambda data, sig: verify(data, sig, public_key=self.public_key)):
                launcher.install_release()
            self.assertIsNone(ms2offline.Releases.unfinished_update(home))
            return saved, launcher.settings.version

    def test_a_written_list_installs_over_every_version(self):
        """BT-170. Golden: the replay against the 1.2.2 files, which refused the first, third and fourth case."""
        lists = ["release.json", "release.json.sig"]
        for installed, packages in ((None, [SETUP_SAVED]), ("1.2.1", [UPDATE]), ("1.2.0", [UPDATE, SETUP_SAVED]), ("1.1.9", [UPDATE, SETUP_SAVED])):
            with self.subTest(installed=installed):
                self.assertEqual(self.install_written_list(installed), (lists + packages, "1.2.2"))


class WriteReleaseTest(unittest.TestCase):
    """package_release.write_release: a listed Setup is listed under both names (BT-170)."""

    @staticmethod
    def rows(*names):
        with tempfile.TemporaryDirectory() as tmp:
            assets = [os.path.join(tmp, name) for name in names]
            for name, asset in zip(names, assets):
                with open(asset, "wb") as f:
                    f.write(b"setup" if "Setup" in name else b"update zip")
            release = os.path.join(tmp, "release.json")
            package_release.write_release(release, "1.2.2", "", assets)
            with open(release, encoding="utf-8") as f:
                return json.load(f)["files"]

    def test_the_versioned_name_has_the_setup_size_and_hash(self):
        digest = hashlib.sha256(b"setup").hexdigest()
        update = {"name": UPDATE, "size": 10, "sha256": hashlib.sha256(b"update zip").hexdigest()}
        self.assertEqual(self.rows(SETUP, UPDATE), [{"name": SETUP, "size": 5, "sha256": digest}, update, {"name": SETUP_SAVED, "size": 5, "sha256": digest}])

    def test_a_setup_only_release_too(self):
        self.assertEqual([row["name"] for row in self.rows(SETUP)], [SETUP, SETUP_SAVED])

    def test_a_name_is_never_listed_twice(self):
        """Windows refuses a list that names a file twice (ReleaseManifest.Parse), and 1.2.0's signing passed both names."""
        self.assertEqual([row["name"] for row in self.rows(SETUP_SAVED, SETUP, UPDATE)], [SETUP_SAVED, SETUP, UPDATE])

    def test_without_the_published_setup_nothing_is_added(self):
        self.assertEqual([row["name"] for row in self.rows(UPDATE)], [UPDATE])


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


class InterruptedUpdateTest(unittest.TestCase):
    """PA-04: a marker stands from the first change to the program's files to the last; Play refuses it and says how to finish."""

    class Killed(BaseException):
        """The process dies here, as in a crash: no handler of the launcher runs."""

    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.tmp = tmp.name
        self.home, self.stage = os.path.join(self.tmp, "home"), os.path.join(self.tmp, "stage")
        for rel, text in (("VERSION", "1.2.3\n"), ("GameData/maple-data.db", "new data"), ("Maple2/a.dll", "new a"), ("Maple2/b.dll", "new b")):
            self.write(self.stage, rel, text)
        package_release.manifest(self.stage)
        self.ini()
        for rel, text in (("VERSION", "1.2.2\n"), ("Maple2/a.dll", "old a"), ("Maple2/gone.dll", "old gone")):
            self.write(self.home, rel, text)
        self.save = os.path.join(self.home, "SaveData", "game-server.db")
        os.makedirs(os.path.dirname(self.save))
        connection = sqlite3.connect(self.save)
        connection.execute("CREATE TABLE t (x)")
        connection.commit()
        connection.close()
        with open(self.save, "rb") as f:
            self.save_bytes = f.read()

    @staticmethod
    def write(folder, rel, text):
        path = os.path.join(folder, rel)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8") as f:
            f.write(text)

    def read(self, rel):
        with open(os.path.join(self.home, rel), encoding="utf-8") as f:
            return f.read()

    def ini(self, auto_update="off"):
        self.write(self.home, "launcher.ini", f"AutoUpdate={auto_update}\nDebugReports=false\nSendReports=never\n")

    def mark(self, text):
        self.write(self.home, ms2offline.UPDATE_MARKER, text)

    def marker(self):
        return ms2offline.Releases.unfinished_update(self.home)

    def setup_zip(self):
        """A full package, new each time: install_release deletes the file it installed from."""
        path = os.path.join(self.tmp, "MapleStory2-Offline-Setup-1.2.3.zip")
        package_release.zip_folder(self.stage, path)
        return path

    def update_zip(self):
        path = os.path.join(self.tmp, "MapleStory2-Offline-Update-1.2.3.zip")
        package_release.zip_folder(self.stage, path, extra={"update.json": json.dumps({"version": "1.2.3", "minVersion": "1.2.1", "delete": ["Maple2/gone.dll"]})})
        return path

    def install(self, source, part=None, calls=0):
        """install --from, without the .NET step. part: a (module, name) that writes program files; the process dies after that many calls."""
        launcher = ms2offline.Launcher(self.home)
        with mock.patch.object(ms2offline.Releases, "install_dotnet"):
            if not part:
                launcher.install_release(source=source)
                return
            real, left = getattr(*part), [calls]

            def dies(*args, **kwargs):
                left[0] -= 1
                if left[0] < 0:
                    raise self.Killed()
                return real(*args, **kwargs)

            with mock.patch.object(*part, dies), self.assertRaises(self.Killed):
                launcher.install_release(source=source)

    def assert_installed(self):
        self.assertIsNone(self.marker())
        for rel, text in (("VERSION", "1.2.3\n"), ("GameData/maple-data.db", "new data"), ("Maple2/a.dll", "new a"), ("Maple2/b.dll", "new b")):
            self.assertEqual(self.read(rel), text, rel)
        self.assert_save_untouched()

    def assert_save_untouched(self):
        with open(self.save, "rb") as f:
            self.assertEqual(f.read(), self.save_bytes)

    def play(self, latest=None, install=None, servers_only=False):
        """Launcher.play with the machine mocked, up to locate_client: the first step after the marker check. Returns the
        exit code, whether play got that far, and what ui.error showed."""
        launcher = ms2offline.Launcher(self.home)
        with mock.patch.object(ms2offline.Client, "running_pids", return_value=[]), \
                mock.patch.object(ms2offline.Releases, "latest", side_effect=OSError("offline") if latest is None else None, return_value=latest), \
                mock.patch.object(launcher, "take_lock"), mock.patch.object(launcher, "start_up_reports"), \
                mock.patch.object(launcher, "install_release", side_effect=install), \
                mock.patch.object(launcher, "locate_client", return_value=False) as locate, \
                mock.patch.object(launcher.ui, "error") as error:
            code = launcher.play(servers_only=servers_only)
        return code, locate.called, [call.args[0] for call in error.call_args_list]

    def test_a_clean_run_leaves_no_marker(self):
        self.install(self.update_zip())
        self.assert_installed()
        self.assertFalse(os.path.exists(os.path.join(self.home, "Maple2", "gone.dll")))
        self.install(self.setup_zip())
        self.assert_installed()

    def test_the_log_has_the_start_and_the_end_of_an_install(self):
        self.install(self.update_zip())
        with open(os.path.join(self.home, "logs", "launcher.log"), encoding="utf-8") as f:
            lines = [line.split("] ", 1)[1].rstrip("\n") for line in f]
        name = "MapleStory2-Offline-Update-1.2.3.zip"
        self.assertEqual(lines[-2:], [f"install: installing from {name}", f"install: installed 1.2.3 from {name} (5 files)"])

    def test_a_crash_leaves_the_marker_and_the_save(self):
        for package, part, calls, marker in ((self.update_zip, (ms2offline.os, "remove"), 0, "update 1.2.3"),  # the delete list
                                             (self.update_zip, (ms2offline.shutil, "copyfileobj"), 0, "update 1.2.3"),
                                             (self.update_zip, (ms2offline.shutil, "copyfileobj"), 2, "update 1.2.3"),
                                             (self.setup_zip, (ms2offline.shutil, "rmtree"), 1, "full"),  # the old folders go; call 0 is the staging folder
                                             (self.setup_zip, (ms2offline.os, "replace"), 0, "full"),
                                             (self.setup_zip, (ms2offline.os, "replace"), 2, "full")):
            with self.subTest(part=part[1], calls=calls):
                self.install(package(), part, calls)
                self.assertEqual(self.marker(), marker)
                self.assert_save_untouched()

    def test_a_crash_stops_play_until_the_install_finishes(self):
        for finish in (self.update_zip, self.setup_zip):
            with self.subTest(finish=finish.__name__):
                self.install(self.update_zip(), (ms2offline.shutil, "copyfileobj"), 2)
                self.assertEqual(self.play(), (1, False, [ms2offline.UPDATE_UNFINISHED]))
                self.assert_save_untouched()
                self.install(finish())
                self.assert_installed()
                self.assertEqual(self.play(), (None, True, []))

    def test_a_fresh_install_behaves_the_same(self):
        shutil.rmtree(os.path.join(self.home, "Maple2"))
        os.remove(os.path.join(self.home, "VERSION"))
        self.install(self.setup_zip(), (ms2offline.os, "replace"), 2)
        self.assertEqual(self.marker(), "full")
        self.assertEqual(self.play(), (1, False, [ms2offline.UPDATE_UNFINISHED]))
        self.install(self.setup_zip())
        self.assert_installed()

    # UG-01. Golden: a portrait path of an rc-1.2.2 install and the shop banner the release ships.
    PORTRAIT = "Maple2/Maple2.Server.Web/Data/profiles/20000000000/f1f21ca3-d786-472f-b2ef-e84bb4daea29.png"
    SHOP_BANNER = "Maple2/Maple2.Server.Web/Data/system/banner/homeproduct_1.png"

    def with_uploaded_files(self):
        self.write(self.stage, self.SHOP_BANNER, "new banner")
        package_release.manifest(self.stage)
        self.write(self.home, self.SHOP_BANNER, "old banner")
        self.write(self.home, self.PORTRAIT, "my portrait")

    def test_a_full_install_keeps_what_the_old_web_server_wrote(self):
        self.with_uploaded_files()
        self.install(self.setup_zip())
        self.assert_installed()
        self.assertEqual(self.read(self.PORTRAIT), "my portrait")
        self.assertEqual(self.read(self.SHOP_BANNER), "new banner")
        self.assertFalse(os.path.exists(os.path.join(self.home, "Maple2", "gone.dll")), "the rest of the old folder goes")

    def test_a_crash_in_a_full_install_keeps_it_too(self):
        self.with_uploaded_files()
        for part, calls in (((ms2offline.shutil, "rmtree"), 1), ((ms2offline.os, "replace"), 0), ((ms2offline.os, "replace"), 2)):
            with self.subTest(part=part[1], calls=calls):
                self.install(self.setup_zip(), part, calls)
                self.assertEqual(self.marker(), "full")
                self.assertEqual(self.read(self.PORTRAIT), "my portrait")
        self.install(self.setup_zip())
        self.assert_installed()
        self.assertEqual(self.read(self.PORTRAIT), "my portrait")

    def test_a_file_that_will_not_go_does_not_stop_a_full_install(self):
        real = os.remove

        def refuses(path, *args, **kwargs):
            if path.endswith("gone.dll"):
                raise PermissionError(path)
            real(path, *args, **kwargs)

        with mock.patch.object(ms2offline.os, "remove", refuses):
            self.install(self.setup_zip())
        self.assert_installed()

    def test_an_update_package_leaves_it(self):
        self.with_uploaded_files()
        self.install(self.update_zip())
        self.assert_installed()
        self.assertEqual(self.read(self.PORTRAIT), "my portrait")

    def test_only_its_own_update_package_or_a_full_one_finishes_it(self):
        for marker in ("full", "update 1.2.2", ""):
            with self.subTest(marker=marker):
                self.mark(marker)
                with self.assertRaises(ValueError):
                    self.install(self.update_zip())
                self.assertEqual(self.marker(), marker)
                self.assertEqual(self.read("Maple2/a.dll"), "old a")
                self.assertTrue(os.path.exists(os.path.join(self.home, "Maple2", "gone.dll")))
        self.install(self.setup_zip())
        self.assert_installed()

    def test_a_setup_that_is_still_being_checked_changes_nothing(self):
        self.install(self.setup_zip(), (ms2offline.shutil, "copyfileobj"), 1)
        self.assertIsNone(self.marker())
        path = os.path.join(self.tmp, "MapleStory2-Offline-Setup-1.2.3.zip")
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("files.json", json.dumps({"VERSION": "0" * 64}))
            archive.writestr("VERSION", "1.2.3\n")
        with self.assertRaises(ValueError):
            self.install(path)
        self.assertIsNone(self.marker())
        self.assertEqual(self.read("Maple2/a.dll"), "old a")
        self.assertEqual(self.read("VERSION"), "1.2.2\n")
        self.assertEqual(self.play(), (None, True, []))

    def test_the_package_that_finishes_it_and_a_failed_download(self):
        """The download writes only to updates/, so a failed one leaves no marker."""
        latest = {"version": "1.2.3", "setup_url": "https://github.com/x/setup", "update_url": "https://github.com/x/update", "update_name": "update.zip",
                  "manifest_url": "https://github.com/x/list", "signature_url": "https://github.com/x/sig"}
        for marker, package in ((None, "update 1.2.3"), ("update 1.2.3", "update 1.2.3"), ("update 1.2.2", "release 1.2.3"), ("full", "release 1.2.3"), ("", "release 1.2.3")):
            with self.subTest(marker=marker):
                if marker is not None:
                    self.mark(marker)
                labels = []

                def download(url, target, ui, label, log=None):
                    labels.append(label)
                    if label not in ("release.json", "release.json.sig"):
                        raise OSError("the network is gone")
                    with open(target, "w", encoding="utf-8") as f:
                        f.write(json.dumps({"version": "1.2.3"}))
                    return target

                with mock.patch.multiple(ms2offline.Releases, download=download, verify_manifest=lambda data, sig: {}), self.assertRaises(OSError):
                    ms2offline.Launcher(self.home).install_release(latest)
                self.assertEqual(labels[-1], package)
                self.assertEqual(self.marker(), marker)

    def test_play_with_auto_update(self):
        newer = {"version": "1.2.3", "notes": ""}
        self.ini("always")

        def finishes(_):
            os.remove(os.path.join(self.home, ms2offline.UPDATE_MARKER))

        def stops(_):
            self.mark("update 1.2.3")
            raise OSError("no space left")

        def fails(_):
            raise OSError("damaged")

        self.assertEqual(self.play(newer, fails), (None, True, ["The update could not be installed (damaged). You can keep playing; it is tried again next time."]))
        self.assertEqual(self.play(newer, stops), (1, False, ["The update could not be installed (no space left).", ms2offline.UPDATE_UNFINISHED]))
        self.assertEqual(self.play(), (1, False, [ms2offline.UPDATE_UNFINISHED]))  # offline
        self.assertEqual(self.play(newer, finishes), (None, True, []))

    def test_servers_only_is_refused_too(self):
        self.mark("full")
        self.assertEqual(self.play(servers_only=True), (1, False, [ms2offline.UPDATE_UNFINISHED]))

    def test_the_update_command_finishes_it_at_the_current_version(self):
        def update():
            with mock.patch.object(ms2offline.Releases, "latest", return_value={"version": "1.2.2", "notes": ""}), \
                    mock.patch.object(ms2offline.Ports, "own_server_pids", return_value=[]), \
                    mock.patch.object(ms2offline.Launcher, "take_lock"), \
                    mock.patch.object(ms2offline.Launcher, "install_release") as install:
                self.assertEqual(ms2offline.main(["--home", self.home, "update"]), 0)
            return install.called

        self.assertFalse(update())
        self.mark("full")
        self.assertTrue(update())

    def test_the_line_has_the_words_of_the_windows_launcher(self):
        with open(os.path.join(HERE, "..", "Launcher", "UpdateInstaller.cs"), encoding="utf-8") as f:
            windows = f.read()
        for words in ("was interrupted last time", "your save was not changed", "Install the update again"):
            self.assertIn(words, windows)
            self.assertIn(words, ms2offline.UPDATE_UNFINISHED)
        self.assertNotIn("\n", ms2offline.UPDATE_UNFINISHED)
        self.assertTrue(ms2offline.UPDATE_UNFINISHED.endswith("ms2offline install"))


class AnswerTest(unittest.TestCase):
    """UI.answer and UI.ask (BT-172): a dialog tool answers with exit code 0 or 1; any other code, or a tool that cannot start, is no answer."""

    @staticmethod
    def call(method, tools, gui=True, tty=False, default=False, typed=""):
        """A UI method with these tools on the PATH ({name: exit code or OSError}). Returns its result and the tools that ran."""
        ui = ms2offline.UI.__new__(ms2offline.UI)
        ui.gui, ui.tty = gui, tty
        ran = []

        def run(args, check):
            ran.append(args[0])
            if isinstance(tools[args[0]], OSError):
                raise tools[args[0]]
            return types.SimpleNamespace(returncode=tools[args[0]])

        with mock.patch("shutil.which", lambda name: name if name in tools else None), mock.patch("subprocess.run", run), \
                mock.patch("builtins.input", return_value=typed):
            return getattr(ui, method)("Update now?", default=default), ran

    def test_exit_codes_0_and_1_are_the_answer(self):
        for tool in ("kdialog", "zenity"):
            self.assertEqual(self.call("answer", {tool: 0}), (True, [tool]))
            self.assertEqual(self.call("answer", {tool: 1}), (False, [tool]))

    def test_any_other_code_or_a_tool_that_cannot_start_is_no_answer(self):
        for failure in (2, 127, 254, 255, -6, OSError("cannot start")):
            with self.subTest(failure=failure):
                self.assertEqual(self.call("answer", {"kdialog": failure}), (None, ["kdialog"]))

    def test_the_next_tool_then_the_terminal_come_after_a_failure(self):
        both = ["kdialog", "zenity"]
        self.assertEqual(self.call("answer", {"kdialog": 127, "zenity": 0}), (True, both))
        self.assertEqual(self.call("answer", {"kdialog": 127, "zenity": 1}), (False, both))
        self.assertEqual(self.call("answer", {"kdialog": OSError("cannot start"), "zenity": 0}), (True, both))
        self.assertEqual(self.call("answer", {"kdialog": 127, "zenity": 255}), (None, both))
        self.assertEqual(self.call("answer", {"kdialog": 127}, tty=True, typed="y"), (True, ["kdialog"]))
        self.assertEqual(self.call("answer", {"kdialog": 127}, tty=True), (False, ["kdialog"]))  # Enter takes the default

    def test_nobody_can_answer_without_a_tool_a_desktop_or_a_terminal(self):
        self.assertEqual(self.call("answer", {}), (None, []))
        self.assertEqual(self.call("answer", {"kdialog": 0}, gui=False), (None, []))

    def test_ask_comes_back_as_it_did(self):
        self.assertEqual(self.call("ask", {}, default=True), (True, []))
        self.assertEqual(self.call("ask", {}, default=False), (False, []))
        self.assertEqual(self.call("ask", {"kdialog": 0}), (True, ["kdialog"]))
        self.assertEqual(self.call("ask", {"zenity": 1}, default=True), (False, ["zenity"]))
        # A failed tool is not the default: the report notice asks with default=True and would turn sending on unseen.
        self.assertEqual(self.call("ask", {"kdialog": 127}, default=True), (False, ["kdialog"]))
        self.assertEqual(self.call("ask", {"kdialog": 127}, default=False), (False, ["kdialog"]))


class AutoUpdateTest(unittest.TestCase):
    """Launcher.offer_online_update (BT-172): with nobody to answer AutoUpdate=ask installs, and a failure reaches the session report."""
    NEWER = {"version": "1.2.3", "notes": ""}
    AVAILABLE = "update: version 1.2.3 available online"
    NOBODY = "update: 1.2.3: nobody can answer the question, so it installs"
    POSTPONED = "update: 1.2.3 postponed by the player"
    UPDATED = "Updated to version 1.2.3"
    FAILED = "The update to version 1.2.3 failed: no space left"

    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.home = tmp.name
        with open(os.path.join(self.home, "VERSION"), "w", encoding="utf-8") as f:
            f.write("1.2.2\n")

    def launcher(self, auto_update="ask"):
        with open(os.path.join(self.home, "launcher.ini"), "w", encoding="utf-8") as f:
            f.write(f"AutoUpdate={auto_update}\nSendReports=never\nClientDir=" + os.path.join(self.home, "MS2", "x64") + "\n")
        return ms2offline.Launcher(self.home)

    def offer(self, launcher, latest=NEWER, install=None, tools=None, gui=False):
        """offer_online_update with no network and no install. tools: the dialog tools on the PATH, {name: exit code}.
        Returns the mocks of install_release and of ui.error."""
        launcher.ui.gui, launcher.ui.tty = gui, False
        tools = tools or {}
        with mock.patch("shutil.which", lambda name: name if name in tools else None), \
                mock.patch("subprocess.run", lambda args, check: types.SimpleNamespace(returncode=tools[args[0]])), \
                mock.patch.object(ms2offline.Releases, "latest", side_effect=None if latest else OSError("offline"), return_value=latest), \
                mock.patch.object(launcher, "install_release", side_effect=install) as installs, \
                mock.patch.object(launcher.ui, "error") as error:
            launcher.offer_online_update()
        return installs, error

    @staticmethod
    def logged(launcher):
        with open(launcher.log.path, encoding="utf-8") as f:
            return [line.split("] ", 1)[1].rstrip("\n") for line in f]

    @staticmethod
    def steps(launcher):
        return [text for _, text in launcher.report.steps]

    def test_nobody_to_answer_installs_and_says_so(self):
        for gui, tools in ((False, {}), (False, {"kdialog": 0}), (True, {}), (True, {"kdialog": 127}), (True, {"kdialog": 254, "zenity": 255})):
            with self.subTest(gui=gui, tools=tools):
                shutil.rmtree(os.path.join(self.home, "logs"), ignore_errors=True)
                launcher = self.launcher()
                installs, error = self.offer(launcher, tools=tools, gui=gui)
                installs.assert_called_once_with(self.NEWER)
                error.assert_not_called()
                self.assertEqual(self.logged(launcher), [self.AVAILABLE, self.NOBODY])
                self.assertEqual(self.steps(launcher), ["Update to version 1.2.3: nobody can answer the question, so it installs", self.UPDATED])
                self.assertFalse(launcher.report.has_problems)

    def test_the_player_answers(self):
        launcher = self.launcher()
        installs, _ = self.offer(launcher, tools={"zenity": 1}, gui=True)
        installs.assert_not_called()
        self.assertEqual(self.logged(launcher), [self.AVAILABLE, self.POSTPONED])
        self.assertEqual(self.steps(launcher), [])
        os.remove(launcher.log.path)
        launcher = self.launcher()
        installs, _ = self.offer(launcher, tools={"kdialog": 0}, gui=True)
        installs.assert_called_once_with(self.NEWER)
        self.assertEqual(self.logged(launcher), [self.AVAILABLE])
        self.assertEqual(self.steps(launcher), [self.UPDATED])

    def test_the_window_always_asks(self):
        for answer, installed in ((False, 0), (True, 1)):
            with self.subTest(answer=answer):
                shutil.rmtree(os.path.join(self.home, "logs"), ignore_errors=True)
                launcher = self.launcher()
                asked = []
                launcher.ui = types.SimpleNamespace(status=lambda text: None, notify=lambda text: None, error=lambda text: None,
                                                    ask=lambda text, default=False: asked.append(text) or answer)
                installs, _ = self.offer(launcher)
                self.assertEqual(installs.call_count, installed)
                self.assertEqual(len(asked), 1)
                self.assertEqual(self.POSTPONED in self.logged(launcher), not answer)

    def test_always_asks_nothing(self):
        launcher = self.launcher("always")
        installs, _ = self.offer(launcher, tools={"kdialog": 1}, gui=True)
        installs.assert_called_once_with(self.NEWER)
        self.assertEqual(self.steps(launcher), [self.UPDATED])

    def test_the_player_is_told_before_the_download(self):
        launcher = self.launcher("always")
        seen = []
        with mock.patch.object(launcher.ui, "notify", side_effect=seen.append):
            self.offer(launcher, install=lambda latest: seen.append("install"))
        self.assertEqual(seen, ["Updating to version 1.2.3. The game starts when it is done.", "install"])

    def test_a_notification_that_cannot_start_is_reported_and_stops_only_the_update(self):
        launcher = self.launcher("always")
        with mock.patch.object(launcher.ui, "notify", side_effect=OSError("cannot start")):
            installs, _ = self.offer(launcher)
        installs.assert_not_called()
        self.assertEqual([text for _, text in launcher.report.problems], ["The update to version 1.2.3 failed: cannot start"])

    def test_a_long_question_fits_a_dialog(self):
        launcher = self.launcher()
        notes = "• " + "a long line of release notes " * 20
        with mock.patch.object(launcher.ui, "answer", return_value=False) as answer:
            self.offer(launcher, {"version": "1.2.3", "notes": notes})
        text = answer.call_args.args[0]
        self.assertLessEqual(max(len(line) for line in text.split("\n")), 100)
        self.assertIn("Update now?", text)
        self.assertEqual(text.split().count("notes"), 20)

    def test_the_current_version_needs_nothing(self):
        launcher = self.launcher()
        installs, _ = self.offer(launcher, {"version": "1.2.2", "notes": ""})
        installs.assert_not_called()
        self.assertEqual(self.steps(launcher), [])

    def test_an_unfinished_update_is_offered_at_the_current_version(self):
        with open(os.path.join(self.home, ms2offline.UPDATE_MARKER), "w", encoding="utf-8") as f:
            f.write("update 1.2.2")
        latest = {"version": "1.2.2", "notes": ""}
        installs, _ = self.offer(self.launcher(), latest)
        installs.assert_called_once_with(latest)

    def test_a_failed_check_is_a_step_and_no_problem(self):
        launcher = self.launcher()
        installs, error = self.offer(launcher, latest={})
        installs.assert_not_called()
        error.assert_not_called()
        self.assertEqual(self.logged(launcher), ["update: online check skipped: offline"])
        self.assertEqual(self.steps(launcher), ["Update check skipped: offline"])
        self.assertFalse(launcher.report.has_problems)

    def test_a_failed_update_is_a_problem(self):
        launcher = self.launcher("always")
        _, error = self.offer(launcher, install=OSError("no space left"))
        self.assertEqual([text for _, text in launcher.report.problems], [self.FAILED])
        self.assertEqual(self.steps(launcher), ["PROBLEM: " + self.FAILED])
        self.assertEqual(self.logged(launcher), [self.AVAILABLE, "update: update to 1.2.3 failed: no space left"])
        error.assert_called_once_with("The update could not be installed (no space left). You can keep playing; it is tried again next time.")

    def test_debug_reports_off_means_no_report_and_no_failure(self):
        for auto_update, kwargs, errors in (("always", {"install": OSError("no space left")}, 1), ("always", {}, 0), ("ask", {}, 0), ("ask", {"latest": {}}, 0)):
            with self.subTest(auto_update=auto_update, kwargs=kwargs):
                launcher = self.launcher(auto_update)
                launcher.report = None
                _, error = self.offer(launcher, **kwargs)
                self.assertEqual(error.call_count, errors)

    def test_a_failed_update_reaches_the_report_file_and_the_upload(self):
        launcher = self.launcher("always")
        prepared = (os.path.join(self.home, "GameData"), None, None, None)
        with mock.patch.object(ms2offline, "prepare_content", return_value=prepared), \
                mock.patch.object(ms2offline.Client, "running_pids", return_value=[]), \
                mock.patch.object(ms2offline.Releases, "latest", return_value=self.NEWER), \
                mock.patch.object(launcher, "install_release", side_effect=OSError("no space left")), \
                mock.patch.object(launcher, "take_lock"), mock.patch.object(launcher, "start_up_reports"), \
                mock.patch.object(launcher, "locate_client", return_value=True), mock.patch.object(launcher, "claim_ports"), \
                mock.patch.object(launcher, "backup_save"), mock.patch.object(launcher, "start_servers"), \
                mock.patch.object(launcher, "install_client_patch"), mock.patch.object(launcher, "set_mod"), \
                mock.patch.object(launcher, "prepare_prefix"), \
                mock.patch.object(launcher, "launch_game", return_value=mock.MagicMock(returncode=0)), \
                mock.patch.object(launcher, "wait_for_game"), mock.patch.object(launcher, "wait_for_save", return_value="nothing"), \
                mock.patch.object(launcher, "offer_report") as offer, \
                mock.patch.object(launcher.ui, "status"), mock.patch.object(launcher.ui, "notify"), \
                mock.patch.object(launcher.ui, "error") as error:
            self.assertEqual(launcher.play(), 0)
        offer.assert_called_once()
        self.assertIn(self.FAILED, ms2offline.ProblemReport.for_upload(offer.call_args.args[0], []))
        self.assertTrue(error.call_args.args[0].startswith("Something went wrong during this session."))
        reports = os.path.join(self.home, "reports")
        with open(os.path.join(reports, os.listdir(reports)[0]), encoding="utf-8") as f:
            self.assertIn(self.FAILED, f.read())


class DownloadProgressTest(unittest.TestCase):
    """BT-172: a package download's progress is in launcher.log too, where a player with no terminal can read it afterwards."""

    def test_the_log_has_what_the_terminal_has(self):
        class Response(io.BytesIO):
            headers = {"Content-Length": str(10 << 20)}

        statuses, lines = [], []
        ui = types.SimpleNamespace(status=statuses.append)
        log = types.SimpleNamespace(write=lambda kind, text: lines.append(f"{kind}: {text}"))
        with tempfile.TemporaryDirectory() as tmp, mock.patch("urllib.request.urlopen", lambda request, timeout: Response(bytes(10 << 20))):
            target = os.path.join(tmp, "update.zip")
            ms2offline.Releases.download("https://github.com/x/update.zip", target, ui, "update 1.2.3", log)
            self.assertEqual(os.path.getsize(target), 10 << 20)
        self.assertEqual(statuses, [f"Downloading update 1.2.3... {n * 10}% ({n} MB)" for n in range(1, 11)])
        self.assertEqual(lines, [f"update: downloading update 1.2.3: {n * 10}% ({n} MB)" for n in range(1, 11)])

    def test_only_the_packages_go_to_the_log(self):
        latest = {"version": "1.2.3", "setup_url": "https://github.com/x/setup", "update_url": "https://github.com/x/update", "update_name": "update.zip",
                  "manifest_url": "https://github.com/x/list", "signature_url": "https://github.com/x/sig"}
        for marker, package in ((None, "update 1.2.3"), ("full", "release 1.2.3")):
            with self.subTest(marker=marker), tempfile.TemporaryDirectory() as home:
                for name, text in (("launcher.ini", "DebugReports=false\n"), ("VERSION", "1.2.2\n")) + ((ms2offline.UPDATE_MARKER, marker),) * bool(marker):
                    with open(os.path.join(home, name), "w", encoding="utf-8") as f:
                        f.write(text)
                calls = []

                def download(url, target, ui, label, log=None):
                    calls.append((label, log))
                    if label not in ("release.json", "release.json.sig"):
                        raise OSError("the network is gone")
                    with open(target, "w", encoding="utf-8") as f:
                        f.write(json.dumps({"version": "1.2.3"}))
                    return target

                launcher = ms2offline.Launcher(home)
                with mock.patch.multiple(ms2offline.Releases, download=download, verify_manifest=lambda data, sig: {}), self.assertRaises(OSError):
                    launcher.install_release(latest)
                self.assertEqual(calls, [("release.json", None), ("release.json.sig", None), (package, launcher.log)])


class WindowUpdateTest(unittest.TestCase):
    """ms2offline-gui.py's Check for updates (BT-189), without Qt: Window.update touches no widget."""

    @classmethod
    def setUpClass(cls):
        qt = types.ModuleType("PyQt6")
        qt.QtCore = types.SimpleNamespace(QObject=object, pyqtSignal=lambda *args: None)
        qt.QtGui = None
        qt.QtWidgets = types.SimpleNamespace(QMainWindow=object)
        spec = importlib.util.spec_from_file_location("ms2offline_gui", os.path.join(HERE, "ms2offline-gui.py"))
        cls.gui = importlib.util.module_from_spec(spec)
        with mock.patch.dict(sys.modules, {"PyQt6": qt}):
            spec.loader.exec_module(cls.gui)

    def check(self, online, marker=None, servers=(), answer=True):
        """Check for updates at version 1.2.2, with this version online. Returns the stand-in launcher and the release it saw."""
        latest = {"version": online, "notes": "Notes"}
        launcher = mock.MagicMock()
        launcher.ui.ask.return_value = answer
        with tempfile.TemporaryDirectory() as home:
            with open(os.path.join(home, "VERSION"), "w", encoding="utf-8") as f:
                f.write("1.2.2\n")
            if marker:
                with open(os.path.join(home, ms2offline.UPDATE_MARKER), "w", encoding="utf-8") as f:
                    f.write(marker)
            window = types.SimpleNamespace(home=home, launcher=launcher, run=lambda name, target: target())
            with mock.patch.object(ms2offline.Releases, "latest", return_value=latest), \
                    mock.patch.object(ms2offline.Ports, "own_server_pids", return_value=list(servers)):
                self.gui.Window.update(window)
        return launcher, latest

    def test_current_only_with_no_marker(self):
        launcher, _ = self.check("1.2.2")
        launcher.ui.info.assert_called_once_with("Version 1.2.2 is current.")
        launcher.ui.ask.assert_not_called()
        launcher.install_release.assert_not_called()

    def test_a_marker_is_never_current_and_says_how_to_finish(self):
        for online, marker in (("1.2.2", "update 1.2.2"), ("1.2.2", "full"), ("1.2.3", "update 1.2.3")):
            with self.subTest(online=online, marker=marker):
                launcher, latest = self.check(online, marker)
                launcher.ui.info.assert_not_called()
                launcher.ui.ask.assert_called_once_with(ms2offline.UPDATE_UNFINISHED + "\n\nInstall it now?")
                launcher.install_release.assert_called_once_with(latest)

    def test_no_installs_nothing(self):
        launcher, _ = self.check("1.2.2", "full", answer=False)
        launcher.ui.info.assert_not_called()
        launcher.install_release.assert_not_called()

    def test_a_newer_release_is_asked_as_before(self):
        launcher, latest = self.check("1.2.3")
        launcher.ui.ask.assert_called_once_with("Version 1.2.3 is available (you have 1.2.2).\n\nNotes\n\nInstall it now?")
        launcher.install_release.assert_called_once_with(latest)

    def test_running_servers_stop_it(self):
        launcher, _ = self.check("1.2.2", "full", servers=[1])
        launcher.ui.error.assert_called_once_with("The servers are running. Stop them first.")
        launcher.ui.ask.assert_not_called()


class KeyAndProfileTest(unittest.TestCase):
    def test_public_key_matches_windows(self):
        with open(os.path.join(HERE, "..", "Launcher", "UpdateKey.cs"), encoding="utf-8") as f:
            windows = re.search(r'PublicKey\s*=\s*"([^"]+)"', f.read()).group(1)
        self.assertEqual(ms2offline.UPDATE_PUBLIC_KEY, windows)

    def test_profile_rules(self):
        for name in ("", "Second run", "a-b_c", "Café"):
            self.assertIsNone(ms2offline.profile_error(name), name)
        for name in ("x" * 33, "a/b", "..", "backups", "BACKUPS", "ugc", "UGC"):
            self.assertIsNotNone(ms2offline.profile_error(name), name)

    def test_the_uploaded_files_folder_is_no_save_name(self):
        with tempfile.TemporaryDirectory() as home:
            for folder in ("ugc", "backups", "Second"):
                os.makedirs(os.path.join(home, "SaveData", folder))
            names, unreadable = ms2offline.ProblemReport.names(types.SimpleNamespace(home=home))
        self.assertIsNone(unreadable)
        self.assertIn("Second", names)
        self.assertNotIn("ugc", names)
        self.assertNotIn("backups", names)

    def test_profile_save_dir(self):
        with tempfile.TemporaryDirectory() as home:
            for value, expected in (("Second", os.path.join(home, "SaveData", "Second")), ("../x", os.path.join(home, "SaveData"))):
                with open(os.path.join(home, "launcher.ini"), "w", encoding="utf-8") as f:
                    f.write(f"Profile={value}\n")
                self.assertEqual(ms2offline.Settings(home).save_dir, expected)


class NewerCopyTest(unittest.TestCase):
    """newer_copy, the start that follows it, and what the package holds (DU-03, BT-171). Nothing here ran on a Deck."""

    def home(self, version):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        linux = os.path.join(tmp.name, "linux")
        os.makedirs(linux)
        for name in ("ms2offline.py", "ms2offline-gui.py"):
            with open(os.path.join(linux, name), "w", encoding="utf-8") as f:
                f.write(f'TITLE = "x"\nLAUNCHER_VERSION = "{version}"  # a comment\n')
        return tmp.name

    def test_a_newer_copy_runs(self):
        home = self.home("99.0.0")
        for script in ("ms2offline.py", "ms2offline-gui.py"):
            self.assertEqual(ms2offline.newer_copy(home, script), os.path.join(home, "linux", script))

    def test_an_equal_or_older_copy_does_not(self):
        self.assertIsNone(ms2offline.newer_copy(self.home(ms2offline.LAUNCHER_VERSION), "ms2offline.py"))
        self.assertIsNone(ms2offline.newer_copy(self.home("1.0.0"), "ms2offline.py"))

    def test_no_copy_no_script_and_no_version(self):
        with tempfile.TemporaryDirectory() as empty:
            self.assertIsNone(ms2offline.newer_copy(empty, "ms2offline.py"))
        home = self.home("99.0.0")
        os.remove(os.path.join(home, "linux", "ms2offline-gui.py"))
        self.assertIsNone(ms2offline.newer_copy(home, "ms2offline-gui.py"))
        with open(os.path.join(home, "linux", "ms2offline.py"), "w", encoding="utf-8") as f:
            f.write("half a file")
        self.assertIsNone(ms2offline.newer_copy(home, "ms2offline.py"))

    def test_an_unfinished_update_runs_no_copy(self):
        home = self.home("99.0.0")
        with open(os.path.join(home, ms2offline.UPDATE_MARKER), "w", encoding="utf-8") as f:
            f.write("full")
        self.assertIsNone(ms2offline.newer_copy(home, "ms2offline.py"))

    def test_main_runs_the_newer_copy_with_the_same_arguments(self):
        home = self.home("99.0.0")
        with mock.patch.object(ms2offline.os, "execv", side_effect=SystemExit(0)) as execv:
            with self.assertRaises(SystemExit):
                ms2offline.main(["--home", home, "status"])
        self.assertEqual(execv.call_args.args, (sys.executable, [sys.executable, os.path.join(home, "linux", "ms2offline.py"), "--home", home, "status"]))

    def test_the_package_holds_linux_in_lf_with_no_tests(self):
        with tempfile.TemporaryDirectory() as stage:
            package_release.copy_linux(stage, ms2offline.LAUNCHER_VERSION)
            self.assertEqual(sorted(os.listdir(os.path.join(stage, "linux"))), sorted(package_release.LINUX_FILES))
            for name in package_release.LINUX_FILES:
                with open(os.path.join(stage, "linux", name), "rb") as f:
                    self.assertNotIn(b"\r", f.read(), name)
        self.assertTrue(ms2offline.Releases.keep("linux/ms2offline.py"))

    def test_a_package_of_another_version_is_refused(self):
        with tempfile.TemporaryDirectory() as stage:
            with self.assertRaises(SystemExit):
                package_release.copy_linux(stage, "99.0.0")


class ProgramFilesTest(unittest.TestCase):
    """ms2offline verify, the program part: program_files_changed against files.json."""

    def test_missing_and_changed_files_are_named(self):
        home = tempfile.mkdtemp(prefix="ms2-program-")
        self.addCleanup(shutil.rmtree, home)
        os.makedirs(os.path.join(home, "Maple2"))
        files = {"Maple2/a.dll": b"a", "Maple2/b.dll": b"b", "VERSION": b"1.2.3"}
        for rel, data in files.items():
            with open(os.path.join(home, *rel.split("/")), "wb") as f:
                f.write(data)
        listed = {rel: hashlib.sha256(data).hexdigest() for rel, data in files.items()}
        listed["Maple2/c.dll"] = hashlib.sha256(b"c").hexdigest()
        listed["MapleStory 2 Offline.exe"] = hashlib.sha256(b"exe").hexdigest()  # not installed on Linux
        with open(os.path.join(home, "files.json"), "w", encoding="utf-8") as f:
            json.dump(listed, f)
        with open(os.path.join(home, "Maple2", "b.dll"), "wb") as f:
            f.write(b"changed")
        with open(os.path.join(home, "VERSION"), "wb") as f:
            f.write(b"1.2.4")  # rewritten by an update: skipped

        self.assertEqual(ms2offline.program_files_changed(home), ["MODIFIED  Maple2/b.dll", "MISSING   Maple2/c.dll"])

    def test_without_a_file_list_nothing_is_named(self):
        home = tempfile.mkdtemp(prefix="ms2-program-")
        self.addCleanup(shutil.rmtree, home)
        self.assertEqual(ms2offline.program_files_changed(home), [])


if __name__ == "__main__":
    unittest.main()