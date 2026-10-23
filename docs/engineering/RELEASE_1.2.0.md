# Release 1.2.0: playability (published 2026-10-22, GitHub release v1.2.0)

Replanned 2026-10-21 (maintainer: "it will focus on playability like reverse fair fight, debug log instead of adding
content and new content now gradually get add instead of all at once"): 1.2.0 brings Reverse Fair Fight, problem
reports and playability fixes, and only the first batch of the unified content (cosmetics). The rest of the unified
content follows in weekly batches, safest first (section "Content batches"). The maintainer's play matrix decides the
release. Earlier plan (the whole pack in 1.2.0): this file's history and PLAN_1.2.0_UNIFIED_CONTENT.md.

## Candidate
- Servers and launcher: 1.2.0 (the Offline Black Market, the M7 world-system fixes, boss HP holds,
  Reverse Fair Fight at floorShare 1.0, BT-18, opt-in problem reports on Windows; Linux in progress; after the play
  matrix: class drops CD-02, Meret from monsters MP-01 and CO-01, Meret Market at normal prices MM-02 and MM-03, Chaos
  Onyx CO-01, README MP-02 and MP-04, the launcher window hotfix LH-01, Navigator dismantling ND-01, Meret Market names
  NM-01 and MM-04).
- Client mods (generated, not in git; shipped from client-mods): offline-dismantle (184 Navigator items,
  tools/build_dismantle_mod.py), offline-shop string/itemname.xml and string/koritemdescription.xml (restored names),
  offline-dungeons and offline-shop tables moved from table/na/ to
  table/.
- Client patch: the 1.1.9 build (2026-10-17). The client-patch rebuild (BT-14: MinHook built from source, guarded hooks)
  waits for the next patch (maintainer 2026-10-21).
- Content pack: 1.2.0-dev.b1 = batch 1 (cosmetics) of selection v5, built by BA-02 with the batch-aware gates
  (`pack_v2_build.ps1 -Batches 1`): every gate ok, 7 archives, 421 MB.

## What's new (player-facing draft)
- Reverse Fair Fight (Adapted mode): alone against a raid or field boss, your hits never fall below a floor that lets a
  modest character finish a raid boss in about 10 minutes; gear still matters. Off with FairFight=false.
- Boss phases hold: a boss's health stops at each phase threshold until its next phase starts, so a big hit no
  longer skips a phase (the holds never worked before).
- Boss fights move on: in 22 maps the map scripts that wait for a boss's health to drop (adds, doors, the next phase)
  now see the real health; before, every boss counted as at full health and those steps never came.
- The right boss: raids spawn their raid boss (not the chaos-raid one), and rooms with a 6-person and a 10-person or a
  normal and a hard version spawn the boss of the version you entered. Dark Stream rounds have their monsters again
  (each round ended at once with no fight). The Colosseum (Queen Bean Arena) can be played: every run used to fail at
  round 1; talk to the host to start a run.
- Quests: alliance-board quests can be accepted; quests that map scripts start or complete now start and complete;
  dungeon clears count for the "clear with a rank" quests (the Sky Fortress side quests).
- Problem reports (opt-in): after a session with a problem the launcher can send the developer a report, only when you
  say yes or tick "Send problem reports to the developer automatically"; it shows what a report contains first, and
  your names, chat and save are never sent. "Report a problem..." sends your own words with it.
- Meret from monsters (Adapted mode): monsters drop Meret by their level (1-10 below level 50, 1-100 from level 50);
  bosses always drop 1-1,000 Meret, often 1-500 more and sometimes another 1-1,000. Picking it up adds it to your
  Meret, no pouch to open.
- Meret Market at normal prices: the shop showed most items as free (the server still charged their price) and the
  rest at the end-of-service sale prices (three even charged more than their list price); every item now shows and
  costs its list price, and items without a price of their own cost the price of their rarity, 300 to 5,000 Meret.
- Chaos Onyx: dismantling Epic or better gear always gives Chaos Onyx Crystals (Epic 1-3, Legendary 2-6, Ascendant
  3-9), and in Adapted mode every boss drops 1-10 of them. Dismantling many items at once no longer gives a very large
  total several times. (Navigator) gear from the level reward boxes can be dismantled.
- Meret Market names: about 230 items showed a blank name, a code like ITEMNAME_..., or no description; they show their
  names and descriptions again. Items the game cannot show (wedding and mentoring items, empty rows) are no longer
  listed. The Design Templates and Furniture tabs appear (their tab list was never loaded).
- Dungeon Directory (Adapted mode): the daily and weekly clear limits show 9999 instead of 15 and 30 (the client mod's
  table was never loaded).
- Class drops: with ClassDropsOnly (on in Adapted mode), monsters and dungeon rewards drop only weapons made for your
  class; before, every class's weapons dropped, because any class can hold them.
- Offline Black Market (Adapted mode): the Black Market is no longer empty. Every Play lists a new pick of 60 level 50-70
  weapons, armor and accessories at three times their sell price; what you buy comes by mail with its stats rolled on
  purchase, and a purchase lowers the item's remaining trades like a trade does. Off with BlackMarket=false. The name
  search now ignores upper and lower case, and listing prices must stay within the game's limits. (All its items are
  Global items: it needs no content pack.)
