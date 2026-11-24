# Loot System — Reference

Loot is the pickup layer of the game: mobs drop it on death, it can be pre-placed on the map, and it
can be spawned at points when conditions are met. A loot item is a pooled `Loot` MonoBehaviour with a
billboard sprite, backed by an ECS entity. The player picks it up by walking over it; mob-dropped loot
also despawns on a per-type timer. This doc is the source of truth so the code doesn't need re-exploring.

## Model

| Thing | Where |
|---|---|
| `LootType` enum | `LootType.cs` — `Ammo`, `Health`, `Weapon`, `Grenade`, `Bonus` |
| `Loot` MonoBehaviour | `Loot.cs` — billboard `Canvas` + `Image`; `SetSprite(sprite)` |
| `LootComponent` | `ECS/Components.cs` — `Loot` ref, `LootType`, `Id`, `Radius`, `Count`, `AmmoCaliber` |
| `RequestLootSpawn` | `ECS/Components.cs` — `SourceEntity`, `PossibleLoots[]`, `Position`, `Source` |
| `LootSpawnedEventComponent` | `ECS/Components.cs` — event fired after a loot entity is created (`Source`, `SourceEntity`, `LootEntity`) |
| `DisposableComponent` | `ECS/Components.cs` — `IsDisposed` flag; drives return-to-pool |
| `LifeTimeComponent` | `ECS/Components.cs` — `Value` (seconds); only on mob-dropped loot |
| `LootPoolComponent` | `ECS/Components.cs` — singleton, `Stack<Loot>` + `Parent` transform |
| `MapLootPoolComponent` | `ECS/Components.cs` — singleton, `List<MapLoot>` (pre-placed items) |
| `PossibleLoot` | `PossibleLoot.cs` — one drop-table row: `LootType`, `Id`, `Count`, `Chance`, `AmmoCaliber` |
| `RequestSpawnSource` enum | `Enums/RequestSpawnSource.cs` — `Mob`, `AdditionalSpawn`, `Quest`, `MapLoot` |
| `MapLoot` MonoBehaviour | `MapLoot.cs` — wraps a `Loot` + its serialized `LootComponent` (pre-placed) |
| drop table (mobs) | `SO/MobConfig.cs` — `PossibleLoot[] PossibleLoots` |
| drop table (conditional) | `LevelConfig.cs` — `AdditionalLootConfig`: `Dictionary<SmartConditionWrapper, PossibleLoot[]>` |
| loot-wide params | `SO/MainHolder.cs` — `LootPrefab`, `LootRadius`, per-type despawn timers |

### Subtype identity per loot type

A `LootType` says *what category*; the specific item is identified differently per type:

- **Weapon / Grenade / Bonus** — `LootComponent.Id` (a string), resolved against the matching holder
  (`GunConfigHolder` / `GrenadeConfigHolder` / `BonusConfigHolder`). Empty `Id` → the holder's `Default`
  (grenade/bonus).
- **Ammo** — `LootComponent.AmmoCaliber` (the `Caliber` enum), not `Id`. `None` is resolved to the
  current weapon's caliber **at spawn**. See `Docs/AmmoSystem.md`.
- **Health** — neither; `Count` is the heal amount.

## Spawn sources

All four sources converge on the same request component, `RequestLootSpawn`, distinguished by `Source`:

| Source | Who creates it | When |
|---|---|---|
| `Mob` | `DamageSystem` | a mob's `CurrentHealth <= 0` (once per mob; tracked in a `entitiesWithLoot` set) |
| `MapLoot` | `LootSystem.Init` | level start, one per `MapLoot` in `MapLootPoolComponent` (the placed object is deactivated and replaced by a pooled loot) |
| `AdditionalSpawn` | `AdditionalLootSpawnSystem` | a `SmartCondition` is fulfilled and a free loot point exists |
| `Quest` | — | enum member reserved; no producer in current code |

`DamageSystem` copies the dead mob's `MobConfig.PossibleLoots` into the request and stamps the mob's
position; it also bumps the frag count and queues a `RequestUpdateFragCountComponent` in the same place.

## Spawn flow (`LootSystem`)

`LootSystem` (`ECS/LootSystem.cs`, registered in `EntryPoint.RegisterSystems`) owns creation, the
lifetime countdown, and return-to-pool. Each `Run`:

```
1. CheckingDisposed   — for every LootComponent with DisposableComponent.IsDisposed:
                        deactivate, push Loot back to LootPoolComponent, fire a "collect" /
                        "collectHealth" RequestEffectComponent, delete the entity.  (this is PICKUP)

2. CountingLifeTime   — for every LootComponent with a LifeTimeComponent (mob loot only):
                        skip if already IsDisposed; else Value -= deltaTime; on <= 0 deactivate,
                        push back to the pool, delete the entity.  NO collect effect (not a pickup).

3. HandlingRequests   — for every RequestLootSpawn:
                        a. roll the drop table -> select one PossibleLoot (or nothing)
                        b. take a Loot from the pool (or Instantiate MainHolder.LootPrefab)
                        c. build the loot entity (LootComponent + ColliderComponent + DisposableComponent
                           + LookerAtCamera billboard)
                        d. resolve the sprite by type, position it (ResolveSpawnPosition: a point inside a
                           navmesh hole — vehicle, breakable, gate, wall — moves to the nearest walkable
                           point within 5 m, X/Z only; no navmesh nearby → as is), (for Mob) attach LifeTimeComponent
                        e. fire LootSpawnedEventComponent, delete the request
```

