# Release 1.2.2: furniture, pets and mounts, the New tab, the players' reports

Planned 2026-10-23 and 24 from the players' session reports and notes of those days, with content batch 3, the boss
phases that wait for a battle end and the bugs set aside for 1.2.2; approved by the maintainer on 2026-10-24. Not
published yet (section "Published").

## Candidate
- First candidate 2026-10-29: 733 server, 149 launcher and 103 Linux tests pass, with the data tests on the shipped
  base DB and the pack tests on the shipped pack DB. Game data: the 1.2.2 rebuild of the base and pack DBs; content
  pack 1.2.2-dev.b3g (batches 1 to 3). Client patch as in 1.2.1. Upload set from 1.2.1: 68 files change and 2 are
  removed; the privacy scan finds no content hits.

## What's new (player-facing draft)
- Unified content: batch 3 (993 items: furniture, pets, mounts, potions and other items) joins the cosmetics, weapons,
  armour and accessories of 1.2.0 and 1.2.1; 18 more (6 instruments and 12 boxes) wait for data they need.
- KMS mounts: the 76 KMS ground mounts can be ridden and the KMS air mounts glide; the KMS-only rides are drawn (the
  Blue Tyrannosaurus, Pizza Delivery Motorcycle, Runaway Suitcase, Dung Beetle, Light Speed Cube, Growling Bull,
  Rhinoceros Beetle, Golden Rhinoceros Beetle and IV Stand), and riders take each ride's pose.
- KMS pets: the Mabinogi Nao Plush Doll and Black Bean (a Lv 70 pet) are drawn, walk and follow you.
- Names: every item, NPC, map and object of the pack has an English name (a translation, or a new name where it had
  none); NPC titles, actions and lines read in English (Time Traveler Andy in Tria); opening a box no longer says
  "You got .". KMS copies of Global items take the Global item's English name, so some cosmetics of 1.2.0 and 1.2.1 are
  renamed (34000072 is the Deep Dark Cello, 40400055 the Pink Bean Car); the Street Rapper Gloves are gloves again.
