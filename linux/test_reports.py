#!/usr/bin/env python3
"""Tests for the Linux launcher's problem reports (ProblemReport, a port of Launcher/ProblemReport.cs)."""

import datetime
import gzip
import json
import os
import re
import sqlite3
import sys
import tempfile
import time
import types
import unittest
import urllib.error
import uuid
from unittest import mock

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.modules.setdefault("fcntl", types.ModuleType("fcntl"))
import ms2offline  # noqa: E402

PR = ms2offline.ProblemReport

REPORT = "\n".join([
    "MapleStory 2 (Offline) - play session report (Linux)",
    "=" * 50,
    "",
    "Steps",
    "-----",
    "2026-10-21 12:00:00  Launcher started",
    "",
    "Problems",
    "--------",
    "None.",
    "",
    "Game server: 120 log lines, 6 warnings/errors (last 30 shown)",
    "-" * 40,
    "12:00:02.000  GameSession [WRN] <1> Something odd",
    "12:00:03.000  GameSession [ERR] <1> [socket_exception] [SendOp: 0x21] [Offset: 4] [Hint: x] [LastPacket:",
    "12:00:04.000  GameSession [ERR] <1> [render exception] raw client debug text",
    "12:00:05.000  GameStorage [WRN] <1> Profile conflict: Motto: proposed='my secret motto'",
    "12:00:06.000  GameSession [ERR] <1> dump 4D 00 65 00 69 00 00 00 01 02",
    "12:00:07.000  GameSession [INF] <1> Client Log: hello",
    "",
    "Game client (Proton) log: last 40 of 300 lines",
    "-" * 40,
    "a client line",
    "-- a client line that looks like a header ---",
    "another client line",
    "a line Proton prints before a separator",
    "----------",
    "client text after the separator --password=secret",
])


class FakeResponse:
    def __init__(self, status):
        self.status = status

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        return False


class FakeOpener:
    """Answers each request with the next status (or raises the next exception) and records it."""

    def __init__(self, answers):
        self.answers = list(answers)
        self.requests = []

    def __call__(self, request, timeout=None):
        self.requests.append(request)
        answer = self.answers.pop(0)
        if isinstance(answer, BaseException):
            raise answer
        if answer >= 400:
            raise urllib.error.HTTPError(request.full_url, answer, "answer", {}, None)
        return FakeResponse(answer)


def save(path, character, account):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    db = sqlite3.connect(path)
    db.execute('CREATE TABLE "character" (Name TEXT)')
    db.execute("CREATE TABLE account (Username TEXT)")
    db.execute('INSERT INTO "character" VALUES (?)', (character,))
    db.execute("INSERT INTO account VALUES (?)", (account,))
    db.commit()
    db.close()


