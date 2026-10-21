# U-15: Old-KMS extract (map, script, dropped tables)

Task U-15 of [PLAN_1.2.0_UNIFIED_CONTENT.md](PLAN_1.2.0_UNIFIED_CONTENT.md), phase A. Measured 2026-10-18 from the
`Xml.m2d` of KMS v61 (2020-06) and KMS v69 (2021-01), the Global (GMS) client and the KMS end-of-service (EoS) client.
Background: [UNIFICATION.md](UNIFICATION.md) section 6.1. The extracted files are untrusted data and stay in the
maintainer's `_ingest` area (`_ingest/u15/<build>/`); nothing from them is committed.

## 1. Result

- Lists computed from the `ClientDiff` listings (path, size, SHA-256): **38 `map/` files, 8 `script/` files and 53
  `table/` files** are in v61 or v69 and not in GMS by path; **33 `table/` files** are in v61 and not in EoS. 16 tables
  are in both sets, so there are 70 distinct table files. Where both builds have a file, v69 is used.
- All 131 files of the two lists (98 from v69, 33 from v61) were extracted with `ClientDiff --extract`. Each SHA-256
  equals its listing entry. v69a and v69b differ in none of the extracted files.
- **Maps: 38 of 38 accepted.** Well-formed; element and attribute set equal to the GMS maps; no attribute value outside
  the GMS value set (attributes with up to 40 distinct values); all 38 deserialize with the parser type and sanitizer
  the ingest uses for maps, as do 1,686 of 1,686 GMS maps. Open: the maps' `xblock` model files live in Resource
  archives that were not extracted for v61/v69 (section 4.1).
- **Scripts: 8 of 8 accepted.** Same structure as the GMS scripts. All eight belong to NPCs that GMS lacks and EoS has
  (section 4.2).
- **Tables: 0 of 70 fail because of KMS data.** Five files fail a strict parser type and the GMS twin fails the same
  way (section 4.3). Only `npcspawndata.xml` is worth taking as it is. 23 tables equal EoS, 11 equal GMS and 2 are
  older than EoS, so they add nothing; 33 differ from a GMS twin so that a whole-file replace is unsafe.
- `KmsSplit --validate` does not apply here: it checks only the `item`, `npc`, `quest` and `skill` folders. Run on the
  extract it prints nothing and exits 0. The ingest's own parser types were used instead (sections 4.1 and 4.3).

## 2. How the lists were made

1. Read the `GMS`, `KMS-v61`, `KMS-v69a`, `KMS-v69b` and `KMS-EoS` listings of `ClientDiff-all`.
2. `map/`, `script/`, `table/` paths in v61 or v69a and not in GMS: 38 + 8 + 53. The 32 maps and 4 scripts that v61 has
   (section 6.1) are all inside the v69 set. v61 alone has one table that v69 lacks (`table/kr/guildchampionship.xml`);
   v69 has four that v61 lacks (`housing`, `individualitemdrop_final`, `loginbgbgm`, `musicscore`).
3. `table/` paths in v61 and not in EoS: 33. 17 of them exist in GMS under the same path, 16 do not.
4. Section 6.1 counts 49 tables for v61 alone; for v61 or v69 the count is 53.

The regional tables sit under `table/na/<name>` in GMS and under `table/kr/<name>` in KMS. So 46 of the 53 "absent
from GMS" paths have a GMS twin under another folder. Seven have no twin of any kind: `housing`,
`individualitemdrop_final`, `loginbgbgm`, `musicscore`, `npcspawndata`, `kr/mapwebfinder`,
`kr/seasonreward_maplesurvival`.

## 3. Extraction and integrity

| Build | Files | What |
|---|---|---|
| KMS-v69a | 98 | 38 `map/`, 8 `script/npc/`, 52 `table/` |
| KMS-v61 | 33 | the 33 tables EoS dropped (v61 copy; a table in both lists exists in both folders) |

Checks on all 131: the XML parses, and the SHA-256 equals the listing. For comparison the GMS maps, scripts and tables
and the EoS tables and map names were extracted beside them.

## 4. Checks

### 4.1 Maps (38)

