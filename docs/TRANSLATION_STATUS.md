# Translation status: the baseline's four classes

TL-01's check (`tools/data/translation_check.py`, table `tools/data/translation-baseline.tsv`) tracks the text the client
shows from its string files, the pack's file where the pack ships one and Global's otherwise. This note says what the
10,134 rows of the baseline are (pack 1.2.3-dev.b4b, RB-02's build; English is the only language the pack ships), which of
them a player sees, and what fixes each kind. It is its own file, not a section of `docs/REGIONAL_RULES.md`, because
the rules change rarely and this note is rewritten at each pack build.

How the rows were read: every row against the pack DB (`GameData/maple-data.db`) and the base DB (Global's), to tell an id
that exists in the game from one that only has a string line, and against the file's other lines for the same id (a key line
can be shadowed by a real one). Counts are exact; "seen" means the text can reach a window of the client; nothing here was
checked in the client (the game is paused).

| Class | Rows | Defects a player sees | Not seen |
|---|---|---|---|
| key (a string key instead of text) | 9,626 | about 9,000 (quest talk lines, 194 Global item names, a few titles) | about 600 |
| missing (an id with no name) | 253 | 0 known | 253 |
| script (Hangul) | 241 | 223 | 18 |
| placeholder (a `$` outside a token) | 14 | 13 key-like UI lines | 1 |

## 1. Key-like lines (9,626)

A line whose text is the string's own key (`SCRIPTQUEST_0831174302000023_NAME`, `ITEMNAME_11301272_NAME`, `NPC 44200001`).

| File | Lines | Whose | What they are |
|---|---|---|---|
| scriptquest_epic.xml | 5,585 | pack's copy of Global's | quest talk lines |
| scriptquest_world.xml | 2,526 | the same | quest talk lines |
| scriptquest_famemission, _tutorial, _guide, _famefield, _item | 279, 233, 90, 50, 5 | Global's | quest talk lines |
| itemname.xml | 463 | pack's copy of Global's | 269 ids with no item record in either DB; 194 Global items |
| itemnameplural.xml | 133 | the same | 8 ids with no record; 125 Global items |
| shadowbuffstrdesc, shadowbuffstrname | 200, 10 | Global's | shadow buff texts, every line behind a feature gate |
| npcnameplural.xml | 23 | Global's | plural NPC names |
| npcname.xml | 7 | pack's | 6 ids with no NPC record (25031001, 44200001 to 44200004, 61200002); 1 shadowed |
| others | 22 | Global's | titlename 4, stringrideename 6, npctitle 2, stringinteractobjectaction 2, stringguide 2, one each in npcfunctionname, scriptnpcshop, setitemname, skillname, stringarcadedesc, stringarcadename |

Examples (5): `scriptquest_epic.xml` 0831174302000023 `SCRIPTQUEST_0831174302000023_NAME`; `itemname.xml` 11301272
`ITEMNAME_11301272_NAME` (a hat); `itemname.xml` 11401206 `ITEMNAME_11401206_NAME:[F]Event`; `titlename.xml` 10000501
`TITLENAME_10000501_NAME`; `stringrideename.xml` 99910391 `STRINGRIDEENAME_99910391_NAME`.

Which a player sees:
- Quest talk lines (8,768): every English client holds them as keys (Taiwan's English client too: 7,327 key lines in its
  `scriptquest_epic.xml`), so the original Global game showed them. The speaker is named in the line's `npc` attribute
  (Korean) and the text exists only in Korean. Seen when a quest conversation reads the line; which quests that is was not
  traced. Not caused by the pack. The batch 4 quests carry their own English (BA-15b) and are not among these.
- 194 Global items whose only name line is a key: the DB names them in English (all 194). 22 of the lines have no
  feature gate (11301272, 11701250, 11701251, 12200553 and 18 more): their names read as keys. 170 are gated (`:[F]Event`,
  `[F]Level70Item`): whether the client reads them depends on the feature. 2 have a real line too.
- 125 Global plural names, 23 NPC plural names: shown only where the client words a count with the plural; low.
- 4 Global titles (10000501, 10000502, 10000510 ungated; 10000786 gated) and 6 ridee names (99910391 to 99910395, 99910401): seen in the title list and on
  mounts; Global's own gap.
- Not seen: 269 item and 8 plural ids and 6 NPC ids and 2 NPC title ids (11004618, 11004619) that exist in no DB (string lines of
  items and NPCs the game does not have), the 210 shadow buff lines (feature gated, none on), and one-line gated rows.

What would fix each kind:
- The 194 items and the 125 plurals: add an ungated key line with the DB's English name, the way `shown_names.py --apply` does for
  shipped ids; it covers Global ids too, because rule 1 of `REGIONAL_RULES.md` lets a key name be replaced. A pack-build step, a
  table of 194 + 125 rows; no new source is needed. The Global titles and ridee names need a reviewed name each (D-16).
- The quest talk lines: translate from the Korean text in KMS's string tables into a reviewed table and overlay the five
  `scriptquest_*` files as `batch4_quests.py` does for batch 4; 8,768 lines is a translation project, not a script change, and
  needs the maintainer's call (it is larger than anything the pack translated so far). First step: trace which quests read
  them, so only the shown ones are translated.
- The ids with no record: nothing to fix; the check can skip an id the pack DB lacks (it needs the DB path as an argument), which drops
  about 290 rows of noise from the baseline.

## 2. Missing names (253)

The pack's `itemname.xml` and `stringinteractobjectaction.xml` have an id whose every line lacks a name. 252 are item ids
(`10400020`, `10400024`, `11300182`, `11300547`, `11300548`, ...): none exists in the pack DB or the base DB, so no
item shows them; the lines come from the nightly's string file, which names items of other regions that the pack does
not ship. The 253rd is interact object action 12000010 (no DB table holds objects; whether it ships is not checked).

Seen by a player: none known. Fix: none for the item lines; D-16 asks a name for what the pack ships, and these are not
shipped. If an item of the list is added later, `shown_names.py` stops the build until it has a name. For 12000010 look the
object up in the map entities of the pack DB; if a shipped map holds it, give it a reviewed row.

## 3. Hangul lines (241)

| File | Lines | What |
|---|---|---|
| setitemoption.xml | 200 | set bonus lines of 90 set ids (`STR 10 증가`, `물리공격력 10 증가`, `스킬 효과 10% 증가`) |
| koritemdescription.xml | 38 | tooltip, guide and main descriptions of 18 items |
| itemname.xml | 2 | 30001318 (`따`), 30001319 (`뜻`): no item record |
| korflash.xml | 1 | `$s_guild_ui_vsGame_info_desc1`: "the dance event is being prepared" |

Examples (5): setitemoption 15123101 partCount 2 `STR 10 증가`; koritemdescription 20001135 (a Christmas apple; tooltip, guide and main
description); koritemdescription 31002126 to 31002139 (the 14 enchant scrolls whose plural names U-10 fixed; their descriptions are still
Korean); koritemdescription 40100021 and 40100034; korflash `$s_guild_ui_vsGame_info_desc1`.

Seen: the set bonus lines (79 of the 90 set ids are on items of the pack DB, so the set tab of those items' tooltips reads Korean), the 18 item
descriptions (tooltip and guide window), the korflash line (a guild window page). Not seen: the two item names (no record).
All are Global's own files, shown in Korean by the Global client as well.

Fix: a reviewed translation table in the style of `tools/data/plural-names.tsv`, applied to overlay copies of the two files:
`setitemoption.xml` (200 short formula lines: stat, amount, "increase" becomes "+10"; they can be written by rule and checked by hand)
and `koritemdescription.xml` (18 items, 38 lines, written by hand; the enchant scroll texts share one pattern). `copy_names.py --apply`
rewrites `name` only, so the apply step needs the attribute as an argument (a small change). The korflash line is one translation.

## 4. Broken placeholders (14)

13 are `korflash.xml` values that are keys with a `$` in them (`KORFLASH_$S_GAMEMENU_POPUP_COUPLEDANCE_VALUE`,
`KORFLASH_$S_BURNING_LEVELUP_EVENT_DESC_BTN_VALUE:[F]Event`, `KORFLASH_$S_COUPLE_DANCE_EVENT_DIALOG_TITLE_VALUE`, ...): the game menu
and event windows of the couple dance and burning events. They are keys, not broken tokens, so they belong to class 1. The 14th is
`banword.xml` 337 `ar$chl0ch`: a banned word, written with a `$` on purpose, never shown.

Seen: the 13 UI keys when the couple dance or burning event windows open (the events are Global's own and gated by `[F]Event`).
Not seen: the banned word; it is no defect.
Fix: the 13 need an English line each (D-16), as the capsule window's keys of BT-111 do (an open row). The check should treat a
`$` inside a banned word as no placeholder: skip `banword.xml`, or change the baseline's row to a known exception.

## For the bug tracker

The notes and decisions name no layout defect of the translation (no text that is too long or wrongly wrapped). What they name,
as rows to open or keep:
- BT-111 (open): the capsule window shows `$s_item_gachabox_title` and `text`; the same class as the 13 korflash keys here.
- BT-194 (open): 8 literal Korean lines in `set_cinematic_ui` on 52010068_qd, the closing scene; not in string files, so this check does not see them.
- New: the 18 items of `koritemdescription.xml` (31002126 to 31002139, 20001135, 20001136, 40100021, 40100034) read Korean in their tooltips.
- New: the 90 set ids of `setitemoption.xml` (79 on items) show Korean set bonus lines.
- New: 22 Global items (11301272, 11701250, 11701251, 12200553, ...) and 4 titles show string keys as names.
- New: the quest talk lines (8,768) read as keys in every English client.

## What cannot be done now

The maintainer's marks on U-10's 60 rows (Q-U10-1) do not exist yet: the sample has 0 marks. Applying
them is a table edit and a pack build and waits for the answer.
