# Unified edition: Korean and Japanese content in the Global game

Goal: one offline game that holds the Global (NA) edition plus the content that only the Korean (KMS) and Japanese
(JMS) editions received. This is vision item 2 ("Korean edition") widened to include Japan.

This file records what is known about the three clients, the gap between them, and the plan. Facts are marked as
measured; anything not measured is marked as inference.

## 1. Source clients

| Label | Client build | Data layout | Languages in `string/` |
|---|---|---|---|
| GMS | `MapleStory2.exe` 20.8.1.0316 (NA final, 2020) | per-id files (`item/`, `npc/`, `skill/`, `quest/`) | de, en, pt |
| JMS | `MapleStory2.exe` 10.7.1.0313 (JMS final) | same as GMS | jp |
| KMS | `MapleStory2.exe` 1.00330.0220.1114 (2025 build, NGM launcher) | consolidated (`itemdata/`, `npcdata/`, `skilldata/`, `questdata/`, `anikeyinfo/`, `additional/`) | cn, de, en, jp, kr, pt |
| CMS | `MapleStory2.exe` 1.912.08021.0 (China client, "final" build; archive dated 11.03.2022, files dated 2022-11-01) | consolidated, same as KMS | cn |
| TW | MapleStars2 (Taiwan edition, 2024), `x64/MapleStars2.exe` plus `Launcher.exe`; archive dated 2024-04-27, client logs from 2024-04-23. The exe has no version resource (measured: empty), so no version string is recorded | consolidated, same as KMS | cn, de, en, jp, kr, kr_og, pt |

The TW client is MapleStars2, a 2024 build of the game (the Taiwan edition is inference from the folder and archive names). Its data is KMS data with a small delta
(section 2.1); it is not a separate content line (measured). Its archive root also holds a few dev leftovers
(`.gitignore`, `dev.zip`, `packer_log.txt`); they are untrusted and were not opened.

The GMS, KMS and TW `Xml.m2d` archives open with `Maple2.File.Parser` (`M2dReader`) without error (measured).
`tools/ClientDiff` lists and hashes every entry (`ClientDiff <outDir> GMS=<Xml.m2d> KMS=<Xml.m2d> ...`) and extracts
paths (`ClientDiff --extract <Xml.m2d> <outDir> <prefix>...`).

## 2. Measured gap (Xml.m2d)

Entries: GMS 115,322; JMS 114,850; KMS 46,249 (fewer files because KMS packs many ids per file).

GMS vs JMS: the archives are nearly identical. JMS-only: 11 items, 17 quests (mostly 8003026x-8003027x), 4 skills,
2 achievements, 5 triggers, and the `table/jp` and `string/jp` locale folders. GMS-only: 15 items, 42 quests,
13 maps, `table/na`, `string/de|en|pt`.

GMS vs KMS, by id (KMS ids read from the consolidated files):

| Kind | KMS | GMS | KMS-only | GMS-only |
|---|---|---|---|---|
| Items | 37,337 | 37,065 | 4,198 | 3,926 |
| NPCs | 10,795 | 11,343 | 154 | 702 |
| Quests | 6,961 | 7,229 | 336 | 604 |

KMS-only items are mostly cosmetics and gear (id prefixes 113, 117, 116, 122, 203, 118, 112). KMS-only quests
include a later Epic Quest run (10003449-10003474) and mission/event quests (3000xxxx, 8900xxxx).

Text: KMS ships `string/en` with 37,046 item names (GMS has 33,868), but few of them cover KMS-only content: of the
4,040 KMS-only items, 720 have an English name, 3,273 only a Korean name and 47 none. None of the 152 KMS-only NPCs
has an English name. Where both exist, the English name matches the model file and the Korean one sometimes does not.
The later Epic Quest text (`questdescription_final.xml`) exists in Korean only.

Maps: KMS-only quests sampled so far use maps that GMS also has (e.g. 02000487). KMS keeps an empty `map/` folder
("kept to reference old map history"); its map metadata moved elsewhere (not yet located).

### 2.1 TW (MapleStars2) vs GMS and KMS (measured)

