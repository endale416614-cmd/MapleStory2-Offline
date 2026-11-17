# Release 1.2.4: 1.2.3 checked and steadied, the players' reports, a player's list of 20 issues, fights tested without the game

Planned 2026-11-10, two days after 1.2.3, from the players' reports of 1.2.3, a player's list of 20 issues (2026-11-14)
and the risk that 1.2.3 shipped on tests alone. Theme: verify and stabilise 1.2.3, then add tests that run fights
without the game. Built 2026-11-16 without play checks: the game was not run for this release either, so players test
it, and their problem reports arrive automatically.

## Candidate
- 2026-11-16, unsigned: 1609 server, 216 launcher and 188 Linux tests pass, with the data tests on the shipped base DB
  and the pack tests on the shipped pack DB; the servers of the built package start and stop cleanly; the boss sweep
  runs all 3,979 monsters with a behaviour file for a minute each (6 known failures on its list). Game data: the 1.2.3
  base and pack DBs, unchanged (no data rebuild in 1.2.4); content pack 1.2.3-dev.b4b. Client patch: the one of 1.2.3.
  Set from 1.2.3: 54 files change and 2 are new (48 server files, the launcher, 3 JSON files beside the servers, the
  Linux launcher, README, VERSION); the privacy scan finds no content hits.
- `MapleStory2-Offline-Setup.exe` 1,169,721,274 bytes, sha256
  `8A16994A178D88CA03F61EF8C08023F4BA79DB8A591961489A99A82558C4C212`; `MapleStory2-Offline-Update-1.2.4.zip`
  78,950,664 bytes (needs 1.2.3; 56 files change and 2 are removed), sha256
  `CAC077734C092E455EAE58A72F32258E2CE510EFEE89382522E479729639B903`; `release.json` lists the Setup under both of its
  names and the update zip. Signed 2026-11-17 from the candidate's stage, nothing rebuilt; the signatures check with the
  public key the launchers of 1.2.2 to 1.2.4 ship; the privacy scan of both files finds no hits.

## Before you update
- Linux players whose launcher is from 1.2.2 or older: update it by hand once, before anything else:
  `git -C <clone> fetch origin`, then `git -C <clone> reset --hard origin/main`, where `<clone>` is the folder
  `git clone` made (or clone it again). From 1.2.3 on the launcher updates itself.
- Linux players who install a Setup by hand with a launcher from 1.2.2 or older: before you do, copy the folder
  `Maple2/Maple2.Server.Web/Data` of your program folder (`~/Games/MapleStory2-Offline` unless you chose another) to a
  safe place. The install replaces the `Maple2` folder, and with it your portraits, item designs, banners, guild marks
  and blueprints; your save is kept. After the first start, copy what that folder held (all but `system`) into
  `SaveData/ugc`.
- If you edited `damage-factors.json` (new in 1.2.4, next to the servers), copy it away first: every update replaces it,
  as it replaces the other JSON files beside the servers.
- Windows players on a version older than 1.2.3: install 1.2.4 once with the Setup below; it keeps your save. The
  automatic update needs 1.2.3.
- A copy that came from 1.2.2 by the update zip has no Verify Game Files entry in the Start menu; the launcher adds it
  at the first start of 1.2.4.

## What's new
### From your reports
- A start is no longer refused with "Another program is using a network port" when the listeners are the game's own
  leftover servers, also those of a second launcher copy or of the same folder written another way. For servers of
  another copy the launcher asks first, naming their folder, and ends them if you say Yes; a server that cannot be
  ended still refuses the start, and the refusal names each listener's folder.
- A game folder with none of the game's files (a client that was moved or half copied) gets its own message: the
  folder is not a complete MapleStory 2 client, choose the folder that holds MapleStory2.exe. Before, it got the [T9]
  message about patch files copied by hand.
- A start you cancel after the launcher started the game now closes that game, so the next start no longer says
  "MapleStory 2 is already running". A game left running by hand is offered to be closed instead of only refused.
- The session report says "The game crashed while closing" when the game exits with an error within 30 s of your Quit,
  and it names exit codes 1 and 0xCFFFFFFF as codes whose meaning is not known, not as a crash.
- At the end of a session a server with a process below it that Windows will not end is ended alone; the launcher no
  longer reports that as a problem.
- A battle mount that a quest script gives works when you were on a normal mount: the script takes you off it first
  (a story map refused it). A reconstruction; the original is not captured.
