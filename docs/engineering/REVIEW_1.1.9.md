# Review of the 1.1.9 changes

Written 2026-10-17. Read-only review of `main` at `80d04c7` (`Maple2` at `aaaab0b0`, `_dl/Agarcium` at `0c1966d`)
against the `v1.1.8` tag. Scope: every change since 1.1.8, traced for bugs, regressions and release risks, with the
1.1.8-to-1.1.9 upgrade path, the update check under the new signing key, the `GameMode` defaults for existing saves,
and the packaging inputs restored after the factory reset. Nothing was edited, committed or packaged.

## Summary

1. **High.** Legacy Mode silently stays Adapted for every player whose `launcher.ini` lists the gameplay options.
   The 1.1.8 `launcher.ini.example` listed all seven as `=true` uncommented, and the Linux installer copied that
   example into `launcher.ini` on every 1.1.8 install. A single option line wins over the mode by design, so these
   players choose Legacy and get the Adapted rules with no warning.
2. **Medium.** 1.1.9 is the first Setup-only release and neither platform verifies the Setup: the Windows launcher
   only checks update zips against `release.json`, the Linux launcher ignores `release.json` altogether, and the
   1.1.8 launcher's "install with the full Setup" prompt has never run against a published release.
3. **Medium.** The shipped client patch is the 1.1.8 binary (it cannot be rebuilt, blocker 3) while the `Agarcium`
   source pointer moved one commit ahead; the package is not reproducible from the recorded sources and the release
   record does not say so.

Builds: 0 errors. Tests: servers 303 passed, 16 skipped, 0 failed; launcher 19 passed, 0 failed; KmsSplit self-test
passed. Details below.

## Change map

Superproject: 15 commits, 33 files, +2,793 / -104. `Maple2`: 7 commits, 43 files, +1,562 / -259. `Agarcium`: 1 commit.

| Area | Commits | Files | What changed |
|---|---|---|---|
| Launcher: signing key, Setup-only release | `5b937de` | `Launcher/UpdateKey.cs` | New ECDSA P-256 public key; comment states that launchers before 1.1.9 move through the full Setup |
| Launcher: signed release manifest | `3b196a5` | `Launcher/ReleaseManifest.cs` (new), `Launcher/UpdateChecker.cs`, `Launcher.Tests/*` (new) | `release.json` + `release.json.sig` verified when a release has them; update zip must be listed with matching size, and its sha256 is checked during download; 19 unit tests |
| Launcher: GameMode chooser | `1ca4440`, `1951069` | `Launcher/ModeChooser.cs` (new), `Launcher/LauncherForm.cs`, `Launcher/Settings.cs`, `Launcher/ServerHost.cs`, `launcher.ini.example` | "Choose how to play" window at every start; options become nullable and are only passed to the servers when an ini file names them; `OFFLINE_PROFILE` and `OFFLINE_PARTY_DUNGEONS` added; `offline-shop` mod always on, `offline-dungeons` and `offline-pets` follow the effective option |
| Launcher: robustness | `db8712f` | `Launcher/LauncherForm.cs`, `Launcher/ServerHost.cs`, `Launcher/Setup.cs` | Save integrity problem prompts before start; log writer tolerates late output; Setup swap puts the old folder back when the move fails |
| Linux launcher | `db8712f`, `1ca4440` | `linux/ms2offline.py`, `linux/README.md` | Staged full install with file-list check and path check; Setup payload read as a file slice; same GameMode/option rules as Windows (no window) |
| Servers: GameMode | `f6d3588b`, `aaaab0b0` | `OfflineProfile.cs` (new), `OfflineDungeons.cs`, `OfflineEconomy.cs`, `GameStorage.Mail.cs`, `DungeonManager.cs`, `Offline*.cs` in `Util`, `TriggerContext.cs` | Every offline flag resolves through `OfflineProfile.Flag` (mode default, per-flag override) or `OfflineProfile.Option` (same in both modes: mail, cheats); party-size door rule with the Cathy Mart exception |
| Servers: save path | `a6965454` | `GameSession.cs`, `StorageManager.cs`, `StorageInventoryHandler.cs`, `PacketRouter.cs`, `ChatUtil.cs` | Save before cleanup on session end; bank included in the atomic save and kept loaded; 5-minute autosave; lock RPC failure tolerated; a throwing packet handler no longer drops the session |
| Servers: economy validation | `e66cf648` | `Shop.cs`, `ShopManager.cs`, `BlackMarketManager.cs`, `ItemBoxManager.cs`, `ItemEnchantManager.cs`, `DbProvider.cs` | Price, quantity and inventory room checked before anything is consumed; refund when adding the bought item fails; black-market cancel mails the item in one transaction before removing the listing; typed defaults for columns the additive save upgrade adds |
| Servers: cheats | `05ca9d2c` | `CheatCommands.cs`, `ItemGrant.cs` (new), `ItemNameResolver.cs` (new), `ItemCommand.cs`, `FindCommand.cs` | `/give <name> [count]`; `/item` and `/find` need no admin rights but are gated by `OfflineCheats.Enabled`; `/item` without `--rarity` uses the data rarity |
| Servers: clock | `de966192` | `GameClock.cs` (new), `WorldServer.cs`, `GameEvent.cs`, `GameEventManager.cs`, `GameStorage.ServerInfo.cs`, `GameSession.cs` | One overridable clock; local stays local, UTC stays UTC |
| Servers: data sweep | `38fcc7db` | `Maple2.Server.Tests/Data/*` | Read-only integrity report over `maple-data.db`; skips without a DB |
| Client patch | `Agarcium` `0c1966d` | `mod_loader.cpp`, `xml_hook.cpp` | Keep original XML when a mod file has no root; report duplicate mod paths. **Not in the shipped DLLs** (see finding 3) |
| Packaging | `1d6ecf8`, `3b196a5`, `5b937de`, `dc27a5b` | `tools/package_release.py`, `tools/sign_update.py`, `.github/workflows/ci.yml` | `defaults.ini` now ships only `GameMode=Adapted`; `GameData/data-build.json`; `--manifest` writes and signs `release.json`; stale manifests removed; `sign_update` importable under `python -I`; CI builds and tests on a Windows runner |
| Data tools (not shipped) | `3a8dec6`, `600df7d` | `tools/KmsSplit`, `tools/ClientDiff`, `tools/M2dPack` | KMS converter with self-test; client diff and pack tools |
| Docs | several | `README.md`, `SECURITY.md`, `docs/ADAPTATIONS.md` (A19, A20), `docs/engineering/*` | Mode description, cheats, manifest, release record and 1.2.0 plan |

