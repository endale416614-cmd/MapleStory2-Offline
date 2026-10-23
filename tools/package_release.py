"""Builds a player release of MapleStory 2 Offline.

  python tools/package_release.py 1.0.0 [--base publish/MapleStory2-Offline-1.0.0] [--notes "..."] [--no-build]

Creates in publish/:
  MapleStory2-Offline-<ver>/            the release folder (what players get)
  MapleStory2-Offline-Setup-<ver>.exe   installer for players (launcher + program zip appended), kept locally
  MapleStory2-Offline-Setup.exe         the same installer under the published name (README download link)
  MapleStory2-Offline-<ver>.zip         the program package inside the Setup (portable copy, kept locally)
  MapleStory2-Offline-Update-<ver>.zip  only with --base: every file changed since that release + update.json
                                        (cumulative: works on the base version and every patch after it)
  MapleStory2-Offline-<ver>/files.json  sha256 of every release file; keep it, the next --base needs it
  release.json + release.json.sig       with --base or --manifest: size and sha256 of the published Setup (and update zip), signed;
                                        upload both with the release (launchers from 1.1.9 check the update against it)
  upload-<ver>/                         exactly the files to publish: MapleStory2-Offline-Setup.exe, the update zip, release.json, release.json.sig

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
sys.path.insert(0, os.path.join(ROOT, "tools"))  # for sign_update: python -I does not add the script's folder
DOTNET = os.path.join(ROOT, "dotnet", "dotnet.exe")
SERVERS = ["World", "Login", "Web", "Game"]
PROTECTED = ("SaveData/", "logs/", "reports/", "updates/", "launcher.ini")
# Files a release after the --base shipped and a later one dropped: the base's files.json does not list them, so they
# are named here to be deleted by every cumulative update (the launcher skips names that are not installed).
RETIRED = (
    "Maple2/offline-doctor-shop.json",  # 1.1.2 Doctor Pask shop, removed in 1.1.3
    # Character portraits of the build PC that 1.1.0-1.1.2 shipped by mistake (exact names only; a player's own
    # portraits get new random names, so they are never touched). The 1.1.0 ones are already in its files.json.
    "Maple2/Maple2.Server.Web/Data/profiles/20000000004/360534ce-ba45-4e6a-a74e-fca994304ea6.png",
)
ENV_DROP = {"MS2_DATA_FOLDER", "SQLITE_DIR", "SQLITE_DATA_DIR", "DB_IP", "DB_PORT", "DB_USER", "DB_PASSWORD"}

PLAYER_DEFAULTS = """# Default settings of this release (replaced by updates).
# To change one, put the same line in launcher.ini - that file is yours and updates never touch it.

# Rules: Adapted = solo-friendly changes (faster grinding, more loot, unlimited dungeon clears, Epic dungeon gear,
# pet taming, party-size dungeons - see Settings in README.txt); Legacy = the original rules. The launcher asks at
# every start and writes the choice to launcher.ini. Single options (GrindRates, ...) set in launcher.ini win over it.
GameMode=Adapted

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
launcher asks GitHub whether a newer version exists when you press Play (AutoUpdate, see Settings), and
sends a problem report to the developer after a session with a problem (SendReports, see Problems?).

You need the MapleStory 2 game client (about 14 GB). If it is in your Steam library it is found
automatically. Otherwise the launcher shows where to get it the first time you start.

Install and play
----------------
Run "MapleStory2-Offline-Setup.exe" and click Install (no administrator rights needed).
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
name and PC name are removed; your save is only included if you say yes. This file is never uploaded.
After every session a short report is also written to "MapleStory 2 Offline Reports" on the desktop.

Problem reports: from 1.2.1 the launcher sends a report to the developer automatically after a session
with a problem; the first start shows a one-time notice where you can choose "Ask me each time" instead.
Every report helps the game's development. Untick "Send problem reports to the developer automatically"
in the "Choose how to play" window to be asked again. The notice and every question first show what a
report contains: error messages and server log lines, versions, launcher settings, basic PC details and
a random install number. Never sent: your Windows user name and PC name, character and account names,
chat, save and game files. "Report a problem..." (in that window; a new install also has it in the Start
menu) sends your own words with it. Without internet a report waits for the next start.

