# MapleStory 2 Offline: player guide

This guide is for MapleStory 2 Offline 1.2.2 on Windows. It explains how to install the game, the first start, the
two ways to play, updates, the content pack, your saves, problem reports, answers to questions players asked, and
what to do when something goes wrong.

Playing on Linux? The Linux launcher has its own guide: `linux/README.md` in the project's source code.

## What you need

- A PC with 64-bit Windows 10 or 11.
- The MapleStory 2 game client (about 14 GB). This project does not include it. If MapleStory 2 is installed with
  Steam, the launcher finds it. If not, the launcher shows you where to get it (see "First start").
- Free disk space:
  - about 3 GB on the drive you install to. Setup checks this first. The program itself takes about 2 GB.
  - about 0.5 GB on the game's drive. The content pack adds this much to the game's own files.
  - for an update, about twice its download size.

The game itself never connects to the internet. Only the launcher goes online: to check for a newer version (see
"Updates") and to send problem reports (see "Problem reports").

All versions are on the project's Releases page: https://github.com/endale416614-cmd/MapleStory2-Offline/releases

## Install

1. Open the Releases page (see above), then the newest version. Under "Assets", download
   `MapleStory2-Offline-Setup.exe` (about 1.1 GB). The Download link at the top of the project's page on GitHub gives
   the same file.
2. Run the downloaded file. If Windows says "Windows protected your PC" (the file is not signed), click **More info**,
   then **Run anyway**.
3. The Setup window shows the install folder. The default is `%LOCALAPPDATA%\Programs\MapleStory 2 Offline`. To use
   another folder, click **Change...** and pick a folder: Setup installs into a `MapleStory 2 Offline` folder inside
   it.
4. Click **Install**. No administrator rights are needed.
5. Setup asks "Start it now?". Click **Yes** to start the game now, or **No** to start it later.

Setup creates:

- a desktop shortcut **MapleStory 2 Offline**;
- a Start menu folder **MapleStory 2 Offline** with these shortcuts:
  - **MapleStory 2 Offline** (play),
  - **MapleStory 2 Offline - Export Diagnostics**,
  - **MapleStory 2 Offline - Report a Problem**,
  - **MapleStory 2 Offline - Install Update**,
  - **Uninstall MapleStory 2 Offline**;
- an entry in the Windows list of installed apps.

Running Setup again repairs the installation, and a newer Setup updates it. It replaces the program files and keeps
your saves and settings. Close MapleStory 2 before you run it.

### Where your files are

| Folder | What is in it |
|---|---|
| `%LOCALAPPDATA%\Programs\MapleStory 2 Offline` (or the folder you chose) | The program. Updates replace files here. |
| `%LOCALAPPDATA%\MapleStory 2 Offline` | Your data folder: saves (`SaveData`), your settings (`launcher.ini`), logs, reports and update backups. Updates never replace anything here. |
| `MapleStory 2 Offline Reports` on your desktop | A short report of every session. |

To open a folder: press the Windows key and R together, type the folder (for example
`%LOCALAPPDATA%\MapleStory 2 Offline`), then press Enter.

## First start

Always start the game with the **MapleStory 2 Offline** shortcut, not from Steam. The launcher starts the game's
servers on your PC first, then the game.

1. Start **MapleStory 2 Offline**. A small window shows what the launcher is doing.
2. The first time, a notice "Problem reports are now sent automatically" appears. Click **Keep it on** or **Ask me
   each time**. See "Problem reports".
3. If a newer version exists, the launcher offers it first (see "Updates").
4. The launcher looks for the game in your Steam libraries. If it does not find it, the window "MapleStory 2 was not
   found" offers three choices:
   - **Choose the game folder...**: pick the game's folder (the one with the `Data` and `x64` folders), or its `x64`
     folder.
   - **Get the game client**: opens the download page of the community downloader, Mushroom Launcher, in your
     browser. Download it, run it and click "Install client". When the client is installed, click **Choose the game
     folder...** and pick its folder.
   - **Install it with Steam**: only works if MapleStory 2 is still in your Steam library.

   The launcher remembers the folder. If you click **Cancel**, the launcher closes. Start it again when the client is
   ready.
