# Ranged (Shooter) Mob — Reference

A mob that attacks the player from a distance with a **telegraphed shot**, the ranged sibling of the
melee mob (`Docs/MeleeMobFeature.md`). It runs the same three phases — **windup → fire → cooldown** —
all played by a single `attack` animation. In every other respect it's an ordinary mob (health,
speed, loot, modifiers, collider), because its config inherits from `MobConfig`. Its bullet reuses the
**player's whole bullet pipeline** (`RequestSpawnBulletComponent` → `BulletSystem` → `BulletOverlapSystem`
→ `CollisionSystem`); the only differences are a `Team` flag and the spawn point. This doc is the
source of truth so the code doesn't need re-exploring.

## The key idea: bullets now have a Team

The whole feature hangs on one new field, `BulletComponent.Team` (`enum BulletTeam { Player, Enemy }`,
default = `Player` so the existing player path is correct without touching it):

- **Player** bullets hit **mobs** and **breakables** (exactly as before).
- **Enemy** bullets hit the **player** (and are blocked by breakables as cover). They never hit mobs —
  no friendly fire.

`Team` is stamped at spawn from `RequestSpawnBulletComponent.Team`: `WeaponFireSystem` sets `Player`,
`RangedAttackerSystem` sets `Enemy`. Detection then branches on it (see below).

## Data flow

```
mob spawns (MobConfig is RangedMobConfig) ──► MobSpawnSystem adds RangedAttackerComponent (State=Chase)
Chase   (dist > AttackRange) ──► normal pathfinding to player (MobPathfindingSystem/MoveSystem)
Windup  (dist ≤ AttackRange) ──► stop + face player + play "attack"; wait RangedMobConfig.WindupDelay
   └─ on windup end ──► RequestSpawnBulletComponent (Team=Enemy) from muzzle toward player ──► EnterCooldown
Cooldown                     ──► stay stopped RangedMobConfig.Cooldown sec (same "attack" clip recovers)
   └─ on cooldown end ──► EnterChase (anim "run" + immediate path recalc) ──► re-evaluate next frame

RequestSpawnBulletComponent ──► BulletSystem spawns the pooled bullet (Team + Radius + ShotDebuffs)
Bullet moves (MoveSystem) ──► BulletOverlapSystem: Enemy bullet → swept distance test vs the single player
                                                    Player bullet → mob/breakable collider-map test (as before)
BulletOverlapComponent ──► CollisionSystem.BulletVsPlayer: RequestDamage on player + hit fx + dispose bullet
```

## Systems & frame order

Registered in `EntryPoint.RegisterSystems()`, right after the melee attacker:

```
… MobPathfindingSystem → GrenadierSystem → MeleeAttackerSystem → RangedAttackerSystem → MoveSystem …
… WeaponFireSystem → BulletSystem → BulletOverlapSystem → CollisionSystem → … → DamageSystem
```

- `RangedAttackerSystem` runs **after** `MobPathfindingSystem` and **before** `MoveSystem` (same slot
  logic as the melee mob): in Windup/Cooldown it clears the player-path waypoints so the mob stops; in
  Chase it leaves the path alone so the move stack walks it toward the player.
- The shot it emits is picked up by `BulletSystem` **the same frame** (it runs later in the list), so
  there's no one-frame lag between windup-end and the bullet existing.

| System | File | Responsibility |
|--------|------|----------------|
| `RangedAttackerSystem` | `ECS/RangedAttackerSystem.cs` | The Chase/Windup/Cooldown state machine. On windup end emits a `RequestSpawnBulletComponent` (`Team=Enemy`) from the mob muzzle toward the player, and plays `GunConfig.FireSoundId` positionally (`AudioSource.PlayClipAtPoint` — mobs have no shared `AudioSource`). Drives the `attack`/`run` animations. |
| `BulletSystem` | `ECS/BulletSystem.cs` | Unchanged spawn path; now also stamps `Team`, `Radius` (see bug-fix note) and `Modifiers` onto the bullet. Team-agnostic — one code path for both sides. |
| `BulletOverlapSystem` | `ECS/BulletOverlapSystem.cs` | Team-aware hit detection (see below) + an early-out that skips the per-frame mob collider-map rebuild when no bullets are in flight. |
| `CollisionSystem` | `ECS/CollisionSystem.cs` | `BulletVsMob` gated to `Player` bullets; new `BulletVsPlayer` region damages the player from `Enemy` bullets; ranged mobs skip contact damage in `PlayerVsMob`. |

## How enemy bullets hit the player