Working tree at review time, not in any commit: `docs/ADAPTATIONS.md` and `docs/engineering/RELEASE_1.1.9.md` edited;
`Maple2` has an uncommitted comment change in `OfflineDungeons.cs` and renamed test cases in `OfflineProfileTest.cs`
(no behaviour change); an untracked `tools/organize_meret_market.py` appeared during the review.

## Test results

Run on this PC on 2026-10-17 from the working tree described above.

| Command | Result |
|---|---|
| `dotnet build Maple2/Maple2.sln -c Release` | 0 errors, 22 warnings |
| `dotnet test Maple2/Maple2.Server.Tests -c Release --no-build` | 303 passed, 16 skipped, 0 failed (319 total, 20 s). The 16 skipped are the pre-existing pathfinding cases. One test logs an expected `FOREIGN KEY constraint failed` stack trace (atomic-save rollback case); it passes |
| `dotnet build Launcher/MapleStory2Offline.csproj -c Release` | 0 errors, 0 warnings |
| `dotnet test Launcher.Tests -c Release` | 19 passed, 0 failed |
| `dotnet run --project tools/KmsSplit -c Release -- --selftest` | passed |
| Python packaging self-tests | none exist: neither `tools/package_release.py`, `tools/sign_update.py` nor `linux/ms2offline.py` has a self-test entry point |
| GitHub CI (`.github/workflows/ci.yml`) | two green runs on `main` today (CI and CodeQL). `RELEASE_1.1.9.md` still says the workflow has not run |

Tests weakened or skipped in this release:

- `OfflineEditionTest`: the "unset" case for `OFFLINE_UNLIMITED_DUNGEON_CLEARS` became `"false"` because the unset
  default flipped to on. The unset behaviour is covered by `OfflineProfileTest` instead. Acceptable.
- `SaveSafetyTest.SetUp` now forces `MailNeverExpires = false`; the never-expires path in those tests is no longer
  exercised there (it is in `GameStorage.Mail` tests only through the option default). Minor.
- Readiness check 3 (a long-played real save under 1.1.9) was skipped by decision. The 1.1.9 save-path changes are
  behaviour changes (save before cleanup, bank inside the atomic save, autosave), not schema changes, so the risk is in
  the save ordering, not in the data format. See "Upgrade path" below.
