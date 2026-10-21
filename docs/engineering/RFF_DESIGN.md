# Reverse Fair Fight (RFF): damage-floor design note

Status: design only, no code (directive rule: evidence before code). Implements `docs/PROJECT_DIRECTIVE.md` section 4.
Implementation task: R12-30 in `RELEASE_PLAN_1.2.0.md` Deliverable 6. Written 2026-10-18 against the 1.1.9 tree and the
shipped `SaveData/maple-data.db` (ingested 2026-10-17). Every `file:line` below is relative to `Maple2/`.

## 1. What RFF is

A contextual damage floor for a solo player in Adapted mode: each direct hit a player lands on a monster is raised to a
minimum that depends on the encounter (its HP, its intended party size, its timer) and on the skill that hit (its damage
rate). Nothing is multiplied. Damage above the floor is untouched, so builds, gear and crits keep their differences.
Off under Legacy, off with more than one player in the field, off for damage-over-time, scripted, percent-of-HP and
constant damage, off while the target has an invincibility buff.

## 2. The damage pipeline today (evidence)

### 2.1 Direct hits

1. The client sends one `Skill.Attack.Damage` packet per attack point (`PacketHandlers/SkillHandler.cs:234-311`,
   `HandleTarget`); `session.Player.TargetAttack(record)` at `SkillHandler.cs:220` and `:311`.
2. `Actor.TargetAttack` (`Model/Field/Actor/Actor.cs:235-263`) builds one `DamageRecord` per attack point (`Model/Skill/DamageRecord.cs:27-42`,
   `Properties.Rate = attackMetadata.Damage.Rate`) and calls `target.ApplyDamage(this, damage, record.Attack)` per target
   (`Actor.cs:257`), then broadcasts the server-computed amounts (`Actor.cs:260`, `Packets/SkillDamagePacket.cs:46-65`). The client
   displays what the server sends, so a floored amount is what the player sees.
3. `FieldNpc.ApplyDamage` (`Model/Field/Actor/FieldNpc.cs:410-430`) handles corpses and aggro, then `base.ApplyDamage`.
4. `Actor.ApplyDamage` (`Actor.cs:96-189`) is the single place every direct hit goes through. Per hit (`attack.Damage.Count` loop,
   `Actor.cs:138-156`) it takes one of three branches:
   - `DamageByTargetMaxHp > 0`: percent of max HP (`Actor.cs:140-142`). Scripted; RFF must not touch it.
   - `IsConstDamage`: the data's fixed `Value` (`Actor.cs:143-145`). Scripted; RFF must not touch it.
   - otherwise `DamageCalculator.CalculateDamage(caster, this, damage.Properties)` (`Actor.cs:148`). **This is the RFF hook.**
   The sum is applied at `Actor.cs:158-168` (`Stats.Values[Health].Add`, `OnDamageReceived`), and `DamageDealers` is credited
   per caster (`Actor.cs:160-164`), which `FieldNpc.HandleDamageDealers` (`FieldNpc.cs:538`) uses for kill credit and drops.
5. `DamageCalculator.CalculateDamage` (`Util/DamageCalculator.cs:11-145`): block/evade/miss (`:13-26`, returns 0), bonus attack
   roll (`:29-30`), damage bonus incl. `BossNpcDamage` (`:33-36`), element and range (`:39-74`), crit (`:77-85`), invoke values
   (`:89-91`), defense and piercing (`:93-94`), resistance by attack type (`:100-128`), `Constant.AttackDamageFactor` (`:129`,
   `Model/Metadata/Constants.cs:85` says `= 4; // Unconfirmed`), shields consumed (`:132-142`, returns `Block`), floor of 1 (`:144`).
   The returned amount is therefore already mitigated: the right value to compare against the floor.
6. Region skills (`Model/Field/Entity/FieldSkill.cs:216`) call the same `target.ApplyDamage(Caster, damage, attack)` once per tick
   and target. Each tick is a direct hit with its own `Rate`, so it goes through the same hook.

