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
| `GameMode=Legacy\|Adapted` preset (R12-07, shipped early; redesigned 2026-10-17 at the maintainer's request for players with no technical knowledge). Behaviour change: a "Choose how to play" window at every start (Legacy Mode / Offline Adapted Mode, last choice selected, Quit / Play; the choice is written to `launcher.ini`, no editing needed). Existing installs and an unset or unknown value stay `Adapted` (the 1.1.8 rules plus A20). `Legacy` turns off the solo-friendly adaptations (grinding rates, all dungeon drops, unlimited clears, Epic dungeon gear, pet taming, class-only drops, party-size dungeons) and keeps Meret pouches, the full Meret Market, mail that never expires and the chat commands. New option `PartyDungeons` (A20). A single option line in `launcher.ini` still wins, except that a `launcher.ini` from before the modes (no `GameMode` line) gets its gameplay lines set to `true` commented out once, so Legacy is not silently overridden (review finding 1, REVIEW_1.1.9.md; `Launcher/LauncherIni.cs`, Linux `migrate_launcher_ini`, Linux example now lists `GameMode` with the options commented; 6 launcher tests, Python rule checked against the 1.1.8 Linux example). Linux launcher: same rules and options, no window (`GameMode` in its ini). See ADAPTATIONS.md A19, A20 | `Maple2` `f6d3588b`, `aaaab0b0`; `1ca4440` and the redesign commit on `main` | server suite 303 passed, 16 skipped (2026-10-17; `OfflineProfileTest` incl. both-mode options and the party-size door rule); window rendered and checked in both states; World + Game servers started from the packaged install under `Legacy` and under `Adapted` (Meret catalog 7,657 entries loaded in both, no errors). Maintainer play test 2026-10-17 with the package from `aaaab0b0`: the window showed, Play started the game and the chosen mode behaved as described |
| Meret Market reorganized (maintainer request 2026-10-17). Behaviour change: capsules (128) under Featured, new Design Templates (103) and Furniture (164: maids + 128 Meret furniture pieces no shop sold) tabs, Crate Spotlight removed (coins to Functional). See ADAPTATIONS.md A21 | `tools/organize_meret_market.py`; Maple2 catalog + `OfflineEconomy`; client mod `offline-shop` (generated) | tool idempotent (second run: nothing to change); catalog diff = 299 tab moves + 128 added entries, nothing else; server suite 304 passed (`MeretCatalog_EveryEntrySitsInAKnownTab`, Featured under 255 slots). Not yet seen in the client |
| `/give <item name> [count]` cheat (R12-25, shipped early). Behaviour changes when `CheatCommands` is on (default in Adapted): `/item` and `/find` no longer need admin rights; `/item` without `--rarity` uses the item's data rarity instead of rarity 1 (gear from `/item` now has base stats). See README and ADAPTATIONS.md | `Maple2` `05ca9d2c`; `56b5e1e` | `ItemNameResolverTest` in the same run; in-game spot checks not recorded here |

The superproject's `Maple2` and `_dl/Agarcium` submodule pointers are not yet committed at these commits.

## 1.2.0 objectives and their 1.1.9 preparation

| 1.2.0 objective (plan id) | 1.1.9 preparation | Behaviour change | Status |
|---|---|---|---|
| Reproducible data build (R12-02) | Identify the `Xml.m2d` used for 1.1.8; make packaging record its inputs | none (adds `GameData/data-build.json`) | done 2026-10-17: ingest of community `Xml.m2d` nightly-2026-10-06 (sha256 `572daba8...`) + `Server.m2d` v1.3.2 (`37a46cc1...`) reproduces all 23 table checksums of the shipped 1.1.8 `maple-data.db`. `package_release.py` now writes `GameData/data-build.json` (per-table CRC32C, Maple2 commit, input sha256 when `MS2_DATA_FOLDER` is set); tested on the reproduced DB, no machine paths in the output |
| CI gate (R12-03) | Workflow: build Maple2 + Launcher, `dotnet test`, `KmsSplit --selftest` | none (repo only) | drafted 2026-10-17: `.github/workflows/ci.yml` (Windows runner, .NET 8, build servers + launcher, server tests, `KmsSplit --selftest`). Each step passes locally; the workflow itself has not run on GitHub (needs a push) |
| Data-integrity sweep (R12-04) | Read-only checks over `maple-data.db`, baseline report | none (test only) | done 2026-10-17: `Maple2.Server.Tests/Data/DataSweepTest.cs` (`[Category("Data")]`, `MAPLE_DATA_DB`, skips without a DB, opens read-only; DB hash unchanged after the run). Baseline on 1.1.8: errors quest refs 31, portal->map 81, trigger parse 6, navmesh 2, item option ids 1,351; warnings 909 unused trigger functions, 16 orphan navmeshes, 1 option-coverage note. Full suite with the DB: 247 passed, 16 skipped, 0 failed |
| Clock abstraction (R12-05) | One clock provider used at the existing call sites, no change to which zone is used (Open Decision 11 is not decided) | none | done 2026-10-17 (`Maple2` `de966192`): `Maple2.Tools/GameClock.cs` (`UtcNow` as `DateTimeOffset`, `Now` as local `DateTime`, `Override(Func<DateTimeOffset>)` returning a restore handle). Every `DateTime.Now`/`UtcNow` and `DateTimeOffset.Now`/`UtcNow` in the five planned files now reads it: `GameEvent.cs` (`IsActive`), `WorldServer.cs` (resets, global portals, world bosses, event scheduling, plot expiry), `GameStorage.ServerInfo.cs`, `GameSession.cs` (TimeSync, last-online, play-time stop), `GameEventManager.cs`. Same values at real time: local stays local, UTC stays UTC; the `IsActive` zone mix is kept and pinned by tests until Open Decision 11 is decided. Tests: `Maple2.Server.Tests/Tools/GameClockTests.cs` (5 clock cases; `GameEventActiveTest` before/inside/after, day of week, part time). Full suite 276 passed, 17 skipped, 0 failed |
| Signed release manifest (R12-06) | Launcher accepts and verifies a manifest when present, ignores its absence | none for 1.1.x installs | done 2026-10-17: `Launcher/ReleaseManifest.cs` verifies `release.json` + `release.json.sig` (ECDSA P-256 / SHA-256 with the update key over `MS2OFFLINE-RELEASE-1\n` + bytes; strict parse). `UpdateChecker` uses it only when the release lists `release.json`: refuses a missing/invalid signature, another version, an unlisted or differently sized update zip, and a download whose sha256 differs (checked before the `.part` file is kept). No `release.json`: unchanged. `tools/sign_update.py --manifest`, `package_release.py` writes and signs `release.json` with `--base`. New `Launcher.Tests` (19 tests, in CI). Local-feed run (`MS2OFFLINE_UPDATE_FEED`, scratch portable copy): no manifest = old path (zip downloaded, rejected by its own signature); unsigned or bad-signature manifest = refused before download. Independent review: no blocking issue; fixed stale `release.json` in `publish/`, body timeout. Signed end to end with the new key (see Signing key change): signed update + manifest installed in a scratch copy; same-size tampered zip refused ("does not match the signed release.json"); manifest edited after signing refused; signed zip without manifest still installed. Open: manifest stripping falls back to the zip signature (as 1.1.x; require it from 1.2.0), Setup self-check (plan item) not done |
| Mode presets (R12-07) | shipped early, see "Already in 1.1.9" | new-install prompt; `Legacy` option | done |
| Save stamp (R12-08) | none in 1.1.9 (feature) | - | out of scope |
| Unified edition (U-xx) | none in 1.1.9; data stays on the research track | - | out of scope; status in BACKLOG.md |

## Signing key change (2026-10-17)

The private key that signed the 1.1.1-1.1.8 updates was lost in a factory reset of the build PC (searched on every drive;
never in git). A new ECDSA P-256 key was generated on 2026-10-17 and stored outside the repository at the default
location `..\Signing\update-signing-key.pem`; `Launcher/UpdateKey.cs` holds its public half. Consequences:

- Launchers 1.1.1-1.1.8 trust only the old key, so they cannot install a 1.1.9 update file. 1.1.9 is installed with the
  full Setup. Setup replaces the program folder only; the save stays.
- Publish 1.1.9 without an update zip: `py -I tools/package_release.py 1.1.9 --manifest` (no `--base`). Upload: the Setup
  (as `MapleStory2-Offline-Setup.exe`), `release.json`, `release.json.sig`. A 1.1.8 launcher with AutoUpdate then finds no update file and offers
  "It has to be installed with its full Setup. Open the download page?" (existing behaviour). An update zip signed with
  the new key would instead be downloaded and rejected as "not signed by the MapleStory 2 Offline developer".
- The 1.1.9 release notes must say: install 1.1.9 with the Setup; updates work again from 1.1.9 on.
- Maintainer action: back up the new key in two places off this PC (password manager, USB stick). Losing it again
  repeats this.

## Blockers

1. Resolved 2026-10-17: `cryptography` 50.0.2 installed (`py -I` loads it); new signing key in place (see above).
2. Resolved: 1.1.8 data inputs identified (R12-02).
3. Client patch link error `C1047` (prebuilt MinHook); only matters if 1.1.9 needs a client-patch change.
4. Resolved 2026-10-17: the packaging inputs lost in the factory reset are restored (all gitignored). From the 1.1.8 Setup payload: `client-patch/*.dll` (unchanged; cannot be rebuilt, blocker 3), `client-mods/`, `Maple2/.env` (portable, no credentials) and `SaveData/maple-data.db` (= the 1.1.8 `GameData/maple-data.db`). `dotnet/` must hold an SDK (the packager builds with it), so it is a copy of the installed .NET SDK 8.0.426 with runtime 8.0.32 only (one `hostfxr`). The 1.1.9 package therefore ships .NET 8.0.32 where 1.1.8 shipped 8.0.31 (patch update).
5. Resolved 2026-10-17: the gh token got the `workflow` scope; `main`, `Maple2` and `_dl/Agarcium` are pushed.

## Local 1.1.9 package (built 2026-10-17, not published)

`py -I tools/package_release.py 1.1.9 --manifest` (fixed the same day: under `py -I` the packager could not import
`sign_update`). Current build: main `4d19298` with Maple2 `43340957` (GameMode redesign, Meret Market layout, review
finding 1): Setup 532.0 MB (557,790,206 bytes), full zip 462.2 MB, `release.json` + `release.json.sig`.
Checks on it:
- `release.json` signature verifies with the launcher's key; both listed files match size and sha256.
- `VERSION` 1.1.9; `GameData/data-build.json` records Maple2 `43340957`; Meret catalog 7,785 entries (no input hashes: `MS2_DATA_FOLDER` was not set).
- No save, log, `launcher.ini`, install file, key or portrait in the folder; no machine path, e-mail or token in any file
  (the text "PRIVATE KEY" occurs only as a PEM label inside .NET and MySqlConnector libraries); test-kit mod disabled.
- Against the 1.1.8 `files.json`: no file removed; added `GameData/data-build.json`; changed are the servers, launcher,
  `README.txt`, `defaults.ini`, `VERSION`, `offline-testkit.json`, the .NET 8.0.31 to 8.0.32 runtime files and
  `maple-data.db` (rewritten by `VACUUM INTO`, same content, check 5).
- The real Setup over a fresh 1.1.8 scratch install: save and `launcher.ini` byte-identical (15 files), install id kept,
  VERSION 1.1.9. The World server then started from that install on the packaged runtime against the 1.1.8 test save
  (no errors in 25 s; resets and 19 world bosses scheduled). After the redesign: Setup over 1.1.8 again keeps the save
  (14 files identical); World + Game reach "Channel 1 has become active" under both modes with 0 errors.
  Rerun on the current build (2026-10-17, game closed): the 1.1.8 Setup into a fresh scratch folder with a copy of the
  1.1.8 test save and the 1.1.8 Linux example as `launcher.ini`; the 1.1.9 Setup over it: save byte-identical (14 files),
  `launcher.ini` untouched by Setup, install id kept, VERSION 1.1.9. First 1.1.9 launcher start: the 4 gameplay lines set
  to `true` commented out, the other lines unchanged. World + Game from that install: "Channel 1 has become active",
  0 errors, Meret catalog 7,785 entries, under Legacy and under Adapted. All pass.
- Upload when releasing, as for 1.1.1-1.1.8: the Setup renamed to `MapleStory2-Offline-Setup.exe` (the release notes link
  `releases/latest/download/` to that name), with `release.json` + `release.json.sig` regenerated for that file name
  (`publish/upload-1.1.9/`, signature verified). No update zip (see Signing key change) and, as before, no full zip.

## Readiness checks (run before packaging 1.1.9)

1. `dotnet build Maple2/Maple2.sln -c Release` and `dotnet build Launcher/MapleStory2Offline.csproj -c Release`: 0 errors. Pass 2026-10-17 with R12-05/06 (servers 0 errors, 10 warnings; launcher 0 warnings).
2. `dotnet test Maple2/Maple2.Server.Tests -c Release` and `dotnet test Launcher.Tests -c Release`: 0 failed. Pass 2026-10-17: servers 276 passed, 17 skipped; launcher 19 passed.
3. A 1.1.8 save opens, plays and saves under 1.1.9 (copy of a real save in a portable test instance). Partial pass 2026-10-17: a save created by the 1.1.8 servers in the test instance opened under the 1.1.9 servers (Maple2 `38fcc7db`), 11.5 min of play (Lv 1 to 5), saved on exit, report "no problems detected". Skipped with a long-played real save by the maintainer's decision (2026-10-17); the partial pass above stands. Accepted risk: a save with long-term state (many items, mail, quests, housing) is not exercised before release; the 1.1.9 save-related changes are the save-path fixes (`a6965454`) and parseable defaults for columns the save upgrade adds (`e66cf648`), both covered by tests.
4. Update path (changed by the key change): a 1.1.8 install is offered the full Setup, and Setup over it keeps the save; a 1.1.9 install accepts an update signed with the new key and can roll back. Pass 2026-10-17 except the live prompt, on a scratch install (no shortcuts, no registry entry):
   - The 1.1.8 Setup installed silently; its data folder got a copy of a save created by the 1.1.8 servers (`_test` backup) and a `launcher.ini`.
   - A 1.1.9 test Setup over it (`--setup-silent`, same folders): VERSION 1.1.9, all 14 `SaveData` files byte-identical, `launcher.ini` and install id kept, no `.old`/`.new` folders left. The test Setup was assembled by hand with `package_release.make_setup` from the 1.1.8 payload plus the 1.1.9 launcher and server builds, because the packaging inputs are missing (blocker 4).
   - On that install, `--install-update` of a test update signed with the new key: installed (99.0.0), save backed up first. A change to the save after the update, then `--rollback-update`: back to 1.1.9, the update's file removed, `game-server.db` content identical to the pre-update save (SQL dump hash, 43 tables). The backup is a consolidated SQLite copy, so the files are not byte-identical; `save-status.json` (last-session status, rewritten each run) is not part of the backup.
   - Earlier the same day: a 1.1.9 launcher took that update from a local feed with `release.json`; tampered copies were refused.
   - Not done: the 1.1.8 launcher's "install with the full Setup" prompt against a real published 1.1.9 release.
5. Package inputs recorded (R12-02) and `maple-data.db` table checksums compared with 1.1.8; every difference explained. Pass 2026-10-17: all 23 table checksums of the packaged `maple-data.db` equal those of the 1.1.8 package (same `data-build` function over both); no difference.

---
## Published (2026-10-17)

Release `v1.1.9` on `82c52a8`, marked Latest: `MapleStory2-Offline-Setup.exe` (557,790,206 bytes, sha256
`61f7e170888ae5b93da3f55668853f79f422b590ec5132e76cf40bde05652c9f`), `release.json`, `release.json.sig`. No update zip, no
full zip. Before upload: Setup sha256 and size matched `release.json`; `release.json.sig` verified with `openssl dgst
-sha256 -verify` against the `UpdateKey.cs` public key. After upload: the `releases/latest/download/release.json` link
serves the uploaded file (sha256 equal). Release notes state the Setup-only install and the key change, and list the
Setup sha256. Live check 2026-10-17: the 1.1.8 launcher (`--check-updates`, portable test copy) against the published release wrote "1.1.9 available, full Setup only", downloaded nothing, exit 0; the GUI path shows the "install with its full Setup" prompt from that same result.