- The launcher adds the Start menu entries a newer version made and your copy lacks, Verify Game Files among them.
- More in the problem reports: the Game server writes the socket error and the seconds since the last packet when a
  game connection ends (for the sessions that closed with exit code 1); the Game, Login and Web servers write a line when
  they begin to stop; the "Unhandled item function" warning names the item; the Linux report shows the crash line of
  `offline_guard.log`, as the Windows one does.

### Combat and skills
From a player's list of 20 issues. These are changed in code and wait for a check in the game; the Known issues name the
ones that still wait for a check or a capture.
- A monster that chases you no longer walks through air and walls to another part of the floor when no path leads
  there: it stops where its path ends. This also keeps the Lost Vayar Guardian on its platform.
- Buffs that act once now act when they start, act again when you apply them again, and end at their duration; before,
  the action came about a second late or not at all. Berserker's Bloodlust heals right after the second hit, and Thief's
  Cunning is no longer lost to a skill used just before. A heal, cancel or buff end of this kind therefore comes about a
  second earlier than in 1.2.3.
- Passive skills you unlearn or lower no longer keep their buffs until the next login (Berserker's Deep Wounds was
  seen); switching the skill tab updates them.
- Knight's Guardian's Wings hits its targets and gives kill credit; before, it dealt 1 damage and gave none. Two monster
  piercing skills (50100951, 60128001) now hit as the monster.
- Knight's Shield Booster holds its resistance to the end of its motion, 1.29 s as captured, not 0.2 s.
- Shadow Power gives its max health in Shadow World maps, and Bloodlust's PvP copy no longer applies outside PvP and
  Shadow World maps: the rates the data ingest dropped (44 effects) are read from a file. From the same data: the 11 Arc
  PvP weapons carry effects (90009501, 90009502) that lower their weapon attack by 90 % in normal maps.
- Soul Binder's Fount of Renewal heals its caster when he stands in it, so a solo player gets the heal.
  A reconstruction past a capture of Divine Wave.
- Runeblade's Rune Balance (Strength to Intelligence) now adds magic attack: a stat converted into Strength,
  Dexterity, Luck or Intelligence counts toward attack. Pets you own hit with the player's damage factor, which changes
  nothing today (an owned pet cannot attack).
- The player and monster damage factors (4 and 2.8) are values in `damage-factors.json` next to the servers, read at
  start. The shipped values are the old constants, so no hit changes; a missing or broken file gives the old values and
  one log line.
- Correction to the 1.2.3 notes: monsters hit 1.40 times as hard as in 1.2.2 (factor 2.8 against 2), which is 40 %,
  not "about 45 %". Nothing changes in 1.2.4.
- Some world bosses and chaos raid bosses hit for a share of your max health whatever your defence (Lo and Moomoo,
  Lernos, the Baphomets, Acreon, the chaos wipes). That is the original's data, not a fault.

### Bosses and dungeons
- One faulty monster, skill region or portal no longer ends the Game server: the fault is logged with its ids and the
  field goes on (a region is dropped at its third fault), and a fault elsewhere in the field's update is caught too.
- Live skill regions are capped (1000 for one caster, 2000 for a field): at the cap the oldest goes.
- 13 bosses (the Blue Lapentas, Eupheria, Ishura, Landevian) get the shield-break phases their scripts expect, so
  Landevian's invincible windows end early. A reconstruction, with no capture.
- 38 spirits (such as the Grieving Spirit in Malevolent Manor and Chateau of Grief) summon on death, and the 6 Pink Bean
  Chaos statues now run their own behaviour. A reconstruction, with no capture.
- Pamokan no longer leaves, comes back at full health and repeats: a monster restored to full gets its reserved moves
  back at its next battle. Whether his jump breaks the floor blocks is still unchecked. The Lost Vayar Guardian's first
  jump now comes at its first window, so it jumps 1, 2, 3.
- Blaknov trains: monsters no longer cross between decks and into containers, and the Steamorbs placed beside the trains
  are removed.
- Moonlight Fortress: the ship comes back for a third ride.
- Twinkling Path: the monsters that chase you stay at the bridge instead of walking back to their spawn.
- At a dungeon's revival limit the revive button answers: "You have no safe revivals left in this dungeon. Choose Leave
  the Dungeon." (English only). What the client itself shows at the limit is not seen.
- A map script's check for a failed run now works in the 11 maps that use it, such as the Orbis-Class Ship, Temple of
  Time and Tiaman Time Tunnel rooms.
