# Breakable Environment — Reference

Destructible scene props (crates, barrels, walls). They **block mob pathing** (their footprint carves the
NavMesh), take damage only from **configurable sources**, and on death **erupt in staggered VFX**, **drop
spread-out loot**, and either **vanish** (freeing the NavMesh) or leave **inert debris** (optionally still
blocking). Built entirely on existing request/pool patterns — this doc is the source of truth so the code
doesn't need re-exploring.

## Model

| Thing | Where |
|---|---|
| `Breakable` MonoBehaviour | `Breakable.cs` — scene refs: `BreakableConfig`, solid `Collider`, carving `NavMeshObstacle`, intact/debris visuals, effect points. `GetEffectPoint(i)`, `ShowDebris(keepObstacle)`, `Vanish()`, `ResetForSpawn()` |
| `BreakableConfig` SO | `SO/BreakableConfig.cs` — `Id`, `Prefab` (the `Breakable` to spawn), `MaxHealth`, `_damageableBy` mask, `DestructionEffect[]`, optional destruction blast (`DamageOnDestruction`, `DamageRadius`, `MaxDamage`/`MinDamage`, `MobDamageScale`/`PlayerDamageScale`, `DamageEffectId`), loot table/count/spread, `AfterDestruction`, `DebrisKeepsObstacle`; `CanBeDamagedBy(source)` |
| `BreakableConfigHolder` SO | `SO/BreakableConfigHolder.cs` — registry of configs, `GetConfigById(id)`. Referenced from `MainHolder` (`BreakableConfigHolder`) |
| `BreakableDamageSources` | `Enums/BreakableDamageSources.cs` — `[Flags]` `Bullet`/`Explosion`/`Melee`/`MobContact` |
| `AfterDestruction` | `SO/BreakableConfig.cs` — `Vanish` / `Debris` |
| `DestructionEffect` | `SO/BreakableConfig.cs` — one staggered VFX: `EffectId` (from `EffectsHolder`), `Delay`, `PointIndex` (into `Breakable` effect points; `-1` = center) |
| `BreakableComponent` | `ECS/Components.cs` — `Value` (Breakable), `Config`, `ContactCooldown`, `Pooled` |
| `BreakablePoolComponent` | `ECS/Components.cs` — singleton, `Dictionary<id, Stack<Breakable>>` + `Parent` (runtime-spawn pool) |
| `RequestSpawnBreakableComponent` | `ECS/Components.cs` — `Config` or `Id`, `Position`, `Rotation` — runtime spawn request |
| `BreakableSpawnSystem` | `ECS/BreakableSpawnSystem.cs` — handles spawn requests; `CreateBreakable`/`RegisterBreakable` statics |
| `BreakableSystem` | `ECS/BreakableSystem.cs` — mob-contact damage + destruction sequence |

The ECS entity for a breakable carries `BreakableComponent` + `HealthComponent` (`TargetType.Enviroment`) +
`ColliderComponent` (`CollisionType.Breakable`), built by `BreakableSpawnSystem.RegisterBreakable`. Two
origins share that registration:

- **Scene-placed** — `EntryPoint.SetupSpawnData` scans `FindObjectsByType<Breakable>()` (same pattern as
  `SpawnPoint`/`MapLoot`; level content is already instantiated by `LoadLevel`) and registers each with
  `pooled: false`.
- **Runtime-spawned** — see *Spawning* below (`pooled: true`).

## NavMesh: carving, not baking

Each `Breakable` carries a **carving `NavMeshObstacle`** (`Breakable.Awake` forces `carving = true`). Carving
punches a hole in the already-baked `NavMeshSurface` at runtime with **no `BuildNavMesh`**. Mob pathing
(`MobPathfindingSystem`, `NavMesh.CalculatePath`) respects the hole automatically, so mobs route *around* the
prop. This is the performance-safe core — baking the mesh into the surface would force a rebuild hitch on
every destruction.

On destruction the obstacle is toggled (removed on `Vanish`, kept/removed on `Debris` per
`DebrisKeepsObstacle`) and `BreakableSystem` fires `PathRecalculationRequest` on mobs within `REPATH_RADIUS`
(8 m) so their cached paths refresh the same frame. No rebuild anywhere.

## Spawning (holder + prefab + request)

Each `BreakableConfig` carries its own `Prefab` and lives in a `BreakableConfigHolder` (on `MainHolder`),
so environment objects can be spawned at runtime by request — not only pre-placed. Raise a
`RequestSpawnBreakableComponent` (helpers: `world.RequestSpawnBreakable(id, position, rotation)` or the
`BreakableConfig` overload); `BreakableSpawnSystem` (registered near `MobSpawnSystem`) resolves the config
(direct `Config`, else `Id` via the holder), pops an instance from `BreakablePoolComponent` (keyed by config
id) or `Instantiate`s the prefab, positions it, `ResetForSpawn()`s it, registers the ECS state, and fires a
nearby-mob re-path (a new carving obstacle appeared).

Instances are **pooled by config id** (like mobs/effects): a runtime-spawned breakable that ends in `Vanish`
is returned to the pool (`ReturnToPool`) for reuse; scene-placed ones (`Pooled == false`) and `Debris`
outcomes are not pooled. `BreakableSpawnSystem.CreateBreakable` is a public static, so other systems can
spawn one directly without going through the request.

## Damage intake (per-source gating)

Damage flows through the **existing** `RequestDamageComponent` → `DamageSystem` → `HealthComponent` path.
`DamageSystem` already no-ops for a target that is neither player nor mob (it just subtracts health), so no
change there — `BreakableSystem` detects `CurrentHealth <= 0` separately. Each damage **site** checks the
breakable's `CanBeDamagedBy(source)` before raising the request:

