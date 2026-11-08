# Release 1.2.3: 15 character slots, the Lv 75 story, pets that level, the players' reports

Planned from 2026-10-29 from two players' suggestions, the players' session reports of 2026-10-29 to 11-06, a Steam Deck
player's notes and every fix planned for 1.2.3. Published 2026-11-08 without play checks (the maintainer's decision,
2026-11-07): players test the release, and their problem reports arrive automatically.

## Candidate
- 2026-11-08: 1449 server, 199 launcher and 185 Linux tests pass, with the data tests on the shipped base DB and the pack
  tests on the shipped pack DB. Game data: the 1.2.3 rebuild of the base and pack DBs; content pack 1.2.3-dev.b4b
  (batches 1 to 4). Client patch rebuilt (a crash line in its log). Update set from 1.2.2: 75 files change and 2 are
  removed; the privacy scan finds no content hits.
- `MapleStory2-Offline-Setup.exe` 1,169,670,761 bytes, sha256
  `274542adf433f2479c0cd1e8066b2876b295d82ef4da3f564a569dd1b4b54aa2`; `MapleStory2-Offline-Update-1.2.3.zip`
  584,509,733 bytes (needs 1.2.2); `release.json` lists the Setup under both of its names (Linux launchers from 1.2.0
  look up the versioned one) and the update zip.

## Before you update
- Linux: update your launcher by hand once, before anything else, or none of the launcher fixes below reach you: `git -C <clone> fetch origin`, then `git -C <clone> reset --hard origin/main`, where `<clone>` is the folder `git clone` made (or clone it again). From 1.2.3 on the launcher updates itself.
- Linux players on 1.2.1 or older: before you update, copy the folder `Maple2/Maple2.Server.Web/Data` of your program folder (`~/Games/MapleStory2-Offline` unless you chose another) to a safe place. The update replaces the `Maple2` folder, and with it your portraits, item designs, banners, guild marks and blueprints; your save is kept. After the first start of 1.2.3, copy what that folder held (all but `system`) into `SaveData/ugc`. The same goes for any Linux player who installs a Setup by hand with a launcher from 1.2.2 or older.
- Gems you upgraded before 1.2.3 get their tier's stats at your first login, in both modes: nothing to do and nothing to pay.
- Windows players on 1.2.1 or older: install 1.2.3 once with the Setup below; it keeps your save. The automatic update needs 1.2.2.

## What's new
### Characters, Elites and pets
- 15 character slots, the original game's limit (it was 8): in Adapted all 15 are open without Character Cards (`CharacterSlots` in launcher.ini); in Legacy each card opens one more, up to 15.
- With GrindRates (on in Adapted) the 24 field Elites come back 10 s after a kill instead of 5 minutes, so their 300-kill trophies are within reach; their loot is the same. Dr. Pippy now stands in Clock Tower Square in Adapted, so his trophy can be earned (no data places him anywhere, so we chose the square his trophy names).
- Pets level up: pet fusion takes the fodder pets, Fusion Stones and mesos it shows and gives your pet experience, up to level 50.

### Unified content (with the content pack)
- The Lv 75 Epic story opens: An Unexpected Request needs only level 75 (the quest before it exists in no data), its 44 Lv 75 quests are offered, and the story goes on with A Sudden Summons to The Power of the Blue Lapenta. The 29 quests between A Link Between Worlds (Lv 70) and Lv 75 need maps and NPCs no data has, so the story jumps there.
- Batch 4 also brings Lapenshards for Beginners, Reach Level 70, News from the Forge and A New Power of Transcendence, with their NPCs, all in English.
- Cutscenes and map-script captions read in English: about 1,100 lines showed nothing; 137 lines that no client of any region has were written new.
- KMS emotes: the 50 KMS emotes show in the Emotes window with their names and animations, and the Meret Market sells them again.
- KMS looks: KMS cosmetics draw KMS's own models (about 400 drew another item's model, such as Blue Sea Boy Earrings), and the 26 Global items KMS also uses (the Lively Alice set, Romantic Wedding pieces, Calico Cat) come with their KMS look from the KR boxes.
- KMS capsule coins open their coin shops, and the 6 KMS instruments can be played.
- Icons: the Wild Rider Jacket (F), Distressed Pants (M) and Distressed Hot Pants (F) have their icons, and 12 KMS cosmetics take their look's icon.
- Names: an item has the same name in its tooltip and in chat; 14 plural item names that were in Korean read in English.

### Meret Market
- New tab: what 1.2.3 adds, 87 entries: the 23 KR capsules (380 Meret each), two exchange vouchers and a package held back in 1.2.2, the 50 KMS emotes, and 11 prizes of the NA Slime Variety Capsules (air and ground mounts such as Flurry of Leaves and Sullen Cat, and two tombstones). 1.2.2's entries go back to their own tabs.
- 317 more prizes of the KR capsules can be bought on their own, in their own tabs, priced by rarity.
- Items that do nothing offline leave the market: the 4 Daily Mission Insta-Completion Vouchers, the Guild and Party Summon Scrolls, the Instant Gathering Vouchers and Pink Bean's Premium Starlight Special Capsule.

