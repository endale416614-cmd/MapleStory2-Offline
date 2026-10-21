# Shared ids: Global (GMS) against KMS end-of-service

Task U-05 of [BACKLOG.md](BACKLOG.md), Phase A of [PLAN_1.2.0_UNIFIED_CONTENT.md](PLAN_1.2.0_UNIFIED_CONTENT.md). Measured 2026-10-18.
The merge rule ([UNIFICATION.md](UNIFICATION.md) section 4) is: Global records never change, KMS records are added only for ids Global lacks.
This report answers what the rule leaves behind: for the ids both editions have, which fields differ, and does any difference mean the id is a different thing.

## 1. Result

- Shared ids: items 33,139, NPCs 10,643, quests 6,626, skills 9,166. 1,209 of the shared items are empty placeholders in KMS (`<item id=".."/>`) that `KmsSplit` does not write, so 31,930 items were compared.
- No shared item or NPC is identical. Most of that is not content: `KmsSplit` cannot recover whole field groups from KMS (sell prices per rank, option tables, NPC skill lists, slot attachment details, NPC effect dummies) and KMS omits values that equal its defaults. Quests are close (5,406 of 6,626 identical, 81.6 %); skills are not (1,538 of 9,166 identical, 16.8 %).
- Where both editions state a number, KMS is almost always the higher one. Skill damage rate: KMS higher in 5,407 of 5,528 differing rows. Quest experience: higher in 413 of 507 (median 2.1 times). NPC hit points: higher in 1,294 of 1,672 (median 1.4 times). KMS end-of-service carries years of later balance changes, so these are not bug fixes. Global stays.
- **Id reuse is real.** 793 items have a different icon, model or re-issue preset in the two editions, and 67 of those also have a different name. Example: item 11300491 is "Ranger's Flexible Headgear" in Global and 너구리 귀 (raccoon ears) in KMS; 20301694 is "Day 2 Login Package" in Global and a Toad's whetstone selection box in KMS. Global stays by rule, so these items are not a data risk, but any KMS-only record that refers to one of these ids (a quest reward, a shop line, a drop, a string) points at the Global item. That needs a decision (section 4, group A).
- NPCs: only 3 shared ids use a different model (11001683, 11004067, 29000199), and one of them (11004067) has a different name. Skills: 3 shared ids have a different icon. Quests: no shared quest has a different start or end NPC.
- Rule check: nothing in this comparison asks to change a Global record. The recommendations below never do.

## 2. Method

Inputs (paths are arguments of the script, none are fixed):

- Global: per-id XML extracted from the Global client `Xml.m2d` (`item/`, `npc/`, `quest/`, `skill/`, and `string/en/`).
- KMS: the KMS end-of-service `Xml.m2d` converted by `tools/KmsSplit` to the same per-id layout, run without `--na` (with `--na` the tool skips every id Global has). The KMS `string/en/` and `string/kr/` tables were extracted the same way.

`tools/shared_ids_diff.py diff` compares every id present in both trees:

1. Both files are parsed and flattened to element path plus attribute. Comments, whitespace and attribute order do not count; `true` and `TRUE` are equal; numbers and comma lists of numbers compare as numbers (tolerance 0.001 absolute or 0.01 %); Gamebryo asset names are compared by model name (Global writes `urn:..` prefixes, KMS writes `Data/.../x.nif`).
2. Global files carry one `<environment>` per region next to the base one. Only the NA variant (else the base one) is compared with the single KMS environment, per feature name.
3. Defaults are excluded. For each element path and attribute, the most common Global value (from a sample of 3,000 files per kind) is the default. A value that only one side states and that equals the default is ignored: that is what `KmsSplit`'s default fill would add. A value that only one side states and that differs from the default is a difference, written with the default as `~value` on the other side.
4. Fields one edition never uses (for example KMS `rank` on items, Global `karma` on quests) are marked `ext_gms` / `ext_kms`. They are not conflicts and do not decide an id's class.

Classes per id: identical (no difference), balance only (all differences are numbers), structure (an element, an enum or a text value differs, or an element exists on one side only), extension fields only. The last column of the first table counts ids where both sides state a different value; it is the hard-conflict count, because it does not rely on what an omitted value means.

Names: the KMS English table repeats the Global names for shared ids, so comparing English to English finds nothing. The Korean name is therefore checked against the Global English name with a word glossary learned from all 40,222 shared name pairs (3,268 Korean words). This is a heuristic: it cannot tell a retranslation from a different item, and it flags transliterations ("트루" and "Tru") as different. A name only counts as a different item when an identity field also differs (item `slotIcon`, model, `tool@itemPreset`; NPC `model@kfm`; skill `ui@icon`; quest start and end NPC), with icons and models compared by file name.

## 3. Results

### Counts per kind

| kind | shared ids compared | identical | balance only (numbers) | structure (elements, enum or text) | extension fields only | both sides state a different value |
|---|---|---|---|---|---|---|
| item | 31930 | 0 | 5481 | 23375 | 3074 | 24306 |
| npc | 10643 | 0 | 6 | 10636 | 1 | 2918 |
| quest | 6626 | 5406 | 765 | 294 | 161 | 945 |
| skill | 9166 | 1538 | 4786 | 2568 | 274 | 468 |

### Most frequent differing attributes: item