class TextRulesTest(unittest.TestCase):
    def test_drops_what_the_game_client_wrote(self):
        text = PR.for_upload(REPORT, [])
        for kept in ("Launcher started", "Something odd", "Game server: 120 log lines"):
            self.assertIn(kept, text)
        for dropped in ("SendOp", "LastPacket", "raw client debug", "secret motto", "4D 00", "Client Log", "hello",
                        "Game client (Proton) log", "a client line", "another client line", "separator", "--password"):
            self.assertNotIn(dropped, text)

    def test_drops_before_the_names_are_replaced(self):
        # A player named "Game" or "Client" must not change the markers the drop rules look for.
        text = PR.for_upload(REPORT, ["Game", "Client", "Log"])
        self.assertNotIn("a client line", text)
        self.assertNotIn("hello", text)

    def test_replaces_names_as_whole_words(self):
        text = PR.for_upload("bob joined\nBOB left\n[Bob]\nBobby is here\noffline user\na", ["Bob", "offline", "a", " "])
        for expected in ("<name> joined", "<name> left", "[<name>]", "Bobby is here", "offline user"):
            self.assertIn(expected, text)
        self.assertTrue(text.endswith("\na"))
        self.assertIsNone(re.search(r"(?i)(?<!\w)bob(?!\w)", text))

    def test_cuts_a_long_report(self):
        text = PR.for_upload("x" * 500_000, [])
        self.assertEqual(len(text), ms2offline.REPORT_MAX_CHARS + len(ms2offline.REPORT_CUT))
        self.assertTrue(text.endswith(ms2offline.REPORT_CUT))

    def test_redacts_home_user_computer_and_machine_id(self):
        with mock.patch("getpass.getuser", return_value="alice"), mock.patch("socket.gethostname", return_value="alice-pc.lan"), \
                mock.patch("os.path.expanduser", return_value="/home/alice"):
            text = PR.redact('/home/alice/Games alice ALICE-PC alice-pc.lan alicesmith machineId: "0123456789abcdef" '
                             "Machine: 0123abcd-4567")
        self.assertEqual(text, '~/Games <redacted> <redacted> <redacted> alicesmith machineId: "<redacted>" Machine: <redacted>')

    def test_note_is_redacted_named_and_cut(self):
        with mock.patch("getpass.getuser", return_value="alice"):
            note = PR.note("Bob and alice " + "y" * 3000, ["Bob"])
        self.assertTrue(note.startswith("<name> and <redacted> y"))
        self.assertEqual(len(note), ms2offline.REPORT_NOTE_CHARS)

    def test_build_writes_the_gzip_json_of_schema_1(self):
        report_id = uuid.uuid4()
        created = datetime.datetime(2026, 10, 21, 10, 0, 0, tzinfo=datetime.timezone.utc)
        payload = json.loads(gzip.decompress(PR.build(report_id, created, "manual", "0123abcd", "1.2.0", "Bob crashed", None, ["Bob"])))
        self.assertEqual(list(payload), ["schema", "reportId", "installId", "createdUtc", "kind", "platform", "launcher", "note", "report"])
        self.assertEqual(payload["schema"], 1)
        self.assertEqual(payload["reportId"], str(report_id))
        self.assertEqual(payload["createdUtc"], "2026-10-21T10:00:00Z")
        self.assertEqual(payload["platform"], "linux")
        self.assertEqual(payload["launcher"], "1.2.0")
        self.assertIsNone(payload["note"])
        self.assertEqual(payload["report"], "<name> crashed")


class NamesTest(unittest.TestCase):
    def test_reads_every_save_and_says_which_one_cannot_be_read(self):
        with tempfile.TemporaryDirectory() as home:
            settings = types.SimpleNamespace(home=home)
            self.assertEqual(PR.names(settings)[1], None, "no save yet")
            save_data = os.path.join(home, "SaveData")
            save(os.path.join(save_data, "game-server.db"), "Mei", "player1")
            save(os.path.join(save_data, "Alt Save", "game-server.db"), "Rook", "offline")
            os.makedirs(os.path.join(save_data, "New Game"))
            sqlite3.connect(os.path.join(save_data, "New Game", "game-server.db")).close()  # a save without tables yet
            names, unreadable = PR.names(settings)
            self.assertIsNone(unreadable)
            for expected in ("Mei", "player1", "Rook", "Alt Save", "New Game"):
                self.assertIn(expected, names)
            os.makedirs(os.path.join(save_data, "Broken"))
            broken = os.path.join(save_data, "Broken", "game-server.db")
            with open(broken, "w") as f:
                f.write("not a database at all, just text that is long enough to look like a file")
            self.assertEqual(PR.names(settings)[1], broken)
            self.assertEqual(PR.save_label(settings, broken), 'save "Broken"')
            self.assertEqual(PR.save_label(settings, os.path.join(save_data, "game-server.db")), "main save")
            self.assertEqual(PR.save_label(settings, save_data), "save folder")

    def test_a_folder_that_cannot_be_listed_blocks_the_report(self):
        with tempfile.TemporaryDirectory() as home:
            locked = os.path.join(home, "SaveData", "Locked")
            os.makedirs(locked)

            def walk(top, onerror=None):
                onerror(PermissionError(13, "denied", locked))
                return iter(())

            with mock.patch("os.walk", walk):
                self.assertEqual(PR.names(types.SimpleNamespace(home=home))[1], locked)