- Nine monsters (Specter of the Past, Grub, Condor, Mason and others) that threw an error when they spawned now spawn.
- A script's "take the buff off the players in the box" no longer takes it off the box's monsters (17 calls in 6 maps).

### Quests, maps and rewards
- The Fallen Man of the Lv 60 Assassin's job chain (map 52000159): his death-pose loop is dropped, so he may now be seen
  and talked to. This is also the test of whether the loop hid him.
- 85 boxes, the Level 10 to 45 Reward Boxes among them, give items at the grade the client shows, not common: 1,965
  entries change, all upward.
- Mission boxes 30000928, 30000932, 30000933 and 30001274 open and give their item.
- Trophies that count dismantling, lapenshard upgrades and buying with Valor Tokens and Treva are counted where the
  action happens (6 condition types, 33 conditions). The three "failed" kinds (a socket unlock, a gem upgrade and a
  lapenshard upgrade that fail) are not counted: none of the three can fail in this server.
- Pet fusion refuses before it takes anything when the pet is at its last level or cannot level, and every refusal
  answers; before, the fodder and mesos were taken for nothing.

### Launcher, saves and Linux
- The update from 1.2.3 is 79 MB instead of about 585 MB: it carries only the files that changed since 1.2.3. A copy on
  1.2.2 or older needs the Setup once.

### Under the hood
- Tests that run fights, server starts and the data registries without the game.

## Known issues
- A game folder that is not Steam's unchanged client (its archives have a header the launcher does not know) is refused
  the unified content: "The unified content does not fit this game client", and the game runs without it, at every
  start. 13 reports of 3 installs met it. Only Steam's unchanged client gets the pack (BT-250).
- Boxes that open to nothing: 14 box items whose drop box has no rows, one box whose drop box is missing, one gacha
  (1003001), and on the content pack 5 gacha drop boxes that each lack one item. They need a data rebuild (BT-283).
- A start is still refused with "Another program is using a network port" when a leftover server cannot be ended (one
  started as administrator, for example): end the process the message names in Task Manager, or restart the PC.
- The game can close with exit code -1 before the login, or with exit code 1 in a healthy session; the cause is not
  known. Send the problem report (BT-292, BT-231, BT-229).
- A Game server can end while the game starts, with no error line (BT-248).
- A `damage-factors.json` you edit is overwritten by every update; keep a copy.
- From the player's list, still waiting for a check in the game: the Song of the Sea quest dungeon (BT-257), the quest
  Safety First (BT-258), a block you cannot see in Muros Studio (BT-265) and Soul Binder's Static Flash showing no
  debuff (BT-271). The Knight's counterattack under Iron Shield never triggers; it needs a capture of a blocked hit
  first (BT-263).
- Pamokan's jump may still not break the floor blocks (BT-55). Whether the Fallen Man can be seen is not checked
  (BT-160).
- A solo chaos raid is likely to fail on its clock (9 chaos raids at 15:00, 2 lapenta rooms at 7:00); after 0:00 the way
  out is Leave the Dungeon, which the client may not show (BT-240, BT-236).
- The 10 Shadow Gate bosses open no room portal (BT-245). Eupheria's mines hit nobody when stepped on (BT-289). More
  Blaknov monsters stand off the walkable floor (BT-276). Shadow Expedition maps 02000461, 02000403 and 52100041 may
  stall below their gate (BT-241). A fusion that reaches level 50 keeps its surplus EXP (BT-242). Rest EXP is shown and
  never given (BT-275).
- Translation gaps: 18 items read Korean in their tooltips (BT-285); the set bonus lines of 90 sets are Korean (BT-286);
  22 items, 3 titles and 6 mount names show their string key as the name (BT-287); 8,768 quest talk lines are string
  keys, as in every English client (BT-288).
- The open issues of 1.2.3 that are not named here are still open.

## Play check
Not run for 1.2.4: no game run, no core-loop run and no fair-fight run. Neither 1.2.3 nor 1.2.4 was played by the
maintainer before release. The tests prove the rules and the code paths they call; they do not show that a fix looks or
feels right in the game. The notes above say what was changed, not that it was seen working. Several changes are
reconstructions with no capture (the shield-break phases, the spirits' summons, Fount of Renewal, the battle mount), and
the fixes of the player's list are checked in code and data only. Players test the release, and their problem reports
arrive automatically.
