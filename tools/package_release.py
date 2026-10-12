"""Builds a player release of MapleStory 2 Offline.

  python tools/package_release.py 1.0.0 [--base publish/MapleStory2-Offline-1.0.0] [--notes "..."] [--no-build]

Creates in publish/:
  MapleStory2-Offline-<ver>/            the release folder (what players get)
  MapleStory2-Offline-Setup-<ver>.exe   installer for players (launcher + program zip appended)
  MapleStory2-Offline-<ver>.zip         full download (portable: extract and run)
  MapleStory2-Offline-Update-<ver>.zip  only with --base: every file changed since that release + update.json
                                        (cumulative: works on the base version and every patch after it)
  MapleStory2-Offline-<ver>/files.json  sha256 of every release file; keep it, the next --base needs it

The release never contains saves, logs, reports, launcher.ini, developer settings or the .NET SDK.
"""
import argparse
import hashlib
import json
import os
import shutil
import sqlite3
import subprocess
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DOTNET = os.path.join(ROOT, "dotnet", "dotnet.exe")
SERVERS = ["World", "Login", "Web", "Game"]
PROTECTED = ("SaveData/", "logs/", "reports/", "updates/", "launcher.ini")
ENV_DROP = {"MS2_DATA_FOLDER", "SQLITE_DIR", "SQLITE_DATA_DIR", "DB_IP", "DB_PORT", "DB_USER", "DB_PASSWORD"}

PLAYER_DEFAULTS = """# Default settings of this release (replaced by updates).
# To change one, put the same line in launcher.ini - that file is yours and updates never touch it.

# Solo-friendly rates (see GameRates in README.txt)
GrindRates=true
DungeonAllDrops=true
UnlimitedDungeonClears=true
MailNeverExpires=true
ClassDropsOnly=true

# Chat commands /godmode, /killall, /unstuck (see Cheats in README.txt)
CheatCommands=true

# Updates: ask = offer a newer version when you press Play (needs internet, otherwise skipped),
# always = install it without asking, off = only manual updates (Start menu > Install Update)
AutoUpdate=ask

# Problem reports on the desktop after every session
DebugReports=true

# Developer options
TestKit=false
TriggerTrace=false
"""

PLAYER_README = r"""MapleStory 2 (Offline) {version}
==============================

MapleStory 2 as a single-player game on your own PC. The game never connects to the internet; only the
launcher asks GitHub whether a newer version exists when you press Play (AutoUpdate, see Settings).

You need the MapleStory 2 game client (about 14 GB). If it is in your Steam library it is found
automatically. Otherwise the launcher shows where to get it the first time you start.

Install and play
----------------
Run "MapleStory2-Offline-Setup-{version}.exe" and click Install (no administrator rights needed).
Then start "MapleStory 2 Offline" from the desktop or the Start menu.
  - First start: it finds the game (or asks you once) and installs the offline patch into it.
    Windows may ask for permission if the game is under "Program Files".
  - When you close the game, the launcher saves and closes everything by itself.
(Portable alternative: extract the .zip anywhere and run "MapleStory 2 Offline.exe".)

Your character is stored in %LOCALAPPDATA%\MapleStory 2 Offline\SaveData (portable copy: the
SaveData folder). It is backed up on every start (SaveData\backups, newest 10 kept).

Updates
-------
Start menu > "MapleStory 2 Offline - Install Update" and pick the downloaded update zip.
(Or copy the zip, unopened, into the program folder and start the game.) The launcher checks the
file, backs up your save, and undoes everything if something goes wrong. Your save and settings
are never replaced.

Problems?
---------
Report bugs on Discord: https://discord.gg/bEEc9QsRx
Start menu > "MapleStory 2 Offline - Export Diagnostics" saves one zip file on your desktop - send
that file with your bug report. It works even when the game does not start. Your Windows user
name and PC name are removed; your save is only included if you say yes.
After every session a short report is also written to "MapleStory 2 Offline Reports" on the desktop.

Settings
--------
defaults.ini lists the options. To change one, put the line in launcher.ini in your data folder
(%LOCALAPPDATA%\MapleStory 2 Offline), for example:  GrindRates=false
  GrindRates             faster exp, more drops, upgrade materials from monsters
  DungeonAllDrops        dungeons drop every item of their loot tables
  UnlimitedDungeonClears dungeon clear rewards on every clear
  MailNeverExpires       mail stays until you delete it (otherwise it expires after 30 days)
  ClassDropsOnly         monsters and dungeon rewards only drop gear your class can wear
  CheatCommands          the chat commands below (CheatCommands=false turns them off)
  AutoUpdate             ask / always / off - online update check when you press Play
  ClientDir              the game's x64 folder, if you moved the game

Cheats
------
Some quests or places can get stuck in this offline version. Type these in the chat box:
  /godmode   on/off: you take no damage and every hit kills a monster
  /killall   kills every monster on the map (counts for quests, exp and drops)
  /unstuck   takes you back to Henesys (revives you first if you are dead)

Uninstall: Start menu > "Uninstall MapleStory 2 Offline" (asks before deleting your saves).

This is a non-commercial fan project, not affiliated with NEXON. MapleStory 2 is a trademark of
NEXON Korea Corporation. See LICENSES for the licenses of the included software.
"""


