#!/usr/bin/env python3
"""MapleStory 2 Offline for Linux.

A port of the Windows launcher of https://github.com/endale416614-cmd/MapleStory2-Offline: the four Maple2
servers run natively on a private .NET runtime, the game client runs under Proton (umu-run), and the client
patch, mods, settings, save backups, logs and session reports work the same way as on Windows.

Program folder (default ~/Games/MapleStory2-Offline, override with MS2OFFLINE_HOME):
  Maple2/        the servers (from the project's release package)      GameData/   maple-data.db (release)
  client-patch/  NxCharacter64.dll + AgarciumClient.dll (release)       client-mods/ XML mods (release)
  dotnet/        private .NET 8 runtime for the servers                 prefix/     the Proton prefix of the game
  SaveData/      your save (game-server.db) + backups/                  logs/ reports/ updates/
  defaults.ini   the release's defaults                                 launcher.ini  your own settings

Commands:
  play            start the servers, the game, wait, save, stop everything (the default)
  stop            stop servers of this installation that are still running
  status          what is installed, where the game is, what is running
  install         first install, or a clean reinstall, from the latest release (or --from <Setup.exe|zip>)
  update          install a newer release if there is one (the Play command can do this too: AutoUpdate)
  client          show, set (--dir) or install from Steam (--steam) the game client
  prefix          create or refresh the Proton prefix of the game
  report          write a session report from the latest logs now
  report-problem  send the developer a problem report with your own words (--note), when this build has the service
  diagnostics     zip the logs and reports for a bug report (--include-save adds your save)
  unpatch         put the game's own NxCharacter64.dll back
"""
import argparse
import base64
import ctypes
import datetime
import errno
import getpass
import gzip
import hashlib
import io
import json
import os
import queue
import re
import shutil
import signal
import socket
import sqlite3
import struct
import subprocess
import sys
import tempfile
import textwrap
import threading
import time
import urllib.error
import urllib.request
import uuid
import zipfile

# Handle platform-specific imports
try:
    import fcntl
except ImportError:
    # Windows doesn't have fcntl, so we'll use a mock or alternative approach
    fcntl = None

TITLE = "MapleStory 2 (Offline)"
APP_NAME = "MapleStory 2 Offline"
REPOSITORY = "endale416614-cmd/MapleStory2-Offline"
STEAM_APP_ID = "560380"
GAME_EXE = "MapleStory2.exe"
PROXY_DLL = "NxCharacter64.dll"
PATCH_DLL = "AgarciumClient.dll"
ORIGINAL_PROXY_SHA256 = "e742ca44b24aad0787d73c5da530eed16fa945bd539b1ab00ed0408ab69d0949"
SERVERS = ["World", "Login", "Web", "Game"]
PORTS = [4000, 20001, 20002, 20003, 21000, 21001, 21002, 21003]
LOGIN_PORT, WEB_PORT, WORLD_GRPC_PORT = 20001, 4000, 21001
SETUP_MAGIC = b"MS2OFFLINE-PAYLOAD-1"
# Windows-only parts of the release package, never installed here.
PACKAGE_SKIP = ("MapleStory 2 Offline.exe", "dotnet/", "README.txt")
# Public half of the update signing key: a copy of UpdateKey.PublicKey in Launcher/UpdateKey.cs (a test keeps them equal).
UPDATE_PUBLIC_KEY = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEOopEbtU/63TMJp0lW7rXWGqAAnKhACiFtjV+oK+BEfgl1Icrha2qGhd5vJvlE0VlmybN3p8Rfo+wnzcOyI0eSg=="
RELEASE_HEADER = b"MS2OFFLINE-RELEASE-1\n"
# Problem reports (ProblemReport; docs/engineering/DIAGNOSTIC_REPORTS.md).
REPORT_SERVICE_URL = "https://ms2-reports.ms2-reports.workers.dev"  # the maintainer's report service (tools/report-service)
REPORT_MAX_CHARS = 400_000
REPORT_NOTE_CHARS = 2_000
REPORT_QUEUE_LIMIT = 20
REPORT_MAX_AGE_DAYS = 30
REPORT_CUT = "\n... (report cut: too long)"
REPORT_SENDING = threading.Lock()  # the start-up thread, the end of a session and Report a problem share one queue
REPORT_NOTICE_VERSION = 1
WHAT_IS_SENT = (
    "A report contains:\n"
    "• What went wrong: the error and warning lines of the game servers' logs (the maps, monsters, items and quests "
    "of your session)\n"
    "• When you played: the launcher's steps with their times\n"
    "• Your MapleStory 2 Offline and Proton versions, and your program, prefix and game folders\n"
    "• A random install number, so reports from the same PC can be grouped\n\n"
    "Never sent:\n"
    "• Your user name and computer name\n"
    "• Your character, account and save names, and your chat\n"
    "• What the game client itself writes (its log and its messages)\n"
    "• Your save file and your game files\n\n"
    "Reports go only to the MapleStory 2 Offline developer and are used only to find and fix bugs.")
REPORT_NOTICE_TEXT = (
    "Problem reports are now sent automatically.\n\n"
    "From this version, when a session ends with a problem (a crash, a stuck loading screen, server errors) or the "
    "launcher itself fails, its report goes to the developer without asking.\n\n"
    "Every report helps the game's development: it shows exactly what broke and where, so fixes reach every player "
    "sooner. Keeping reports on is the easiest way to contribute to MapleStory 2 Offline. Want to do more? Share bugs "
    "and ideas on Discord (https://discord.gg/bEEc9QsRx), or contribute on GitHub (https://github.com/endale416614-cmd/MapleStory2-Offline).\n\n"
    "Details: https://github.com/endale416614-cmd/MapleStory2-Offline#problem-reports\n\n" +
    WHAT_IS_SENT + "\n\n"
    "You can change this any time with the \"Send problem reports to the developer automatically\" checkbox, or "
    "SendReports=always, ask or never in launcher.ini.\n\n"
    "Keep sending problem reports automatically?"
)


def now():
    return datetime.datetime.now()


def wrap_text(text, width=100):
    """Lines longer than `width` broken between words, for dialogs that do not wrap (zenity --no-wrap); bullets keep
    their indent and links stay whole."""
    return "\n".join(line if len(line) <= width else textwrap.fill(
        line, width, subsequent_indent="  " if line.startswith("• ") else "", break_long_words=False, break_on_hyphens=False)
        for line in text.split("\n"))


def stamp():
    return now().strftime("%Y-%m-%d %H:%M:%S")


def sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


class FileSlice(io.RawIOBase):
    """Read-only view of bytes [start, start + length) of an open file (like SubStream in Launcher/Setup.cs)."""

    def __init__(self, f, start, length):
        super().__init__()
        self.f, self.start, self.length, self.pos = f, start, length, 0

    def readable(self):
        return True

    def seekable(self):
        return True

    def tell(self):
        return self.pos

    def seek(self, offset, whence=os.SEEK_SET):
        base = (0, self.pos, self.length)[whence]
        self.pos = max(0, min(self.length, base + offset))
        return self.pos

    def readinto(self, buffer):
        self.f.seek(self.start + self.pos)
        data = self.f.read(min(len(buffer), self.length - self.pos))
        buffer[:len(data)] = data
        self.pos += len(data)
        return len(data)

    def close(self):
        if not self.closed:
            self.f.close()
        super().close()


def windows_path(path):
    """A Linux path as the game (under Proton) sees it: Z: is the root of the file system."""
    return "Z:" + os.path.abspath(path).replace("/", "\\")


# ---------------------------------------------------------------------------------------------------------------------
# Settings: defaults.ini (shipped, replaced by updates) then launcher.ini (yours), "Key=Value" lines.
# ---------------------------------------------------------------------------------------------------------------------
MODE_OPTIONS = ("grindrates", "dungeonalldrops", "unlimiteddungeonclears", "classdropsonly", "pettaming", "dungeonepicgear")


def without_pre_mode_options(lines):
    """A launcher.ini from before the game modes (the installer copied the example, all options "=true") would keep
    GameMode=Legacy adapted: comment those lines out (true is the Adapted default anyway). Lines set to false stay; a
    file that names GameMode is never changed. Returns the new lines, or None when nothing changes.
    Same rule as Launcher/LauncherIni.cs."""
    result, changed = [], False
    for line in lines:
        if "=" not in line or line.lstrip().startswith("#"):
            result.append(line)
            continue
        key, value = (part.strip() for part in line.split("=", 1))
        if key.lower() == "gamemode":
            return None
        if key.lower() in MODE_OPTIONS and value.lower() == "true":
            result.append(f"#{line.strip()}  (1.1.9: now set by the game mode)")
            changed = True
            continue
        result.append(line)
    return result if changed else None


def migrate_launcher_ini(path):
    try:
        with open(path, encoding="utf-8", errors="replace") as f:
            lines = without_pre_mode_options(f.read().splitlines())
        if lines is not None:
            with open(path, "w", encoding="utf-8") as f:
                f.write("\n".join(lines) + "\n")
    except OSError:
        pass  # missing or read-only: the options then still win over the mode, as in 1.1.8


def profile_error(name):
    """None when the name can be a save profile folder (the rules of Settings.ValidateProfile on Windows), else the message."""
    name = name.strip()
    if not name:
        return None
    if len(name) > 32:
        return "Use letters, digits, spaces, - or _ (up to 32 characters)."
    if any(not (c.isalnum() or c in " -_") for c in name):
        return "Use letters, digits, spaces, - or _ (up to 32 characters)."
    if name.lower() == "backups":
        return "That name is reserved."
    return None


class Settings:
    def __init__(self, home):
        self.home = home
        values = {}
        migrate_launcher_ini(os.path.join(home, "launcher.ini"))
        for name in ("defaults.ini", "launcher.ini"):
            self._read(values, os.path.join(home, name))
        self.values = values
        version_file = os.path.join(home, "VERSION")
        self.version = open(version_file).read().strip() if os.path.exists(version_file) else "dev"
        self.client_dir = values.get("ClientDir", "")
        self.username = values.get("Username", "offline")
        self.password = values.get("Password", "offline")
        self.server_configuration = values.get("ServerConfiguration", "Release")
        self.test_kit = self._flag("TestKit", False)
        self.unified_content = self._flag("UnifiedContent", True)
        self.trigger_trace = self._flag("TriggerTrace", False)
        # GameMode: "Legacy" (original rules) or "Adapted" (solo-friendly, also when not set). An option no ini file
        # names is None: nothing is sent to the server, whose default for the mode applies.
        self.game_mode = "Legacy" if values.get("GameMode", "").strip().lower() == "legacy" else "Adapted"
        self.dungeon_all_drops = self._flag("DungeonAllDrops", None)
        self.unlimited_dungeon_clears = self._flag("UnlimitedDungeonClears", None)
        self.grind_rates = self._flag("GrindRates", None)
        self.mail_never_expires = self._flag("MailNeverExpires", None)
        self.cheat_commands = self._flag("CheatCommands", None)
        self.party_dungeons = self._flag("PartyDungeons", None)
        self.fair_fight = self._flag("FairFight", None)
        self.black_market = self._flag("BlackMarket", None)
        self.class_drops_only = self._flag("ClassDropsOnly", None)
        self.pet_taming = self._flag("PetTaming", None)
        self.dungeon_epic_gear = self._flag("DungeonEpicGear", None)
        self.debug_reports = self._flag("DebugReports", True)
        # Only the player's own launcher.ini: a shipped defaults.ini must never turn sending on.
        own = {}
        self._read(own, os.path.join(home, "launcher.ini"))
        own = {key.lower(): value for key, value in own.items()}  # any case, as on Windows and in save_user_value
        if "gamemode" not in own and os.path.exists(os.path.join(home, "launcher.ini")):
            # The mode in effect, as the Windows chooser writes it at the first start: the line ends the 1.1.9 migration.
            try:
                self.save_user_value("GameMode", self.game_mode)
            except (OSError, ValueError):  # read-only, or not UTF-8: the migration then runs again at the next start
                pass
        written = own.get("sendreports")
        report_notice = 0
        try:
            report_notice = int(own.get("reportnotice") or 0)
        except ValueError:
            pass
        self.send_reports = Settings.default_send_reports(written, report_notice)
        self.report_notice_pending = Settings.needs_report_notice(written, report_notice)
        self.backups_to_keep = self._int("BackupsToKeep", 10)
        auto = values.get("AutoUpdate", "off").strip().lower()
        self.auto_update = "ask" if auto in ("ask", "true", "yes", "on") else "always" if auto in ("always", "auto", "silent") else "off"
        # Linux-only settings
        self.proton = values.get("Proton", "").strip()
        self.prefix = os.path.expanduser(values.get("Prefix", "").strip()) or os.path.join(home, "prefix")
        self.runner = values.get("Runner", "umu-run").strip() or "umu-run"
        self.client_console = self._flag("ClientConsole", False)
        # Profile setting
        self.profile = values.get("Profile", "").strip()
        if profile_error(self.profile):
            self.profile = ""
        self.launch_env = {}
        for item in values.get("LaunchEnv", "").split(";"):
            if "=" in item:
                key, value = item.split("=", 1)
                self.launch_env[key.strip()] = value.strip()

    # paths
    @property
    def server_dir(self): return os.path.join(self.home, "Maple2")
    @property
    def dotnet(self): return os.path.join(self.home, "dotnet", "dotnet")
    @property
    def log_dir(self): return os.path.join(self.home, "logs")
    @property
    def report_dir(self): return os.path.join(self.home, "reports")
    @property
    def patch_dir(self): return os.path.join(self.home, "client-patch")
    @property
    def mods_dir(self): return os.path.join(self.home, "client-mods")
    @property
    def save_dir(self): return os.path.join(self.home, "SaveData", self.profile) if self.profile else os.path.join(self.home, "SaveData")
    @property
    def save_file(self): return os.path.join(self.save_dir, "game-server.db")
    @property
    def backup_dir(self): return os.path.join(self.save_dir, "backups")
    @property
    def game_data_dir(self): return os.path.join(self.home, "GameData")
    @property
    def content_pack_dir(self): return os.path.join(self.home, "ContentPack")
    @property
    def client_root(self): return os.path.normpath(os.path.join(self.client_dir, "..")) if self.client_dir else ""
    @property
    def update_dir(self): return os.path.join(self.home, "updates")
    @property
    def save_status_file(self): return os.path.join(self.save_dir, "save-status.json")
    @property
    def report_queue_dir(self): return os.path.join(self.home, "report-queue")

    @property
    def install_id(self):
        """A random number for this install (install-id, made on first use), so reports from one PC can be grouped."""
        path = os.path.join(self.home, "install-id")
        try:
            if os.path.exists(path):
                with open(path, encoding="utf-8") as f:
                    value = f.read().strip()
                if value:
                    return value
            value = uuid.uuid4().hex
            with open(path, "w", encoding="utf-8") as f:
                f.write(value)
            return value
        except OSError:
            return "unknown"

    @staticmethod
    def default_send_reports(written, report_notice):
        """SendReports from the player's own launcher.ini: a written choice wins; without one, reports go automatically
        once the one-time notice has been shown (ReportNotice), and are asked until then."""
        if (written or "").strip():
            return Settings.parse_send_reports(written)
        return "always" if report_notice >= REPORT_NOTICE_VERSION else "ask"

    @staticmethod
    def needs_report_notice(written, report_notice):
        """The notice is due when the player never chose and has not seen this version of it."""
        return not (written or "").strip() and report_notice < REPORT_NOTICE_VERSION

    @staticmethod
    def parse_send_reports(value):
        """launcher.ini SendReports: "always" or "never" when written so (or as true/yes, false/no/off), else "ask"."""
        value = (value or "").strip().lower()
        return "always" if value in ("always", "true", "yes") else "never" if value in ("never", "false", "no", "off") else "ask"

    @staticmethod
    def send_reports_after_checkbox(current, ticked):
        """SendReports after the "send automatically" checkbox: ticked is always, unticking always asks again, never stays."""
        return "always" if ticked else "ask" if current == "always" else current

    def _flag(self, key, fallback):
        value = self.values.get(key, "").strip().lower()
        return True if value == "true" else False if value == "false" else fallback

    def _int(self, key, fallback):
        try:
            return int(self.values.get(key, ""))
        except ValueError:
            return fallback

    @staticmethod
    def _read(values, path):
        if not os.path.exists(path):
            return
        with open(path, encoding="utf-8", errors="replace") as f:
            for line in f:
                if "=" in line and not line.lstrip().startswith("#"):
                    key, value = line.split("=", 1)
                    if key.strip():
                        values[key.strip()] = value.strip()

    def save_user_value(self, key, value):
        """Stores one choice in launcher.ini (adds or replaces the line, keeps the rest)."""
        path = os.path.join(self.home, "launcher.ini")
        lines = open(path, encoding="utf-8").read().splitlines() if os.path.exists(path) else [
            "# Your own settings. They override defaults.ini and are kept by updates."]
        for i, line in enumerate(lines):
            if line.lstrip().lower().startswith(key.lower() + "="):
                lines[i] = f"{key}={value}"
                break
        else:
            lines.append(f"{key}={value}")
        with open(path, "w", encoding="utf-8") as f:
            f.write("\n".join(lines) + "\n")
        self.values[key] = value


