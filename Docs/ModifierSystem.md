# Modifier & Bonus System — Reference

The **modifier system** is the project's single mechanism for time-limited, stacking, data-driven
buffs/debuffs/damage-over-time on any entity (player, mobs, camera, bullets). A *modifier* is a
serializable object carrying an effect (`Value`) and a `Lifetime`; it lives in an entity's
`ModifierOwnerComponent.Modifiers` list, ticks down in `ModifiersSystem`, and is *read* by whatever
system cares about that effect (movement reads speed, damage reads shield, etc.).

The **bonus system** (pickable speed/shield power-ups) is a thin feature layered on top — a bonus is
just a configured modifier granted to the player on pickup, with a UI bar. See
[`BonusFeature.md`](BonusFeature.md) for that layer; it's summarized at the end here.

> Mental model: **modifiers are passive data**. Adding one does nothing by itself — a *consumer*
> system must look for it. `Lifetime` and damage-over-time are the only behaviours the modifier
> framework runs centrally; every other effect (speed, shield, …) is implemented by a reader.

---

## 1. The Modifier model — `Assets/_Scripts/Modifier.cs`

### Base class

```csharp
[Serializable]
public abstract class Modifier
{
    [ValueDropdown(nameof(GetModifierIds))] public string Id;   // categorization label (see §1.4)
    public BuffSource Source;        // who/what applied it (informational)
    public float Value;              // effect magnitude — meaning is per-consumer
    public float Lifetime;           // seconds remaining; ticked down by ModifiersSystem
    public bool HasEffect;           // if true, applying it spawns a VFX…
    [ShowIf(nameof(HasEffect))] public string EffectId;  // …this id from EffectsHolder

    public T Clone<T>() where T : Modifier => (T)MemberwiseClone();
}
```

`Clone<T>()` is a shallow `MemberwiseClone` — **always clone a config's modifier before applying it**
so each owner gets its own `Lifetime` countdown. Producers do this (`GetAllModifiersAsCopies`,
`BonusConfig.CreateModifierInstance`, `CollisionSystem` mob attack mods).

### Concrete types

| Class | Extra fields | `Value` means | Consumed by |
|-------|-------------|---------------|-------------|
| `SpeedModifier` | — | movement-speed **multiplier** (1.5 = +50%, 0.8 = −20%) | `MoveSystem`, `PlayerMovementSystem` |
| `ShieldModifier` | `DamageType ImmuneType` | incoming-damage **multiplier** (0.5 = −50%) | `DamageSystem` (player). `ImmuneType` currently unused |
| `DamageModifier` | `Interval`, `DamageType Type`, `Chance`, `_iterationTimer` | damage dealt **per tick** | `ModifiersSystem` (DoT) — see §4 |
| `HealthModifier` | `Interval`, `HealthModifierType Type` | (intended periodic heal/harm) | **none yet** — defined but no consumer |

`DamageModifier` is the only type implementing **`IIteratableModifier`** (`bool TryIterate(dt, out
value)`) — that's what drives damage-over-time / poison / bleed ticking.

### Supporting enums

- **`BuffSource`** (`BuffSource.cs`): `None, Weapon, Damage, Explosion, Environment, Mob, Player,
  Projectile, Trap` — provenance tag, informational only.
- **`HealthModifierType`** (`HealthModifierType.cs`): `Unknown, Bleed, Poison, Fire, Magic`.
- **`DamageType`** — used by `DamageModifier.Type` / `ShieldModifier.ImmuneType` and effect linkage.

### `ModifierConstants` — id catalogue (categorization only)

A static table of string ids grouped by prefix (`Speed*`, `Health*`, `Damage*`, `Shield*`). The
inspector's `Id` dropdown is populated by **matching the prefix to the concrete type**
(`SpeedModifier` → `Speed*`, etc.). The `Id` is a **label** (used for the dropdown, effect linkage,
debugging) — **runtime behaviour comes from the concrete C# subclass + `Value`, not from the `Id`
string.** Two `SpeedModifier`s with different ids behave identically.

