# Release 1.2.1: Black Market, boss summons, world clock

Planned 2026-10-22 (maintainer: "start planning development of 1.2.1 aiming to progress toward the directive again";
the Black Market requests of the same day; "Add boss, monster and NPC behavioral restoration"). Published 2026-10-23
after the maintainer's play check.

## Candidate
- Fourth candidate 2026-10-23, released as 1.2.1: the second candidate plus DL-51 (the third candidate: an
  update replaces 1.2.0's content pack), DL-52 and DL-53 (usable NPCs, the Pirate Cannons; range offsets
  turn with the caster; BT-44). 518 server, 145 launcher and 102 Linux tests pass on that commit, the pack tests on the
  shipped pack DB too. Game data and pack as in the second candidate. Upload set: 1.2.0 to 1.2.1 changes 67 files and
  removes 2; the signature verifies; the privacy scan finds no content hits. The test copy is refreshed.
- Second respin 2026-10-23 (debug-log round 2: DL-09 to DL-50 on top of the first
  candidate; 512 server, 140 launcher and 28 Linux report tests pass; the data tests pass on both game DBs). Game data
  rebuilt for DL-44: the base and the pack DB were ingested again with this code and only `map-entity` changed (2,997
  scripted platforms in 43 maps; research DL-Q2); the pack stays 1.2.1-dev.b2 with that DB. Upload set: Setup, update
  zip (1.2.0 to 1.2.1: 67 files changed, 2 removed), signed release.json and its signature (verified with the launcher
  key); the privacy scan finds no content hits. The test copy is refreshed.
- First candidate 2026-10-22 (the summons, clock and Black Market work, the README line for
  /time, and the debug-log round of 2026-10-22: NB-13 to NB-15, DL-01 to DL-08; 457 server and 122 launcher tests pass).
- Client patch: rebuilt from source (MinHook 1.3.4 vendored, every hook guarded by exe identity and byte hash and
  logged INSTALLED or REFUSED; BT-14), linked with /PDBALTPATH:%_PDB% so the DLLs name only
  their .pdb file, never the build folder.
- Client mods: as 1.2.0; with the unified content applied the launcher sets offline-shop's feature list aside so the
  pack's own list (which has the shop's features too) reaches the game.
- Content pack: 1.2.1-dev.b2 = batches 1 and 2 of the unified content (cosmetics; weapons, armour and accessories),
  38,206 items in its data, built with every batch-aware gate.

## What's new (player-facing draft)
- Black Market (Adapted mode): gear costs more the higher its level and rarity (a level 70 Legendary weapon about 24.5
  million mesos, a level 50 Epic about 2.5 million); 60% of the gear on sale is near the level of the character you
  played last (10 levels below to 5 above); no Normal (white) gear is sold. Every time you press Play it also sells
  upgrade materials (Onyx, Chaos Onyx, Crystal Fragments, Gem Dust, Fusion Stones), 160 gemstones (tiers 1-10) and
  about 110 potions, priced by rarity and power and never below an NPC shop's price. Prices and shares are in
  Maple2/offline-market.json.
- Boss summons: bosses call their adds and their next phase again (Gigantica's tail summons her head, Zakum his arms,
  Big Bobo his riders, and about 100 more bosses); adds tied to a boss leave when it dies; bosses and their adds signal
  each other and count how many adds are left, so phases that waited for that move on.
- Timed boss patterns: 29 bosses (Papulatus, Devilin Warrior, Paika, Baphomet among them) use their timed attacks.
- Map counters that bosses add to (+1/-1) now add up, so phases such as the bridge in Blackshard Nexus and Zakum's arms
  can start; bosses' signals reach the monsters around them.
- Infernog's ship can be cleared: its end check looked at the wrong value and always failed.
- Respawns: 1,843 monster slots in 61 maps (Forain Forest among them) refill again; they shared one count and most
  never came back.
- Passive monsters (71, such as Orange Mushroom) fight back when you hit them.
- Two quests can be finished: Wigglers now live in Sylvan Woods Trail ("Only You Can Preserve the Forest") and the
  Devilin Chief on South Royal Road ("Notorious Monsters: The Devilish Commander").
- Nine monsters whose AI file is missing use their family's AI instead of the default one (Bjorn, Papulatus Chaos).
- Wild pets' taming AI measures distance the right way round.
- Iron Shield works: while you hold it, it takes 25 % (level 1) to 80 % (level 12) off the damage you take, as its tooltip
  says; so do Iron Defense, Guardian's Wings, Mesoguard and the Iron Defense tonics (from a player's report).
- Monsters use all their skills: about 840 monsters whose AI picks a skill by the monster's own chances only ever used
  their first one.
