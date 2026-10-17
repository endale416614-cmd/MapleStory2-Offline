# Release plan 1.2.0: audit and roadmap

Planning document. Written 2026-10-17 against tag `v1.1.8` (= `main` `610f6e8`, `Maple2` `8000f16`, `_dl/Agarcium` `f0974e5`).
Nothing in this document is implemented unless the "Verified current state" column says so. Evidence is cited as
`file:line`; claims marked (inferred) were not confirmed by running code. Companion files: [PROJECT_STATUS.md](PROJECT_STATUS.md),
[BACKLOG.md](BACKLOG.md) (unified-edition tasks U-xx), [UNIFICATION.md](UNIFICATION.md), [../ADAPTATIONS.md](../ADAPTATIONS.md).

Task ids in this plan are `R12-nn`. Executor tiers follow the routing policy: T1 = scripts or a small model, T2 = standard
implementation model, T3 = strong model or maintainer, T4 = independent review by a different model than the author.

---

## Deliverable 1: Executive assessment

**Maturity.** 1.1.8 is a shipped, auto-updating, signed-update single-player build with a 1.1.x cadence of eight releases in
four days (2026-10-12 to 10-15), all bug fixes and solo-play options. Build and tests pass (237 passed, 16 skipped). The
Maple2 fork is 53 commits ahead of upstream `MS2Community/Maple2` `master` (`7279d3b6`); upstream `dev` was not compared.
Local checkouts equal GitHub for all three repositories (0 ahead, 0 behind). Untracked work: `docs/engineering/` and
`tools/ClientDiff|KmsSplit|M2dPack` (about 920 lines, reviewed, see Deliverable 3).

**Readiness for the proposed 1.2.0 scope.** Of the six workstreams, none is implemented. Two have real groundwork
(B: per-option flags and plumbing exist; D: a clean level-1 start already works in Release builds). Two need a foundation first
(A: no clock abstraction, 275 direct `DateTime` sites, ingest drops past events; C: good launcher diagnostics, no data sweep,
no CI). Two are blocked on non-code facts (E: whether the client switches voice banks on `voiceIndex`; F: no product named
"MS2 Tools" exists, and Setup/full zips are unsigned).

**Vision conflict.** `VISION.md` orders work: 1 playable, 2 Korean edition, 3 QoL, 4 co-op, 5 modding. The engineering docs
record the current phase as the unified edition (vision 2, research done, implementation not started). The proposed 1.2.0
scope is mostly vision 3 (living world, modes, beginner start) plus distribution. Nothing in the repository asked for
seasons, day/night or torrents. This plan does not choose; it keeps 1.2.0 to what can be made coherent and testable and
flags the ordering as Open Decision 1.

**Most important decisions.** (1) 1.2.0 is a "modes and foundations" release: game-mode presets, new-game flow, debugging
sweep, clock abstraction and a signed release manifest. (2) Seasonal events, day/night, solo-raid scaling and voice packs
each get a bounded pilot, not full coverage. (3) Torrents are deferred; a signed release manifest is the prerequisite and ships now.
(4) Commit the untracked tools and docs after the review fixes, before any other work.

---

## Deliverable 2: Current state audit

