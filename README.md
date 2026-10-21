# MapleStory 2 Offline

A single-player, fully offline edition of MapleStory 2 for Windows. It runs a local copy of the community
[Maple2](https://github.com/MS2Community/Maple2) server emulator next to the official game client, so the game can be
played again after the official servers closed in 2020. The game never connects to the internet; only the launcher
checks GitHub for a newer version when you press Play (and skips that when you are offline), and sends a problem
report to the developer only when you agree (see "Found a bug?").

## Download

### [⬇ Download MapleStory 2 Offline 1.2.0 (Setup, about 1.1 GB)](https://github.com/endale416614-cmd/MapleStory2-Offline/releases/latest/download/MapleStory2-Offline-Setup.exe)

1. Run the downloaded file and click **Install**. No administrator rights are needed.
2. Start the game from the **MapleStory 2 Offline** icon on your desktop.
3. Windows may say "Windows protected your PC" because the file is not signed: click **More info**, then **Run anyway**.

You also need the MapleStory 2 game client (about 14 GB). If it is in your Steam library, the launcher finds it.
Otherwise the launcher points you to the community downloader ([Mushroom Launcher](https://github.com/shuabritze/mushroom-launcher/releases),
"Install client"), or you choose any folder that holds the client. This project does not include the game client.

Updates install themselves: when you press Play and a newer version exists, the launcher asks and updates (your save
is backed up first). Prefer to update by hand? Put `AutoUpdate=off` in `launcher.ini`, download the update file and use
Start menu → **MapleStory 2 Offline - Install Update**. All versions: [Releases](https://github.com/endale416614-cmd/MapleStory2-Offline/releases).

**Still on 1.1.8 or older?** Install 1.2.0 once with the Setup above; it keeps your save. Launchers before 1.1.9 cannot take
an automatic update (the update signing key was replaced); from 1.1.9 on, updates install themselves.

### Found a bug?

Report it on **[Discord](https://discord.gg/bEEc9QsRx)** (or open an [issue](https://github.com/endale416614-cmd/MapleStory2-Offline/issues)).
Run Start menu → **MapleStory 2 Offline - Export Diagnostics** first and attach the file it saves on your desktop;
it works even when the game does not start.

**Problem reports (from 1.2.0):** after a session with a problem the launcher asks whether it may send a report to the
developer; tick **Send problem reports to the developer automatically** in the "Choose how to play" window to skip the
question. Every question first shows what a report contains: error messages and server log lines, versions, launcher
settings, basic PC details and a random install number. Never your Windows user name, PC name, character or account
names, chat, save or game files. **Report a problem...** (in that window; a new install also has it in the Start menu)
sends your own words with it. Without internet a report waits and goes out at the next start; `SendReports=ask`,
`always` or `never` in `launcher.ini` changes the choice. The diagnostics file is never uploaded.

> The green **Code → Download ZIP** button gives you the source code only, which cannot be played. Use the download link above.

**Linux:** the same servers run natively and the game runs under Proton. See [linux/README.md](linux/README.md).

## Vision

Make the game 100% playable and give players full control over their game, characters and progress. In order of
priority:

1. 100% playable: all Epic Quests, dungeons and bosses.
2. Korean edition: translate the Korean content and merge it with the Global edition.
3. Quality of life: smoother systems, shaped by community feedback.
4. Co-op wLAN RPG, not an MMORPG: like Stardew Valley's co-op, your character is a save on your own PC. Join a
   friend's game over wLAN or a community server, then take your character and everything it earned back home.
5. Modding tools for customizing and expanding your own game.

Own your game. Own your character. Keep your progress. The full plan is in [VISION.md](VISION.md).

## Features

- One-click launcher: starts the local servers, the game, saves your progress on exit and shuts everything down.
- Save data in a single local file (SQLite), backed up on every start.
- Signed updates: the launcher only installs updates signed with the project's update key.
- The client is locked to this PC: connections to anything except `127.0.0.1` are blocked.
- Earnable Meret (bought with Mesos) and a Meret Market listing the former cash and capsule items, sorted for browsing: every
  capsule under Featured, separate Design Templates and Furniture tabs.
- Solo-friendly options: adjustable exp and drop rates, upgrade-material drops, dungeon reward options.
- Two ways to play, picked in a "Choose how to play" window every time you start the game (your last choice is selected):
  - **Legacy Mode**: the original online-era rules and balance: original exp and drop rates, dungeon clear limits and party
    requirements. Nothing is changed for solo play, so content built for a party stays closed.
  - **Offline Adapted Mode**: built for playing alone: faster exp and more loot, unlimited dungeon clear rewards, Epic dungeon
    gear, easier pet taming, party-size dungeons open to you (except Cathy Mart, which needs a full group), bosses scaled for one player, and a Black Market with items for sale.
  - Both modes keep Meret pouches, the full Meret Market, mail that never expires and the chat commands. Your characters work in
    either mode. To switch, close the game and start it again.
  - Advanced: the choice is stored as `GameMode=Legacy|Adapted` in `launcher.ini`, and a single option there (for example
    `GrindRates=true`) still overrides the mode.
- Cheat commands (`CheatCommands=true`, on in both modes), typed in the chat box:
  - `/give <item name> [count]` gives an item by its English name (`/give Red Potion 5`); a name that fits several items lists their ids.
  - `/item <id>` gives an item by id (`--amount`, `--rarity`, `--drop`); without `--rarity` the item gets its own default rarity.
  - `/find item <text>` searches item names and ids (also `map`, `npc`, `quest`, `skill`, `achievement`).
- A plain-text problem report after every session (desktop folder "MapleStory 2 Offline Reports") to send with bug reports.

Every change to the original game is listed in [docs/ADAPTATIONS.md](docs/ADAPTATIONS.md).

## Repository layout

| Path | What |
|---|---|
| `Launcher/` | the one-click launcher (C#, WinForms) |
| `Maple2/` | the server emulator, fork of MS2Community/Maple2 (git submodule) |
| `_dl/Agarcium/` | client patch source, fork of shuabritze/Agarcium (git submodule) |
| `tools/` | release packaging and update signing (Python) |
| `linux/` | the Linux launcher: servers native on .NET 8, game under Proton (Python 3, PyQt6 window) |
| `docs/ADAPTATIONS.md` | every change to the original game |
| `launcher.ini.example` | launcher settings with their defaults |

## Building from source

Requirements: Windows 10/11 x64, .NET 8 SDK, Visual Studio 2022 with the C++ workload (client patch), Python 3,
and the MapleStory 2 client from Steam.

1. `git clone --recurse-submodules <this repository>`
2. Servers: `dotnet build Maple2/Maple2.sln -c Release`
3. Game data: run the ingest tool (`Maple2/Maple2.File.Ingest`) against your own client.
4. Client patch: build `_dl/Agarcium/Agarcium.sln` (Release, x64) and copy the two DLLs to `client-patch/`.
5. Launcher: `dotnet publish Launcher/MapleStory2Offline.csproj -c Release -o .`
6. Copy `launcher.ini.example` to `launcher.ini` and set `ClientDir` if your Steam library is not the default one.

## License

- `Maple2/` is licensed under the GNU Affero General Public License v3.0 (see `Maple2/LICENSE`). If you distribute a
  modified build, you must make its source available under the same license.
- `_dl/Agarcium/` is licensed under the MIT License (see `_dl/Agarcium/LICENSE`).
- The launcher, tools and docs in this repository are licensed under the GNU Affero General Public License v3.0.

## Disclaimer

This is a non-commercial fan preservation project. It is not affiliated with, endorsed or sponsored by NEXON.
MapleStory and MapleStory 2 are trademarks of NEXON Korea Corporation. Use your own legally obtained game client.