- New cosmetics from the Korean client (batch 1 of the unified content): the launcher adds them on Play and removes them
  byte-exact when you turn UnifiedContent off; your Steam files are never changed for good.
- Save profiles: "New game..." starts a fresh save next to your main one. Its name box always opens in front of the
  "Choose how to play" window (it could open behind it, and the window could then be neither clicked nor moved).
- Launcher windows: the start-up window can be dragged and no longer stays in front of other programs; neither does
  "Choose how to play".
- Deleting a character removes its quests, achievements and items from the save.
- The mini-game portals (every 3 hours) open on schedule again and are announced when you log in during one;
  event progress is no longer wiped at login while its event is not running.
- Safer saves: a session without the content pack (or after removing it) keeps the pack's items, placed furniture and
  mail attachments for when it is back; taking an item out of the storage or a pet's bag into a full inventory is
  refused instead of losing the item; an expiring outdoor home stores its furniture for you.
- Diagnostics: session reports name known problems (TROUBLESHOOTING.txt) and show recent trigger states, and they name
  each map-script function the game met that is not implemented offline yet (once per map).
- Linux launcher: applies the content pack like Windows; installs only downloads listed in the signed release list.

## Content batches (weekly from 1.2.0)
| Release | Batch | Content (from selection v5) |
|---|---|---|
| 1.2.0 | 1 | 2,666 cosmetics (outfits, faces and other look items) and the 61 pack skill files no later batch needs |
| 1.2.1 | 2 | 209 weapons, armor and accessories |
| 1.2.2 | 3 | 1,011 other items (furniture, pets, mounts, consumables), the NPCs of 2 pets and 62 more skill files |
| 1.2.3 | 4 | the pack's 186 quests and 128 NPCs, their 8 items and the skills of those NPCs |

Every pack build also has what the nightly data gives through the extra features: 40 quests, 61 NPCs, 16 maps and
8 items beyond 1.1.9 (CONTENT_REGISTRY.md; they were in every pack so far). A batch build checks that nothing it ships
names what it leaves out, except the shop lists (the game skips items it does not have) and what 1.1.9 already named.

Each batch: built with every pack gate on top of the earlier batches, checked in the game client (harness), its rows
in the content registry, release notes, and the problem reports of the previous batch read first. Saves keep the items
of later batches safe (pack items stay in the save when the pack is off).

## Known limitations
- The Global client never offers the tutorial skip (PC-3 finding; the server side exists).
- Bosses that summon their next phase (Gigantica) stop at the first phase change: the summon step is not
  implemented yet (BT-19; after 1.2.0, maintainer 2026-10-21).
- 4 dungeon rooms name a clear-reward box no drop table has (BT-21); quest 10003399 is never offered (BT-20, Global data).
- Shadow Expedition waves and score-board missions end as soon as they start, without their rewards: their point
  counters are not implemented yet (BT-26).
- Korean, Japanese and Chinese text and voice selection come later; JMS/CMS-only content after the KMS batches.
- No day/night cycle: the Global client shows none (checked 2026-10-20). The original seasonal events are not
  active offline yet.
- With batch 4: the Epic Apocalypse chain (quests 10003455 onward) needs instance maps the data does not have; 14 pack
  items keep a Korean name; a few pack items are named differently in the client and in server messages.

