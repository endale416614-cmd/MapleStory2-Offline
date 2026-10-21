# Compatibility

Supported game clients, operating systems and hardware, with what was actually tested. Living document.

## Game client
| Client | Support | Tested |
|---|---|---|
| MapleStory 2 Global (Steam), the last build | supported | yes: every release; the 1.2.0 content pack applies only to the exact archives listed in the release's COMPATIBILITY.txt (size and SHA-256 per archive) |
| The same build installed by the community downloader (Mushroom Launcher, "Install client") | supported when its archives match the Steam build | the launcher checks the archive hashes; a different build plays as Global without the pack (BT-03) |
| Regional clients (KMS, JMS, CMS, TMS) | not supported as the game client | their data is used offline to build the content pack only |

The launcher never changes the game client for good: the content pack is appended to seven archives and removed
byte-exact when UnifiedContent is turned off or the pack is uninstalled.

## Operating systems
| System | Support | Tested |
|---|---|---|
| Windows 11 x64 | supported (Setup, no administrator rights) | yes: development and every harness play test |
| Windows 10 x64 | supported (same build; the .NET 8 runtime ships with the Setup) | not tested |
| Linux x64 | servers native, game under Proton, launcher `ms2offline` (linux/README.md) | script tests (`linux/test_*.py`); a full Linux play session is an open maintainer check |
| Steam Deck (SteamOS) | expected to work as Linux | untested |
| macOS | not supported | untested; no native server or client path |

## Hardware
- Disk: the game client about 14 GB; MapleStory 2 Offline about 560 MB (Setup) plus about 1 GB with the content pack
  and its game data.
- Memory: besides the game client, the local servers; observed 2026-10-20: the game server about 0.6 GB during a
  play session. Not measured on a low-memory PC yet.
- The game client's own requirements apply (64-bit; Windows or Proton).