Settings
--------
defaults.ini lists the options. To change one, put the line in launcher.ini in your data folder
(%LOCALAPPDATA%\MapleStory 2 Offline), for example:  GrindRates=false
  GameMode               Picked in the "Choose how to play" window at every start (no need to edit this).
                         Adapted = built for playing alone (the options below); Legacy = the original online-era rules.
                         Both keep Meret pouches, the full Meret Market, mail that never expires and the chat commands.
                         An option below that you set yourself wins over the mode.
  GrindRates             faster exp, more drops, upgrade materials and Meret from monsters
  DungeonAllDrops        dungeons drop every item of their loot tables
  UnlimitedDungeonClears dungeon clear rewards on every clear
  MailNeverExpires       mail stays until you delete it (otherwise it expires after 30 days; on in both modes)
  PartyDungeons          party-size dungeons open to a smaller group or one player (not Cathy Mart, which needs 4)
  FairFight              bosses and group-dungeon monsters scaled for one player: each hit does at least a solo share of their health
  BlackMarket            the Black Market sells gear near your level, upgrade materials, gems and potions: a new pick every time you press Play
  UnifiedContent         the extra content of the content pack (1.2.0: new cosmetics); false puts the original game files back
  ClassDropsOnly         monsters and dungeon rewards only drop gear made for your class
  PetTaming              pet candy and snares from level 10, wild pets 3x as often, 10 candies capture a pet
  DungeonEpicGear        weapons, armor and accessories dropped in dungeons are at least Epic (where the item exists at Epic)
  CheatCommands          the chat commands below (on in both modes; set it to false to turn them off)
  AutoUpdate             ask / always / off - online update check when you press Play
  SendReports            always (default after the one-time notice) / ask / never - problem reports to the developer (see Problems?; launcher.ini only)
  ClientDir              the game's x64 folder, if you moved the game
                         With TestKit=true the admin chat commands work too: /warp <map id>, /goto <map name>, /npc <npc id>

Cheats
------
Some quests or places can get stuck in this offline version. Type these in the chat box:
  /godmode   on/off: you take no damage and every hit kills a monster
  /killall   kills every monster on the map (counts for quests, exp and drops)
  /unstuck   restarts the quest map you are in, anywhere else takes you back to Henesys (revives you first)
             /unstuck --town always goes to Henesys
  /freebies  on/off: everything you buy is free, whatever the currency
  /give      gives an item by name: /give Red Potion 5 (a name that fits several items lists their ids)
  /item      gives an item by id: /item 20000001 --amount 5 (add --rarity 1-6 to pick the rarity)
  /find      searches ids by name: /find item potion (also map, npc, quest, skill, achievement)

Also in the chat box, in both modes:
  /time      shows the in-game time, day, season and year (the world clock only runs while you play)

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


