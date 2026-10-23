# MapleStory 2 Offline on Linux

The launcher in this folder does on Linux what the project's Windows launcher does: it starts the local servers,
starts the game, saves your progress when you close it and shuts everything down.

- The four Maple2 servers (World, Login, Web, Game) run **natively**, on a private .NET 8 runtime, straight from the
  project's release package (its server files are platform-neutral .NET assemblies with Linux SQLite natives).
- The game client runs under **Proton** through `umu-run`, in its own prefix, with the project's client patch
  (`NxCharacter64.dll` + `AgarciumClient.dll`) and client mods.
- Save backups, logs, session reports, settings (`launcher.ini`), the gameplay options and the online update check
  work as on Windows.

The game never connects to the internet (all servers listen on 127.0.0.1 and the client patch blocks every other
address). Only the update check and `ms2offline update` talk to GitHub, and problem reports go to the project's report
service (see Problem reports below). The one other download is the .NET runtime, which is fetched from dot.net
(Microsoft's install script) on first install.

## Install

```
git clone https://github.com/endale416614-cmd/MapleStory2-Offline.git     # the submodules are not needed
cd MapleStory2-Offline/linux
./install.sh                     # downloads the latest release (about 530 MB) and sets everything up
```

Needs: `python3`, `python3-pyqt6` (for the window; the command line works without it), `umu-launcher` (`umu-run`)
and `iproute2` (`ss`). Nothing goes outside your home folder: `~/.local/bin/ms2offline`, a menu entry, and the
program folder `~/Games/MapleStory2-Offline` (`MS2OFFLINE_HOME` or `--home` changes it). Everything lives in that
folder: servers, game data, the .NET runtime, the Proton prefix, your save (`SaveData/game-server.db`, backed up on
every start into `SaveData/backups`), logs and reports. Deleting the folder removes everything except the game client.

You also need the MapleStory 2 game client (about 14 GB), which this project does not include. If it is in your Steam
library, the launcher finds it (in any Steam library folder). If it is anywhere else, press **Find my game...** in the
window (or `ms2offline client --dir "<folder>"`) and pick the game's folder: the one with `Data` and `x64` inside, or
the `x64` folder itself. No client yet? Install it from your Steam library (`ms2offline client --steam` opens the
page), or get it with the community downloader ([Mushroom Launcher](https://github.com/shuabritze/mushroom-launcher/releases),
"Install client") and point the launcher at that folder. Do not start the game from Steam itself; it would try the
Nexon launcher. Always start it from the launcher.

## Installing .NET yourself

The launcher asks before it downloads https://dot.net/v1/dotnet-install.sh (Microsoft's script, the only download from outside GitHub); to do it yourself run `bash dotnet-install.sh --runtime aspnetcore --channel 8.0 --install-dir <program folder>/dotnet --no-path`.

## Use

The menu entry **MapleStory 2 Offline** opens a small window (`ms2offline-gui.py`, PyQt6) with Play, Stop servers,
Check for updates, Find my game... and Open folder. Play with no game folder known opens Find my game... first.
The same engine is available as a command:

```
ms2offline play          start the servers and the game; when you close the game it saves and stops everything
ms2offline play --servers-only   servers only (for the game started by hand, or testing)
ms2offline stop          stop servers that are still running (also an action on the menu entry)
ms2offline status        version, game folder, patch state, Proton, what is running
ms2offline update        install a newer release if there is one
ms2offline client        show / --dir set / --steam install the game client
ms2offline prefix        create or refresh the Proton prefix
ms2offline report        write a session report from the latest logs
ms2offline report-problem  send the developer a problem report with your own words (--note "...")
ms2offline diagnostics   zip logs and reports for a bug report (--include-save adds your save)
ms2offline unpatch       put the game's own NxCharacter64.dll back
```

Problem reports: from 1.2.1 the launcher sends a report to the developer automatically after a session with a problem,
after a one-time notice at the next `play` where you can answer no; then it asks each time and writes the exact text to
`reports/to-send.txt` first. Every report helps the game's development. The **Send problem reports to the developer
automatically** checkbox in the window changes this too (it shows what a report contains before it turns on), and
**Report a problem...** / `ms2offline report-problem` sends your own words. A report holds the servers' error and
warning lines, the launcher's steps, versions and folders, and a random install number; never your user name, computer
name, character, account or save names, chat, the game client's own text, your save or your game files. Without
internet it waits for the next start. `SendReports=always`, `ask` or `never` in `launcher.ini` changes the choice.

Settings: `launcher.ini` in the program folder (see `launcher.ini.example` here). The gameplay options are the same
keys as on Windows. Linux-only keys: `Proton` (which Proton build; default GE-Proton10-28 when installed, else the
newest GE-Proton), `Prefix`, `LaunchEnv` (extra environment for the game, e.g. MangoHud), `ClientConsole`, and `Profile`.

Downloads are installed only when the release's signed file list verifies (needs the `openssl` command).

## How it maps to the Windows launcher

| Windows | Linux |
|---|---|
| `MapleStory 2 Offline.exe` (WinForms) | `ms2offline.py` (Python 3, standard library only) + `ms2offline-gui.py` (PyQt6) |
| job object kills servers with the launcher | `PR_SET_PDEATHSIG` + process groups (children started from one long-lived thread: the death signal follows the starting thread) |
| `GetExtendedTcpTable` port guard | `ss -ltnp` + `/proc` |
| servers: `Maple2.Server.X.exe` + bundled Windows .NET | `dotnet/dotnet Maple2.Server.X.dll` (private Linux ASP.NET Core 8 runtime from Microsoft's install script) |
| game: `MapleStory2.exe` started directly | `umu-run MapleStory2.exe ...` (Proton), same arguments, `--mods` as a `Z:\` path |
| signed update zips verified with the project's key | release packages fetched from GitHub over HTTPS, installed only when their signed release.json verifies with the project's key (openssl, ECDSA P-256 / SHA-256, as on Windows) and each file matches its sha256 |
| reports on the Desktop | `reports/` in the program folder |
| MessageBox / TaskDialog | the window's dialogs; on the command line kdialog / zenity / notify-send, or the terminal |

Tested on CachyOS (Arch) with GE-Proton10-28 and the Steam client. License: AGPL-3.0, like the rest of this repository.
