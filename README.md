# MapleStory 2 Offline

A single-player, fully offline edition of MapleStory 2 for Windows. It runs a local copy of the community
[Maple2](https://github.com/MS2Community/Maple2) server emulator next to the official game client, so the game can be
played again after the official servers closed in 2020. The game never connects to the internet; only the launcher
checks GitHub for a newer version when you press Play (and skips that when you are offline).

## Download

### [⬇ Download MapleStory 2 Offline (Setup, about 530 MB)](https://github.com/endale416614-cmd/MapleStory2-Offline/releases/latest/download/MapleStory2-Offline-Setup.exe)

1. Run the downloaded file and click **Install**. No administrator rights are needed.
2. Start the game from the **MapleStory 2 Offline** icon on your desktop.
3. Windows may say "Windows protected your PC" because the file is not signed: click **More info**, then **Run anyway**.

You also need the MapleStory 2 game client (about 14 GB). If it is in your Steam library, the launcher finds it.
Otherwise the launcher points you to the community downloader ([Mushroom Launcher](https://github.com/shuabritze/mushroom-launcher/releases),
"Install client"), or you choose any folder that holds the client. This project does not include the game client.

Updates install themselves: when you press Play and a newer version exists, the launcher asks and updates (your save
is backed up first). Prefer to update by hand? Put `AutoUpdate=off` in `launcher.ini`, download the update file and use
Start menu → **MapleStory 2 Offline - Install Update**. All versions: [Releases](https://github.com/endale416614-cmd/MapleStory2-Offline/releases).

### Found a bug?

Report it on **[Discord](https://discord.gg/bEEc9QsRx)** (or open an [issue](https://github.com/endale416614-cmd/MapleStory2-Offline/issues)).
Run Start menu → **MapleStory 2 Offline - Export Diagnostics** first and attach the file it saves on your desktop;
it works even when the game does not start.

> The green **Code → Download ZIP** button gives you the source code only, which cannot be played. Use the download link above.

## Features

- One-click launcher: starts the local servers, the game, saves your progress on exit and shuts everything down.
- Save data in a single local file (SQLite), backed up on every start.
- Signed updates: the launcher only installs updates signed with the project's update key.
- The client is locked to this PC: connections to anything except `127.0.0.1` are blocked.
- Earnable Meret (bought with Mesos) and a Meret Market listing the former cash and capsule items.
- Solo-friendly options: adjustable exp and drop rates, upgrade-material drops, dungeon reward options.
- A plain-text problem report after every session (desktop folder "MapleStory 2 Offline Reports") to send with bug reports.

Every change to the original game is listed in [docs/ADAPTATIONS.md](docs/ADAPTATIONS.md).

## Repository layout

| Path | What |
|---|---|
| `Launcher/` | the one-click launcher (C#, WinForms) |
| `Maple2/` | the server emulator, fork of MS2Community/Maple2 (git submodule) |
| `_dl/Agarcium/` | client patch source, fork of shuabritze/Agarcium (git submodule) |
| `tools/` | data and maintenance scripts (Python) |
| `docs/` | design notes, the list of changes to the original game, release guide |
| `launcher.ini.example` | launcher settings with their defaults |
| `docs/RELEASING.md` | how to build releases and updates |

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