| Workstream | Verified current state | Evidence | Remaining work | Blockers | Confidence |
|---|---|---|---|---|---|
| A Living world | No clock abstraction; about 182 `DateTime.Now` + 93 `UtcNow` sites, local/UTC mixed. `GameEvent` infra exists (119 types, about 30 parsed, 13 with logic) but ingest drops events whose end is past; shipped DB holds 3 events. Daily/weekly/monthly resets with boot catch-up. No day/night, season, weather or holiday logic; `DayToNightTime` constants parsed, unused. `IsActive` mixes local dates with UTC time of day. Client gets raw UTC via TimeSync. | `Maple2.File.Ingest/Mapper/ServerTableMapper.cs:917`; `Maple2.Model/Game/Event/GameEvent.cs:19-42`; `WorldServer.cs:56-68,154-237`; `GameSession.cs:302-303`; `TimeSyncPacket.cs:18-39`; `ConstantsTable.cs:908-914`; `GameEventManager.cs:46-49` | Clock abstraction at 5 injection points; persisted world-time/event state; recurring (yearly) event overlay; fix tz bug; data for holiday events (unknown whether `Server.m2d` still holds any) | Client behaviour on TimeSync unverified; user-value cleanup deletes progress of inactive events | High on code facts, low on client behaviour |
| B Legacy / Adapted modes | 11 `Offline*` switches, each an env var set by the launcher from `launcher.ini`, each preserving original behaviour when off. No preset, no mode stamp in the save. Dungeon `MinUserCount` entry rule is a hard block (no switch). `PlayerCountFactorId`, `MaxRevivalCount`, `PartyOnly`, `CustomMonsterLevel` are parsed but unused. No HP/damage scaling anywhere. Key list duplicated in 4 places; Linux launcher lacks PetTaming and DungeonEpicGear. | `Launcher/ServerHost.cs:78-96`; `Launcher/Settings.cs:70-107`; `DungeonManager.cs:286-291`; `DungeomRoomTable.cs:7`; `Dungeon.cs:11` (`DungeonGroupType`); `linux/ms2offline.py:101-109`; `docs/ADAPTATIONS.md` A1-A18 | Preset layer; save stamp; raid scaling design from data; list of raid dungeons by `MinUserCount`/group (not extracted yet) | None technical; raid feasibility needs play sessions | High |
| C Debugging / quality | Launcher: per-session report with redaction, Export Diagnostics zip, event log, port guard, backup + `integrity_check`. Server: Serilog console, packet hex trace at Verbose, trigger trace env, admin/debug commands, `DebugGame` visual debugger (not shipped). Ingest: `--probe-all`, per-table CRC, strict `Enum.Parse` (any bad value aborts). Tests: NUnit, about 253, in-memory SQLite, optional real `maple-data.db`. No data-integrity sweep, no top-level CI, unknown opcodes dropped silently. | `Launcher/SessionReport.cs:103-203,342-356`; `Launcher/Diagnostics.cs`; `Session.cs:277-279,449-482`; `TriggerContext.cs:53-77`; `PacketRouter.cs:21-31`; `ItemMapper.cs:117,147,167,183`; `GearRepairTest.cs:34-47`; `Maple2/.github/workflows/test.yml` (upstream only) | Sweep fixture, tolerant ingest with skip log, CI, triage categories, opcode counter | `GlitchHuntFixesTest.cs:10` cites `docs\audit\GLITCHES.md`, which does not exist | High |
| D Beginner start | Works today in Release: creation sets level 1, exp 0, empty currency, outfits only; start map from `job.xml`; starter weapons on request at Lv1; 1006 auto-start quests. Observed save: one Lv1 Striker on 63000015 with 7 outfit items, no grants. `start_tutorial` is a no-op; `RequestSkipTutorial` has no handler; no "New Game" in launcher; character delete leaves items/quests orphaned (inferred). A9 developer grants are `#if DEBUG`. | `CharacterManagementHandler.cs:114-201,203-233`; `TriggerContext.Dungeon.cs:116-118`; `RecvOp.cs:93`; `JobTable.cs:13-14`; `GameStorage.User.cs:588-619,671-682`; `Stats.cs:105-110`; `DbProvider.cs:234-255` | New Game / profile switch; tutorial skip handler; Lv1 playthrough test; orphan cleanup | Tutorial end-to-end never verified by test | High on code, medium on gameplay |
| E JP/KR voice | Audio is FMOD Ex (`fmodex64.dll`, `fmod_event64.dll`), 22 loose `.fsb` + 1 `.fev` in `Data\Sound`, not in any `.m2d`. Voice banks are language-suffixed: GMS `_en`, JMS `_jp`, KMS `_Kor_def` (JMS/KMS sets about 2x the size of EN). `Config\GameOption.xml` has `<language languageIndex voiceIndex>` (GMS 2/2, KMS 0/0). No launcher, patch or server code touches voice; the mod loader hooks `ReadXMLFile` only. Server sends voice ids as strings only. | agent survey of the three local clients; `_dl/Agarcium/AgarciumClient/xml_hook.cpp:63`, `dllmain.cpp:14`, `hook.cpp:151-160`; `ScriptMetadata.cs:28`; `NpcTalkPacket.cs:115` | Prove the switch (no code: copy banks + set `voiceIndex`); then a launcher import from the player's own JMS/KMS client; fallback rules | Cannot ship Nexon audio (README: "does not include the game client"); `.fev` event lists may differ (KMS `.fev` is larger); exe mapping of `voiceIndex` to suffix unconfirmed | Medium |
| F Torrents / tools | Updates: GitHub `releases/latest`, asset `MapleStory2-Offline-Update-*.zip`, ECDSA P-256 signature in the zip comment over `update.json`+`files.json`, per-file sha256, journaled apply with rollback, protected paths. Setup.exe and the full zip are unsigned and unhashed. No resume. Linux launcher: no signature check, fetches `dot.net` (contradicts SECURITY.md). No "MS2 Tools" product; the three `tools/` programs are developer-only and not packaged. No torrent/magnet mentions. | `Launcher/UpdateChecker.cs:12-19,104-112`; `Launcher/UpdateInstaller.cs:83-146,162-273`; `Launcher/UpdateKey.cs:8-13`; `Launcher/Setup.cs:136-151`; `tools/package_release.py:174-230,297-318`; `linux/ms2offline.py:714,790-822`; `SECURITY.md:11-14` | Signed release manifest for all artifacts; decide whether a tools bundle exists at all; torrent transport later | Torrent needs a client library (none in BCL), changes the GitHub-only promise, exposes player IPs | High |

Cross-cutting facts found on the way:
- Blocker carried over: the `Xml.m2d` build used for the 1.1.8 `maple-data.db` is not recorded (ingest from the raw client reproduces only 10 of 23 table checksums). `PROJECT_STATUS.md`.
- Client patch cannot be rebuilt (`C1047`, prebuilt MinHook). Any change needing a new hook is blocked until fixed. `PROJECT_STATUS.md`.
- The U-02 test instance (NA client + overlay `Xml.m2d` with 4,795 KMS records) logged one client crash, exit `0xC000000D`, on its first launch; four later launches opened the game window; the test outcome is not recorded. The overlay path is therefore unproven.
- `README.md:10` links `MapleStory2-Offline-Setup.exe` (unversioned); `package_release.py` emits `-Setup-<ver>.exe`. The alias is created by hand and undocumented. `UpdateKey.cs:5` cites `docs/RELEASING.md`, which does not exist.
- `UpdateInstaller.ParseVersion` drops non-numeric suffixes, so `1.2.0-beta` equals `1.2.0`; GitHub prereleases are skipped by `/releases/latest`, so a prerelease tag is safe from auto-update but a prerelease asset name must not be mistaken for the final.
- Character deletion removes only the `character` row; items, quests, achievements and dungeon records have no foreign key (inferred orphaning). `GameStorage.User.cs:671-682`.

---

## Deliverable 3: GitHub and local reconciliation