- Structure: the GMS maps use 139 distinct element and element@attribute paths. The 38 KMS maps add **none**, and every
  path found in at least 95 % of the GMS maps is present in each of them.
- Values: no attribute with up to 40 GMS values carries a value GMS lacks.
- Features: the maps use six `feature` names. Five are not in the GMS `table/feature.xml`: `DungeonOpen70AH5`,
  `DungeonOpen70CH6`, `Mastery07`, `EventMeowParadise`, `DungeonOS01`. The sixth, `Timaion01`, is in GMS with level
  100 for every region, so it is switched off there (the same in EoS). EoS `feature.xml` has four of the five, at KR/CN
  level 64 and 65 for the two dungeons and 99 (off) for `Mastery07` and `EventMeowParadise`; `DungeonOS01` is in no
  `feature.xml` (EoS uses it only in `dungeonmission.xml`). These names need the U-14 feature gate before the maps can
  be reached.
- Links: every `revivalreturnid` and `enterreturnid` points to the map itself or is 0.
- Ingest type: `Sanitizer.SanitizeMap` followed by `MapDataRoot` of `Maple2.File.Parser` (rebuilt with reflection, not
  the `MapParser` call itself): 38 of 38; the same call passes 1,686 of 1,686 GMS maps.
- Not checked: each map names an `xblock` model (for example `02000526_BF`) in the Resource archives. They were not
  extracted for v61/v69, so it is unknown whether a pack that adds these maps also needs model data. No KMS-only id is
  used by a different GMS map; the Timaion ids fill gaps in used GMS id ranges.

### 4.2 Scripts (8)

All eight are `script/npc/` files for the NPC ids 11004838 to 11004841, 11004844, 11004845, 11500001 and 11500002.
Their elements (`select`, `script`, `monologue`) and attributes are the GMS set, and `feature` and `locale` carry GMS
values. None of the ids has an NPC file in GMS; all have a record in the EoS `npcdata`, with the Korean names 틴차이,
이브, 혁이, 케이틀린, 살레 (two ids), 루비 and 바비. The `$script:..$` text lives in the v69 Korean `scriptnpcnam` string
file; there is no English text.

### 4.3 Tables (70 distinct)

Method: each file is read with the ingest's parser type for that table (`Maple2.File.Parser`, after
`Sanitizer.RemoveEmpty`), once for the KMS file and once for its GMS twin. A KMS file counts as failing only if the GMS
twin passes. **No KMS file fails alone.** These fail with their twin or have no twin:

| File | Failing field | GMS twin |
|---|---|---|
| `individualitemdrop_charge.xml` | `isApplySmartGenderDrop="TRUE"` is not a Boolean (line 522) | fails the same |
| `individualitemdrop_event.xml` | same field, line 1589 | fails the same |
| `individualitemdrop_newgacha.xml` | same field, value `True`, line 3 | fails the same |
| `kr/individualitemdrop_monster.xml` | `dropGroup` over 255 does not fit an unsigned byte (value 991, line 3033) | fails the same (653) |
| `individualitemdrop_final.xml` | server-table type only: `isApplySmartGenderDrop="True"`, line 21941 (the client-table type reads it) | none |

The Boolean failures are most likely a missing `SanitizeBool` step in this check, because the GMS data goes through the
real parser; the `dropGroup` range is a parser limit shared with GMS (the ingest does not read that file).
25 of the 70 files have no parser type in `Maple2.File.Parser`; they were checked for well-formedness only.

Which tables the ingest reads (`Maple2.File.Ingest` `TableMapper` and `ServerTableMapper`, file names embedded in the
parser library). "Read if LANGUAGE=kr" means the `kr/` copy is used only when the ingest runs with the Korean language;
the GMS run reads the `na/` twin (inferred from where GMS keeps them).

- **Read:** `individualitemdrop` (plain, `_charge`, `_event`, `_eventnpc`, `_newgacha`, `_pet`, `_quest_mob`,
  `_quest_obj`), `itemmergeoptionmaterial` (through `ParseItemMergeOption`; base or material file not confirmed),
  `itemoptionpick`, `setitemoption`, `maplesurvivalskininfo`.
