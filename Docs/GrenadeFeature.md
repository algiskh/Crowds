# Grenade Feature — Reference

Player-thrown grenades: collectible loot → hold-to-charge throw with on-ground targeting →
arcing projectile (with optional escort effect) → radial explosion. Per-grenade stats live in
`GrenadeConfig` ScriptableObjects. Built on the existing Leopotam EcsLite + singleton-entity
patterns. This doc is the source of truth so the code doesn't need re-exploring.

## Data flow

```
pickup grenade loot ──► set GrenadeState.CurrentConfig (by loot.Id) + Count++ ──► UI "x{n}"
hold  Throw  ──► charge accumulates ──► targeting marker shows landing spot (live, sized to radius)
release Throw ──► spend 1 grenade ──► spawn arcing Grenade projectile
                 └─ if config has TrailEffectId: pop effect from pool, parent to grenade, Show()
projectile ──► parabolic flight (Start→Target) ──► on land:
                 ├─ return trail effect to pool
                 └─ RequestExplosionComponent
ExplosionSystem ──► pooled explosion effect (config.ExplosionEffectId) + radial damage (max center → min edge)
```

## Systems & frame order

Registered in `EntryPoint.RegisterSystems()`:

```
… CollisionSystem → GrenadeProjectileSystem → ExplosionSystem → DamageSystem
  … InputSystem → GrenadeThrowSystem → PlayerSystem …
```

- `ExplosionSystem` is placed right before `DamageSystem` so radial damage applies the same frame it detonates.
- `GrenadeProjectileSystem` runs before `ExplosionSystem` so a landing emits its explosion in time.
- `GrenadeThrowSystem` runs after `InputSystem` (which resolves the `Throw` action in `Init`).

| System | File | Responsibility |
|--------|------|----------------|
| `GrenadeThrowSystem` | `ECS/GrenadeThrowSystem.cs` | **Input-calc + targeting.** Hold→charge; distance `Lerp(Min,Max,charge)` from `PlayerConfig`; direction via cursor→ground raycast (same technique as weapon aim, see `AimVisualizer`/`LookAtCursorSystem`). Per-grenade params (speed/damage/radius/effects) come from `GrenadeState.CurrentConfig`. Drives the visualizer during charge; on release spends a grenade, spawns the projectile, and spawns the trail effect (if any) parented to the grenade. Can only throw when `Count>0 && CurrentConfig!=null`. Distance is charge-driven, direction is cursor-driven (distance is NOT clamped to cursor distance). |
| `GrenadeProjectileSystem` | `ECS/GrenadeProjectileSystem.cs` | **Arc flight.** Moves each grenade `Vector3.Lerp(Start,Target,t)` + parabola `ArcHeight*4*t*(1-t)` (peak at t=0.5). On `t>=1`: returns the trail effect to the effect pool, emits `RequestExplosionComponent` at `Target`, deactivates the grenade view and pushes it back to the grenade pool. |
| `ExplosionSystem` | `ECS/ExplosionSystem.cs` | **Explosion on demand.** Fuse countdown (`Delay`); on detonate: requests pooled effect (`RequestEffectComponent`, returned to pool by `EffectsSystem` after its duration) + `Physics.OverlapSphereNonAlloc` against `MainHolder.MobLayerMask`, mapping colliders→mob entities (same approach as `BulletOverlapSystem`), damage `Lerp(MaxDamage,MinDamage,dist/radius)` per mob via `RequestDamageComponent`. |

## Per-grenade config — `GrenadeConfig` (SO)

`SO/GrenadeConfig.cs` — one asset per grenade type. `SO/GrenadeConfigHolder.cs` maps id→config
(`GetConfig(id)` falls back to `Default` = first entry; `Default` for empty id). Referenced from
`MainHolder.GrenadeConfigHolder`.

