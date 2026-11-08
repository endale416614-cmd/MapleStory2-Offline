#!/usr/bin/env python3
"""Tests for the Linux launcher's client patch checks (BT-216): Client.missing_original, Client.proxy_state and the stop in
Launcher.play, as ClientPatchTests on Windows."""

import os
import shutil
import sys
import tempfile
import types
import unittest
from unittest import mock

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.modules.setdefault("fcntl", types.ModuleType("fcntl"))
import ms2offline  # noqa: E402

Client = ms2offline.Client


class MissingOriginalTest(unittest.TestCase):
    """The patch's NxCharacter64.dll without the game's own beside it as NxCharacter64.dll.bak, or no NxCharacter64.dll at
    all, stops the game before the servers, and nothing in the game folder changes."""

    def setUp(self):
        self.home = tempfile.mkdtemp(prefix="ms2-patch-")
        self.client = os.path.join(self.home, "MS2", "x64")
        self.patch = os.path.join(self.home, "client-patch")
        os.makedirs(self.client)
        os.makedirs(self.patch)
        self.put(self.patch, "NxCharacter64.dll", b"the patch's proxy")
        self.put(self.patch, "AgarciumClient.dll", b"the patch")
        self.put(self.client, "MapleStory2.exe", b"the game")

    def tearDown(self):
        shutil.rmtree(self.home, ignore_errors=True)

    @staticmethod
    def put(folder, name, data):
        with open(os.path.join(folder, name), "wb") as f:
            f.write(data)

    def folder(self):
        """Every file of the game folder with its bytes, to show that nothing in it changed."""
        files = {}
        for name in sorted(os.listdir(self.client)):
            with open(os.path.join(self.client, name), "rb") as f:
                files[name] = f.read()
        return files

    def test_the_patch_without_the_games_own_stops(self):
        self.put(self.client, "NxCharacter64.dll", b"the patch's proxy")
        self.put(self.client, "AgarciumClient.dll", b"the patch")
        before = self.folder()
        message = Client.missing_original(self.client, self.patch)
        self.assertTrue(message.startswith(f"The game's own NxCharacter64.dll is missing from the game folder:\n{self.client}\n\n"), message)
        self.assertTrue(message.endswith("Never copy the patch files by hand. If it still fails, post the report on Discord."), message)
        self.assertEqual(Client.proxy_state(self.client, self.patch), "patched, without the game's own NxCharacter64.dll.bak; see TROUBLESHOOTING.txt [T9]")
        self.assertEqual(self.folder(), before, "nothing in the game folder changes")
        self.put(self.client, "NxCharacter64.dll", b"an earlier build of the patch, which loads NxCharacter64.dll.bak")
        self.assertIsNotNone(Client.missing_original(self.client, self.patch), "an earlier build of the patch")
        os.remove(os.path.join(self.client, "NxCharacter64.dll"))
        self.assertIsNotNone(Client.missing_original(self.client, self.patch), "no NxCharacter64.dll at all")
        self.assertEqual(Client.proxy_state(self.client, self.patch), "missing, without the game's own NxCharacter64.dll.bak; see TROUBLESHOOTING.txt [T9]")

    def test_the_games_own_or_its_backup_starts(self):
        self.put(self.client, "NxCharacter64.dll", b"the game's own")
        self.assertIsNone(Client.missing_original(self.client, self.patch), "before the first start")
        self.assertEqual(Client.proxy_state(self.client, self.patch), "modified")  # a made-up file, not the real game's
        self.put(self.client, "NxCharacter64.dll", b"the patch's proxy")
        self.put(self.client, "NxCharacter64.dll.bak", b"the game's own")
        self.assertIsNone(Client.missing_original(self.client, self.patch), "installed by the launcher")
        self.assertEqual(Client.proxy_state(self.client, self.patch), "patched")
        os.remove(os.path.join(self.client, "NxCharacter64.dll"))
        self.assertIsNone(Client.missing_original(self.client, self.patch), "the backup alone: installing puts the patch beside it")
        self.assertIsNone(Client.missing_original(self.home, self.patch), "not a game folder")

    def test_play_stops_before_the_servers(self):
        self.put(self.client, "NxCharacter64.dll", b"the patch's proxy")
        with open(os.path.join(self.home, "launcher.ini"), "w", encoding="utf-8") as f:
            f.write("AutoUpdate=off\nDebugReports=false\nClientDir=" + self.client + "\n")
        launcher = ms2offline.Launcher(self.home)
        before = self.folder()
        prepared = (os.path.join(self.home, "GameData"), None, None, None)
        with mock.patch.object(Client, "running_pids", return_value=[]), \
                mock.patch.object(launcher, "take_lock"), mock.patch.object(launcher, "show_report_notice"), \
                mock.patch.object(launcher, "start_up_reports"), mock.patch.object(launcher, "locate_client", return_value=True), \
                mock.patch.object(launcher, "claim_ports") as claim, mock.patch.object(launcher, "backup_save"), \
                mock.patch.object(ms2offline, "prepare_content", return_value=prepared), \
                mock.patch.object(launcher, "start_servers", side_effect=RuntimeError("the test stops here")) as start, \
                mock.patch.object(launcher.ui, "status"), mock.patch.object(launcher.ui, "error") as error:
            self.assertEqual(launcher.play(), 1)
        claim.assert_not_called()
        start.assert_not_called()
        self.assertTrue(error.call_args.args[0].startswith("The game's own NxCharacter64.dll is missing from the game folder:"))
        self.assertEqual(self.folder(), before, "nothing in the game folder changes")


if __name__ == "__main__":
    unittest.main()