| Repository | Local | Remote | Divergence | Working tree |
|---|---|---|---|---|
| `MapleStory2-Offline` | `main` `610f6e8` | `origin/main` `610f6e8` | 0 / 0 | Untracked: `docs/engineering/{BACKLOG,PROJECT_STATUS,UNIFICATION}.md`, `tools/ClientDiff/{ClientDiff.csproj,Program.cs}`, `tools/KmsSplit/{KmsSplit.csproj,Program.cs}`, `tools/M2dPack/{M2dPack.csproj,Program.cs}`. `bin/`, `obj/` ignored. |
| `Maple2` (submodule) | detached `8000f16` | `origin/offline` `8000f16` | 0 / 0 (53 ahead of upstream `master`) | clean |
| `_dl/Agarcium` (submodule) | detached `f0974e5` | `origin/offline` `f0974e5` | 0 / 0 | clean |
| Internal docs repo (orchestration policies, outside this project) | `main` | local only | n/a | `COST_LOG.md` modified |

Installed player build on this PC = 1.1.8 = release asset (SHA-256 match recorded in `PROJECT_STATUS.md`). Test instance and
staging folders exist outside the repository and hold no unique source.

Review of the untracked tools (uncommitted change review, 2026-10-17): two must-fix items before commit:
1. `tools/ClientDiff/Program.cs:9-14`: `--extract` writes `<outDir>/<entry name>` without a containment check; an archive
   entry named with `..` escapes `outDir`. Fix: reject when `Path.GetFullPath(dest)` is not under `outDir`.
2. `tools/KmsSplit/Program.cs:40-44`: unknown options are ignored silently (`--only-id` converts every id). Fix: fail on an unknown option.
Should fix: `Kms` ctor crashes on a record without `id` with no file name (`:348`); `ClientDiff` writes full exception text with
local paths into `summary.md` (`:39,83`); `M2dPack` has no self-test for its two library workarounds; the required-skill-member
list exists twice in `KmsSplit` (`:197-209`, `:543-553`).

Safe integration steps (no history rewrite, nothing destructive):
1. Apply the two must-fix items (and the should-fix ones if cheap) in place.
2. `git add docs/engineering tools/ClientDiff/*.cs tools/ClientDiff/*.csproj tools/KmsSplit/... tools/M2dPack/...` and commit on `main`
   as "Engineering docs and client-data tools (ClientDiff, KmsSplit, M2dPack)". No submodule pointer change.
3. Add a `tools/README.md` line per tool (one sentence each) so the player README's "tools/: release packaging" row stays true.
4. Push. No tags.

---

## Deliverable 4: 1.2.0 scope proposal

Release theme: **modes and foundations**. One coherent, testable step: a player can pick Legacy or Offline-Adapted, start a
fresh game, and the project gains the clock, data-sweep and release-manifest foundations that every later feature needs.

| Pri | Item | Workstream | Why here / why not |
|---|---|---|---|
| P0 | Commit untracked tools and docs after review fixes | all | Work is at risk on a clean checkout; everything below cites them |
| P0 | Record and pin the data build: `Xml.m2d` + `Server.m2d` identity in the release, reproducible `maple-data.db` | C, A, B | Carried-over blocker; no data-dependent feature can be verified without it |
| P0 | Top-level CI: build Maple2 + Launcher, `dotnet test`, `KmsSplit --selftest`, `M2dPack` self-test | C | Regression gate for the rest of the release |
| P0 | Data-integrity sweep fixture (read-only over `maple-data.db`) | C | The "discover" stage of the debugging framework; cheap; finds whole classes of bugs the 1.1.x cadence fixed one by one |
| P0 | `GameClock` abstraction at the five injection points + `IsActive` time-zone fix | A | Prerequisite of every time feature; small; also fixes a live bug |
| P0 | Signed release manifest (`release.json` with sha256 of Setup, full zip, update zip, signed with the update key) | F | Prerequisite for any non-GitHub transport; closes the unsigned-Setup gap now |
| P1 | `GameMode=Legacy|Adapted` preset in launcher and server (`OfflineProfile`), per-key overrides kept, mode stamped in save, single key list | B | The release's headline; mostly plumbing over existing flags |
| P1 | New Game: save profiles in the launcher (switch `SaveDir`), backup first; tutorial skip handler (`RequestSkipTutorial` → `SkipField`/`SkipItem`); orphan cleanup on delete | D | Lv1 start already works; this makes it reproducible and reachable |
| P1 | Debugging framework stage 2: tolerant ingest with skip log, unknown-opcode counter, triage categories in `SessionReport`, defect-pinning test template | C | Extends existing tools, no new framework |
| P1 | Linux launcher parity for options and the manifest check (sha256 at least) | B, F | Prevents the two launchers drifting further; Linux currently installs unverified updates |
| P2 | Adapted solo-raid pilot: under Adapted, bypass `MinUserCount`, apply `PlayerCountFactorId` scaling if the table exists, on 2 named dungeons | B | Needs data extraction and play sessions; pilot only |
| P2 | Seasonal pilot: persisted world-state table, recurring yearly event overlay, one event type that already has server logic (`AttendGift` login calendar) with restart recovery | A | Proves persistence and recovery before any holiday content |
| P2 | Voice: manual experiment (JMS banks + `voiceIndex`), then `VoiceLanguage=en|jp|kr` import from a player-supplied client folder with fallback to `_en` | E | Cannot proceed past the experiment without the result; cannot ship audio |
| P3 | Day/night, seasons, weather, holiday events (Halloween, Thanksgiving, Christmas) | A | Client-side behaviour unverified; no data located; depends on P2 pilot |
| P3 | Solo-raid scaling across all raids; companion systems | B | Depends on pilot results and per-dungeon script review |
| P3 | Torrent transport (opt-in, same signed artifacts, GitHub webseed, HTTP fallback) | F | Needs a library, SECURITY/README wording change, seeding duty, privacy opt-in; no evidence GitHub bandwidth is a problem |
| P3 | "MS2 Tools" bundle | F | No such product exists; define it first (Open Decision 7) |
| P3 | Unified-edition tasks U-05..U-14 | (vision 2) | Separate track; U-02 crash must be understood first |