| Field | Default | Meaning |
|-------|---------|---------|
| `Id` | — | matched against loot `Id` / `PlayerConfig.StartGrenadeId` |
| `Preview` | — | loot icon (falls back to `SpriteHolder."Grenade"` if unset) |
| `ThrowSpeed` | 12 | horizontal flight speed; `FlightTime = dist/speed` (min 0.15s) |
| `ArcHeight` | 2.5 | peak arc height (units) |
| `FuseDelay` | 0 | post-landing fuse before explosion (sec) |
| `Radius` | 3.5 | blast radius |
| `MaxDamage` | 120 | damage at epicenter |
| `MinDamage` | 30 | damage at radius edge |
| `MobDamageScale` | 1 | `0–1` multiplier on damage dealt to mobs (`0` = mobs unaffected) |
| `PlayerDamageScale` | 1 | `0–1` multiplier on damage dealt to the player (`0` = player unaffected) |
| `ExplosionEffectId` | `"explosion"` | explosion effect id in `EffectsHolder` |
| `TrailEffectId` | `""` | escort effect id; spawned as child of the grenade, returned to pool on explosion. Empty = none |

## Player-level config — `PlayerConfig` (via `MainHolder.PlayerConfig`)

Only the throw *mechanic* (not per-grenade stats):

| Field | Default | Meaning |
|-------|---------|---------|
| `StartGrenades` | 0 | grenades at level start |
| `StartGrenadeId` | `""` | `GrenadeConfig.Id` for starting grenades (empty = holder `Default`) |
| `MinThrowDistance` | 3 | distance at instant release |
| `MaxThrowDistance` | 10 | distance at full charge |
| `MaxThrowChargeTime` | 1.2 | seconds of hold for full distance |

## Components (in `ECS/Components.cs`, `#region Grenade`)

| Struct | Kind | Fields / notes |
|--------|------|----------------|
| `GrenadeStateComponent` | singleton | `int Count; bool IsCharging; float ChargeTime; GrenadeConfig CurrentConfig;` — inventory + charge + current grenade type |
| `GrenadeCounterUIComponent` | singleton | `GrenadeCounter Value;` |
| `GrenadeAimVisualizerComponent` | singleton | `GrenadeAimVisualizer Value;` (may be null if not in scene) |
| `GrenadePoolComponent` | singleton | `Stack<Grenade> Value; Transform Parent;` (single shared prefab; trail effect differentiates visuals) |
| `GrenadeProjectileComponent` | per-entity | `Grenade Value; Vector3 Start,Target; float Elapsed,FlightTime,ArcHeight;` + blast params `Radius,MaxDamage,MinDamage,FuseDelay; string EffectId;` + `SceneEffect TrailEffect;` |
| `UpdateGrenadeViewRequestComponent` | tag-request | consumed by `UISystem` to refresh the counter |
| `RequestExplosionComponent` | request | `Vector3 Position; float Radius,MaxDamage,MinDamage,Delay; string EffectId;` — explosion-on-demand, reusable independently of grenades |

Also: `InputActionsComponent.ThrowAction` (resolved in `InputSystem.Init`, `throwIfNotFound:false`).

## Integration touch-points (modified files)

- `LootType.cs` — `Grenade` enum value. Grenade loot's `Id` = `GrenadeConfig.Id`.
- `CollisionSystem.cs` — `case LootType.Grenade:` → sets `GrenadeState.CurrentConfig` (via `GrenadeConfigHolder`, by `loot.Id` or `Default`), `Count += loot.Count`, requests UI update.
- `LootSystem.cs` — grenade loot icon uses `GrenadeConfig.Preview` when present, else `SpriteHolder."Grenade"`.
- `UISystem.cs` — on `UpdateGrenadeViewRequestComponent` → `GrenadeCounter.SetCount(count)`; deletes the request.
- `InputSystem.cs` — resolves `"Throw"` action.
- `Utils.cs` — `EffectPoolComponent.SpawnFromPool(FxWrapper)` helper (mirror of `Pool`) for direct pool spawn of the trail effect.
- `SO/MainHolder.cs` — `Grenade GrenadePrefab` (Prefabs), `GrenadeConfigHolder GrenadeConfigHolder` (Configs).
- `SO/PlayerConfig.cs` — player-level throw fields (above).
- `EntryPoint.cs` — serialized `_grenadeCounter` (UI, Required) and `_grenadeParent` (Parents, optional); creates `GrenadeStateComponent` (+ `CurrentConfig` from `StartGrenadeId`/`Default`), `GrenadeCounterUIComponent`, `GrenadePoolComponent`, `GrenadeAimVisualizerComponent`; registers the systems.
- `InputSystem_Actions.inputactions` — `Throw` action; bindings: Keyboard **G**, Gamepad **right shoulder**.