Entries: TW 46,421 (KMS 46,249). Same ClientDiff run, three labels: 25,514 entries identical in all three, 16,498 differ,
and TW has 174 entries KMS lacks (161 under `string/`, 5 `anikeyinfo`, 4 `additionaleffect`, 1 quest, 3 root dev files).
KMS has 3 entries TW lacks (all `achieve/`).

By id (read from the consolidated files, same method as above):

| Kind | TW | KMS | GMS | TW-only vs KMS | KMS-only vs TW | TW-only vs GMS | GMS-only vs TW |
|---|---|---|---|---|---|---|---|
| Items | 37,379 | 37,337 | 37,065 | 48 | 6 | 4,239 | 3,925 |
| NPCs | 10,795 | 10,795 | 11,343 | 0 | 0 | 152 | 700 |
| Quests | 6,904 | 6,961 | 7,229 | 1 | 58 | 293 | 618 |
| Skills | 9,433 | 9,433 | 9,915 | 0 | 0 | 267 | 749 |

(KMS vs GMS skills, same method: 267 KMS-only, 749 GMS-only.)

TW adds nothing over KMS for NPCs and skills. Its 48 extra items are 90000027 and 90090000-90090046 (a test-looking
id block); its one extra quest is 70000100. It lacks 6 KMS items (20000284, 20000285, 20000325, 30001534-30001536) and
58 KMS quests (ids 70000010 to 89000670). TW is therefore not a source of new content beyond KMS.

Text: `string/cn` ships 36,041 item names in TW and 35,993 in KMS (of 37,337 items, so 1,341 TW items and 1,347 KMS items
have no Chinese name). Of the 4,198 KMS-only items, 2,956 have a Chinese name in KMS's own `string/cn`; the 48 TW-only
items all have one too. GMS has no `string/cn`. The sampled `cn` entries are Simplified Chinese, and neither client has a
Traditional Chinese (`tw`) folder, so the Taiwan edition carries no Traditional Chinese text of its own (sample only,
not every file read). TW also has `string/kr_og` (144 files, 33,240 item names).

### 2.2 CMS (China) vs GMS, KMS and TW (measured)

Entries: CMS 45,273 (KMS 46,249, TW 46,421); `Xml.m2d` is 39 MB (TW: 167 MB); unpacked it holds 237 MB
(TW: 569 MB). Four-way ClientDiff run: only 33 entries exist in CMS alone (32 `table/cn/*.xml`
files such as `seasondata_*`, `shop_maid`, `ugcdesign`, and `camera/map/cn/02000064.xml`); every other CMS entry is also
in KMS and TW. The `table/cn` files are China-specific server-style tables; they were not opened beyond their names.

By id (same method as section 2.1):

| Kind | CMS | CMS-only vs KMS | KMS-only vs CMS | CMS-only vs GMS | GMS-only vs CMS |
|---|---|---|---|---|---|
| Items | 36,015 | 1 | 1,323 | 2,958 | 4,008 |
| NPCs | 10,795 | 0 | 0 | 152 | 700 |
| Quests | 6,898 | 1 | 64 | 272 | 603 |
| Skills | 9,408 | 0 | 25 | 242 | 749 |

CMS is an older, smaller slice of KMS: every CMS id of the four kinds is in GMS or KMS. Its single extra item
(15500184) and quest (80020000) are missing from KMS but exist in GMS (item 15500184 also has a name in KMS's
`string/cn`). CMS adds nothing for the merge beyond KMS.

Chinese text: CMS ships only `string/cn` (153 files), no `en`, `kr`, `jp`, `de` or `pt`. It has 33,658 item names,
7,828 NPC names and 1,715 skill names in `cn`. Every CMS Chinese item name also exists in KMS's `string/cn`, and the
text is identical except one entry (an HTML-escaped apostrophe in an English-looking name). So CMS does not ship `cn`
item names for any id that KMS lacks; KMS (35,993 names) and TW (36,041) cover more. The 4,198 KMS-only items already
have Chinese names for 2,956 of them in KMS itself (section 2.1), so Chinese coverage for the merge can come from KMS.
Name-level equality was checked for items only; NPC, skill and quest text was not compared.