- `Launcher.Tests` covers `ReleaseManifest` only. The `UpdateChecker` rules (missing `.sig`, version mismatch, unlisted
  or wrong-size zip, sha256 mismatch during download) and the Setup-only branch were checked by hand on a local feed and
  have no automated test.

## Findings

### 1. High: Legacy Mode is silently ineffective when launcher.ini lists the options

- Where: `Launcher/Settings.cs:150` (`Flag` returns the ini value whenever present), `Launcher/ServerHost.cs:87-107`
  (an option with a value is always sent and wins), `Launcher/LauncherForm.cs:469` (`ChooseGameMode` writes only
  `GameMode`), `linux/ms2offline.py:1288` (installer copies `launcher.ini.example` into `launcher.ini`),
  `v1.1.8:launcher.ini.example:7-13` (all options uncommented, `=true`).
- What breaks, for whom: a player whose `launcher.ini` names `GrindRates`, `DungeonAllDrops`, `UnlimitedDungeonClears`,
  `ClassDropsOnly`, `PetTaming`, `DungeonEpicGear` (any of them) picks Legacy Mode in the new window and still plays with
  those adaptations on. Affected: every Linux 1.1.8 install (the installer wrote the example as the ini), every Windows
  portable copy or install where the player created `launcher.ini` from the 1.1.8 example, and anyone who followed the
  README's "put the line in launcher.ini" advice. The window gives no hint; the session report only logs the mode.
- Reproduce: take a 1.1.8 `launcher.ini` from the 1.1.8 example, install 1.1.9 over it, choose Legacy Mode, kill a
  monster: scaled exp and extra loot still apply. `ServerHost` sends `OFFLINE_GRIND=true` next to `OFFLINE_PROFILE=Legacy`.
- Suggested fix (smallest): in `ChooseGameMode`, after a choice, collect the option keys that `launcher.ini` sets
  (reading the user file only, not `defaults.ini`) and, when any would contradict the chosen mode, show one message
  listing them with "Remove these lines so the mode applies?" and remove them on Yes (`SaveUserValue` can drop a line).
  Alternative that needs no prompt: treat an explicit value equal to the 1.1.8 default (`true`) as "not set" when
  `GameMode` is also absent from the user file, since such a file predates the mode. Document either way in A19.

### 2. Medium: the Setup is the only upgrade path and nothing verifies it