| ids | element@attribute | classes | numbers: KMS vs GMS | GMS example | KMS example |
|---|---|---|---|---|---|
| 22733 | `ms2/environment/property@subtype` | num 22664, only_kms 114, only_gms 13 | KMS higher 15326, KMS lower 1335 | `17` | `~0` |
| 13323 | `ms2/environment/property/sell@price` | num 8336, only_gms 4987 | KMS higher 4955, KMS lower 3381 | `1,1,1,1,1,1` | `1` |
| 12058 | `ms2/environment/slots/slot/asset@name` | only_gms 18025, text 363, only_kms 193 |  | `11200020_C_ERAmethystCrossGold` | `11200029_F_EAFireFighter01` |
| 11708 | `ms2/environment/customize@colorPalette` | only_gms 10255, num 1453, only_kms 78 | KMS higher 1446 | `5` | `10` |
| 10858 | `ms2/environment/slots/slot/asset@ani` | only_gms 8420, num 3209 | KMS higher 4 | `0` | `~1` |
| 10648 | `ms2/environment/slots/slot/asset@replace` | only_gms 8421, num 2970 | KMS higher 4 | `0` | `~1` |
| 10578 | `ms2/environment/slots/slot/asset@selfnode` | only_gms 15079, only_kms 171, text 16 |  | `EA` | `Scene Root` |
| 10569 | `ms2/environment/slots/slot/asset@targetnode` | only_gms 15079, only_kms 171, text 7 |  | `Bip01 Spine1` | `Scene Root` |
| 10275 | `ms2/environment/customize@color` | only_gms 10232, only_kms 78, num 43 | KMS higher 19 | `1` | `~0` |
| 6863 | `ms2/environment/slots/slot/asset@gender` | only_gms 12839, only_kms 68, num 58 | KMS lower 12, KMS higher 4 | `1` | `~2` |
| 5383 | `ms2/environment/limit@transferType` | num 4998, only_gms 373, only_kms 60 | KMS higher 2478, KMS lower 411 | `0` | `~1` |
| 4270 | `ms2/environment/tool@itemPreset` | only_gms 4027, text 131, num 112, only_kms 106 | KMS higher 98, KMS lower 14 | `11250012` | `11200102` |
| 4139 | `ms2/environment/property@type` | num 4098, only_kms 57, only_gms 25 | KMS higher 3994, KMS lower 104 | `10` | `25` |
| 3857 | `ms2/environment/property@slotIcon` | text 3763, only_gms 78, only_kms 57 |  | `./Data/Resource/Image/item/icon/11250012.png` | `~icon0.png` |
| 3293 | `ms2/environment/tool@itemPresetPath` | only_gms 3162, text 131, only_kms 106 |  | `5/02/` | `(empty)` |
| 3163 | `ms2/environment/property@presetPath` | text 3105, only_kms 106, only_gms 58 |  | `1/80/` | `1/81/` |
| 2420 | `ms2/environment/function@parameter` | text 2375, only_gms 23, num 19, only_kms 5 | KMS higher 15, KMS lower 4 | `0,1,0,330000503` | `~` |
| 2178 | `ms2/environment/option@optionLevelFactor` | num 2100, only_gms 78 | KMS lower 1317, KMS higher 692 | `54` | `50` |
| 1746 | `ms2/environment/slots/slot/asset/dummy@gender` | only_gms 1958, only_kms 97, num 17 |  | `2` | `~0` |
| 1565 | `ms2/environment/customize@defaultColorIndex` | only_gms 1422, num 143, only_kms 78 | KMS higher 105, KMS lower 28 | `-1` | `2` |
| 1532 | `ms2/environment/slots/slot/asset/dummy@translation` | only_gms 2964, only_kms 158, text 26, num 16 | KMS lower 4 | `-5.000000, 5.000000, 20.000000` | `-5, 5., 20` |
| 1507 | `ms2/environment/slots/slot/asset/dummy@rotation` | only_gms 2964, only_kms 158, num 18 | KMS lower 4, KMS higher 2 | `0.000000, 0.000000, 0.000000` | `~77.000000, 90.000000, 127.000000` |
| 1239 | `ms2/environment/property@itemGroup` | num 1161, only_kms 132, only_gms 59 | KMS higher 126, KMS lower 4 | `99` | `~1` |
| 1139 | `ms2/environment/property@globalRePackingItemConsumeCount` | num 1139 |  | `100` | `~4` |
| 1136 | `ms2/environment/property@moveDisable` | text 1136 |  | `1` | `true` |

Extension fields, top 10: `ms2/environment/property@rank` (KMS only, 30273 ids), `ms2/environment/option@constant` (GMS only, 19350 ids), `ms2/environment/customize@ch0` (GMS only, 14314 ids), `ms2/environment/customize@colordetail` (GMS only, 14307 ids), `ms2/environment/customize@ch2` (GMS only, 14221 ids), `ms2/environment/property@representOption` (GMS only, 13414 ids), `ms2/environment/property/sell@priceCustom` (GMS only, 11845 ids), `ms2/environment/option@constantID` (KMS only, 9782 ids), `ms2/environment/option@random` (GMS only, 9137 ids), `ms2/environment/option@optionID` (GMS only, 9002 ids)

### Most frequent differing attributes: npc