class ConsentTest(unittest.TestCase):
    """Launcher.offer_report: nothing is queued without SendReports=always or a yes to the question."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.home = self.tmp.name
        self.sent = []
        patches = [mock.patch.dict(os.environ, {"MS2OFFLINE_REPORT_URL": "http://test.local"}),
                   mock.patch.object(PR, "send", lambda *args, **kwargs: self.sent.append(args) or 0)]
        for patch in patches:
            patch.start()
            self.addCleanup(patch.stop)

    def tearDown(self):
        self.tmp.cleanup()

    def launcher(self, send_reports, answer=False):
        with open(os.path.join(self.home, "launcher.ini"), "w") as f:
            f.write(f"SendReports={send_reports}\n")
        launcher = ms2offline.Launcher.__new__(ms2offline.Launcher)  # no servers, no lock: only the report parts
        launcher.home, launcher.settings = self.home, ms2offline.Settings(self.home)
        launcher.log = types.SimpleNamespace(write=lambda *args: None)
        launcher.ui = types.SimpleNamespace(questions=[], infos=[])
        launcher.ui.ask = lambda text, default=False: launcher.ui.questions.append((text, default)) or answer
        launcher.ui.info = launcher.ui.infos.append
        return launcher

    def queued(self):
        folder = os.path.join(self.home, "report-queue")
        return sorted(os.listdir(folder)) if os.path.isdir(folder) else []

    def test_never_and_no_queue_nothing(self):
        never = self.launcher("never")
        never.offer_report(REPORT)
        self.assertEqual((never.ui.questions, self.queued()), ([], []))
        no = self.launcher("ask", answer=False)
        no.offer_report(REPORT)
        self.assertEqual(len(no.ui.questions), 1)
        self.assertFalse(no.ui.questions[0][1], "the question's default is No")
        self.assertTrue(os.path.exists(os.path.join(self.home, "reports", "to-send.txt")))
        self.assertEqual(self.queued(), [])

    def test_yes_and_always_queue_one_report_each(self):
        self.launcher("ask", answer=True).offer_report(REPORT)
        always = self.launcher("always")
        always.offer_report(REPORT)
        self.assertEqual(always.ui.questions, [])
        self.assertEqual(len(self.queued()), 2)
        self.assertTrue(all(name.endswith(".problem.json.gz") for name in self.queued()))
        self.assertEqual(len(self.sent), 2)

    def test_an_unreadable_save_queues_nothing_and_says_so(self):
        os.makedirs(os.path.join(self.home, "SaveData"))
        with open(os.path.join(self.home, "SaveData", "game-server.db"), "w") as f:
            f.write("not a database at all, just text that is long enough to look like a file")
        launcher = self.launcher("always")
        launcher.offer_report(REPORT)
        self.assertEqual(self.queued(), [])
        self.assertEqual(len(launcher.ui.infos), 1)
        self.assertIn("main save", launcher.ui.infos[0])


class NoticeTest(unittest.TestCase):
    """Launcher.show_report_notice (1.2.1): once, only to a player who never chose, and never answered unseen."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.home = self.tmp.name
        patch = mock.patch.dict(os.environ, {"MS2OFFLINE_REPORT_URL": "http://test.local"})
        patch.start()
        self.addCleanup(patch.stop)

    def tearDown(self):
        self.tmp.cleanup()

    def launcher(self, ini, answer=True):
        with open(os.path.join(self.home, "launcher.ini"), "w") as f:
            f.write(ini)
        launcher = ms2offline.Launcher.__new__(ms2offline.Launcher)  # no servers, no lock: only the notice
        launcher.home, launcher.settings = self.home, ms2offline.Settings(self.home)
        launcher.ui = types.SimpleNamespace(gui=True, tty=False, questions=[], errors=[])  # the window's UI
        launcher.ui.ask = lambda text, default=False: launcher.ui.questions.append(default) or answer
        launcher.ui.error = launcher.ui.errors.append
        return launcher

    def written(self):
        with open(os.path.join(self.home, "launcher.ini"), encoding="utf-8") as f:
            return f.read()

    def test_yes_keeps_reports_automatic_and_no_asks(self):
        yes = self.launcher("")
        self.assertEqual(yes.settings.send_reports, "ask", "nothing goes automatically before the answer")
        yes.show_report_notice()
        self.assertEqual((yes.ui.questions, yes.settings.send_reports, yes.settings.report_notice_pending), ([True], "always", False))
        self.assertIn("SendReports=always", self.written())
        self.assertIn("ReportNotice=1", self.written())
        no = self.launcher("", answer=False)
        no.show_report_notice()
        self.assertEqual(no.settings.send_reports, "ask")
        self.assertIn("SendReports=ask", self.written())

    def test_a_written_choice_gets_no_notice(self):
        for choice in ("ask", "never", "always"):
            launcher = self.launcher(f"SendReports={choice}\n")
            launcher.show_report_notice()
            self.assertEqual((launcher.ui.questions, launcher.settings.send_reports), ([], choice))

    def test_no_notice_when_nobody_can_answer(self):
        launcher = self.launcher("")
        launcher.ui = ms2offline.UI.__new__(ms2offline.UI)
        launcher.ui.gui, launcher.ui.tty = True, False  # a desktop without kdialog or zenity, no terminal
        with mock.patch("shutil.which", lambda name: None):
            launcher.show_report_notice()
        self.assertEqual((launcher.settings.send_reports, launcher.settings.report_notice_pending), ("ask", True))
        self.assertNotIn("ReportNotice", self.written())

    def test_the_notice_fits_a_dialog(self):
        wrapped = ms2offline.wrap_text(ms2offline.REPORT_NOTICE_TEXT)
        self.assertLessEqual(max(len(line) for line in wrapped.split("\n")), 100)
        self.assertEqual(wrapped.split(), ms2offline.REPORT_NOTICE_TEXT.split())

    def test_a_written_choice_in_any_case_is_kept(self):
        launcher = self.launcher("sendreports=never\n")
        launcher.show_report_notice()
        self.assertEqual((launcher.ui.questions, launcher.settings.send_reports), ([], "never"))

    def test_the_launcher_has_its_settings_and_ui(self):
        launcher = ms2offline.Launcher(self.home)
        self.assertTrue(launcher.settings.report_notice_pending)
        self.assertIsInstance(launcher.ui, ms2offline.UI)