Multihit evidence: player multihit skills ship as several attack points with the same rate each, not as `Damage.Count > 1`
(level-1 data: 592 attacks with `Count` 1, 214 with 0, one with 2). Examples: Tornado Slash 10100011 (3 points, rate 0.81 each),
Typhoon Slash 10100131 (5 points, rate 1.47 each), Drill Thrust 10100031 (3 points, rate 4.12 each). Each point is a separate
client packet and a separate `ApplyDamage` call.

### 2.2 Damage over time

`Buff.ApplyDotDamage` (`Model/Field/Buff.cs:217-251`) builds a `DotDamageRecord` (`Model/Skill/DotDamageRecord.cs:21-54`) which calls
`DamageCalculator.CalculateDamage` directly (`DotDamageRecord.cs:36`) and subtracts HP itself (`Buff.cs:230`). It never enters
`Actor.ApplyDamage`. So a hook placed at `Actor.cs:148` cannot reach DoT ticks, which is what the directive asks for. A hook inside
`CalculateDamage` would reach them, which is why the hook must not go there. 1,537 additional effects carry `Dot.Damage`
(example effect 10200112 level 1: poison, rate 0.04).

### 2.3 Immunity and invulnerability

- `AdditionalEffectMetadataStatus.Invincible` (`Model/Metadata/AdditionalEffectMetadata.cs:85`) is parsed (122 effects set it, e.g.
  40500021, 40500022, 49016011, 49126001) but no server code reads it: the only hit for `Invincible` in `Maple2.Server.*` is the
  opcode `SendOp.InvincibleEffect` (`Maple2.Server.Core/Constants/SendOp.cs:136`). Invincible targets take damage today.
- `NpcMetadataBasic.HitImmune` / `AbnormalImmune` (`Model/Metadata/NpcMetadata.cs:58-59`) are ingested (`Maple2.File.Ingest/Mapper/NpcMapper.cs:77-78`)
  and unused by the server. Values in data: 99 (629 bosses), 98 (113), 1 (87), 90 (14). Semantics unknown.
- Buff immunity (`Manager/BuffManager.cs:610`) is about resisting buffs, not damage.
- Shields: `CalculateDamage` consumes `Buff.ShieldHealth` and returns `DamageType.Block` (`DamageCalculator.cs:132-142`).
- Trigger conditions `NpcDamage` and `NpcHp` (`Trigger/TriggerContext.cs:99-121`) are stubs that compare the argument with 1.0 / 100,
  never the real HP. HP-driven scripted phases do not fire from HP today.
- AI node `MinimumHpNode` (`Model/Field/Actor/ActorStateComponent/AiState.cs:552-563`) heals an NPC back to a percentage after a hit
  takes it lower. This is the one server-side "cannot die yet" mechanic that works; it runs after `ApplyDamage`, so a floor cannot bypass it.

### 2.4 Encounter data

- `DungeonRoomMetadata` (`Model/Metadata/Table/DungeomRoomTable.cs:7-29`): `GroupType` (`:11`), `PlayerCountFactorId` (`:19`),
  `Limit.MinUserCount` / `MaxUserCount` (`:46-47`), `Limit.MaxRevivalCount` (`:60`), `DurationTick` (`:14`). `DungeonGroupType`
  at `Model/Enum/Dungeon.cs:11-27`. Ingest at `Maple2.File.Ingest/Mapper/TableMapper.cs:1584-1618`.
- The field knows its dungeon: `DungeonFieldManager.DungeonMetadata` (`Manager/Field/FieldManager/DungeonFieldManager.cs:13`), `DungeonId`
  (`:25`), `Size` = party size at creation (`:26`, from `ChannelService.Field.cs:27`, set in `Manager/DungeonManager.cs:263` and `:283`, sent at `:296`).
- Live player count: `IField.Players` (`Manager/Field/FieldManager/IField.cs:47`, `FieldManager.State.cs:34`); the factory sums lobby
  plus sub-rooms at `FieldManager.Factory.cs:287`. An NPC reaches its field through `Actor.Field`.