Deferred items return when: A3 after the P2 pilot survives a restart-during-event test and client day/night is observed;
B3 after two pilot dungeons are cleared solo under Adapted with recorded difficulty; F3 after the manifest ships and a
measured download problem exists; E full coverage after the experiment passes and a bank/event-id mapping is produced.

---

## Deliverable 5: Architecture and dependency analysis

Shared foundations (build first):
1. **Data build identity** (R12-02): without it, no sweep result or event data claim is reproducible.
2. **GameClock** (R12-05): one static `Now`/`UtcNow` provider with a test override, used by `GameEvent.IsActive`, World
   reset schedules, `ServerInfo` catch-up, the two TimeSync sends, and `GameEventUserValue` expiry. The remaining ~265 sites stay
   on real time by design (expiries, timestamps) and are listed, not migrated.
3. **OfflineProfile** (R12-07): one server-side table of option defaults per mode; launcher passes `OFFLINE_PROFILE`; existing env
   vars override. Replaces the four hand-kept key lists (`Settings.cs`, `ServerHost.cs`, `package_release.py`, `ms2offline.py`).
4. **Save stamp** (R12-08): `server-info` row `Mode` + `ModeHistory`; `SaveSchemaVersion` stays 1 (additive).
5. **Sweep fixture** (R12-04): `[Category("Data")]` tests over the real DB, skipping when absent (pattern: `GearRepairTest.FindDataDb`).
6. **Release manifest** (R12-06): signed by the same key as updates; verified by both launchers.

Dependency graph (arrow = needs):

```
R12-01 commit tools/docs
R12-02 data build identity ─────────────┐
R12-03 CI ──────────────────────────────┤
R12-04 sweep fixture ◄── R12-02         ├──► R12-10 ingest skip log ◄── R12-04
R12-05 GameClock + tz fix ──────────────┼──► R12-14 world-state table ──► R12-15 recurring event overlay ──► R12-16 AttendGift pilot
R12-06 release manifest ────────────────┼──► R12-13 Linux manifest check ──► (P3 torrent)
R12-07 OfflineProfile ──► R12-08 save stamp ──► R12-09 mode UI/docs
R12-07 ──► R12-17 Adapted raid pilot ◄── R12-18 raid data extract (needs R12-02)
R12-11 New Game profiles ──► R12-12 tutorial skip + Lv1 test ◄── R12-07 (mode decides start preset)
R12-19 voice experiment (manual) ──► R12-20 VoiceLanguage import
```

Critical path: R12-01 → R12-02 → R12-04 → R12-07 → R12-08 → R12-12 (Lv1 under both modes) → release validation. GameClock and
the manifest run in parallel on the side. Voice and raid pilots are off the critical path and can be dropped without
affecting the release.

Interfaces to respect:
- Launcher ↔ server contract is environment variables set at process start (`ServerHost.cs:78-96`); all `Offline*` classes
  read them into `static readonly` fields, so no runtime switching. Mode is therefore a restart-scoped setting.
- Client ↔ server: protocol 12, locale NA only (`ResponseVersionHandler.cs`). Nothing in this plan changes the client exe or
  needs a new patch hook, which keeps the `C1047` blocker out of the critical path.
- Save: additive schema only via `UpgradeSqliteSchema`; a non-additive change needs `SaveSchemaVersion` 2 and a
  `SaveMigrations` entry (`DbProvider.cs:112-143`). This plan needs none.
- Client data: `client-mods` toggled by renaming `mod.json` (`LauncherForm.cs:450-458`); mode presets reuse this.

Major compatibility risks: a Legacy session loading a character with Adapted-origin items (Epic dungeon gear, Meret
pouches) is allowed today and will stay allowed; the stamp only warns (Open Decision 3). Switching GameClock semantics
(paused world time) would break every wall-clock expiry; the plan keeps real time and only abstracts the call.

---

## Deliverable 6: Implementation backlog

Format: id, objective; files; depends on; acceptance; tests; complexity/risk; tier; deliverables.

**Phase 0**

- **R12-01** Review fixes and commit of untracked tools and docs. `tools/ClientDiff/Program.cs`, `tools/KmsSplit/Program.cs`,
  `tools/M2dPack/Program.cs`, `docs/engineering/*`, new `tools/README.md`. Depends: none. Accept: both must-fix items applied;
  `git status` clean; `dotnet build` of the three tools 0 errors. Tests: `KmsSplit --selftest` passes; new `M2dPack selftest <base>` passes on the NA archive.
  Low / low. T2 + T4 review. Deliverable: one commit on `main`.
- **R12-02** Data build identity. `tools/package_release.py`, new `GameData/DATA-BUILD.txt` (sha256 of `Xml.m2d`, `Server.m2d`,
  ingest commit, locale), `Launcher/Diagnostics.cs` (include it in `summary.txt`). Depends: none. Accept: a release folder names its
  inputs; rebuilding `maple-data.db` from the named inputs reproduces all 23 table checksums, or the differing tables are listed with cause.
  Tests: script run on the 1.1.8 inputs. Medium / medium (the 1.1.8 inputs may be unrecoverable; then 1.2.0 ships a freshly built, now-recorded DB and says so). T2. Deliverable: file in release, note in `PROJECT_STATUS.md`.
