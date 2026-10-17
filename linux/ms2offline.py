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
  diagnostics     zip the logs and reports for a bug report (--include-save adds your save)
  unpatch         put the game's own NxCharacter64.dll back
"""
import argparse
import ctypes
import datetime
import fcntl
import hashlib
import io
import json
import os
import queue
import re
import shutil
import signal
import sqlite3
import struct
import subprocess
import sys
import threading
import time
import urllib.request
import zipfile

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


def now():
    return datetime.datetime.now()


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
class Settings:
    def __init__(self, home):
        self.home = home
        values = {}
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
        self.class_drops_only = self._flag("ClassDropsOnly", None)
        self.pet_taming = self._flag("PetTaming", None)
        self.dungeon_epic_gear = self._flag("DungeonEpicGear", None)
        self.debug_reports = self._flag("DebugReports", True)
        self.backups_to_keep = self._int("BackupsToKeep", 10)
        auto = values.get("AutoUpdate", "off").strip().lower()
        self.auto_update = "ask" if auto in ("ask", "true", "yes", "on") else "always" if auto in ("always", "auto", "silent") else "off"
        # Linux-only settings
        self.proton = values.get("Proton", "").strip()
        self.prefix = os.path.expanduser(values.get("Prefix", "").strip()) or os.path.join(home, "prefix")
        self.runner = values.get("Runner", "umu-run").strip() or "umu-run"
        self.client_console = self._flag("ClientConsole", False)
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
    def save_dir(self): return os.path.join(self.home, "SaveData")
    @property
    def save_file(self): return os.path.join(self.save_dir, "game-server.db")
    @property
    def backup_dir(self): return os.path.join(self.save_dir, "backups")
    @property
    def game_data_dir(self): return os.path.join(self.home, "GameData")
    @property
    def update_dir(self): return os.path.join(self.home, "updates")
    @property
    def save_status_file(self): return os.path.join(self.save_dir, "save-status.json")

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
            subprocess.run(["zenity", "--error", "--title", TITLE, "--no-wrap", "--text", text], check=False)
        elif shutil.which("notify-send"):
            subprocess.run(["notify-send", "-u", "critical", "-a", APP_NAME, "-i", self.icon, TITLE, text], check=False)

    def info(self, text):
        print(f"\n{text}\n", flush=True)
        if not self.gui:
            return
        if shutil.which("kdialog"):
            subprocess.run(["kdialog", "--title", TITLE, "--msgbox", text], check=False)
        elif shutil.which("zenity"):
            subprocess.run(["zenity", "--info", "--title", TITLE, "--no-wrap", "--text", text], check=False)

    def ask(self, text, default=False):
        """Yes/No. A dialog when there is a desktop, the terminal otherwise, the default when neither can ask."""
        if self.gui and shutil.which("kdialog"):
            return subprocess.run(["kdialog", "--title", TITLE, "--yesno", text], check=False).returncode == 0
        if self.gui and shutil.which("zenity"):
            return subprocess.run(["zenity", "--question", "--title", TITLE, "--no-wrap", "--text", text], check=False).returncode == 0
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
        if os.path.exists(os.path.join(s.game_data_dir, "maple-data.db")):
            env["SQLITE_DATA_DIR"] = s.game_data_dir
        env["OFFLINE_PROFILE"] = s.game_mode
        flags = {
            "OFFLINE_TESTKIT": s.test_kit, "OFFLINE_TRIGGER_TRACE": s.trigger_trace,
            "OFFLINE_DUNGEON_ALL_DROPS": s.dungeon_all_drops, "OFFLINE_UNLIMITED_DUNGEON_CLEARS": s.unlimited_dungeon_clears,
            "OFFLINE_GRIND": s.grind_rates, "OFFLINE_MAIL_NEVER_EXPIRES": s.mail_never_expires,
            "OFFLINE_CLASS_DROPS": s.class_drops_only, "OFFLINE_CHEATS": s.cheat_commands,
            "OFFLINE_PET_TAMING": s.pet_taming, "OFFLINE_DUNGEON_EPIC_GEAR": s.dungeon_epic_gear,
            "OFFLINE_PARTY_DUNGEONS": s.party_dungeons,
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
        try:
            with open(path, "w", encoding="utf-8") as f:
                f.write("\n".join(lines) + "\n")
        except OSError:
            return None
        for old in sorted(os.listdir(s.report_dir), reverse=True)[20:]:
            try:
                os.remove(os.path.join(s.report_dir, old))
            except OSError:
                pass
        return path


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
        for asset in data.get("assets", []):
            name = asset.get("name", "")
            url = asset.get("browser_download_url", "")
            if not url.startswith("https://github.com/"):
                continue
            if name == "MapleStory2-Offline-Setup.exe" or (name.startswith("MapleStory2-Offline-Setup") and name.endswith(".exe")):
                setup_url = url
            elif name.startswith("MapleStory2-Offline-Update-") and name.endswith(".zip"):
                update_url, update_name, update_size = url, name, asset.get("size", 0)
        notes = "\n".join("• " + line.strip()[2:].replace("**", "") for line in data.get("body", "").splitlines()
                          if line.strip().startswith(("- ", "* ")))[:1500]
        return {"version": version, "notes": notes, "page": data.get("html_url", ""), "setup_url": setup_url,
                "update_url": update_url, "update_name": update_name, "update_size": update_size}

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
            return 1
        if report and report.has_problems and report_path:
            ui.error("Something went wrong during this session." + hint)
        else:
            ui.status("Done.")
        return 0

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
            installed = Releases.parse_version(s.version)
            use_update = latest["update_url"] and s.version != "dev"
            if use_update:
                package_path = Releases.download(latest["update_url"], os.path.join(s.update_dir, latest["update_name"]), ui, f"update {latest['version']}")
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
        if command == "diagnostics":
            path = launcher.diagnostics(args.include_save, args.to)
            ui.info(f"Saved:\n{path}\n\nAttach this file to your bug report.")
            return 0
        if command == "unpatch":
            client_dir = Client.find(s)
            if not client_dir:
                raise RuntimeError("MapleStory 2 was not found.")
            Client.remove_patch(client_dir, s.patch_dir)
            ui.status(f"Client patch removed; {PROXY_DLL} is {Client.proxy_state(client_dir, s.patch_dir)}.")
            return 0
    except KeyboardInterrupt:
        return 130
    except Exception as ex:  # noqa: BLE001
        ui.error(str(ex))
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