- Boss flag: `Npc.IsBoss => Friendly == 0 && Class >= 3` (`Model/Game/Npc/Npc.cs:11`). `BossNpcDamage` already keys on it (`DamageCalculator.cs:34`).
  Hostile NPC classes in the data: 1 (2,757 normal), 3 (258), 4 (82), 6 (391), 7 (112). `SpecialAttribute.EliteNpcDamage` exists
  (`Model/Enum/SpecialAttribute.cs:28`) but nothing maps a class to "elite".
- Door check reused by RFF's classification: `OfflineDungeons.TooFewPlayers` (`Maple2.Database/Storage/Metadata/OfflineDungeons.cs:26-27`),
  called at `DungeonManager.cs:289`.

What the shipped `dungeonroom.xml` actually says (101 rooms):

| GroupType | rooms | MinUserCount | MaxUserCount | DurationTick | PlayerCountFactorId |
|---|---|---|---|---|---|
| normal | 38 | 0 | 4 | 0 | 4001 (most) |
| raid | 14 | 0 | 4 | 0 | 0, 1200 or 3001 |
| chaosRaid | 16 | 0 | 10 (6 for the 6-man variants) | 900000 (300000 for 24090005) | 0 or 3004-3010 |
| guildRaid | 5 | 4 | 10 | 0 | 0 |
| event | 2 | 4 | 4 | 0 | 0 |
| fameChallenge, colosseum, darkStream, vip | 8 | 0 | 1 | 0 | 0 or 1000 |
| none, lapenta | 18 | 0 | 0, 4, 6 | 0 | 0 or 4001 |