- **R12-03** Top-level CI. New `.github/workflows/ci.yml` in the launcher repo: checkout with submodules, .NET 8, build
  `Maple2/Maple2.sln` and `Launcher`, `dotnet test Maple2.Server.Tests`, `KmsSplit --selftest`. Depends: R12-01. Accept: green run on `main`;
  a deliberately failing test turns it red. Low / low. T1. Deliverable: workflow + badge-free README line.
- **R12-04** Data-integrity sweep. New `Maple2.Server.Tests/Data/DataSweepTest.cs` (`[Category("Data")]`, skips without DB):
  quest → NPC/map references, portal → map, spawn → NPC, trigger names parse via `TriggerFunctionMapping`, navmesh file per xblock
  (`Navmeshes/`), item option-table coverage per rarity, dungeon `MinUserCount`/group listing written to a report file. Shared
  `FindDataDb` helper extracted from `GearRepairTest.cs:34-47`. Depends: R12-02. Accept: runs on the shipped DB; emits
  `sweep-report.md` grouped by severity (error = reference missing; warning = unused/unimplemented); zero crashes. Tests: it is the test. Medium / low. T2. Deliverable: fixture + first report attached to `KNOWN_ISSUES` section of `PROJECT_STATUS.md`.
- **R12-05** GameClock. New `Maple2.Tools/GameClock.cs` (`UtcNow`, `Now`, `Override(Func<DateTimeOffset>)` for tests).
  Use at `GameEvent.cs:19-42` (also fix: compare dates in one zone), `WorldServer.cs:154-237`, `GameStorage.ServerInfo.cs`,
  `GameSession.cs:302-303`, `GameEventManager.cs:46-49`. Depends: none. Accept: no behaviour change at real time; a test sets the
  clock to a date-bounded event's window and `IsActive` is true at 00:30 local on the start day (today false, see tz bug). Tests:
  new `GameEventActiveTest` (3 cases: before/inside/after, plus day-of-week). Low / medium (touches resets). T2 + T4. Deliverable: class + 5 call-site edits + tests.
- **R12-06** Signed release manifest. `tools/package_release.py` writes `release.json` {version, files:[{name,size,sha256}], notes};
  `tools/sign_update.py` gains `--manifest`; `Launcher/UpdateChecker.cs` downloads `release.json` + `.sig` first and pins the asset sha256;
  `Launcher/Setup.cs:136-151` verifies the payload zip against the sha256 embedded next to the magic trailer. Depends: none.
  Accept: a tampered byte in Setup payload or update zip is refused with a clear message; old launchers ignore the new assets.
  Tests: unit test for manifest parse/verify (new `Launcher.Tests` project, first launcher tests). Medium / medium. T2 + T4 (security). Deliverable: manifest in release, `SECURITY.md` paragraph.

**Phase 1**

- **R12-07** OfflineProfile. New `Maple2.Server.Core/Constants/OfflineProfile.cs`: `Mode` from `OFFLINE_PROFILE` (`Legacy|Adapted`,
  default `Adapted` for existing installs), default per flag; each `Offline*.Enabled` reads env override else profile default.
  `Launcher/Settings.cs` gains `GameMode`; `ServerHost.cs` passes it; `LauncherForm.cs:165-167` toggles `offline-shop` too; `package_release.py`
  `PLAYER_DEFAULTS` drops per-flag lines in favour of `GameMode=Adapted` (per-flag lines still honoured); `linux/ms2offline.py` same.
  Legacy = all A5, A13-A18 off, cheats off, Meret catalog off (`OfflineEconomy` files ignored). Depends: none. Accept: `GameMode=Legacy`
  with no other keys yields original rates, 30-day mail, original dungeon limits, no `/godmode`; `GameMode=Adapted` equals 1.1.8 defaults;
  a per-flag line overrides either. Tests: extend `OfflineEditionTest` with profile cases via env. Medium / medium. T2 + T4. Deliverable: code, `launcher.ini.example`, ADAPTATIONS row.
- **R12-08** Save stamp. `server-info` keys `Mode`, `ModeChangedAt`, `ModeHistory` (additive). On login under Legacy with
  `ModeHistory` containing Adapted, log once and show a client notice (reuse `NpcNoticePacket`). Depends: R12-07. Accept: stamp
  written on first start, history appended on change, notice shown once per session. Tests: `SaveSafetyTest` addition. Low / low. T2.
- **R12-09** Mode UX and docs. First-run prompt in `LauncherForm.cs` (two buttons, explanation text), `README.md` and player
  `README.txt` sections, `docs/ADAPTATIONS.md` table column "Mode". Depends: R12-07. Accept: choice saved to `launcher.ini`; docs list
  which options each mode sets. Low / low. T1 text + T2 form.
- **R12-10** Tolerant ingest. `Maple2.File.Ingest/MapperExtensions.cs` helper `ParseEnumOrSkip<T>(value, context)` writing
  `ingest-skipped.log`; replace the strict sites listed in the C audit (`ItemMapper.cs:117,147,167,183`, `TableMapper.cs:144,187-196,649-655,755,991`,
  `MapperExtensions.cs:342,383,403,435`). Depends: R12-04 (sweep proves nothing regressed). Accept: NA ingest produces an empty skip log
  and identical table checksums; a synthetic bad enum is skipped and logged, not fatal. Tests: unit test on the helper + checksum compare. Medium / medium. T2 + T4.
- **R12-11** New Game profiles. `Launcher/Settings.cs` `Profile` (sub-folder of `SaveData`); `LauncherForm.cs` "New game" button:
  backup current, create `SaveData/<name>/`, switch; "Switch profile" list. `Diagnostics.cs` and `SessionReport.cs` name the active profile.
  Depends: none. Accept: new profile starts at character select with no characters; old profile untouched; backups per profile.
  Tests: launcher unit test on path logic; manual: two profiles, 10 backups each. Medium / medium (save paths). T2 + T4.