## View files

- `Grenade.cs` — bare projectile view (pooled). Prefab assigned to `MainHolder._grenadePrefab`.
- `GrenadeAimVisualizer.cs` — `Show(Vector3 pos, float radius)` / `Hide()`; serialized `_root`, `_marker`, `_scaleMarkerToRadius` (scales marker XZ to `radius*2`).
- `UI/GrenadeCounter.cs` — `SetCount(int)`: whole widget (`_root`) active when count>0; `_countText` (UniText `"x{n}"`) active only when count>1.

## Unity setup checklist (prefabs/assets to author)

1. Focus Editor to **reimport** `InputSystem_Actions.inputactions` (compiles the `Throw` action; code soft-fails until then).
2. Create one or more **`GrenadeConfig`** assets (right-click → Create → Scriptable Objects → GrenadeConfig); set `Id`, stats, `ExplosionEffectId`, optional `TrailEffectId`/`Preview`.
3. Create a **`GrenadeConfigHolder`** asset, add the configs, and assign it to `MainHolder.GrenadeConfigHolder`. First entry = default.
4. Build the **grenade projectile prefab** → add `Grenade` component → assign to `MainHolder._grenadePrefab`.
5. Add a **`GrenadeAimVisualizer`** to the scene (ground ring/decal): set `_root` + `_marker`. Auto-found via `FindFirstObjectByType`.
6. Build a **`GrenadeCounter`** UI widget: set `_root` + `_countText`; assign to `EntryPoint._grenadeCounter`.
7. In `EffectsHolder` add `FxWrapper`s for each `ExplosionEffectId` (e.g. `"explosion"`, `HasDuration`+`Duration`) and each `TrailEffectId` (looping VFX; duration is irrelevant — its lifetime is controlled by the grenade).
8. (optional) Add a `SpriteHolder` sprite under id `"Grenade"` as icon fallback when a config has no `Preview`.
9. Fill `PlayerConfig` player-level grenade fields (start count/id, distances, charge time).
10. (optional) assign `EntryPoint._grenadeParent` for hierarchy tidiness.
11. Add `Grenade` loot entries to spawn configs (`LootSpawnPoint` / `MapLoot` / `PossibleLoot`) with `Id` = a `GrenadeConfig.Id`.

## Grenadier mob (mob that throws grenades)

A ranged enemy that keeps its distance and lobs grenades at the player instead of meleeing.
In **every other respect it's an ordinary mob** — health, speed, melee damage, loot, collider,
modifiers — because its config inherits from `MobConfig`.

- **`GrenadierMobConfig : MobConfig`** (`SO/GrenadierMobConfig.cs`) — adds only the throw behavior:
  `ThrowMaxDistance` (X), `ThrowMinDistance` (Y), `ThrowCooldown`, `ThrowWindup` (windup of the
  `throw` anim before the grenade leaves the hand), and `GrenadeConfig` (which grenade type to throw).
- **`GrenadierComponent`** (per-entity, `Components.cs`) — `Config`, `State` (`GrenadierState`
  enum: `Chase`/`Throw`/`Cooldown`/`Flee`), `Timer`, `FleeTarget`/`HasFleeTarget`. Added by
  `MobSpawnSystem` only when the spawned `MobConfig is GrenadierMobConfig`.
