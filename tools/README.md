# Developer tools

None of these tools is shipped to players.

- `ClientDiff`: read-only comparison of `Xml.m2d` archives from different clients, plus extraction of chosen path prefixes.
- `KmsSplit`: converts the consolidated KMS XML into per-id NA-style XML, and verifies, validates and self-tests that conversion.
- `M2dPack`: writes `.m2d`/`.m2h` archives by overlaying a folder of files on a base archive, and verifies, inspects and self-tests the result.
- `build_dismantle_mod.py`: builds the offline-dismantle client mod that allows dismantling (Navigator) gear items. Run `ClientDiff --extract <client>/Data/Xml.m2d <dir> item/1/` first, then `python tools/build_dismantle_mod.py --xml <dir>`; run it before packaging when client-mods is rebuilt.
- `organize_meret_market.py`: sorts the offline Meret Market into tabs (capsules in Featured, pet capsules in Pets,
  Design Templates, Furniture) in `Maple2/offline-meret-catalog.json` and the `offline-shop` client mod, and adds the
  rows of two lists to the catalog: `data/meret-new.tsv`, what the release adds to the market, which the New tab
  (tab 11) shows until the next release's list replaces it, and `data/meret-capsule-prizes.tsv`, the prizes of the
  capsules the market sells, each in its own tab. A row costs its original price, else 380 Meret for a gacha capsule,
  else the price of its rarity. Boxes that open to nothing, assistants whose NPC the data lacks and emotes the client's
  Emotes window does not list leave the market, and a row the server cannot use is left out; the run says why. Featured
  and New hold at most 90 entries each, the 5 pages of 18 tiles the client shows. `--emote-strings` names the client's
  `string/en/kordynamicaction.xml` (the content pack's when it ships one, else the NA client's), whose skills the Emotes
  window lists; every run but `--selftest` needs it. `--check` reports what would change and exits 1 if anything would;
  `--selftest` checks the rules on built-in examples, without a data file or the catalog; `--db` names the game data
  (default `SaveData/maple-data.db`). Run it before packaging whenever `client-mods/` is restored, and after a change to
  either list.
- `data/shop_item_names.py`: builds the offline-shop client mod's item names (`client-mods/offline-shop/Data/Xml/string/itemname.xml`) from the content pack's names and `data/shop-item-names.tsv`, and checks that every item of the shipped batches has a name. Run it after every content-pack build and whenever `client-mods/` is restored.
- `data/copy_names.py`: names the KMS copies after the item they copy, and an item that shows Global's name of another item after the Taiwan English client, or without Taiwan names after its look preset or its own English server name (NM-03). Its reviewed fix list `FIXES` corrects Global's own names that name another slot, for the Global item and for its copies (NM-04); a fixed item's row in `data/shop-item-names.tsv` must carry the fixed name. It writes `data/copy-names.tsv` from the selection, the pack's server and client name files and the Global, KMS and Taiwan names, and applies that table to an `itemname.xml`; the pack build applies it to the server stage's and the client's. The apply writes nothing when a listed id has a key element it cannot rename. Measure only from name files the table has not been applied to.
- `data/shown_names.py`: gives every item, NPC, map and interact object the pack ships an English name (NM-05, maintainer D-16): its client or server English name, an English line behind a feature or locale gate, the Taiwan English client's name, or else a reviewed translated or invented name. It writes `data/shown-names.tsv` from a pack build that no name table has touched, keeps the table's reviewed rows, and writes nothing while a shipped id has neither a name nor a reviewed row. Its apply step writes the names into a string file and adds a key line without gates where an id has none; the pack build applies it, after the copy-names table, to the server stage's item and NPC names and to the client's item, NPC, map and interact object names. NM-06 (BT-110, BT-106) adds the client's NPC titles, NPC action names and NPC script lines (Global's `npctitle.xml`, `actionname.xml` and `scriptnpcnam.xml`) wherever the client shows one as a string key or not in English, and the pack build applies the table to copies of those three files as well.
- `package_release.py`: builds the player release folder, installer, full zip and update zip.
- `sign_update.py`: signs an update zip with the update signing key.