## 3. Third-party reference bundle

A community server's client bundle (dated 2026-03-31) was also inspected. Its data is the GMS layout plus
`string/kr|cn|jp`, 24 extra items and about 8,000 modified item and 2,000 modified NPC files. Its origin and the
reason for those edits are unknown, so it is a reference only and is never used as a source of game data.

## 4. Architecture decision: NA client, NA protocol, additive data

The NA client exe (20.8.1.0316) stays the only supported client. The client patch hooks 11 fixed addresses of that
exe, and the server handshake accepts only protocol version 12 with locale NA
(`ResponseVersionHandler.cs`). Porting to the KMS or JMS exe would mean redoing both, so content moves to the NA
client instead.

Content is merged per record: NA records stay as they are, and KMS/JMS records are added only for ids NA does not
have.

Delivery (tested in game, 2026-10-17): the added records go into a repacked `Xml.m2d` built with `tools/M2dPack`
(NA entries copied byte for byte, new entries appended). The server database is built by the normal ingest from the
same repacked archive. The client mod loader is not needed for added XML. The ingest locale stays "NA". Switching it to "KR" would change feature gates for every existing record
(shops, Meret Market, Global* overrides).

## 5. Obstacles found

1. KMS stores many records per file; the parser and the NA client expect one file per id. A converter must split
   KMS files into NA-layout per-id XML.
2. `Maple2.File.Parser` is a closed NuGet package (locale filter, string paths, NA server tables are inside it).
3. The server database is built from `Xml.m2d` plus `Server.m2d` (server-only tables, NA era, published separately
   by the community). KMS-only monsters, maps and dungeons have no server tables (drops, spawns, AI).
4. The ingest parses enums strictly (`Enum.Parse<ItemFunction>`, `Enum.Parse<EquipSlot>`); unknown KMS values
   would stop it.
5. The client mod loader replaces whole XML files read through one function. It can serve an XML path that is not
   in `Xml.m2d`, but it cannot serve textures, models, icons, sound or Lua. Whether the NA client requests a
   per-id file for an id it does not know is untested.
6. KMS-only art sits in the KMS resource archives (models and textures 4-9 % larger than NA). Options: a new
   client-patch hook, repacking archives, or loose files if the client honours `PackClient.ini` (untested).
7. Big shared tables (`string/en/itemname.xml`, `feature.xml`) must be shipped complete because mods replace whole
   files.
8. Korean-only text needs translation; voice lines exist only in Korean voice banks.
9. Nothing guards against an id meaning different things in NA and KMS; shared-id content has not been compared.
10. Saves must stay valid: changes must be additive, and a schema change rebuilds the shipped data database.

## 6. Preservation archive scan (2026-10-18, measured)

Every client build in the maintainer's preservation archive was opened and its `Xml.m2d` hashed and diffed with
`tools/ClientDiff` (15 labels). Id counts come from the extracted `item*/`, `npc*/`, `quest*/`, `skill*/` folders
(regex over `id="..."`, per-id file names where the layout is per-id). Duplicates found by SHA-256: the archive's
GMS2 "clean" client equals the GMS client used above; its KMS end-of-service client equals the KMS client above; the
Doll Mansion capture client equals KMS v72; the v81 debug client's patched `Xml.m2d` equals the "Default English
Snapshot" (community edit, not Nexon data; its `.orig` is the real v81); the JMS archive is the same split installer
the JMS client above was unpacked from. Not opened: the KMS v81 and JMS installers (Nexon's own split format, not a
zip; the same builds exist unpacked), and the Nexon installer exes.

### 6.1 KMS builds over time