Current ids: speed (`SpeedMeleeAttackerDebuff`, `SpeedMeleeRecieverDebuff`, `SpeedLowHealthDebuff`,
`SpeedShotDebuff`, `SpeedReloadDebuff`), health (`HealthBleeding/Poisoning/Burning/Regeneration/
MagicDebuff/MagicBuff`), damage (`DamageMeleeBleeding/MeleeBurning/ShotBleeding/Poisoning`), shield
(`ShieldElectricity/Fire/Magic/Physical`).

### Serialization

Modifiers are polymorphic, so config assets serialize them with **`[SerializeReference, OdinSerialize]
private Modifier[] …`** (see `MobConfig.AttackModifiers`, `MeleeConfig.Modifiers/Debuffs`,
`GunConfig.ShotDebuffs/ReloadDebuffs`, `BonusConfig.Modifier`). The Odin inspector renders a type
picker so designers choose `SpeedModifier`/`ShieldModifier`/… per entry. Mirror this attribute pair
for any new modifier-carrying field.

---

## 2. Components — `Assets/_Scripts/ECS/Components.cs`

| Struct | Kind | Fields | Notes |
|--------|------|--------|-------|
| `ModifierOwnerComponent` | per-entity | `int Entity; Transform Transform; List<Modifier> Modifiers; bool ReadyToRemove;` | The bag of active modifiers on an entity. `Transform` lets effect/DoT find the owner. `ReadyToRemove` is currently unused. |
| `TryApplyModifierComponent` | request | `int TargetEntity; Modifier Modifier;` | "Apply this modifier to that entity." Consumed by `ModifiersSystem`. |
| `ApplyModifierResponseComponent` | — | `int TargetEntity; Modifier Modifier;` | Defined but **currently unused** (reserved). |

**Entities that own a `ModifierOwnerComponent`:** player + camera follower (`EntryPoint`), every mob
(`MobSpawnSystem`), every bullet (`BulletSystem`). Each initializes `Modifiers = new()`.

---

## 3. The heartbeat — `ModifiersSystem` (`ECS/ModifiersSystem.cs`)

Registered in `EntryPoint.RegisterSystems()` early — right after `AnimationSystem`, **before** the
combat/collision/damage systems. Per frame it does three things:

1. **Apply requests.** For every `TryApplyModifierComponent`: validate target still has
   `ModifierOwnerComponent`, add `request.Modifier` to its list. If the modifier `HasEffect`, look up
   the effect in `EffectsHolder` and emit a `RequestEffectComponent` (parented to the owner, tagged
   with the modifier's `DamageType` if it's a `DamageModifier`). Delete the request entity.
2. **Tick lifetimes.** For every owner, decrement each modifier's `Lifetime` by `deltaTime`; collect
   `(owner, modifier)` pairs whose `Lifetime <= 0` into a reusable scratch list.
3. **Iterate DoT.** For every modifier implementing `IIteratableModifier`, call `TryIterate`; for a
   `DamageModifier` that fires, emit a `RequestDamageComponent` on the owner (self-damage tick).
4. **Remove expired** from each owner's list (after the loop, via the scratch list).

> Frame ordering matters: `ModifiersSystem` ticks/removes **before** `DamageSystem` and movement run
> the same frame, so a modifier that expired this frame is already gone when shield/speed are read.
> A modifier *applied* later in the frame (melee, bonus, damage-borne) starts ticking **next** frame.

---

## 4. Damage-over-time (DoT) flow

```
DamageModifier on owner ──(ModifiersSystem, every Interval)──► RequestDamageComponent{owner, Value}
                                                              └─► DamageSystem subtracts health
```