- **Read if LANGUAGE=kr:** `autoactionpricepackage`, `banner`, `chapterbook`, `dungeonconfig`, `dungeonroom`,
  `fieldmission`, `individualitemdrop_gacha`, `individualitemdrop_gearbox`, `itemextraction`, `itemgemstoneupgrade`,
  `itemlapenshardupgrade`, `meratmarketcategory`, `newworldmap`, nine `seasondata_*`, `shop_maid`, `shop_ugcall`,
  `smartpush`, `ugcdesign`.
- **Type exists, never called:** `guildexp` (call commented out), `kr/survivallevel`, `kr/survivallevelreward`,
  `individualitemdrop_final`.
- **Ignored:** the rest, among them `npcspawndata`, `potentialability`, `jobgroup`, `housing`, `musicscore`,
  `loginbgbgm`, the `*webfinder*` files, `rewardpreview_*`, `survivalpass*`, `userconditionevent`,
  `itemgemproperty_fishing`.

Fields a KMS file has that its GMS (or EoS) twin never has: `kr/dungeonroom` `extraRewardTicketTableID`;
`kr/itemlapenshardupgrade` `IngredientItemID2/3` and `IngredientCount2/3`; `kr/newworldmap` `level_min`, `level_max`;
`kr/rewardpreview_boss` `reqJobs`; `kr/rewardpreview_dungeon` `itemTag`, `locale`; `kr/userconditionevent`
`attachment@*` and `condition@*` (the GMS twin has no rows); `setitemoption` `sgi_elite_rate_base` (it lacks
`sgi_rate_base`, `sgi_target`, `sgi_boss_target`); `itemmergeoptionmaterial` `locale`; `individualitemdrop_final`
`PackageUIShowGrade` (it lacks `uiItemRank`, `itemLifeType`, `itemLifeValue`, `tradableCountDeduction` that EoS has).
All of these load with the existing parser types.

Row comparison keys on the first attribute of each row, so files with repeated keys give a guide, not an exact diff.
"Shared-differ" includes rows that differ only in `feature` or `locale`.

## 5. KMS-only maps (38)

Names come from the v69 Korean `mapname`, else the EoS Korean file, else the v69 Chinese file ("cn"). The v69 English
file holds only placeholders (`MAPNAME_<id>_NAME:[F]<feature>`) for these ids and the GMS English file has no entry for
any of them, so none has an English name. No KMS-only id exists as a map in GMS. The three dungeon ids have a
same-named relative in GMS (the "환영" prefix reads as a phantom or illusion variant); that is a name match only.

