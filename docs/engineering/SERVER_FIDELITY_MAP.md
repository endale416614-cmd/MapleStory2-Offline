# Server fidelity map

How close the server is to the original Nexon server, subsystem by subsystem, and which local material can close each
gap. Measured 2026-10-25 on the server code (after NB-16), read-only, from the sources, the server code, the
protocol and the behaviour evidence; 22 numbers were measured twice and this document was checked against the
code. The plan built on it:
[../agents/PLAN_SERVER_FIDELITY.md](../agents/PLAN_SERVER_FIDELITY.md).

Fidelity percentages are judgement anchored on measured ratios (protocol handlers fed, requests handled, trigger calls
that reach a stub, original data read, known wrong behaviour). They rank the work; they are not measurements. Unread
field counts are floors (a name match counts as read). Some gaps counted here are closed by 1.2.2 rows still open
(GD-03 guild buffs, SE-01 Shadow Expedition gauges) when this map was written; GD-03 landed in 1.2.2 and SE-01 in 1.2.3.
Section 6 re-measures FT-04's counts on 1.2.3 (FT-06, 2026-11-08); the rows of section 2 are as measured on 2026-10-25.

## 1. What local material holds original server behaviour

| Source | What it holds | Used today |
|---|---|---|
| Community `Server.m2d` v1.3.2 (`_ingest\MapleStory2-XML-v1.3.2`) | 1,814 files: 131 `table/Server` tables, 1,683 AI scripts; also the `CN\` folder (9 files), 12,683 `locale="CN"` rows, 185 `locale="NA"` rows (promotionContents 140, dated 2018-10 to 2020-03: GMS-era server data; npcScriptCondition 28, shop_beautyCoupon 8, enchantScroll 5, npcScriptFunction 1) and 4 kept originals (`individualItemDrop_Final_Backup`, `ItemOptionRandom_Backup`, `itemOptionVariation_Backup`, `OxQuiz_KR`) | read by the ingest, except 76 tables with no parser method and 7 parser-only tables; promotionContents is ignored |
| TW dev leak 2024-04-25 (`_ingest\reawakening\TW-leak-2024-04-25`) | the same server line before the community edits: 1,781 of 1,799 shared files identical; the 18 that differ are constants (921 keys against the community's 1,028), fieldRestrainTable, globalDropItemBox_Final, globalDropItemSet_Final, individualItemDrop_Final (4,637 boxes, no NA rows; the community's 5,363 add 8,303 NA rows), ItemOptionRandom (500 rows against 21,031), itemOptionVariation (139 against 761), OxQuiz (2,132 Korean against 1,353 English) and shop_game, plus 9 DungeonOS04 AI | not read (the community copies are) |
| GMS client `Xml.m2d` (2020-03, the shipped client) | client tables with server meaning (map properties, quests, NPC scripts 3,366, triggers 4,584, skills 9,915, additional effects 6,079, dungeon rooms, season data, gacha odds `smartDropRate` 8,574 + 1,269 rows, `individualitemdrop_monster` 12,374 pool rows without rates), `Exported.m2d` map entities (28,565 NPC spawn points, 29,353 region spawns, 8,013 breakable actors, 4,159 region skills) | read, with dropped attributes (section 3.1) |
| KMS builds v61 to end of service (`Full Client\KMS2\*`; EoS unpacked) | the KMS line of the same tables, the era of the server line (2024); v61/v69 keep 33 tables later builds dropped (`npcspawndata`, `individualitemdrop*`, ...) and per-id `map/`, `npc/`, `script/` | read for the pack (1.2.0 unified content) |
| GMS2 clean (`_ingest\reawakening\GMS2-clean-2020-05-27`), CMS final (`_ingest\CMS2\client`, `_ingest\reawakening\CMS2-final-2022-11`), JMS (partly: `_ingest\browser\JMS\xml` item, npc, quest, string; `_ingest\scan\JMS` achieve, item, map, npc, quest), TW MapleStars2 | client data only: none of the 15 client builds has an `ai/` or `table/server` folder (`_ingest\reawakening\ClientDiff-all`, 15 file-hash lists). The GMS2 clean `Xml.m2d` is probably the shipped client's (same size, 191,573,128 B, 2020-03-16; no hash compared) | region-only content: M2c (active); the JMS and CMS additions are content batch 4 (1.3.0) |
| Recovered KMS v81 client C++ (partial: 5 of 401 `commonfiles` units local) and the v81 debug exe (66,052,624 B, unpacked, disassembled with Capstone; no decompiler configured) | what the client expects of the server: every opcode handler, packet layouts, client-side rules (BR-01: cubes break on the client from the Vibrate packet) | used per task (BR-01, earlier DL rounds) |
| Packet captures (MapleShark2 `.msb`) | 190 read in place: 61 loose (50 GMS build 12, 11 KMS), 126 at the top of the orion2 zip, 3 in the Doll Mansion zip; 27 of them byte-identical copies (24 GMS, 3 KMS). This map's packet counts cover the 174 GMS files among them. BR-01's research also unpacked the 40 nested `.rar` and 8 nested zips (434 more): 585 unique of 624 in all (572 GMS build 12, 13 KMS; 39 copies, 12 of them nested), not in this map's counts. 10 more under `Website Archives\MS2 Hub - 09.28.2024` (1 GMS 2020-04; 6 live KMS 2023-09 to 2024-05, the closest in time to the 2024 server tables). Systems: class skills, enchanting, housing, life skills, mini games, party, quests, UGC, world bosses, the weekly reset, Dark Descent, Ishura, dismantling | the Ophelia enchant rates; BR-01 (155,267 Vibrate packets over the 572 GMS files). The reader is `tools/msb.py`. The set is unpacked once in `_ingest\captures`, with `index.tsv` by system, map and opcode. `Maple2/capture-opcode-tally.tsv` holds the counts (FT-01) |
| GMS asset exports 2018-07 to 2020-03 (unpacked) | launch-to-sunset history; `Data\lua\Precompiled\luapack.o` in each (131 functions at sunset) | the bytecode runs in LuaTests through Maple2.Lua for all 53 ported functions (FT-02); the package embeds the shipped client's luapack.o byte for byte (sha256 4342722e), and luapack64.o is the same code in another size_t width |
| Discord and website archives | patch notes, guides, 128 news links in announcements | not used |

There is one original server line: the KMS/TW 2024 tables, with community edits in the copy the ingest reads. Its era
is KMS 2024, not GMS 2020. GMS-era server data is limited to the 185 NA rows (in both sets); NA files in the
community set are otherwise copies of KR.

## 2. Map by subsystem

| # | Subsystem | Original behaviour and evidence | Current state | Material that closes the gap | Not recoverable locally | Fidelity | Effort | Needs |
|---|---|---|---|---|---|---|---|---|
| 1 | Session, login, keep-alive | `Ping` 0x06 every 9.55 s (median, 5,811 GMS packets); `Ah` 0x95 periodic both ways (an anti-cheat heartbeat, inferred); Reconnect 524; RequestSystemInfo 164 | `RequestHeartbeat` instead of `Ping`; 12 login RecvOps unhandled, 7 deliberate no-ops (`IgnoredPacketHandlers.cs`) | captures (layout and timing) | server anti-cheat | 85 % | S | - |
| 2 | Characters and player state | once per login: CharacterAbility 0xE1, UserConditionEvent 0xBF, SyncWorld 0x112, UserMaid 0xA0, MaidCraftItem 0xAB (about 161 each across the 174 GMS captures), 0x132 (134); BindItem 0x105 (660) | none sent; CharacterAbility and NameChange requests unhandled; base stats marked inaccurate (`BaseStat.cs:278,297`); premium reward never reset; HP/SP/EP are filled at login by rule (ST-01, 1.2.2), not carried over | capture payloads; v81 handlers | character-ability rules (outputs only) | 75 % | M | decode, play |
| 3 | Field, maps, instances | GMS map properties (non-default: capacity 777 maps, pkMode 78, skillUseDisable 72, useTimeEvent 839, tutorialType 104, bigCity 10); server InstanceField 595 rows | 8 map properties, 4 limit fields (plus Capacity, MaxLevel, Climb, Move, FallDamage) and 5 InstanceField fields unread; 10 field RecvOps unhandled, 19 field SendOps never sent | all ingested; only server reads are missing | channel capacity and migration rules | 75 % | M | - |
| 4 | Map entities and spawns | `Exported.m2d` entities; PeaceRestore true on 28,523 of 28,565 spawn points; server combineSpawn, groupSpawn (29) | not mapped: MS2SafeZone 184, MS2UGCReturnPlace 375 (MS2LiftableTargetGuide 277 is probably client-only); unread: RegionSkill `friendly` 690, moving breakables 195, BattleField 32, TeamID 24, SpawnTypeID 42, PeaceRestore; groupSpawn parser-only; the vibrate tag and reset time are in code (IG-02) and reach the data with RB-01 | GMS `Exported.m2d`; KMS `Exported.m2d` (1,927 xblocks) | respawn and safe-zone rules beyond the data | 70 % | M | ingest, rebuild |
| 5 | NPC AI | 1,683 AI scripts, 48 kinds, 122,587 nodes; NPC control every 0.080 s | 39 of 45 model types run; 6 empty nodes used 104 times in 33 AIs, 32 of them bosses (`AiState.cs`: ShowVibrate and HideVibrateAll in Balrog, its Chaos and Kritias forms, the BalrogMagicBurster files, Blackbean, Tristan Chaos and others; ride; ModifyRoomTime in 13 Zakum, Balrog and Dark Star chaos files; CreateRandomRoom; CreateInteractObject); parser drops `battle startAni/endAni` (70 files), `battleEnd isKeepBattle` (2), `isBattle` (1); battle end and onlyDead are in code (NB-16) and reach the data with RB-01; at runtime 268 AI paths for 307 hostile NPCs exist in no source (172 of class 4, 6 or 7), served by the default AI | server AI; the leak originals of the 9 DungeonOS04 files | the 268 missing AI scripts | 75 % | M | raw XML, rebuild, play |
| 6 | NPC stats, scaling, EXP | npcStatFactorByPlayerCount 814 rows, ByLevel 297, DungeonScaleStat 5; monsterExpTable by rank (level 60: common 15,393, elite 35,019, dungeonElite 150,084, boss 395,826, dungeonBoss 554,156); restExp, exploreExp, pvpExp | none ingested (no parser method); `NpcStatFactorId` and `PlayerCountFactorId` kept and never read; InstanceField names a stat factor on 11 of 595 rows, 5 of them the missing 5001; 56 of 101 GMS dungeon rooms name a player-count factor, 38 of them (4001, 1000) HP x1 and attack x0.95 solo; the solo changes are 9 raids (1200: HP and attack x0.7) and 6 chaos raids (HP x0.121 to 0.58); chaos raids 3004 and 3005 name missing factors. Monster EXP comes from `CustomExp` or the common curve only (`ExperienceManager.cs:99-104`), so 719 hostile class 3, 4, 6 and 7 NPCs give common EXP. Adapted floors solo raid damage (`OfflineFairFight.cs`, RFF_DESIGN.md; no ADAPTATIONS row yet) and scales EXP by its grind rate (A16) | server tables; ExperienceUp after kills in the class and Dark Descent captures (the class-to-column mapping) | - | 45 % | M | raw XML, rebuild, play |
| 7 | Map scripts (triggers) | 4,584 GMS scripts; 42,580 captured Trigger packets (Dark Descent, Charnel House) | 250 functions, 92 do nothing: 3,174 stub calls in 389 scripts on 162 map blocks (0.96 % of 331,566 calls); `WaitSecondsUserValue` always false (34 calls: 27 Kritias field maps, 7 test maps); dungeon stubs DungeonFail 36/31, DungeonTimeout 35/16, DungeonCloseTimer 21/14, GiveGuildExp 16/16 (calls/maps, raw XML) | trigger data; captures for replay | functions with no capture (wedding, guild vs guild) | 80 % by call share, lower on dungeon and mini-game maps | M-L | play |
| 8 | Quests and NPC scripts | 3,366 GMS scripts; server condition and function tables (read); exceptEpicRestart and _2 (748 quests, ignored); `commonexptable`: quest table 3 x1, mission x0.5, dailymission x1, dailymissionLevelUp x2, questSkyFortress table 11 x1 | **quests and missions with a relative EXP reward give none of it**: `RelativeExp` is ingested (`QuestMapper.cs:115`) and read nowhere, and `ExperienceManager.AddExp` has no case for those types ("Unhandled ExpType"); raw GMS quest XML: 2,650 quests give relative EXP only (quest 990, mission 791, dailymission 388, questSkyFortress 294, dailymissionLevelUp 187), 1,918 both kinds. 14 of 15 quest commands (EpicRestart unhandled); fame quests never start (`QuestManager.cs` TODOs); GuildCoin, MenteeCoin, MissionPoint unread | `commonexptable`; server exceptEpicRestart; ExperienceUp after a turn-in in the quest captures | - | 65 % | S-M | ingest, rebuild |
| 9 | Skills, combat, buffs | 9,915 skills, 6,079 effects; 17 class-skill captures | the 61 sensors with a non-zero `targetSelectType` (all 2) are dropped (`SkillMapper.cs:42-48`); they are player skills in 5 files (Ice Cream Time 10300193/195/197, Little Meteor 10300254/255; FS-01 left Ice Cream Time out); the target filters on `rangeProperty` (267 elements; targetHasBuffID, targetHasBuffOwner, targetStatCompare, ...) are unmapped, in player skills (14 files) and NPC and trigger skills (folders 49, 50, 90, 99); 48 skill fields unread; resistance, reflect and chaining TODOs; SkillUseFailed 0x41 is sent with two wrong fields (an i32 that should be the skill id, a byte that should be 6; 5 captured packets); StateSkill mode 6 writes an extra int (layout to check) | GMS skill XML; v81 layouts; class captures | the server's damage pipeline order (Lua and captures only) | 65 % | L | ingest, rebuild, decode, play |
| 10 | Formula layer (client Lua) | `luapack.o`: 131 functions | `Lua.cs` 53 functions, all on bytecode grids in LuaTests (FT-02): 35 agree, 18 hold their differences under BT-201 to BT-205; 56 of the 109 Maple2.Lua names not ported, listed with their sites at the end of section 3 (damage, attack and move speed, merge, market and UGC listing fees, pet, guild, survival, gathering) | the original bytecode (test dependency, used) | server-only formulas outside the pack | 60 % | M | tests |
| 11 | Items, enchant, options, sockets, lapenshards | Ophelia +0 to +10: 100 to 30 %; fail type 2 (Unstabilize) at the 40 % and 30 % stages; catalyst lines 2 up to 60 %, 3 from 50 % (captures); server enchantScroll 2,029, itemSocketScroll 29 (ignored); 1,150 GMS lapenshards, each naming a skill | rates and EXP match; catalysts TODO; fail type always None (`ItemEnchantPacket.cs:36`); 6 item RecvOps unhandled (TransferEnchant, PotentialAbility, ItemRepeat, Gamble, RecallScroll, TreasureMap); 3 of 9 socket commands unhandled; dismantle uses ItemBreakTable, GachaInfo coins, then 700-1000 x rarity Onyx, plus Chaos Onyx for Epic and above (`ItemDismantleHandler.cs:25,251`; a capture of dismantling exists); lapenshards are stored and do nothing (`ConfigManager.cs:511-560`) | captures; server scroll tables; the lapenshard skills in the item XML | gamble and potential odds | 70 % | M | ingest, decode, play |
| 12 | Drops and loot | individualItemDrop (community 5,363 boxes); globalDropItemBox 64, Set 115; globalEquipmentRankProbability 3,960 rows (rarity by NPC rank, level, assist); shadowExpeditionDrop 102; GMS gacha `smartDropRate`; NA `individualitemdrop_monster` pools | at runtime 32 hostile NPCs name one of 4 missing boxes (22400071, 22409035, 23406071, 23502022; 24 of them 23502022); rank probability ignored, rarity from the box or a default (`ItemDropManager.cs:347-350`); SocketDataId, DisableBreak, Announce TODO (`:295`); pet drop rarity TODO | server tables; the GMS gacha odds and monster pools to check the community rows | GMS-era monster drop rates | 70 % | M | raw XML, rebuild, Legacy line |
| 13 | Shops, Meret Market, Black Market, economy | shop_game 289 (read); shop_merat 1,531 parser-only; shop_cash, shop_specialRandom_new, meratMarketRandomTable ignored | LoadNew and LoadCart unhandled; `resetIntervalTime` dropped (2 shops); shop requirements TODO; the UGC listing fee is whatever the client sends; the Meret catalog is an adaptation (A5-A7, A11, A12, A21, A22); the Dynamic Black Market is M6, not yet an ADAPTATIONS row | server shop tables | player-driven market activity | 70 % excluding adaptations | M | ingest, rebuild |
| 14 | Crafting, mastery, life skills, fishing | fish tables (read; catchProp 10000 and fishingTime 15000 on all 233 fish); 5 life-skill captures | lure fields dropped (24 lures with `spawnFish`, 3 with an effect); MaidCraftItem unhandled; mastery recipe HabitatMapId and 6 fields unread | server fish tables; captures | - | 75 % | S-M | ingest, rebuild |
| 15 | Housing and UGC | housing captures (11,329 packets); 3 home layout tables (ignored); FittingDoll 0x7D (258) | RequestApartment, Maid, Ugd unhandled; VoteHome, KickOut, Accept no-ops; 11 dead writers; EnterUgcMap writes 3 of 12 result modes; English text instead of string ids; UGCReturnPlace not mapped | captures | maid behaviour rules | 70 % | M | decode, play |
| 16 | Guild | client guild tables; server GuildRaidReward (6); guild captures; Gvg 0x6F (326) | 23 no-op methods (`GuildHandler.cs:579-747`; the buff ones are GD-03 in 1.2.2), UpdateEmblem; 27 dead writers; 13 trigger stubs; 17 GuildTable fields unread; ParseGuildContribution and ParseGuildEvent never called; guild Lua not ported | client tables; captures | guild vs guild and guild event rules | 35 % | L | ingest, rebuild, decode |
| 17 | Party, dungeons, raids | InstanceField 595; dungeon room, mission and rank reward tables; chaos-raid rooms with `durationTick="900000" isExpireTimeOut="1" defaultRevivalLimitCount="3" limitMeratRevival="1"`; DungeonScoreBonus 24, RewardContentItemTable 162, room_random 46; Dark Descent DarkStream 0xC7 (763) | the room timer is not run (`DurationTick` read only by `OfflineFairFight.cs`); `MaxRevivalCount` mapped and never read; DungeonMission and DungeonRankReward loaded and never read; 19 DungeonRoom fields unread; rank score TODO (`DungeonManager.cs:521`); 13 dungeon trigger stubs; 3 party no-ops; DungeonReviveCount and DarkStream never sent; RoomRandomTable not ingested | server and client tables; Dark Descent captures | the rank score formula; matchmaking | 50 % | L | ingest, rebuild, play |
| 18 | PvP and arenas | Pvp 0xB8 (488), Gvg (326), WorldChampion (27); season data; Red Arena popup capture | no Pvp or WorldChampion handler; season data unread; PkMode unread; PvP triggers only log | client season tables; captures | matchmaking, ranking, season rewards | 5 % | XL | decision |
| 19 | World bosses, events, time, resets | timeEventData 6,063 rows; GameEvent (1,615, NA a copy of KR; CN/GameEvent 1,427 rows 2017-2022, in both sets); weekly reset pushes (captured at 00:00) | world bosses and global portals run, but the feature filter keeps WorldBoss 19 of 25 rows, GlobalPortal 15 of 247 and FieldInteract 2,987 of 5,515; time-event types 2, 6 and 7 never ingested (`ServerTableMapper.cs:796-860`): HiddenRealmPortal 247 rows on 181 maps, naming `room_random` ids 1, 2 and 4 (the server invents a schedule of 5 min to 8 h and a 30 s portal, `FieldManager.State.cs:846-870`), KritiasChestPuzzle 27 rows (feature `Kritias_2018_12`) that set `EventStart` or `MelodyOn` on the 27 Kritias field maps, whose puzzles therefore never start (they also wait on the `TimeEventLifeTime` user value), KritiasInvasion 2 rows (`fieldwardata.xml` not ingested); 3 of 1,615 dated events reach the DB (expired events skipped, `:916-919`; `ParseGameEventData`, `:962`, returns null for unparsed types), so of the 15 event types the server runs (AttendGift, Mapleopoly, Bingo, Gallery, TimeRun, DTReward, ReturnUser, EventFieldPopup, sales) only the two UGC map sales have data (M9 T-04); BirthdayCard, TreeWatering unhandled | server tables; the reset capture as a test target | GMS event rows | 30 % | M | ingest, rebuild |
| 20 | Mail, chat, buddies, mentor, wedding | misc captures; client wedding tables | chat 9 types work; mentor 4 commands unhandled, 9 dead writers; AdBill read-only; mail text not escaped; wedding 3 commands unhandled, 2 no-ops, 13 trigger stubs | captures; client tables | the wedding hall flow | 65 % | L (wedding) | play |
| 21 | Achievements, trophies, prestige | adventure level abilities; the weekly reset pushes Prestige | PrestigeLevelAbilityTable never read (`BuffManager.LoadFieldBuffs` has placeholder comments for lapenshards, game events and prestige); prestige exp fixed or missing (`GameSession.cs:671`); achievement `etc` reward ignored; 96 condition types never reported (TA-01, 1.2.2) | client tables (loaded) | - | 75 % | S-M | - |
| 22 | Pets and mounts | pet exp and evolution tables; Lua calc_petExp; mount captures | Fusion, Attack, Evolve, EvolvePoints only read bytes; 11 of 20 pet writers dead; no pet exp; ride node empty; RideSync writer dead | client tables; Lua; captures | - | 45 % | L | ingest, rebuild |
| 23 | Mini games and arcades | OxQuiz (2,132 Korean originals against the community's 1,353 English); hideAndSeek 36; KMS Doll Mansion and Cathy Mart captures | OX Judge and Winner empty; survival steps empty; 36 arcade trigger stubs; PlayArcade, CardReverseGame, Microgame, SpectateMushtopia unhandled | the Korean originals (need L-01's Korean text); KMS captures | exact scoring of most modes | 30 % | L-XL | decode, play |
| 24 | Death and revival | `infinityMeratRevival` 0 on 66 GMS maps; `recoveryFullHP` 14 maps; the dungeon revival limits; DungeonReviveCount (7 GMS packets) | auto-revive on 71 maps (RV-01); `infinityMeratRevival`, `recoveryFullHP`, `ConstantsTable.LimitMeratRevival` and the dungeon limits unread (`RevivalHandler.cs` checks only NoRevivalHere); DungeonReviveCount never sent | map XML; constants; captures | - | 70 % | S | - |
| 25 | Developer and GM | - | 3 RecvOps unhandled, 9 SendOps never sent; replaced by 44 chat commands | - | - | not scored | - | - |

## 3. Cross-cutting gaps

1. **The ingest drops attributes.** Parser level (no member in Maple2.File.Parser.Tadeucci 2.3.7): AI `battle
   startAni/endAni` (70 files), `battleEnd isKeepBattle` (2), `isBattle` (1), 4 server condition, function and event
   attributes, KMS `ObjectSearchIndex`. Mapper level: skill target filters (`rangeProperty`), the 61 player-skill
   sensors, 7 more skill fields (415 values), fish lures, shop `resetIntervalTime`, time-event durations (229),
   time-event types 2, 6 and 7. Map entities: SafeZone, UGCReturnPlace and the unread properties of row 4. NB-16's raw
   XML reader next to the parser (`AiMapper.cs`) closes parser-level drops without forking the package.
2. **Parser package limits.** 76 of the 131 server tables have no parser method (scaling, EXP, rank probability, rooms,
   rewards); 7 are parser-only; 9 client parsers are never called (DungeonRoundData, ReverseRaidConfig,
   GuildContribution, GuildEvent, PetExp, FishHabitat, SurvivalLevel and its reward, IndividualItemDropFinal).
3. **Loaded but unread.** DungeonMission, DungeonRankReward, the client IndividualItemDrop copy, PrestigeLevelAbility,
   SeasonData, `RelativeExp`, `MaxRevivalCount`, `DurationTick` (outside RFF); at least 388 metadata fields;
   `ConstantsTable` 841 fields (mostly client UI, inferred); the scaling ids.
4. **Protocol.** 53 RecvOps without a handler (the client sends 48 of them); 86 client handlers never fed, 25 of them
   with original GMS traffic (DarkStream 763, BindItem 660, Pvp 488, Gvg 326, FittingDoll 258, the login burst about 161
   each, 0x132 134); 23 sub-commands unhandled; 160 dead writers; 3 server ops without a name (0x010D, 0x0111, 0x0132).
   Capture counts include the 24 GMS files among the 27 duplicates.
5. **Content with no original server data.** 268 AI paths (runtime), GMS event rows, GMS-era monster drop rates, the
   4 drop boxes. KMS-only content has its server rows (the server line is KMS); what it lacks is client data in the
   pack. An older plan says "KMS-only NPCs lack server tables (U-12)": U-12's scope is to be
   settled against this.
6. **Provenance.** The ingest reads community edits; the originals sit beside them (section 1). Data outside the
   ingest that both modes read: `summon-npcs.json` (531 NPCs, 1,857 summons, skill odds), `defensive-effects.json`
   (72 effects), `offline-spawns.json` (2), `offline-triggers.json` (1 patch). Their generators
   (`tools/data/summon_npcs.py`, from the nightly's `Xml.m2d`, and `defensive_effects.py`, from the NA client's) are in the repository,
   and `--check` proves the shipped files are their output (FT-05). The Meret catalog is source data: the script
   that first built it was never committed. `organize_meret_market.py` (A7, A21, A25) and the one-time edits MM-02, NM-01 and
   MK-01 change it in place (ADAPTATIONS A6).
7. **Evidence not wired into tests.** The captures can serve as golden decode tests (SkillUseFailed, Ophelia, Ping),
   shape replays (quest, shop, mail, housing) and timing (Ping 9.55 s, NPC control 0.080 s); the Lua bytecode can run
   against the C# port over input grids.

### The 56 Lua functions with no port

Maple2.Lua 2.0.1 exposes 109 formula names; 53 are in `Lua.cs`, 4 of those under another spelling (`CalcRevivalMerat`, `StaticValueAddwap`, `ConstantValueAddwap`, `ConstantValueAddndd`). None of the 56 below has a caller in the server. Each row names the code that does that job today, at Maple2 `da7c4cf`. The match is by the function's name (inferred: the formulas' inputs were not read), and "none" means no such code was found.

| Functions | Site today | What it does instead |
|---|---|---|
| CalcPlayerDamage | `DamageCalculator.cs:13` | its own formula (FT-26a) |
| CalcPlayerAttackSpeed | `AnimationManager.cs:27`, `:221` | reads AttackSpeed, which nothing writes, so it stays 1 (FT-26a) |
| CalcNpcAttackSpeed | `AnimationManager.cs:27`, `:221` | the same |
| CalcMoveSpeed, CalcRMoveSpeed | `MovementState.Walk.cs:42`, `AnimationManager.cs:26`, `:220` | a percent of the data's speed; AnimationManager's MoveSpeed stays 1 |
| CalcJumpHeight | `Stats.cs:96`, `StatsPacket.cs:78` | sends the raw stat |
| CalcPlayerOffenseScore, CalcPlayerDefenseScore | none | - |
| CalcAvailableSkillPoint | `ConfigManager.cs:313` | adds points per source, no total by level |
| CalcItemMergeMaterialCount, CalcItemMergeRequireMeso, CalcItemMergeRevertMeso | `ItemMergeManager.cs:87-120` | server table ItemMergeTable; no revert found |
| CalcMarketRegisterFee, CalcMarketSaleFee | `MesoMarketHandler.cs:212`, `MeretMarketHandler.cs:128` | a tax constant; the fee the client sends |
| CalcMarketSellPriceMinMaxMusicScore | `MeretMarketHandler.cs:160` | clamps to UGCShopSellMinPrice and Max |
| CalcUgcShopBuyCost, CalcUgcShopRegisterCost | `MeretMarketHandler.cs:128`, `:191`, `:384` | the fee the client sends; the item's price |
| CalcBlueprintCreateCost | `RequestCubeHandler.cs:687` | UgcDesignTable CreatePrice |
| CalcPetEnchantBookCost, CalcPetEnchantCost, CalcPetEnchantExp, CalcPetExp | `PetHandler.cs:127` | HandleFusion only reads the packet |
| CalcPetEvolutionCost, CalcPetEvolutionResultLevel | `PetHandler.cs:145`, `:149` | HandleEvolve and HandleEvolvePoints only read the packet |
| CalcTamingPetRank | `BattleState.cs:95`, `AiState.cs:782` | a taming point, no rank |
| CalcGuildFundMax | `GuildManager.cs:248` | sets the funds, no maximum |
| CalcGuildSkillCost | `GuildHandler.cs:653` | HandleUpgradeBuff only reads the packet |
| CalcCraftRecipeRequireItemCount | `MasteryHandler.cs:100` | the recipe's RequiredItems |
| CalcGatheringActionCount, CalcGatheringRecipeRewardItemCount | `MasteryManager.cs:147-164` | MasteryRecipeTable |
| CalcManufactureCompleteByMeret | none | - |
| CalcEnchantExpGainExp, CalcEnchantExpGainExpNew | `ItemEnchantManager.cs:21`, `:266`, `:417` | the GainExp array |
| CalcEnchantRepairMeret | none | - |
| CalcUnlimitedEnchantItemLevel | `ItemEnchantManager.cs:577` | Core.Formulas.LimitBreak |
| CalcOHItemAdditionalLevel | none established; the dual-wield TODO at `StatsManager.cs:284` may be it | - |
| CalcLapenshardLevel | `LapenshardHandler.cs:108`, `:124` | LapenshardUpgradeTable |
| CalcItemSocketTransferPrice | none: `ItemSocketHandler` has no transfer command | - |
| CalcGetExtendCapacity, CalcGetExtendCapacityPrice | `ItemInventoryHandler.cs:77`, `InventoryManager.cs:566`, `StorageManager.cs:199` | InventoryExpandRowCount, InventoryExpandPrice1Row, StoreExpandPrice1Row |
| CalcGetItemRemakeIngredient | `ChangeAttributesHandler.cs:65` | calls CalcGetItemRemakeIngredientNew |
| CalcMeretSkinItemBreakRefundPercent, CalcMeretSkinItemBreakReward | `ItemDismantleHandler.cs:29` | no refund formula |
| CalcFameCompletionTicketCount | none; `QuestHandler.cs:310` only enters the fame area | - |
| CalcQuestRewardExpFactorRateByRank, CalcQuestRewardFamePoint | `QuestManager.cs:427` | the data's reward.Exp, no fame |
| CalcSurvivalAdventureRate, CalcSurvivalCharacterExp, CalcSurvivalCoin, CalcSurvivalExp, CalcSurvivalMatchingInfo, CalcSurvivalObservingAdventureRate, CalcSurvivalObservingCharacterExp, CalcSurvivalObservingCoin, CalcSurvivalObservingExp, CalcSurvivalRating | none: `SurvivalManager.cs` holds the level, exp and medals, and no code ends a match | - |

## 4. Highest value per effort (a solo offline player)

| Rank | Gap | Why (measured) | Effort |
|---|---|---|---|
| 1 | Quest and mission relative EXP | 2,650 quests (raw GMS XML) give their relative EXP as nothing; every daily mission | S |
| 2 | Kritias field puzzles and Hidden Realm portals: time-event types 2, 6, 7, room_random, `WaitSecondsUserValue`, `TimeEventLifeTime` | 27 field puzzles never start; portals on 181 maps run on an invented schedule | M |
| 3 | Monster EXP by rank | 719 elite and boss NPCs give common EXP (x2.3 to x36 too little at level 60) | S-M |
| 4 | Mobs heal and reset when they lose their target (PeaceRestore) | true on 28,523 of 28,565 spawn points; builds on AI-05 | S |
| 5 | The dungeon room timer, its time changes, the revival limit, and the fail and timeout triggers | 36, 35 and 21 calls; chaos raids carry 15-minute timers and 3 revivals | M |
| 6 | SkillUseFailed fields; Ophelia fail type and catalysts | captured golden packets exist | S |
| 7 | Prestige level abilities, prestige EXP, lapenshards | tables and 1,150 items loaded, no effect | S-M |
| 8 | ShowVibrate and HideVibrateAll | Balrog (a named boss) and 9 other AIs; BR-01 gives the vibrate semantics | S |
| 9 | Player damage formula against the original Lua (with R3-F) | gear stats add nothing to attack (1.2.2 report) | M |
| 10 | NPC scaling tables | 9 raids and 6 chaos raids change solo; overlaps RFF, which ADAPTATIONS does not list yet | M |
| 11 | Equipment rarity by NPC rank; the drop TODO fields | 3,960 rows unused | M |
| 12 | The player-skill sensors (Ice Cream Time, Little Meteor) | 61 sensors, the FS-01 follow-up | S |
| 13 | Login-burst packets, CharacterAbility and NameChange | sent once per login (about 161 each across the 174 captures); a dead dialog and voucher | M |
| 14 | Lua ports | 56 functions unported; the 53 ported are on grids, BT-201 to BT-205 open | M |
| 15 | Dungeon rank, mission and score rewards; random rooms in dungeons | every dungeon clear screen | M |
| 16 | Revival rules (`infinityMeratRevival`, `recoveryFullHP`, the Meret revival limits) | 66 and 14 maps | S |
| 17 | Map entities (SafeZone, RegionSkill friendly, moving breakables, UGCReturnPlace) | 184, 690, 195, 375 | M |
| 18 | Quest EpicRestart | 748 quests | S-M |
| 19 | Event calendar (M9 T-04) | 3 of 1,615 events reach the DB | L |
| 20 | Pets, guild core, mini games | no pet exp; 23 guild no-ops; OX, survival, arcades | L-XL |

## 5. Not checked

KMS capture payloads; the 434 nested captures (unpacked by BR-01, not tallied here) and the 12.8 GB Doll Mansion zip;
whether every writer counted as fed actually runs in play; JMS, CMS and TW `Exported.m2d` counts; the class-to-column
mapping of monster EXP and the level relative quest EXP uses (inferred); whether the client enforces each map limit
itself; the pack DB (runtime counts come from the base DB); the quest counts are raw XML, not feature-filtered. KMS v81
layouts may differ from GMS 2020 by version rather than by bug.

## 6. Re-measured on 1.2.3 (FT-06)

Measured 2026-11-08 by FT-04's fidelity registry (BehaviorRegistryTest writes `fidelity-registry.csv` and
`FIDELITY_REGISTRY.md`), not by this map's four inventory passes. The code is the 1.2.3 release line. Four DBs: before RB-02, the 1.2.2 base (main's SaveData until 2026-11-08) and pack 1.2.2-dev.b3g (BA-14);
after RB-02, the RB-02 base (main's SaveData since) and pack 1.2.3-dev.b4b. The baseline is FT-04's commit (Maple2
`a1866a16`) on the 1.2.2 base. The four DBs give the same CSV, 496 rows. RB-02 re-imported no table the counts read
(triggers, AI, NPC AI paths, quests, trophies), so every change below is code. The registry counts the DB's trigger
rows with a rule on the compiled code, so its trigger numbers differ from section 2's raw-XML count. Row 7 gave 92
functions and 3,174 calls at `98340567`; the registry gave 94 and 3,238 at FT-04. Section 2's rows and percentages are
not re-judged here. The 1.2.3 tasks that change them: FT-11, FT-12, FT-13a to FT-13c, FT-14, FT-14b,
FT-15a, FT-15b, FT-17, FT-18, FT-19, FT-10a, FT-10b, FT-26a, FT-26b, SE-01a to SE-01e and SE-02.

| Count | FT-04, 1.2.2 data | 1.2.3, before and after RB-02 | Lowered by (uses) |
|---|---|---|---|
| Trigger stub calls | 3,238: 94 functions, 78 actions with 2,707 calls, 16 conditions with 531 | 3,012: 82 functions, 71 actions with 2,626 calls, 11 conditions with 386 | FT-13a 57 (dungeon_timeout 35, dungeon_close_timer 17, dungeon_stop_timer 5); FT-10b 34 (wait_seconds_user_value); SE-02 10 (dungeon_first_user_mission_score); SE-01c 125 (shadow_expedition_close_boss_gauge 19, shadow_expedition_open_boss_gauge 12, shadow_expedition_points 32, score_board_create 10, score_board_remove 16, score_board_set_score 2, score_board_score 34) |
| Empty AI node uses | 104 in 6 nodes | 28 in 3 nodes | FT-13a 13 (ModifyRoomTime); FT-18 with NB-28 63 (ShowVibrate 36, HideVibrateAll 27) |
| NPCs without an AI file | 310 (271 paths) | 310 (271 paths) | - |
| RecvOps without a handler, with captured traffic | 7 | 7 | - |
| SendOps never sent, with captured traffic | 31 | 29 | FT-13b (DungeonReviveCount, 46 captured packets); SE-01c (ShadowExpedition, 306) |
| Tables never read | 7 | 5 | FT-12 (MonsterExpTable); SE-02 (DungeonMissionTable) |
| Condition types never reported | 99 (723 quest and trophy conditions) | 99 | - |

What is left, by uses. Traffic is captured packets in the 572 unique GMS files (`capture-opcode-tally.tsv`).
- Trigger actions doing nothing: add_effect_nif 1,353, field_game_constant 308, remove_effect_nif 129, wedding_move_user
  108, npc_to_patrol_in_box 96, and 66 more.
- Trigger conditions doing nothing: check_same_user_tag 128, wedding_mutual_agree_result 72, wedding_entry_in_field 60,
  dungeon_state 37, dungeon_round 23, pvp_zone_ended 19, guild_vs_game_scored_team 18, dungeon_max_user_count 17,
  is_playing_maple_survival 6, bonus_game_reward 3, guild_vs_game_winner_team 3.
- Empty AI nodes: ride 24, CreateInteractObject 2, CreateRandomRoom 2 (FT-22).
- RecvOps without a handler: Pong 14,633, Ah 2,123, SyncWorld 119, FinalizeReconnect, Reconnect and ReconnectUnk 11 each,
  RequestServerList 6.
- SendOps never sent, the largest: Ping 14,426, Ah 4,142, Reconnect 1,557, Pvp 1,546, Gvg 1,047, BindItem 909. The login
  burst: SyncWorld and UserConditionEvent 520 each; CharacterAbility, MaidCraftItem and UserMaid 508 each (FT-25). Cash 520.
- Tables never read: DungeonRankRewardTable, IndividualItemDropTable, SeasonDataTable, ShopBeautyCouponTable,
  SurvivalSkinInfoTable.
