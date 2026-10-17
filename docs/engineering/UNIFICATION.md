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

All three `Xml.m2d` archives open with `Maple2.File.Parser` (`M2dReader`) without error (measured).
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