5. The "Choose how to play" window opens. Pick a mode and a save, then click **Play**. See the next section.
6. The launcher backs up your save and prepares the content pack ("Preparing the unified content..."). The first
   time, it adds the pack to the game's files. If the game is in a protected folder such as "Program Files (x86)"
   (where Steam puts games by default), Windows asks for permission. Click **Yes**.
7. The launcher starts the servers ("Starting the world...", "Starting the login server...", "Loading Maple
   World..."). This can take a few minutes.
8. The first time, the launcher installs the offline patch into the game folder. If the game is in a protected
   folder, Windows asks for permission again. Click **Yes**: without the patch the game cannot play offline.
9. The game opens and logs in by itself. Create a character, or pick one.
10. Play. The launcher window hides while you play.
11. When you are done, close the game. The launcher saves your progress ("Saving your progress..."), stops the
    servers and closes by itself. Wait until it has closed.

The game also saves by itself while you play: at every map change and every 5 minutes.

## Choose how to play

This window opens at every start. Your last choice is already selected.

- **Legacy Mode**: the original online-era rules and balance: original exp and drop rates, dungeon clear limits and
  party requirements. Nothing is changed for solo play, so content built for a party stays closed.
- **Offline Adapted Mode**: built for playing alone: faster exp and more loot, unlimited dungeon clear rewards, Epic
  dungeon gear, easier pet taming, party-size dungeons open to you (except Cathy Mart, which needs a full group),
  bosses scaled for one player, and a Black Market with items for sale.
- **Save:** pick **Main save** or another save. **New game...** makes a new, empty save. Type its name (letters,
  digits, spaces, - or _, up to 32 characters, not "backups") and click **OK**. Each save has its own characters.
- **Send problem reports to the developer automatically**, **What is sent?** and **Report a problem...**: see
  "Problem reports".
- **Play** starts the game. **Quit** closes the launcher.

Your characters work in either mode. To switch modes, close the game and start it again, then pick the other mode.

### What each mode changes

| What | Legacy Mode | Offline Adapted Mode | launcher.ini line |
|---|---|---|---|
| Faster exp, more drops, upgrade materials from monsters | off | on | `GrindRates` |
| Dungeons drop every item of their loot tables | off | on | `DungeonAllDrops` |
| Dungeon clear rewards on every clear (up to 9999 a week and a day) | off | on | `UnlimitedDungeonClears` |
| Weapons, armor and accessories dropped in dungeons are at least Epic (where the item exists at Epic) | off | on | `DungeonEpicGear` |
| Monsters and dungeon rewards drop only gear made for your class | off | on | `ClassDropsOnly` |
| Pet taming: candy and snares from level 10, wild pets 3 times as often, 10 candies capture a pet | off | on | `PetTaming` |
| Party-size dungeons open to a smaller group or one player (not Cathy Mart, which needs 4) | off | on | `PartyDungeons` |
| Bosses and group-dungeon monsters scaled for one player | off | on | `FairFight` |
| Black Market sells gear near your level, upgrade materials, gems and potions (a new pick at every start) | off | on | `BlackMarket` |
| Cheat commands (`/godmode`, `/unstuck` and the others below) | off | on | `CheatCommands` |
| Mail never expires (otherwise it expires after 30 days) | on | on | `MailNeverExpires` |

Both modes keep Meret pouches (buy Meret with Mesos in town potion shops), the full Meret Market, mail that never
expires and the `/time` command.

Since 1.2.2 the cheat commands work in Offline Adapted Mode only. The original game had none, so Legacy Mode has none
either. To use them in Legacy Mode, put `CheatCommands=true` in launcher.ini (see below).

### Lines in launcher.ini win over the mode

`launcher.ini` in your data folder holds your own settings. The launcher makes this file at the first start. A line
you put there wins over the mode, both ways. For example, `CheatCommands=true` turns the cheat commands on in Legacy
Mode, and `GrindRates=false` turns the faster exp off in Offline Adapted Mode.

To change a setting:

1. Close the game and wait until the launcher has closed.
2. Open `%LOCALAPPDATA%\MapleStory 2 Offline\launcher.ini` with Notepad. Windows may hide the `.ini` ending and show
   the file as `launcher`.
3. Add the line on its own, for example `GrindRates=false`. Use `true` or `false`. To go back to the mode's default,
   delete the line.
4. Save the file and start the game.

Other lines you can use in launcher.ini:

| Line | What it does |
|---|---|
| `AutoUpdate=ask`, `always` or `off` | The online update check (see "Updates"). `ask` is the default. |
| `SendReports=always`, `ask` or `never` | Problem reports to the developer (see "Problem reports"). |
| `UnifiedContent=true` or `false` | The content pack on or off (see "The content pack"). `true` is the default. |
| `DebugReports=true` or `false` | The session report on your desktop after every session. `true` is the default. With `false`, no problem report goes to the developer after a session either. |
| `ClientDir=` the game's `x64` folder | Where the game is, if you moved it. |
| `BackupsToKeep=10` | How many automatic save backups are kept. |

The "Choose how to play" window writes `GameMode` and `Profile` (your save) by itself. `TestKit` and `TriggerTrace`
are developer options: leave them off. `defaults.ini` in the program folder lists this release's defaults. Updates
replace it, so do not change it: put your own lines in launcher.ini, which updates never touch.

## Cheat commands

Some quests or places can get stuck in this offline version. In Offline Adapted Mode, or with `CheatCommands=true`,
type these in the chat box:

| Command | What it does |
|---|---|
| `/godmode` | On or off: you take no damage and every hit kills a monster. |
| `/killall` | Kills every monster on the map. It counts for quests, exp and drops. |
| `/unstuck` | Revives you first if needed. On a quest map it restarts that map. On an open field map it takes you back to the map's start. Anywhere else (a dungeon, your home) it takes you to Henesys. |
| `/unstuck --town` | Always takes you to Henesys. |
| `/freebies` | On or off: everything you buy is free, whatever the currency. |
| `/give` | Gives an item by name: `/give Red Potion 5`. A name that fits several items lists their ids. |
| `/item` | Gives an item by id: `/item 20000001 --amount 5`. Add `--rarity 1` to `--rarity 6` to pick the rarity. |
| `/find` | Searches ids by name: `/find item potion` (also `map`, `npc`, `quest`, `skill`, `achievement`). |

When the cheat commands are off, the game answers "Cheats are off. Set CheatCommands=true in launcher.ini and restart the game."

In both modes, `/time` shows the in-game time, day, season and year. The world clock only runs while you play.

## Updates

### Automatic updates

Each time you start the game, before the "Choose how to play" window, the launcher asks GitHub whether a newer
version exists. The check sends nothing about you or your PC, only the launcher's version. Without internet the
check is skipped and the game starts as usual.

When a newer version exists (with the default `AutoUpdate=ask`):

1. The launcher shows "Version ... is available", what changed and the download size. Click **Yes** to update, or
   **No** to play the installed version. It asks again at the next start.
2. The launcher downloads the update, checks it, backs up your save and installs it.
3. It shows "Updated to version ...". If the launcher itself changed, it restarts by itself. Then the game starts as
   usual.

Some versions can only be installed with their full Setup. The launcher then offers to open the download page.

With `AutoUpdate=always` the launcher installs updates without asking. With `AutoUpdate=off` it never checks.

### Updating by hand

1. Download the update file (`MapleStory2-Offline-Update-` and the version, a `.zip` file) from the project's
   Releases page. Do not unzip it.
2. Close the game and wait until the launcher has closed.
3. Start menu: **MapleStory 2 Offline - Install Update**. Pick the downloaded zip.
4. The launcher shows "Updated to version ...".

Or copy the zip, unopened, into the program folder, then start the game. The launcher installs it at the start.

An update file only works on the versions it was made for. If your version is too old, the launcher says so: then
download and run the full Setup instead. It keeps your save.

### What an update keeps

- Your data folder: saves, `launcher.ini`, logs and reports. An update never changes them.
- A checked copy of the save you played last, made before anything changes. It is kept in the `updates` folder of
  your data folder (`backup-before-`, the version, the date and the time).
- Your old program files: each one is backed up before it is replaced. If an update is interrupted (a crash, a power
  cut), the next start undoes it and tells you.

The launcher only installs updates signed by the MapleStory 2 Offline developer. A file that is not signed, was
changed or is damaged is refused, and nothing changes. It never installs a version older than the one you have.

If the game's servers do not start after an update, the launcher offers to go back to the version before it. The
save you played last before the update is put back too, from its copy in the `updates` folder. Other saves stay as
they are. An update installed by a version before 1.2.2, such as the update to 1.2.2 itself, did not note which save
the copy is from: going back from it puts no save back, and the message names the copy (see "Restore a backup").

## The content pack

The content pack (the launcher calls it "the unified content") adds content of the Korean and Japanese versions of
the game to the Global game:

- batch 1, since 1.2.0: cosmetics;
- batch 2, since 1.2.1: weapons, armor and accessories.

It is in the `ContentPack` folder of the program. `COMPATIBILITY.txt` in the program folder names the pack's version
and the exact game files it fits. It is on by default.

### How it is applied

At every start, after the "Choose how to play" window, the launcher shows "Preparing the unified content...". When
the pack is not applied yet, the launcher adds it to the end of the game's data files in the game folder and puts the
pack's index files in place. It keeps the game's own index files next to them, so the pack can be removed again. If
the game is in a protected folder such as "Program Files (x86)", Windows asks for permission.

The pack only fits the Global (Steam) game whose files are exactly those listed in `COMPATIBILITY.txt`. With any other
version of those files (a game update, a changed client), the launcher starts the Global game without the pack and
tells you. It never writes the pack over files it does not know.

### How to turn it off or remove it

1. Close the game and wait until the launcher has closed.
2. Put `UnifiedContent=false` in launcher.ini.
3. Start the game. The launcher removes the pack from the game files ("Checking the game files...") and you play the
   Global game. If the game is in a protected folder, Windows asks for permission. Click **Yes**.

To turn it on again, delete that line (or write `UnifiedContent=true`) and start the game. Uninstalling also removes
the pack from the game folder.

Items, furniture and mail attachments from the pack are not lost while the pack is off. They stay in your save and
show again once the pack is back on.

## Saves and backups

### Where your save is

- The main save: `%LOCALAPPDATA%\MapleStory 2 Offline\SaveData\game-server.db`.
- A save you made with **New game...**: `SaveData\` and the save's name, then `game-server.db`.

### Automatic backups

At every start, before the servers start, the launcher makes a checked copy of the save you picked. It goes into
the `backups` folder next to the save (`SaveData\backups` for the main save), named `game-server-` and the date and
time. The newest 10 are kept (`BackupsToKeep` changes that number).

If your save fails its check, the launcher warns you: "Your save failed its integrity check". Click **No** to stop,
then restore a backup (see below).

### Make your own backup

1. Close the game and wait until the launcher has closed.
2. Open `%LOCALAPPDATA%\MapleStory 2 Offline`.
3. Copy the `SaveData` folder to a safe place, for example a USB drive. To keep your settings too, copy the whole
   folder.

### Restore a backup

1. Close the game and wait until the launcher has closed.
2. In File Explorer, turn on file name extensions, so you see the `.db` endings. Windows 11: **View**, **Show**,
   **File name extensions**. Windows 10: the **View** tab, **File name extensions**.
3. Open the folder of the save, for example `%LOCALAPPDATA%\MapleStory 2 Offline\SaveData` for the main save.
4. Move `game-server.db` to a safe place. If you see files named `game-server.db-wal` or `game-server.db-shm` next to
   it, move them too.
5. Copy the backup you want (for example from the `backups` folder) into the save's folder.
6. Rename the copy to `game-server.db`.
7. Start the game.

Keep the file you moved away until you are sure the restored save is the one you want.

### What keeps your save

- An update never replaces your save. It backs up the save you played last first.
- Running Setup again keeps it.
- Turning the content pack off keeps the pack's items in it.
- Uninstalling asks first. Choose **No** to keep your saves (see "Uninstall").

## Uninstall

1. Close MapleStory 2.
2. Start menu: **Uninstall MapleStory 2 Offline** (or remove it from the Windows list of installed apps).
3. Click **Yes** to confirm.
4. The launcher puts the game's own files back: it removes the offline patch and the content pack from the game
   folder. If the game is in a protected folder, Windows asks for permission. Click **Yes**.
5. It asks "Also delete your saves, settings and logs?". Click **No** to keep them: a later install continues where
   you left off. **Yes** deletes the data folder.

If the content pack could not be removed, the launcher tells you. Then verify the game files in Steam.

## Problem reports

### The session report on your desktop

After every session the launcher writes a short report into the folder **MapleStory 2 Offline Reports** on your
desktop. Its name is `Report`, the date and the time; it ends with ` - PROBLEM` when something went wrong. The newest
15 are kept. Your Windows user name and PC name are removed from it.

When a session had a problem, the launcher says "Something went wrong during this session." and names the report.
A report names a section of `TROUBLESHOOTING.txt` (in the program folder) next to each problem it knows, for example
[T3].

### Reports to the developer

Since 1.2.1 the launcher sends a report to the developer after a session with a problem (a crash, a stuck loading
screen, server errors), or when the launcher itself fails. Every report helps fix bugs for every player.

- The first start shows the notice "Problem reports are now sent automatically". **Keep it on** sends them
  automatically. **Ask me each time** asks after each session with a problem.
- In the "Choose how to play" window, the box **Send problem reports to the developer automatically** shows your
  choice. Ticking it first shows what a report contains; click **Turn on** to confirm. Unticking it means the launcher
  asks you each time.
- When the launcher asks ("Send a problem report to the developer?"), click **Send** or **Don't send**.
  **See exactly what would be sent** opens the exact text in Notepad. Tick **Do this every time (no more questions)**
  to stop the question.
- `SendReports=never` in launcher.ini turns reports off.
- Without internet a report waits and goes out at a later start.

### Send a report yourself

There is no report command inside the game. Use the launcher:

1. Close the game.
2. Start menu: **MapleStory 2 Offline - Report a Problem**. Or click **Report a problem...** in the "Choose how to
   play" window.
3. Write what happened, in a few words (you can leave it empty).
4. To check the text first, click **See exactly what will be sent**.
5. Click **Send**. The launcher says "Thank you! Your report was sent to the developer.", or that it will send it the
   next time it starts with an internet connection.

No **Report a Problem** shortcut in your Start menu? Use the link in the "Choose how to play" window, or run Setup
again: it adds the shortcut.

### What a report contains

A report contains:

- what went wrong: error messages, and the last lines of the game servers' logs and map scripts (the maps, monsters,
  items and quests of your session);
- when you played: the session's date and times, and the launcher's steps;
- your MapleStory 2 Offline and content pack versions, your launcher settings, install folders and save size;
- your Windows version and basic PC details: processor, graphics card and driver, memory, screen size, free disk
  space;
- if the game crashed: which part of it crashed (program and module names, versions, error code and address from the
  Windows event log);
- your last character's level, job and map (not its name);
- which online addresses the game tried to reach (they are blocked offline);
- a random install number, so reports from the same PC can be grouped.

Never sent: your Windows user name and PC name; your character, account and save names; your chat; what the game
client itself writes (its log and its messages); your save file and your game files.

Reports go only to the MapleStory 2 Offline developer and are used only to find and fix bugs.

### Bug reports on Discord

Report bugs on Discord: https://discord.gg/bEEc9QsRx

To attach the details:

1. Start menu: **MapleStory 2 Offline - Export Diagnostics**. It works even when the game does not start.
2. The launcher asks whether to include your save. It helps to reproduce a problem, but it contains your characters.
   **No** is the default.
3. It saves one file on your desktop, `MapleStory2-Offline-Diagnostics-` with the date and time (a `.zip` file), and
   shows it in Explorer.
4. Attach that file to your bug report.

Your Windows user name and PC name are removed from it. The launcher never uploads this file: only you can send it.

## Questions players asked

### Where are Lith Harbor's mining and foraging islands?

This is not a bug. Berg Island and Alkimi Island open later:

- **Berg Island** (mining): reach Mining rank 5 (the achievement "Veteran Miner"). A letter arrives in your postbox:
  the quest "Donald's Invitation". Follow the quests ("To Berg Island").
- **Alkimi Island** (foraging): reach Foraging rank 5 (the achievement "Veteran Gatherer"). A letter arrives in your
  postbox: the quest "Alkimi's Invitation". Follow the quests ("To Alkimi Island").

You reach the islands through portals in Lith Harbor. The quests cannot be missed.

### Do my characters work in both modes?

Yes. Your characters work in either mode. To switch, close the game, start it again and pick the other mode.

### Can I keep a separate game?

Yes. Click **New game...** in the "Choose how to play" window and give the save a name. Each save has its own
characters and its own backups.

## Troubleshooting

| Problem | What to do |
|---|---|
| You are stuck in a quest or a place. | Type `/unstuck` in the chat box. In Legacy Mode, first put `CheatCommands=true` in launcher.ini and start the game again. For a map script that does not go on, leave the map and enter it again. |
| "Windows protected your PC" | Click **More info**, then **Run anyway**. |
| "Not enough free space on ..." (Setup) | Free up the space it names on that drive, or click **Change...** and pick another drive. |
| "MapleStory 2 was not found" | Choose the game's folder, or get the client (see "First start"). If you moved the game, the launcher asks again. |
| "MapleStory2.exe was not found in that folder." | Pick the game's folder (the one with the `Data` and `x64` folders), or its `x64` folder. |
| "MapleStory 2 (Offline) is already running." | The launcher is already open; it hides while you play. Close the game, wait until the launcher has closed, then start again. |
| "MapleStory 2 is already running. Close it first." | Close the game, then start the launcher again. If you see no game window, end `MapleStory2.exe` in Task Manager. |
| "Another program is using a network port MapleStory 2 Offline needs" | Close the program it names (or another copy of MapleStory 2 Offline), or restart the PC, then start again. Servers left over from an earlier session of this install are closed by the launcher itself. |
| "The offline patch could not be installed into the game folder" | Start the launcher again and click **Yes** at the Windows prompt. |
| "MapleStory 2 closed while starting" | The offline patch or the game files may be damaged. Verify the game files in Steam, then start again: the launcher installs the patch again. |
| "MapleStory 2 has not opened a window yet. Keep waiting?" | Click **Yes** to wait longer. **No** closes the game. |
| The game's servers do not start after an update. | Click **Yes** when the launcher offers to go back to the version before it. |
| "The game servers are still running. Close the game, wait a moment and try again." (Install Update) | Wait until the launcher has closed, then try again. |
| "Your save failed its integrity check" | Click **No**, then restore a backup (see "Saves and backups"). |
| "Your progress from this session could not be saved. Your previous save was kept." | Use **MapleStory 2 Offline - Export Diagnostics** and post the file with your report on Discord. |
| "Your progress is still being saved. Keep waiting?" | Click **Yes**. **No** closes the servers, and progress since your last save may be lost. |
| "The unified content does not fit this game client" | The game starts without the content pack. Verify the game files in Steam to use it again. |
| "The unified content is only partly applied" | Start the game again. If it repeats, verify the game files in Steam. |
| "The unified content could not be prepared" | Close Steam and any virus scan of the game folder, then start the game again. |
| "The unified content could not be removed from the game folder" | Start the game again and click **Yes** at the Windows prompt, or put `UnifiedContent=true` back in launcher.ini. |
| An update is refused ("is not signed by the MapleStory 2 Offline developer") | Get updates only from the project's Releases page. Nothing was changed. |
| A report names "Save file in use by another program" [T5] | Close the other launcher, and exclude the `SaveData` folder from virus scans and backup or sync tools. |

`TROUBLESHOOTING.txt` in the program folder explains every problem a session report names ([T1] to [T7]). If your
problem is not there, post the whole report on Discord.

---

MapleStory 2 Offline is a non-commercial fan project, not affiliated with NEXON. MapleStory 2 is a trademark of NEXON
Korea Corporation.