- Where: `Launcher/UpdateChecker.cs:66-90` (manifest consulted only for the update zip; a Setup-only release passes
  through untouched), `linux/ms2offline.py:753` and `:1258` (Linux downloads and installs the Setup payload with no
  signature or manifest check; `files.json` inside the package only proves self-consistency), `SECURITY.md` ("The Setup
  and the full zip are not yet checked automatically").
- What breaks, for whom: a replaced or corrupted Setup asset would be installed by every Linux launcher that auto-updates,
  and by every Windows player following the prompt, with no check. This is pre-existing, but 1.1.9 is the first release
  where every existing install must go through this path, so the exposure is total rather than marginal.
- Reproduce: edit one byte in a copy of the Setup payload past the zip directory, feed it to `ms2offline update --file`
  (or run the Windows Setup): both install it.
- Suggested fix for 1.1.9: publish the Setup sha256 in the release notes and in `release.json` (already listed there) and
  tell players how to compare. For the Linux launcher, a 15-line change: download `release.json` and `release.json.sig`
  when present, verify with `openssl dgst -sha256 -verify` against the public key read from `Launcher/UpdateKey.cs`
  (already bundled as text), and compare the Setup's sha256 before installing. For 1.2.0 the plan's Setup self-check.

### 3. Medium: shipped client patch is the 1.1.8 binary while the source pointer moved

- Where: `_dl/Agarcium` pointer `0c1966d` (mod-loader fix) versus `client-patch/*.dll` restored from the 1.1.8 payload
  (`RELEASE_1.1.9.md`, blocker 3 and 4: "cannot be rebuilt").
- What breaks, for whom: nothing for players today (no 1.1.9 mod needs the fix; `offline-shop` already shipped in 1.1.8).
  For the maintainer: the release record claims the package is built from the recorded commits, but the patch DLLs
  come from a commit one behind, and `GameData/data-build.json` does not cover them. A future bisect or rebuild would
  produce a different DLL and nobody would know which behaviour 1.1.9 players had.
- Suggested fix: add the sha256 of each shipped `client-patch` DLL and the `Agarcium` commit they were built from
  (the 1.1.8 pointer) to `RELEASE_1.1.9.md`, and either keep the submodule pointer at that commit until the link error
  (`C1047`) is fixed, or state in the release notes that the mod-loader fix is source-only in 1.1.9.

### 4. Medium: the 1.1.8 Setup-only prompt is untested against the real asset list and nags every start

- Where: `v1.1.8:Launcher/LauncherForm.cs:359-369` (unchanged in 1.1.9 at `Launcher/LauncherForm.cs:377`).
- Traced: the 1.1.8 `UpdateChecker` keeps the last asset whose name starts with `MapleStory2-Offline-Update-` and ends
  with `.zip`; with none, `FileUrl` is null and the prompt opens `html_url`. The logic is sound. Two risks remain:
  no release since signing began has been Setup-only, so this branch has never run for a player; and a 1.1.8 player who
  answers No sees the same prompt at every start until they install, with no "later" or "don't ask again".
- Release risk to guard: if any asset named `MapleStory2-Offline-Update-*.zip` is attached to the 1.1.9 release by habit,
  every 1.1.8 launcher downloads it at each start, rejects it ("not signed by the MapleStory 2 Offline developer") and
  tries again next time. The release doc states this; the upload step is manual.
- Suggested check before publishing (no code): point a 1.1.8 launcher at a local feed (`MS2OFFLINE_UPDATE_FEED`) that
  returns the planned 1.1.9 asset list (Setup, `release.json`, `release.json.sig`, nothing else) and confirm the prompt
  and the page link. Optional for 1.1.9's own launcher: log the postponed version and ask again only once per day.

### 5. Low: release.json is re-signed by hand for the renamed Setup

- Where: `tools/package_release.py:284` (`write_release` records the publish-folder name `MapleStory2-Offline-Setup-1.1.9.exe`),
  `README.md:10` (download link expects `MapleStory2-Offline-Setup.exe`), `RELEASE_1.1.9.md` ("regenerated for that
  file name").
- Impact: no launcher checks the Setup entry today, so a stale name only matters once the Setup self-check exists. The
  manual re-sign is an unscripted step that touches the private key.
- Suggested fix: an `--upload-name` option (or always emit the final name) so `package_release.py` writes and signs the
  manifest once with the names that will be uploaded.

### 6. Low: the data-build record is incomplete and points at a dirty tree

- Where: `tools/package_release.py:238-253` (`write_data_build`), `RELEASE_1.1.9.md` ("no input hashes:
  `MS2_DATA_FOLDER` was not set").
- Impact: R12-02's goal "record its inputs" is half met for 1.1.9: the table checksums and the `Maple2` commit are
  recorded, the input archive hashes are not. The recorded commit is `aaaab0b0` while the submodule working tree has
  uncommitted edits (comment and test names only, verified: no product behaviour change).
- Suggested fix: set `MS2_DATA_FOLDER` for the final packaging run so the archive hashes land in the record, and have
  `write_data_build` append `-dirty` when `git status --porcelain` in the submodule is non-empty.

### 7. Low: the 5-minute autosave runs on the field thread under the item lock and may block on the World lock

- Where: `Maple2/Maple2.Server.Game/Session/GameSession.cs:144` (`ScheduleRepeated` on the session scheduler, which
  the field loop drives), `:787` (`AcquireLock`, up to 5 tries with 500 ms back-off), `:977` (`SessionSave`).
- Impact: the full atomic save (thirteen components) runs synchronously inside the field tick every five minutes, and
  if the World lock is held (a migration in flight, or the leave-save racing the autosave) the field loop stalls for up
  to 2.5 s before the autosave gives up and records `lastSaveError`. Single player on SQLite, so this shows as a short
  hitch at worst; the leave-save is not affected (it holds the lock first). Verified: `Scheduler.Stop` on map change only
  pauses, so the repeated event survives, and `autosaveScheduled` prevents duplicates within one session.
- Suggested fix: none required for 1.1.9. If hitches are reported, run `SessionSave` from a thread-pool task and skip a
  tick when the previous autosave is still running.

### 8. Low: `/give` and `/find` treat `%` and `_` as wildcards

- Where: `Maple2/Maple2.Database/Storage/Metadata/ItemMetadataStorage.cs:67` (`EF.Functions.Like` with the raw text).
- Impact: `/give Red_Potion` matches more than the player meant and may list ten unrelated ids. Cheats only; cosmetic.
- Suggested fix: escape `%`, `_` and the escape character in the search text (`Like(name, pattern, "\\")`).

### 9. Info: other observations, no action needed for 1.1.9

- `DbProvider` dropped the `premium-market-item` id seed: the seed insert was guarded by "table exists", and no such
  table is part of the save (the Meret catalog lives in the data DB), so the line was a no-op. Harmless.
- `PacketRouter` now catches every handler exception instead of dropping the session. A handler that throws between
  consuming and granting leaves partial state, but the previous behaviour (disconnect, then save on disconnect) persisted
  the same partial state, so this is not a regression.
- Black-market cancel: the mail with the attached item is created in one transaction before the World listing is
  removed; if the World call fails the DB listing is deleted so a buyer's `GetBlackMarketListing` returns null. Sound.
- `ItemBoxManager`: `Consume([key, box])` validates both tags before consuming either, so the key cannot be lost on a
  missing box. Sound.
- `RELEASE_1.1.9.md` drift: "the workflow itself has not run on GitHub" (it has, two green runs today) and "the
  superproject's submodule pointers are not yet committed" (committed in `014d778`).
- `tools/organize_meret_market.py` appeared untracked during the review; `package_release.py` does not ship `tools/`,
  so it cannot leak into the package, but it should be committed or removed before tagging.

## Upgrade path, traced

- **Save**: no table or column was added between the two `Maple2` commits (`Maple2.Database/Model` unchanged), so a
  1.1.8 save opens under 1.1.9 without the additive upgrade running, and a save touched by 1.1.9 opens under 1.1.8 again.
  The typed defaults in `DbProvider.UpgradeSqliteSchema` matter only for future columns. Rollback from 1.1.9 is
  "run the 1.1.8 Setup again" (the 1.1.9 Setup deletes the `.old` folder after a successful swap); the save survives.
- **launcher.ini and install id**: `Setup.Install` moves the program folder aside, extracts next to it, writes the kept
  install id into the new folder and swaps; player data lives outside the program folder. If the swap fails the old
  folder is moved back (new in `db8712f`). `launcher.ini` is never touched except by `SaveUserValue` (adds or replaces
  one line, keeps the rest).
- **GameMode for existing installs**: `defaults.ini` ships `GameMode=Adapted`, so `GameModeSet` is true for every install
  and the chooser writes `launcher.ini` only when the choice differs from the effective mode. With no option lines in
  `launcher.ini`, Adapted reproduces the 1.1.8 rules exactly (every 1.1.8 default was `true`, and both `MailNeverExpires`
  and `CheatCommands` default on in both modes). With option lines, see finding 1.
- **Client mods**: `offline-dungeons` and `offline-pets` follow the effective option, `offline-shop` is always on,
  `offline-testkit` follows `TestKit`. The Setup replaces `client-mods` (mods re-enabled); the launcher re-applies the
  state at every start. Consistent with the server flags.
- **Update check with the new key**: a 1.1.8 launcher cannot verify anything signed with the new key, so the release
  must carry no update zip; it then shows the Setup prompt (finding 4). A 1.1.9 launcher seeing 1.1.9 does nothing
  (same version). A 1.1.9 launcher seeing a later release with `release.json`: refuses a missing `.sig`, a bad signature,
  a version mismatch, an unlisted or differently sized zip, and a download whose sha256 differs; without `release.json`
  it falls back to the zip's own signature (documented, to be tightened in 1.2.0).
- **Factory-reset inputs**: the signing key path (`..\Signing`), the `dotnet/` SDK copy, `Maple2/.env`,
  `SaveData/maple-data.db` (table checksums equal to 1.1.8), `client-mods/` and `client-patch/` were restored and are
  read by `package_release.py` as before. `MS2_DATA_FOLDER` is unset (finding 6). The runtime moves from 8.0.31 to
  8.0.32 because the restored `dotnet/` is the installed SDK; a patch-level change, documented.

## Not reviewed in depth

`tools/KmsSplit` (875 lines), `tools/ClientDiff`, `tools/M2dPack` and the data sweep test: developer tools that are
not shipped; only their self-tests were run. `docs/engineering/RELEASE_PLAN_1.2.0.md` and `UNIFICATION.md`: planning
text, not checked for accuracy. The in-game behaviour of Legacy Mode and `/give` was not play-tested here; the
maintainer's play test of 2026-10-17 stands.
