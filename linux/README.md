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
ms2offline verify        check the game files the content pack needs, repair them, or open Steam's verify
```

Problem reports: from 1.2.1 the launcher sends a report to the developer automatically after a session with a problem,
after a one-time notice at the next `play` where you can answer no; then it asks each time and writes the exact text to
`reports/to-send.txt` first. Every report helps the game's development. The **Send problem reports to the developer
automatically** checkbox in the window changes this too (it shows what a report contains before it turns on), and
**Report a problem...** / `ms2offline report-problem` sends your own words. A report holds the servers' error and
warning lines, the launcher's steps, versions and folders, what your PC is (Linux version, Steam Deck or not, session type, screen size, memory, graphics card, umu version), the names (never the values) of the proxy and server environment variables that are set, and a random install number; never your user name, computer
name, character, account or save names, chat, the game client's own text, your save or your game files. Without
internet it waits for the next start. `SendReports=always`, `ask` or `never` in `launcher.ini` changes the choice.

Settings: `launcher.ini` in the program folder (see `launcher.ini.example` here). The gameplay options are the same
keys as on Windows. Linux-only keys: `Proton` (which Proton build; default GE-Proton10-28 when installed, else the
newest GE-Proton), `Prefix`, `LaunchEnv` (extra environment for the game, e.g. MangoHud), `ClientConsole`, and `Profile`.

Downloads are installed only when the release's signed file list verifies (needs the `openssl` command).

## Updates

When you press Play, the launcher asks GitHub whether a newer release exists. Without internet the check is skipped and
the game starts. `AutoUpdate` in `launcher.ini` decides the rest:

- `AutoUpdate=ask` (the default) shows the question in the window, in a kdialog or zenity dialog, or in the terminal. No
  plays the installed version and asks again at the next start. When nobody can answer (no window, no dialog tool that
  works and no terminal) it installs the update.
- `AutoUpdate=always` installs without asking.
- `AutoUpdate=off` never checks. `ms2offline update` or **Check for updates** in the window installs a newer release.

An update is a large download: 623 MB for the update to 1.2.2, over 1 GB when your version needs the full Setup. The
window and the terminal show its progress, and `logs/launcher.log` has it too. With `ms2offline play` as a Steam shortcut
in Game Mode there is no window or terminal to show it, so the game opens when the update is done, which can take
minutes: wait for it. If the game does not open and the log stops at `available online`, a question is waiting where you
cannot see it: set `AutoUpdate=always` in Desktop Mode. None of this has been tested on a Steam Deck.

If an update fails, you play the installed version and it is tried again at the next start. The reason is in the session
report, and that session counts as one with a problem (see Problem reports above). If an update stops halfway, the next
Play finishes it when it can, and otherwise refuses to start and says so: `ms2offline install`, `ms2offline update` or
**Check for updates** finishes it. Your save is backed up first and is not changed.

An update also brings the launcher: from 1.2.3 a package holds `linux/`, and `ms2offline` and the window run the newer copy
from the program folder. A git clone as new or newer keeps running. A launcher older than 1.2.3 does not do this yet, so
update that clone by hand this one time: `git -C <clone> fetch origin`, then `git -C <clone> reset --hard origin/main`, or
clone it again.

## A bigger interface on a Steam Deck

Most of the game's text is 13 to 16 pixels, which is small on a Deck's screen, and a lower resolution does not help. The
client has its own setting for this. Nobody has measured what it does on this client yet, so try it and tell us:

1. In Desktop Mode, close the game and open `Config/GameOption.xml` in the game folder with a text editor.
2. On the `<interface ...>` line, change `UIScale="1"` to `UIScale="0.8"` and save.
3. Start the game. In an older client of this game 0.8 made the interface about 25 % larger. If it got smaller here, use
   `UIScale="1.2"`. If the file says `1` again after the game closes, the game does not keep the setting.

Please tell us on Discord which value worked: the launcher can then set it for everyone. Put `UIScale="1"` back to undo
it. Nothing else changes, and your save is not touched.

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