- Gigantica's fight ends when she dies, instead of always running into the 10-minute "Gigantica got away?" ending.
- Map scripts pick their random branches fairly: Gigantica appears at all six of her spots, the melody puzzles use all 20
  patterns, shop customers order every item (297 scripts were skewed or stalled).
- Monster groups on rooftops and floating platforms spread out instead of standing on one spot (57 spawn areas).
- NPCs play the animations their scenes ask for (Peachy in Healing Forest; 25 scenes that name a chain of animations).
- Session reports list fewer false problems: the five client packets no server answers and the original game's data gaps
  are no longer reported, and the trigger list shows the last 200 state changes instead of a few seconds of idle loops.
- World clock: in-game time runs only while you play (a day is about 17 minutes, seasons last 15 days); /time shows it.
- Unified content: batch 2 (weapons, armour, accessories) joins the cosmetics of 1.2.0; pack items whose names were
  blank show them.

### From the players' reports (debug-log round 2, 2026-10-22 and 23)
- Problem reports go to the developer automatically: the first start shows a one-time notice that says why every report
  helps the game's development, how to contribute (Discord, GitHub), what a report contains and where the details are;
  "Ask me each time" keeps the question; a choice you wrote in launcher.ini is kept. Linux too.
- Pocket realms (Karkar's mini-games, the Hat of Misdirection rooms) no longer freeze on the loading screen when their
  90-second timer ends: you come back to the map below. Their exit portal works, and a logout inside one logs you in below it.
- Moonlight Fortress (Lv 41): the Pirate Cannons fire when you use them (the ledge monsters no melee class could reach),
  and the ship sails when you use the wheel, so the dungeon can be cleared; scripted platforms work in the other maps that
  move them (Katramus Arena, Undercroft Corridor, Poison Cave, Croc's Dock, Time Barrier's doors); 35 usable NPCs work
  (cannons on Treasure Island, in Cusp of Life, Watchtower Rampart, Sky Fortress in Crisis, and those bosses summon).
- Attacks whose area starts ahead of the attacker (608 skills, monsters' and players') land in front of it whichever way it
  faces; they used to be placed as if it faced one fixed direction.
- The Challenge Map's Queen Bean Rumble button takes you to Queen's Parlor and Alberto Bean, so the Royal Rumble quests go
  on (its door asks no gear score offline); the Chaos Raid and Eye of Lapenta buttons work too.
- Arrow Storm, Smiting Aura and the other area skills count as your hits again: their damage numbers and the enemy's
  health bar show.
- Quests: follow-up quests that start by themselves do so again (the Assassin's An Origin Remembered and job change, the
  Soul Binder's A Power Sealed Within); A Difficult Delivery's pizza box and the carried items of 6 more quests stay where
  you put them down; quest items monsters drop only during a quest drop again (Arwen's Glass Slippers and 31 more items);
  quests that need items you carry count them right, also in old saves, and an NPC offers a quest's completion only when
  it can be completed; Cave Depths opens its exit for The End of Nelph; MC Kay's mini-game no longer pulls you out of a
  solo quest map or a dungeon lobby; a quest's summon portal appears at the right NPC; map scripts open NPC dialogues.
- /unstuck in a solo quest map starts it again from its start (Crisis in Henesys); /unstuck --town goes to Henesys.
- Trophies count: your level, meta trophies and Trophy Maniac, dungeon and quest trophies, play time, shopping, dungeon
  rewards and first clears, race trophies (Vegetarian, Positively Beastly, Exterminator, A Grave Affair and 7 more; 56
  quests count kills by race too) and Unstoppable!'s kill chains; old saves catch up at login. Guild check-in gives EXP.
- Combat: buffs asked for at a level their effect lacks use level 1 (Smiting Aura's light, boss invincibility windows of
  Gigantica, Mika, Landevian and the Colosseum); pull skills pull monsters to you along the ground; skills hit the targets
  you aimed at, up to their maximum; "all"-type attacks are resisted by resistance; a late skill cancel no longer stops your
  next skill's motion (Iron Shield); a skill you cannot afford stops at once and shows your real spirit and stamina; mount
  sprints cost EP; 23 monster models that only have a run animation move.
- Map scripts: their buffs apply (Ice Beast's Den's chill and freeze, the tube damage fields, Enhanced Geyser in 12 arena
  and guild maps, the mine-train aura, the wedding pinata buffs); NPCs turn back to their spawn facing; event scenes give
  the camera back; portals without a target bring you to the map's spawn point instead of an endless fall; a dungeon room
  is never its own return map.
- Housing: Lapentouched Portraits and Monster Boxes go to furnishing storage; stacks keep their amount; Clear All never
  loses a furnishing (a second housing assistant made it fail).
- The summoned pet follows you through portals; beauty NPCs open without a missing-shop error; instruments play without a
  warning; the Style Coin no longer opens an empty shop.
- Reports list fewer false problems (original-data messages log at Debug) and a crash report names the crashing part.
- The update keeps the unified content: the launcher replaces 1.2.0's pack in the game files with 1.2.1's (it used to say
  the unified content "does not fit this game client" and start without it until the game files were verified in Steam).

## Known issues
- Boss phase changes that depend on a boss's battle end or death (the battle end steps) still fire early or not at all
  until the next data rebuild; summons that inherit a damage ledger (Gigantica's bodies) or share health are not done.
- Summon lifetimes read the data's mixed units by a rule (below 6,000 seconds, else milliseconds); some summons may stay
  or leave earlier than in the original.
- Day and night lighting by the clock is not in yet (the clock and the /light test command are).
- Debuffs that raise the damage you take (Frozen, Ice Block, Defense Down) do not apply yet; Iron Shield reduces every hit
  (the original's "some attacks cannot be blocked" is not known well enough to add).
- Queen Bean Rumble runs end roughly (a lost round has no result window: Leave Dungeon; BT-46, 1.2.2). The hall buttons'
  command bytes are inferred: if a button does nothing, the server log names the byte it got.
- The Style Coin Shop (NA only) has no items offline. Overcome's cooldown cuts are not in the NA data (as in the
  original).
- A furnishing placement the shop refuses can leave a free copy in storage (BT-45, upstream).

## Play check (maintainer)

Results 2026-10-23 (second candidate, Lv 50-70 Knight, the session report showed no server error or warning): everything
the maintainer tried passed. Found: the unified content "does not fit" a client with 1.2.0's pack (fixed, DL-51, third
candidate); Moonlight Fortress stops at Battle03 for a melee character, before the ship (BT-44, the Pirate Cannon), so
the ship (item 11) is untested.

Results of the fourth candidate (2026-10-23): the maintainer: "it is all good" (the cannons, the ship, the
attack areas and the update from 1.2.0's pack included).
1. Black Market in Adapted mode: gear near your level, no white gear, materials, gems and potions listed, prices as
   above; buy one gear item (stats roll), one stack of Onyx (part of the stack).
2. Summons: Zakum (arms), Gigantica (tail to head), a Shoo and Booboo or Big Bobo fight; adds leave when the boss dies.
3. Infernog's ship: the clear after 15 minutes at full damage.
4. Respawns in Forain Forest; an Orange Mushroom fights back.
5. /time twice a few minutes apart; close and reopen the game: the time continued from where it was.
6. Unified content on: a batch 2 weapon or armour item shows its name and model.
7. Knight: hold Iron Shield while a monster hits you: the damage numbers drop (25 % at skill level 1).
8. Gigantica: after the kill the fight ends at once, without "Gigantica got away?".
9. A field of monsters with two attacks: they use both.
10. First start: the problem report notice shows (once); "Ask me each time" and "Keep it on" both work, and the "Send
    problem reports automatically" checkbox shows the answer.
11. Moonlight Fortress with a melee character: at "Fire the cannon." the cannon shows its key hint; a press kills the ledge
    monsters about 1.5 s later, a second press within 7 s does nothing; nothing moves before the wheel; the wheel sails the
    ship east in 3 s with you on it; at Battle13 cannon 502 kills the ledge monsters 217-219; the second ride works.
    Golden Tower Elevator and North Royal Road: their platforms behave as in 1.2.0.
12. Challenge Map, Queen Bean Rumble button: Queen's Parlor; talk to Alberto Bean (The Royal Rumble, For the Queen's Joy!);
    the room portal opens the dungeon window.
13. Archer Arrow Storm and Priest Smiting Aura: damage numbers and the enemy's health bar.
14. A hat room (Hat of Misdirection): use its exit (you land on the hat's map); in another, let the 90 s run out; quit inside
    one and log in again (you are below it). A Karkar mini-game ends without a loading-screen loop.
15. A Difficult Delivery: put the pizza down, break the wall, pick it up again, deliver it.
16. Assassin at job rank 2 (52000159): after quest 40002736, An Origin Remembered starts by itself and the Fallen Man and
    the Child talk; Arwen's quest: Glass Slippers drop in Ellin Grove (about 1 pair per 100 kills).
17. Housing: place Momo, get a second Momo, Clear All: both kept.
18. A game client that still has 1.2.0's unified content: Play applies 1.2.1's without a warning (the session report's
    content pack lines show "applied size, pack header"); UnifiedContent=false then puts the Global files back.
19. Meret Market with the unified content on: every tab shows and lists items (the pack's feature.xml turns
    MeratMarketNewBM and MeratMarketClosing back on, which the shop mod turned off because they hid tabs; research DL-U).
20. Attack areas (DL-53): a boss with forward attacks (Gigantica or Zakum) hits you in front of it, not beside it; your
    own skills still hit monsters standing in front of you whichever way you face.
