# Release 1.1.9: staging ground for 1.2.0

Written 2026-10-17. 1.1.9 is a preparation release: bug fixes plus the foundations that
[RELEASE_PLAN_1.2.0.md](RELEASE_PLAN_1.2.0.md) lists as prerequisites. It keeps every 1.1.x save and data format. Two
1.2.0 items were finished early and ship in 1.1.9 by the maintainer's decision (2026-10-17): the `GameMode` preset (R12-07)
and the `/give` cheat (R12-25), listed below with their behaviour changes. Nothing else from 1.2.0 ships (no New Game,
seasons, voice or unified-edition content).

## Version convention

The repository holds no version number. `tools/package_release.py <version>` passes it to the builds
(`-p:Version=`) and writes `VERSION` into the package. So 1.1.9 needs no source edit; it is chosen when packaging.
Nothing may be labelled 1.2.0 until that release is packaged.

## Already in 1.1.9 (committed, not yet released)

| Change | Where | Verified |
|---|---|---|
| Save on disconnect before cleanup, storage in the atomic save, 5-minute autosave, tolerant lock and packet handling | `Maple2` `a6965454` | build passed; tests 246 passed, 16 skipped, 0 failed (2026-10-17, `Maple2` `e66cf648`) |
| Shops, enchant, boxes and black market validate before consuming; safe defaults for added save columns | `Maple2` `e66cf648` | same run |
| Launcher: log writer lifetime, backup check prompt, setup swap rollback; Linux staged install with hash check | `db8712f` | launcher build passed; no launcher tests exist |
| Developer tools `ClientDiff`, `KmsSplit`, `M2dPack` and engineering docs (not shipped to players) | `3a8dec6` | selftests pass; see BACKLOG.md |
| `GameMode=Legacy\|Adapted` preset (R12-07, shipped early). Behaviour change: a new install without a save asks once for the mode; existing installs and an unset or unknown value stay `Adapted` (the 1.1.8 rules). `Legacy` turns the solo-friendly adaptations off (original rates, 30-day mail, original dungeon limits and Meret Market, no cheat commands); a single option line in `launcher.ini` still wins. Release `defaults.ini` and `launcher.ini.example` list `GameMode` instead of every option. Linux launcher gains `GameMode`, `PetTaming`, `DungeonEpicGear` and the pets/shop mod toggles. See ADAPTATIONS.md A19 | `Maple2` `f6d3588b`; `1ca4440` | `OfflineProfileTest` in the server suite (276 passed, 17 skipped, 2026-10-17); launcher build passed; not played under `Legacy` |
| `/give <item name> [count]` cheat (R12-25, shipped early). Behaviour changes when `CheatCommands` is on (default in Adapted): `/item` and `/find` no longer need admin rights; `/item` without `--rarity` uses the item's data rarity instead of rarity 1 (gear from `/item` now has base stats). See README and ADAPTATIONS.md | `Maple2` `05ca9d2c`; `56b5e1e` | `ItemNameResolverTest` in the same run; in-game spot checks not recorded here |

The superproject's `Maple2` and `_dl/Agarcium` submodule pointers are not yet committed at these commits.

## 1.2.0 objectives and their 1.1.9 preparation