| ids | element@attribute | classes | numbers: KMS vs GMS | GMS example | KMS example |
|---|---|---|---|---|---|
| 10375 | `ms2/effectdummy/dummy/attachInfo@applyLocalScale` | only_gms 26197, text 5024 |  | `true` | `~false` |
| 10375 | `ms2/effectdummy/dummy/attachInfo@applyWorldTransform` | only_gms 26197, text 5056 |  | `false` | `~true` |
| 9808 | `ms2/effectdummy/dummy/attachInfo@parentNode` | only_gms 9838, only_kms 2 |  | `Eff_UI` | `(empty)` |
| 8621 | `ms2/effectdummy/dummy/transform@translate` | only_gms 8671, only_kms 1635 |  | `0.0, 0.0, 180.0` | `(empty)` |
| 4126 | `ms2/environment/dead@time` | only_gms 4129, only_kms 53, num 51 |  | `0` | `~5` |
| 4055 | `ms2/environment/stat@car` | num 4049, only_gms 11, only_kms 4 | KMS lower 31 | `0` | `~50` |
| 4016 | `ms2/environment/basic@damagedVibrateAmp` | num 4015, only_gms 2 |  | `0` | `~1` |
| 4013 | `ms2/environment/stat@cad` | num 4012, only_gms 2 |  | `0` | `~500` |
| 3907 | `ms2/environment/distance@sightHeightDown` | num 3905, only_gms 4, only_kms 1 |  | `50` | `~0` |
| 3898 | `ms2/environment/distance@sightHeightUP` | num 3897, only_gms 2 |  | `200` | `~300` |
| 3894 | `ms2/environment/basic@defenceMaterial` | num 3882, only_gms 24, only_kms 11 |  | `0` | `~1` |
| 3883 | `ms2/environment/stat@asp` | num 3882, only_gms 2 |  | `0` | `~100` |
| 3701 | `ms2/environment/aiInfo@path` | only_gms 3464, text 323, only_kms 70 |  | `AI_DefaultNew.xml` | `Pet/AI_DefaultPetTaming.xml` |
| 3480 | `ms2/environment/normal@movearea` | num 3469, only_gms 21, only_kms 3 | KMS lower 1, KMS higher 1 | `0` | `~150` |
| 3434 | `ms2/environment/lookattarget@lookAtMyPCWhenTalking` | only_gms 3435 |  | `1` | `(empty)` |
| 2796 | `ms2/environment/skill@probs` | text 2699, only_gms 172, only_kms 35, num 11 | KMS lower 11 | `100` | `~` |
| 2183 | `ms2/environment/stat@pap` | num 2097, only_gms 172, only_kms 86 | KMS higher 1515, KMS lower 108 | `0` | `~100` |
| 2162 | `ms2/environment/stat@map` | num 2076, only_gms 172, only_kms 86 | KMS higher 1514, KMS lower 112 | `4812448` | `7748038` |
| 2041 | `ms2/environment/basic@nametag` | num 2041 | KMS higher 2 | `0` | `~1` |
| 1923 | `ms2/environment/skill@ids` | num 1808, only_gms 172, only_kms 86, text 29 | KMS lower 1198, KMS higher 610 | `50000525` | `50000525,50000526,50000527,50000530,50000531,50000531,500005` |
| 1913 | `ms2/environment/basic@carePathToEnemy` | num 1911, only_gms 4 |  | `0` | `~1` |
| 1883 | `ms2/environment/skill@levels` | num 1768, only_gms 172, only_kms 86, text 29 | KMS lower 1619, KMS higher 146 | `1` | `1,2,3,11,12,13,21,22,23,1,1,1,2,3,1,1,1,2,1,2,1,12,1,2,3,4,2` |
| 1818 | `ms2/environment/speed@run` | num 1735, only_gms 166, only_kms 80 |  | `0` | `~320` |
| 1758 | `ms2/environment/stat@hp` | num 1672, only_gms 172, only_kms 86 | KMS higher 1294, KMS lower 378 | `47937` | `490373` |
| 1110 | `ms2/environment/speed@walk` | num 1028, only_gms 163, only_kms 79 |  | `0` | `~120` |

Extension fields, top 10: `ms2/environment/basic@difficulty` (GMS only, 6629 ids), `ms2/environment/skill@priorities` (GMS only, 6335 ids), `ms2/environment/basic@mainTags` (GMS only, 4044 ids), `ms2/environment/lookattarget@targetdummy` (GMS only, 3997 ids), `ms2/environment/stat@int` (GMS only, 3883 ids), `ms2/environment/stat@sp` (GMS only, 3883 ids), `ms2/environment/stat@str` (GMS only, 3883 ids), `ms2/environment/stat@hiddenhpadd04` (GMS only, 2652 ids), `ms2/environment/stat@hiddenhpadd03` (GMS only, 2580 ids), `ms2/environment/skill@coolDown` (GMS only, 2438 ids)

### Most frequent differing attributes: quest

