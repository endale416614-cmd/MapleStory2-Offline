# Adaptation log

Every intentional difference from the original online game, with reason and how to undo it.

| # | Change | Reason | Where / undo |
|---|---|---|---|
| A1 | Servers run on this PC, loopback only | no online service exists | `Target.BindIp`; `BIND_IP=0.0.0.0` restores LAN hosting |
| A2 | SQLite save files instead of MySQL | no database server for players to administer | `DB_PROVIDER=sqlite`; `mysql` still supported |
| A3 | Single local profile logs in without a password | one player, no account service | `OFFLINE_MODE=true` in `.env` |
| A4 | Client internet access blocked (DNS, TCP, UDP, Discord pipe) | no external dependency or publication of play | `offline_guard.cpp`; reverting `agarcium-offline.patch` |
| A5 | Meret bought with Mesos: "N-Meret Pouch" items (10/100/500/1000) sold in town potion shops at 200 Mesos per Meret | cash currency must be earnable; reuses the game's own pouch items | `offline-economy.json` (rate and shops); delete the file to disable |
| A6 | Meret Market lists every cash item of the NA client (7,657): all items from the cash source sheets (Skin, Skin_newGacha, Ride, Pet, DynamicAction, Badge, Maid, Chat, Gacha, EventCharge, BeautyCoupon, play, fishingRod), tab from the item's own sheet + category; original price where an original catalog has one, else the tab median. Left out: no client icon, currency-paying boxes, time-limited copies of permanent items, (Dummy)/Test placeholders, items the server cannot create | rotating and seasonal items must stay obtainable | `offline-meret-catalog.json`; delete to revert |
| A7 | All gacha capsule prizes directly purchasable, priced at the median original price of their store tab | random boxes must not be the only route | same file; prices editable per entry |
| A8 | Mounts split by their item sheet: ride skills (category SKB, item type 5 = air mounts) under "Air Mounts", the rest under "Ground Mounts" | the client's own item data says which mounts fly | catalog generator `tab_for_item` |
| A9 | Release server build (no developer +40 speed, free currency, 2x respawns, 1-year item lifetimes) | honest rules | launcher `ServerConfiguration=Release`; existing characters Sena/Zena keep their developer-build grants (kept, 2026-10-11) |
| A11 | Client data mod `client-mods\offline-shop`: NA feature flags `MeratMarketNewBM` and `MeratMarketClosing` (end-of-service store layout) set to 99 = off; closing-layout override removed from `meratmarketcategory.xml`. Server mirrors it: premium section gets the full tab list | the NA final client only showed Functional + Lifestyle tabs, hiding every cosmetic | delete the mod folder; `OfflineEconomy.Apply(MeretMarketCategoryTable)` |
| A12 | Items without NA client art left out of the Meret Market (208 among cash items) | the NA client has no icon art for them (KMS-only items); they would show blank | `ExcludedNoIconInClient` in the catalog |
| A13 | Play-testing kit (OFF by default): `launcher.ini TestKit=true` → `/testkit knight70` gives the level-70 Knight Fire Prism set (12 items) at rarity 6, +15 via the game's enchant table; those items ignore level/job limits (server `EquipManager` + client mod `offline-testkit`) | the game is balanced for parties; solo dungeon testing needs strong gear | `offline-testkit.json`; `TestKit=false` disables command, waiver and client mod |
| A14 | Guaranteed dungeon drops (launcher.ini `DungeonAllDrops=true`, on since 2026-10-12): in dungeon fields every eligible item of every drop group drops every time, for monster deaths/corpses, breakables, interact objects, script drops and the clear reward's global part (the individual part of the clear reward already gave every item). Kept: level, map and active-quest conditions, clear-count limits, rarity/quantity rolls. Not affected: per-hit drops, item boxes, gacha, fishing, housing, open-world fields | collect dungeon loot without repeat runs | `OfflineDrops`, `ItemDropManager.AllDrops`; `DungeonAllDrops=false` restores the original rates |
| A15 | Dungeon clear-reward limits raised to 9999 per week and per day (launcher.ini `UnlimitedDungeonClears=true`, since 2026-10-12). Also makes the 20 dungeons without a daily limit in the data pay out: the emulator's "daily clears < limit" check never passed for a limit of 0. Clears are still counted, so weekly united milestone rewards still trigger. The client's dungeon window may still show the original maximum | solo play, repeat runs for loot | `OfflineDungeons.Apply(DungeonRoomTable)`; `UnlimitedDungeonClears=false` restores the original limits |
| A16 | Grinding rates (launcher.ini `GrindRates=true`, since 2026-10-12; values in `Maple2\offline-grind.json`): monster exp scaled so same-level kills take ~200 per level (multiplier = original kills-per-level / 200, min 1x, max 50x: Lv20 3.5x, Lv40 19x, Lv50 20x); monster loot-roll chances x3; level-based upgrade-material drops per kill (Crystal Fragment Lv1+, Onyx Lv20+, Chaos Onyx Lv40+, Red/Blue/Green Crystal and Blue Gem Dust Lv30+, Metacell and Purple Gem Dust Lv50+; bosses x5 amount); treasure chests give bonus upgrade materials, level-matched enchant scrolls, re-rollers and socket scrolls (`Chests` in the same file, rare chests x3 chance and amount) | the game's exp curve and drop rates were tuned for an MMO economy with parties and the cash shop | `OfflineGrind`; `GrindRates=false` restores original rates |
| A17 | Pet taming (launcher.ini `PetTaming=true`, since 2026-10-14): wild-pet spawn chance x3 per spawn cycle (10% → 30%, capped at 100%); a full taming gauge (10,000 points: 10 Dryad Pet Candies) captures the pet without a snare; each throw of a candy or snare uses one up (their throw skills lack the data's useItem flag); client mod `offline-pets` lowers the level limit of the released taming items (candies, Lapis Master Snares, Dryad G1-G3 Snares) from 50 to 10 so shops that show only usable items list them. Original flow kept underneath: candy fills the gauge, a G1-G3 snare (box drops only) captures once it is full. Rarity on capture: the emulator's placeholder (Normal 24 / Rare 25 / Exceptional 25 / Epic 25 / Legendary 1 %, no original rule known) becomes 16 / 16 / 16 / 50 / 2 % | the original level-50 gate and box-only snares put taming out of reach for most of the game | `OfflinePets` + `tools/build_pet_mod.py`; `PetTaming=false` restores the original rules and disables the client mod |
| A10 | Text comparisons case-insensitive in SQLite | match MySQL behaviour the data relies on | `SqliteSupport.ApplyConventions` |

Bug fixes (not adaptations; behaviour now matches intent): Meret Market achievement check was inverted;
Meret was charged before the item existed; bulk-quantity entries were dropped at load; GameMeret purchases
were charged as Meret; negative or overflowing shop quantities paid the buyer.
2026-10-12: monster AI kept the AI script's retarget range (e.g. 10-1200) after a battle and used it to decide whether
to keep chasing, so monsters farther than that from the player dropped them and idled for good (Crisis in Henesys
waves never cleared); keeping a target now depends on sight only, the target node only picks the target, and is
cleared when the battle ends. Trigger cubes that are also liftable object weapons (Henesys bombs, 193 in all maps)
were dropped at import. Scripted "walk the player along a path" only moved the player to the path end when an NPC
stood nearby. The "monsters in combat" trigger check skipped every script-spawned monster. State skills (air mount
glides) were checked on the next server tick after casting and cancelled before the client reported the new state;
a glide also counts as still running while the client reports Jump/Fall.
2026-10-12: the generated Meret catalog sold every item at rarity 1; items now sell at their own default rarity
(option ConstantId, as the server's item boxes and all original Meret entries with a rarity do). Air mounts bought
at rarity 1 (Surfboard, Neon Hoverboard, Starsailor) were refused by the client.
2026-10-12 performance audit fixes: trigger and area-skill timers replayed every missed tick in a burst when a
player re-entered a map that had been empty (field clock stops while empty) - now resume from the current time;
trace log arguments (162 call sites) were built even with tracing off, and its dedupe only remembered one message per
trigger; every NPC spawn created three rolled items for unused debug markers; NPC navigation agents were never removed
from the crowd; discarded items were re-saved on every map change; an achievement DB context was never disposed;
with DungeonAllDrops every hit on a corpse dropped the full table (now once per player and corpse). Server log level
Verbose->Information, duplicate %TEMP%\Maple2 log files removed, launcher TriggerTrace off by default. Client patch:
mod XML documents leaked on each overridden read, CreateWindowExA hook crashed on ATOM/NULL class names, a hidden
console was always created, and a failed hook install left hooks pointing into the unloaded DLL (now unhooks and
closes the game).
2026-10-14: gear created without a rarity (drop-table entries without a grade, e.g. every raid weapon, armor piece and accessory inside the treasure selection boxes) came out Normal: for gear, Option.ConstantId is an option-table id, not a grade, so the fallback picked 1 although that gear only has option rows at Epic, Legendary or Ascendant. It now takes the lowest rarity its option tables (constant, static, random) define; boxes and outfits keep ConstantId 1-6.