### Drop-table roll

In `HandlingRequests`, given `PossibleLoots`:

- **One entry** → always selected (its `Chance` is ignored).
- **Multiple entries** → `totalChance = Σ Chance`. The roll is `Random.value * max(totalChance, 1)`.
  If the roll lands above `totalChance`, **nothing drops** (the request is dropped) — so a drop table
  whose chances sum below `1` has a literal "no loot" probability. Otherwise the first cumulative
  segment covering the roll wins.

So `Chance` is a *relative weight* once the table has 2+ rows, but the `max(total, 1)` normalization
turns a sub-1 total into a real empty-drop chance.

### Sprite resolution

`LootSystem` sets the billboard sprite by type, each with a fallback to `SpriteHolder.GetSpriteById(LootType.ToString())`:

| Type | Primary source |
|---|---|
| `Weapon` | `GunConfigHolder.GetConfig(Id).Preview` |
| `Grenade` | `GrenadeConfigHolder` (`Default` if `Id` empty) → `.Preview` |
| `Ammo` | `AmmoConfigHolder.GetConfig(resolvedCaliber).LootIcon` |
| `Bonus` | `BonusConfigHolder` (`Default` if `Id` empty) → `.Preview` |
| `Health` / default | `SpriteHolder` by name |

The billboard itself is the `LookerAtCamera` component (`FlatBillboard = true`) targeting the loot's
`SpriteLooker` canvas.

## Pickup (`CollisionSystem` → `PlayerVsLoot`)

`CollisionSystem` checks every non-disposed loot against the player each frame: if
`sqrDistance <= LootRadius²` it sets `IsDisposed = true` and applies the effect by type:

| Type | Effect |
|---|---|
| `Ammo` | `AmmoInventory.Add(caliber, Count)` (caliber from loot; `None` → current weapon) → `UpdateAmmoViewRequestComponent` |
| `Weapon` | swap `WeaponComponent.GunConfig` to `GunConfigHolder.GetConfig(Id)`, refill magazine → `UpdateWeaponViewRequestComponent`. By default on contact, like any loot (losing a good gun by accident is intended). Optional mode `PlayerConfig.WeaponPickupRequiresInteract` (off; reserved for a future perk): **only on Interact** (the `Interact` action, Hold interaction: E / gamepad north) unless it's the gun already in hands (then it's automatic — just a magazine refill). Standing on it without Interact logs a one-time hint "Hold [E] to pick up X" (`LootComponent.PickupHintShown`, `LootLogFormatter.FormatPickupHint`). One weapon per press. `InputSystem` writes `PlayerInputComponent.InteractPressed`; `CollisionSystem` runs earlier in the frame, so it reads it one frame later. |
| `Health` | `CurrentHealth += Count` (clamped to `MaxHealth`) → `UpdateHealthViewRequestComponent` |
| `Grenade` | set `GrenadeStateComponent.CurrentConfig` (`Default` if `Id` empty), `Count += Count` → `UpdateGrenadeViewRequestComponent` |
| `Bonus` | `RequestApplyBonusComponent { ConfigId = Id }` (consumed by `BonusSystem`, see `Docs/BonusFeature.md`) |

Setting `IsDisposed` is all that pickup does to the lifecycle; the actual deactivate/pool/effect/delete
happens in `LootSystem.CheckingDisposed` on the same frame. Pickup plays the collect effect; a lifetime
expiry does not.