| Build | exe version | Date | Entries | Layout | Items | NPCs | Quests | Skills | kr names |
|---|---|---|---|---|---|---|---|---|---|
| KMS v61 | (no version resource) | 2020-06 | 113,761 | **per-id like GMS** (`item/` 37,118, `npc/`, `quest/`, `skill/`, `map/` 1,718, `script/` 3,369); plus `itemdata/` 221 | 37,212 | 10,824 | 7,266 | 9,932 | 37,075 |
| KMS v69 (two archives, same exe; data differs in 2 entries) | 1.276.0127.1200 | 2021-01/02 | 60,016 | mixed: `npc/`, `quest/`, `map/` 1,722, `script/` 3,373 per-id; `itemdata/`, `skilldata/` consolidated | 37,492 | 10,838 | 7,344 | 9,546 | 34,933 |
| KMS v72 | 1.286.0721.1619 | 2021-07 | 56,694 | consolidated; `map/` 1, `script/` 2; `npc/` still per-id | 35,340 | 10,874 | 6,783 | 9,285 | 33,008 |
| KMS v76 | 1.295.1124.1310 | 2021-11 | 45,938 | consolidated | 35,561 | 10,739 | 6,820 | 9,315 | 33,193 |
| KMS v77 | 1.297.1209.1508 | 2021-12 | 45,940 | consolidated | 35,680 | 10,739 | 6,820 | 9,312 | 33,318 |
| KMS v81 (`.orig`) | 1.305.0413.1728 | 2022-04 | 46,114 | consolidated | 35,930 | 10,793 | 6,856 | 9,391 | 33,554 |
| KMS v82 | 1.308.0511.1918 | 2022-05 | 46,158 | consolidated | 35,949 | 10,795 | 6,898 | 9,406 | 33,575 |
| KMS 2024-05-14 | 1.00329.0514.1757 | 2024-05 | 46,249 | consolidated | 37,337 | 10,795 | 6,962 | 9,433 | 34,976 |
| KMS EoS | 1.00330.0220.1114 | 2025-02 | 46,249 | consolidated | 37,337 | 10,795 | 6,962 | 9,433 | 34,976 |

Changes between consecutive builds (ids added / removed): v61 -> v69 items +1,051/-771, quests +80/-2, skills
+19/-405; v69 -> v72 items +817/-2,969, NPCs +36, quests +52/-613, skills +39/-300; v72 -> v76 items +421/-200,
NPCs +34/-169, skills +95/-65; v76 -> v77 items +119; v77 -> v81 items +250, NPCs +54, quests +36, skills +79;
v81 -> v82 items +22/-3, quests +42, skills +15; v82 -> 2024 items +1,389, quests +64, skills +27; 2024 -> EoS none.
The KMS 2024-05 and EoS builds hold the same ids; only the exe and a few files differ.

What the older builds add over EoS plus GMS: across all KMS builds 41,068 distinct item ids, 10,974 NPCs, 7,580
quests, 10,212 skills. Of the ids EoS no longer has (3,731 items, 179 NPCs, 618 quests, 779 skills) almost all exist
in GMS: not in GMS either are 22 items, 5 NPCs, 16 quests (all 8003xxxx, last seen in v69) and 30 skills. So the
later builds removed old content rather than KMS-only content; the unified data set loses nothing by taking KMS-only
ids from EoS.

The real value of v61 and v69 is layout and the folders later builds dropped:

| Folder | v61 | v69 | EoS | v61 entries not in GMS |
|---|---|---|---|---|
| `map/` (map metadata) | 1,718 | 1,722 | 1 (empty folder) | 32 (38 in v69) |
| `script/` (NPC and quest scripts) | 3,369 | 3,373 | 2 | 4 (8 in v69) |
| `npc/` per-id | 10,824 | 10,838 | 0 | 13 (34 in v69) |
| `quest/` per-id | 7,266 | 7,344 | 6,780 | 38 (118 in v69) |
| `table/` | 316 | 306 | 304 | 49; 33 tables EoS dropped, among them `npcspawndata.xml`, `individualitemdrop*.xml` (8 files), `itemoptionpick.xml`, `setitemoption.xml`, `potentialability.xml` |

This answers the open question in section 2 (KMS map metadata "not yet located"): it was in the client until v69 and
removed in v72. For the KMS-only maps and NPC scripts that exist in v61/v69 the NA-layout file is available as is, so
the converter (U-04) is not needed for those records; for ids added after v69 it still is.

### 6.2 Other regional data