## Checks
| Check | Result |
|---|---|
| Global records unchanged in the pack data (row compare with 1.1.9) | pass for m2f; batch 1: the pack db = the full build minus exactly the left-out content, no reference to it (build gates) |
| Data sweep: no new error on a Global id | pass for m2f; batch 1: Data tests 10/10 (PackContentTest at the batch-1 totals) |
| Save compatibility (pack off, pack back) | pass: SaveSafetyTest, PackHousingMailTest |
| Pack in the real client (harness) | pass for m2b to m2e; batch 1: a batch-1 mask (11020411) equips, a batch-2 earring (11200106) shows as Invalid Item, as planned |
| Offline Black Market (tests and harness) | pass: OfflineMarketTest, EconomyValidationTest; 60 listings at World start, a purchase paid 7,395 mesos and mailed the item with rolled stats, no seller mail |
| Problem reports (tests, two privacy reviews, screenshots) | pass on Windows; Linux open; the service is live (DR-06, 2026-10-21: https://ms2-reports.ms2-reports.workers.dev, ready) and both launchers point at it; end to end 2026-10-21: two "Report a problem" sends from the candidate's copy arrived, fetch_reports.py read them with the password and wrote the digest with both notes, no user, PC or character name in them; in the game a session report names the unimplemented map-script function met (DR-07, PC-5) |
| Dungeon run checks (V-03) | done 2026-10-21, Adapted and Legacy: every sample room enters; clears seen: chaos raid 23010003, fame challenge 24094005; rooms that need clicked objects or quest steps are in the play matrix; found BT-23 (fixed) and BT-22. PC-5 (2026-10-21, with /interactall and the command inbox): 21001001 clears in 99 s, the raid 20004002 clears with its raid boss, Dark Stream 25008001 rounds spawn their monsters and clear; rooms that go on through portals (21007001) stay in the play matrix |
| Main story blockers (PL-01) | pass: fixes merged (QF-01 alliance board, QF-02 quests from map scripts, QF-03 dungeon rank); the in-game checks of QF-01, QF-02 and QF-04 passed in the play matrix 2026-10-21 |
| Quest NPC triage (PL-02) | done: quest registry warnings 275 -> 6; the 2 side quests without an NPC spawn after 1.2.0 (QF-06) |
| Black Market trade count (PL-03) | pass (tests) |
| Map-script stubs (audit V-05, 2026-10-21) | 43 functions only log; fixed: npc_hp (BT-23), dungeon_id, dungeon_level, Dark Stream spawns (BT-24), Colosseum rounds (BT-25); known: BT-26; each stub met is logged once per map for problem reports (DR-07) |
| Play matrix (maintainer) | pass 2026-10-21 on the candidate copy: QF-01, QF-02, QF-04, BT-23, the Colosseum and 21007001 through its portals all cleared (some behavior janky, for a later patch); found: dungeons drop gear of every class (CD-02); asked for Meret grinding (MP-01) and normal cash-shop prices (MM-02) before publishing |
| Release files: Setup, update zip, content-pack zip, signed release.json | candidate rebuilt 2026-10-22 with the update key after the maintainer's third test (hotfixes LH-01, MM-03, CO-01, MP-04, ND-01, NM-01, MM-04 and the client-mod path fixes; client patch of 1.1.9; the earlier candidates are in the quarantine): Setup 1,079 MB, full zip 1,009 MB, update zip from 1.1.9 853 MB (280 files changed, 4 deleted: the 2 retired files and the 2 unserved table/na/ mod files); sizes, sha256 and both signatures checked against the launcher key; release scan clean; the mod-path check serves all 5 mod tables and strings in the release and the test copy; before them: Setup 1,077 MB (also as MapleStory2-Offline-Setup.exe, listed in release.json), full zip 1,008 MB, update zip from 1.1.9 852 MB (91 files changed, 2 retired files deleted, content pack inside); every listed size and sha256 and both signatures checked against the launcher key; release scan clean; file by file against the first candidate (`900aae7`) only the server build, offline-grind.json, offline-meret-catalog.json, README, the manifests and the launcher build differ; maintainer's test copy refreshed (saves kept); not published. A first rebuild had picked up the uncommitted client-patch rebuild of the main checkout; it is in the quarantine |