| Map id | feature | mapType | Name (kr, else cn) | GMS map |
|---|---|---|---|---|
| 02000526 | DungeonOpen70AH5 | 5 | 환영의 불멸의 신전 | none by id; nearest by name: Temple of Immortals (02000037, 02000419) |
| 02000527 | DungeonOpen70CH6 | 5 | 환영의 그림자 제단 어려움 | none by id; nearest by name: Shadow Altar (02000213, 02000326, 02000356, 02000500) |
| 02000528 | DungeonOpen70CH6 | 5 | 환영의 그림자 제단 일반 | none by id; nearest by name: Shadow Altar (same four) |
| 02000556 | Mastery07 | 2 | (unnamed placeholder) | none |
| 02000557 | EventMeowParadise | 1 | 彩虹钓鱼场 (cn) | none |
| 02020002 | Timaion01 | 4 | 暴风战场 (cn) | none |
| 02020005 | Timaion01 | 4 | 遗忘城镇北部 (cn) | none |
| 02020007 | Timaion01 | 4 | 时间丛林 (cn) | none |
| 02020011 | Timaion01 | 4 | 眠歌森林 (cn) | none |
| 02020012 | Timaion01 | 1 | 提麦欧斯 (cn) | none |
| 02200001 | DungeonOS01 | 5 | 푸른 화염의 혼돈 | none |
| 03000281 | Timaion01 | 11 | 动荡废墟 (cn) | none |
| 52020002 | Timaion01 | 4 | 罗丁的营帐 (cn) | none |
| 52020003 | Timaion01 | 4 | 坍塌的工作室 (cn) | none |
| 52020004 | Timaion01 | 4 | 地下避难处 (cn) | none |
| 52020005 | Timaion01 | 4 | 废弃卡车 (cn) | none |
| 52020006 | Timaion01 | 4 | 城墙下方通道 (cn) | none |
| 52020008 | Timaion01 | 4 | 螺旋屋1层 (cn) | none |
| 52020009 | Timaion01 | 4 | 无名房屋 (cn) | none |
| 52020010 | Timaion01 | 4 | 复原的时间之屋 (cn) | none |
| 52020011 | Timaion01 | 4 | 梦幻之屋 (cn) | none |
| 52020012 | Timaion01 | 4 | 树中空间 (cn) | none |
| 52020014 | Timaion01 | 4 | 地下通道 (cn) | none |
| 52020015 | Timaion01 | 4 | 梦幻通道 (cn) | none |
| 52020016 | Timaion01 | 4 | 梦幻剧场 (cn) | none |
| 52020017 | Timaion01 | 4 | 永恒的彷徨 (cn) | none |
| 52020018 | Timaion01 | 4 | 噩梦之特洛伊旅馆 (cn) | none |
| 52020019 | Timaion01 | 4 | 忘却大道外围 (cn) | none |
| 52020022 | Timaion01 | 4 | 果园管理室 (cn) | none |
| 52020023 | Timaion01 | 4 | 机械控制室 (cn) | none |
| 52020024 | Timaion01 | 4 | 地下铁匠铺 (cn) | none |
| 52020025 | Timaion01 | 4 | 地下逃生路 (cn) | none |
| 52020026 | Timaion01 | 4 | 螺旋屋中心区域 (cn) | none |
| 52020027 | Timaion01 | 4 | 螺旋屋顶端 (cn) | none |
| 52020028 | Timaion01 | 4 | 卫城 (cn) | none |
| 52020029 | Timaion01 | 4 | 真理之门 (cn) | none |
| 52020030 | Timaion01 | 4 | 苍穹之塔 (cn) | none |
| 52020031 | Timaion01 | 4 | 苍穹祭坛 (cn) | none |


The 38 maps: three dungeon variants (02000526 to 02000528), one map with feature `Mastery07` (02000556, unnamed), one
event fishing map (02000557, "Rainbow fishing ground"), one dungeon with feature `DungeonOS01` (02200001, EoS name
푸른 화염의 혼돈), and 32 maps with feature `Timaion01` (02020xxx, 52020xxx and 03000281). The Timaion names exist in
Chinese only, in part also in Japanese.

## 6. Table files (70)

Build: v61, v69 or both (the v69 copy was analysed). "GMS twin": same path, or the same name under `table/na/` or
`table/default/`. "In EoS": identical, differs or absent. Verdict: ACCEPT = take as it is; redundant = equals the named
edition and adds nothing; REJECT = a whole-file replace would change GMS rows or drop GMS-only rows; merge rows by key
instead (the KMS-only row count is in the row column).