| Source | Measured |
|---|---|
| CMS final (2022-11) | 45,273 entries, exe 1.912.08021.0; ids: 36,015 items, 10,795 NPCs, 6,898 quests, 9,408 skills; only `string/cn`; see 2.2 |
| TW MapleStars2 (2024-04) | 46,421 entries; see 2.1 |
| TW "dev leak" (2024-04-25), loose XML | 48,250 files = the TW client data unpacked plus 1,682 server AI scripts (`ai/bossdungeon` 1,100, `ai/guildraid` 56, ...) and 117 `table/server` files. All 1,799 of those paths also exist in the community `Server.m2d` v1.3.2 (1,814 entries); 1,781 are byte-identical and 18 differ (9 server tables: `constants`, `fieldrestraintable`, `globaldropitembox_final`, `globaldropitemset_final`, `individualitemdrop_final`, `itemoptionrandom`, `itemoptionvariation`, `oxquiz`, `shop_game`; 9 `ai/bossdungeon` scripts). The community archive therefore derives from this leak line; the leak adds no server file the project does not already have. Reference only (see PROJECT_STATUS legal note) |
| MapleStars EN strings (2024-05) | 207 `string/en` files, the TW client's English text |
| JMS final (2020-05) | per-id layout; 37,061 items, 11,343 NPCs, 7,204 quests, 9,918 skills; `string/jp` 31,196 item names |

### 6.3 GMS2 history (asset exports, file lists with CRC from the archive headers, nothing extracted)

| Export | Files | `Xml/item` | `Xml/npc` | `Xml/quest` | `Xml/skill` | `Xml/map` | string locales |
|---|---|---|---|---|---|---|---|
| CBT2 2018-07-18 | 212,133 | 22,885 | 7,895 | 5,135 | 7,890 | 1,413 | de, en |
| prelaunch 2018-08-01 | 131,768 | resources only (no `Xml/`) | | | | | |
| MKR 2018-08-29 | 218,178 | 23,910 | 8,146 | 5,311 | 8,110 | 1,430 | de, en |
| MKR 2018-09-21 | 218,228 | 23,912 | 8,146 | 5,311 | 8,110 | 1,430 | de, en |
| Headstart 2018-10-01 | 222,306 | 25,458 | 8,282 | 5,410 | 8,231 | 1,385 | de, en, pt |
| Launch 2018-10-10 | 223,667 | 25,625 | 8,395 | 5,443 | 8,249 | 1,416 | de, en, pt |
| Sunset 2020-03-27 | 274,933 | 37,065 | 11,343 | 7,229 | 9,915 | 1,686 | de, en, pt |

Launch -> sunset: 52,063 files added, 797 removed, 31,188 changed (11,441 items added, 5,166 item files changed,
1,822 quests added, 2,484 skill files changed). The sunset export's `Xml/` holds exactly the 115,322 entries of the
GMS `Xml.m2d`. These exports are the evidence base for "what the original did at launch versus at closure"
(Legacy Mode questions); the sunset export also carries the extracted `Resource/` tree (159,608 files).

### 6.4 Behaviour evidence (not data)

Packet captures: 12 folders, 58 `.msb` logs and 14 videos. Class skills (10 classes, rank 1 and rank 2 trees),
enchanting (Ophelia and Peachy 1 to 10), black market and UGC market, housing (4), life skills (mining, foraging,
farming/ranching, crafting, leisure), quest turn-in, party scenarios (7 videos, 2021-02-04), Doll Mansion PvP
(2021-08-08), Cathy Mart cashier point of view (KMS2, 2025-05-03), legion popup, world boss notification, premium
club, friends and mailbox, shops and furnishing with two `PacketDefinitions.xml`, plus a 232-file dump of the orion2
packet-log channel (2021-01). Voice export: 19,880 English voice lines as mp3 (`en_<npc>_<n>`), the bank-to-line map
the voice tasks (R12-19/20) need. MS2Codex archive: 32,375 pages of the 2019-11/12 item, NPC, dungeon and set
database; official-site media dump (4,997 files); two Discord dumps (official server 2020-05, MS2 Hub 2024-09 with
client-dev and dev-leak channels); the KMS debug IDB (a 1.4 GB `MapleStory2.exe.i64`).