| Source flag | Site | Notes |
|---|---|---|
| `Bullet` | `CollisionSystem` (`BulletVsBreakable`) | Bullet is **blocked & consumed** on hit (`IsDisposed = true`) — the prop acts as cover. Hits recorded by `BulletOverlapSystem.BreakableHits`. |
| `Explosion` | `ExplosionSystem` | Radial damage with the same center→edge lerp used for mobs. |
| `Melee` | `MeleeSpawnSystem` | Any melee attack (`RequestMeleeComponent`) whose radius covers the prop. Breakables have no `MoveComponent`, so they're checked in a dedicated loop by transform distance. |
| `MobContact` | `BreakableSystem` | Mobs bashing the prop. `OverlapSphereNonAlloc` on the mob mask (only for opted-in breakables), throttled by `ContactCooldown` (0.5 s); damage = the touching mob's `Config.Damage`. |

Bullet/explosion overlap queries widen their layer mask to `MainHolder.DamageableLayerMask`
(`MobLayerMask | BreakableLayerMask`) so breakable colliders are returned. **Breakables must be on a layer
included in `MainHolder._breakableLayerMask`** or they won't be hit.

## Destruction sequence (`BreakableSystem.Destroy`)

Runs right after `DamageSystem`, before `LootSystem`/`EffectsSystem` (so the requests it emits resolve the
same frame). For each breakable at `CurrentHealth <= 0`:

1. **Staggered VFX** — one `RequestEffectComponent` per `DestructionEffect`, positioned at
   `Breakable.GetEffectPoint(PointIndex)` with `Delay`. The delay is honored by a new fuse in
   `EffectsSystem` (`RequestEffectComponent.Delay` counts down before the effect spawns) — reusable
   game-wide. E.g. two explosions, the second with `Delay = 0.3`.
   - **Optional blast damage** — if `BreakableConfig.DamageOnDestruction` is set, `Destroy` raises a
     `RequestExplosionComponent` at the center (radius/max/min/`MobDamageScale`/`PlayerDamageScale`/
     `DamageEffectId` from the config). This **reuses `ExplosionSystem`** wholesale: center→edge falloff,
     hits mobs and the player, and **chain-detonates nearby breakables** that allow `Explosion` damage
     (a barrel sets off its neighbours). `ExplosionSystem` runs before `BreakableSystem` in the loop, so
     the blast resolves the *next* frame — the source entity is already gone, so it never re-hits itself.
     Leaving `DamageEffectId` empty falls back to the default `"explosion"` VFX (on top of the staggered
     destruction effects).
2. **Loot spread** — `LootCount` × `RequestLootSpawn { Source = Breakable, PossibleLoots = LootTable }` at
   Vogel-spiral offsets (`r = spread * sqrt(i)`, golden-angle) around the center, giving ~`LootSpread`
   min spacing. Each request rolls the table independently in `LootSystem`. Breakable loot **persists**
   (only `Source == Mob` gets a despawn timer).
3. **Outcome** — `Vanish` → `Breakable.Vanish()` (obstacle off, object deactivated), and if the instance
   was pooled (runtime-spawned) it returns to `BreakablePoolComponent`; `Debris` →
   `Breakable.ShowDebris(DebrisKeepsObstacle)` (swap visual, disable collider, keep/drop carving).
4. **Re-path** — `PathRecalculationRequest` on nearby mobs.
5. **Delete** the ECS entity (the GameObject lives on as debris, is deactivated, or waits in the pool).

## Performance notes

- No `BuildNavMesh` on destruction; only a bounded `PathRecalculationRequest` broadcast.
- Reused physics buffers and collider→entity maps (same pattern as `BulletOverlapSystem`/`ExplosionSystem`);
  no per-frame managed allocations.
- Destruction work is O(destroyed-this-frame); `MobContact` polling runs only for opted-in props on a cooldown.

## Editor setup

1. **Prefab**: a `Breakable` with a **mesh**, a **solid `Collider`** on a breakable layer, a **carving
   `NavMeshObstacle`** sized to the footprint, optional **debris** child (assigned to `_debrisVisual`), and
   child transforms in `_effectPoints` for the VFX bursts.
2. **Config**: create a `BreakableConfig` asset (Scriptable Objects ▸ BreakableConfig). Set a unique `Id`
   and its `Prefab`, `MaxHealth`, tick the allowed damage sources, add `DestructionEffects` (effect id +
   delay + point index), fill the loot table + count + spread, pick `Vanish`/`Debris`. Assign the same
   config to the prefab's `Breakable._config`.
3. **Holder**: add the config to a `BreakableConfigHolder`, and assign that holder on `MainHolder`.
4. **Layer**: add the breakable layer to `MainHolder._breakableLayerMask`.
5. **Placement / spawn**: drop the prefab into the level for pre-placed props (`EntryPoint` registers it at
   load), and/or spawn at runtime with `world.RequestSpawnBreakable(id, position, rotation)`. Optionally set
   `EntryPoint._breakableParent` to keep pooled instances tidy (auto-created if left empty).

## Adding behavior

- **New damage source**: add a flag to `BreakableDamageSources`, gate a new damage site on
  `CanBeDamagedBy(...)`.
- **New outcome**: extend `AfterDestruction` and handle it in `BreakableSystem.Destroy` + a `Breakable`
  method.

## Related docs

- `Docs/LootFeature.md` — the loot spawn flow the destruction sequence feeds into.
- `Docs/GrenadeFeature.md` — `RequestExplosionComponent` / `ExplosionSystem`, which now also hits breakables.
- `Docs/SectorFeature.md` — `NavMeshManager` and how the surface is baked (carving is independent of it).