- Meret Market, New tab: it lists what 1.2.2 adds, 89 entries: 14 KR (Korean) capsules at 380 Meret each (the Ducky
  Outfit Capsules of 2021 and 2022, Pink Bean's Starlight and Premium Starlight Special Capsules), KMS badges, mounts,
  packages, exchange coupons and Super Chat Theme Bundles, 14 pieces of undersea and delivery-box furniture, and Kirin
  and Sun Wukong. The next release's New tab replaces it, and these entries go back to their own tabs.
- Meret Market paging: New and Featured show every entry in a fixed order, within the 5 pages the client shows of
  them (New 89, Featured 45; each showed at most 90 and hid the rest); the 75 pet capsules (Pengy Capsule and the
  others) are back under Summons > Pets, as in the NA market.
- Every prize of those capsules can also be bought on its own, in its own tab (197 more entries, 300 to 1,200 Meret by
  rarity), except some KMS emotes (Known issues). The capsules and the KMS boxes give their KMS contents (a Super Chat
  Theme Bundle opens its 10 chat themes).
- Items with an old end date in their data (939 Global items, 341 of them sold in the Meret Market, such as Meta Badge:
  Bronze Attack Mark and the Maple Journalist Hat) no longer arrive expired; ones you have are repaired when they load.
- Potions are used up: one use takes one from the stack, from the inventory or a quickslot (most potions, the Elixir
  among them, never left the stack), and at full health the use is refused; the Helium Balloon and the Lapenta
  Essence are used up too. KMS potions, lures, vouchers and snares sit in the Consumable tab.
- A box or capsule whose roll lands on an item without data no longer uses itself up for nothing.
- KMS looks: the Black Bean Car and the Potato Corn Dog are drawn when you glide on them, 7 KMS ground mounts (Chubby
  Cat and the IV Stand among them) and 4 capes show their effects, 104 KMS hats (the Cupcake Puff Hats, the Kerning
  Mail Carrier Hat, the Autumn Picnic Acorn Hat) sit on your head instead of at your feet, and about 230 KMS items get
  their icons, the KR capsules and 61 cosmetics of the Meret Market among them.
- Bosses run the steps their AI keeps for the end of a battle and for their death when that happens, so the phases
  that waited for them go on (1.2.1's first known issue); a monster's AI choice runs only the step it picked (it ran
  its steps in turn).
- Monster spawns: monsters appear closer to where the original put them (scored against the original's captured
  respawns: 96.8 %, against 94.5 % in 1.2.1).
- Queen Bean Rumble ends: a lost later round shows the result window and gives the rewards of the rounds new this week,
  a lost first round shows the failed banner, and Continue resumes at the lost round; rooms without sub-rewards
  (Colosseum, fame challenges, Dark Stream, guild raids) pass the reward check.
- Housing: a full furnishing storage refuses a new furnishing and its source keeps it (the shop refunds, the mail keeps
  it, a reward comes by mail); a refused placement leaves no free copy and takes no payment.
- Sky Fortress in Crisis loads, so the epic quest Some Assembly Required goes on; the cutscenes of Madrakan Spire's
  ending and of Savage Crossroads play. The Waddle Penguin Club can be entered.
- Girls, Interrupted gives the 4 Chaos Onyx that its enchant at Ophelia needs.
- Legacy Mode has no cheat commands, as the original had none; `CheatCommands=true` in launcher.ini still turns them on.
- Launcher: an update's rollback puts back only the save it backed up, where it came from (it could overwrite another
  profile's save); option lines you set to true in launcher.ini are no longer commented out at start; a read-only
  launcher.ini does not stop play.
- A player guide comes with the release (PLAYER_GUIDE.txt): installing, the first start and the two modes, updates, the
  content pack, saves and backups, uninstalling, problem reports, players' questions (Lith Harbor's mining and foraging
  islands) and troubleshooting.

### From the players' reports (1.2.0 and 1.2.1 sessions, from 2026-10-23)
- Guilds: guild EXP, funds, contribution and check-ins are saved at every change (they were lost at every exit);
  donations work (daily and funds limits, mesos, guild coins, contribution, EXP); the guild level is read right; you
  check in once a day; the weekly contribution resets. Personal guild buffs work (their meso cost, 60 minutes).
- Pets follow you and stay near you (they kept about 1,200 units away and backed off when you came near).
- House assistants appear: a placed assistant (Momo and 26 more the Meret Market sells) walks about in your home, and
  removing her cube removes her; the 9 whose NPC the data lacks are no longer sold.
- Souvenirs give their buff each time you use them (the Imperial Medal: Strength, Dexterity, Intelligence and Luck +2
  for 1 hour; 20 souvenirs), then switch off for the next use.
- Meret Market boxes: Kirin and Sun Wukong (Combat Pet) are sold as pets (their boxes opened to nothing and landed in
  Misc), and 103 more boxes that opened to nothing are no longer sold (the Orange Mushroom Power Up Capsule among them).
- Boss health steps ("at or below X %") wait for their threshold: since 1.2.0 they fired at once, so bosses in 22 maps
  changed phase or called their adds as the fight began.
- Heavy Gunner's Med Kit arms 1 s after the throw and heals whoever steps on it within its own range (it went off on
  the thrower after 30 s wherever they stood); Booby Trap MK 2 and Ice Cream Time work the same way.
- Breakable floors and gates break: Pamokan's jump in Ignicore takes you down with him, so Dark Forces: Study 1 can be
  finished, and the root-stem gates in Root of Darkness break, so An Unseen Threat goes on.
- Auto-revive: on the 71 maps that revive by themselves (Blazing Forest, Calamitysnare and the other Shadow World maps,
  arenas, event maps) you come back after the countdown; you used to stay dead.
- Objects spawn their monsters: the rocks of Fairy Tree Lake spawn the Wigglers of Only You Can Preserve the Forest,
  the jars of Redsand Hills the bugs that drop The Fallen Chief's Dried Moromoro Herbs; an object with several drop
  boxes drops them all.