| ids | element@attribute | classes | numbers: KMS vs GMS | GMS example | KMS example |
|---|---|---|---|---|---|
| 511 | `ms2/environment/quest/completeReward@exp` | num 510, only_gms 1 | KMS higher 413, KMS lower 94 | `2067175` | `1638426` |
| 329 | `ms2/environment/quest/completeReward/essentialItem@count` | num 290, only_gms 43, only_kms 33 | KMS lower 221, KMS higher 69 | `14` | `1` |
| 191 | `ms2/environment/quest/basic@locking` | num 185, only_kms 6, only_gms 6 | KMS higher 170 | `0` | `1` |
| 129 | `ms2/environment/quest/condition@value` | num 135, only_kms 9, only_gms 3 | KMS higher 121, KMS lower 14 | `1` | `10` |
| 95 | `ms2/environment/quest/condition@type` | text 83, only_kms 11, only_gms 11 |  | `talk_in` | `taxiuse` |
| 88 | `ms2/environment/quest/condition@code` | only_gms 58, num 29, only_kms 13, text 2 | KMS higher 20, KMS lower 9 | `21001001,25001001,21005001,21006001,21007001,25002001,210030` | `1` |
| 75 | `ms2/environment@feature` | text 66, only_gms 12, only_kms 9 |  | `ExtendLevel70` | `~` |
| 69 | `ms2/environment/quest/completeReward/globalEssentialItem@count` | only_gms 98, only_kms 24, num 9 | KMS higher 6, KMS lower 3 | `1` | `0` |
| 61 | `ms2/environment/quest/completeReward/essentialItem@code` | only_kms 51, only_gms 13, num 12 | KMS higher 7, KMS lower 5 | `11150051` | `20300883` |
| 53 | `ms2/environment/quest/require@level` | num 41, only_kms 12, only_gms 12 | KMS lower 25, KMS higher 16 | `1` | `50` |
| 49 | `ms2/environment/quest/basic@standardLevel` | num 37, only_kms 12, only_gms 12 | KMS lower 21, KMS higher 16 | `1` | `50` |
| 37 | `ms2/environment/quest/dispatch@script` | num 37 |  | `0` | `~1` |
| 30 | `ms2/environment/quest/completeReward@money` | num 29, only_gms 1 | KMS lower 22, KMS higher 7 | `5050` | `4750` |
| 30 | `ms2/environment/quest/dispatch@portal` | num 30 |  | `0` | `~1` |
| 29 | `ms2/environment/quest/condition@target` | num 24, only_gms 6, only_kms 5 | KMS higher 3 | `31` | `~10` |
| 28 | `ms2/environment/quest/completeReward/essentialItem@rank` | only_kms 22, num 6 | KMS higher 4, KMS lower 2 | `3` | `1` |
| 27 | `ms2/environment/quest/completeReward/globalEssentialItem@code` | only_kms 15, num 13 | KMS lower 7, KMS higher 6 | `20001048` | `0` |
| 25 | `ms2/environment/quest/completeReward/globalEssentialJobItem@code` | only_kms 102 |  | `(empty)` | `11300073` |
| 25 | `ms2/environment/quest/completeReward/globalEssentialJobItem@count` | only_kms 102 |  | `(empty)` | `1` |
| 25 | `ms2/environment/quest/completeReward/globalEssentialJobItem@rank` | only_kms 102 |  | `(empty)` | `4` |
| 22 | `ms2/environment/quest/completeReward/essentialJobItem@rank` | only_kms 164, num 9, only_gms 7 | KMS higher 9 | `1` | `4` |
| 20 | `ms2/environment/quest/completeReward/essentialJobItem@code` | only_kms 164, only_gms 7, num 7 | KMS lower 5, KMS higher 2 | `20301179` | `12200297` |
| 20 | `ms2/environment/quest/basic@questType` | only_kms 13, only_gms 12, num 7 |  | `1` | `~0` |
| 19 | `ms2/environment/quest/require@quest` | only_gms 12, text 11, only_kms 8 |  | `90000660` | `~` |
| 18 | `ms2/environment/quest/summonPortal@fieldID` | num 18 | KMS higher 18 | `0` | `2000001` |

Extension fields, top 10: `ms2/environment/quest/complete@fieldID` (GMS only, 403 ids)

### Most frequent differing attributes: skill

| ids | element@attribute | classes | numbers: KMS vs GMS | GMS example | KMS example |
|---|---|---|---|---|---|
| 3214 | `ms2/level/motion/attack@brokenOffence` | num 11105, only_gms 4287, only_kms 22 | KMS lower 12 | `0` | `~2` |
| 2880 | `ms2/basic/kinds@rangeType` | num 2073, only_gms 855, only_kms 27 | KMS higher 1 | `0` | `~1` |
| 2827 | `ms2/basic/kinds@element` | num 1955, only_gms 956, only_kms 49 | KMS higher 6 | `0` | `~7` |
| 1967 | `ms2/level/motion/attack/arrowProperty@nonTarget` | num 5400, only_gms 826 | KMS higher 132 | `0` | `~1` |
| 1478 | `ms2/level/motion/attack@targetCount` | only_gms 7259, num 2632, only_kms 62 | KMS lower 49, KMS higher 39 | `0` | `~1` |
| 1282 | `ms2/basic/ui@attackType` | num 1034, only_gms 291, only_kms 26 | KMS lower 6, KMS higher 1 | `0` | `~1` |
| 1175 | `ms2/level/motion/attack/conditionSkill@skillTarget` | num 5624, only_gms 2204, only_kms 118 | KMS higher 3 | `0` | `~2` |
| 1166 | `ms2/level/motion/attack/conditionSkill@skillOwner` | num 5673, only_gms 1892 | KMS higher 3 | `0` | `~1` |
| 744 | `ms2/level/motion/attack/rangeProperty@distance` | only_gms 6890, num 2092, only_kms 72 | KMS higher 899, KMS lower 49 | `0` | `~300` |
| 725 | `ms2/level/motion/attack/damageProperty@attackMaterial` | only_gms 6200, num 3240, only_kms 89 | KMS higher 12 | `0` | `~1` |
| 640 | `ms2/level/motion/motionProperty@motionEffect` | only_gms 1261, text 752, only_kms 46 |  | `CH/Skill/Knight/Eff_Knight_CycloneShield_SplashCast_01.xml` | `~` |
| 582 | `ms2/level/motion/attack/rangeProperty@height` | only_gms 3282, num 1066, only_kms 65 | KMS lower 17, KMS higher 4 | `0` | `~150` |
| 562 | `ms2/level/motion/attack@unrideOnHit` | num 3776, only_gms 1570 |  | `0` | `~1` |
| 514 | `ms2/basic/kinds@unrideOnUse` | only_gms 308, num 269, only_kms 64 | KMS higher 1 | `1` | `~0` |
| 444 | `ms2@feature` | text 444 |  | `(empty)` | `JobChange_01` |
| 422 | `ms2/level/motion/attack/damageProperty@rate` | only_gms 7449, num 5555, only_kms 66 | KMS higher 5407, KMS lower 121 | `0.86` | `0.89` |
| 338 | `ms2/level/beginCondition@cooldownTime` | only_gms 3443, num 754, only_kms 43 | KMS lower 381, KMS higher 193 | `0.1` | `~0` |
| 323 | `ms2/level/motion/attack@attacktype` | only_gms 2307, num 910, only_kms 53 | KMS lower 72, KMS higher 30 | `0` | `~1` |
| 322 | `ms2/level/motion/attack/damageProperty@effectHit` | only_gms 6896, text 250, only_kms 50 |  | `CH/Common/CH/Common/Eff_Trap_Hit.xml` | `~` |
| 288 | `ms2/level/motion/attack/conditionSkill@skillID` | only_gms 4889, only_kms 439, num 8 | KMS lower 8 | `99999697` | `50005335` |
| 281 | `ms2/level/motion/attack@hitImmuneBreak` | only_gms 7970, num 64, only_kms 44 | KMS higher 60 | `1` | `5` |
| 272 | `ms2/level/motion/motionProperty@sequenceName` | only_gms 4178, text 68, only_kms 47 |  | `Ranger_MultiDriveShot2_A` | `Ranger_MultiDriveShot1_A` |
| 262 | `ms2/level/motion/attack/rangeProperty@rangeType` | only_gms 4517, text 82, only_kms 74 |  | `hole_cylinder` | `cylinder` |
| 258 | `ms2/basic/stateAttr@autoTargeting` | num 189, only_gms 104, only_kms 19 | KMS lower 1 | `0` | `~1` |
| 248 | `ms2/level/motion/motionProperty@strTagEffects` | only_gms 3664, text 119, only_kms 11 |  | `Additional/Etc/Eff_Knight_Ice_Hurricane_Additional_01.xml` | `~` |