- **R12-12** Tutorial skip and Lv1 playthrough. New handler for `RecvOp.RequestSkipTutorial` using `JobTable.SkipField`/`SkipItem`
  (`JobTable.cs:13-14`); implement `start_tutorial` minimally or document the no-op; delete-character cleanup for item, quest,
  achievement and dungeon rows (`GameStorage.User.cs:671-682`). Depends: R12-07, R12-11. Accept: skip lands the character on Lith Harbor
  2000062 with the skip items; a Lv1 of each of the 12 classes reaches Lith Harbor under both modes (manual matrix, recorded);
  deleting a character leaves no rows with its id. Tests: unit for cleanup; manual matrix sheet. Medium / medium. T2 + maintainer play.
- **R12-13** Linux parity. `linux/ms2offline.py`: GameMode + the two missing flags + `offline-pets` toggle; verify `release.json`
  sha256 for downloaded assets (stdlib `hashlib`); remove or gate the `dot.net` fetch behind an explicit prompt and document it.
  Depends: R12-06, R12-07. Accept: option list identical to Windows; tampered asset refused. Tests: Python unit test for the verify function. Low / low. T2.
- **R12-21** Debugging stage 2: `PacketRouter.cs:21-31` counts unknown opcodes and logs once per opcode per session; `SessionReport.cs:173`
  gains a known-signature table (regex → category → doc anchor) and a `--triage` output; a bounded ring buffer of the last 200 trigger
  events included in reports even when trace is off. Depends: R12-04. Accept: a report with a known failure shows its category;
  unknown opcodes appear with counts. Tests: unit on the classifier. Medium / low. T2.

**Phase 2 (pilots, each droppable)**

- **R12-14** World-state table: `server-info` keys `WorldLastRun`, `WorldTotalRunSeconds`; written every minute by World.
  Depends: R12-05. Accept: survives restart; readable in diagnostics. Low / low. T2.
- **R12-15** Recurring event overlay: `Maple2/offline-events.json` (id, type, month-day start/end, yearly) loaded by
  `ServerTableMetadataStorage` after the ingested events, rebased to the current year at load, re-evaluated on World start
  and at midnight. Depends: R12-05, R12-14. Accept: an event defined for yesterday-to-tomorrow is active now; one for last week is not;
  the same file works in a different year; `GameEventUserValue` rows of an inactive overlay event are kept, not deleted (change
  `GameEventManager.cs:46-49` to expire by `ExpirationTime` only). Tests: `GameClock`-driven unit tests across a year boundary and a leap day. Medium / medium. T2 + T4.
- **R12-16** AttendGift pilot: one overlay event using the existing `AttendanceHandler.cs` logic for a seven-day window; restart
  during the window keeps the claimed days. Depends: R12-15. Accept: claim day 1, restart, day 2 claimable, day 1 not re-claimable;
  server down across the end date closes the event on next start. Manual + unit. Low / medium. T2.
- **R12-17** Adapted raid pilot: under Adapted, `DungeonManager.cs:288` skips `MinUserCount` when `OfflineProfile.SoloRaids`;
  if `playerCountFactor` data exists in `Server.m2d`/tables, apply it to monster HP/attack in `DungeonFieldManager` for `Size=1`; else
  a configurable `offline-raids.json` {dungeonId: hpFactor, attackFactor, reviveCap}. Depends: R12-07, R12-18. Accept: two named
  dungeons (one `raid`, one `chaosRaid`) enterable solo; a solo clear is possible with level-appropriate gear in a recorded play
  session; Legacy still refuses at the door. Tests: unit on factor application; manual clears. High / high. T3 + maintainer play.
- **R12-18** Raid data extract: from the sweep (R12-04) list every dungeon with `GroupType`, `MinUserCount`, `MaxUserCount`,
  `PlayerCountFactorId`, `MaxRevivalCount`; from `--probe-all` list xblocks whose triggers use `count_users`/`user_count` with n > 1.
  Depends: R12-02. Accept: `docs/engineering/RAIDS.md` table. Low / low. T1.
- **R12-19** Voice experiment (manual, no code): on a copy of the NA client, set `voiceIndex` in `Config\GameOption.xml` to 1 and
  to 0 with JMS `MS2Sound_Voice*_jp.fsb` and KMS `*_Kor_def.fsb` banks copied into `Data\Sound`; record which NPC lines play, which
  are silent, and any crash. Depends: none. Accept: a written result for each index value and bank set. T0 maintainer.
- **R12-20** VoiceLanguage import (only if R12-19 passes): `Launcher/Settings.cs` `VoiceLanguage=en|jp|kr`, `VoiceSourceDir`; launcher
  copies the matching banks from the player's own JMS/KMS client into the NA client's `Data\Sound` with the originals backed up,
  edits `GameOption.xml` `voiceIndex`, restores on switch back; refuses if any bank is missing (fallback `_en`). Depends: R12-19.
  Accept: switch, play a line, switch back, original banks byte-identical. Tests: unit on file plan; manual playback. Medium / medium. T2 + T4.

**Phase 3 (deferred, not scheduled)**: day/night and weather (needs client observation), holiday content (needs located data),
full raid scaling, torrent transport (`update.json` `magnet`+`sha256` fields are already signed; MonoTorrent or similar; opt-in;
webseed), "MS2 Tools" bundle definition, U-05..U-14.

---

## Deliverable 7: Test and audit plan