On pickup, `PlayerVsLoot` also raises a `RequestUILogComponent` whose `Message` is built by
`UI/LootLogFormatter.Format(loot, muzzle, mainHolder)` (e.g. "Picked up 9mm Ammo (10)", "Obtained Shield
bonus"). Item names come from `MainHolder.Localization` (`LocalizationHolder`) keyed by the loot's `Id`
(weapon/grenade/bonus) via the non-logging `TryGetKey`, falling back to the raw id when no key exists;
ammo uses `Caliber.ToDisplay()`. The request is dispatched by `UILogSystem` (runs right after `UISystem`)
to the `UILogView` HUD log — a pooled, newest-on-top column of `UILogSlot` lines that each fade out after
a configurable lifetime. The log request is generic, so any system (not just pickups) can post a line.

## Despawn timer (mob loot only)

Loot dropped by mobs (`Source == Mob`) despawns after a **configurable, per-`LootType` timer**; loot
from any other source stays until picked up.

- **Config** lives on `SO/MainHolder.cs`, group `LootLifetime`:
  - `_defaultMobLootLifetime` — fallback (seconds) for any type not listed.
  - `_mobLootLifetimes` — `LootLifetimeEntry[]` (`LootType` + `Lifetime`), the per-type overrides.
  - `GetMobLootLifetime(LootType)` — returns the override or the default. **`<= 0` means "never
    despawns"** (no `LifeTimeComponent` is attached).
- **Attach**: in `LootSystem.HandlingRequests`, mob loot with a positive lifetime gets a
  `LifeTimeComponent` seeded from `GetMobLootLifetime(type)`.
- **Count down**: `LootSystem.CountingLifeTime` decrements it; on expiry the loot is returned to the
  pool silently (no collect effect/sound, since it wasn't picked up).
- **Expiry warning**: during the last `LootDespawnWarningTime` seconds (default `3`, `<= 0` disables),
  the same loop pulses the loot's icon toward `LootDespawnWarningColor` (default red) via
  `Loot.SetWarningTint`, using `0.5*(1 + sin(remaining * LootDespawnWarningPulseSpeed))`. The pulse is
  driven off the remaining time (no extra state), costs only an `Image.color` write on an
  already-rendered icon, and is cleared by `Loot.ResetColor()` when a pooled loot is reused at spawn.

`MainHolder` was chosen for the config because the timer is per **loot type** (not per mob, ruling out
`MobConfig`) and `MainHolder` already owns the other loot-wide params (`LootPrefab`, `LootRadius`),
keeping loot tuning in one place. The mechanism reuses the existing `LifeTimeComponent` + pool pattern
that `DecalSystem` uses for decals.

## Pooling

`LootPoolComponent` is one shared `Stack<Loot>` (loot visuals are interchangeable; the type only drives
the sprite, set per spawn). Spawn pops from the stack or `Instantiate`s `MainHolder.LootPrefab` under
`LootPoolComponent.Parent`. Both despawn paths (pickup and lifetime) deactivate the `Loot` and `Push`
it back. Deleting the ECS entity drops `LifeTimeComponent` with it, so a reused `Loot` always starts
clean.

`AdditionalLootSpawnSystem` also recycles **loot points** (not the loot): when a loot it placed is gone
(its entity no longer has `LootComponent`), the point returns to `LootPointsPool` for reuse.

## Editor setup

1. **Mob drops**: on each `MobConfig` fill `PossibleLoots` (type + id/caliber + count + chance). Keep
   the chance sum below `1` if you want a chance of no drop.
2. **Despawn timers**: on the `MainHolder` asset, `LootLifetime` group — set `_defaultMobLootLifetime`
   and add `_mobLootLifetimes` rows for types you want to override (set a row's `Lifetime` to `0` to
   make that type never despawn).
3. **Conditional drops**: configure `LevelConfig.AdditionalLootConfig` (condition → drop table) and the
   loot points / `AdditionalLootSpawnHolderComponent` in the scene.
4. **Map loot**: place `MapLoot` objects in the scene with their `LootComponent` filled in; they're
   swapped for pooled loot at level start.

## Adding a new loot type

1. Add a member to the `LootType` enum (`LootType.cs`).
2. Handle its **pickup** in `CollisionSystem.PlayerVsLoot` (what it gives the player).
3. Handle its **sprite** in `LootSystem.HandlingRequests` (a new `case`, or rely on the `SpriteHolder`
   default). Add a per-type config/holder if the item needs one (mirror `Bonus`/`Grenade`).
4. (Optional) add a `_mobLootLifetimes` entry if its despawn time should differ from the default.

## Related docs

- `Docs/AmmoSystem.md` — ammo loot caliber resolution and the per-caliber reserve.
- `Docs/BonusFeature.md` — what `LootType.Bonus` applies (speed/shield modifiers).
- `Docs/GrenadeFeature.md` — what `LootType.Grenade` feeds into.

## Ground aura (`LootAura`)

Every loot item gets a flat ring on the ground under it (`LootAura.cs`, created in `Loot.Awake`). The ring is designed to read on any floor:

The ring floats **0.15 m** above the loot position (`LootAura.GroundOffset`). That clears every floor dressing layer (suburb ground tops out at about 0.09 m) and the navmesh height error. At 0.035 m it z-fought with the road surface in "Тупик Мейпл-Корт" and flickered.
- On dark floors, you see the bright ring and the soft inner glow, tinted by loot type.
- On light floors (ice, concrete), you see the dark outer halo and the thin dark inner edge. The tint doesn't affect them because black stays black.

Colours (`LootAura.ColorFor`, set by `LootSystem` on every spawn through `Loot.SetAuraColor`):

| Loot type | Colour |
|---|---|
| Ammo | Gold |
| Health | Green |
| Weapon | Orange |
| Grenade | Red |
| Bonus | Cyan |

The ring is 1.7 m wide and pulses slowly (scale ±4%, alpha 0.8–1, random phase per instance).

Implementation: a tiny world-space Canvas with a UI `Image`. The UI shader is always in the build, unlit and transparent. The texture is a 128 px radial profile generated once and shared. Map loot goes through the same `LootSystem` spawn path, so it gets the aura too.
