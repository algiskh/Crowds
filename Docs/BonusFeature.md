# Bonus Feature — Reference

Pickable player bonuses (speed up, shield) built **on top of the existing modifier system**
(`Modifier.cs` / `ModifiersSystem.cs`). A bonus is just a configured `Modifier` granted to the
player on pickup, with a UI bar showing remaining time. This doc is the source of truth so the
code doesn't need re-exploring.

## Data flow

```
Bonus loot (map or mob drop, LootType.Bonus, loot.Id = BonusConfig.Id)
   │  player walks into it (CollisionSystem PlayerVsLoot)
   ▼
RequestApplyBonusComponent { ConfigId }
   │  BonusSystem
   ▼
look up BonusConfig → clone its Modifier → (refresh: drop existing bonus of same type)
   → add modifier to player ModifierOwnerComponent.Modifiers
   → register ActiveBonus { Type, Modifier, TotalDuration = Lifetime }
   ▼
each frame:  ModifiersSystem ticks Modifier.Lifetime down (and removes it at 0)
             BonusSystem reads Lifetime → PlayerStats bar fill = Lifetime/TotalDuration,
                                          UniText = ceil(seconds left); prunes at 0
```

The **effect** of each bonus is whatever its modifier already does in the common system:
- **Speed** → `SpeedModifier` (Value = speed multiplier). Already consumed by `MoveSystem` /
  `PlayerMovementSystem` via `GetModifier<SpeedModifier>()` — no extra wiring.
- **Shield** → `ShieldModifier` (Value = **incoming-damage multiplier**, e.g. `0.5` = 50% reduction).
  Consumed in `DamageSystem`: when the player takes damage, `damage *= GetModifier<ShieldModifier>()`
  (returns `1` when no shield → no reduction). Multiple shields combine multiplicatively.

## Stacking semantics

**Refresh, one bonus per `BonusType`.** Picking up a bonus while one of the same type is active
removes the old modifier + record and applies the new one (timer/value reset). Keeps the single
bar/timer unambiguous.

## Per-bonus config — `BonusConfig` (SO)

`SO/BonusConfig.cs` — one asset per bonus. `SO/BonusConfigHolder.cs` maps id→config
(`GetConfig(id)`, `Default` = first entry). Referenced from `MainHolder.BonusConfigHolder`.

| Field | Meaning |
|-------|---------|
| `Preview` | loot icon (falls back to `SpriteHolder."Bonus"` if unset) |
| `Id` | matched against loot `Id` |
| `Type` | `BonusType` — which PlayerStats bar (`Speed`→speedbar, `Shield`→shieldbar) |
| `Modifier` | `[SerializeReference]` polymorphic modifier the bonus grants. Set its `Value` (speed mult / damage mult) and **`Lifetime` = duration in seconds** (drives bar fill + seconds text). `HasEffect`/`EffectId` optionally spawns a pickup VFX. |

Mirror the `GrenadeConfig` Odin layout; `Modifier` uses the same `[SerializeReference, OdinSerialize]`
pattern as `MobConfig.AttackModifiers`.

## Components (`ECS/Components.cs`, `#region Bonus`)

| Struct | Kind | Fields |
|--------|------|--------|
| `RequestApplyBonusComponent` | request | `string ConfigId` — emitted by `CollisionSystem` on pickup |
| `ActiveBonus` | plain struct | `BonusType Type; Modifier Modifier; float TotalDuration;` |
| `ActiveBonusesComponent` | singleton | `List<ActiveBonus> Value;` — created in `EntryPoint` |

## System — `BonusSystem` (`ECS/BonusSystem.cs`)

Registered in `EntryPoint.RegisterSystems()` **right after `DamageSystem`** (so it consumes the
pickup request emitted by `CollisionSystem` the same frame, and reads `Lifetime` values already
ticked by `ModifiersSystem` earlier in the frame). Two passes per frame:
1. **Apply** every `RequestApplyBonusComponent`: refresh same-type, clone+add the modifier, register
   an `ActiveBonus`, optionally spawn the pickup VFX.