| Area | Automated | Manual | Needs unavailable thing |
|---|---|---|---|
| Clean build | CI (R12-03): both solutions, 3 tools | fresh clone with `--recurse-submodules` builds on a second machine | client patch build (`C1047`) stays manual until fixed |
| Regression | `dotnet test` 237+; sweep fixture; `KmsSplit --selftest`; `M2dPack selftest` | 1.1.8 save loads, plays, saves under 1.2.0 | |
| New game Lv1 | cleanup unit test; profile path tests | 12 classes × 2 modes reach Lith Harbor (matrix sheet) | |
| Save/load, restart | `SaveSafetyTest`; stamp test | restart mid-dungeon; update 1.1.8→1.2.0 keeps save and `launcher.ini` | |
| Time, calendar | GameClock tests: year boundary, leap day (2028-02-29), DST change day, restart inside window | clock set back on the PC, game still starts and events behave | client day/night: none |
| Seasonal pilot | overlay tests | AttendGift restart scenario | |
| Legacy preservation | profile tests assert every `Offline*.Enabled == false` | dungeon limits, mail expiry, no cheats, Meret Market absent | original server behaviour beyond the emulator: cannot be tested |
| Adapted raids | factor unit test | two pilot dungeons solo-cleared, recorded | |
| Voice | file-plan unit test | R12-19 result; switch/back byte-identical | JMS/KMS clients are the player's own; none shipped |
| Distribution integrity | manifest verify tests; tamper test | SmartScreen path; Linux refuse-on-tamper | code signing (no certificate) |
| Torrent | none (deferred) | | |
| Migration | `OfflineEditionTest` schema upgrade | 1.1.0 and 1.1.8 saves opened by 1.2.0 | |
| Debug framework safety | sweep is read-only (test asserts no write); skip log never deletes | report categories reviewed on three real reports | |

Release gates (all must hold): CI green; sweep report has zero new errors versus the 1.1.8 baseline; Lv1 matrix complete;
1.1.8 save migration verified; both modes verified against the Legacy/Adapted checklist; manifest verified on Windows and Linux;
secret scan of the release folder clean; `files.json` and `release.json` match the uploaded assets. A feature whose defining
behaviour was not exercised (e.g. a raid pilot with no recorded solo clear) is removed from the release notes, not declared done.

---

## Deliverable 8: Risk register

| # | Risk | Likelihood | Impact | Evidence | Mitigation | Contingency |
|---|---|---|---|---|---|---|
| 1 | 1.1.8 data inputs unrecoverable; 1.2.0 DB differs from 1.1.8 in 13 tables | medium | medium | `PROJECT_STATUS.md` checksum result | R12-02 records inputs; sweep diff of the two DBs | ship the new DB, list table differences in notes |
| 2 | GameClock change alters reset timing | low | high | resets anchored to TickCount, catch-up on boot | keep real time; tests on boundaries; T4 review | revert the five call sites |
| 3 | Legacy loads Adapted-origin items; players call it a bug | medium | low | items baked into rows; `OfflineGearRepair` precedent | stamp + one-time notice | document in README |
| 4 | Profile switch loses or confuses saves | low | high | one `SaveFile` today | backup before switch; profile named in report | restore from `SaveData\backups` |
| 5 | Tolerant ingest hides a real data error | medium | medium | strict parse today | skip log reviewed; checksum identical for NA | keep strict mode flag |
| 6 | Voice switch does nothing or crashes the client | medium | low | suffix logic unconfirmed; `.fev` sizes differ | experiment first, feature gated on result | drop E from 1.2.0 |
| 7 | Solo raid pilot trivialises or remains impossible | high | low | no scaling data verified | pilot on 2 dungeons; factors in a json | drop or ship as experimental option |
| 8 | Seasonal overlay collides with ingested events or deletes progress | medium | medium | `GameEventManager.cs:46-49` deletes inactive values | change expiry rule; distinct id range for overlay | disable overlay file |
| 9 | Manifest/Setup verification breaks installs for old launchers | low | high | old launchers parse `update.json` loosely | new assets are additive; test 1.1.1 launcher against the 1.2.0 release | republish without manifest |
| 10 | Client patch unbuildable (`C1047`) blocks a needed hook | low for this plan | medium | `PROJECT_STATUS.md` | plan avoids new hooks | rebuild MinHook from source |
| 11 | U-02 overlay archive crash recurs | medium | medium (unified track) | exit `0xC000000D` logged | record the test outcome; keep unified track out of 1.2.0 | |
| 12 | Untracked tools lost before commit | medium | medium | `git status` | R12-01 first | |
| 13 | Linux launcher installs tampered asset | low | high | no signature check | R12-13 sha256 pin | advise manual install |
| 14 | Weekly subscription windows exhausted by Fable-tier work | medium | medium | routing policy | T1/T2 for all plumbing; T3 only for R12-17 | pause pilots |

---

## Deliverable 9: Release roadmap

| Phase | Tasks | Deliverables | Exit criteria | Major risks |
|---|---|---|---|---|
| 0 Baseline | R12-01..06 | tools committed; data build recorded; CI; sweep baseline; GameClock; manifest | CI green; sweep report filed; manifest verified | 1, 2, 9 |
| 1 Modes and new game | R12-07..13, R12-21 | GameMode presets; stamp; UX/docs; tolerant ingest; profiles; tutorial skip; Linux parity; debug stage 2 | Lv1 matrix done; mode checklist done; 1.1.8 save migrates | 3, 4, 5 |
| 2 Pilots | R12-14..20 | world-state; overlay + AttendGift; raid extract + pilot; voice experiment (+ import) | each pilot has a recorded pass or is cut | 6, 7, 8 |
| 3 Hardening and release | validation plan, notes, tag | release notes; `DATA-BUILD.txt`; `release.json`; update zip from `--base 1.1.8` | all gates in Deliverable 7 | 9, 11 |