# ---------------------------------------------------------------------------------------------------------------------
# Messages: the terminal always; desktop notifications and dialogs when there is a desktop (kdialog, zenity).
# ---------------------------------------------------------------------------------------------------------------------
class UI:
    def __init__(self, home):
        self.icon = os.path.join(home, "icon.png")
        self.gui = bool(os.environ.get("DISPLAY") or os.environ.get("WAYLAND_DISPLAY"))
        self.tty = sys.stdin.isatty()

    def status(self, text):
        print(f"[{now():%H:%M:%S}] {text}", flush=True)

    def notify(self, text):
        self.status(text)
        if self.gui and shutil.which("notify-send"):
            subprocess.run(["notify-send", "-a", APP_NAME, "-i", self.icon, TITLE, text], check=False)

    def error(self, text):
        print(f"\n{TITLE}: {text}\n", file=sys.stderr, flush=True)
        if not self.gui:
            return
        if shutil.which("kdialog"):
            subprocess.run(["kdialog", "--title", TITLE, "--error", text], check=False)
        elif shutil.which("zenity"):
            subprocess.run(["zenity", "--error", "--title", TITLE, "--no-wrap", "--no-markup", "--text", text], check=False)
        elif shutil.which("notify-send"):
            subprocess.run(["notify-send", "-u", "critical", "-a", APP_NAME, "-i", self.icon, TITLE, text], check=False)

    def info(self, text):
        print(f"\n{text}\n", flush=True)
        if not self.gui:
            return
        if shutil.which("kdialog"):
            subprocess.run(["kdialog", "--title", TITLE, "--msgbox", text], check=False)
        elif shutil.which("zenity"):
            subprocess.run(["zenity", "--info", "--title", TITLE, "--no-wrap", "--no-markup", "--text", text], check=False)

    def can_ask(self):
        """Whether ask() reaches the player (a dialog tool on the desktop, or a terminal) instead of returning its default."""
        return self.tty or self.gui and bool(shutil.which("kdialog") or shutil.which("zenity"))

    def ask(self, text, default=False):
        """Yes/No. A dialog when there is a desktop, the terminal otherwise, the default when neither can ask. The dialog's
        default button follows `default` (kdialog --yesno defaults to Yes, --warningyesno to No; zenity --default-cancel)."""
        if self.gui and shutil.which("kdialog"):
            return subprocess.run(["kdialog", "--title", TITLE, "--yesno" if default else "--warningyesno", text],
                                  check=False).returncode == 0
        if self.gui and shutil.which("zenity"):
            return subprocess.run(["zenity", "--question", "--title", TITLE, "--no-wrap", "--no-markup", "--text", text]
                                  + ([] if default else ["--default-cancel"]), check=False).returncode == 0
        if self.tty:
            answer = input(f"{text} [{'Y/n' if default else 'y/N'}] ").strip().lower()
            return default if answer == "" else answer.startswith("y")
        return default


class EventLog:
    """logs/launcher.log: one line per event of the launcher itself (what the Windows launcher writes too)."""
    session_id = now().strftime("%Y%m%d-%H%M%S")

    def __init__(self, settings):
        os.makedirs(settings.log_dir, exist_ok=True)
        self.path = os.path.join(settings.log_dir, "launcher.log")

    def write(self, kind, text):
        try:
            with open(self.path, "a", encoding="utf-8") as f:
                f.write(f"{stamp()} [{self.session_id}] {kind}: {text}\n")
        except OSError:
            pass


# ---------------------------------------------------------------------------------------------------------------------
# The game client: Steam libraries or the folder you chose; the client patch.
# ---------------------------------------------------------------------------------------------------------------------
class Client:
    @staticmethod
    def normalize(folder):
        """Accepts the install folder, its x64 folder or the Data folder; returns the x64 folder or None."""
        if not folder:
            return None
        folder = os.path.expanduser(folder)
        for candidate in (folder, os.path.join(folder, "x64"), os.path.join(folder, "..", "x64")):
            full = os.path.abspath(candidate)
            if os.path.isfile(os.path.join(full, GAME_EXE)) and os.path.isdir(os.path.join(full, "..", "Data")):
                return full
        return None

    @staticmethod
    def steam_libraries():
        roots = [os.path.expanduser(p) for p in ("~/.local/share/Steam", "~/.steam/steam", "~/.steam/root",
                                                  "~/.var/app/com.valvesoftware.Steam/.local/share/Steam")]
        libraries = []
        for root in roots:
            root = os.path.realpath(root)
            if not os.path.isdir(root) or root in libraries:
                continue
            libraries.append(root)
            vdf = os.path.join(root, "steamapps", "libraryfolders.vdf")
            if os.path.exists(vdf):
                for match in re.finditer(r'"path"\s+"([^"]+)"', open(vdf, encoding="utf-8", errors="replace").read()):
                    path = os.path.realpath(match.group(1).replace("\\\\", "\\"))
                    if path not in libraries:
                        libraries.append(path)
        return libraries

    @classmethod
    def find(cls, settings):
        """The client's x64 folder: the saved choice, the program folder's own client/, then every Steam library."""
        for candidate in (settings.client_dir, os.path.join(settings.home, "client")):
            found = cls.normalize(candidate)
            if found:
                return found
        for library in cls.steam_libraries():
            found = cls.normalize(os.path.join(library, "steamapps", "common", "MapleStory 2"))
            if found:
                return found
        return None

    @staticmethod
    def running_pids():
        me = os.getpid()
        try:
            out = subprocess.run(["pgrep", "-f", GAME_EXE], capture_output=True, text=True, check=False).stdout
        except OSError:
            return []
        return [int(p) for p in out.split() if p.isdigit() and int(p) != me]

    @staticmethod
    def install_patch(client_dir, patch_dir, log):
        """Keeps the game's own proxy DLL as .bak, copies the patched DLLs. Returns True when installed."""
        proxy, backup = os.path.join(client_dir, PROXY_DLL), os.path.join(client_dir, PROXY_DLL + ".bak")
        patched = os.path.join(patch_dir, PROXY_DLL)
        try:
            if not os.path.exists(backup) and os.path.exists(proxy) and sha256(proxy) != sha256(patched):
                current = sha256(proxy)
                if current != ORIGINAL_PROXY_SHA256:
                    log.write("patch", f"client {PROXY_DLL} is not the original (sha256 {current[:16]}); kept as .bak")
                shutil.copy2(proxy, backup)
            for dll in (PATCH_DLL, PROXY_DLL):
                source, target = os.path.join(patch_dir, dll), os.path.join(client_dir, dll)
                if os.path.exists(source) and (not os.path.exists(target) or sha256(source) != sha256(target)):
                    shutil.copy2(source, target)
            return True
        except OSError as ex:
            log.write("patch", f"could not install: {ex}")
            return False

    @staticmethod
    def remove_patch(client_dir, patch_dir):
        proxy, backup = os.path.join(client_dir, PROXY_DLL), os.path.join(client_dir, PROXY_DLL + ".bak")
        if os.path.exists(backup) and (not os.path.exists(proxy) or sha256(proxy) == sha256(os.path.join(patch_dir, PROXY_DLL))):
            shutil.copy2(backup, proxy)
            os.remove(backup)
        agarcium = os.path.join(client_dir, PATCH_DLL)
        if os.path.exists(agarcium) and sha256(agarcium) == sha256(os.path.join(patch_dir, PATCH_DLL)):
            os.remove(agarcium)

    @staticmethod
    def patch_installed(client_dir, patch_dir):
        return all(os.path.exists(os.path.join(client_dir, dll)) and
                   sha256(os.path.join(client_dir, dll)) == sha256(os.path.join(patch_dir, dll)) for dll in (PATCH_DLL, PROXY_DLL))

    @staticmethod
    def proxy_state(client_dir, patch_dir):
        proxy = os.path.join(client_dir, PROXY_DLL)
        if not os.path.exists(proxy):
            return "missing"
        digest = sha256(proxy)
        return "pristine" if digest == ORIGINAL_PROXY_SHA256 else "patched" if digest == sha256(os.path.join(patch_dir, PROXY_DLL)) else "modified"


