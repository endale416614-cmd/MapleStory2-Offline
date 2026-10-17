# Project status

Updated: 2026-10-17

## Phase

Unified edition (vision item 2, Korean and Japanese content in the Global game): research done, implementation
not started. Vision item 1 (100 % playable) continues in parallel through bug-fix releases.

## Verified state

| Check | Result |
|---|---|
| Released version | 1.1.8; tag `v1.1.8` = `main` (`610f6e8`); the published Setup.exe matches the release asset (SHA-256) |
| Submodules | `Maple2` `8000f16` (offline), `_dl/Agarcium` `f0974e5` (offline) |
| `dotnet build Maple2/Maple2.sln -c Release` | passed, 0 errors |
| `dotnet build Launcher/MapleStory2Offline.csproj -c Release` | passed, 0 errors |
| `dotnet test Maple2.Server.Tests` | passed: 237 passed, 16 skipped, 0 failed |
| Client patch (`_dl/Agarcium`, C++), VS 2022 Build Tools 17.14 | failed: `NxCharacter64.dll` builds; `AgarciumClient` link fails with `C1047` because the prebuilt `libMinHook.lib` (NuGet `minhook_v143` 1.3.4.1) was compiled with LTCG by an older MSVC. Fix options: matching MSVC toolset, or MinHook built from source |
| Game data ingest from raw NA client `Xml.m2d` + `Server.m2d` v1.3.2 | passed (2 min 38 s); does not reproduce the shipped `maple-data.db`: 10 of 23 table checksums equal; item, npc, quest, map, skill, trigger, table and others differ |

The documented ingest input (`Maple2/setup.ps1`) is the community-maintained `Xml.m2d` from the MapleStory2-XML
releases, not the raw client archive; that is the likely cause of the checksum difference (to be confirmed).

## Open defects

None recorded at critical or high severity. Player reports arrive through Discord and issues.

## Blockers

1. The exact `Xml.m2d` build used for the 1.1.8 data database is not recorded; record it for every release.
2. Client patch link error `C1047` (see above) blocks client-patch changes (U-08).
3. Client experiments U-02 and U-03 need a play session on the test copy of the NA client.

## Next actions

1. U-04 KMS converter, then U-05 shared-id comparison (no blockers).
2. U-09 string coverage report.
3. U-02 / U-03 client experiments once a test client copy exists.

Backlog: [BACKLOG.md](BACKLOG.md). Findings: [UNIFICATION.md](UNIFICATION.md).

Release planning: [RELEASE_PLAN_1.2.0.md](RELEASE_PLAN_1.2.0.md) (2026-10-17 audit, scope, backlog R12-xx).
