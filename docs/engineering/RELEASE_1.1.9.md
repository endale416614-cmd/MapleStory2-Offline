# Release 1.1.9: staging ground for 1.2.0

Written 2026-10-17. 1.1.9 is a preparation release: bug fixes plus the foundations that
[RELEASE_PLAN_1.2.0.md](RELEASE_PLAN_1.2.0.md) lists as prerequisites. It ships no 1.2.0 feature (game modes, New Game,
seasons, voice, unified-edition content) and keeps every 1.1.x behaviour, save and data format.

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

The superproject's `Maple2` and `_dl/Agarcium` submodule pointers are not yet committed at these commits.

## 1.2.0 objectives and their 1.1.9 preparation

| 1.2.0 objective (plan id) | 1.1.9 preparation | Behaviour change | Status |
|---|---|---|---|
| Reproducible data build (R12-02) | Identify the `Xml.m2d` used for 1.1.8; make packaging record its inputs | none (adds `GameData/data-build.json`) | done 2026-10-17: ingest of community `Xml.m2d` nightly-2026-10-06 (sha256 `572daba8...`) + `Server.m2d` v1.3.2 (`37a46cc1...`) reproduces all 23 table checksums of the shipped 1.1.8 `maple-data.db`. `package_release.py` now writes `GameData/data-build.json` (per-table CRC32C, Maple2 commit, input sha256 when `MS2_DATA_FOLDER` is set); tested on the reproduced DB, no machine paths in the output |
| CI gate (R12-03) | Workflow: build Maple2 + Launcher, `dotnet test`, `KmsSplit --selftest` | none (repo only) | drafted 2026-10-17: `.github/workflows/ci.yml` (Windows runner, .NET 8, build servers + launcher, server tests, `KmsSplit --selftest`). Each step passes locally; the workflow itself has not run on GitHub (needs a push) |
| Data-integrity sweep (R12-04) | Read-only checks over `maple-data.db`, baseline report | none (test only) | done 2026-10-17: `Maple2.Server.Tests/Data/DataSweepTest.cs` (`[Category("Data")]`, `MAPLE_DATA_DB`, skips without a DB, opens read-only; DB hash unchanged after the run). Baseline on 1.1.8: errors quest refs 31, portal->map 81, trigger parse 6, navmesh 2, item option ids 1,351; warnings 909 unused trigger functions, 16 orphan navmeshes, 1 option-coverage note. Full suite with the DB: 247 passed, 16 skipped, 0 failed |
| Clock abstraction (R12-05) | One clock provider used at the existing call sites, no change to which zone is used (Open Decision 11 is not decided) | none | todo; wait until the server-file audit fixes are committed (same files) |
| Signed release manifest (R12-06) | Launcher accepts and verifies a manifest when present, ignores its absence | none for 1.1.x installs | todo; security review required |
| Mode presets, save stamp (R12-07/08) | none in 1.1.9 (feature) | - | out of scope |
| Unified edition (U-xx) | none in 1.1.9; data stays on the research track | - | out of scope; status in BACKLOG.md |

## Blockers

1. Signing: the update-signing key is not on the build PC, and the Python package `cryptography` (used by `tools/sign_update.py`) is not installed. Python itself was repaired on 2026-10-17 (`py -I` runs 3.15.0; `package_release.py --help` runs).
2. Resolved: 1.1.8 data inputs identified (R12-02).
3. Client patch link error `C1047` (prebuilt MinHook); only matters if 1.1.9 needs a client-patch change.

## Readiness checks (run before packaging 1.1.9)

1. `dotnet build Maple2/Maple2.sln -c Release` and `dotnet build Launcher/MapleStory2Offline.csproj -c Release`: 0 errors.
2. `dotnet test Maple2/Maple2.Server.Tests -c Release`: 0 failed.
3. A 1.1.8 save opens, plays and saves under 1.1.9 (copy of a real save in a portable test instance).
4. Update path: a 1.1.8 install accepts the signed 1.1.9 update zip and can roll back.
5. Package inputs recorded (R12-02) and `maple-data.db` table checksums compared with 1.1.8; every difference explained.