def add_content_pack(stage, pack_dir):
    """Unified content (Q-4 a): the folder from tools/build_content_pack.py --data-db ships as ContentPack/.
    Refuses a pack whose server data was not built for it (the launcher's data lock would stop every Play)."""
    with open(os.path.join(pack_dir, "pack.json"), encoding="utf-8") as f:
        pack = json.load(f)
        version = pack["version"]
    data_build = os.path.join(pack_dir, "GameData", "data-build.json")
    if not os.path.isfile(os.path.join(pack_dir, "GameData", "maple-data.db")) or not os.path.isfile(data_build):
        sys.exit(f"{pack_dir}: GameData/maple-data.db or GameData/data-build.json is missing (build the pack with --data-db)")
    with open(data_build, encoding="utf-8") as f:
        data_pack = json.load(f).get("contentPack")
    if data_pack != version:
        sys.exit(f"{pack_dir}: GameData was built for pack {data_pack}, but pack.json is {version}")
    for folder, _, names in os.walk(pack_dir):
        for name in names:
            if name == "apply-journal.json" or name.endswith(".tmp"):
                continue  # written by the launcher at runtime
            path = os.path.join(folder, name)
            copy(path, os.path.join(stage, "ContentPack", os.path.relpath(path, pack_dir)))

    # Write COMPATIBILITY.txt from pack.json
    compatibility_content = f"MapleStory 2 Offline content pack {version}\n\nThe pack applies to the Global (Steam) client whose archives are exactly these (path, size in bytes, SHA-256):\n"
    for archive in pack.get("archives", []):
        compatibility_content += f"  {archive['path']}  {archive['globalSize']}  {archive['globalSha256']}\n"
    compatibility_content += "\nWith any other build of these files (a game update, a modified client) the launcher starts the Global game without\nthe pack and says so; the pack is never applied over archives it does not know.\n"

    with open(os.path.join(stage, "COMPATIBILITY.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write(compatibility_content)

    return version


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

    # TROUBLESHOOTING.txt
    copy(os.path.join(ROOT, "TROUBLESHOOTING.txt"), put("TROUBLESHOOTING.txt"))

    # Licenses of everything shipped.
    copy(os.path.join(ROOT, "LICENSE"), put("LICENSES/MapleStory2-Offline-AGPL-3.0.txt"))
    copy(os.path.join(ROOT, "Maple2", "LICENSE"), put("LICENSES/Maple2-AGPL-3.0.txt"))
    copy(os.path.join(ROOT, "_dl", "Agarcium", "LICENSE"), put("LICENSES/Agarcium-MIT.txt"))
    copy(os.path.join(ROOT, "_dl", "Agarcium", "third_party", "minhook", "LICENSE.txt"), put("LICENSES/MinHook-BSD-2-Clause.txt"))
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
    # Only the tracked web data (shop banner). profiles/, guildmark/, blueprint/... are written at runtime by the
    # build PC's own game (character portraits) and must never ship.
    copy_tree(os.path.join(maple, "Maple2.Server.Web", "Data", "system"), put("Maple2/Maple2.Server.Web/Data/system"))
    for name in ("offline-economy.json", "offline-meret-catalog.json", "offline-grind.json", "offline-market.json", "offline-spawns.json",
                 "offline-testkit.json", "offline-triggers.json", "summon-npcs.json", "defensive-effects.json"):
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
    write_data_build(put("GameData/data-build.json"), put("GameData/maple-data.db"))


def write_data_build(path, db):
    """Record which data the shipped maple-data.db was built from, so a later release can reproduce or diff it:
    the ingest's per-table CRC32C checksums, the Maple2 commit, and (when MS2_DATA_FOLDER is set, as for the
    ingest) the sha256 of the input archives. File names only; no machine paths."""
    con = sqlite3.connect(f"file:{db}?mode=ro", uri=True)
    tables = dict(con.execute("SELECT TableName, Crc32C FROM checksum ORDER BY TableName"))
    con.close()
    commit = subprocess.run(["git", "-C", os.path.join(ROOT, "Maple2"), "rev-parse", "HEAD"],
                            capture_output=True, text=True).stdout.strip()
    inputs = {}
    data = os.environ.get("MS2_DATA_FOLDER")
    if data:
        for name in ("Xml.m2d", "Xml.m2h", "Server.m2d", "Server.m2h", "Resource/Exported.m2d"):
            if os.path.exists(os.path.join(data, name)):
                inputs[name] = sha256(os.path.join(data, name))
    with open(path, "w", encoding="utf-8") as f:
        json.dump({"maple2Commit": commit, "inputs": inputs, "tables": tables}, f, indent=1, sort_keys=True)
        f.write("\n")


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


def write_release(path, version, notes, assets):
    """release.json: name, size and sha256 of each release file (read by Launcher/ReleaseManifest.cs). File names only."""
    files = [{"name": os.path.basename(a), "size": os.path.getsize(a), "sha256": sha256(a)} for a in assets]
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump({"version": version, "files": files, "notes": notes}, f, indent=1)
        f.write("\n")


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
    parser.add_argument("--manifest", action="store_true", help="write and sign release.json also without --base (Setup-only release)")
    parser.add_argument("--sign-key", help="update signing key (default ../Signing/update-signing-key.pem)")
    parser.add_argument("--content-pack", help="content pack folder (build_content_pack.py --data-db) to ship as ContentPack/")
    args = parser.parse_args()

    out = os.path.join(ROOT, "publish")
    stage = os.path.join(out, f"MapleStory2-Offline-{args.version}")
    # A release.json left from an earlier run names another version; uploaded by mistake, launchers would refuse the update.
    for name in ("release.json", "release.json.sig"):
        if os.path.exists(os.path.join(out, name)):
            os.remove(os.path.join(out, name))
    if not args.no_build:
        build(args.version)
    print("assembling", stage)
    assemble(stage, args.version)
    if args.content_pack:
        print("content pack", add_content_pack(stage, args.content_pack))
    files = manifest(stage)

    full_zip = os.path.join(out, f"MapleStory2-Offline-{args.version}.zip")
    print("zipping", full_zip)
    zip_folder(stage, full_zip)

    setup = os.path.join(out, f"MapleStory2-Offline-Setup-{args.version}.exe")
    print("building", setup)
    make_setup(os.path.join(ROOT, "publish", "launcher", "MapleStory 2 Offline.exe"), full_zip, setup)

    # The README links the unversioned name, and the Linux launcher installs only files release.json lists (RC-03).
    alias = os.path.join(out, "MapleStory2-Offline-Setup.exe")
    print("copying", alias)
    shutil.copyfile(setup, alias)

    # Published (maintainer 2026-10-22): the Setup under its README name and, with --base, the update zip; the versioned Setup and the full zip stay local.
    assets = [alias]
    if args.base:
        with open(os.path.join(args.base, "files.json"), encoding="utf-8") as f:
            base_files = json.load(f)
        with open(os.path.join(args.base, "VERSION"), encoding="utf-8") as f:
            base_version = f.read().strip()
        # A file that went back to its base content after a later patch changed it must still ship,
        # or players on that patch keep the patched copy: compare against every release kept in publish\ too.
        others = [base_files]
        for name in os.listdir(out):
            other = os.path.join(out, name, "files.json")
            if name.startswith("MapleStory2-Offline-") and os.path.join(out, name) != stage and os.path.isfile(other):
                with open(other, encoding="utf-8") as f:
                    others.append(json.load(f))
        changed = [rel for rel, digest in files.items() if any(o.get(rel) != digest for o in others)]
        deleted = [rel for rel in base_files if rel not in files and not rel.startswith(PROTECTED)]
        deleted += [rel for rel in RETIRED if rel not in files and rel not in deleted]
        update = {"version": args.version, "minVersion": base_version, "delete": deleted, "notes": args.notes}
        update_zip = os.path.join(out, f"MapleStory2-Offline-Update-{args.version}.zip")
        print(f"update {base_version} -> {args.version}: {len(changed)} changed, {len(deleted)} deleted")
        zip_folder(stage, update_zip, only=changed + ["files.json"], extra={"update.json": json.dumps(update, indent=1)})
        import sign_update
        sign_update.sign(update_zip, args.sign_key)
        assets.append(update_zip)

    if args.base or args.manifest:
        import sign_update
        # After the update zip is signed: signing rewrites its comment, so its sha256 changes.
        release = os.path.join(out, "release.json")
        write_release(release, args.version, args.notes, assets)
        sign_update.sign_manifest(release, args.sign_key)

        # Exactly the files to upload, nothing else (a fresh folder of hard links to this run's outputs).
        upload = os.path.join(out, f"upload-{args.version}")
        if os.path.isdir(upload):
            shutil.rmtree(upload)
        os.makedirs(upload)
        for path in assets + [release, release + ".sig"]:
            os.link(path, os.path.join(upload, os.path.basename(path)))
        print("upload (publish exactly these):", ", ".join(sorted(os.listdir(upload))))

    for name in sorted(os.listdir(out)):
        path = os.path.join(out, name)
        if os.path.isfile(path):
            print(f"{os.path.getsize(path) / 1048576:9.1f} MB  {name}")


if __name__ == "__main__":
    sys.exit(main())