class DialogDefaultTest(unittest.TestCase):
    def ask(self, tool, default):
        ui = ms2offline.UI.__new__(ms2offline.UI)
        ui.gui, ui.tty = True, False
        runs = []
        with mock.patch("shutil.which", lambda name: name if name == tool else None), \
                mock.patch("subprocess.run", lambda args, check: runs.append(args) or types.SimpleNamespace(returncode=1)):
            ui.ask("Send it?", default=default)
        return runs[0]

    def test_no_is_the_default_unless_asked_for(self):
        self.assertIn("--warningyesno", self.ask("kdialog", False))
        self.assertIn("--yesno", self.ask("kdialog", True))
        zenity = self.ask("zenity", False)
        self.assertIn("--default-cancel", zenity)
        self.assertIn("--no-markup", zenity)
        self.assertNotIn("--default-cancel", self.ask("zenity", True))


class QueueTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.folder = self.tmp.name

    def tearDown(self):
        self.tmp.cleanup()

    def write(self, name, mtime):
        path = os.path.join(self.folder, name)
        with open(path, "w") as f:
            f.write(name)
        os.utime(path, (mtime, mtime))
        return path

    def test_pending_keeps_the_newest_20_within_30_days_oldest_first(self):
        now = time.time()
        reports = [self.write(f"{i:02}.problem.json.gz", now - (22 - i) * 3600) for i in range(22)]
        old = self.write("old.problem.json.gz", now - 31 * 86400)
        leftover = self.write("x.problem.json.gz.part", now - 2 * 3600)
        writing = self.write("y.problem.json.gz.part", now)
        self.assertEqual(PR.pending(self.folder, now), reports[2:])
        self.assertFalse(os.path.exists(reports[0]) or os.path.exists(reports[1]) or os.path.exists(old) or os.path.exists(leftover))
        self.assertTrue(os.path.exists(writing))

    def test_enqueue_names_the_kind_and_withdraw_takes_back_only_those_kinds(self):
        report_id = uuid.uuid4()
        manual = PR.enqueue(self.folder, report_id, "manual", b"\x01\x02", time.time())
        PR.enqueue(self.folder, uuid.uuid4(), "problem", b"\x03", time.time())
        PR.enqueue(self.folder, uuid.uuid4(), "crash", b"\x04", time.time())
        self.assertEqual(os.path.basename(manual), f"{report_id}.manual.json.gz")
        PR.withdraw(self.folder, ["problem", "crash"])
        self.assertEqual(PR.pending(self.folder, time.time()), [manual])

    def test_send_deletes_sent_and_rejected_reports_and_stops_at_other_answers(self):
        now = time.time()
        paths = [self.write(f"{name}.problem.json.gz", now - 60 * (5 - i)) for i, name in enumerate("abcd")]
        opener = FakeOpener([201, 400, 413, 503])
        sent = set()
        self.assertEqual(PR.send(self.folder, "http://test.local/", opener, now, sent), 1)
        self.assertEqual(sent, {paths[0]})
        self.assertEqual([os.path.exists(p) for p in paths], [False, False, False, True])
        self.assertEqual(len(opener.requests), 4)
        self.assertEqual(opener.requests[0].full_url, "http://test.local/v1/report")
        self.assertEqual(opener.requests[0].get_header("Content-encoding"), "gzip")
        self.assertEqual(opener.requests[0].data, b"a.problem.json.gz")

    def test_send_keeps_everything_when_the_service_is_busy_or_unreachable(self):
        now = time.time()
        paths = [self.write(f"{name}.problem.json.gz", now - 60 * (3 - i)) for i, name in enumerate("ab")]
        busy = FakeOpener([503, 201])
        self.assertEqual(PR.send(self.folder, "http://test.local", busy, now), 0)
        self.assertEqual(len(busy.requests), 1)
        for failure in (urllib.error.URLError("down"), TimeoutError("slow"), OSError("reset")):
            self.assertEqual(PR.send(self.folder, "http://test.local", FakeOpener([failure]), now), 0)
        self.assertTrue(all(os.path.exists(p) for p in paths))
        nobody = FakeOpener([201])
        self.assertEqual(PR.send(self.folder, None, nobody, now), 0)
        self.assertEqual(nobody.requests, [])


