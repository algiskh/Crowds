# Ammo System — Reference

Reserve ammo is tracked **per caliber**, not per weapon. Weapons that share a caliber share one
ammo pool (e.g. rifle + assault rifle both `Caliber.Cal762` / "7.62mm"); a shotgun has its own
`Caliber.Gauge12` pool. The magazine still lives on the weapon; only the reserve moved to a
per-caliber inventory. This doc is the source of truth so the code doesn't need re-exploring.

## Model

| Thing | Where |
|---|---|
| `Caliber` enum | `Enums/Caliber.cs` — `[InspectorName]` per member for dropdowns/UI; `None` = unset |
| `Caliber.ToDisplay()` | `Enums/Caliber.cs` — reads `[InspectorName]` (cached) for UI text |
| `GunConfig.Caliber` | `SO/GunConfig.cs` — a `Caliber` dropdown |
| `AmmoInventoryComponent` | `ECS/Components.cs` — singleton, `Dictionary<Caliber,int>` + `Get/Add/Spend` |
| `WeaponComponent` | `ECS/Components.cs` — magazine (`CurrentMagazineCount`) only; the old `AmmoCount` is gone |
| `LootComponent.AmmoCaliber` / `PossibleLoot.AmmoCaliber` | the ammo loot's caliber; `None` = current weapon |
| `AmmoConfig` | `SO/AmmoConfig.cs` — per-caliber **projectile prefab** + **loot icon** |
| `AmmoConfigHolder` | `SO/AmmoConfigHolder.cs` — `GetConfig(Caliber)` (null if unset); on `MainHolder` |
| seeding | `EntryPoint` — `StartAmmo` is added to the **starting weapon's caliber** |

`AmmoInventoryComponent` methods mutate the inner `Dictionary` (a reference type), so fetching the
component **by value** (`world.GetAsSingleton<AmmoInventoryComponent>()`) and calling `Add`/`Spend`
works — no `ref` needed. `Get/Add/Spend` treat `Caliber.None` as a no-op.

## Flow

```
Spawn (LootSystem, LootType.Ammo)
   if AmmoCaliber == None -> bake current weapon's caliber (or AmmoConfigHolder.First when no weapon)
   the resolved caliber is stored on the loot and drives its icon (AmmoConfig.LootIcon)

Pickup (CollisionSystem, LootType.Ammo)
   AmmoInventory.Add(loot.AmmoCaliber, loot.Count)  -> UpdateAmmoViewRequest
   (caliber was fixed at spawn; the None -> current-weapon branch remains only as a safety net)

Fire (WeaponFireSystem)
   magazine empty & reserve(current caliber) > 0  -> RequestReload

Reload (WeaponReloadSystem)
   ReloadMagazine / ReloadSingleAmmo
     ammoToLoad = AmmoInventory.Spend(currentCaliber, needed)
     CurrentMagazineCount += ammoToLoad

UI (UISystem -> WeaponUIView)
   reserve shown = AmmoInventory.Get(weapon.GunConfig.Caliber)
   SetWeaponView also sets the optional caliber label (_caliberText = Caliber.ToDisplay())
```

## Loot convention

Ammo loot carries the caliber in a dedicated **`AmmoCaliber`** enum field (on `PossibleLoot` drop
tables and on `MapLoot`'s serialized `LootComponent`):
- `AmmoCaliber = Gauge12` → adds to the 12-gauge pool regardless of the held weapon.
- `AmmoCaliber = None` (default) → **resolved at spawn** to the current weapon's caliber (or
  `AmmoConfigHolder.First` if there's no weapon) and **baked into the loot**. So a `None` ammo loot
  spawned while holding the rifle becomes a 7.62mm pickup — it shows the 7.62mm icon and gives 7.62mm
  even if you switch weapons before grabbing it.

`None` is the enum's `0` value, so existing ammo loot assets default to "current weapon at spawn" ammo.
(Other loot types still use the `Id` string for their subtype — weapon/grenade/bonus id; only ammo
uses `AmmoCaliber`.)

## Projectile & loot icon (AmmoConfig)

The **projectile prefab** and the **ammo-loot icon** live on `AmmoConfig` (per caliber), not on the
gun. `GunConfig` keeps only ballistics (damage, speed, spread, lifetime, check type).

- **Firing** (`BulletSystem`): the projectile prefab is resolved by the gun's caliber via
  `MainHolder.AmmoConfigHolder.GetConfig(caliber).ProjectilePrefab`, falling back to
  `MainHolder.BulletPrefab` when there's no `AmmoConfig` (or no prefab) for that caliber.
- **Loot icon** (`LootSystem`): ammo loot uses `AmmoConfig.LootIcon` for its caliber, falling back
  to `SpriteHolder.GetSpriteById("Ammo")` (used when `AmmoCaliber == None` or no config/icon).

> Caveat: `BulletPoolComponent` is a single shared `Stack<Bullet>` (not keyed by prefab), so a
> pooled bullet may visually be a different caliber's prefab. This pre-dates this change (the prefab
> was already per-gun). If distinct per-caliber projectile visuals must be reliable, key the bullet
> pool by prefab/caliber like the mob/effect/decal pools do.

## Adding a caliber

Add a member to the `Caliber` enum with an `[InspectorName("...")]` for its display name. That's the
only code change — it then appears in every `GunConfig` / ammo-loot dropdown and in the UI.

## Editor setup

1. On every **`GunConfig`** pick a **Caliber** from the dropdown (weapons that share ammo pick the
   same one). Leaving it `None` is a config error (that weapon can never hold reserve).
1a. Create an **`AmmoConfig`** per caliber (Caliber + projectile prefab + loot icon) and add them to
   an **`AmmoConfigHolder`** assigned on `MainHolder`. Calibers without an `AmmoConfig` fall back to
   `MainHolder.BulletPrefab` for the projectile and the generic "Ammo" sprite for loot.
2. (Optional) Wire **`WeaponUIView._caliberText`** (a `UniText`) to show the current caliber.
3. For caliber-specific ammo pickups, set the ammo loot's **AmmoCaliber**; leave `None` for
   "ammo for the current weapon".