class ContentPack:
    """Append-only content pack (PLAN_1.2.0_UNIFIED_CONTENT section 2). The pack folder holds pack.json and, per client
    archive, <stem>.tail (the bytes appended after the untouched Global archive) and <stem>.m2h (the new header),
    where stem is the archive file name without extension (Xml.tail, Xml.m2h for Data/Xml.m2d).

    Apply, per archive: the file must be byte-identical to the Global archive (size and sha256 from pack.json); the
    tail is appended, verified, then the .m2h is replaced and the original kept as <stem>.orig.m2h. An archive that
    already ends with the tail is left alone. Anything else (unknown build, half-applied file, a Steam update) is
    reported and nothing is changed. Restore truncates to the Global size and puts the .orig.m2h back, then verifies.
    Every step is journaled in the pack folder like UpdateInstaller does.
    """

    FILE_NAME = "pack.json"
    JOURNAL_NAME = "apply-journal.json"

    class Archive:
        def __init__(self, path, global_size, global_sha256, tail_size, tail_sha256, m2h_sha256, orig_m2h_sha256):
            self.path = path
            self.global_size = global_size
            self.global_sha256 = global_sha256
            self.tail_size = tail_size
            self.tail_sha256 = tail_sha256
            self.m2h_sha256 = m2h_sha256
            self.orig_m2h_sha256 = orig_m2h_sha256

        @property
        def stem(self):
            return os.path.splitext(os.path.basename(self.path))[0]

        @property
        def m2h_path(self):
            return self.path.replace(".m2d", ".m2h")

        @property
        def orig_m2h_path(self):
            return self.path.replace(".m2d", ".orig.m2h")

    class Status:
        NotApplied = "NotApplied"
        Applied = "Applied"
        EarlierPack = "EarlierPack"
        Foreign = "Foreign"

    class ArchiveState:
        def __init__(self, archive, status, reason):
            self.archive = archive
            self.status = status
            self.reason = reason

    class Journal:
        def __init__(self, version="", state="", archive="", step="", utc=""):
            self.version = version
            self.state = state
            self.archive = archive
            self.step = step
            self.utc = utc

    def __init__(self, version, archives):
        self.version = version
        self.archives = archives

    @classmethod
    def load(cls, pack_dir):
        """Load a content pack from the specified directory."""
        with open(os.path.join(pack_dir, cls.FILE_NAME), "rb") as f:
            return cls.parse(f.read())

    @classmethod
    def parse(cls, json_bytes):
        """Parse JSON bytes into a ContentPack object."""
        try:
            data = json.loads(json_bytes.decode('utf-8'))
            version = data.get("version", "")
            if not version:
                raise ValueError(f"{cls.FILE_NAME} has no version")

            archives = []
            for item in data.get("archives", []):
                path = item.get("path", "").replace("\\", "/")
                if not path or os.path.isabs(path) or any(part in ("", ".", "..") for part in path.split("/")) \
                   or not path.lower().endswith(".m2d"):
                    raise ValueError(f"{cls.FILE_NAME} lists an invalid archive path: {path}")

                archive = cls.Archive(
                    path=path,
                    global_size=item.get("globalSize"),
                    global_sha256=cls._sha(item, "globalSha256"),
                    tail_size=item.get("tailSize"),
                    tail_sha256=cls._sha(item, "tailSha256"),
                    m2h_sha256=cls._sha(item, "m2hSha256"),
                    orig_m2h_sha256=cls._sha(item, "origM2hSha256")
                )

                if archive.global_size <= 0 or archive.tail_size <= 0:
                    raise ValueError(f"{cls.FILE_NAME} lists {path} without valid sizes")

                if any(a.path.lower() == path.lower() or a.stem.lower() == archive.stem.lower() for a in archives):
                    raise ValueError(f"{cls.FILE_NAME} lists {path} twice")
                archives.append(archive)

            if not archives:
                raise ValueError(f"{cls.FILE_NAME} lists no archives")

            return cls(version, archives)
        except Exception as ex:
            if isinstance(ex, (json.JSONDecodeError, KeyError, ValueError, TypeError, AttributeError)):
                raise ValueError(f"{cls.FILE_NAME} is not readable: {ex}")
            else:
                raise

    @staticmethod
    def _sha(item, name):
        """Extract and validate SHA256 hash from JSON."""
        sha = item.get(name, "").lower()
        if len(sha) != 64 or not all(c in "0123456789abcdef" for c in sha):
            raise ValueError(f"{ContentPack.FILE_NAME} has an invalid {name}")
        return sha

    def state(self, client_root):
        """State of every archive in the client. Foreign carries the reason in words."""
        return [self._state(client_root, archive) for archive in self.archives]

    @classmethod
    def _state(cls, client_root, archive):
        """Get the state of a single archive."""
        path = os.path.join(client_root, archive.path)
        m2h = os.path.join(client_root, archive.m2h_path)

        if not os.path.exists(path) or not os.path.exists(m2h):
            return cls.ArchiveState(archive, cls.Status.Foreign, f"{archive.path} or its .m2h is missing")

        size = os.path.getsize(path)
        m2h_hash = cls._hash(m2h)

        if size == archive.global_size:
            if cls._hash(path) != archive.global_sha256:
                return cls.ArchiveState(archive, cls.Status.Foreign, f"{archive.path} has the Global size but not the Global sha256 (unknown client build)")
            return cls.ArchiveState(archive, cls.Status.NotApplied, "") if m2h_hash == archive.orig_m2h_sha256 else \
                cls.ArchiveState(archive, cls.Status.Foreign, f"{archive.m2h_path} is not the Global header")
        applied_size = size == archive.global_size + archive.tail_size
        if applied_size and cls._hash(path, archive.global_size, archive.tail_size) == archive.tail_sha256:
            # The original header is allowed: an apply or restore interrupted between the tail and the header step.
            return cls.ArchiveState(archive, cls.Status.Applied, "") if m2h_hash in (archive.m2h_sha256, archive.orig_m2h_sha256) else \
                cls.ArchiveState(archive, cls.Status.Foreign, f"{archive.m2h_path} is neither the Global nor the pack header")
        if size > archive.global_size and cls._has_earlier_pack(client_root, archive):
            return cls.ArchiveState(archive, cls.Status.EarlierPack, "")
        if applied_size:
            return cls.ArchiveState(archive, cls.Status.Foreign, f"{archive.path} has the applied size but its tail does not match the pack")
        # Format numbers without N0 format specifier
        size_str = f"{size:,}"
        global_size_str = f"{archive.global_size:,}"
        applied_size_str = f"{archive.global_size + archive.tail_size:,}"
        return cls.ArchiveState(archive, cls.Status.Foreign,
                                f"{archive.path} has an unexpected size ({size_str} bytes; expected {global_size_str} or {applied_size_str})")

    @classmethod
    def _has_earlier_pack(cls, client_root, archive):
        """An earlier pack (an update from it): the Global header was backed up when it was applied, and the archive still
        starts with the Global bytes, so its tail can go."""
        orig = os.path.join(client_root, archive.orig_m2h_path)
        return os.path.exists(orig) and cls._hash(orig) == archive.orig_m2h_sha256 and \
            cls._hash(os.path.join(client_root, archive.path), 0, archive.global_size) == archive.global_sha256

    @classmethod
    def apply(cls, client_root, pack_dir):
        """Applies the content pack to the client. Returns False when access is denied."""
        try:
            pack = cls.load(pack_dir)
            states = pack.state(client_root)

            # Check if any archive is Foreign
            foreign = [state for state in states if state.status == cls.Status.Foreign]
            if foreign:
                raise RuntimeError(f"The content pack cannot be applied: {'; '.join(state.reason for state in foreign)}. Nothing was changed. Verify the game files in Steam and try again.")

            # Validate all required pack files exist
            for archive in pack.archives:
                tail = os.path.join(pack_dir, archive.stem + ".tail")
                m2h = os.path.join(pack_dir, archive.stem + ".m2h")

                if not os.path.exists(tail) or cls._hash(tail) != archive.tail_sha256:
                    raise RuntimeError(f"The content pack file {archive.stem}.tail is missing or does not match pack.json. Nothing was changed.")

                if not os.path.exists(m2h) or cls._hash(m2h) != archive.m2h_sha256:
                    raise RuntimeError(f"The content pack file {archive.stem}.m2h is missing or does not match pack.json. Nothing was changed.")

            journal = cls.Journal(version=pack.version, state="applying", utc=datetime.datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%S.%fZ"))

            try:
                for state in states:
                    archive = state.archive
                    target = os.path.join(client_root, archive.path)
                    header = os.path.join(client_root, archive.m2h_path)
                    orig = os.path.join(client_root, archive.orig_m2h_path)

                    if state.status == cls.Status.EarlierPack:
                        # Back to the Global archive first, header then size: each step leaves a state _state knows.
                        cls._step(pack_dir, journal, archive.path, "earlier")
                        cls._copy_over(orig, header)
                        cls._truncate(target, archive.global_size)

                    # Per archive, as ContentPack.Apply: one that already has its tail (applied, or the header step was
                    # interrupted) never gets a second one.
                    if state.status in (cls.Status.NotApplied, cls.Status.EarlierPack):
                        cls._step(pack_dir, journal, archive.path, "tail")

                        try:
                            with open(target, "ab") as append_file, open(os.path.join(pack_dir, archive.stem + ".tail"), "rb") as source:
                                shutil.copyfileobj(source, append_file)

                            # Verify tail was appended correctly
                            if os.path.getsize(target) != archive.global_size + archive.tail_size or \
                               cls._hash(target, archive.global_size, archive.tail_size) != archive.tail_sha256:
                                raise RuntimeError(f"{archive.path} did not verify after the tail was appended.")
                        except Exception:
                            # Truncate back to global size on failure
                            cls._truncate(target, archive.global_size)
                            raise

                    # Replace header if needed
                    if cls._hash(header) != archive.m2h_sha256:
                        cls._step(pack_dir, journal, archive.path, "m2h")

                        # Copy original header if needed
                        if not os.path.exists(orig) or cls._hash(orig) != archive.orig_m2h_sha256:
                            cls._copy_over(header, orig)

                        cls._copy_over(os.path.join(pack_dir, archive.stem + ".m2h"), header)

                journal.state = "done"
                cls._write_journal(pack_dir, journal)
                return True
            except OSError as ex:
                # Not writable: access denied, or a read-only mount (EROFS, e.g. an NTFS library after Fast Startup).
                if isinstance(ex, PermissionError) or ex.errno == errno.EROFS:
                    return False
                raise
        except Exception:
            # Re-raise with original exception type
            raise

    @classmethod
    def restore(cls, client_root, pack_dir):
        """Restores the client to the original state. Returns False when access is denied."""
        try:
            pack = cls.load(pack_dir)
            states = pack.state(client_root)

            # Check if any archive is Foreign
            foreign = [state for state in states if state.status == cls.Status.Foreign]
            if foreign:
                raise RuntimeError(f"The content pack cannot be removed: {'; '.join(state.reason for state in foreign)}. Nothing was changed. Verify the game files in Steam and try again.")

            journal = cls.Journal(version=pack.version, state="restoring", utc=datetime.datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%S.%fZ"))

            try:
                for state in states:
                    if state.status not in (cls.Status.Applied, cls.Status.EarlierPack):
                        continue

                    archive = state.archive
                    target = os.path.join(client_root, archive.path)
                    header = os.path.join(client_root, archive.m2h_path)
                    orig = os.path.join(client_root, archive.orig_m2h_path)

                    # Restore original header if needed
                    if cls._hash(header) != archive.orig_m2h_sha256:
                        if not os.path.exists(orig) or cls._hash(orig) != archive.orig_m2h_sha256:
                            raise RuntimeError(f"{archive.orig_m2h_path} is missing or damaged, so the Global header cannot be put back. Nothing was changed.")

                        cls._step(pack_dir, journal, archive.path, "m2h")
                        cls._copy_over(orig, header)

                    # Truncate back to global size
                    cls._step(pack_dir, journal, archive.path, "truncate")
                    cls._truncate(target, archive.global_size)

                    # Verify the restored file matches the Global archive
                    if cls._hash(target) != archive.global_sha256:
                        raise RuntimeError(f"{archive.path} does not match the Global archive after the tail was removed. Verify the game files in Steam.")

                    # Remove original header if exists
                    if os.path.exists(orig):
                        os.remove(orig)

                journal.state = "done"
                cls._write_journal(pack_dir, journal)
                return True
            except OSError as ex:
                # Not writable: access denied, or a read-only mount (EROFS, e.g. an NTFS library after Fast Startup).
                if isinstance(ex, PermissionError) or ex.errno == errno.EROFS:
                    return False
                raise
        except Exception:
            raise

    @classmethod
    def _step(cls, pack_dir, journal, archive, step):
        """Record a journal step."""
        journal.archive = archive
        journal.step = step
        cls._write_journal(pack_dir, journal)

    @classmethod
    def _write_journal(cls, pack_dir, journal):
        """Write the journal to disk."""
        path = os.path.join(pack_dir, cls.JOURNAL_NAME)
        temp = path + ".tmp"

        with open(temp, "w") as f:
            json.dump({
                "Version": journal.version,
                "State": journal.state,
                "Archive": journal.archive,
                "Step": journal.step,
                "Utc": journal.utc
            }, f, indent=2)

        os.replace(temp, path)

    @classmethod
    def describe(cls, client_root, pack_dir):
        """Support text for reports: pack version, each archive by size and header (no full hash), and the last journal."""
        if not os.path.exists(os.path.join(pack_dir, cls.FILE_NAME)):
            return "Content pack  : none (no ContentPack folder)"

        try:
            pack = cls.load(pack_dir)
        except Exception as ex:
            return f"Content pack  : DAMAGED ({ex})"

        lines = [f"Content pack  : {pack.version}"]

        for archive in pack.archives:
            path = os.path.join(client_root, archive.path)
            m2h = os.path.join(client_root, archive.m2h_path)

            try:
                if not os.path.exists(path) or not os.path.exists(m2h):
                    state = "MISSING"
                else:
                    size = os.path.getsize(path)
                    header = cls._hash(m2h)
                    state = (f"Global size, {('pack header' if header == archive.m2h_sha256 else 'Global header' if header == archive.orig_m2h_sha256 else 'UNKNOWN header')}"
                            if size == archive.global_size else
                            f"applied size, {('pack header' if header == archive.m2h_sha256 else 'Global header' if header == archive.orig_m2h_sha256 else 'UNKNOWN header')}"
                            if size == archive.global_size + archive.tail_size else
                            f"UNEXPECTED size {size:,}, {('pack header' if header == archive.m2h_sha256 else 'Global header' if header == archive.orig_m2h_sha256 else 'UNKNOWN header')}")
            except Exception as ex:
                state = "unreadable: " + str(ex)

            lines.append(f"  {archive.path}: {state}")

        journal_path = os.path.join(pack_dir, cls.JOURNAL_NAME)
        last = "none"
        if os.path.exists(journal_path):
            try:
                with open(journal_path, "r") as f:
                    j = json.load(f)

                if not j:
                    last = "empty"
                else:
                    last = f"{j.get('State', 'unknown')} {j.get('Version', 'unknown')} {j.get('Archive', 'unknown')} {j.get('Step', 'unknown')} at {j.get('Utc', 'unknown')}"
            except Exception as ex:
                last = "unreadable: " + str(ex)

        lines.append(f"Last journal  : {last}")
        return "\n".join(lines)

    @classmethod
    def _copy_over(cls, source, target):
        """Copy a file and replace the target atomically."""
        temp = target + ".tmp"
        shutil.copy2(source, temp)
        os.replace(temp, target)

    @classmethod
    def recover_interrupted(cls, client_root, pack_dir):
        """Undoes a tail append that stopped halfway."""
        journal_path = os.path.join(pack_dir, cls.JOURNAL_NAME)

        if not os.path.exists(journal_path):
            return None

        try:
            with open(journal_path, "r") as f:
                journal = json.load(f)
        except Exception:
            return None  # an unreadable journal names no archive

        if not (journal and journal.get("State") == "applying" and journal.get("Step") == "tail"):
            return None

        pack = cls.load(pack_dir)
        archive = next((a for a in pack.archives if a.path == journal.get("Archive")), None)

        if not archive or journal.get("Version") != pack.version:
            return None

        target = os.path.join(client_root, archive.path) if archive else None

        if not target or not os.path.exists(target):
            return None

        size = os.path.getsize(target)
        applied = archive.global_size + archive.tail_size
        interrupted = (size > archive.global_size and size < applied) or \
                      (size == applied and cls._hash(target, archive.global_size, archive.tail_size) != archive.tail_sha256)

        if not interrupted or cls._hash(target, 0, archive.global_size) != archive.global_sha256:
            return None

        cls._truncate(target, archive.global_size)

        # The journal file uses the C# field names (Version, State, ...); the class uses lowercase ones.
        cls._write_journal(pack_dir, cls.Journal(version=journal.get("Version", ""), state="recovered",
                                                 archive=journal.get("Archive", ""), step=journal.get("Step", ""),
                                                 utc=journal.get("Utc", "")))

        return f"The last change to the unified content was interrupted; the unfinished part of {archive.path} was removed."

    @staticmethod
    def _hash(path, offset=0, length=None):
        """Calculate SHA256 hash of a file."""
        import hashlib
        digest = hashlib.sha256()

        with open(path, "rb") as f:
            if offset > 0:
                f.seek(offset)

            if length is not None:
                # Read only the specified length
                bytes_read = 0
                while bytes_read < length:
                    chunk_size = min(1 << 20, length - bytes_read)
                    chunk = f.read(chunk_size)
                    if not chunk:
                        break
                    digest.update(chunk)
                    bytes_read += len(chunk)
            else:
                # Read entire file
                for chunk in iter(lambda: f.read(1 << 20), b""):
                    digest.update(chunk)

        return digest.hexdigest()

    @staticmethod
    def _truncate(path, length):
        """Truncate a file to specified length."""
        with open(path, "r+b") as f:
            f.truncate(length)

    @classmethod
    def read_data_build_pack(cls, data_build_json):
        """Reads the contentPack field from data-build.json."""
        if not os.path.exists(data_build_json):
            return None

        try:
            with open(data_build_json, "r") as f:
                data = json.load(f)

            if not isinstance(data, dict):
                raise ValueError(f"{data_build_json}: the root is not a JSON object")

            content_pack = data.get("contentPack")

            if content_pack is None:
                return None  # No contentPack field

            if isinstance(content_pack, str):
                if not content_pack.strip():
                    raise ValueError(f"{data_build_json}: contentPack is empty")
                return content_pack
            else:
                raise ValueError(f"{data_build_json}: contentPack is not a string")
        except Exception as ex:
            if isinstance(ex, json.JSONDecodeError):
                raise ValueError(f"{data_build_json} is not valid JSON: {ex}")
            else:
                raise

    @classmethod
    def data_lock_error(cls, applied_pack, data_pack):
        """Generates an error message when there's a mismatch between applied content pack and server data."""
        # No error case: both null or both equal
        if (applied_pack is None and data_pack is None) or \
           (applied_pack is not None and data_pack is not None and applied_pack == data_pack):
            return None

        # Generate appropriate error message based on mismatch type
        if applied_pack is not None and data_pack is None:
            return f"The unified content (pack {applied_pack}) is applied to the game, but the server data is the Global data. Update the launcher, or turn the unified content off in launcher.ini (UnifiedContent=false)."
        elif applied_pack is None and data_pack is not None:
            return f"The server data was built for the unified content (pack {data_pack}), but the game has no content pack applied. Turn the unified content on (UnifiedContent=true in launcher.ini) and press Play, or repair the installation."
        else:
            # Both non-null but different
            return f"The game has content pack {applied_pack}, but the server data was built for pack {data_pack}. Update the launcher, or turn the unified content off in launcher.ini (UnifiedContent=false)."

    def applied_version(self, client_root):
        """Gets the content pack version applied to all archives."""
        states = self.state(client_root)

        # Check if any archive is in Foreign state
        foreign = [s for s in states if s.status == self.Status.Foreign]
        if foreign:
            raise RuntimeError(f"The game files do not match content pack {self.version}: {'; '.join(s.reason for s in foreign)}. Verify the game files in Steam and try again.")

        # If no archive has this pack (an earlier pack counts as none), return None
        if all(s.status in (self.Status.NotApplied, self.Status.EarlierPack) for s in states):
            return None

        # If all archives are Applied, return the pack version
        if all(s.status == self.Status.Applied for s in states):
            return self.version

        # Mixed states - throw exception with specific error message
        not_applied = [s for s in states if s.status == self.Status.NotApplied]
        raise RuntimeError(f"The content pack is only partly applied: {', '.join(s.archive.path for s in not_applied)}. Press Play again to finish applying it, or remove the unified content.")



def _fully_applied(client_root, state):
    """Applied needs the pack header too: tail + Global header is an interrupted apply (apply repairs it)."""
    return state.status == ContentPack.Status.Applied and \
        ContentPack._hash(os.path.join(client_root, state.archive.m2h_path)) == state.archive.m2h_sha256


def _lock(data_dir, applied_pack, warning):
    try:
        data_pack = ContentPack.read_data_build_pack(os.path.join(data_dir, "data-build.json"))
    except ValueError as ex:
        return data_dir, applied_pack, warning, str(ex)
    if applied_pack is not None and not os.path.exists(os.path.join(data_dir, "maple-data.db")):
        return data_dir, applied_pack, warning, ("The server data of the unified content is missing (ContentPack/GameData/maple-data.db). "
                                                 "Repair the installation, or set UnifiedContent=false.")
    return data_dir, applied_pack, warning, ContentPack.data_lock_error(applied_pack, data_pack)


def set_mod_file(mods_dir, mod, relative, enabled):
    """A client mod file that follows a setting: served while on, kept as '<name>.off' while off (D-4)."""
    on = os.path.join(mods_dir, mod, relative)
    off = on + ".off"
    if enabled and os.path.exists(off) and not os.path.exists(on):
        os.rename(off, on)
    elif not enabled and os.path.exists(on):
        os.replace(on, off)


def prepare_content(unified, client_root, pack_dir, global_data_dir):
    """Port of ContentPackFlow.Prepare (Windows): (data_dir, applied_version, warning, error) for the servers' data folder.
    Linux has no administrator retry: an apply or restore that cannot write the game folder is reported instead."""
    warning = None
    try:
        if not os.path.exists(os.path.join(pack_dir, ContentPack.FILE_NAME)):
            return _lock(global_data_dir, None, None)
        pack = ContentPack.load(pack_dir)
        warning = ContentPack.recover_interrupted(client_root, pack_dir)
        # Unified off and no archive longer than the Global one (no pack, this or an earlier one): nothing to remove, so
        # Play skips hashing the Global archives.
        if not unified and all(not os.path.exists(os.path.join(client_root, a.path))
                               or os.path.getsize(os.path.join(client_root, a.path)) <= a.global_size
                               for a in pack.archives):
            return _lock(global_data_dir, None, warning)
        states = pack.state(client_root)
        if any(s.status == ContentPack.Status.Foreign for s in states):
            # A client the pack does not fit (Steam update, other build) plays Global and is never written.
            if unified or any(s.status == ContentPack.Status.Applied for s in states):
                reasons = "; ".join(s.reason for s in states if s.status == ContentPack.Status.Foreign)
                warning = (f"The unified content does not fit this game client ({reasons}). The game starts without it; "
                           "verify the game files in Steam to use it again.")
            return _lock(global_data_dir, None, warning)
        changed = False
        if unified and not all(_fully_applied(client_root, s) for s in states):
            changed = True
            try:
                if not ContentPack.apply(client_root, pack_dir):
                    warning = "The unified content could not be written to the game folder. The game starts without it."
            except RuntimeError as ex:
                warning = str(ex)
        elif not unified and any(s.status in (ContentPack.Status.Applied, ContentPack.Status.EarlierPack) for s in states):
            changed = True
            try:
                if not ContentPack.restore(client_root, pack_dir):
                    return (global_data_dir, None, None, "The unified content could not be removed from the game folder. "
                                                         "Make the game folder writable, or set UnifiedContent=true.")
            except RuntimeError as ex:
                warning = str(ex)
        if changed:
            states = pack.state(client_root)
        if all(_fully_applied(client_root, s) for s in states):
            return _lock(os.path.join(pack_dir, "GameData"), pack.version, warning)
        if all(s.status == ContentPack.Status.NotApplied for s in states):
            return _lock(global_data_dir, None, warning)
        return (global_data_dir, None, warning,
                "The unified content is only partly applied. Press Play again; if this repeats, verify the game files in Steam.")
    except ValueError as ex:
        return (global_data_dir, None, warning,
                f"The content pack is damaged: {ex}. Repair the installation, or delete the ContentPack folder to play the Global game.")
    except OSError as ex:
        return (global_data_dir, None, warning,
                f"The unified content could not be prepared: {ex}. Close Steam and any virus scan of the game folder, then press Play again.")


# ---------------------------------------------------------------------------------------------------------------------
# Ports: who is listening. Our own servers are stopped; anybody else's are a conflict and never touched.
# ---------------------------------------------------------------------------------------------------------------------
class Ports:
    @staticmethod
    def listeners():
        """[(port, pid, exe, cmdline)] for our ports."""
        result = []
        try:
            out = subprocess.run(["ss", "-ltnpH"], capture_output=True, text=True, check=False).stdout
        except OSError:
            return result
        for line in out.splitlines():
            parts = line.split()
            if len(parts) < 4:
                continue
            try:
                port = int(parts[3].rsplit(":", 1)[1])
            except (IndexError, ValueError):
                continue
            if port not in PORTS:
                continue
            pids = [int(p) for p in re.findall(r"pid=(\d+)", line)]
            for pid in pids or [0]:
                exe, cmdline = "", ""
                if pid:
                    try:
                        exe = os.readlink(f"/proc/{pid}/exe")
                        cmdline = open(f"/proc/{pid}/cmdline", "rb").read().replace(b"\0", b" ").decode(errors="replace")
                    except OSError:
                        pass
                result.append((port, pid, exe, cmdline))
        return result

    @staticmethod
    def is_own_server(listener, settings):
        _, pid, exe, cmdline = listener
        return pid != 0 and "Maple2.Server." in cmdline and (
            exe.startswith(os.path.join(settings.home, "dotnet") + os.sep) or settings.server_dir in cmdline)

    @staticmethod
    def own_server_pids(settings):
        """Every process running one of this installation's servers (by its command line), listening or not."""
        pids = []
        for entry in os.listdir("/proc"):
            if not entry.isdigit():
                continue
            try:
                cmdline = open(f"/proc/{entry}/cmdline", "rb").read().replace(b"\0", b" ").decode(errors="replace")
            except OSError:
                continue
            if "Maple2.Server." in cmdline and settings.server_dir in cmdline and settings.dotnet in cmdline:
                pids.append(int(entry))
        return pids

    @staticmethod
    def stop(pids, grace=5.0):
        for pid in pids:
            try:
                os.kill(pid, signal.SIGTERM)
            except OSError:
                pass
        deadline = time.time() + grace
        while time.time() < deadline and any(Ports._alive(p) for p in pids):
            time.sleep(0.2)
        for pid in pids:
            if Ports._alive(pid):
                try:
                    os.kill(pid, signal.SIGKILL)
                except OSError:
                    pass

    @staticmethod
    def _alive(pid):
        try:
            os.kill(pid, 0)
            return True
        except OSError:
            return False

    @staticmethod
    def listening(port):
        try:
            out = subprocess.run(["ss", "-ltnH"], capture_output=True, text=True, check=False).stdout
        except OSError:
            return False
        return any(line.split()[3].endswith(f":{port}") for line in out.splitlines() if len(line.split()) > 3)


# ---------------------------------------------------------------------------------------------------------------------
# The servers.
# ---------------------------------------------------------------------------------------------------------------------
def _child_setup():
    # The servers die with the launcher (PR_SET_PDEATHSIG), like the Windows job object kills them with it.
    try:
        libc = ctypes.CDLL("libc.so.6", use_errno=True)
        libc.prctl(1, signal.SIGKILL, 0, 0, 0)
    except OSError:
        pass
    os.setsid()


class Spawner:
    """Starts the servers from one thread that lives as long as the launcher. The parent-death signal above is tied
    to the THREAD that started the child, not to the process (prctl(2), BUGS): started from a short-lived worker
    thread (the window's buttons run on one), the servers would be killed the moment that thread finished."""

    def __init__(self):
        self.jobs = queue.Queue()
        threading.Thread(target=self._run, name="spawner", daemon=True).start()

    def _run(self):
        while True:
            make, result, done = self.jobs.get()
            try:
                result.append(make())
            except BaseException as ex:  # noqa: BLE001  (handed back to the caller)
                result.append(ex)
            done.set()

    def popen(self, *args, **kwargs):
        result, done = [], threading.Event()
        self.jobs.put((lambda: subprocess.Popen(*args, **kwargs), result, done))
        done.wait()
        if isinstance(result[0], BaseException):
            raise result[0]
        return result[0]


SPAWN = Spawner()


class ServerHost:
    def __init__(self, settings, log):
        self.settings = settings
        self.log = log
        self.servers = []  # (name, Popen)
        self.data_dir = None  # the server data folder prepare_content chose; None means GameData

    def log_path(self, name):
        return os.path.join(self.settings.log_dir, f"{name.lower()}.log")

    def prepare_logs(self):
        """Moves the last session's logs to logs/history/<time> (newest 5 kept)."""
        s = self.settings
        os.makedirs(s.log_dir, exist_ok=True)
        history = os.path.join(s.log_dir, "history")
        last = [self.log_path(n) for n in SERVERS] + [os.path.join(s.log_dir, "game-client.log")]
        last = [p for p in last if os.path.exists(p)]
        if last:
            target = os.path.join(history, datetime.datetime.fromtimestamp(os.path.getmtime(last[0])).strftime("%Y-%m-%d_%H-%M-%S"))
            os.makedirs(target, exist_ok=True)
            for path in last:
                shutil.move(path, os.path.join(target, os.path.basename(path)))
        if os.path.isdir(history):
            for old in sorted(os.listdir(history), reverse=True)[5:]:
                shutil.rmtree(os.path.join(history, old), ignore_errors=True)

    def environment(self):
        s = self.settings
        env = dict(os.environ)
        env["DOTNET_ROOT"] = os.path.dirname(s.dotnet)
        env["DOTNET_ENVIRONMENT"] = "Production"
        env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
        env["DOTNET_NOLOGO"] = "1"
        for variable in ("BIND_IP", "LOGIN_IP", "GAME_IP", "GRPC_GAME_IP", "GRPC_WORLD_IP", "GRPC_LOGIN_IP", "WEB_IP"):
            env[variable] = "127.0.0.1"
        for variable in ("ASPNETCORE_URLS", "DOTNET_URLS", "ASPNETCORE_HTTP_PORTS", "ASPNETCORE_HTTPS_PORTS"):
            env.pop(variable, None)
        env["OFFLINE_MODE"] = "true"
        env["DB_PROVIDER"] = "sqlite"
        env["OFFLINE_SAVE_STATUS_FILE"] = s.save_status_file
        env["OFFLINE_LAUNCH_ID"] = EventLog.session_id
        env["SQLITE_DIR"] = s.save_dir
        data_dir = self.data_dir or s.game_data_dir
        if os.path.exists(os.path.join(data_dir, "maple-data.db")):
            env["SQLITE_DATA_DIR"] = data_dir
        env["OFFLINE_PROFILE"] = s.game_mode
        flags = {
            "OFFLINE_TESTKIT": s.test_kit, "OFFLINE_TRIGGER_TRACE": s.trigger_trace,
            "OFFLINE_DUNGEON_ALL_DROPS": s.dungeon_all_drops, "OFFLINE_UNLIMITED_DUNGEON_CLEARS": s.unlimited_dungeon_clears,
            "OFFLINE_GRIND": s.grind_rates, "OFFLINE_MAIL_NEVER_EXPIRES": s.mail_never_expires,
            "OFFLINE_CLASS_DROPS": s.class_drops_only, "OFFLINE_CHEATS": s.cheat_commands,
            "OFFLINE_PET_TAMING": s.pet_taming, "OFFLINE_DUNGEON_EPIC_GEAR": s.dungeon_epic_gear,
            "OFFLINE_PARTY_DUNGEONS": s.party_dungeons,
            "OFFLINE_FAIR_FIGHT": s.fair_fight,
            "OFFLINE_MARKET": s.black_market,
        }
        for key, value in flags.items():
            if value is None:
                env.pop(key, None)  # the player's own environment must not leak in
            else:
                env[key] = "true" if value else "false"
        return env

    def start(self, name):
        s = self.settings
        project_dir = os.path.join(s.server_dir, f"Maple2.Server.{name}")
        dll = os.path.join(project_dir, "bin", s.server_configuration, "net8.0", f"Maple2.Server.{name}.dll")
        if not os.path.exists(dll):
            raise FileNotFoundError(f"Server program not found: {dll}\n\nRun: ms2offline install")
        if not os.path.exists(s.dotnet):
            raise FileNotFoundError(f".NET runtime not found: {s.dotnet}\n\nRun: ms2offline install")
        log = open(self.log_path(name), "wb")
        process = SPAWN.popen([s.dotnet, dll], cwd=project_dir, env=self.environment(), stdin=subprocess.DEVNULL,
                              stdout=log, stderr=subprocess.STDOUT, preexec_fn=_child_setup)
        log.close()
        self.servers.append((name, process))

    def wait(self, ready, what, timeout, ui, status=None):
        """Waits until ready() holds; fails fast if a server exits."""
        started = time.time()
        while not ready():
            for name, process in self.servers:
                if process.poll() is not None:
                    raise RuntimeError(f"The {name} server stopped unexpectedly (exit code {process.returncode}).\n\nLog: {self.log_path(name)}")
            if time.time() - started > timeout:
                raise TimeoutError(f"Timed out waiting for {what}.\n\nLogs: {self.settings.log_dir}")
            time.sleep(0.25)

    def log_contains(self, name, text):
        try:
            with open(self.log_path(name), encoding="utf-8", errors="replace") as f:
                return text in f.read()
        except OSError:
            return False

    def stopped_servers(self):
        return [name for name, process in self.servers if process.poll() is not None]

    def stop_all(self):
        # Game first so it stops writing, World last since the others report to it.
        for name, process in reversed(self.servers):
            if process.poll() is None:
                try:
                    os.killpg(process.pid, signal.SIGTERM)
                except OSError:
                    process.terminate()
                try:
                    process.wait(5)
                except subprocess.TimeoutExpired:
                    try:
                        os.killpg(process.pid, signal.SIGKILL)
                    except OSError:
                        process.kill()
                    process.wait()
        self.servers.clear()


# ---------------------------------------------------------------------------------------------------------------------
# Validated SQLite backups of the save.
# ---------------------------------------------------------------------------------------------------------------------
class SaveBackup:
    @staticmethod
    def create(source, target):
        """Online backup + integrity_check of the copy. Returns None when good, else the problem (copy removed)."""
        try:
            src = sqlite3.connect(f"file:{source}?mode=ro", uri=True)
            dst = sqlite3.connect(target)
            with dst:
                src.backup(dst)
                dst.execute("PRAGMA journal_mode=DELETE")
            src.close()
            dst.close()
            problem = SaveBackup.check(target)
            if problem:
                os.remove(target)
            return problem
        except Exception as ex:  # noqa: BLE001
            try:
                os.remove(target)
            except OSError:
                pass
            return str(ex)

    @staticmethod
    def checkpoint(path):
        """After the servers are stopped: folds the write-ahead log back into the save."""
        if not os.path.exists(path) or not os.path.exists(path + "-wal"):
            return None
        try:
            connection = sqlite3.connect(path)
            busy = connection.execute("PRAGMA wal_checkpoint(TRUNCATE)").fetchone()
            connection.close()
            return "the save is busy" if busy and busy[0] != 0 else None
        except Exception as ex:  # noqa: BLE001
            return str(ex)

    @staticmethod
    def check(path):
        connection = sqlite3.connect(f"file:{path}?mode=ro", uri=True)
        result = connection.execute("PRAGMA integrity_check").fetchone()
        connection.close()
        return None if result and result[0] == "ok" else (result[0] if result else "no result")


# ---------------------------------------------------------------------------------------------------------------------
# Session report: reports/<time>.txt, plain text, for bug reports.
# ---------------------------------------------------------------------------------------------------------------------
class SessionReport:
    def __init__(self, settings, proton="default"):
        self.settings = settings
        self.proton = proton
        self.steps = []
        self.problems = []
        self.exit_code = None

    def step(self, text):
        self.steps.append((stamp(), text))

    def problem(self, text):
        self.problems.append((stamp(), text))
        self.steps.append((stamp(), "PROBLEM: " + text))

    @property
    def has_problems(self):
        return bool(self.problems)

    def write(self):
        s = self.settings
        os.makedirs(s.report_dir, exist_ok=True)
        path = os.path.join(s.report_dir, now().strftime("%Y-%m-%d_%H-%M-%S") + ".txt")
        try:
            with open(path, "w", encoding="utf-8") as f:
                f.write(ProblemReport.redact(self.raw_text()))
        except OSError:
            return None
        for old in sorted(os.listdir(s.report_dir), reverse=True)[20:]:
            try:
                os.remove(os.path.join(s.report_dir, old))
            except OSError:
                pass
        return path

    def raw_text(self):
        """The report before its redaction: only for ProblemReport.for_upload (which drops the game client's text while
        its markers are intact, then redacts) and for write(). Never written or shown as it is."""
        s = self.settings
        lines = [f"{TITLE} - play session report (Linux)", "=" * 50, "",
                 f"Version: {s.version}", f"Session: {EventLog.session_id}", f"Proton: {self.proton}", f"Prefix: {s.prefix}",
                 f"Program folder: {s.home}", f"Game folder: {s.client_dir}", f"Game exit code: {self.exit_code}", "",
                 "Steps", "-----"] + [f"{t}  {text}" for t, text in self.steps] + ["", "Problems", "--------"]
        lines += [f"{t}  {text}" for t, text in self.problems] or ["None."]
        for name in SERVERS:
            log = os.path.join(s.log_dir, f"{name.lower()}.log")
            if os.path.exists(log):
                text = open(log, encoding="utf-8", errors="replace").read().splitlines()
                flagged = [line for line in text if re.search(r"\[(WRN|ERR|FTL)\]", line)]
                lines += ["", f"{name} server: {len(text)} log lines, {len(flagged)} warnings/errors (last 30 shown)", "-" * 40] + flagged[-30:]
        client = os.path.join(s.log_dir, "game-client.log")
        if os.path.exists(client):
            text = open(client, encoding="utf-8", errors="replace").read().splitlines()
            lines += ["", f"Game client (Proton) log: last 40 of {len(text)} lines", "-" * 40] + text[-40:]
        return "\n".join(lines) + "\n"


# ---------------------------------------------------------------------------------------------------------------------
# Problem reports for the developer, only with the player's consent: a port of Launcher/ProblemReport.cs (design:
# docs/engineering/DIAGNOSTIC_REPORTS.md). The session report without what the game client wrote and without personal
# details, as gzip JSON, queued in report-queue/ and sent when the launcher is online.
# ---------------------------------------------------------------------------------------------------------------------
class ProblemReport:
    # What the game client wrote into the server logs (its messages; socket errors with a dump of the last packet, which
    # can hold names and chat) and save-conflict values (free text). A dropped line takes its continuation lines with it;
    # any run of 8 hex bytes is a packet dump. Long lines may be cut, so a client error is matched without its "]".
    CLIENT_MARKERS = ("Client Log:", "[SendOp:", "[LastPacket:", "proposed='")
    CLIENT_ERROR = re.compile(r"\[[^\]\n]*exception")
    HEX_RUN = re.compile(r"(?:[0-9A-Fa-f]{2} ){8,}")
    NEW_ENTRY = re.compile(r"(?:\d{4}-\d\d-\d\d )?\d\d[:.]\d\d[:.]\d\d")  # a server log line, or a step of the report
    SECTION_RULE = re.compile(r"-{5,}\r?$")
    MACHINE_ID = re.compile(r"(?i)(machine[ _-]?id[\"']?\s*[:=]\s*(?:proposed=)?[\"']?)[0-9a-f][0-9a-f-]{7,}")

    @staticmethod
    def url():
        """MS2OFFLINE_REPORT_URL for tests; players use REPORT_SERVICE_URL. None: no service."""
        url = os.environ.get("MS2OFFLINE_REPORT_URL")
        if url:
            return url
        return REPORT_SERVICE_URL if REPORT_SERVICE_URL.startswith("https://") else None

    @staticmethod
    def for_upload(raw, names):
        """The text that may leave the PC, from the unredacted report (SessionReport.raw_text): the game client's text goes
        first, while its markers are intact (a user named "Game" would change them), then redact, the names, the cut."""
        text = ProblemReport.replace_names(ProblemReport.redact(ProblemReport.drop_client_text(raw)), names)
        return text if len(text) <= REPORT_MAX_CHARS else text[:REPORT_MAX_CHARS] + REPORT_CUT

    @staticmethod
    def note(note, names):
        """The player's own note as sent: redacted, names replaced, at most REPORT_NOTE_CHARS."""
        return ProblemReport.replace_names(ProblemReport.redact(note), names)[:REPORT_NOTE_CHARS]

    @staticmethod
    def drop_client_text(report):
        """Without the game client's log section and without what the client wrote into the server logs. The client
        section is the report's last, so it goes to the end: its own lines may look like a section title."""
        lines = report.split("\n")
        kept = []
        continuation = False
        for i, line in enumerate(lines):
            title = i + 1 < len(lines) and ProblemReport.SECTION_RULE.match(lines[i + 1]) is not None
            if title and line.startswith("Game client (Proton) log"):
                break
            if title or not line.strip() or ProblemReport.NEW_ENTRY.match(line):
                continuation = False
            if any(marker in line for marker in ProblemReport.CLIENT_MARKERS) or ProblemReport.CLIENT_ERROR.search(line):
                continuation = True
                continue
            if not continuation and not ProblemReport.HEX_RUN.search(line):
                kept.append(line)
        return "\n".join(kept)

    @staticmethod
    def identity():
        """The user name and the computer name, also without its domain (alice-laptop.lan and alice-laptop)."""
        names = []
        for get in (getpass.getuser, socket.gethostname):
            try:
                name = get()
            except Exception:  # noqa: BLE001 - no such name to remove
                continue
            if name:
                names += [name, name.split(".")[0]]
        return names

    @staticmethod
    def redact(text):
        """Without the home folder, the user name, the computer name and machine ids."""
        home = os.path.expanduser("~")
        if len(home) > 1:
            text = text.replace(home, "~")
        for name in sorted(set(ProblemReport.identity()), key=len, reverse=True):
            if len(name) >= 2:
                text = re.sub(r"(?<!\w)" + re.escape(name) + r"(?!\w)", "<redacted>", text, flags=re.IGNORECASE)
        text = re.sub(r"Machine: [0-9a-fA-F-]{8,}", "Machine: <redacted>", text)
        return ProblemReport.MACHINE_ID.sub(r"\1<redacted>", text)

    @staticmethod
    def replace_names(text, names):
        """Whole words only (Bob, not Bobby), any case; names of 1 character and the default account "offline" stay."""
        for raw in names:
            name = (raw or "").strip()
            if len(name) >= 2 and name.lower() != "offline":
                text = re.sub(r"(?<!\w)" + re.escape(name) + r"(?!\w)", "<name>", text, flags=re.IGNORECASE)
        return text

    @staticmethod
    def names(settings):
        """(names, unreadable): the character and account names in every save, the save folder names and the user,
        computer and home folder names. unreadable names a save that could not be read, else None: then nothing may be
        sent, because that save's character names would stay in the server logs."""
        names = ProblemReport.identity() + [os.path.basename(os.path.expanduser("~"))]
        save_data = os.path.join(settings.home, "SaveData")
        if not os.path.isdir(save_data):
            return names, None
        try:
            names += [d for d in os.listdir(save_data) if os.path.isdir(os.path.join(save_data, d)) and d.lower() != "backups"]
        except OSError:
            return names, save_data
        unreadable = None

        def walk_error(error):  # a folder that cannot be listed may hold a save
            nonlocal unreadable
            unreadable = unreadable or getattr(error, "filename", None) or save_data

        for root, _, files in os.walk(save_data, onerror=walk_error):
            if "game-server.db" in files:
                path = os.path.join(root, "game-server.db")
                if not ProblemReport._read_names(path, names):
                    unreadable = unreadable or path
        return names, unreadable

    @staticmethod
    def save_label(settings, path):
        """The player's name for what names() could not read: a profile folder, "main save" or "save folder"."""
        save_data = os.path.normpath(os.path.join(settings.home, "SaveData"))
        if os.path.normpath(path) == save_data:
            return "save folder"
        folder = os.path.dirname(os.path.normpath(path))
        return "main save" if folder == save_data else f'save "{os.path.basename(folder)}"'

    @staticmethod
    def build(report_id, created_utc, kind, install_id, launcher, raw, note, names):
        """The report as sent: gzip JSON of schema 1 (design doc section 1), from the unredacted session report."""
        names = list(names)
        payload = {
            "schema": 1, "reportId": str(report_id), "installId": install_id,
            "createdUtc": created_utc.isoformat().replace("+00:00", "Z"), "kind": kind, "platform": "linux",
            "launcher": launcher, "note": None if note is None else ProblemReport.note(note, names),
            "report": ProblemReport.for_upload(raw, names),
        }
        return gzip.compress(json.dumps(payload).encode("utf-8"))

    @staticmethod
    def enqueue(queue_dir, report_id, kind, payload, now):
        """Queues a report as <id>.<kind>.json.gz (written through a .part file); returns its path."""
        os.makedirs(queue_dir, exist_ok=True)
        path = os.path.join(queue_dir, f"{report_id}.{kind}.json.gz")
        with open(path + ".part", "wb") as f:
            f.write(payload)
        os.replace(path + ".part", path)
        os.utime(path, (now, now))
        ProblemReport.pending(queue_dir, now)
        return path

    @staticmethod
    def withdraw(queue_dir, kinds):
        """Deletes the queued reports of these kinds (the player chose never to send reports after problems)."""
        if os.path.isdir(queue_dir):
            for name in os.listdir(queue_dir):
                if any(name.endswith(f".{kind}.json.gz") for kind in kinds):
                    ProblemReport._remove(os.path.join(queue_dir, name))

    @staticmethod
    def pending(queue_dir, now):
        """The queued reports, oldest first. Drops reports older than REPORT_MAX_AGE_DAYS, all but the newest
        REPORT_QUEUE_LIMIT, and .part files of an interrupted write (an hour old: a newer one may still be written)."""
        if not os.path.isdir(queue_dir):
            return []
        reports = []
        for name in os.listdir(queue_dir):
            path = os.path.join(queue_dir, name)
            try:
                mtime = os.path.getmtime(path)
            except OSError:
                continue
            if name.endswith(".part"):
                if mtime < now - 3600:
                    ProblemReport._remove(path)
            elif name.endswith(".json.gz"):
                if mtime < now - REPORT_MAX_AGE_DAYS * 86400:
                    ProblemReport._remove(path)
                else:
                    reports.append((mtime, name, path))
        reports.sort()
        for _, _, path in reports[:-REPORT_QUEUE_LIMIT]:
            ProblemReport._remove(path)
        return [path for _, _, path in reports[-REPORT_QUEUE_LIMIT:]]

    @staticmethod
    def send(queue_dir, url, opener=None, now=None, sent=None):
        """Sends the queued reports, oldest first, each POSTed to <url>/v1/report within 10 s. Sent (2xx) and rejected
        (400, 413) reports are deleted; any other answer, a timeout or no network stops until the next time; a file that
        cannot be read waits. Returns the number sent and adds their paths to sent (a set)."""
        if not url:
            return 0
        opener = opener or urllib.request.urlopen
        count = 0
        for path in ProblemReport.pending(queue_dir, time.time() if now is None else now):
            try:
                with open(path, "rb") as f:
                    data = f.read()
            except OSError:
                continue
            request = urllib.request.Request(url.rstrip("/") + "/v1/report", data=data, method="POST", headers={
                "Content-Type": "application/json", "Content-Encoding": "gzip", "User-Agent": "MapleStory2-Offline-Launcher"})
            try:
                with opener(request, timeout=10) as response:
                    status = response.status
            except urllib.error.HTTPError as ex:
                status = ex.code
                ex.close()
            except (urllib.error.URLError, TimeoutError, OSError):
                break  # no network or a timeout
            if 200 <= status < 300:
                ProblemReport._remove(path)
                count += 1
                if sent is not None:
                    sent.add(path)
            elif status in (400, 413):
                ProblemReport._remove(path)  # the service will never take this one
            else:
                break
        return count

    @staticmethod
    def _read_names(path, names):
        try:
            db = sqlite3.connect(f"file:{urllib.request.pathname2url(path)}?mode=ro", uri=True)
        except sqlite3.Error:
            return False
        try:
            for sql in ('SELECT Name FROM "character"', "SELECT Username FROM account"):
                try:
                    names += [row[0] for row in db.execute(sql) if row[0]]
                except sqlite3.Error as ex:
                    if "no such table" not in str(ex).lower():
                        return False  # damaged, locked or unreadable
            return True
        finally:
            db.close()

    @staticmethod
    def _remove(path):
        try:
            os.remove(path)
        except OSError:
            pass  # in use or gone: the next look tries again


# ---------------------------------------------------------------------------------------------------------------------
# Releases of the project: the Setup.exe carries the whole program package as a zip; update zips carry changed files.
# ---------------------------------------------------------------------------------------------------------------------
class Releases:
    @staticmethod
    def _get(url, timeout=10):
        request = urllib.request.Request(url, headers={"User-Agent": f"MapleStory2-Offline-Linux/1.0", "Accept": "application/vnd.github+json"})
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return response.read()

    @staticmethod
    def parse_version(text):
        return tuple(int(p) if p.isdigit() else 0 for p in re.split(r"[.\-]", text.strip().lstrip("vV")))

    @classmethod
    def latest(cls):
        data = json.loads(cls._get(f"https://api.github.com/repos/{REPOSITORY}/releases/latest"))
        version = data.get("tag_name", "").lstrip("vV")
        setup_url, update_url, update_name, update_size = None, None, None, 0
        manifest_url, signature_url = None, None
        for asset in data.get("assets", []):
            name = asset.get("name", "")
            url = asset.get("browser_download_url", "")
            if not url.startswith("https://github.com/"):
                continue
            if name == "MapleStory2-Offline-Setup.exe" or (name.startswith("MapleStory2-Offline-Setup") and name.endswith(".exe")):
                setup_url = url
            elif name.startswith("MapleStory2-Offline-Update-") and name.endswith(".zip"):
                update_url, update_name, update_size = url, name, asset.get("size", 0)
            elif name == "release.json":
                manifest_url = url
            elif name == "release.json.sig":
                signature_url = url
        notes = "\n".join("• " + line.strip()[2:].replace("**", "") for line in data.get("body", "").splitlines()
                          if line.strip().startswith(("- ", "* ")))[:1500]
        return {"version": version, "notes": notes, "page": data.get("html_url", ""), "setup_url": setup_url,
                "update_url": update_url, "update_name": update_name, "update_size": update_size,
                "manifest_url": manifest_url, "signature_url": signature_url}

    @staticmethod
    def verify_manifest(manifest, signature, public_key=UPDATE_PUBLIC_KEY):
        """{file name: sha256} of a release.json whose release.json.sig verifies (ECDSA P-256 / SHA-256, as Windows checks it).
        Raises ValueError when openssl is missing, the signature does not verify or the list is malformed."""
        openssl = shutil.which("openssl")
        if not openssl:
            raise ValueError("The download cannot be checked: the openssl command is missing. Install openssl, or install the release manually (README.md).")

        with tempfile.TemporaryDirectory() as tmpdir:
            # Write public key
            pem_path = os.path.join(tmpdir, "public.pem")
            with open(pem_path, "w") as f:
                f.write("-----BEGIN PUBLIC KEY-----\n")
                for i in range(0, len(public_key), 64):
                    f.write(public_key[i:i+64] + "\n")
                f.write("-----END PUBLIC KEY-----\n")

            # Write signature
            sig_path = os.path.join(tmpdir, "sig.der")
            with open(sig_path, "wb") as f:
                f.write(base64.b64decode(signature.strip()))

            # Write message
            msg_path = os.path.join(tmpdir, "message")
            with open(msg_path, "wb") as f:
                f.write(RELEASE_HEADER + manifest)

            # Verify signature
            result = subprocess.run([openssl, "dgst", "-sha256", "-verify", pem_path, "-signature", sig_path, msg_path],
                                    capture_output=True, check=False)
            if result.returncode != 0:
                raise ValueError("The release's file list (release.json) is not signed by the MapleStory 2 Offline key. Nothing was installed.")

            # Parse manifest
            try:
                manifest_data = json.loads(manifest)
            except json.JSONDecodeError:
                raise ValueError("release.json is malformed.")

            files = manifest_data.get("files", [])
            result_dict = {}
            for entry in files:
                name = entry.get("name")
                sha256 = (entry.get("sha256") or "").lower()
                if not name or len(sha256) != 64 or not all(c in "0123456789abcdef" for c in sha256):
                    raise ValueError("release.json is malformed.")
                result_dict[name] = sha256

            return result_dict

    @staticmethod
    def check_listed(path, listed):
        """Raises ValueError (and deletes the file) unless the file's sha256 is the one the signed list gives for its name."""
        name = os.path.basename(path)
        if listed.get(name) != sha256(path):
            os.remove(path)
            raise ValueError(f"{name} does not match the signed release list (release.json). It was deleted and nothing was installed.")

    @staticmethod
    def download(url, target, ui, label):
        os.makedirs(os.path.dirname(target), exist_ok=True)
        part = target + ".part"
        request = urllib.request.Request(url, headers={"User-Agent": "MapleStory2-Offline-Linux/1.0"})
        with urllib.request.urlopen(request, timeout=60) as response, open(part, "wb") as f:
            total = int(response.headers.get("Content-Length") or 0)
            done, last = 0, -1
            while True:
                block = response.read(1 << 20)
                if not block:
                    break
                f.write(block)
                done += len(block)
                percent = done * 100 // total if total else -1
                if percent != last and percent % 5 == 0:
                    ui.status(f"Downloading {label}... {percent}% ({done >> 20} MB)")
                    last = percent
        os.replace(part, target)
        return target

    @staticmethod
    def open_package(path):
        """The program package zip inside a Setup.exe, or the zip itself."""
        if path.lower().endswith(".exe"):
            size = os.path.getsize(path)
            f = open(path, "rb")  # closed by the slice when the ZipFile is released
            try:
                f.seek(size - 28)
                tail = f.read(28)
                length = struct.unpack("<q", tail[:8])[0]
                if tail[8:] != SETUP_MAGIC or length <= 0 or length > size - 28:
                    raise ValueError("This file is not a MapleStory 2 Offline Setup.")
                return zipfile.ZipFile(FileSlice(f, size - 28 - length, length))
            except BaseException:
                f.close()
                raise
        return zipfile.ZipFile(path)

    @staticmethod
    def keep(name):
        """Which files of a package are installed here: no Windows launcher, no Windows .NET, no Windows-only natives."""
        if name.endswith("/") or name.startswith(PACKAGE_SKIP):
            return False
        if name.endswith((".exe", ".pdb")):
            return False
        if "/runtimes/" in name and "/runtimes/linux-x64/" not in name:
            return False
        return True

    @classmethod
    def install_package(cls, package, home, ui, full):
        """Extracts a package into the program folder. A full package is extracted and checked in updates/staging-<version>
        first (every file must be listed in files.json and match), then replaces the release folders; an update
        package only adds its files and deletes the ones update.json lists."""
        names = [n for n in package.namelist() if cls.keep(n)]
        expected = {}
        try:
            expected = json.loads(package.read("files.json"))
        except KeyError:
            pass
        root = home
        if full:
            if not expected:
                raise ValueError("The package has no file list (files.json), so it cannot be checked.")
            present = set(names)
            for rel in expected:
                if cls.keep(rel) and rel not in present:
                    raise ValueError(f"The package is damaged: {rel} is missing.")
            try:
                version = package.read("VERSION").decode("utf-8", "replace").strip()
            except KeyError:
                version = ""
            root = os.path.join(home, "updates", "staging-" + (re.sub(r"[^\w.-]", "_", version) or "new"))
            shutil.rmtree(root, ignore_errors=True)
        else:
            try:
                update = json.loads(package.read("update.json"))
            except KeyError:
                update = {}
            for rel in update.get("delete", []):
                path = os.path.join(home, rel)
                if os.path.isfile(path):
                    os.remove(path)
        count = 0
        for name in names:
            if name == "update.json":
                continue
            if full and name not in expected and name != "files.json":
                raise ValueError(f"The package is damaged: {name} is not in its file list.")
            target = os.path.join(root, name)
            if not os.path.abspath(target).startswith(os.path.abspath(root) + os.sep):
                raise ValueError(f"Invalid path in the package: {name}")
            os.makedirs(os.path.dirname(target), exist_ok=True)
            with package.open(name) as src, open(target, "wb") as dst:
                shutil.copyfileobj(src, dst, 1 << 20)
            if name in expected and sha256(target) != expected[name]:
                raise ValueError(f"The package is damaged: {name} does not match its checksum.")
            count += 1
            if count % 200 == 0:
                ui.status(f"Installing... {count}/{len(names)} files")
        if full:
            for folder in ("Maple2", "GameData", "client-patch", "client-mods", "LICENSES"):
                shutil.rmtree(os.path.join(home, folder), ignore_errors=True)
            for entry in os.listdir(root):
                source, target = os.path.join(root, entry), os.path.join(home, entry)
                if os.path.isdir(source):
                    shutil.copytree(source, target, dirs_exist_ok=True, copy_function=os.replace)
                else:
                    os.replace(source, target)
            shutil.rmtree(root, ignore_errors=True)
        return count

    @staticmethod
    def install_dotnet(home, ui):
        """A private .NET 8 runtime (ASP.NET Core) for the servers, from Microsoft's install script. No root needed."""
        dotnet = os.path.join(home, "dotnet", "dotnet")
        if os.path.exists(dotnet):
            runtimes = subprocess.run([dotnet, "--list-runtimes"], capture_output=True, text=True, check=False).stdout
            if "Microsoft.AspNetCore.App 8." in runtimes:
                return

        # Ask before downloading Microsoft's dotnet-install.sh
        if not ui.ask("The servers need Microsoft's .NET 8 runtime (about 90 MB, into the program folder). "
                      "Download Microsoft's install script from dot.net and run it now?", default=False):
            raise RuntimeError("The servers need the .NET 8 ASP.NET Core runtime in " + os.path.join(home, "dotnet") +
                               '. See README.md, "Installing .NET yourself", then start again.')

        ui.status("Installing the .NET 8 runtime for the servers (about 90 MB, into the program folder)...")
        script = os.path.join(home, "updates", "dotnet-install.sh")
        ui.status("Fetching Microsoft's dotnet-install.sh from dot.net (the only download from outside GitHub)...")
        Releases.download("https://dot.net/v1/dotnet-install.sh", script, ui, "dotnet-install.sh")
        subprocess.run(["bash", script, "--runtime", "aspnetcore", "--channel", "8.0", "--install-dir", os.path.join(home, "dotnet"), "--no-path"],
                       check=True, stdout=subprocess.DEVNULL)


# ---------------------------------------------------------------------------------------------------------------------
# The launcher.
# ---------------------------------------------------------------------------------------------------------------------
class Launcher:
    def __init__(self, home):
        self.home = home
        os.makedirs(home, exist_ok=True)
        self.settings = Settings(home)
        self.ui = UI(home)
        self.log = EventLog(self.settings)
        self.host = ServerHost(self.settings, self.log)
        self.report = SessionReport(self.settings, self.proton_name()) if self.settings.debug_reports else None
        self.lock = None

    def show_report_notice(self):
        """The one-time notice (1.2.1): reports now go automatically. Waits while nobody can answer (no dialog tool on the
        desktop, no terminal), where UI.ask would return its default unseen; the window (QtUI) always asks."""
        s, ui = self.settings, self.ui
        if not s.report_notice_pending or ProblemReport.url() is None or isinstance(ui, UI) and not ui.can_ask():
            return
        try:
            keep = ui.ask(wrap_text(REPORT_NOTICE_TEXT), default=True)
        except EOFError:  # the terminal closed: no answer, so the notice comes again
            return
        s.send_reports = "always" if keep else "ask"
        s.report_notice_pending = False  # answered: this run keeps the answer even when it cannot be saved
        try:
            s.save_user_value("SendReports", s.send_reports)
            s.save_user_value("ReportNotice", str(REPORT_NOTICE_VERSION))
        except (OSError, ValueError) as ex:  # ValueError: a launcher.ini that is not UTF-8
            ui.error(f"launcher.ini could not be written: {ex}")

    # -- single instance ------------------------------------------------------------------------------------------
    def take_lock(self):
        self.lock = open(os.path.join(self.home, "launcher.lock"), "w")
        try:
            fcntl.flock(self.lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError:
            raise RuntimeError(f"{TITLE} is already running.")

    # -- play -------------------------------------------------------------------------------------------------------
    def play(self, servers_only=False):
        s, ui, log, host, report = self.settings, self.ui, self.log, self.host, self.report
        error = None
        try:
            self.take_lock()
            self.show_report_notice()
            threading.Thread(target=self.start_up_reports, daemon=True).start()  # reports agreed to earlier
            if not servers_only and Client.running_pids():
                raise RuntimeError("MapleStory 2 is already running. Close it first.")
            if s.auto_update != "off":
                self.offer_online_update()
                self.settings = s = Settings(self.home)  # the update may have changed VERSION and defaults
            if not servers_only and not self.locate_client():
                return
            self.claim_ports()

            ui.status("Backing up your save...")
            self.backup_save()
            # Before the servers start: the client archives as UnifiedContent asks, and the server data that matches them.
            if s.client_root:
                if servers_only and Client.running_pids():  # the normal Play checked this at the start
                    raise RuntimeError("MapleStory 2 is already running. Close it first.")
                ui.status("Preparing the unified content..." if s.unified_content else "Checking the game files...")
                data_dir, applied, warning, problem = prepare_content(s.unified_content, s.client_root, s.content_pack_dir, s.game_data_dir)
                log.write("content", f"pack={applied or '-'} data={data_dir} warning={warning or '-'} error={problem or '-'}")
                if problem:
                    raise RuntimeError(problem if warning is None else warning + "\n\n" + problem)
                if warning:
                    if report:
                        report.problem(warning)
                    ui.notify(warning)
                host.data_dir = data_dir
            host.prepare_logs()
            os.makedirs(s.save_dir, exist_ok=True)
            if os.path.exists(s.save_status_file):
                os.remove(s.save_status_file)
            try:
                self.start_servers()
            except Exception as ex:  # noqa: BLE001
                host.stop_all()
                log.write("error", "servers failed to start: " + str(ex))
                raise
            if report:
                report.step("Servers started")
            log.write("servers", "started")

            if servers_only:
                ui.notify("Servers are running (servers-only mode). Press Ctrl+C or run \"ms2offline stop\" to stop them.")
                try:
                    while not host.stopped_servers():
                        time.sleep(1)
                    stopped = host.stopped_servers()
                    if len(stopped) < len(host.servers):
                        raise RuntimeError(f"The {stopped[0]} server stopped.")
                    ui.status("The servers were stopped.")
                    if report:
                        report.step("Servers stopped from outside (ms2offline stop)")
                except KeyboardInterrupt:
                    pass
            else:
                ui.status("Launching MapleStory 2...")
                self.install_client_patch()
                self.set_mod("offline-testkit", s.test_kit)
                # Client mods follow the effective option: its own line in the ini, else the game mode's default.
                adapted = s.game_mode == "Adapted"
                self.set_mod("offline-dungeons", adapted if s.unlimited_dungeon_clears is None else s.unlimited_dungeon_clears)
                self.set_mod("offline-pets", adapted if s.pet_taming is None else s.pet_taming)
                self.set_mod("offline-shop", True)  # full Meret Market in both modes
                set_mod_file(s.mods_dir, "offline-shop", os.path.join("Data", "Xml", "table", "feature.xml"), applied is None)  # the pack's feature.xml has the shop's features too (D-4)
                self.prepare_prefix()
                game = self.launch_game()
                if report:
                    report.step("Game started")
                log.write("game", "started")
                self.wait_for_game(game)
                exit_code = game.returncode
                if report:
                    report.exit_code = exit_code
                log.write("game", f"closed exit={exit_code}")

            if servers_only:
                outcome = "nothing"  # no game ran: nothing to save
            else:
                ui.status("Saving your progress...")
                outcome = self.wait_for_save()
            if outcome == "committed":
                if report:
                    report.step("Progress saved")
            elif outcome == "nothing":
                if report:
                    report.step("Closed before entering the world (nothing to save)")
            elif outcome == "failed":
                if report:
                    report.problem("The game server reported that saving your progress FAILED; the previous save is kept")
            else:
                if report:
                    report.problem("The save was not confirmed; the servers were closed at your request")
            log.write("save", outcome)
            if outcome == "failed":
                ui.error("Your progress from this session could not be saved. Your previous save was kept.\n\n"
                         "Run \"ms2offline diagnostics\" and post the file with your report.")
            ui.status("Closing...")
            host.stop_all()
            log.write("servers", "stopped")
            problem = SaveBackup.checkpoint(s.save_file)
            if problem:
                log.write("save", "could not fold the write-ahead log into the save (kept beside it): " + problem)
        except KeyboardInterrupt:
            if report:
                report.step("Start-up cancelled")
            log.write("cancel", "cancelled")
            host.stop_all()
        except Exception as ex:  # noqa: BLE001
            if report:
                report.problem(str(ex))
            log.write("error", str(ex))
            host.stop_all()
            error = str(ex)

        report_path = None
        if report and (report.steps or error):
            ui.status("Writing the session report...")
            report_path = report.write()
        hint = f"\n\nA report was saved:\n{report_path}" if report_path else ""
        if error:
            ui.error(error + hint)
            if report and report.has_problems:
                self.offer_report(report.raw_text())
            return 1
        if report and report.has_problems and report_path:
            ui.error("Something went wrong during this session." + hint)
            self.offer_report(report.raw_text())
        else:
            ui.status("Done.")
        return 0

    # -- problem reports: only with the player's consent (ProblemReport) -------------------------------------------
    def start_up_reports(self):
        """At every start, in the background: sends the reports the player agreed to earlier."""
        self.send_queued_reports()

    def send_queued_reports(self, sent=None):
        """Sends the queued reports, one sending at a time; when the player chose never, the queued automatic reports are
        taken back first. Never raises; returns the number sent."""
        try:
            with REPORT_SENDING:
                if self.settings.send_reports == "never":
                    ProblemReport.withdraw(self.settings.report_queue_dir, ["problem", "crash"])
                count = ProblemReport.send(self.settings.report_queue_dir, ProblemReport.url(), sent=sent)
        except Exception as ex:  # noqa: BLE001 - a report must never break Play
            self.log.write("report", f"problem reports not sent: {ex}")
            return 0
        if count:
            self.log.write("report", f"{count} problem report(s) sent")
        return count

    def queue_report(self, kind, raw, note, names):
        """Queues a report from the unredacted session report; the names must be complete (ProblemReport.names)."""
        report_id = uuid.uuid4()
        payload = ProblemReport.build(report_id, datetime.datetime.now(datetime.timezone.utc), kind, self.settings.install_id,
                                      self.settings.version, raw, note, names)
        return ProblemReport.enqueue(self.settings.report_queue_dir, report_id, kind, payload, time.time())

    def write_preview(self, text):
        """The exact text a report would send, for the player to read first; returns its path."""
        os.makedirs(self.settings.report_dir, exist_ok=True)
        path = os.path.join(self.settings.report_dir, "to-send.txt")
        with open(path, "w", encoding="utf-8") as f:
            f.write(text)
        return path

    def unreadable_save_text(self, unreadable):
        return (f"No problem report could be made: the {ProblemReport.save_label(self.settings, unreadable)} could not be read "
                "(it may be in use or damaged), so its character names could not be removed from the report.")

    def offer_report(self, raw):
        """A session with a problem: its report goes to the developer per SendReports ("ask" asks first, saying what is sent)."""
        s, ui = self.settings, self.ui
        if ProblemReport.url() is None or s.send_reports == "never":
            return
        try:
            names, unreadable = ProblemReport.names(s)
            if unreadable:
                self.log.write("report", f"not offered: could not read {unreadable}")
                ui.info(self.unreadable_save_text(unreadable))
                return
            if s.send_reports == "ask":
                preview = self.write_preview(ProblemReport.for_upload(raw, names))
                question = ("Send a problem report to the developer?\n\nSomething went wrong in this session. A report helps find "
                            f"and fix it.\n\n{WHAT_IS_SENT}\n\nThe exact text that would be sent: {preview}\n"
                            "To stop this question, set SendReports=always or never in launcher.ini, or tick the box in the window.")
                if not ui.ask(question, default=False):
                    return
            self.queue_report("problem", raw, None, names)
            self.send_queued_reports()
        except Exception as ex:  # noqa: BLE001 - a report must never break the end of a session
            self.log.write("report", f"problem report not queued: {ex}")

    def offer_online_update(self):
        s, ui, log = self.settings, self.ui, self.log
        ui.status("Checking for updates...")
        try:
            latest = Releases.latest()
        except Exception as ex:  # noqa: BLE001
            log.write("update", "online check skipped: " + str(ex))
            return
        if Releases.parse_version(latest["version"]) <= Releases.parse_version(s.version):
            return
        log.write("update", f"version {latest['version']} available online")
        if s.auto_update == "ask":
            text = (f"Version {latest['version']} is available (you have {s.version}).\n\n" +
                    (latest["notes"] + "\n\n" if latest["notes"] else "") +
                    "Update now? (Your save is backed up first.)\n\nTo update by hand instead, set AutoUpdate=off in launcher.ini.")
            if not ui.ask(text):
                log.write("update", f"{latest['version']} postponed by the player")
                return
        try:
            self.install_release(latest)
        except Exception as ex:  # noqa: BLE001
            log.write("update", f"update to {latest['version']} failed: {ex}")
            ui.error(f"The update could not be installed ({ex}). You can keep playing; it is tried again next time.")

    def locate_client(self):
        s, ui = self.settings, self.ui
        found = Client.find(s)
        if not found:
            ui.error("MapleStory 2 was not found.\n\nThis offline edition needs the MapleStory 2 game client (about 14 GB).\n"
                     f"Install it from your Steam library (steam://install/{STEAM_APP_ID}) and start again, or point the launcher "
                     "at its folder:\n\n  ms2offline client --dir \"/path/to/MapleStory 2\"")
            if self.report:
                self.report.step("No game folder found")
            return False
        if found != s.client_dir:
            s.client_dir = found
            s.save_user_value("ClientDir", found)
        if self.report:
            self.report.step(f"Game folder: {found}")
        return True

    def claim_ports(self):
        s, ui, log = self.settings, self.ui, self.log
        listeners = Ports.listeners()
        foreign = [l for l in listeners if not Ports.is_own_server(l, s)]
        if foreign:
            text = "\n".join(f"  port {port}: {os.path.basename(exe) or cmdline[:40] or 'unknown program'} (process {pid})" for port, pid, exe, cmdline in foreign)
            log.write("error", "ports in use: " + text.replace("\n", ";"))
            raise RuntimeError(f"Another program is using a network port MapleStory 2 Offline needs:\n{text}\n\n"
                               "Close that program (or another copy of MapleStory 2 Offline) and start again.")
        own = sorted({pid for _, pid, _, _ in listeners if pid} | set(Ports.own_server_pids(s)))
        if own:
            ui.status("Closing servers left over from last time...")
            log.write("startup", "stopping leftover servers of this installation: " + ", ".join(map(str, own)))
            if self.report:
                self.report.step("Stopped servers left over from a previous run")
            Ports.stop(own)

    def backup_save(self):
        s, log = self.settings, self.log
        if not os.path.exists(s.save_file) or s.backups_to_keep <= 0:
            return
        os.makedirs(s.backup_dir, exist_ok=True)
        target = os.path.join(s.backup_dir, now().strftime("game-server-%Y%m%d-%H%M%S.db"))
        problem = SaveBackup.create(s.save_file, target)
        if problem:
            if self.report:
                self.report.problem("The save backup failed its check and was discarded: " + problem)
            log.write("backup", "FAILED " + problem)
        else:
            log.write("backup", "ok " + os.path.basename(target))
        backups = sorted((f for f in os.listdir(s.backup_dir) if f.startswith("game-server-") and f.endswith(".db")), reverse=True)
        for old in backups[s.backups_to_keep:]:
            os.remove(os.path.join(s.backup_dir, old))

    def start_servers(self):
        ui, host = self.ui, self.host
        ui.status("Starting the world...")
        host.start("World")
        host.wait(lambda: Ports.listening(WORLD_GRPC_PORT), "the world server", 120, ui)
        ui.status("Starting the login server...")
        host.start("Login")
        host.start("Web")
        host.wait(lambda: Ports.listening(LOGIN_PORT) and Ports.listening(WEB_PORT), "the login server", 120, ui)
        ui.status("Loading Maple World...")
        host.start("Game")
        host.wait(lambda: host.log_contains("World", "has become active"), "the game server", 240, ui)

    def install_client_patch(self):
        s, log = self.settings, self.log
        if Client.patch_installed(s.client_dir, s.patch_dir):
            return
        Client.install_patch(s.client_dir, s.patch_dir, log)
        if not Client.patch_installed(s.client_dir, s.patch_dir):
            raise RuntimeError(f"The offline patch could not be installed into the game folder:\n{s.client_dir}\n\n"
                               "Without it the game tries to reach Nexon's closed servers. Check that the folder is writable.")
        if self.report:
            self.report.step("Client patch installed")
        log.write("patch", "client patch installed into " + s.client_dir)

    def set_mod(self, mod, enabled):
        folder = os.path.join(self.settings.mods_dir, mod)
        on, off = os.path.join(folder, "mod.json"), os.path.join(folder, "mod.json.disabled")
        if enabled and os.path.exists(off) and not os.path.exists(on):
            os.rename(off, on)
        elif not enabled and os.path.exists(on):
            os.replace(on, off)

    # -- Proton -------------------------------------------------------------------------------------------------------
    def proton_name(self):
        s = self.settings
        if s.proton:
            return os.path.expanduser(s.proton)
        tools = os.path.expanduser("~/.local/share/Steam/compatibilitytools.d")
        if os.path.isdir(os.path.join(tools, "GE-Proton10-28")):
            return "GE-Proton10-28"
        return "GE-Proton"  # umu picks (and fetches) the newest GE-Proton

    def game_environment(self):
        s = self.settings
        env = dict(os.environ)
        env["WINEPREFIX"] = s.prefix
        env["GAMEID"] = f"umu-{STEAM_APP_ID}"
        env["PROTONPATH"] = self.proton_name()
        env["STORE"] = "none"
        env.setdefault("PROTON_VERB", "waitforexitandrun")
        env.update(s.launch_env)
        return env

    def prepare_prefix(self):
        s, ui = self.settings, self.ui
        if os.path.isdir(os.path.join(s.prefix, "drive_c")):
            return
        if not shutil.which(s.runner):
            raise RuntimeError(f"{s.runner} was not found. Install umu-launcher (it runs the game with Proton).")
        ui.status("Preparing the Proton prefix (first time only, this can take a minute)...")
        os.makedirs(s.prefix, exist_ok=True)
        with open(os.path.join(s.log_dir, "game-client.log"), "ab") as log:
            subprocess.run([s.runner, ""], env=self.game_environment(), stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT, check=False)
        if self.report:
            self.report.step("Proton prefix prepared")

    def game_arguments(self):
        s = self.settings
        args = ["--nxapp=nxl", "--ip=127.0.0.1", f"--port={LOGIN_PORT}", f"--username={s.username}", f"--password={s.password}",
                f"--title={TITLE}", "--autologin", f"--mods={windows_path(s.mods_dir)}"]
        if s.client_console:
            args.append("--console")
        return args

    def launch_game(self):
        s = self.settings
        if not shutil.which(s.runner):
            raise RuntimeError(f"{s.runner} was not found. Install umu-launcher (it runs the game with Proton).")
        log = open(os.path.join(s.log_dir, "game-client.log"), "ab")
        log.write(f"--- {stamp()} {s.runner} {GAME_EXE} (Proton {self.proton_name()}, prefix {s.prefix})\n".encode())
        log.flush()
        process = subprocess.Popen([s.runner, os.path.join(s.client_dir, GAME_EXE)] + self.game_arguments(), cwd=s.client_dir,
                                   env=self.game_environment(), stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT)
        log.close()
        return process

    def wait_for_game(self, game):
        """Waits for the game to close. A game that quits within the first moments is reported as a start failure.
        The game may hand off to helper processes, so every MapleStory2.exe has to be gone too."""
        started = time.time()
        reported = set()
        while True:
            code = game.poll()
            if code is not None:
                if time.time() - started < 45 and code != 0:
                    self.log.write("error", f"game closed during start-up exit={code}")
                    raise RuntimeError(f"MapleStory 2 closed while starting (exit code {code}).\n\n"
                                       f"Log: {os.path.join(self.settings.log_dir, 'game-client.log')}")
                if not Client.running_pids():
                    break
            if time.time() - started > 20 and "window" not in reported and Client.running_pids():
                reported.add("window")
                if self.report:
                    self.report.step("Game process running")
                self.log.write("game", "running")
            for name in self.host.stopped_servers():
                if name not in reported:
                    reported.add(name)
                    if self.report:
                        self.report.problem(f"The {name} server stopped while the game was running")
                    self.log.write("error", f"{name} server stopped during play")
            time.sleep(1)

    def wait_for_save(self):
        """save-status.json: "active" once a character is in the world, then "committed" or "failed"."""
        s, ui, log = self.settings, self.ui, self.log
        started = time.time()
        asked = 0
        while True:
            state = None
            try:
                if os.path.exists(s.save_status_file):
                    state = json.load(open(s.save_status_file, encoding="utf-8"))
            except (OSError, ValueError):
                state = None
            if state is None and time.time() - started > 5:
                return "nothing"
            result = (state or {}).get("result") or (state or {}).get("Result")
            if result == "committed":
                return "committed"
            if result == "failed":
                log.write("save", f"failed character={state.get('characterId')} error={state.get('error')}")
                return "failed"
            if "Game" in self.host.stopped_servers():
                log.write("save", "game server stopped before confirming the save")
                return "failed"
            if time.time() - started > 60 * (asked + 1):
                asked += 1
                if asked > 5 or not ui.ask("Your progress is still being saved.\n\nKeep waiting? (No closes the servers now; progress since your last save may be lost.)", default=True):
                    return "unconfirmed"
            time.sleep(0.5)

    # -- install / update ---------------------------------------------------------------------------------------------
    def install_release(self, latest=None, source=None):
        """Installs the latest release (full Setup package, or the smaller update package when it applies), or a given file."""
        s, ui, log = self.settings, self.ui, self.log
        os.makedirs(s.update_dir, exist_ok=True)
        if source:
            package_path, full = source, "Update" not in os.path.basename(source)
        else:
            latest = latest or Releases.latest()
            if not (latest.get("manifest_url") and latest.get("signature_url")):
                raise RuntimeError("This release has no signed file list (release.json), so its download cannot be checked. "
                                    "Install it manually (README.md).")
            manifest = Releases.download(latest["manifest_url"], os.path.join(s.update_dir, "release.json"), ui, "release.json")
            signature = Releases.download(latest["signature_url"], os.path.join(s.update_dir, "release.json.sig"), ui, "release.json.sig")
            with open(manifest, "rb") as f, open(signature, "rb") as g:
                manifest_bytes = f.read()
                listed = Releases.verify_manifest(manifest_bytes, g.read())
            # Like Windows (UpdateChecker): the signed list must be this release's, not an older one served again.
            if json.loads(manifest_bytes).get("version", "").lstrip("vV") != latest["version"]:
                raise RuntimeError("The signed file list (release.json) belongs to another version. Nothing was installed.")

            installed = Releases.parse_version(s.version)
            use_update = latest["update_url"] and s.version != "dev"
            if use_update:
                package_path = Releases.download(latest["update_url"], os.path.join(s.update_dir, latest["update_name"]), ui, f"update {latest['version']}")
                Releases.check_listed(package_path, listed)
                with zipfile.ZipFile(package_path) as z:
                    try:
                        minimum = json.loads(z.read("update.json")).get("minVersion", "0")
                    except KeyError:
                        minimum = "999"
                use_update = Releases.parse_version(minimum) <= installed
                full = False
            if not use_update:
                if not latest["setup_url"]:
                    raise RuntimeError("The release has no Setup file to install from.")
                package_path = Releases.download(latest["setup_url"], os.path.join(s.update_dir, f"MapleStory2-Offline-Setup-{latest['version']}.exe"), ui, f"release {latest['version']}")
                Releases.check_listed(package_path, listed)
                full = True

        self.backup_save()
        ui.status(f"Installing from {os.path.basename(package_path)}...")
        with Releases.open_package(package_path) as package:
            count = Releases.install_package(package, self.home, ui, full)
        Releases.install_dotnet(self.home, ui)
        if not os.path.exists(os.path.join(self.home, "launcher.ini")):
            example = os.path.join(os.path.dirname(os.path.abspath(__file__)), "launcher.ini.example")
            if os.path.exists(example):
                shutil.copy(example, os.path.join(self.home, "launcher.ini"))
        self.settings = Settings(self.home)
        log.write("install", f"installed {self.settings.version} from {os.path.basename(package_path)} ({count} files)")
        ui.notify(f"Installed version {self.settings.version}.")
        try:
            os.remove(package_path)
        except OSError:
            pass

    # -- diagnostics ---------------------------------------------------------------------------------------------------
    def diagnostics(self, include_save, folder):
        s = self.settings
        folder = folder or (os.path.expanduser("~/Desktop") if os.path.isdir(os.path.expanduser("~/Desktop")) else os.path.expanduser("~"))
        path = os.path.join(folder, now().strftime("MapleStory2-Offline-Diagnostics-%Y%m%d-%H%M%S.zip"))
        info = [f"version {s.version}", f"proton {self.proton_name()}", f"prefix {s.prefix}", f"client {s.client_dir}",
                f"patch state {Client.proxy_state(s.client_dir, s.patch_dir) if s.client_dir else 'no client'}"]
        for command in (["uname", "-a"], [s.runner, "--version"], ["nvidia-smi", "--query-gpu=name,driver_version", "--format=csv,noheader"]):
            try:
                info.append(" ".join(command) + ": " + subprocess.run(command, capture_output=True, text=True, timeout=10, check=False).stdout.strip())
            except (OSError, subprocess.TimeoutExpired):
                pass
        try:
            info.append(open("/etc/os-release").read())
        except OSError:
            pass
        with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
            z.writestr("summary.txt", "\n".join(info) + "\n")
            for name in ("VERSION", "defaults.ini", "launcher.ini"):
                if os.path.exists(os.path.join(s.home, name)):
                    z.write(os.path.join(s.home, name), name)
            for sub in ("logs", "reports"):
                base = os.path.join(s.home, sub)
                for root, _, files in os.walk(base):
                    for f in files:
                        full = os.path.join(root, f)
                        z.write(full, os.path.relpath(full, s.home))
            if include_save and os.path.exists(s.save_file):
                z.write(s.save_file, "SaveData/game-server.db")
        return path

    def status(self):
        s = self.settings
        client = Client.find(s)
        print(f"Program folder : {s.home}")
        print(f"Version        : {s.version}")
        print(f"Servers        : {'installed' if os.path.exists(os.path.join(s.server_dir, 'Maple2.Server.World')) else 'NOT installed (run: ms2offline install)'}")
        print(f".NET runtime   : {'ok' if os.path.exists(s.dotnet) else 'missing (run: ms2offline install)'}")
        print(f"Game data      : {'ok' if os.path.exists(os.path.join(s.game_data_dir, 'maple-data.db')) else 'missing'}")
        if client:
            print(ContentPack.describe(os.path.normpath(os.path.join(client, "..")), s.content_pack_dir))
        print(f"Game client    : {client or 'NOT found (install it from Steam, or: ms2offline client --dir ...)'}")
        if client:
            print(f"Client patch   : {Client.proxy_state(client, s.patch_dir)}")
        print(f"Proton         : {self.proton_name()} (umu: {shutil.which(s.runner) or 'NOT installed'})")
        print(f"Proton prefix  : {s.prefix} ({'ready' if os.path.isdir(os.path.join(s.prefix, 'drive_c')) else 'created on first play'})")
        print(f"Save           : {s.save_file if os.path.exists(s.save_file) else 'none yet'}")
        running = Ports.own_server_pids(s)
        print(f"Servers running: {', '.join(map(str, running)) if running else 'no'}")
        listeners = [l for l in Ports.listeners() if not Ports.is_own_server(l, s)]
        if listeners:
            print("Ports held by other programs:")
            for port, pid, exe, cmdline in listeners:
                print(f"  port {port}: {os.path.basename(exe) or cmdline[:40]} (process {pid})")


def report_problem(launcher, note=None):
    """ms2offline report-problem: the player's own words with a report of the latest session; the confirmation after
    "what is sent" and the exact text is the consent. Returns the exit code."""
    s, ui = launcher.settings, launcher.ui
    if ProblemReport.url() is None:
        ui.error(f"This version cannot send problem reports. Please post the newest report from {s.report_dir} instead.")
        return 1
    names, unreadable = ProblemReport.names(s)
    if unreadable:
        ui.error(launcher.unreadable_save_text(unreadable) + " If the game is running, close it and try again.")
        return 1
    report = SessionReport(s)
    report.step("Report a problem (sent by the player)")
    raw = report.raw_text()
    if note is None and sys.stdin.isatty():
        note = input("What happened? (optional) ").strip()
    preview = launcher.write_preview(ProblemReport.for_upload(raw, names) +
                                     (f"\n\n-- Your note --\n{ProblemReport.note(note, names)}" if note else ""))
    if not ui.ask(f"{WHAT_IS_SENT}\n\nThe exact text: {preview}\n\nSend this report?", default=False):
        return 1
    path = launcher.queue_report("manual", raw, note or None, names)
    sent = set()
    launcher.send_queued_reports(sent)
    ui.info("Thank you! Your report was sent to the developer." if path in sent else
            "Your report is saved and will be sent the next time the launcher starts with internet." if os.path.exists(path) else
            "The report service did not take this report. Please report the problem on Discord instead.")
    return 0


def main(argv=None):
    parser = argparse.ArgumentParser(prog="ms2offline", description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--home", default=os.environ.get("MS2OFFLINE_HOME") or os.path.expanduser("~/Games/MapleStory2-Offline"),
                        help="program folder (default: ~/Games/MapleStory2-Offline or $MS2OFFLINE_HOME)")
    sub = parser.add_subparsers(dest="command")
    play = sub.add_parser("play", help="start the servers and the game (default)")
    play.add_argument("--servers-only", action="store_true", help="only the servers, until Ctrl+C (testing)")
    sub.add_parser("stop", help="stop this installation's servers")
    sub.add_parser("status", help="what is installed and running")
    install = sub.add_parser("install", help="install or reinstall from the latest release")
    install.add_argument("--from", dest="source", help="a downloaded Setup.exe or release zip instead of the download")
    sub.add_parser("update", help="install a newer release if there is one")
    client = sub.add_parser("client", help="find, set or install the game client")
    client.add_argument("--dir", help="the game folder (with MapleStory2.exe or its x64 folder)")
    client.add_argument("--steam", action="store_true", help="open the Steam install of MapleStory 2")
    sub.add_parser("prefix", help="create or refresh the Proton prefix")
    sub.add_parser("report", help="write a session report from the latest logs")
    problem = sub.add_parser("report-problem", help="send the developer a problem report with your own words")
    problem.add_argument("--note", help="what happened (asked for in a terminal when not given)")
    diag = sub.add_parser("diagnostics", help="zip logs and reports for a bug report")
    diag.add_argument("--include-save", action="store_true")
    diag.add_argument("--to", help="folder for the zip (default: Desktop)")
    sub.add_parser("unpatch", help="restore the game's own NxCharacter64.dll")
    args = parser.parse_args(argv)
    command = args.command or "play"

    launcher = Launcher(os.path.abspath(os.path.expanduser(args.home)))
    s, ui = launcher.settings, launcher.ui
    try:
        if command == "play":
            return launcher.play(servers_only=args.servers_only)
        if command == "stop":
            pids = Ports.own_server_pids(s)
            if pids:
                Ports.stop(pids)
                SaveBackup.checkpoint(s.save_file)
                ui.notify(f"Stopped {len(pids)} server process(es).")
            else:
                ui.status("No servers of this installation are running.")
            return 0
        if command == "status":
            launcher.status()
            return 0
        if command == "install":
            launcher.take_lock()
            if Ports.own_server_pids(s):
                raise RuntimeError("The servers are running. Run \"ms2offline stop\" first.")
            launcher.install_release(source=os.path.abspath(args.source) if args.source else None)
            ui.status("Installed. Next: ms2offline client (to find the game), then ms2offline play.")
            return 0
        if command == "update":
            launcher.take_lock()
            latest = Releases.latest()
            if Releases.parse_version(latest["version"]) <= Releases.parse_version(s.version):
                ui.status(f"Version {s.version} is current.")
                return 0
            ui.status(f"Version {latest['version']} is available (you have {s.version}).")
            if latest["notes"]:
                print(latest["notes"])
            if Ports.own_server_pids(s):
                raise RuntimeError("The servers are running. Run \"ms2offline stop\" first.")
            launcher.install_release(latest)
            return 0
        if command == "client":
            if args.steam:
                subprocess.Popen(["xdg-open", f"steam://install/{STEAM_APP_ID}"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                ui.status("Steam opened. When the download is done, run: ms2offline play")
                return 0
            if args.dir:
                found = Client.normalize(args.dir)
                if not found:
                    raise RuntimeError(f"{GAME_EXE} (with a Data folder beside its x64 folder) was not found in {args.dir}")
                s.save_user_value("ClientDir", found)
                ui.status(f"Game folder saved: {found}")
                return 0
            found = Client.find(s)
            ui.status(f"Game folder: {found}" if found else f"MapleStory 2 was not found. Install it from Steam (ms2offline client --steam) or set its folder with --dir.")
            return 0 if found else 1
        if command == "prefix":
            if os.path.isdir(os.path.join(s.prefix, "drive_c")):
                ui.status(f"Prefix exists: {s.prefix}; refreshing it with {launcher.proton_name()}.")
            launcher.prepare_prefix()
            subprocess.run([s.runner, ""], env=launcher.game_environment(), check=False)
            ui.status("Prefix ready.")
            return 0
        if command == "report":
            report = SessionReport(s)
            report.step("Report made by hand (ms2offline report) from the latest logs")
            path = report.write()
            ui.status(f"Report: {path}")
            return 0
        if command == "report-problem":
            return report_problem(launcher, args.note)
        if command == "diagnostics":
            path = launcher.diagnostics(args.include_save, args.to)
            ui.info(f"Saved:\n{path}\n\nAttach this file to your bug report.")
            return 0
        if command == "unpatch":
            if Client.running_pids():
                raise RuntimeError("MapleStory 2 is running. Close it first.")
            launcher.take_lock()
            client_dir = Client.find(s)
            if not client_dir:
                raise RuntimeError("MapleStory 2 was not found.")
            Client.remove_patch(client_dir, s.patch_dir)
            pack_note = ""
            if os.path.exists(os.path.join(s.content_pack_dir, ContentPack.FILE_NAME)):
                try:
                    removed = ContentPack.restore(os.path.normpath(os.path.join(client_dir, "..")), s.content_pack_dir)
                    pack_note = " Unified content removed." if removed else " The unified content could not be removed (the game folder is not writable)."
                except (RuntimeError, ValueError, OSError) as ex:
                    pack_note = f" The unified content was not removed: {ex}"
            ui.status(f"Client patch removed; {PROXY_DLL} is {Client.proxy_state(client_dir, s.patch_dir)}.{pack_note}")
            return 0
    except KeyboardInterrupt:
        return 130
    except Exception as ex:  # noqa: BLE001
        ui.error(str(ex))
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