Extension fields, top 10: `ms2/basic@mainType` (KMS only, 3089 ids), `ms2/basic/kinds@subType` (GMS only, 1703 ids), `ms2/level/motion/attack/conditionSkill@splash` (GMS only, 1148 ids), `ms2/basic/kinds@type` (GMS only, 908 ids), `ms2/level@feature` (GMS only, 590 ids), `ms2/level@stringParam` (KMS only, 461 ids), `ms2/level/upgrade@level` (GMS only, 341 ids), `ms2/basic/ui@exceptUI` (GMS only, 308 ids), `ms2/level/motion/motionProperty@IgnoreOptimalHideSplashInvokeEffect` (GMS only, 242 ids), `ms2/level/upgrade@skillIDs` (GMS only, 235 ids)

### Name comparison

| kind | same | retranslation | different | unverified | missing | identity field differs | name different and identity field differs |
|---|---|---|---|---|---|---|---|
| item | 24339 | 0 | 439 | 3733 | 3419 | 793 | 67 |
| npc | 4458 | 0 | 267 | 2132 | 3786 | 3 | 1 |
| quest | 1852 | 0 | 249 | 1917 | 2608 | 0 | 0 |
| skill | 672 | 0 | 106 | 420 | 7968 | 3 | 0 |

#### Different-meaning candidates: item

| id | GMS en | KMS en | KMS kr | name class | identity field differs |
|---|---|---|---|---|---|
| 11300491 | Ranger's Flexible Headgear | Ranger's Flexible Headgear | 너구리 귀 | different | yes |
| 11300492 | Rogue's Flexible Headgear | Rogue's Flexible Headgear | 다람쥐 귀 | different | yes |
| 11300545 | Roosting Seagull | Roosting Seagull | 꿈꾸는 갈매기 조나단 | different | yes |
| 11300640 | Mage's Darksteel Hat | Mage's Darksteel Hat | 스카우트 베레모 | different | yes |
| 11300741 | Piratical Hat | Piratical Hat | 삐약삐약 노란 병아리 모자 | different | yes |
| 11300956 | (Intro) Beginner Fishing Hat | (Intro) Beginner Fishing Hat | 바니바니 오컬트 토끼 모자 | different | yes |
| 11300981 | (Intro) Velvet Beret | (Intro) Velvet Beret | 바니바니 하얀 달토끼 모자 | different | yes |
| 11600633 | Emerald Gloves | Emerald Gloves | 시크 스타츠 시계 (남) | different | yes |
| 11600664 | Piratical Gloves | Piratical Gloves | 삐약삐약 노란 병아리 손 | different | yes |
| 11700607 | Dark Steel Shoes | Dark Steel Shoes | 보이 스카우트 워커 (남) | different | yes |
| 11700659 | Draconic Boots | Draconic Boots | 걸 스카우트 워커 (여) | different | yes |
| 11700663 | Emerald Shoes | Emerald Shoes | 시크 스타츠 워커 (남) | different | yes |
| 11700964 | (Intro) Velvet Boots | (Intro) Velvet Boots | 바니바니 오컬트 토끼 신발 | different | yes |
| 11700965 | (Intro) Cubic Velvet Boots | (Intro) Cubic Velvet Boots | 바니바니 하얀 달토끼 신발 | different | yes |
| 11850170 | Absolute Cape | Absolute Cape | 메리메리 홀리 페어리 윙 (여) | different | yes |
| 11850172 | MK 52 Omega Propeller | MK 52 Omega Propeller | 풍성한 삼미호 꼬리 | different | yes |
| 11850173 | Balrog Wings | Balrog Wings | 리본 고양이 꼬리 | different | yes |
| 11850174 | Katvan's Wings | Katvan's Wings | 하얀 리본 고양이 꼬리 | different | yes |
| 13300276 | Large Crimson Trial Longsword | Large Crimson Trial Longsword | 루미나이츠 오네스타 홀 | different | yes |
| 13400275 | Large Crimson Trial Thrown Weapon | Large Crimson Trial Thrown Weapon | 루미나이츠 오네스타 표창 | different | yes |
| 15000281 | Large Crimson Trial Greatsword | Large Crimson Trial Greatsword | 루미나이츠 오네스타 대검 | different | yes |
| 20000914 | Tin Whetstone | Tin Whetstone | (이벤트) 공격력 강화 물약 | different | yes |
| 20000915 | Yarrow Candle | Yarrow Candle | 녹슨 벚꽃 상자 열쇠 | different | yes |
| 20000985 | Blockade | Blockade | 숙련자의 벚꽃 상자 열쇠 | different | yes |
| 20001325 | Ducky Cookie | Ducky Cookie | 칭호 : 4주년을 함께한 | different | yes |
| 20001772 | Creamy Tomato Soup | Creamy Tomato Soup | 하이퍼 하얀 포션 | different | yes |
| 20001785 | 'Devoted' Title | 'Devoted' Title | 비상한 집중 물약 | different | yes |
| 20300320 | Dungeon Souvenir Chest | Dungeon Souvenir Chest | 로터스 무전기 : 250개 | different | yes |
| 20300321 | Dungeon Souvenir Chest | Dungeon Souvenir Chest | 로터스 무전기 : 500개 | different | yes |
| 20301694 | Day 2 Login Package | Day 2 Login Package | 토드의 연마석 선택상자 | different | yes |