- Stats: the attribute points you allocate count (every stat change dropped them, the first right after login); buffs
  no longer refill HP, Spirit and Stamina (Feinting Guard's endless recovery, Shield Wall, Warhorn); Centered Mind's
  tiers count one at a time.
- Buffs and debuffs on monsters work: Magnetic Bomb's slow and every other one change a monster's stats and speed, and
  so do a boss's own buffs, so some fights feel different.
- Bosses face and turn: they face you while they wait and turn before they cast, so their attacks come where they look
  (Pamokan, Ninecap Copper); their phase timers start when their AI sets them; backward hops go back and looping
  attacks loop; Pamokan's and Hemokan's whirlwind rings hit their sides, not the safe centre.
- Bosses give up a chase: they end the battle and walk back when you lead them away, and no longer follow you off
  their platform.
- Monsters use skills that have no detect distance: the Giant Turtle casts its water prison, so I Can't Breathe! can
  be earned.
- Trophies: helping defeat a world boss and its final blow count (Devilin Warrior, Devilin Chief, Griffin, Griffina,
  Giant Turtle, Doondun), as do Fly Griffin Airlines, Blargh!, Merciless and the field mission trophies; hitting trees
  counts for Shaking Trees and 5 quests.
- Mounted: an action you start while mounted (the air taxi, a ground taxi, a skill) works at the first press.
- Furnishing quests count a replaced furnishing (Furnishing Fundamentals, A Lesson in Product Placement, the pet-nest
  quests) and a bought placement (Spend 5,000 mesos placing furnishings in your house).
- Moving platforms start when they are switched on: Van Corta's lever lift comes up from the bottom and its boards no
  longer vanish under you; so do the Moonlight Fortress ship and the platforms of 32 more maps.
- Quests with a dungeon (Under the Palace and 34 more) show the Teleport button as soon as you accept them.
- Dungeon-lobby portals outside a dungeon run take you to the map below (Bastei Peak during The War Chief's Mission),
  or to Lith Harbor when there is none.
- /unstuck on an open field map takes you to that map's own spawn (Umbral Passage's low blocks); /unstuck --town still
  goes to Henesys.
- Katramus Gardens: each glass door opens with one use (each spot had two doors).
- Problem reports keep more of the map-script history: a trigger that repeats its states is counted on one line.
- A connection check the server answers late no longer drops you: a heartbeat handled more than 2 s after it was sent
  (during a slow login, map load or first entry into a map) closed the game with a disconnect notice (3 of the 5
  reports of the latest round).
- Reactor Site (the Kritias epic, after The Daring Rescue) no longer leaves you hidden in its cutscene.
- Coming back to a map you left about 10 minutes before no longer hangs on the loading screen: the empty map could be
  closed while you loaded into it (the probable cause of a report from Sylvan Woods Trail).
- On a PC whose number format uses a decimal comma, the world boss maps and the event mini-games read their map
  scripts' decimals right: their EXP rates, effects and gravity came out as 0 or tenfold.
- Problem reports and the server logs keep non-ASCII text as written (Korean map-script names were garbled).
- A game that stops reading for a few seconds in a busy map (a long load, a frozen window) keeps its connection: the
  server ended it after 5 s, and the game went on in a world that no longer moved, without a notice.
- `/quest <id> -s Completed` (cheat commands) completes a quest you never had; it only started it.
- A new Soul Binder can finish the first quest, Mystical Jewel: the box in the Disciple's Room drops the Guidance
  Training Orb again (since 1.2.0 the drops-for-your-class rule took it for Heavy Gunner gear, as its data names that
  class), and a Soul Binder who comes back to the room during the quest can leave it (the room's script stopped at a
  step the data lacks and kept the exit shut).

## Known issues
- KMS looks: about 390 KMS items of the pack, most of them cosmetics released in 1.2.0 and 1.2.1, draw another item's
  model than in KMS (Blue Sea Boy Earrings (M) show another pair of earrings), and 26 Global items KMS also uses keep
  Global's look where KMS has another (the Lively Alice set, Romantic Wedding pieces, Calico Cat). The 23 KR capsules,
  two exchange coupons and a package that give such items, and the prizes only they give, stay out of the Meret Market
  until the fix in 1.2.3 (BT-120, BT-121).
- 12 KMS cosmetics cannot be equipped (Darkmoon Kitten Earrings, Dark Moon Kitten Cape, Fresh Lemon Overalls and
  Dress): their look comes from a preset neither client has. Nothing sells them: the two capsules that give 8 of them,
  Pink Bean's Halloween Special Capsule and Chuseok 2021 Special Capsule, stay out of the market (BT-118, 1.2.3).
- Pink Bean's Premium Starlight Special Capsule (380 Meret, New tab) gives only Instant Gathering Vouchers (50), which
  do nothing offline; nor do the Guild Summon Scroll (3) and Party Summon Scroll (10), sold in Functional as prizes of
  Pink Bean's Starlight Special Capsule (BT-122, 1.2.3).
- KMS emotes: the client's Emotes window cannot list the 50 KMS emotes (it has no strings for them), so a learned one
  gets no tile and cannot be used; the market no longer sells them, but 11 of the 14 KR capsules can still roll 32 of
  them (2 to 13 % per roll). The content pack brings their strings in 1.2.3 (BT-124).
- The capsule window shows "$s_item_gachabox_title" as its title and "text" as its timer line on a capsule without an
  NA season, such as the March 2022 Ducky Outfit Capsule; the window still lists the prizes (Global's own string gap,
  BT-111).
- Three Meret Market tiles show no icon: Wild Rider Jacket (F), Distressed Pants (M) and Distressed Hot Pants (F),
  prizes of the May 2022 Ducky Outfit Capsule; their records name icon files no client has (BT-119, 1.2.3). With the
  content pack off, 22 Global items show no icon either, the Paraglider among them (1.2.3).
- 93 KMS items of the pack show a placeholder icon, such as the Kerning City Station Attendant Hat: their records
  name icon files neither client has. None of them is sold in the Meret Market or comes from a capsule (BT-136, 1.2.3).
- Items with an old end date still print "This is a temporary item. (Expires ...)" in their tooltip; the date is not
  enforced (client text). The Meret Market's home page shows three grey banners without an image.
- Monster projectiles and placed attack areas are not simulated yet: Ninecap Copper's bolts and electrified floor,
  Pamokan's pillar rush (BT-68, 1.2.3). Summons that inherit a damage ledger (Gigantica's bodies) or share health are
  not done. Summon lifetimes read the data's mixed units by a rule (below 6,000 seconds, else milliseconds); some
  summons may stay or leave earlier than in the original.
