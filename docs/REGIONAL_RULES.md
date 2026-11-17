# Regional content: the rules

MapleStory 2 Offline plays the Global (NA) game. The content pack adds what the Korean (KMS) and other regional clients
have and Global lacks. This document keeps the rules that decide how a regional record joins the game, so that each
release builds the pack the same way. Each rule has its reason, the place that enforces it and the test that fails when
a pack breaks it.

The pack tests run on the pack's data DB. Set `PACK_DATA_DB` to the pack's `GameData/maple-data.db` (and `MAPLE_DATA_DB`
to the base data DB, Global's, for the tests that compare the two); without them the tests skip. The tests of this
document that are new in 1.2.4 are in `Maple2/Maple2.Server.Tests/Data/RegionalRulesTest.cs`. Run them with
`dotnet test Maple2/Maple2.Server.Tests/Maple2.Server.Tests.csproj -c Release --no-build --filter FullyQualifiedName~RegionalRulesTest`.

## 1. Global stays as it is

Rule: a record whose id Global has keeps every field it has in Global's data. The pack adds records; it never changes or
removes one of Global's. The one exception is the name (rule 4): a Global name that is blank, a string key or Korean is
replaced, and two reviewed names that name the wrong slot are corrected (11620059 and 11620060, NM-04).

Why: saves, quests, shops and drops of the Global game rely on those records, and the maintainer's goal is Global plus the
unique content of the other regions, each piece once (decisions 2026-10-19; D-9, D-15).

Where enforced: the merge adds KMS records only for ids Global lacks (`docs/engineering/UNIFICATION.md` section 4;
`tools/KmsSplit`); `tools/data/copy_names.py` and `tools/data/shown_names.py` change names only, and `FIXES` in the first
is the list of reviewed corrections. Shared tables (`gacha_info`, the drop tables, the shops) get rows added on purpose
and are held by rule 5.

Test: `RegionalRulesTest.GlobalRecords_StayAsTheyAre` (every column but the name of every Global item, NPC, map, quest,
skill, achievement, pet, ride, script and function cube is the same in the pack, and none is gone) and
`RegionalRulesTest.GlobalNames_ChangeOnlyWhereGlobalShowsNone` (a Global name differs only where Global's was blank, a
string key, Korean, or one of the four reviewed fixes). New.

## 2. A duplicate of a Global record is found by fingerprint and not added twice

Rule: a KMS item that is the same item as one Global (or another pack item) has is a duplicate and is left out. Same item
means the same fingerprint (look, class, level, kind) and the same name (lower case, letters and digits). Variants stay: an
item with another rarity, duration or trade setting than its twin is no duplicate (maintainer, 2026-10-20, Q-M2c-4). Only a
duplicate that nothing refers to is dropped; one of a group of duplicates stays.

Why: the pack should carry each piece once; two items with one look and one name confuse the player's inventory and the
shops.

Where enforced: `tools/data/unified_selection.py` writes the judgement to `tools/data/unified-selection.tsv` (id, name,
fingerprint, verdict, decision keep, drop or hold) and the pack build ships only the items it keeps; `tools/data/unified-hold.tsv`
lists the items held back for a later release. The decisions of 2026-10-19 to 2026-10-20 (M2c-04, M2c-06).

Test: `RegionalRulesTest.FingerprintDuplicates_AreNotAddedTwice` (every id the selection keeps is in the pack's item table
and none it drops is). New. The fingerprint itself is computed from the client's item files, which the pack DB does not hold, so
the test holds the recorded judgement against the pack, not the fingerprint.

## 3. KMS records get their own homes: ids, tabs and categories

Rule: a KMS record whose id Global uses for another thing ships under its own home id, and it appears in the places
a player looks for it: the right item tab (a KMS potion, lure or voucher goes to the Consumable tab, not to a default one),
and, for the Meret Market, the New category of the release that adds it (maintainer rule: each release's new Meret Market
items go in its New tab, tab 11).

Why: a record under another id would replace Global's; one in the wrong tab cannot be found or used.

Where enforced: ids by rule 5; tabs by `tools/KmsSplit` (subtype of a consumable, IT-06) and the server's inventory tab map
(`ItemTabTest`; an unknown type goes to a safe tab, IT-02); categories by `tools/organize_meret_market.py` and
`tools/data/meret-new.tsv` (`docs/ADAPTATIONS.md` A25).

Tests: already held, not added. `PackContentTest.KmsConsumables_GoToTheConsumableTab` (pack DB: three KMS consumables have
subtype 2) and `MeretNewTabTest.ShippedCatalog_NewListsThisReleasesAdditions` (the shipped catalog's New list is shown in tab
11 and the file keeps each entry's own tab). The ids part is rule 5's test.

## 4. Every pack item, NPC, map and object has an English name (D-16)

Rule: an item, NPC, map or interact object without an English name gets one: a translation of its Korean (or other regional)
name or, if it has none, a new name that fits it. Nothing is held or shipped blank for lack of a name. A name is not
blank, not a string key (`ITEMNAME_123_NAME`, `NPC 123`) and not in Hangul, kana or Chinese script.

Why: the client shows the name in every window; a blank or a key reads as a bug (BT-98, BT-102, BT-110).

Where enforced: `tools/data/copy_names.py` (copies take their twin's name), `tools/data/shown_names.py` with its reviewed
table `tools/data/shown-names.tsv` (it stops when a shipped id has neither a name nor a reviewed row), and the client string
files the pack build writes. `tools/data/translation_check.py` tracks the client's string files per language and fails on a new key, Korean
line, broken placeholder or missing name (TL-01; its state is in `docs/TRANSLATION_STATUS.md`).

Test: `RegionalRulesTest.ShippedRecords_HaveEnglishNames` (no item, NPC or map of the pack has a blank, key or foreign-script
name; the maps 80000000 and 51000006 are Global's own and outside the rule). New. Interact object names are string lines of the
client, not DB rows: `translation_check.py` holds them (its `missing` class covers `stringinteractobjectaction.xml`).

## 5. Colliding ids are resolved by a fixed rule

Rule: where a KMS reference names an id that Global also uses for something else, the reference resolves to KMS's record, and
that record ships under a fixed home. A KMS item that is a re-issue of a Global item (a twin) uses Global's item. A KMS item
that is another thing than Global's of that id is copied under a home id pinned in `tools/data/kms-homes.tsv` (a free id;
verdict `copy`); one the pack does not ship is dropped with its row (`drop`); one that is the same as Global's keeps Global's
(`keep`). A KMS drop box whose id Global uses for another box goes under its home, box id plus 90000. A reached Global id whose
KMS look, function or box contents differ needs a reviewed row: the build stops without it, so no unreviewed collision ships.

Why: before IB-02 a KMS box of a colliding id gave Global's contents (a Super Chat Theme Bundle gave beauty vouchers) and a
KMS prize id gave another Global item (BT-114, D-20).

Where enforced: `tools/KmsSplit` (`KmsHomes`, with the gate); the table `tools/data/kms-homes.tsv`; `tools/data/shown_names.py`
(each copy needs a name row).

Tests: `PackContentTest.KmsBoxes_GiveTheirKmsContents` (the box case: homes of 320090392, 545090004 and the others; already held)
and `RegionalRulesTest.KmsHomes_AreFreeIdsAndShipped` (every copy's home is in the pack and is no Global id, every copy's own id
is Global's, every twin names an item the pack ships). New.

## Which rules had a test

Rules 3 and the box part of 5 had one (`PackContentTest`, `MeretNewTabTest`). Rules 1, 2 and 4, and the table part of 5, had
none; RG-01 adds one test for each. Each new test was run on a pack DB with one record changed against the rule (a Global column
changed, a Global item renamed, a kept item removed, a copy home removed, an NPC name blanked) and failed for each.