class SettingsTest(unittest.TestCase):
    def test_send_reports_comes_only_from_the_players_launcher_ini(self):
        with tempfile.TemporaryDirectory() as home:
            with open(os.path.join(home, "defaults.ini"), "w") as f:
                f.write("SendReports=always\n")
            self.assertEqual(ms2offline.Settings(home).send_reports, "ask")
            with open(os.path.join(home, "launcher.ini"), "w") as f:
                f.write("SendReports=no\n")
            self.assertEqual(ms2offline.Settings(home).send_reports, "never")
            settings = ms2offline.Settings(home)
            first = settings.install_id
            self.assertTrue(first and first != "unknown")
            self.assertEqual(settings.install_id, first)

    def test_default_send_reports(self):
        cases = {(None, 0): "ask", (None, 1): "always", ("", 1): "always", ("never", 1): "never", ("never", 0): "never",
                 ("ask", 1): "ask", ("always", 0): "always"}
        for (written, notice), expected in cases.items():
            self.assertEqual(ms2offline.Settings.default_send_reports(written, notice), expected, (written, notice))

    def test_needs_report_notice(self):
        cases = {(None, 0): True, (None, 1): False, ("never", 0): False, ("ask", 0): False}
        for (written, notice), expected in cases.items():
            self.assertEqual(ms2offline.Settings.needs_report_notice(written, notice), expected, (written, notice))

    def test_parse_and_checkbox(self):
        cases = {None: "ask", "": "ask", " Always ": "always", "yes": "always", "NEVER": "never", "off": "never", "maybe": "ask"}
        for value, expected in cases.items():
            self.assertEqual(ms2offline.Settings.parse_send_reports(value), expected, value)
        after = ms2offline.Settings.send_reports_after_checkbox
        self.assertEqual([after("ask", True), after("never", True), after("always", False), after("never", False), after("ask", False)],
                         ["always", "always", "ask", "never", "ask"])

    def test_launcher_ini_with_report_notice(self):
        with tempfile.TemporaryDirectory() as home:
            with open(os.path.join(home, "launcher.ini"), "w") as f:
                f.write("ReportNotice=1\n")
            settings = ms2offline.Settings(home)
            self.assertEqual((settings.send_reports, settings.report_notice_pending), ("always", False))


if __name__ == "__main__":
    unittest.main()