| 1.2.0 objective (plan id) | 1.1.9 preparation | Behaviour change | Status |
|---|---|---|---|
| Reproducible data build (R12-02) | Identify the `Xml.m2d` used for 1.1.8; make packaging record its inputs | none (adds `GameData/data-build.json`) | done 2026-10-17: ingest of community `Xml.m2d` nightly-2026-10-06 (sha256 `572daba8...`) + `Server.m2d` v1.3.2 (`37a46cc1...`) reproduces all 23 table checksums of the shipped 1.1.8 `maple-data.db`. `package_release.py` now writes `GameData/data-build.json` (per-table CRC32C, Maple2 commit, input sha256 when `MS2_DATA_FOLDER` is set); tested on the reproduced DB, no machine paths in the output |
| CI gate (R12-03) | Workflow: build Maple2 + Launcher, `dotnet test`, `KmsSplit --selftest` | none (repo only) | drafted 2026-10-17: `.github/workflows/ci.yml` (Windows runner, .NET 8, build servers + launcher, server tests, `KmsSplit --selftest`). Each step passes locally; the workflow itself has not run on GitHub (needs a push) |
| Data-integrity sweep (R12-04) | Read-only checks over `maple-data.db`, baseline report | none (test only) | done 2026-10-17: `Maple2.Server.Tests/Data/DataSweepTest.cs` (`[Category("Data")]`, `MAPLE_DATA_DB`, skips without a DB, opens read-only; DB hash unchanged after the run). Baseline on 1.1.8: errors quest refs 31, portal->map 81, trigger parse 6, navmesh 2, item option ids 1,351; warnings 909 unused trigger functions, 16 orphan navmeshes, 1 option-coverage note. Full suite with the DB: 247 passed, 16 skipped, 0 failed |
| Clock abstraction (R12-05) | One clock provider used at the existing call sites, no change to which zone is used (Open Decision 11 is not decided) | none | done 2026-10-17 (`Maple2` `de966192`): `Maple2.Tools/GameClock.cs` (`UtcNow` as `DateTimeOffset`, `Now` as local `DateTime`, `Override(Func<DateTimeOffset>)` returning a restore handle). Every `DateTime.Now`/`UtcNow` and `DateTimeOffset.Now`/`UtcNow` in the five planned files now reads it: `GameEvent.cs` (`IsActive`), `WorldServer.cs` (resets, global portals, world bosses, event scheduling, plot expiry), `GameStorage.ServerInfo.cs`, `GameSession.cs` (TimeSync, last-online, play-time stop), `GameEventManager.cs`. Same values at real time: local stays local, UTC stays UTC; the `IsActive` zone mix is kept and pinned by tests until Open Decision 11 is decided. Tests: `Maple2.Server.Tests/Tools/GameClockTests.cs` (5 clock cases; `GameEventActiveTest` before/inside/after, day of week, part time). Full suite 276 passed, 17 skipped, 0 failed |
| Signed release manifest (R12-06) | Launcher accepts and verifies a manifest when present, ignores its absence | none for 1.1.x installs | done 2026-10-17 (launcher side; signing untested): `Launcher/ReleaseManifest.cs` verifies `release.json` + `release.json.sig` (ECDSA P-256 / SHA-256 with the update key over `MS2OFFLINE-RELEASE-1\n` + bytes; strict parse). `UpdateChecker` uses it only when the release lists `release.json`: refuses a missing/invalid signature, another version, an unlisted or differently sized update zip, and a download whose sha256 differs (checked before the `.part` file is kept). No `release.json`: unchanged. `tools/sign_update.py --manifest`, `package_release.py` writes and signs `release.json` with `--base`. New `Launcher.Tests` (19 tests, in CI). Local-feed run (`MS2OFFLINE_UPDATE_FEED`, scratch portable copy): no manifest = old path (zip downloaded, rejected by its own signature); unsigned or bad-signature manifest = refused before download. Independent review: no blocking issue; fixed stale `release.json` in `publish/`, body timeout. Open: manifest stripping falls back to the zip signature (as 1.1.x; require it from 1.2.0), Setup self-check (plan item) not done, Python signer not run (no `cryptography`) |
| Mode presets (R12-07) | shipped early, see "Already in 1.1.9" | new-install prompt; `Legacy` option | done |
| Save stamp (R12-08) | none in 1.1.9 (feature) | - | out of scope |
| Unified edition (U-xx) | none in 1.1.9; data stays on the research track | - | out of scope; status in BACKLOG.md |

## Blockers

1. Signing: the update-signing key is not on the build PC, and the Python package `cryptography` (used by `tools/sign_update.py`) is not installed. Python itself was repaired on 2026-10-17 (`py -I` runs 3.15.0; `package_release.py --help` runs).
2. Resolved: 1.1.8 data inputs identified (R12-02).
3. Client patch link error `C1047` (prebuilt MinHook); only matters if 1.1.9 needs a client-patch change.

## Readiness checks (run before packaging 1.1.9)

1. `dotnet build Maple2/Maple2.sln -c Release` and `dotnet build Launcher/MapleStory2Offline.csproj -c Release`: 0 errors. Pass 2026-10-17 with R12-05/06 (servers 0 errors, 10 warnings; launcher 0 warnings).
2. `dotnet test Maple2/Maple2.Server.Tests -c Release` and `dotnet test Launcher.Tests -c Release`: 0 failed. Pass 2026-10-17: servers 276 passed, 17 skipped; launcher 19 passed.
3. A 1.1.8 save opens, plays and saves under 1.1.9 (copy of a real save in a portable test instance). Partial pass 2026-10-17: a save created by the 1.1.8 servers in the test instance opened under the 1.1.9 servers (Maple2 `38fcc7db`), 11.5 min of play (Lv 1 to 5), saved on exit, report "no problems detected". Skipped with a long-played real save by the maintainer's decision (2026-10-17); the partial pass above stands. Accepted risk: a save with long-term state (many items, mail, quests, housing) is not exercised before release; the 1.1.9 save-related changes are the save-path fixes (`a6965454`) and parseable defaults for columns the save upgrade adds (`e66cf648`), both covered by tests.
4. Update path: a 1.1.8 install accepts the signed 1.1.9 update zip and can roll back.
5. Package inputs recorded (R12-02) and `maple-data.db` table checksums compared with 1.1.8; every difference explained.