def run(args, **kwargs):
    print("  >", " ".join(args))
    subprocess.run(args, check=True, **kwargs)


def sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def copy(src, dst):
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    shutil.copy2(src, dst)


def copy_tree(src, dst, skip_ext=()):
    for folder, _, files in os.walk(src):
        for name in files:
            if name.lower().endswith(skip_ext):
                continue
            path = os.path.join(folder, name)
            copy(path, os.path.join(dst, os.path.relpath(path, src)))


def build(version):
    env = dict(os.environ, DOTNET_ROOT=os.path.dirname(DOTNET), DOTNET_CLI_TELEMETRY_OPTOUT="1")
    run([DOTNET, "build", os.path.join(ROOT, "Maple2", "Maple2.sln"), "-c", "Release", "-m:4", "-v:q", f"-p:Version={version}"], env=env)
    run([DOTNET, "publish", os.path.join(ROOT, "Launcher", "MapleStory2Offline.csproj"), "-c", "Release", "-v:q",
         "-r", "win-x64", "--self-contained", "true", "-p:PublishSingleFile=true",
         "-p:EnableCompressionInSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
         f"-p:Version={version}", "-o", os.path.join(ROOT, "publish", "launcher")], env=env)


def assemble(stage, version):
    if os.path.exists(stage):
        shutil.rmtree(stage)
    os.makedirs(stage)
    put = lambda rel: os.path.join(stage, rel)

    copy(os.path.join(ROOT, "publish", "launcher", "MapleStory 2 Offline.exe"), put("MapleStory 2 Offline.exe"))
    with open(put("VERSION"), "w", encoding="utf-8") as f:
        f.write(version)
    with open(put("defaults.ini"), "w", encoding="utf-8") as f:
        f.write(PLAYER_DEFAULTS)
    with open(put("README.txt"), "w", encoding="utf-8") as f:
        f.write(PLAYER_README.format(version=version))

    # Licenses of everything shipped.
    copy(os.path.join(ROOT, "LICENSE"), put("LICENSES/MapleStory2-Offline-AGPL-3.0.txt"))
    copy(os.path.join(ROOT, "Maple2", "LICENSE"), put("LICENSES/Maple2-AGPL-3.0.txt"))
    copy(os.path.join(ROOT, "_dl", "Agarcium", "LICENSE"), put("LICENSES/Agarcium-MIT.txt"))
    copy(os.path.join(ROOT, "dotnet", "LICENSE.txt"), put("LICENSES/dotnet-MIT.txt"))
    copy(os.path.join(ROOT, "dotnet", "ThirdPartyNotices.txt"), put("LICENSES/dotnet-ThirdPartyNotices.txt"))

    # Client patch and client mods (the test-kit mod ships disabled).
    for dll in ("AgarciumClient.dll", "NxCharacter64.dll"):
        copy(os.path.join(ROOT, "client-patch", dll), put(f"client-patch/{dll}"))
    copy_tree(os.path.join(ROOT, "client-mods"), put("client-mods"))
    testkit = put("client-mods/offline-testkit/mod.json")
    if os.path.exists(testkit):
        os.replace(testkit, testkit + ".disabled")

    # .NET runtime only (no SDK): what the servers need.
    dotnet = os.path.join(ROOT, "dotnet")
    copy(os.path.join(dotnet, "dotnet.exe"), put("dotnet/dotnet.exe"))
    copy_tree(os.path.join(dotnet, "host"), put("dotnet/host"))
    for framework in ("Microsoft.NETCore.App", "Microsoft.AspNetCore.App"):
        copy_tree(os.path.join(dotnet, "shared", framework), put(f"dotnet/shared/{framework}"))

    # Servers: project folder appsettings (working directory) + Release output.
    maple = os.path.join(ROOT, "Maple2")
    for server in SERVERS:
        project = os.path.join(maple, f"Maple2.Server.{server}")
        copy(os.path.join(project, "appsettings.json"), put(f"Maple2/Maple2.Server.{server}/appsettings.json"))
        copy_tree(os.path.join(project, "bin", "Release", "net8.0"), put(f"Maple2/Maple2.Server.{server}/bin/Release/net8.0"))
    copy_tree(os.path.join(maple, "Maple2.Server.Web", "Data"), put("Maple2/Maple2.Server.Web/Data"))
    for name in ("offline-economy.json", "offline-meret-catalog.json", "offline-grind.json", "offline-testkit.json", "offline-doctor-shop.json"):
        copy(os.path.join(maple, name), put(f"Maple2/{name}"))

    # Portable .env: no machine paths (the launcher passes the folders), no MySQL credentials.
    lines = ["# MapleStory 2 Offline server settings (paths are passed by the launcher)."]
    with open(os.path.join(maple, ".env"), encoding="utf-8-sig") as f:
        for line in f:
            line = line.strip()
            if line and not line.startswith("#") and "=" in line and line.split("=", 1)[0].strip() not in ENV_DROP:
                lines.append(line)
    with open(put("Maple2/.env"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")

    # Game data: a compact, consistent copy (VACUUM INTO), kept apart from the save folder.
    os.makedirs(put("GameData"))
    source = sqlite3.connect(f"file:{os.path.join(ROOT, 'SaveData', 'maple-data.db')}?mode=ro", uri=True)
    source.execute("VACUUM INTO ?", (put("GameData/maple-data.db"),))
    source.close()


SETUP_MAGIC = b"MS2OFFLINE-PAYLOAD-1"


def make_setup(launcher, payload_zip, target):
    """Setup.exe = the launcher with the program zip appended, then <payload length:int64 LE><magic>."""
    with open(target, "wb") as out_file:
        with open(launcher, "rb") as f:
            shutil.copyfileobj(f, out_file)
        with open(payload_zip, "rb") as f:
            shutil.copyfileobj(f, out_file, 1 << 20)
        out_file.write(os.path.getsize(payload_zip).to_bytes(8, "little") + SETUP_MAGIC)


def manifest(stage):
    files = {}
    for folder, _, names in os.walk(stage):
        for name in names:
            path = os.path.join(folder, name)
            rel = os.path.relpath(path, stage).replace("\\", "/")
            if rel != "files.json":
                files[rel] = sha256(path)
    with open(os.path.join(stage, "files.json"), "w", encoding="utf-8") as f:
        json.dump(files, f, indent=1, sort_keys=True)
    return files


def zip_folder(stage, zip_path, only=None, extra=None):
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for rel in sorted(only if only is not None else manifest_paths(stage)):
            archive.write(os.path.join(stage, rel), rel)
        for name, text in (extra or {}).items():
            archive.writestr(name, text)


def manifest_paths(stage):
    with open(os.path.join(stage, "files.json"), encoding="utf-8") as f:
        return list(json.load(f)) + ["files.json"]


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("version")
    parser.add_argument("--base", help="folder of the last FULL release (with its files.json) to build an update zip against")
    parser.add_argument("--notes", default="", help="one-line release notes shown to the player after updating")
    parser.add_argument("--no-build", action="store_true", help="package the existing builds")
    parser.add_argument("--sign-key", help="update signing key (default ../Signing/update-signing-key.pem)")
    args = parser.parse_args()

    out = os.path.join(ROOT, "publish")
    stage = os.path.join(out, f"MapleStory2-Offline-{args.version}")
    if not args.no_build:
        build(args.version)
    print("assembling", stage)
    assemble(stage, args.version)
    files = manifest(stage)

    full_zip = os.path.join(out, f"MapleStory2-Offline-{args.version}.zip")
    print("zipping", full_zip)
    zip_folder(stage, full_zip)

    setup = os.path.join(out, f"MapleStory2-Offline-Setup-{args.version}.exe")
    print("building", setup)
    make_setup(os.path.join(ROOT, "publish", "launcher", "MapleStory 2 Offline.exe"), full_zip, setup)

    if args.base:
        with open(os.path.join(args.base, "files.json"), encoding="utf-8") as f:
            base_files = json.load(f)
        with open(os.path.join(args.base, "VERSION"), encoding="utf-8") as f:
            base_version = f.read().strip()
        changed = [rel for rel, digest in files.items() if base_files.get(rel) != digest]
        deleted = [rel for rel in base_files if rel not in files and not rel.startswith(PROTECTED)]
        update = {"version": args.version, "minVersion": base_version, "delete": deleted, "notes": args.notes}
        update_zip = os.path.join(out, f"MapleStory2-Offline-Update-{args.version}.zip")
        print(f"update {base_version} -> {args.version}: {len(changed)} changed, {len(deleted)} deleted")
        zip_folder(stage, update_zip, only=changed + ["files.json"], extra={"update.json": json.dumps(update, indent=1)})
        import sign_update
        sign_update.sign(update_zip, args.sign_key)

    for name in sorted(os.listdir(out)):
        path = os.path.join(out, name)
        if os.path.isfile(path):
            print(f"{os.path.getsize(path) / 1048576:9.1f} MB  {name}")


if __name__ == "__main__":
    sys.exit(main())