Consequence: `MinUserCount` is 0 for every raid and chaos raid. Classification by `MinUserCount` (the 1.2.0 plan's assumption)
would call them solo content. RFF classifies by `GroupType` first and takes the intended party size from `MaxUserCount`
(`MinUserCount` only where it is above 0). `PlayerCountFactorId` references a table that is not ingested (`playerCountFactor`
appears in no other `table` or `server-table` row), so no per-player-count scaling data is available; R12-17's "if the table
exists" branch is dead until someone finds the client XML.

### 2.5 Feature gating pattern

`OfflineProfile` lives at `Maple2.Database/Context/OfflineProfile.cs:8-25` (not under `Maple2.Server.Core/Constants` as the task
assumed). `OfflineProfile.Flag("OFFLINE_X", adaptedDefault)` returns the env var when it parses as bool, else
`adaptedDefault` under Adapted and false under Legacy (`:15-19`). Existing users: `OfflineDungeons.Enabled` and `PartyDungeonsOpen`
(`OfflineDungeons.cs:15-19`), `OfflineGrind.Enabled` (`Maple2.Server.Game/Util/OfflineGrind.cs:27-28`). Config-file pattern:
`OfflineGrind.Load` reads `offline-grind.json` from `OFFLINE_GRIND_FILE` or next to the solution and parses it lazily
(`OfflineGrind.cs:128-141`); `OfflineEconomy.cs:27` and `OfflineTestKit.cs:29` do the same. The launcher passes `OFFLINE_PROFILE`
and one env var per flag (`Launcher/ServerHost.cs:87-100`, `Launcher/Settings.cs:45-48`). `OfflineProfileTest.cs` tests only the pure
`Resolve`, since the statics read the environment once.

RFF follows this exactly: `Maple2.Server.Game/Util/OfflineFairFight.cs` with `Enabled = OfflineProfile.Flag("OFFLINE_FAIR_FIGHT", true)`
and a lazily loaded `offline-rff.json` (`OFFLINE_RFF_FILE`). Legacy therefore gets no floor, and `OFFLINE_FAIR_FIGHT=false` turns it
off under Adapted (launcher key `FairFight`, same wiring as `PartyDungeons` at `ServerHost.cs:100`).

## 3. The floor model

### 3.1 Encounter classification (per target NPC, evaluated per hit)

| Class | Rule | Intended party size `P` | Target clear time `T` |
|---|---|---|---|
| dungeon | `target.Field is DungeonFieldManager d` | `d.DungeonMetadata.Limit.MinUserCount` if > 0, else `MaxUserCount`; `offline-rff.json` `dungeons[id].partySize` overrides | `dungeons[id].clearSeconds`, else `groupTypes[GroupType].clearSeconds`; capped at `DurationTick * timerShare` when `DurationTick > 0` |
| boss (open field) | not dungeon, `npc.Value.IsBoss` (Class 3, 4, 6, 7) | `classes[Class].partySize` (defaults: 3 → 1, 4 → 2, 6 → 4, 7 → 1) | `classes[Class].clearSeconds` |
| normal | Class 1, or `Friendly > 0`, or corpse | no floor | - |

The floor only ever applies to hits where `caster is FieldPlayer` and `target is FieldNpc`. Only dungeons of `GroupType` raid,
chaosRaid, guildRaid, worldBossDungeon and the two `event` rooms with `MinUserCount` 4 are on by default; `normal` dungeons get a
floor only if `groupTypes.normal` is present in the file (default absent: a level-appropriate solo player already clears them).

### 3.2 Expected contribution and the per-hit floor

```
requiredDps  = maxHp(target) / T                       // what a solo clear in T seconds needs
floorPerRate = requiredDps / referenceRatePerSecond    // damage one unit of skill rate must do
floorHit     = floorPerRate * property.Rate * floorShare
floorHit     = min(floorHit, maxHp(target) * maxHitShare)
effective    = max(calculated, floorHit)               // only when the hit was Normal or Critical with amount > 0
```

- `maxHp` is `target.Stats.Values[BasicAttribute.Health].Total` (NPC stats come from `NpcMetadataStat`, `Manager/StatsManager.cs:46-48`).
- `property.Rate` is the attack point's own rate (`DamageRecord.cs:38`). This is what makes multihit safe: Tornado Slash's three
  0.81 points each get a floor proportional to 0.81, so the skill's floored total equals that of a single 2.43 hit. No per-skill
  bookkeeping, no double counting across packets.
- `referenceRatePerSecond` is the assumed total rate a level-appropriate rotation lands per second (config, start at 2.5). It
  calibrates "a solo player who hits the floor on every hit clears in `T`". It is the one number that needs a play session.
- `floorShare` (config, 1.0; maintainer 2026-10-20, Q-M4-1: about 10 minutes for an under-geared solo raid boss; was 0.8) scales every floor.
- `maxHitShare` (config, 0.02) caps one hit at 2 % of max HP so a single floored hit cannot skip a phase threshold.
- Crits: the comparison is against the final amount. A crit above the floor keeps its value; a crit below it is floored like a normal
  hit (the crit label stays, so `OnOwnerAttackCrit` events at `Actor.cs:172-174` still fire).
- Misses, blocks and evades (amount 0, `DamageType.Miss`/`Block`) are never floored: mitigation and shields stay meaningful.

Worked numbers from the shipped data (`floorShare` 1.0, `referenceRatePerSecond` 2.5):

| Target | HP | Encounter | T | requiredDps | floor for a rate-1.0 hit |
|---|---|---|---|---|---|
| Devorak 23200016 in Shadow Altar 23007003 (chaosRaid, 6-man, timer 900 s) | 646,587,248 | dungeon | 720 (900 × 0.8) | 898,038 | 359,215 |
| Balrog 23090005 in Temple of Immortals 20004002 (raid, 4-man) | 42,196,479 | dungeon | 600 | 70,327 | 28,131 |
| Forgotten Vayar 23100006 in Baraska Ice Cave 20001001 (normal, 4-man) | 2,089,233 | off by default | - | - | 0 |
| Lava Golem 21000087 (Class 1, open field) | 109,567 | normal | - | - | 0 |

### 3.3 Where the clamp applies

One call, in the `else` branch of `Actor.ApplyDamage` right after `Actor.cs:148`:

```
(DamageType type, double amount) = DamageCalculator.CalculateDamage(caster, this, damage.Properties);
amount = OfflineFairFight.Floor(caster, this, damage.Properties, type, amount);
```

Why there and nowhere else:
- After mitigation: `CalculateDamage` has already applied defense, resistance, crit and shields.
- Not in `CalculateDamage`: `DotDamageRecord.cs:36` calls it for every DoT tick; the floor would multiply across ticks.
- Not in the `DamageByTargetMaxHp` or `IsConstDamage` branches (`Actor.cs:140-145`): those are the data's scripted amounts.
- Not in `FieldNpc.ApplyDamage`: the corpse and friendly paths return before damage; the floor must see the computed amount.
- `DamageDealers`, kill credit, drops and `OnDamageReceived` (`Actor.cs:158-168`) see the floored sum, so credit stays correct.

### 3.4 When the floor is off

`OfflineFairFight.Floor` returns the amount unchanged when any of these holds:

1. `!Enabled` (Legacy, or `OFFLINE_FAIR_FIGHT=false`).
2. `caster is not FieldPlayer` or `target is not FieldNpc`, or `Friendly > 0`.
3. `target.Field.Players.Count > 1` (live count, `IField.Players`). For a dungeon, count lobby plus `RoomFields` like
   `FieldManager.Factory.cs:287` does, so a second player waiting in the lobby also turns it off. Co-op therefore runs the original rules;
   a scaled floor for partial groups (`(P - N) / P`) is a later option, not in v1.
4. `type` is `Miss` or `Block`, or `amount <= 0`.
5. The target has any buff whose `Metadata.Status.Invincible` is true (`target.Buffs.EnumerateBuffs()`), or any buff with
   `Metadata.Shield != null` still active after the call (meaning the hit was absorbed).
6. The target is a corpse or `IsDead` (already handled before the hook).
7. The encounter classifies as `normal` (section 3.1).

Scripted damage, boss phases: trigger scripts never deal damage through this path (they spawn, buff and set HP bars; `NpcDamage`
and `NpcHp` are read-only conditions), the AI's `MinimumHpNode` heal-back runs after the hook, and `maxHitShare` bounds the HP a
single floored hit can remove. Phase thresholds are reached in order, only faster.

### 3.5 Data vs config

From data, per hit: `maxHp`, `property.Rate`, `DamageType`, `GroupType`, `MinUserCount`, `MaxUserCount`, `DurationTick`, `IsBoss`, `Class`,
`Friendly`, `Players.Count`, buff `Status.Invincible`, `Shield`.

`offline-rff.json` (all optional, defaults in code):

```json
{
  "referenceRatePerSecond": 2.5,
  "floorShare": 1.0,
  "maxHitShare": 0.02,
  "timerShare": 0.8,
  "groupTypes": {
    "raid":              { "clearSeconds": 600 },
    "chaosRaid":         { "clearSeconds": 900 },
    "guildRaid":         { "clearSeconds": 900 },
    "worldBossDungeon":  { "clearSeconds": 600 },
    "event":             { "clearSeconds": 600 }
  },
  "classes": {
    "4": { "partySize": 2, "clearSeconds": 300 },
    "6": { "partySize": 4, "clearSeconds": 600 }
  },
  "dungeons": {
    "23007003": { "clearSeconds": 720 }
  },
  "logFlooredHits": false
}
```

`partySize` is kept in the file for the later partial-group scaling and for the `PlayerCountFactorId` table if it is ever found; v1
uses it only in the log line. `logFlooredHits` writes one `Information` line per floored hit (skill id, rate, calculated, floor), the
evidence the validation scenarios need.

## 4. Unknowns (not verifiable from the tree or the DB)

1. `Constant.AttackDamageFactor = 4` is marked unconfirmed (`Constants.cs:85`): the absolute scale of player damage, and so the right
   `referenceRatePerSecond`, needs one measured play session (log calculated amounts of a known character against Balrog 23090005).
2. `HitImmune` / `AbnormalImmune` semantics (99/98/90/1) are unknown and unused server-side; RFF ignores them until someone decodes them.
3. `Status.Invincible` is not enforced by the server: RFF skipping those targets is correct but the targets still take ordinary damage.
   Enforcing invincibility is a separate fix (listed as a follow-up, not part of R12-30).
4. `NpcHp` / `NpcDamage` trigger conditions are stubs: HP-driven phases do not fire today, so scenario 7 can only verify that the floor
   does not change the order of AI-driven phases (`MinimumHpNode`), not trigger-driven ones.
5. The `playerCountFactor` table is absent from the ingest; the directive's "intended party size" therefore comes from `MaxUserCount`
   and `GroupType`, which is a proxy.
6. Whether region-skill ticks (`FieldSkill.cs:216`) use the same `Rate` scale as attack points is assumed, not measured.
7. The 6-man chaos raids (23007003, 23008003, 23024003-23026003, 23041003, 23043003) have `GearScore` limits 4,500-53,900 at the door;
   whether an "under-equipped" player can enter at all depends on that check, which this note did not trace.

## 5. Validation scenarios (directive 4.4)

All in Adapted with `logFlooredHits: true`; "Legacy equal" means the logged calculated amounts match a Legacy run of the same hits.

| # | Scenario | Setup | Expected |
|---|---|---|---|
| 1 | Under-equipped solo vs group boss | Level 50 character with the 1.1.9 test kit's base gear (`OFFLINE_TESTKIT`), solo in Temple of Immortals 20004002 (raid, 4-man), boss Balrog 23090005 (42,196,479 HP) | Most hits are logged as floored; the kill lands inside `clearSeconds` 600 ± 25 %; no single hit above 2 % of max HP; kill credit, drops and dungeon reward are granted to the player. Repeat in Shadow Altar 23007003 vs Devorak 23200016 once the gear-score door is understood (unknown 7) |
| 2 | Properly equipped solo vs the same boss | Same dungeon, character at the dungeon's `GearScore` 2,100 or above | Fewer than 5 % of hits floored; every non-floored hit is Legacy equal; clear time shorter than scenario 1 |
| 3 | Normal monsters | Lava Golem 21000087 (Class 1, 109,567 HP) and Leafy Turto 21000143 in their open fields | Zero floored hits; every hit Legacy equal |
| 4 | Damage immunity | Unit test: `FieldNpc` carrying buff 40500021 level 1 (`Status.Invincible` true) and a fake field with one player; plus an in-game check with `/buff 40500021 --level 1` (GameMaster command, `Commands/BuffCommand.cs:16-23`) on a Balrog spawned with cheats | `Floor` returns the calculated amount unchanged while the buff is active and floors again after it expires |
| 5 | Multiplayer encounter | Two clients on LAN (`BIND_IP=0.0.0.0`, ADAPTATIONS A1), both in 20004002; one stays in the lobby, one fights | Zero floored hits for both while `Players.Count` across lobby and rooms is 2; after one leaves, the next hit is floored. Unit test with two `FieldPlayer` entries in `Players` |
| 6 | Multihit and DoT | Tornado Slash 10100011 (3 points, rate 0.81) and Typhoon Slash 10100131 (5 points, rate 1.47) on Balrog; a poison DoT (effect 10200112) on the same target | Each point floored to `floorPerRate × rate × floorShare`; the three Tornado Slash floors sum to one rate-2.43 floor (unit test on `Floor` with the three rates); DoT ticks logged by `SkillDamagePacket.DotDamage` are Legacy equal and never appear in the floored-hit log |
| 7 | Multi-phase boss | A boss whose AI uses `MinimumHpNode` (pick one from the `ai` table during implementation; the node type is at `AiMetadata.cs:284`), fought under-geared solo | The HP floor set by the node still holds after a floored hit (HP is healed back on the next AI tick); phase order unchanged versus a Legacy run; no hit above 2 % of max HP in the log |

Acceptance for R12-30 is scenarios 3, 4, 5 and 6 as unit tests on `OfflineFairFight.Floor` with fake actors, scenarios 1, 2 and 7 as
recorded play sessions with the log attached to `ADAPTATIONS.md`.

## 6. Relation to R12-17 and R12-18

R12-17 (monster HP/attack factor per dungeon) and RFF are complementary: R12-17 shrinks the encounter, RFF lifts the player. Ship RFF
first: it needs no per-dungeon data, keeps monsters as designed, and is off for groups. R12-18's dungeon extract is superseded by the
table in section 2.4 for the party-size question; its trigger `count_users` probe is still needed for the dungeons whose scripts wait
for players.
