# 1.2.0 development plan: unified content on the Global client through the patcher

Written 2026-10-18 against `main` `793091b` (1.1.9 released). Decision by the maintainer, 2026-10-18: 1.2.0 ships the
regional content (Korean, Japanese, and the Chinese and Taiwanese text) merged into the Global client that players
already have (Steam, or the community downloader's copy of the same build), delivered and kept up to date by the
launcher. This resolves Open Decision 1 of [RELEASE_PLAN_1.2.0.md](RELEASE_PLAN_1.2.0.md): the "modes and foundations"
items that 1.1.9 did not finish (R12-08 save stamp, R12-11 profiles, R12-12 tutorial skip) stay in 1.2.0 as the
second track; the pilots (R12-14 to R12-20) move to 1.3.0.

Evidence for every "measured" claim is in [UNIFICATION.md](UNIFICATION.md) (sections 2 and 6) and
[BACKLOG.md](BACKLOG.md). Task ids: existing `U-nn` and `R12-nn` are reused; new patcher tasks are `P-nn`.

---

## 1. What "unified content" means in 1.2.0

The Global client (`MapleStory2.exe` 20.8.1.0316) stays the only supported client and the only protocol. Content is
added per record for ids the Global data does not have; no Global record is changed (section 4 of UNIFICATION.md).

| Content | Source of truth | Count (measured) | Delivered as |
|---|---|---|---|
| KMS-only items, NPCs, quests, skills | KMS end-of-service build (same ids as the 2024-05 build) through the converter `KmsSplit` (U-04, U-04d) | 4,198 items, 152 NPCs, 336 quests, 267 skills | per-id XML appended to `Xml.m2d`; rows in `maple-data.db` |
| KMS-only maps and NPC scripts | KMS v61 and v69 clients, which still carry per-id `map/` and `script/` (UNIFICATION 6.1); nothing after v69 has them | 32 to 38 maps, 4 to 8 scripts not in Global; the rest of the KMS-only NPCs use Global scripts or none | appended to `Xml.m2d` as is (no conversion needed) |
| JMS-only content | JMS final client | 11 items, 17 quests, 4 skills | same path (U-11) |
| Icons, models, textures, effects for the added items and NPCs | KMS end-of-service resource archives | 1,308 icons, 3,680 model/texture/effect/NPC files (U-08 pilot) | appended to the six resource archives |
| Names and text | KMS `string/en` where it exists, the item-name translation file (U-10, 3,615 names), KMS `string/kr`, `string/jp`, `string/cn`; TW `string/en` as a second English source | 422 + 3 names already English, 3,615 translated, NPC names and quest text still Korean | `string/en` tables replaced whole (client mod), other locales shipped for the language switch (phase 4) |
| Server tables for the added content | community `Server.m2d` v1.3.2 (the TW dev leak holds the same files, 18 differ; not used) plus the 33 client tables only KMS v61 still has (`npcspawndata`, `individualitemdrop*`, ...) | measured in UNIFICATION 6.1 | ingest input; rows in `maple-data.db` |

Not in 1.2.0: CMS and TW content (they add nothing over KMS, measured), Korean voice banks (cannot be shipped),
KMS-only dungeons that need instance scripts the Global client lacks (listed, not restored), the in-game language
switch beyond English (phase 4 prepares the files; the switch itself is 1.3.0 unless phase 3 finishes early).

## 2. Delivery architecture: append-only content pack

Measured on the U-08 test client: every archive repacked with `tools/M2dPack` is a byte-for-byte prefix extension of
the Global archive. `Image.m2d` keeps its first 1,000,458,896 bytes unchanged and gains 181 MB; `Xml.m2d` keeps its
191,573,128 bytes and gains 8.9 MB. Only the header file (`.m2h`) is rewritten. So a content pack does not need to
ship or back up the archives:

| Archive | Global size | Appended (tail) | New header |
|---|---|---|---|
| `Data/Xml.m2d` | 191.6 MB | 8.9 MB | 2.4 MB |
| `Data/Resource/Image.m2d` | 1,000.5 MB | 181.3 MB | 0.6 MB |
| `Data/Resource/Model/Character.m2d` | 131.6 MB | 11.7 MB | 0.1 MB |
| `Data/Resource/Model/Effect.m2d` | 705.5 MB | 61.1 MB | 0.5 MB |
| `Data/Resource/Model/Item.m2d` | 546.2 MB | 39.2 MB | 0.3 MB |
| `Data/Resource/Model/Npc.m2d` | 1,043.7 MB | 41.1 MB | 0.9 MB |
| `Data/Resource/Model/Textures.m2d` | 1,862.2 MB | 81.0 MB | 0.9 MB |
| total download | | about 424 MB (entries are already compressed; the zip gains little) | 5.7 MB |

The pack is a zip `content-pack-<version>.zip` with: `pack.json` (pack version, required Global archive sha256 and
size per archive, tail sha256 and size, new `.m2h` sha256, original `.m2h` sha256, the `data-build.json` of the
matching `maple-data.db`), one `<archive>.tail` and one `<archive>.m2h` per archive, the `client-mods/unified-strings`
folder, and `GameData/maple-data.db`. It is listed in the signed `release.json` (R12-06, shipped in 1.1.9) like any
other asset, so the launcher refuses a tampered pack.

Apply (launcher, before Play, idempotent):

1. For each archive: if size equals Global size and sha256 of the file equals the Global hash, append the tail and
   replace the `.m2h` (keep the original `.m2h` as `<name>.orig.m2h`, 5.7 MB in all). If size equals Global size plus
   tail size and the last tail-size bytes hash to the tail hash, the pack is already applied: do nothing. Anything else
   (unknown client build, half-applied file, a Steam update) stops with a message and nothing is changed; the game can
   still be played without the pack.
2. Write the pack version to `install.json`; `SessionReport` and Export Diagnostics name it.
3. Steam's "verify integrity" restores the Global archives (it truncates nothing; it replaces the files). The next Play
   re-applies from the cached pack in `updates/`; no download.

Restore ("Remove unified content" in the launcher, and the uninstall path): truncate each archive to the Global size
and put the `.orig.m2h` back; verify sha256. No multi-gigabyte backup is ever written.

Why not the alternatives: a loose-file overlay needs U-03 (untested, and would still not cover models); a second copy
of the client costs 14 GB per player; a client-patch hook to redirect archive reads is blocked by the `C1047` build
error and would add a hook on every file open. The append path works with the client as it is and was verified in
game on the test instance (U-02, U-07, U-08).

Hard rule: the pack and the server data must match. The launcher refuses to start the servers when
`GameData/data-build.json` does not name the pack version that is applied (or none, for a client without the pack),
because the Global shop table already gained entries for KMS-only items (U-06 side effect).

## 3. Phases and tasks

Dependency order. Tier codes as in RELEASE_PLAN_1.2.0.md (T1 scripted, T2 standard, T3 complex or
maintainer, T4 independent review).

### Phase A: content set frozen (data only, no launcher change)

| Id | Task | Depends | Accept | Tier |
|---|---|---|---|---|
| U-05 | Shared-id comparison Global vs KMS EoS (normalised XML): list ids whose content differs by field; decide per field whether Global stays (default: always) | - | report `SHARED_IDS.md`; zero Global records changed in the pack | T2 |
| U-15 (new) | Old-KMS extract: from v61 and v69 take `map/`, `script/` and the 33 dropped `table/` files for ids not in Global; run them through `KmsSplit --validate` and the ingest parser | - | list of accepted and rejected files with the failing field | T2 |
| U-14 | Feature gate: KMS feature names added to `table/feature.xml` without changing any Global record's resolved environment | U-06 | Global rows unchanged (row compare); the 313 items, 92 NPCs, 303 quests, 22 skills now present or listed as unsupported | T2 + T4 |
| U-12 | Server tables for the added quests and NPCs (spawns, drops, AI) from `Server.m2d` plus the U-15 tables; KMS-only dungeons that have no instance script are listed as unsupported | U-15, U-14 | the later Epic Quest (10003449 to 10003474) playable start to finish on the test instance | T3 + maintainer play |
| U-10 | Translation review of the 3,615 item names; NPC names (152) and the Epic Quest text translated | U-09 | reviewed file, placeholders intact, glossary file started | T2 + review |
| U-11 | JMS-only records through the same converter path | U-06 | each record playable or listed | T1 |
| U-08 | Visual check of the repacked art on the test instance (the pending TEST-U08 list) | - | every listed item shows icon and model, or is on the unsupported list | maintainer play |
| U-16 (new) | Content registry: `CONTENT_REGISTRY.md` with one row per added quest, NPC, map and dungeon: source build, server tables present, test status | U-12 | registry exists; every "playable" row has a recorded test | T1 |

Exit: `tools/build_content_pack.py` (P-01) can build the full pack from named inputs; the registry says what is
playable; Global tables unchanged by checksum.

### Phase B: pack build and launcher apply

| Id | Task | Depends | Accept | Tier |
|---|---|---|---|---|
| P-01 | `tools/build_content_pack.py`: runs `KmsSplit`, the U-15 extract and `M2dPack overlay` for the seven archives, computes the tails (`new[GlobalSize:]`), writes `pack.json`, the mod folder and the rebuilt `maple-data.db` with `data-build.json`; refuses when any Global prefix is not byte-identical | A | pack rebuilt twice from the same inputs is byte-identical; `M2dPack verify` passes per archive; pack size reported | T2 + T4 |
| P-02 | `Launcher/ContentPack.cs`: parse `pack.json`, apply (section 2), detect applied state, restore by truncation; every step journaled like `UpdateInstaller`; elevated retry when the client folder is write-protected (reuse `ClientLocator.InstallPatchElevated`) | P-01 | unit tests with small synthetic archives: fresh apply, re-apply, half-written tail, wrong Global hash, restore; a tampered tail is refused | T2 + T4 (security) |
| P-03 | Launcher flow: `release.json` lists the pack; "Play" downloads it once into `updates/` (resumable, sha256 checked), applies, then starts; "Remove unified content" and uninstall restore; `UnifiedContent=true|false` in `launcher.ini` (default true) | P-02, R12-06 | fresh 1.1.9 install updated to 1.2.0 gets the pack on first Play; switching the option off restores the Global archives byte-identical | T2 |
| P-04 | Server/data lock: the launcher passes the applied pack version to the servers; `GameData/data-build.json` carries the pack version; mismatch refuses to start with a clear message | P-03 | matrix: no pack + Global DB ok; pack + unified DB ok; the two cross cases refused | T2 |
| P-05 | Linux launcher parity (`linux/ms2offline.py`): same apply, restore and lock logic, stdlib only | P-02 | same test matrix on Linux | T2 |
| P-06 | Diagnostics: pack version, per-archive state and the last apply journal in the session report and Export Diagnostics | P-03 | report shows the state after a deliberate half-apply | T1 |

Exit: a player with a Global client (Steam or the community downloader) presses Play once and has the content; a
player who turns it off is back on the untouched Global archives.

### Phase C: modes and new game (carried over from RELEASE_PLAN_1.2.0.md)

R12-08 save stamp, R12-09 mode docs, R12-11 save profiles, R12-12 tutorial skip and the level-1 matrix, R12-10
tolerant ingest, R12-13 Linux parity, R12-21 debug stage 2. Unchanged scope and acceptance; R12-10 now runs before P-01
so an unknown KMS enum value cannot stop the pack build.

Unified content and the modes are independent: the pack is applied in both Legacy and Adapted. Open decision C-1:
whether Legacy should hide the added content (it is not part of the original Global experience). Default: Legacy keeps
the content and the registry marks each record with its source edition; a `UnifiedContent=false` line removes it for
players who want the pure Global game.

### Phase D: languages (prepares 1.3.0; ships in 1.2.0 only if B is done early)

| Id | Task | Depends | Accept |
|---|---|---|---|
| L-01 | Ship `string/kr`, `string/jp`, `string/cn` (from KMS EoS and TW, the Global `string/de` and `string/pt` already exist) inside the pack; measure what the client does when `GameOption.xml` selects a locale whose folder exists (experiment on the test instance, no code) | P-01 | written result per locale: text shown, fallback, crash |
| L-02 | Launcher `TextLanguage=en|kr|jp|cn` writing `GameOption.xml`; English fallback table for ids a locale lacks | L-01 passes | switch, see Korean names, switch back |

### Phase E: validation and release

- Rebuild `maple-data.db` from the pack's `Xml.m2d` and `Server.m2d`; all Global table checksums equal the 1.1.9
  build except the tables listed in `data-build.json` as extended (shop table, feature table).
- Data sweep (R12-04) on the unified DB: zero new errors against the 1.1.9 baseline for Global ids; errors for added
  ids triaged into the registry.
- Play matrix on a Steam client and on a community-downloader client: apply, play the later Epic Quest start to finish,
  spawn 20 registry items (icon, model, name), remove the pack, verify archives byte-identical, Steam "verify
  integrity" then Play again.
- Save compatibility: a 1.1.9 save opens with and without the pack; items from the pack in a save opened without the
  pack show as unknown but do not crash the server (test in `SaveSafetyTest`).
- Release: Setup and cumulative update zip as today plus `content-pack-1.2.0.zip`, all three in the signed
  `release.json`; `GameData/data-build.json` names the pack; release notes list the registry totals and the unsupported
  list; `COMPATIBILITY` section in the README names the exact Global build hashes the pack applies to.

## 4. Order of work

1. U-05, U-15, U-08 visual check (parallel; U-08 needs the maintainer).
2. R12-10 tolerant ingest, then U-14, then U-12 with the registry U-16.
3. P-01 pack build once A's inputs are frozen; P-02 with tests; P-03, P-04, P-06; P-05.
4. U-10 translation review and U-11 run alongside B (data only).
5. Phase C tasks in the gaps; R12-08 and R12-11 first.
6. L-01 experiment when a pack exists; L-02 only on a pass.
7. Phase E, then tag `v1.2.0`.

Critical path: U-14 -> U-12 -> P-01 -> P-02 -> P-03 -> P-04 -> E. Everything else can slip to 1.2.1 without blocking.

## 5. Risks

| # | Risk | Mitigation |
|---|---|---|
| 1 | The client crashed once at start with the overlay archive (`0xC000000D`, U-02); cause known for one case (missing required attribute, fixed in U-04d) but not proven for all | P-01 runs `KmsSplit --validate` and the required-attribute fill on every record; the Play matrix includes 10 cold starts |
| 2 | Appended entries are not name-sorted in the archive (U-13 note); lookup of appended entries was verified for XML, icons and models on the test instance but not for every archive type | P-01 refuses archives other than the seven listed; registry rows record "rendered in game" per asset type |
| 3 | Steam updates the Global build (unlikely after closure, but a depot change would alter the prefix) | pack names the exact Global hashes; mismatch refuses cleanly; nothing is modified |
| 4 | Shop table extension changes the Global shop list (U-06 side effect) | P-04 lock; U-05 report lists every Global table row that the merged ingest changes |
| 5 | Translation quality (glossary slips) | review pass before freeze; source column kept; Korean original always shipped |
| 6 | Pack size (about 430 MB) on slow connections | resumable download, applied from cache, one download per pack version; tails only change when content changes |
| 7 | Players with a write-protected game folder | elevated retry exists for the patch; same path for the pack; clear message otherwise |
| 8 | Added content in Legacy mode contradicts "original experience" | Open decision C-1; registry marks sources; `UnifiedContent=false` restores the pure Global game |
| 9 | Dungeons among the KMS-only quests need scripts the Global client never had | listed as unsupported in the registry; the quest line is cut or routed around them, documented per quest |

## 6. Open decisions

- C-1 Legacy and added content (above). Default: keep, mark, allow removal.
- C-2 Pack as a separate asset (default) versus inside the update zip (one 430 MB update for everyone, including
  players who turn the content off).
- C-3 KMS v61 as the base for KMS-only NPC and quest records that exist in both v61 and EoS (per-id file as is, no
  converter) versus EoS through the converter for all. Default: EoS through the converter, v61 only for maps, scripts
  and the dropped tables, so one record never has two sources.
- C-4 Whether the 22 items, 5 NPCs, 16 quests and 30 skills that only old KMS builds had (gone from both EoS and
  Global) are worth restoring. Default: no; listed in the registry as "removed by Nexon".