2. **Drive UI / prune**: for each `ActiveBonus`, `PlayerStats.SetBonus(type, Lifetime/TotalDuration,
   Lifetime)`; at `Lifetime <= 0` call `ClearBonus(type)` and drop the record.

`Modifier.Lifetime` is the single source of truth — ticked/removed by `ModifiersSystem`, read here.

## UI

- `ValueBar` gained an **optional** `[SerializeField] UniText _valueText` and `SetText(string)`
  (`IValueBar`). Empty string hides it. Used to show seconds left; no-op when unassigned.
- `PlayerStats`:
  - `Awake` starts the speed/shield bars empty (`ApplyValue(0)` → hidden) instead of full.
  - `SetBonus(BonusType, float fraction, float secondsLeft)` — fill (0..1) + ceil-seconds text.
  - `ClearBonus(BonusType)` — empties/hides the bar.
  - (legacy `SetBonusValue(BonusType,float)` kept.)
  - Bars are normalized to max `1` = `fraction` of remaining duration.

## Integration touch-points (modified files)

- `LootType.cs` — added `Bonus`.
- `CollisionSystem.cs` — `case LootType.Bonus:` → emits `RequestApplyBonusComponent { ConfigId = loot.Id }`.
- `LootSystem.cs` — bonus loot icon uses `BonusConfig.Preview`, else `SpriteHolder."Bonus"`.
- `DamageSystem.cs` — player damage scaled by `GetModifier<ShieldModifier>()`.
- `SO/MainHolder.cs` — `BonusConfigHolder BonusConfigHolder` (Configs group).
- `EntryPoint.cs` — creates `ActiveBonusesComponent`; registers `BonusSystem` after `DamageSystem`.
- `PlayerStats.cs`, `UI/ValueBar.cs` — UI (above).

## Unity setup checklist (assets to author)

1. Create **`BonusConfig`** assets (right-click → Create → Scriptable Objects → BonusConfig):
   - **Speed**: `Type = Speed`, `Id` e.g. `"speedup"`, add a **`SpeedModifier`** in the `Modifier`
     field with `Value` e.g. `1.5`, `Lifetime` e.g. `8`.
   - **Shield**: `Type = Shield`, `Id` e.g. `"shield"`, add a **`ShieldModifier`** with `Value` e.g.
     `0.5` (= 50% damage reduction), `Lifetime` e.g. `6`.
   - (optional) set `Preview`; set `HasEffect` + `EffectId` for a pickup VFX.
2. Create a **`BonusConfigHolder`** asset, add the configs, assign it to `MainHolder.BonusConfigHolder`.
3. On the **`PlayerStats`** widget, assign `_speedbar` / `_shieldbar` (already present). To show
   seconds, assign each bar's new optional `_valueText` (a `UniText`, ideally a child of the bar so
   it hides with it).
4. Spawn bonus loot: add `PossibleLoot` entries with `LootType = Bonus` and `Id` = a `BonusConfig.Id`
   to a `MobConfig.PossibleLoots` (mob drop) and/or a scene `MapLoot.LootComponent` (placed on map).
5. (optional) add `SpriteHolder` sprite under id `"Bonus"` as icon fallback.
6. Verify in Play mode: bar fills on pickup and drains over the duration, seconds count down, speed
   actually changes / damage is reduced, and same-type re-pickup refreshes the timer.

## Reuse / extension notes

- New bonus types: add a `BonusType`, a bar to `PlayerStats.GetBonusBar`, and (if it needs a runtime
  effect) consume its modifier where relevant. `Strength` already exists in the enum but has no bar
  and no consumer yet.
- Bonuses are pure data: a `BonusConfig` is any `Modifier` + a `BonusType` for which bar to drive.
- No automated tests — verify feel in Play mode.