The player is a **singleton with no registered `ColliderComponent`** (it's deliberately not on the
`DamageableLayerMask`, so player bullets can't hit it). So enemy bullets are **not** matched via the
physics collider map. Instead `BulletOverlapSystem` does a direct, cheap test against the one player:

- **Horizontal** (top-down): the bullet flies at muzzle height, the player origin is at ground — the
  y-axis is ignored.
- **Swept**: the closest point of the segment `[prev → current]` (this frame's travel = `Direction *
  Speed * dt`) to the player, so fast bullets don't tunnel past in a single frame.
- **Reach** = `bullet.Radius + PLAYER_HIT_RADIUS` (a `0.4` constant standing in for the player body,
  since there's no collider to read). Tune the constant in `BulletOverlapSystem` if the player feels
  too easy/hard to hit.

On a hit, `BulletOverlapComponent.PlayerHit = true`; `CollisionSystem.BulletVsPlayer` then raises a
`RequestDamageComponent` on `player.Entity`, spawns the `playerHit` effect + `Blood` decal (same as
melee/contact), applies any `ShotDebuffs`, and disposes the bullet (no pierce through the player).

Breakables still block enemy bullets as cover (their `BreakableHits` are recorded for both teams and
consumed in `BulletVsBreakable`, which is team-agnostic).

## Configs

### `RangedMobConfig : MobConfig` (`SO/RangedMobConfig.cs`)
Adds only the engage behavior + the shot definition on top of a normal mob:

| Field | Default | Meaning |
|-------|---------|---------|
| `AttackRange` | 8 | distance at which the mob stops and shoots; beyond it, it chases. |
| `GunConfig` | — (Required) | the shot: damage, speed, radius, check type, accuracy/spread, projectiles-per-shot, caliber (→ projectile prefab & fire sound). **The same `GunConfig` type the player's weapon uses.** |
| `WindupDelay` | 0.4 | telegraph seconds before the shot leaves the muzzle (the `attack` clip plays here). |
| `Cooldown` | 0.8 | recovery seconds the mob stays stopped after firing, before re-engaging. |
| `MuzzleHeight` | 1 | vertical offset of the spawn point above the mob origin. |
| `MuzzleForwardOffset` | 0.5 | forward offset of the spawn point (so the bullet doesn't spawn inside the mob's own collider). |

Reusing `GunConfig` means a shooter mob's shot is authored with the *exact same* knobs as a player
weapon — including `ProjectilesNumber` (shotgun-style spread), `Accuracy`, `BulletCheckType`, and
`Caliber` (which resolves the projectile prefab via `AmmoConfigHolder`, falling back to
`MainHolder.BulletPrefab`). Note the mob does **not** consume ammo or reload — it just fires on the
windup→cooldown cadence.

## Components (`ECS/Components.cs`)

| Struct / enum | Notes |
|---------------|-------|
| `BulletTeam` (enum) | `Player`, `Enemy`. On `BulletComponent` and `RequestSpawnBulletComponent`. Default `Player`. |
| `BulletOverlapComponent.PlayerHit` (bool) | set by `BulletOverlapSystem` for enemy bullets that reach the player; read by `CollisionSystem.BulletVsPlayer`. |
| `RangedAttackerState` (enum) | `Chase`, `Windup`, `Cooldown`. |
| `RangedAttackerComponent` | `RangedMobConfig Config; RangedAttackerState State; float Timer;`. Added by `MobSpawnSystem` only when the spawned `MobConfig is RangedMobConfig` (mirrors the grenadier/melee branches). |

## Fixes & optimizations bundled with this feature

- **Bullet radius bug (fix).** `BulletSystem.SpawnBullet` never assigned `BulletComponent.Radius`, so
  `OverlapSphere`/`OverlapBox` detection ran with a stale/zero radius (EcsLite doesn't zero recycled
  structs). Now set from `GunConfig.BulletRadius`. `Raycast` mode happened to survive because it falls
  back to `Speed*dt`; sphere/box modes were silently under-hitting before.
- **Overlap early-out (perf).** `BulletOverlapSystem` rebuilt the collider→entity map over **all** mobs
  every frame. It now returns early when there are no bullets in flight — in a crowd with thousands of
  mobs and no active shots, that skips an O(mobs) rebuild per frame.
- **Shot debuffs wired (latent feature activated).** `GunConfig.ShotDebuffs` existed but was never
  consumed. Bullets now carry it (`BulletComponent.Modifiers`) and apply it to whatever they hit via
  `RequestDamageComponent.DamageModifiers` (cloned per hit — the modifiers are shared SO instances).
  Empty `ShotDebuffs` (the current state everywhere) = no behavior change.
- **DamageModifiers recycle-safety (fix).** `DamageSystem` now nulls `RequestDamageComponent.
  DamageModifiers` before recycling the request entity, so a later request that doesn't set the field
  can't inherit a stale modifier list from a pooled slot.

## Unity setup checklist

1. Create (or reuse) a **`GunConfig`** asset for the shot: damage, bullet speed, radius, check type,
   accuracy, projectiles-per-shot, caliber, and optionally `FireSoundId`. Keep `BulletSpeed` moderate
   relative to `WindupDelay`/reach so the swept player test reads cleanly.
2. Create a **`RangedMobConfig`** asset (right-click → Create → Scriptable Objects → RangedMobConfig).
   Fill the usual mob fields (health/speed/loot/etc.), set `AttackRange`, assign the `GunConfig`, and
   tune `WindupDelay`/`Cooldown`/`MuzzleHeight`/`MuzzleForwardOffset`.
3. Build/assign its **`Mob` prefab** with an `Animator` whose states include **`run`** and **`attack`**
   (non-looping `attack`, strike frame ~`WindupDelay`, total length ~`WindupDelay + Cooldown`). State
   names must match `AnimationTypes` in `Animation/AnimationType.cs`.
4. Add the `RangedMobConfig` to a spawn source (`MobConfigHolder` / spawn lists) like any other mob.
5. Verify in Play mode: the mob chases, stops at `AttackRange`, telegraphs, fires a bullet that damages
   the player, recovers during cooldown, then re-engages. Confirm it does **no** contact damage, its
   bullets **don't** hurt other mobs, and cover (breakables) blocks its shots.

## Animations, held weapon, muzzle flash

- **Per-phase clips** on `RangedMobConfig` (`AnimationType`, Animator state name = type name):
  `ChaseAnimation` (default `Run`), `WindupAnimation` (default `Attack`), `FireAnimation` (default `Attack` — same as
  windup ⇒ the clip just continues, i.e. the old single-`attack` behaviour). `MobSpawnSystem` starts a ranged mob in
  its `ChaseAnimation` (the generic default `Run` may not exist on a walking shooter). `AnimationType` gained
  `Aim` ("aim") and `Shoot` ("shoot") — appended, so serialized ints of older types are unchanged.
- After the cooldown, if the player is still within `AttackRange`, the mob goes straight back to Windup (no
  one-frame flick into the chase clip).
- **`MobWeapon`** (`_Scripts/MobWeapon.cs`): view-only component on a held weapon — `Grip`, `Muzzle` (+Z = shot
  direction), `MuzzleEffectId` (default `muzzle_flash`). Referenced from `Mob.Weapon` (`Mob._weapon`). When present,
  `RangedAttackerSystem.Fire` spawns the bullet at `Muzzle.position` (direction still flat toward the player) and
  requests the muzzle effect (`RequestEffectComponent` with `Parent = muzzle`, yaw = shot direction). Without it the
  config offsets `MuzzleHeight`/`MuzzleForwardOffset` are used, as before.
- **Muzzle flash**: `_Prefabs/Effects/MuzzleFlash.prefab` = `SceneEffect` root + nested Epic Toon FX
  `BulletMuzzleFire` (root rotation reset so the cone fires along +Z, scale 0.6). Registered in
  `Resources/FxHolder.asset` as `muzzle_flash` (Duration 0.6, `IsChild` → follows the muzzle, pooled like every effect).
- **Rifle**: `_Prefabs/Weapons/Mini14.prefab` (formerly `MobRifle`) — primitive Mini-14 built by
  `WeaponModelBuilder`, shared with the player's `RifleConfig`; `WeaponHandIK` on `Gunner.prefab` keeps the left hand on
  its forend. See `Docs/WeaponModels.md`.

## Bursts & stronger shooters (extension points)

- **Burst fire** (`RangedMobConfig`): `BurstCount` (default 1 = single shot, the old behaviour), `BurstInterval`
  (sec between shots), `RestartFireAnimationPerShot` (replay the fire clip on every bullet — via the one-shot
  `AnimationStateComponent.Restart` → `SimpleAnimator.RestartAnimation`). Flow: Windup → shot 1 (fire clip) →
  `RangedAttackerState.Burst` (stays put, keeps facing the player, `ShotsLeft` counts down) → Cooldown.
  One attack cycle = `WindupDelay + (BurstCount-1)·BurstInterval + Cooldown`.
- **Stronger gun** = another `GunConfig` on the new `RangedMobConfig` (damage, `ProjectilesNumber` for a shotgun
  spread, `Accuracy`, bullet speed/radius/colour, `FireSoundId`). No code needed.
- **Different gun model on the same body**: `RangedMobConfig.WeaponOverride` (a `MobWeapon` prefab). On a new
  instance `MobSpawnSystem.SpawnMob` calls `Mob.ReplaceWeapon`, which puts it in the hand with the prefab weapon's
  local pose (convention: weapon root = grip, barrel along +Z, `Muzzle` at the tip, own `MuzzleEffectId`). The mob pool
  is per config id, so it happens once per instance. → a new shooter can reuse `Gunner.prefab`; only a new body model
  needs its own prefab + `MobModelSetupPreset`.
- New muzzle flash = another `SceneEffect` prefab + `FxHolder` entry (`IsChild`, short `Duration`), referenced by id
  from the weapon's `MuzzleEffectId`.

Recipe for a new shooter: duplicate `GunnerMob.asset` (new `Id`!), assign a new `GunConfig`, set `BurstCount`/
`BurstInterval`, optionally `WeaponOverride` (any `WeaponModel` prefab from `_Prefabs/Weapons`, see `Docs/WeaponModels.md`), tune windup/cooldown/range, add it to spawn lists.

## Death animation (any skinned mob)

`MobConfig.DeathAnimationTime` (sec, default 0 = vanish instantly). On death `DamageSystem` still does everything at
once (loot, frags, death effect, **entity deleted** — so nothing can hit/target/collide with it), but for a mob with
time > 0, a live `SimpleAnimator` and no `CrowdInstanceComponent` it keeps the GameObject: disables its collider,
hides the health bar, plays `die`, and creates a `DyingMobComponent { Mob, Timer }` entity. `DyingMobSystem` (right
after `DamageSystem`, respects pause) counts down and then pools + deactivates the view via
`DyingMobSystem.ReturnToPool` (also used for instant deaths). `MobSpawnSystem` re-enables the collider on reuse.
VAT mobs ignore the field (no live Animator).

## Current content: `GunnerMob`

- Config `_Data/Mobs/GunnerMob.asset` (in `MobConfigHolder`): 50 HP, speed 2, `AttackRange` 8, windup 0.4 + cooldown 0.8
  → **one shot per ~1.2 s** (the mob ignores `GunConfig.FireCoolDown`), shot = `ZombieGun` (12 dmg, red bullets).
  Clips: chase `walk`, windup `aim`, fire `shoot`; `DeathAnimationTime` 2 (`die`). Loot: ammo 10 (35%), health 15 (10%), grenade (8%).
- Prefab `_Prefabs/Mobs/Gunner.prefab`: built by the model pipeline from `Model/Gunner` (Mixamo zombie soldier, 5k)
  with preset `_Data/Crowd/Setup/Gunner_ModelSetup.asset` — no VAT (few on screen). The placeholder `Gun` cube is
  removed, the red `Capsule` keeps only its collider (renderer off), `Mini14` is attached to `mixamorig:RightHand`
  and aligned to the first frame of `aim`. Controller `_Animations/Mob/Gunner.controller`: idle, walk, aim, shoot, die.
  `walk` = Mixamo "Rifle Walk" (drift removed by the pipeline if it isn't In Place).
- Spawns from `RegularSpawnPoint` (weight 0.5) in stages level2–level6 (`DifficultyLevel` 3–7). One instance in
  `Level_01` overrides the list size to 3 and doesn't get it.
- Red bullets: `GunConfig.BulletColor` (alpha 0 = prefab look) → `Bullet.SetColor` tints the mesh (`_BaseColor`
  via MaterialPropertyBlock) and the trail gradient on every spawn; pooled bullets are reset for other guns.

## Reuse / extension notes

- Bullet spawning/moving/pooling/piercing is 100% shared with the player — the only per-side split is
  `Team` (detection routing) and the spawn point. A new "boss cannon", "turret breakable", etc. just
  emits `RequestSpawnBulletComponent { Team = Enemy }` from wherever it likes.
- A mob that both shoots *and* melees is possible in principle, but `MobSpawnSystem`'s branch is
  `if/else if` (grenadier → melee → ranged), so a single config is one archetype. Split into two
  components + a combined system if you want a hybrid.
- No automated tests in this project — verify feel (windup readability, hit reliability, cover) in Play
  mode. If enemy bullets feel like they phase through the player, raise `PLAYER_HIT_RADIUS` or the
  gun's `BulletRadius`, or lower `BulletSpeed`.
```