- Debuffs that raise the damage you take (Frozen, Ice Block, Defense Down) do not apply yet; Iron Shield reduces every
  hit (the original's "some attacks cannot be blocked" is not known well enough to add; BT-30, BT-31). Some area
  skills that map scripts cast hit no one (in Palace Undercroft, the Blaknov No. 1 cars, Splint Coal Mine; BT-93,
  1.2.3).
- Map scripts: 199 conditions in 34 scripts of the content pack name a state that does not exist, and while such a
  condition is true the script never tests the next one of that step, so some scripted scenes may wait for good (the
  Reactor Site cutscene was one, now fixed; BT-131, 1.2.3).
- Quests and missions whose reward is relative EXP give none of it, daily missions among them (BT-81, 1.2.3; drop this
  line if its fix makes the candidate).
- Shadow Expedition gauges and score boards are full at once, so their waves and rewards are skipped (the maps still
  go on), and fame challenges cannot be lost (BT-26, BT-37, 1.2.3).
- Queen Bean Rumble: each round gives a tenth of the room's EXP and mesos, not its own reward (BT-84). The hall
  buttons' command bytes are inferred: if a button does nothing, the server log names the byte it got.
- Housing: an assistant bought before 1.2.2 whose NPC the data lacks (the 9 no longer sold) still shows only her
  pedestal; an assistant placed on an outdoor plot that is forfeited or expires stays there until the map closes
  (1.2.3). A design on a furnishing is lost when the furnishing goes back to storage (BT-09). Portraits, item designs
  and banners live in the program folder: backups and profiles do not include them, and uninstalling deletes them
  (BT-15).
- Guild-wide guild buffs do nothing yet: their cost for a one-player guild is still to be decided (BT-52). Using house
  furniture counts for no trophy and no Navigator daily (BT-74).
- Day and night lighting by the clock is not in yet (the clock and the /light test command are).
- Smaller gaps: 40 Unstable Power gear items start without their base stats (BT-04); 56 Global boxes that list an item
  more than once give only its last row (BT-117); part of the clear reward of two raids and of the 6-person Zakum and
  Tiaman chaos raids is empty (BT-21); An Unexpected Request (Epic, Lv 75) is never offered (BT-20); the field games of
  Fields of Perkau and four more maps never start (BT-94); 20 NPCs name a shop that does not exist, such as Monarre and
  Scarlet (BT-39); Black Market and birthday NPCs open only a plain dialog (BT-40); the tutorial skip is never offered
  (BT-02); some pack items have two names, one in the tooltip and one in chat (BT-10); equipping an item whose buff has
  stats can count some stats twice until the next stat change (BT-79); a reward of more than one stack into a nearly
  full bag can give part of it twice (BT-85); a skill tick the server rejects still costs (BT-75).
- A return to a map can still hang on the loading screen in a rarer case than the one 1.2.2 fixes (1.2.3).
- After a Steam update changes one of the game's archives, the servers start on the Global data while pack content
  stays in the client (a warning says so; BT-03). At the first 1.2.2 start, option lines set to true in a launcher.ini
  without a GameMode line are commented out once more before the launcher writes the mode, and lines earlier starts
  commented out stay so (BT-90).
- The Style Coin Shop (NA only) has no items offline. Overcome's cooldown cuts are not in the NA data (as in the
  original). Nine prizes of the NA Slime Variety Capsules (mounts such as Flurry of Leaves and Sullen Cat) cannot be
  bought on their own yet (1.2.3).