- **`GrenadierSystem`** (`ECS/GrenadierSystem.cs`, registered between `MobPathfindingSystem` and
  `MoveSystem`) — the state machine:
  - **Chase** (dist > X): normal pathfinding to the player (system untouched).
  - **Throw** (Y ≤ dist ≤ X): stops, plays **`throw`**, and after `ThrowWindup` launches a grenade
    at the player's current position via `GrenadeLauncher`.
  - **Cooldown** (after throw): stays put for `ThrowCooldown` seconds, plays **`throw_cooldown`**.
  - **Flee** (dist < Y): moves to a free NavMesh point away from the player (`NavMesh.SamplePosition`
    over a fan of angles = "free space nearby"); re-engages once back outside Y.
  - In Throw/Cooldown it stops by clearing the player-path waypoints (running after pathfinding,
    before movement); Flee overwrites the path with a single flee waypoint each frame.
- **`GrenadeLauncher`** (`ECS/GrenadeLauncher.cs`) — shared projectile-spawn helper used by **both**
  the player (`GrenadeThrowSystem`) and the grenadier. Differs only by start/target; who the blast
  hurts comes from the `GrenadeConfig` itself, so the same helper serves both.
- **Explosion damage targets (per-config sliders)** — by default a grenade hurts **everyone in the
  radius** (mobs *and* the player, including its own thrower). Each `GrenadeConfig` carries two
  `0–1` sliders, `MobDamageScale` and `PlayerDamageScale`, that scale the radial damage applied to
  each target type (e.g. player `1.0` + mobs `0.5` = full damage to player, half to mobs; set one to
  `0` to spare that target entirely). These flow `GrenadeConfig → GrenadeProjectileComponent →
  RequestExplosionComponent`; `ExplosionSystem` multiplies the distance-based damage by the matching
  scale and skips a target whose scale is `0`. Player damage is applied by distance to the player
  (same as melee), mob damage via `OverlapSphere` on `MobLayerMask`.

### Unity setup for a grenadier
1. Create a **`GrenadierMobConfig`** asset (right-click → Create → Scriptable Objects → GrenadierMobConfig);
   fill the usual mob fields plus X/Y/cooldown/windup and a `GrenadeConfig`.
2. Build/assign its **`Mob` prefab** with an `Animator` whose states include **`run`**, **`throw`**,
   and **`throw_cooldown`**. `GrenadierSystem` doesn't touch the `Animator` directly — its state
   transitions set `AnimationStateComponent.Requested` (`AnimationType.Run`/`Throw`/`ThrowCooldown`),
   and `AnimationSystem` reconciles that to the `SimpleAnimator`, which cross-fades to the matching
   controller state. The state names must match `AnimationTypes` in `Animation/AnimationType.cs`
   (`run` / `throw` / `throw_cooldown`).
3. Add the config to a spawn source (`MobConfigHolder` / spawn lists) like any other mob.
4. Verify in Play mode: approach band, throw/cooldown stops, flee on getting too close, and that the
   blast actually hurts the player.

## Reuse / extension notes

- `RequestExplosionComponent` is grenade-agnostic — any system can request an explosion at a point (e.g. exploding mobs, barrels). Set `MobDamageScale`/`PlayerDamageScale` (`0–1`) to choose who it hurts and how much.
- Per-grenade variation is data-driven via `GrenadeConfig`; the thrown projectile copies its config's params at spawn, so changing types mid-flight is safe.
- The trail effect is managed directly (spawned in `GrenadeThrowSystem`, returned in `GrenadeProjectileSystem`) rather than via `EffectsSystem`, because its lifetime is "until explosion" rather than a fixed duration.
- Single grenade prefab is shared across configs; visual differentiation comes from the trail effect. For per-type meshes, switch `GrenadePoolComponent` to a `Dictionary<string,Stack<Grenade>>` keyed by config id (like effects/mobs) and add a prefab field to `GrenadeConfig`.
- No automated tests in this project — verify feel (charge curve, arc height, radius, trail follow) in Play mode.