| File | Build | Not in GMS path | GMS twin | In EoS | Ingest | Parser type, KMS file | KMS-only / GMS-only / shared-differ rows | Verdict |
|---|---|---|---|---|---|---|---|---|
| `dungeonrewardcoupon.xml` | v69 | yes | na twin | differs | ignored | none | 0 / 0 / 1 | REJECT (no new rows) |
| `guildexp.xml` | v61 | no | same path | absent | type exists, never called | ok | 0 / 0 / 0 | redundant (equals GMS) |
| `housing.xml` | v69 | yes | none | differs | ignored | none | - | redundant (EoS newer) |
| `individualitemdrop.xml` | v61 | no | same path | absent | read | ok | 23 / 9 / 4 | REJECT whole-file, merge rows |
| `individualitemdrop_charge.xml` | v61 | no | same path | absent | read | fails (see 4.3) | 81 / 1 / 26 | REJECT whole-file, merge rows |
| `individualitemdrop_event.xml` | v61 | no | same path | absent | read | fails (see 4.3) | 43 / 0 / 10 | REJECT whole-file, merge rows |
| `individualitemdrop_eventnpc.xml` | v61 | no | same path | absent | read | ok | 0 / 0 / 0 | redundant (equals GMS) |
| `individualitemdrop_final.xml` | v69 | yes | none | differs | type exists, never called | fails (see 4.3) | - | redundant (EoS newer) |
| `individualitemdrop_newgacha.xml` | v61 | no | same path | absent | read | fails (see 4.3) | 0 / 0 / 12 | REJECT (no new rows) |
| `individualitemdrop_pet.xml` | v61 | no | same path | absent | read | ok | 0 / 0 / 0 | redundant (equals GMS) |
| `individualitemdrop_quest.xml` | v61 | no | same path | absent | ignored | ok | 0 / 0 / 0 | redundant (equals GMS) |
| `individualitemdrop_quest_mob.xml` | v61 | no | same path | absent | read | ok | 19 / 0 / 0 | REJECT whole-file, merge rows |
| `individualitemdrop_quest_obj.xml` | v61 | no | same path | absent | read | ok | 1 / 0 / 0 | REJECT whole-file, merge rows |
| `itemgemproperty_fishing.xml` | v61 | no | same path | absent | ignored | none | 5 / 0 / 0 | REJECT whole-file, merge rows |
| `itemmergeoptionmaterial.xml` | v61 | no | same path | absent | read | ok | 0 / 0 / 48 | REJECT (no new rows) |
| `itemoptionpick.xml` | v61 | no | same path | absent | read | ok | 0 / 0 / 0 | redundant (equals GMS) |
| `jobgroup.xml` | v61 | no | same path | absent | ignored | ok | 0 / 0 / 0 | redundant (equals GMS) |
| `kr/attendgiftdata.xml` | v69 | yes | na twin | identical | ignored | none | 0 / 7 / 3 | redundant (equals EoS) |
| `kr/autoactionpricepackage.xml` | v69 | yes | na twin | identical | read if LANGUAGE=kr | ok | 0 / 0 / 2 | redundant (equals EoS) |
| `kr/banner.xml` | v69 | yes | na twin | identical | read if LANGUAGE=kr | ok | 0 / 10 / 0 | redundant (equals EoS) |
| `kr/banurl.xml` | v69 | yes | na twin | identical | ignored | none | 10 / 0 / 1 | redundant (equals EoS) |
| `kr/chapterbook.xml` | v69 | yes | na twin | identical | read if LANGUAGE=kr | ok | 2 / 0 / 27 | redundant (equals EoS) |
| `kr/dungeonconfig.xml` | v69 | yes | na twin | differs | read if LANGUAGE=kr | ok | 1 / 1 / 1 | REJECT whole-file, merge rows |
| `kr/dungeonroom.xml` | v61+v69 | yes | na twin | absent | read if LANGUAGE=kr | ok | 29 / 32 / 64 | REJECT whole-file, merge rows |
| `kr/fieldmetadata.xml` | v69 | yes | na twin | differs | ignored | none | 967 / 419 / 235 | REJECT whole-file, merge rows |
| `kr/fieldmission.xml` | v69 | yes | na twin | identical | read if LANGUAGE=kr | ok | 0 / 0 / 0 | redundant (equals GMS) |
| `kr/guildchampionship.xml` | v61 | yes | na twin | absent | ignored | none | 6 / 0 / 1 | REJECT whole-file, merge rows |
| `kr/individualitemdrop_gacha.xml` | v61+v69 | yes | na twin | absent | read if LANGUAGE=kr | ok | 213 / 331 / 253 | REJECT whole-file, merge rows |
| `kr/individualitemdrop_gearbox.xml` | v61+v69 | yes | na twin | absent | read if LANGUAGE=kr | ok | 11 / 42 / 0 | REJECT whole-file, merge rows |
| `kr/individualitemdrop_monster.xml` | v61+v69 | yes | na twin | absent | ignored | fails (see 4.3) | 28 / 7 / 72 | REJECT whole-file, merge rows |
| `kr/itemextraction.xml` | v61+v69 | yes | na twin | absent | read if LANGUAGE=kr | ok | 346 / 1627 / 0 | REJECT whole-file, merge rows |
| `kr/itemgemstoneupgrade.xml` | v69 | yes | na twin | identical | read if LANGUAGE=kr | ok | 0 / 0 / 0 | redundant (equals GMS) |
| `kr/itemlapenshardupgrade.xml` | v69 | yes | na twin | identical | read if LANGUAGE=kr | ok | 0 / 0 / 990 | redundant (equals EoS) |
| `kr/itemwebfinder.xml` | v69 | yes | na twin | identical | ignored | none | 7506 / 1796 / 844 | redundant (equals EoS) |
| `kr/maidsalary.xml` | v69 | yes | na twin | identical | ignored | none | 0 / 0 / 0 | redundant (equals GMS) |
| `kr/maplesurvivalopenperiod.xml` | v61+v69 | yes | na twin | absent | ignored | none | 0 / 0 / 1 | REJECT (no new rows) |
| `kr/mapwebfinder.xml` | v69 | yes | none | identical | ignored | none | - | redundant (equals EoS) |
| `kr/meratmarketcategory.xml` | v69 | yes | na twin | identical | read if LANGUAGE=kr | ok | 0 / 1 / 1 | redundant (equals EoS) |
| `kr/monsterwebfinder_mob.xml` | v69 | yes | na twin | identical | ignored | none | 0 / 1809 / 101 | redundant (equals EoS) |
| `kr/monsterwebfinder_npc.xml` | v69 | yes | na twin | identical | ignored | none | 0 / 1582 / 67 | redundant (equals EoS) |
| `kr/multicontrol.xml` | v69 | yes | na twin | identical | ignored | none | 1 / 3 / 0 | redundant (equals EoS) |
| `kr/newworldmap.xml` | v69 | yes | na twin | identical | read if LANGUAGE=kr | ok | 3 / 0 / 5 | redundant (equals EoS) |
| `kr/rewardpreview_boss.xml` | v61+v69 | yes | na twin | absent | ignored | none | 20 / 2 / 0 | REJECT whole-file, merge rows |
| `kr/rewardpreview_dungeon.xml` | v61+v69 | yes | na twin | absent | ignored | none | 47 / 7 / 84 | REJECT whole-file, merge rows |
| `kr/seasondata_arcade.xml` | v69 | yes | na twin | differs | read if LANGUAGE=kr | ok | 79 / 0 / 223 | REJECT whole-file, merge rows |
| `kr/seasondata_bosscolosseum.xml` | v69 | yes | na twin | identical | read if LANGUAGE=kr | ok | 0 / 9 / 0 | redundant (equals EoS) |
| `kr/seasondata_darkstream.xml` | v69 | yes | na twin | differs | read if LANGUAGE=kr | ok | 47 / 0 / 112 | REJECT whole-file, merge rows |
| `kr/seasondata_guildpvp.xml` | v69 | yes | na twin | identical | read if LANGUAGE=kr | ok | 7 / 1 / 0 | redundant (equals EoS) |
| `kr/seasondata_maplesurvival.xml` | v61+v69 | yes | na twin | absent | read if LANGUAGE=kr | ok | 4 / 0 / 1 | REJECT whole-file, merge rows |
| `kr/seasondata_maplesurvival_squad.xml` | v61+v69 | yes | na twin | absent | read if LANGUAGE=kr | ok | 4 / 0 / 1 | REJECT whole-file, merge rows |
| `kr/seasondata_pvp.xml` | v69 | yes | na twin | differs | read if LANGUAGE=kr | ok | 14 / 0 / 54 | REJECT whole-file, merge rows |
| `kr/seasondata_ugcmapcommendation.xml` | v69 | yes | na twin | differs | read if LANGUAGE=kr | ok | 39 / 12 / 39 | REJECT whole-file, merge rows |
| `kr/seasondata_worldchampion.xml` | v69 | yes | na twin | identical | read if LANGUAGE=kr | ok | 11 / 0 / 1 | redundant (equals EoS) |
| `kr/seasonreward_maplesurvival.xml` | v69 | yes | none | identical | ignored | none | - | redundant (equals EoS) |
| `kr/shop_maid.xml` | v69 | yes | na twin | identical | read if LANGUAGE=kr | ok | 0 / 0 / 6 | redundant (equals EoS) |
| `kr/shop_ugcall.xml` | v69 | yes | na twin | differs | read if LANGUAGE=kr | ok | 27 / 0 / 1440 | REJECT whole-file, merge rows |
| `kr/smartpush.xml` | v69 | yes | na twin | differs | read if LANGUAGE=kr | ok | 0 / 0 / 0 | redundant (equals GMS) |
| `kr/survivallevel.xml` | v61+v69 | yes | na twin | absent | type exists, never called | ok | 240 / 0 / 60 | REJECT whole-file, merge rows |
| `kr/survivallevelreward.xml` | v61+v69 | yes | na twin | absent | type exists, never called | ok | 0 / 0 / 300 | REJECT (no new rows) |
| `kr/survivalpassreward.xml` | v61+v69 | yes | na twin | absent | ignored | none | 0 / 2 / 1 | REJECT (no new rows) |
| `kr/survivalpassreward_paid.xml` | v61+v69 | yes | na twin | absent | ignored | none | 0 / 2 / 1 | REJECT (no new rows) |
| `kr/ugcdesign.xml` | v69 | yes | na twin | identical | read if LANGUAGE=kr | ok | 0 / 4 / 125 | redundant (equals EoS) |
| `kr/userconditionevent.xml` | v69 | yes | na twin | identical | ignored | none | 133 / 0 / 0 | redundant (equals EoS) |
| `kr/webguide.xml` | v69 | yes | na twin | identical | ignored | none | 0 / 2 / 0 | redundant (equals EoS) |
| `loginbgbgm.xml` | v69 | yes | none | identical | ignored | none | - | redundant (equals EoS) |
| `maplesurvivalskininfo.xml` | v61 | no | same path | absent | read | ok | 0 / 18 / 0 | REJECT (no new rows) |
| `musicscore.xml` | v69 | yes | none | identical | ignored | none | - | redundant (equals EoS) |
| `npcspawndata.xml` | v61+v69 | yes | none | absent | ignored | none | - | ACCEPT (old builds only) |
| `potentialability.xml` | v61 | no | same path | absent | ignored | none | 0 / 0 / 0 | redundant (equals GMS) |
| `setitemoption.xml` | v61 | no | same path | absent | read | ok | 30 / 197 / 24 | REJECT whole-file, merge rows |