### Enchanting, gems and attributes
- Adapted: `/autoenchant [target]` makes one press of Enchant at Ophelia or Peachy keep trying until the next level or your target, or until materials or clean copies run out; odds, costs and failure charges stay as they are. With it on, one gem upgrade press goes up level after level until your target or the materials end.
- Adapted: `/autoreroll <attribute>` makes one press of Change Attributes roll until that line appears, the materials or lock scrolls run out, or 100 rolls; you then apply or redo the last roll as usual.
- Adapted: Change Attributes costs half its materials, as the original's login events made it; with GrindRates monsters also drop Metacells and gem dust.
- Both modes: a gem upgrade changes the gem's stats at once, in a socket or in the bag, and putting a gem in or taking it out changes your stats at that moment; the socket slot check is right, and an unlock needs its copy.
- Both modes: enchanting asks for copies only from rank 4 gear, as the original did, and +15 no longer fails; Ophelia's failures from +9 and dismantling listed Epic or better gear give what the original gave.
- Both modes: the lock scrolls sold work on Ascendant armor and accessories, and 4 Change Attributes trophies can be finished.

### Quests, maps and dungeons
- Quests remember whether you track them.
- Quests and missions that reward a share of a level's EXP pay it, daily missions among them.
- Shadow Expedition gauges and score boards start empty and fill with your kills, weighted as in the original, so their waves and rewards come in order; a Distinct Paul you pick up fills the treasure maps' board. Fame challenges score their missions and can be lost.
- Dungeons keep time: a timed room ends the run when its clock reaches 0 (9 chaos raids among them), and a dungeon's revival limit counts.
- Map scripts: the areas scripts place hit what the original's hit (the Palace Undercroft flames, the Jolly Crocker's blast) and repeat until the script stops them; a ship turn's blast hits the corsairs, not you; trigger areas keep their rotation; a script no longer sees a monster as dead before it spawns, so Varrekant's ending plays at his death; Mason's invincible windows end early, as in the original; a script that waited for a step its data lacks no longer holds you.
- Hidden Realm portals open on their schedule, the Kritias field puzzles run, and bosses that hide and show parts of the floor do so.

### Combat
- Monsters hit you about 45 % harder than before, as measured in the original's fights, in both modes; your gear's stats count toward your attack as they did in the original.
- Monsters can be stunned, and invincible monsters and bosses take no damage while they are invincible, so some fights feel different.
- Monster missiles fly and hit at a distance, and their areas land where they hit (Cyber-Balrog, Devorak, Springer); attacks that ring their caster place their areas around him (864 attacks of 324 monsters); Pamokan's pillar rush works; attacks without a timing key hit.
- A monster whose battle ends heals to full, as in the original, and monster EXP follows the game's own table by rank.
- Prestige level abilities and a lapenshard's skill apply.
- Striker's Fighting Spirit stacks, and Vengeance adds 4 % damage per stack.
- A skill you hold keeps its hits after later casts (Smiting Aura); Holy Relic and Mind Stealer no longer show a 0 after each hit; Cusp of Life's cannons hit the monsters.