Order change from the first plan: dynamic world moves after modes/progression because it depends on GameClock and the
world-state table, and because modes are the user-facing value of this release. Voice and distribution stay in phase 2 as
experiments only.

---

## Deliverable 10: Proposed release notes (draft; planned, not shipped)

```
## What's new in 1.2.0 (planned)

Planned - not yet implemented:
- Game modes: choose Legacy (the original rules, no solo-play changes, no cheat commands) or Offline-Adapted (the 1.1.x
  solo-friendly defaults). The launcher asks once; launcher.ini GameMode=Legacy|Adapted; single options still override.
- New game: save profiles. Start a fresh level-1 character without touching your existing save; switch between profiles.
- Tutorial skip works (Lith Harbor, starter items), as the original client offered.
- Deleting a character no longer leaves its items and quests behind in the save.
- Release files are now covered by a signed manifest; Setup and updates refuse tampered downloads. Linux checks the same hashes.
- Game data build is recorded in the release (GameData/DATA-BUILD.txt) and shown in Export Diagnostics.
- Time-limited in-game events now use one game clock; events starting at midnight no longer miss their first hours.

Experimental (shipped only if the pilot passed; otherwise removed from this list):
- A recurring seven-day login-gift event that survives restarts.
- Two raid dungeons enterable solo in Offline-Adapted mode with adjusted monster strength.
- VoiceLanguage=jp|kr: use the Japanese or Korean voice banks from your own copy of that client.

Already in 1.1.8 (unchanged): one-click launcher, SQLite saves with backups, signed updates, loopback-only client, Meret
economy, solo-friendly options, session reports.

Known issues carried over: client patch cannot be rebuilt from source on current MSVC (C1047); Korean/Japanese content
(unified edition) is not part of this release.
```

---

## Deliverable 11: Recommended next actions (dependency order)

1. R12-01: apply the two must-fix review items, commit tools and docs, push. (T2, one session)
2. R12-02: try to identify the 1.1.8 `Xml.m2d`; whatever the result, make `package_release.py` record inputs. (T2)
3. R12-03: CI workflow. (T1)
4. R12-05: GameClock + `IsActive` fix + tests. (T2, T4 review)
5. R12-04: sweep fixture, baseline report. (T2)
6. R12-06: signed manifest, launcher verification, first `Launcher.Tests`. (T2, T4 security review)
7. R12-07 → R12-08 → R12-09: mode presets, stamp, UX. (T2)
8. R12-11 → R12-12: profiles, tutorial skip, Lv1 matrix with the maintainer. (T2 + play)
9. R12-19: voice experiment by the maintainer (no code), result recorded in this folder.
10. R12-18 then decide R12-17; R12-14/15/16 only if time remains after phase 1.

---

## Deliverable 12: Open decisions

1. **Vision ordering.** 1.2.0 as "modes and foundations" (this plan) versus continuing the unified edition (vision 2) as the
   next release. Default: this plan; the unified track continues on its own branch after the U-02 crash is understood.
   Alternative: 1.2.0 = unified edition, and this plan becomes 1.3.0; cost is that the clock/sweep/manifest foundations wait.
2. **Default mode for existing installs.** Default `Adapted` (behaviour unchanged for 1.1.x players) versus asking everyone
   once. Default: `Adapted` silently, prompt only on a fresh install. Alternative risks a surprise rule change on update.
3. **Legacy loading Adapted-origin saves.** Warn once (default) versus refuse versus strip items. Refusing breaks players who
   try Legacy out of curiosity; stripping destroys items. Default: warn.
4. **World time model.** Real time everywhere (default; expiries keep running while the game is closed) versus paused world
   time while offline. Paused time needs every expiry re-based and is deferred until the pilot shows demand.
5. **Seasonal event source.** Overlay JSON with yearly recurrence (default) versus re-enabling the client's `gameEvent.xml`
   entries by rewriting dates at ingest. Rewriting touches data derived from Nexon files and changes every ingest.
6. **Voice assets.** Import from the player's own JMS/KMS client only (default; nothing shipped) versus any form of
   redistribution (not acceptable under the project's own rule). Also whether to pursue E at all if R12-19 fails.
7. **"MS2 Tools".** Does a player-facing tools bundle exist as a goal? Default: no bundle in 1.2.0; developer tools stay in
   `tools/` with a README. If yes, it needs its own no-Nexon-data rule and its own manifest entry.
8. **Torrents.** Default: deferred; prerequisite (signed manifest) ships in 1.2.0. If pursued later: opt-in setting,
   `magnet` + `sha256` in the signed `update.json`, GitHub webseed, HTTP fallback, SECURITY.md and README rewording, library
   licence check against AGPL.
9. **Release type and tag.** Stable `v1.2.0` (default) with the usual Setup + cumulative Update zip (`--base 1.1.8`).
   A `v1.2.0-rc1` GitHub prerelease is safe from auto-update (`/releases/latest` skips prereleases) but `ParseVersion`
   treats `1.2.0-rc1` as `1.2.0`, so never name a prerelease asset `MapleStory2-Offline-Update-1.2.0.zip`.
10. **Feature freeze.** Freeze when phase 1 exit criteria hold; phase 2 pilots are included only with a recorded pass.
    Default: freeze date set after R12-12 completes, not before.

Rollback: the existing update journal and `RollBackLastUpdate` cover a failed 1.2.0 update; the 1.1.8 Setup stays on the
Releases page; `SaveSchemaVersion` stays 1, so a 1.2.0 save opens in 1.1.8 (new `server-info` keys are ignored).