## 7. Recommendation for the content pack (P-01)

Take as they are:

1. All 38 `map/` files and all 8 `script/npc/` files from v69. They add ids that GMS does not have, parse with the
   ingest's types and need no conversion. Keep them closed with the U-14 feature gate: add the five missing feature
   names to the NA `table/feature.xml` (`Timaion01` is already level 100).
2. `table/npcspawndata.xml` (v69 copy, 4,141 spawn-tag rows; no twin in GMS or EoS). The ingest ignores it; it is the
   only source of KMS spawn tags and an input for U-12. The v61 and v69 copies differ; use v69.

Do not take whole-file:

3. The 33 tables with a differing GMS twin. The old builds are older for some tables and newer for others; a replace
   would drop for example 1,627 `itemextraction`, 331 gacha and 197 `setitemoption` rows. Where the GMS ingest reads the
   table (`individualitemdrop*`, `setitemoption`), the KMS-only rows (81 in `_charge`, 43 in `_event`, 30 in
   `setitemoption`) can be added by key in U-12 after a conflict check on repeated keys. The `kr/` tables (346
   `itemextraction`, 29 `dungeonroom`, 240 `survivallevel` rows) only matter if the pack serves the Korean language.
4. The 34 tables that equal EoS or GMS and the 2 that are older than EoS: nothing to add.

Open before P-01:

- Extract the Resource archives of v69 and look for the `xblock` files of the 38 maps. If they are missing, the maps
  have no terrain and are not playable.
- Translate the 38 map names (only Korean and Chinese exist; belongs to U-10).
- Confirm whether `ParseItemMergeOption` reads the base or the material file.