### From your reports
- The launcher no longer times out waiting for the login or world server when the server is up but Windows' list of listening ports misses it: a server also counts as up when its own log says it listens. Problem reports now name the Windows revision, as the cause on those PCs is still not known.
- "The Game server stopped unexpectedly" on PCs with a proxy: the servers now talk to each other directly and ignore any proxy; problem reports say whether a proxy applies (names only) and TROUBLESHOOTING.txt has the steps.
- If the game's own NxCharacter64.dll is missing because the patch's copy was put in its place by hand, the launcher stops before starting and says how to restore it (this made the game crash). When the game crashes, the patch now writes one line about it, and the problem report shows it.
- An old update zip left in the program folder is moved aside and no longer reported at every start; a folder that holds an unpacked update zip or part of the game says so.
- Setup's repair refuses to run while the game sits inside the program folder (it would have deleted the game) and says to rename the folder; an uninstall keeps that folder.
- Golden Tower 4F can be cleared (a room's timer sent you to Golden Tower 8F). Crisis in Henesys: the waves end after 5, 3 and 3 minutes, so a monster nobody can reach no longer holds back the next wave. Sanctum of the Sage: the Dais Maze rounds start one by one and round 12's gate opens.
- Life skill items raise a gather's items and a craft's mastery (58 items). A home with a Nurturing cube that has no data no longer breaks its cube list. Pets no longer float in the air after you fly.
- Trophies: I Need It!, Track Star, the housing and Daily Life furniture trophies and the guild trophies (United We Stand among them) count; claiming a trophy pays only the grades you earned (Gilded Globetrotter showed a tier VI at 0/0); decoration EXP restarts at each level.
- Guilds: the guild day starts at UTC midnight, as in the game, so a morning donation east of UTC is no longer refused; a guild expel or leave can no longer be undone by a save in between.
- A quest portal an NPC summons faces as the original's did and opens 0.6 s after it appears.
- Items: a refused add no longer duplicates part of a stack, a mail attachment above one stack can no longer be collected twice, and a pickup above one stack is no longer lost; boxes that list an item more than once give every row (56 Global boxes), and a box's group for other jobs gives your job nothing unless the box would then give you nothing.
- Stats: a buff added during a stat rebuild no longer counts your gear twice.
- Housing: forfeiting an outdoor plot or letting it expire puts its furniture back in storage and takes the assistant, the door and the portals with it.
- Returning to a map no longer hangs on the loading screen in the rarer case 1.2.2 left.
- Problem reports: under "send automatically", a session whose server logs hold an error is sent too; reports name the epic quests you started and your level; Swift Swim's normal end no longer counts as a problem.

### Launcher, saves and Linux
- What you make in the game (portraits, item designs, banners, guild marks, blueprints) now lives with your save, in the `ugc` folder next to it, copied there once at the first start; Setup's repair and the Linux full update no longer lose it, and a copy of your SaveData folder now holds it too.
- Verify game files (Start menu, or `ms2offline verify` on Linux) checks the content pack's archives, repairs one whose original part is intact, offers Steam's own check for the rest, and names any program file that is missing or changed. Turning the content pack off restores archive by archive, so a Steam update of one archive no longer blocks it.
- Linux: updates work from any version and new installs work again; with AutoUpdate=ask and nobody to answer, the update installs instead of being skipped in silence; an update that stops halfway is finished, or refused with a message, at the next start; reports have a System line (Deck or not, display, memory; no user or host name).
- A bigger interface on a Steam Deck: the Linux README explains the game's own UIScale setting; tell us which value works.
- TROUBLESHOOTING.txt has a recovery section, and COMPATIBILITY lists what is supported and what was tested.

## Known issues
- 1.2.3 was checked by its automatic tests, not played through before release; your problem reports tell us what still goes wrong.
- The Epic Apocalypse story stops after The Power of the Blue Lapenta: the next quest's maps exist in no build of the game (BT-01).
- Stacking buffs other than Fighting Spirit still change no stat per stack, and damage over time ignores stacks (BT-155, BT-158).
- Debuffs that raise the damage you take (Frozen, Ice Block, Defense Down) do not apply yet, and Iron Shield reduces every hit (BT-30, BT-31).
- Some monster attacks are still not as in the original: a few placed areas and sensors, and summons that share a damage ledger or health, such as Gigantica's bodies (BT-68).
- Landevian's room runs only 4 of its script's 16 buff steps, so his invincible windows do not end early yet (BT-223). A Lv 60 Assassin's job chain may stop in 52000159, where Fallen Man should appear (BT-160).
- 12 KMS cosmetics still cannot be equipped (Darkmoon Kitten Earrings, Dark Moon Kitten Cape, Fresh Lemon Overalls and Dress); nothing sells them (BT-118).
- 81 KMS items keep a placeholder icon: no client has their icon, and we draw none (BT-136). The capsule window can show "$s_item_gachabox_title" as its title on a capsule without an NA season (BT-111).
- 49 Meret Market items carry the client's PC cafe mark; whether the client lets you use them is not known yet (BT-148).
- Pet training with Holotrainers is not in yet, and Change Attributes refuses pets below rarity 4 (BT-186). House assistants only stand there (BT-157); the Maple Arcade games do nothing (BT-156).
- Dismantling gear the data does not list gives a placeholder amount of Onyx (BT-208); an outfit cannot unlock a socket (BT-185).
- Queen Bean Rumble: each round gives a tenth of the room's EXP and mesos, not its own reward (BT-84).
- Housing: a design on a furnishing is lost when the furnishing goes back to storage (BT-09). Guild-wide guild buffs do nothing yet (BT-52).
- Smaller gaps: part of the clear reward of two raids and of the 6-person Zakum and Tiaman chaos raids is empty (BT-21); the field games of Fields of Perkau and four more maps never start (BT-94); 20 NPCs name a shop that does not exist, such as Monarre and Scarlet (BT-39); Black Market and birthday NPCs open only a plain dialog (BT-40); the tutorial skip is never offered (BT-02); Swift Swim lasts about 10 % less than in the original (BT-166).
- Day and night lighting by the clock is not in yet. The Style Coin Shop (NA only) has no items offline. Items with an old end date still print "This is a temporary item" in their tooltip; the date is not enforced.
- A stuck loading screen that you close normally does not count as a problem, so it sends no report: tell us on Discord (BT-168).

## Play check
Not run for 1.2.3 (the maintainer's decision, 2026-11-07): no game run, no core-loop run and no fair-fight run; players
test the release, and their problem reports arrive automatically.