#### Different-meaning candidates: npc

| id | GMS en | KMS en | KMS kr | name class | identity field differs |
|---|---|---|---|---|---|
| 11004067 | Chalk Graffiti | Chalk Graffiti | 연구원 제이튼 | different | yes |
| 11000016 | Tru | Tru | 트루 | different | no |
| 11000017 | Rumi | Rumi | 루미 | different | no |
| 11000048 | Tru | Tru | 트루 | different | no |
| 11000049 | Navy | Navy | 나비 | different | no |
| 11000079 | Rumi | Rumi | 루미 | different | no |
| 11000088 | Seagull | Seagull | 갈매기 | different | no |
| 11000091 | Flying Seagull | Flying Seagull | 갈매기 날기 | different | no |
| 11000123 | Hawaii | Hawaii | 하와이 연구원 | different | no |
| 11000169 | Mark | Mark | 마크 | different | no |
| 11000246 | Beans | Beans | 미스터 빈 | different | no |
| 11000247 | Alfred | Alfred | 알프레드 | different | no |
| 11000266 | Jin | Jin | 진 | different | no |
| 11000273 | William | William | 윌리엄 | different | no |
| 11000288 | Murpagoth's Vulture | Murpagoth's Vulture | 무르파고스의 독수리 | different | no |
| 11000333 | Common Alchemist | Common Alchemist | 연금술사 일반 | different | no |
| 11000423 | Wild Vulture | Wild Vulture | 야생 독수리 | different | no |
| 11000435 | Beest | Beest | 비스트 | different | no |
| 11000450 | Royal Watchman | Royal Watchman | 근위대 순찰병 | different | no |
| 11000541 | Tai | Tai | 타이 | different | no |
| 11000568 | Militia Reinforcement | Militia Reinforcement | 자경단 지원군 | different | no |
| 11000577 | Guest | Guest | 손님 | different | no |
| 11000578 | Guest | Guest | 손님 | different | no |
| 11000579 | Guest | Guest | 손님 | different | no |
| 11000580 | Guest | Guest | 손님 | different | no |
| 11000581 | Guest | Guest | 손님 | different | no |
| 11000593 | Televiewer | Televiewer | TELI_TRAP | different | no |
| 11000597 | Strange Note | Strange Note | 이상한 메모 | different | no |
| 11000659 | Coco | Coco | 코코 | different | no |
| 11000675 | Khan | Khan | 칸 | different | no |

#### Different-meaning candidates: quest

| id | GMS en | KMS en | KMS kr | name class | identity field differs |
|---|---|---|---|---|---|
| 10001000 | Santiago's Dilemma | Santiago's Dilemma | 산티아고의 고민 | different | no |
| 10001011 | Relaying the Message | Relaying the Message | 소식 전하기 | different | no |
| 10001083 | That Man of Mine | That Man of Mine | 방랑 약초꾼 | different | no |
| 10001121 | Unusual Circumstances | Unusual Circumstances | 그녀의 사정 | different | no |
| 10001230 | Quite a Quandary | Quite a Quandary | 시장의 고민 | different | no |
| 10001250 | The Cat in the Cliff | The Cat in the Cliff | 사라진 고양이 | different | no |
| 10001310 | Twinkle Toes | Twinkle Toes | 요정의 신발 | different | no |
| 10001401 | The Vid-Bleim Mansion | The Vid-Bleim Mansion | 비드블라임 저택으로 | different | no |
| 10001430 | Kanto's Dream | Kanto's Dream | 칸토의 꿈 | different | no |
| 10001510 | Flight of the Butterflies | Flight of the Butterflies | 날아라, 나비야 | different | no |
| 10001560 | Legends of Perion | Legends of Perion | 메이플 연대기 | different | no |
| 10001570 | Robots Running Amuck | Robots Running Amuck | 최첨단 로봇 메이드 | different | no |
| 10001850 | A Timely Interview | A Timely Interview | 합격 소식 | different | no |
| 10002101 | Chasing Footprints | Chasing Footprints | 추적대의 발자국 | different | no |
| 10002103 | Inside the Forest of Chaos | Inside the Forest of Chaos | 혼돈의 숲이 삼킨 벨라 | different | no |
| 10002195 | Into Thin Air | Into Thin Air | 사라진 카트반 | different | no |
| 10002197 | Reporting Facts | Reporting Facts | 진실 보고 | different | no |
| 10002201 | Soul Dimension | Soul Dimension | 영혼의 차원 | different | no |
| 10002203 | A Lost Hope | A Lost Hope | 사라진 희망 | different | no |
| 10002362 | Dazed and Confused | Dazed and Confused | 혼돈의 시간 | different | no |
| 10002410 | A Welcome Guest | A Welcome Guest | 반가운 손님 | different | no |
| 10002504 | Lost Time | Lost Time | 잃어버린 시간 | different | no |
| 10002581 | Location of Ermen | Location of Ermen | 에르멘의 행방 | different | no |
| 10002650 | Strange Ones | Strange Ones | 아리까리 수상한 그들 | different | no |
| 10002660 | Well-deserved Me Time | Well-deserved Me Time | 힐링 타임 | different | no |
| 10002675 | Solitary Bliss | Solitary Bliss | 고독한 행복 | different | no |
| 10002693 | Eyy, Ellinia! | Eyy, Ellinia! | 안녕, 엘리니아! | different | no |
| 10002694 | Howdy, Perion! | Howdy, Perion! | 안녕, 페리온! | different | no |
| 10002696 | Take Care, Taliskar! | Take Care, Taliskar! | 안녕, 탈리스커! | different | no |
| 10002750 | Flying Blind | Flying Blind | 불길한 예감 | different | no |