## Play check (maintainer)

Steps 1 to 16 were not run for 1.2.2 (the maintainer's decision, 2026-10-29): players test the release, and their
problem reports arrive automatically.

On the candidate, in Offline Adapted Mode with the content pack on unless a step says otherwise.
1. Update a 1.2.1 install: Play applies the 1.2.2 pack without a warning; launcher.ini keeps your option lines and
   gains a GameMode line. In Legacy Mode, /godmode is refused.
2. Meret Market: New shows 89 entries on 5 pages, Kirin first, and Featured 45 on 3 pages; the pet capsules are under
   Summons > Pets; buy and open a March 2022 Ducky Outfit Capsule and find one of its prizes in its own tab. With the
   content pack off, New lists only Kirin and Sun Wukong.
3. Batch 3: ride the Blue Tyrannosaurus, glide on the Pink Bean Car (40400055) and the Black Bean Car (drawn since
   pack b3e), wear a Cupcake Puff Hat and the Kerning Mail Carrier Hat (on the head since pack b3g), summon the
   Mabinogi Nao Plush Doll, place an Undersea Sofa in a home; the KR capsules' tiles have icons.
4. Housing: place Momo: she appears and walks about, and goes when you remove her cube; use the Imperial Medal: its
   buff starts, and it switches off for the next use.
5. Take damage and drink an Elixir from the inventory, then from a quickslot: the stack goes down by one each time.
6. Guild: check in and donate, quit, start again: both are kept and check-in is not offered again; buy a personal
   guild buff (60 minutes).
7. Summon a pet and walk away: it follows you. Die in Blazing Forest: you revive after the countdown.
8. Fairy Tree Lake (Only You Can Preserve the Forest): a rock spawns Wigglers.
9. Ignicore (Dark Forces: Study 1): at Pamokan's jump the floor breaks; Root of Darkness (An Unseen Threat): the gates
   break and the escort goes on.
10. Bosses (Zakum, Infernog, Gigantica, Balrog, Papulatus, Devilin Warrior, Masked King Chakoon, Pamokan, Ninecap
    Copper): they face you while they wait, turn before they cast, change phase at their health thresholds and not at
    once, and walk back when you lead them away.
11. Heavy Gunner: throw a Med Kit and step on it after a second: it heals.
12. Van Corta: pull the lever standing on the boards: the lift comes up from the bottom. Mounted, call the air taxi
    from the world map: it comes at the first press.
13. Allocate Intelligence and relog: the points still count; take damage and cast Warhorn: no refill.
14. Housing: with a full furnishing storage, buy a furnishing: it is refused and the mesos stay; with A Lesson in
    Product Placement active, replace a placed cube: it counts.
15. Queen Bean Rumble: lose a later round: the result window and the rewards; Continue starts at that round.
16. Suspend the game for 35 s in the world and in a busy map (Precipice Fortress), then resume: you stay connected.
    Leave a map for 10 minutes and come back: it loads. If the Kritias epic can be set up, Reactor Site's cutscene
    after The Daring Rescue ends.

## Published
