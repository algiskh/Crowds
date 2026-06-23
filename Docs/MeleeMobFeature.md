# Melee Attacker Mob — Reference

A mob that attacks the player with a **telegraphed melee strike**, just like the player's own
melee, instead of dealing instant contact damage. The attack runs in three phases — **windup →
strike → cooldown** — all played by a **single `attack` animation**. In every other respect it's
an ordinary mob (health, speed, loot, modifiers, collider), because its config inherits from
`MobConfig`. This doc is the source of truth so the code doesn't need re-exploring.

## Why "categorized" melee attacks

The melee attack itself is a reusable **`MeleeConfig`** ScriptableObject — the *same type the player
uses*. Think of each `MeleeConfig` as a category of melee attack (e.g. `PlayerKnife`, `ZombieClaw`,
`BruteSlam`): it owns damage, reach, hit radius, target type, on-hit modifiers/debuffs, and the two
phase timings. A `MeleeMobConfig` just points at one. The same attack definition can be shared by the
player and any number of mobs; what each one *hits* is decided by the config's `TargetType`.

## Data flow

```
mob spawns (MobConfig is MeleeMobConfig) ──► MobSpawnSystem adds MeleeAttackerComponent (State=Chase)
Chase   (dist > AttackRange) ──► normal pathfinding to player (MobPathfindingSystem/MoveSystem)
Windup  (dist ≤ AttackRange) ──► stop + face player + play "attack"; wait MeleeConfig.Delay
   └─ on windup end ──► RequestMeleeComponent at point in front of mob (Delay=0) ──► EnterCooldown
Cooldown                     ──► stay stopped MeleeConfig.Cooldown sec (same "attack" clip recovers)
   └─ on cooldown end ──► EnterChase (anim "run" + immediate path recalc) ──► re-evaluate next frame
RequestMeleeComponent ──► MeleeSpawnSystem: radial damage to entities of MeleeConfig.TargetType in radius
```

## Systems & frame order

Registered in `EntryPoint.RegisterSystems()`, right next to the grenadier:

```
… MobPathfindingSystem → GrenadierSystem → MeleeAttackerSystem → MoveSystem …
… MeleeSpawnSystem … → CollisionSystem → … → DamageSystem
```

- `MeleeAttackerSystem` runs **after** `MobPathfindingSystem` and **before** `MoveSystem` so that in
  Windup/Cooldown it can clear the player-path waypoints the pathfinder just laid down (= the mob
  stops). In Chase it leaves the path alone, so the standard move stack walks the mob to the player.
- `MeleeSpawnSystem` (unchanged) consumes the `RequestMeleeComponent` the strike emits — the exact
  same path the player's melee uses.

| System | File | Responsibility |
|--------|------|----------------|
| `MeleeAttackerSystem` | `ECS/MeleeAttackerSystem.cs` | The Chase/Windup/Cooldown state machine. On windup end emits a `RequestMeleeComponent` (in front of the mob, toward the player, `Delay=0`). Drives the `attack`/`run` animations via `AnimationStateComponent`. |
| `MeleeSpawnSystem` | `ECS/MeleeSpawnSystem.cs` | Ticks `RequestMeleeComponent.Delay`, then deals `MeleeConfig.Damage` to every `HealthComponent` of matching `TargetType` within `Radius` of `Position`, applies on-hit modifiers/debuffs, spawns the hit effect (`MeleeConfig.Id`), a damage decal, and plays `MeleeConfig.AttackSoundId` (if set) at the strike point. |

## The three phases live in one animation

All three phases are a single non-looping **`attack`** clip, triggered **once** when entering Windup:

```
|<------------- "attack" clip ------------->|
| windup (pre-attack) | strike |  recovery  |
|  MeleeConfig.Delay   ^ damage | MeleeConfig.Cooldown
                       (RequestMeleeComponent emitted here)
```

- **Windup** = `MeleeConfig.Delay` seconds. Mob is stopped and faces the player; the clip plays its
  pre-attack portion. No damage yet.
- **Strike** = the instant windup ends. The mob emits the melee request at a point `MeleeConfig.Range`
  in front of it toward the player; `MeleeSpawnSystem` resolves the radial hit that frame.
- **Cooldown** = `MeleeConfig.Cooldown` seconds. Mob stays stopped; the same clip plays its recovery
  portion. The animation is **not** re-triggered here.

Author the clip so its strike frame lands at ~`Delay` and its full length is ~`Delay + Cooldown`.
Between two separate attacks the mob passes through `Chase` for a frame (anim `run`) — this is
deliberate: `SimpleAnimator` ignores a re-request of the state it's already playing, so the brief
`run → attack` transition is what makes the clip replay from the start each attack.

## Configs

### `MeleeMobConfig : MobConfig` (`SO/MeleeMobConfig.cs`)
Adds only the engage behavior on top of a normal mob:

| Field | Default | Meaning |
|-------|---------|---------|
| `AttackRange` | 2 | distance at which the mob stops and attacks. Beyond it, it chases. Keep `≥ MeleeConfig.Range`. |
| `MeleeConfig` | — (Required) | the melee attack category to perform (see below). |

### `MeleeConfig` (`SO/MeleeConfig.cs`) — the shared, categorized attack
Already existed for the player; now also used by mobs. Phase timings clarified with tooltips:

| Field | Meaning |
|-------|---------|
| `Id` | also used as the **hit effect id** (`EffectsHolder`) spawned at the strike point |
| `Damage` | damage dealt to each target in radius |
| `Range` | how far in front of the attacker the hit point is placed |
| `Radius` | hit sphere around that point |
| `TargetType` | who it hits (flags). **Mob attacks must include `Player`**; the player's config targets mobs. This is the "categorization". |
| `Modifiers` | on-hit modifiers applied to targets |
| `Debuffs` | modifiers applied to the **attacker** (self-buffs on swing) |
| `Delay` | **windup / pre-attack** seconds before damage |
| `Cooldown` | **recovery** seconds after the strike (player: gap between swings) |
| `AttackSoundId` | sound id (in `SoundHolder`) played at the strike, mirroring weapon sound ids. Empty = silent. Played positionally via `AudioSource.PlayClipAtPoint` (works for both player and mobs, which have no shared `AudioSource`). |

## Components (`ECS/Components.cs`, `#region MeleeAttacker`)

| Struct | Kind | Fields / notes |
|--------|------|----------------|
| `MeleeAttackerState` | enum | `Chase`, `Windup`, `Cooldown` |
| `MeleeAttackerComponent` | per-entity | `MeleeMobConfig Config; MeleeAttackerState State; float Timer;` — `Timer` counts down the windup, then the cooldown. Added by `MobSpawnSystem` only when the spawned `MobConfig is MeleeMobConfig`. |

`RequestMeleeComponent` (the player/mob shared strike request) is unchanged:
`int SourceEntity; Vector3 Position; float Delay; MeleeConfig Config; float Rotation;`.

## Integration touch-points (modified files)

- `MobSpawnSystem.cs` — `else if (mobConfig is MeleeMobConfig)` → adds `MeleeAttackerComponent`
  (`State=Chase`), mirroring the grenadier branch.
- `CollisionSystem.cs` — in `PlayerVsMob`, skips contact damage for any mob that has a
  `MeleeAttackerComponent` (its damage comes solely from the telegraphed strike; otherwise it would
  double-dip).
- `EntryPoint.cs` — registers `MeleeAttackerSystem` between `GrenadierSystem` and `MoveSystem`.
- `SO/MeleeConfig.cs` — added tooltips clarifying `Delay` = pre-attack and `Cooldown` = recovery.
- `Animation/AnimationType.cs` — uses the existing `AnimationType.Attack` (`"attack"` state). No change.

## Unity setup checklist

1. Create a **`MeleeConfig`** asset (right-click → Create → Scriptable Objects → MeleeConfig). Set
   `Damage`, `Range`, `Radius`, `Delay` (windup), `Cooldown` (recovery), optional `Modifiers`/`Debuffs`,
   and **`TargetType` including `Player`** so the swing hurts the player. Optionally add a hit effect in
   `EffectsHolder` under the same id as `MeleeConfig.Id`.
2. Create a **`MeleeMobConfig`** asset (right-click → Create → Scriptable Objects → MeleeMobConfig).
   Fill the usual mob fields (health/speed/loot/etc.), set `AttackRange` (`≥ MeleeConfig.Range`), and
   assign the `MeleeConfig` from step 1.
3. Build/assign its **`Mob` prefab** with an `Animator` whose states include **`run`** and **`attack`**.
   The `attack` state must be **non-looping**, with its strike frame at ~`Delay` and total length
   ~`Delay + Cooldown`. State names must match `AnimationTypes` in `Animation/AnimationType.cs`.
4. Add the `MeleeMobConfig` to a spawn source (`MobConfigHolder` / spawn lists) like any other mob.
5. Verify in Play mode: the mob chases, stops at `AttackRange`, plays the windup, lands damage on the
   player at the strike frame, recovers during cooldown, then re-engages. Confirm it does **no** contact
   damage outside the swing.

## Reuse / extension notes

- The strike reuses `RequestMeleeComponent` + `MeleeSpawnSystem` verbatim — no special-casing for
  source type. Player vs mob differ only by the `MeleeConfig.TargetType` and the spawn point.
- Want a multi-hit or different reach mid-combo? It's a `MeleeConfig` change — no code.
- For a mob that both melees *and* keeps distance, this pattern and the grenadier's are siblings; a
  config can be one or the other (the `MobSpawnSystem` branch is `if/else if`).
- No automated tests in this project — verify feel (windup readability, reach, cooldown) in Play mode.