#### Different-meaning candidates: skill

| id | GMS en | KMS en | KMS kr | name class | identity field differs |
|---|---|---|---|---|---|
| 10000051 | Second Wind | Second Wind | 원기 회복 | different | no |
| 10100041 | Warhorn | Warhorn | 승리의 함성 | different | no |
| 10100081 | Bulwark | Bulwark | 기사의 의지 | different | no |
| 10100281 | Aegis Barrage | Aegis Barrage | 실드 오라 | different | no |
| 10200062 | Improved Intimidation | Improved Intimidation | 위협 강화형 | different | no |
| 10200111 | Deep Wounds | Deep Wounds | 출혈 | different | no |
| 10200121 | Greatsword Mastery | Greatsword Mastery | 라지 소드 마스터리 | different | no |
| 10200211 | Blood Fury | Blood Fury | 피의 광란 | different | no |
| 10300041 | Arcane Blast | Arcane Blast | 에너지 익스플로전 | different | no |
| 10300043 | Arcane Blast | Arcane Blast | 에너지 익스플로전 | different | no |
| 10300091 | Focus Seal | Focus Seal | 집중의 마법진 | different | no |
| 10300266 | Risky Rink | Risky Rink | 폴 스케이트 | different | no |
| 10300291 | Wildfire | Wildfire | 와일드 파이어 | different | no |
| 10400001 | Celestial Light | Celestial Light | 천상의 빛 | different | no |
| 10400031 | Holy Relic | Holy Relic | 홀리 크로스 | different | no |
| 10400081 | Celestial Guardian | Celestial Guardian | 천상의 수호자 | different | no |
| 10400091 | Celestial Blessings | Celestial Blessings | 천상의 축복 | different | no |
| 10400191 | Light Spear | Light Spear | 빛의 창 | different | no |
| 10400193 | Light Sword | Light Sword | 빛의 검 | different | no |
| 10400231 | Greater Healing | Greater Healing | 그레이터 힐 | different | no |
| 10400271 | Divine Cross | Divine Cross | 신의 십자가 | different | no |
| 10500011 | Arrow Barrage | Arrow Barrage | 멀티 샷 | different | no |
| 10500041 | Evasive Salvo | Evasive Salvo | 이베이젼 샷 | different | no |
| 10500121 | Conditioning | Conditioning | 피지컬 트레이닝 | different | no |
| 10500293 | Eagle Squad Self | Eagle Squad Self | 사거리 확보 | unverified | yes |
| 10600001 | Gatling Fire | Gatling Fire | 개틀링 파이어 | different | no |
| 10600051 | Stun Grenades | Stun Grenades | 그레네이드 | different | no |
| 10600101 | Bullet Spray | Bullet Spray | 불렛 샤워 | different | no |
| 10600111 | Advanced Bullets | Advanced Bullets | 불렛 업그레이드 | different | no |
| 10600141 | Advanced Missiles | Advanced Missiles | 미사일 업그레이드 | different | no |

How to read the tables: "ids" is the number of shared ids with at least one difference in that field. Class counts are rows (one id can have many rows in repeated elements). `~value` is the implied default of the side that omits the field. Numbers are the first value of a list. Examples are the most frequent pair in which both sides state a value, otherwise the most frequent pair.

