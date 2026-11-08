# Compatibility

Supported game clients, operating systems and hardware, with what was actually tested. Living document.

Support levels, as the directive separates them:
- **Native**: built for the system and tested on it.
- **Compatibility layer**: the game client runs through a layer such as Proton. "Supported" only once a full play session has passed there; until then **experimental**.
- **Not supported**: no path is offered.

## Game client
| Client | Support | Tested |
|---|---|---|
| MapleStory 2 Global (Steam), the last build | supported | yes: every release; the content pack applies only to the exact archives listed in the release's COMPATIBILITY.txt (size and SHA-256 per archive) |
| The same build installed by the community downloader (Mushroom Launcher, "Install client") | supported when its archives match the Steam build | the launcher checks the archive hashes; a different build plays as Global without the pack (BT-03) |
| Regional clients (KMS, JMS, CMS, TMS) | not supported as the game client | their data is used offline to build the content pack only |

The launcher never changes the game client for good: the content pack is appended to eight archives and removed
byte-exact when UnifiedContent is turned off or the pack is uninstalled.

## Operating systems
| System | Level | Tested |
|---|---|---|
| Windows 11 x64 | native (Setup, no administrator rights) | yes: development and every harness play test |
| Windows 10 x64 | native (same build; the .NET 8 runtime ships with the Setup) | not tested on a Windows 10 PC |
| Linux x64 | servers native; game through Proton (umu-run, GE-Proton): experimental; launcher `ms2offline` (linux/README.md) | script tests (`linux/test_*.py`); a full Linux play session is an open maintainer check |
| Steam Deck (SteamOS) | as Linux: experimental | see the matrix below |
| macOS | not supported | not evaluated: see below |

## Steam Deck
One player plays on a Deck and reported the results below (2026-11-02; their version and path are not known). None of the
36 stored reports was from Linux at that time; from 1.2.3 a Linux report has a System line that says whether it is a Deck.

| Steam Deck item | State | Evidence |
|---|---|---|
| Controller navigation and input mapping | not evaluated | |
| Keyboard and mouse fallback | not evaluated on a Deck | the player's "my mouse" suggests a mouse or the touchpad is in use |
| Readable UI at handheld resolutions | known problem (BT-173) | most text is 13 to 16 px on a 216 PPI screen; `UIScale` in the client's `Config/GameOption.xml` may help, its effect on this client is not measured (linux/README.md) |
| Performance, memory, battery | not measured | |
| Launcher and patcher | fixed for 1.2.3 in code, not run on a Deck | the update failures of R-22 proved by replay (DU-01, DU-02, PA-04, DU-03), with script tests |
| Offline launch and save behavior | not tested on a Deck | as Linux; script tests |
| Runtime and graphics stack | works for that one player | servers on a private .NET 8, game through Proton |

## macOS
Not supported and not evaluated: the original client's feasibility, macOS security and runtime requirements, graphics,
translation layers, patcher portability and local networking have not been tested. No native server or client path
exists, and none is promised.

## Hardware
- Disk: the game client about 14 GB; the Setup of MapleStory 2 Offline about 1.2 GB (1.2.2), content pack included. The installed size is not measured.
- Memory: besides the game client, the local servers; observed 2026-10-20: the game server about 0.6 GB during a
  play session. Not measured on a low-memory PC yet.
- The game client's own requirements apply (64-bit; Windows or Proton).

## Recovery
TROUBLESHOOTING.txt holds what a player tries, in order: Setup again, verify the game files in Steam, play without the
content pack, restore a save backup, post the session report.