`DamageModifier` holds an internal `_iterationTimer`; `TryIterate` returns `true` once per `Interval`
and outputs `Value` as the tick damage. This powers bleed/poison/burn. `Chance` (0–1) is used by
producers (`CollisionSystem`) to *probabilistically apply* the modifier on hit, not by the tick.

---

## 5. How modifiers get applied — two paths

There is **no single chokepoint**; producers use one of two equivalent paths:

### A. Direct list add (synchronous, same frame)
Add a *clone* straight into the target's `ModifierOwnerComponent.Modifiers`:
- **`MeleeSpawnSystem`** — on a landed melee: adds `MeleeConfig.Modifiers` clones to each target in
  radius, and `MeleeConfig.Debuffs` clones to the **attacker** (self-debuff, via `TryApplyDebuffs`).
- **`BonusSystem`** — adds the bonus's modifier clone to the player (see §9).

### B. Request via `TryApplyModifierComponent` (processed by `ModifiersSystem`, applied next frame)
- **`DamageSystem`** — when a `RequestDamageComponent` carries `DamageModifiers`, it spins up a
  `TryApplyModifierComponent` per modifier targeting the damaged entity. Those damage-borne modifiers
  originate in **`CollisionSystem` PlayerVsMob**, which clones `MobConfig.AttackModifiers` (rolling
  `DamageModifier.Chance`) into `RequestDamageComponent.DamageModifiers` when a mob hits the player.

Path B is the one that also auto-spawns the linked VFX (step 1 of §3). Path A does not (callers spawn
their own effects if needed; `BonusSystem` replicates the VFX emission manually).

---

## 6. How modifiers get *read* (consumers)

| Consumer | Reads | Via | Effect |
|----------|-------|-----|--------|
| `MoveSystem`, `PlayerMovementSystem` | `SpeedModifier` | `GetModifier<SpeedModifier>()` | multiplies move speed |
| `DamageSystem` | `ShieldModifier` | `GetModifier<ShieldModifier>()` | multiplies incoming player damage |
| `ModifiersSystem` | `IIteratableModifier`/`DamageModifier` | type check | DoT ticks (§4) + lifetime decay |
| `EffectsSystem` | any `DamageModifier` of a type | `HasModifierWithDamageType(type)` | keeps a linked VFX alive/repositioned while a matching modifier persists (§8) |

### `Utils` helpers (`Assets/_Scripts/Utils.cs`)

```csharp
// Multiplicative composite of all modifiers of type T. Returns 1 when none → safe to always multiply.
public static float GetModifier<T>(this ModifierOwnerComponent modifiers);

// True if any DamageModifier of the given DamageType is present.
public static bool HasModifierWithDamageType(this ModifierOwnerComponent modifiers, DamageType type);
```

`GetModifier<T>` multiplies the `Value` of **every** modifier assignable to `T`. So two speed buffs
(1.5 × 1.2) and a debuff (× 0.8) all compose into one factor. The "returns 1 when empty" contract is
why callers can do `speed * GetModifier<SpeedModifier>()` and `damage * GetModifier<ShieldModifier>()`
unconditionally.

---

## 7. Producers at a glance

| Producer | Source field | Path | Applies to |
|----------|--------------|------|------------|
| `CollisionSystem` (mob melee hit) | `MobConfig.AttackModifiers` (rolls `Chance`) | B (via `RequestDamageComponent.DamageModifiers` → `DamageSystem`) | player |
| `MeleeSpawnSystem` (melee hit) | `MeleeConfig.Modifiers` | A | each target in radius |
| `MeleeSpawnSystem` (on attack) | `MeleeConfig.Debuffs` | A | the attacker (self) |
| `BonusSystem` (pickup) | `BonusConfig.Modifier` | A | player |
| `GunConfig.ShotDebuffs` / `ReloadDebuffs` | declared | — | **not yet consumed** (no system applies them) |

---

## 8. Effect linkage (modifier ↔ VFX)