Reading the name tables: "different" is the glossary heuristic only. The first 30 NPC and 30 quest names flagged "different" without an identity change read as transliterations (Tru and 트루) or free translations of the same title (Santiago's Dilemma and 산티아고의 고민); a few quest titles differ in wording ("That Man of Mine" and 방랑 약초꾼, wandering herbalist) and could be re-titled quests. No shared quest has a different start or end NPC, so those counts are treated as noise, not as different quests. "unverified" means the Korean name is missing or too few of its words are in the glossary. "missing" means Global has no usable English name: quest names in the Global string tables are placeholder keys, and most skill names are placeholders too. A real check of quest and skill names needs the Global client's other string source and is not done.

## 4. Recommendation per field group

Default for every group: Global stays. The rule needs no code, because the ingest is additive. "Bug fix candidate" and "conflict" only name where a human decision could change that default.

| Group | Fields (examples from the tables) | Observed | Recommendation |
|---|---|---|---|
| A. Identity of the record | item `slotIcon`, `asset@name`, `tool@itemPreset`, `property@type`; NPC `model@kfm`; skill `ui@icon` | 793 items, 3 NPCs, 3 skills differ in icon, model or preset; 67 items and 1 NPC also have a different name | **Conflict needing a decision.** Global record stays. Decide how KMS-only content that references one of these ids is handled: remap the reference to a new id in an unused range, or drop it. Start from the item list in section 3; the full list is the tsv rows for these fields |
| B. Balance numbers | item `sell@price`, `option@optionLevelFactor`, `limit@transferType`, `property@itemGroup`; NPC `stat@hp/map/pap/ndd`, `speed`, `distance`, `dead@time`; quest `completeReward@exp/money`, `essentialItem@count`, `require@level`, `standardLevel`; skill `damageProperty@rate`, `beginCondition@cooldownTime`, `rangeProperty`, `attack@targetCount` | KMS higher in most rows (skill rate 98 %, quest exp 81 %, NPC hp 77 %); quest money and skill cooldown are mostly lower in KMS | **Global stays.** KMS values are later balance, not fixes. Do not mix them in: a merged skill or NPC would change Global combat |
| C. Value of an omitted field | the `~value` rows: NPC `stat@car/cad/asp`, `basic@damagedVibrateAmp`, `basic@nametag`, item `asset@ani/replace`, skill `attack@brokenOffence`, `kinds@rangeType/element` | Global states a non-default value, KMS omits the field. `KmsSplit` output has no value there, so this is missing data, not a measured conflict | **Global stays.** Treat as converter-limited (see U-04): nothing to merge |
| D. Presentation and attachment | item `slots/slot/asset@selfnode/targetnode/gender/ani/replace`, `dummy@translation/rotation`, `customize@colorPalette/color/defaultColorIndex`; NPC `effectdummy`, `lookattarget`, `aiInfo@path`; skill `motionEffect`, `sequenceName`, `strTagEffects`, `effectHit` | Mostly one side states, the other is a converter default or empty (item slots 12,058 ids, NPC effect dummies 10,375 ids) | **Global stays.** Item slot values are already set per slot by the converter rules (`replace`/`ani`, U-02); do not feed KMS values for Global ids |
| E. Fields one edition does not have | KMS only: item `rank`, `option@constantID`, skill `mainType`, `level@stringParam`. Global only: quest `karma`, `complete@fieldID`, NPC `basic@difficulty/mainTags`, `stat@str/int/sp/hiddenhpadd*`, item `customize@ch0..2`, `option@constant` | 3,074 items, 1 NPC, 161 quests, 274 skills differ only in such fields | **Ignore.** Not a conflict. The Global client and server do not read the KMS-only fields; Global-only fields stay as they are |
| F. Quest conditions and rewards | quest `condition@type/value/code/target`, `completeReward/essentialItem*`, `globalEssentialJobItem*`, `require@quest` | 294 quests differ in structure (for example job reward items exist only in KMS) | **Global stays.** No shared quest changes its NPCs. Candidate for a decision only if a later check shows a Global quest cannot be completed |
| G. Quest helper fields | quest `dispatch@script/portal`, `summonPortal@fieldID/portalID`, `basic@locking` | Global 0, KMS a value, in 18 to 191 quests; `locking` is higher in KMS in all 170 rows | **Bug fix candidate**, unverified. Check in game whether the Global quest is stuck without the value before taking it; otherwise Global stays |
| H. Feature gates | quest `environment@feature` (75 quests); skill `ms2@feature` (444 skills that KMS gates, for example `JobChange_01`, and Global does not) and `level@feature` (590 skills whose levels Global gates, for example `CharacterBalance_01`, and KMS does not) | Global and KMS gate the same id with different feature names or with none | **Conflict needing a decision**, same as U-14 (feature gate for KMS content). Changing a Global gate changes which skills a Global character gets, so the default is: do not touch the Global gate |
| I. NPC skill lists and AI | NPC `skill@ids/levels/probs`, `aiInfo@path` | KMS lists differ from Global in about 1,900 NPCs; the KMS lists are partly unrecoverable (U-04) | **Global stays** |

## 5. Rerun

The raw per-id table is `shared-ids-diff.tsv` (one row per difference, columns `kind id path attr gms kms cls`; `cls` is `identical`, `num`, `text`, `only_gms`, `only_kms`, `ext_gms`, `ext_kms`) and `shared-ids-diff.names.tsv`. It is large (about 71 MB) and lives in the ingest folder, not in the repository.

```
py tools/shared_ids_diff.py diff --gms <global per-id dir> --kms <KmsSplit output dir> \
    --gms-strings <global string dir with en/> --kms-strings <kms string dir with en/ and kr/> \
    --out <ingest dir>/shared-ids-diff.tsv
py tools/shared_ids_diff.py summary --diff <ingest dir>/shared-ids-diff.tsv \
    --names <ingest dir>/shared-ids-diff.names.tsv > summary.md
```

`py tools/shared_ids_diff.py selftest` runs the built-in checks. The KMS tree comes from `KmsSplit <KMS Xml.m2d> <output dir>` (no `--na`). The diff takes about 5 minutes on 20 cores (one process per CPU) for all four kinds; `--kinds quest,skill` limits it. `summary --ignore REGEX` drops fields from every table; without the groups KmsSplit cannot fill (`--ignore "/sell@|/option@|/slots/|/customize|effectdummy|environment/skill@|environment/additionalEffect|/motion/attack|conditionSkill"`) the class counts become:

| kind | identical | balance only | structure | extension only | both sides state a different value |
|---|---|---|---|---|---|
| item | 0 | 12,882 | 15,765 | 3,283 | 24,248 |
| npc | 0 | 2,547 | 7,685 | 411 | 2,003 |
| quest | 5,406 | 765 | 294 | 161 | 945 |
| skill | 3,940 | 2,433 | 2,359 | 434 | 230 |

## 6. Not covered

- JMS is not compared (BACKLOG named it; the plan scopes U-05 to Global against KMS).
- Table-driven data (shops, drops, `feature.xml`, string tables other than names) is not per-id XML. The plan's risk 4 (a shop table row that the merged ingest changes) needs its own row compare against the merged database.
- The default per field is the most common Global value of a sample, and an omitted KMS value is read as "default". If KMS omits a field because the data is not in its source (group C), the comparison under-reports that field.
- Names: see the reading notes above. Quest and skill names could not be checked, and 3,733 items, 2,132 NPCs, 1,917 quests and 420 skills are unverified.
- Strings other than names (descriptions, quest text) are not compared. KMS English text repeats Global for shared ids.