`RequestEffectComponent` carries `Parent`, `DamageType`, and `ModifierEntity`. When `ModifiersSystem`
applies a modifier with `HasEffect`, it requests the effect tagged with the owner entity + the
modifier's `DamageType`. In **`EffectsSystem`**, such an effect (`ModifierEntity != 0`,
`DamageType != Unknown`) is **kept alive and repositioned to the owner** as long as
`HasModifierWithDamageType(DamageType)` is true; once no matching modifier remains, `ModifierEntity`
is cleared and the effect returns to the pool. This is how a "burning" loop VFX persists exactly for
the duration of a burning `DamageModifier`.

---

## 9. The Bonus subsystem (summary — full detail in `BonusFeature.md`)

Pickable player power-ups built entirely on the above:

- **`BonusConfig`** (SO) = a `BonusType` (which UI bar) + a `[SerializeReference] Modifier` (the
  granted effect; `Lifetime` = duration). **`BonusConfigHolder`** maps id→config; referenced from
  `MainHolder.BonusConfigHolder`.
- Pickup: `LootType.Bonus` loot → `CollisionSystem` emits `RequestApplyBonusComponent{ConfigId}`.
- **`BonusSystem`** (after `DamageSystem`): clones the config modifier, **refresh-replaces** any
  same-type bonus, adds it to the player's modifier list (path A), and records an `ActiveBonus`
  (`Type`, `Modifier`, `TotalDuration`). Each frame it reads the modifier's `Lifetime` to drive
  `PlayerStats.SetBonus(type, Lifetime/TotalDuration, Lifetime)` and prunes at 0.
- Effects reuse existing consumers: **Speed** via `GetModifier<SpeedModifier>()` (movement);
  **Shield** via `GetModifier<ShieldModifier>()` (`DamageSystem` % reduction).
- UI: `ValueBar` gained an optional `UniText` (`SetText`) for the seconds-left readout.

---

## 10. Status / known gaps

- `HealthModifier` — defined (with `HealthRegeneration`/`Bleeding`/… ids) but **no consumer**; it
  isn't iteratable, so periodic heal/harm isn't wired. To use it, either make it implement
  `IIteratableModifier` (like `DamageModifier`) or add a reader.
- `ShieldModifier.ImmuneType` — unused; shield currently only does `Value` % reduction (no
  type-specific immunity). `RequestDamageComponent` carries no single `DamageType` for melee, so
  type-aware shields would need that plumbing.
- `GunConfig.ShotDebuffs` / `ReloadDebuffs` — declared on the config, **not applied** by any system.
- `ApplyModifierResponseComponent` and `ModifierOwnerComponent.ReadyToRemove` — declared, unused.

---

## 11. Recipes

**Add a new modifier type** (e.g. a damage-output buff):
1. Add `class DamageBuffModifier : Modifier {}` in `Modifier.cs` (add a `Get…Ids` prefix branch if you
   want a dedicated id group).
2. Add a consumer: read it where the effect applies, e.g. `weapon.Damage * owner.GetModifier<DamageBuffModifier>()`.
3. Produce it from a config (`[SerializeReference, OdinSerialize] Modifier[]`) via path A or B.

**Apply a modifier from code:** clone it, then either
`modifierOwner.Modifiers.Add(cfg.X.Clone<Modifier>())` (immediate) or
`world.CreateSimpleEntity<TryApplyModifierComponent>()` with `TargetEntity`+`Modifier` (next frame,
auto-VFX). Always clone — never share a config instance across owners.

**Gotchas**
- Forgetting to clone → all owners share one `Lifetime`, expiring together.
- Adding to `Modifiers` when the list is `null` → guard/`new()` first (owners init it, but be safe).
- Expecting an `Id` to change behaviour — it doesn't; behaviour is type + `Value`.
- No automated tests — verify modifier feel (durations, stacking, DoT cadence) in Play mode.
```
