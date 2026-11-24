# Sewer hatches: poison zone + toxic spawns on Infinite Scroll — Reference

The sewer hatch prop (`_Prefabs/Props/SewerHatch.prefab`, first made for the Key level, see `Docs/KeyLevelFeature.md`) is now a hazard. On Infinite Scroll (`Level_01`) the hatches are also **the only spawn points of the toxic zombies**.

## Poison zone

`SewerHatch` (`Assets/_Scripts/SewerHatch.cs`) is a round `ModifierZone` (radius 0.75 m, scaled with the transform). `ModifierZoneSystem` handles it like the oil slicks and the ice (`Docs/HockeyLevelFeature.md`):

| Setting | Value | Notes |
|---|---|---|
| Modifier | `DamageModifier` `DamageSewerStench`, `Poison`, 1 HP every 0.5 s | The first tick lands on entry. That is ≈ 2 HP/s while standing on it, so it's light next to the Toxic zombie's bite (3 HP / 0.5 s for 2 s). `ModifiersSystem` ticks it as DoT (`IsPeriodic`: weak vignette flash, no shake). |
| Linger | 0.3 s | The modifier fades shortly after you step off. |
| `AffectsMobs` | **off** | The zombies come out of these hatches, so they don't get poisoned. |
| `EntryEffect` | **on** | On entry the `toxicDamage` VFX is spawned on the player, linked to the modifier. It follows the player and goes back to the pool once no `Poison` modifier is left. |

Two new `ModifierZone` options, both off by default so oil slicks and ice behave as before:

- `_affectsMobs` (default **on**): turn it off to make a zone player-only.
- `_entryEffect` (default **off**): spawn the modifier's `HasEffect`/`EffectId` VFX once per entry, linked to the owner.

The zone settings are written by `KeyLevelBuilder.BuildHatchPrefab`, so every rebuild of the prefab restores them.

## Infinite Scroll placement

Run **Tools ▸ Crowds ▸ Build Sewer Hatches (Infinite Scroll)** (`Editor/Levels/SewerHatchBuilder.cs`) **after** *Build Suburb* and *Build Street Vehicles*. Re-running it is safe. The builder:

1. rebuilds `SewerHatch.prefab` (same guid, so the Key level keeps its hatches and gets the poison too);
2. removes every `ToxicPoint` instance from `Level_01`;
3. gives every `FloorSector` a `SewerHatches` child (re-created on each run, so it rides along when the sector recycles). The child holds `SewerHatch_N` plus a `ToxicPoint_HatchN` spawn on its centre.

Hatches go only on the road lanes, and here and there on the sidewalks. They never go on grass, on the dashed centre line or at the kerb. Coordinates are sector-local (road x −11…3, centre line x −4, sidewalks x −14…−12.5 / 4.5…6, see `Docs/SuburbFeature.md`):

| Sector | Surface | Where | Notes |
|---|---|---|---|
| 0 (z −50) | Road, east lane | (−1.5, −14) | |
| 0 | Sidewalk, houses' side | (−13.25, 9) | Lid ×0.9 so it fits the 1.5 m walk. |
| 1 (z 0, start) | Road, west lane | (−7.5, −8) | ≥ 10 m from the start and the pre-placed loot. |
| 1 | Road, east lane | (−0.8, 19) | |
| 2 (z +50) | Road, west lane | (−7.6, 15) | |
| 2 | Sidewalk, far side | (5.25, −19) | Lid ×0.9. |

On every run the builder logs a warning when a hatch is closer than 1 m to a vehicle obstacle, or closer than 2 m to a spawn, loot or breakable point or to pre-placed loot. `VehicleBuilder` already keeps vehicles clear of `ToxicPoint*`.

**Spawning.** Toxic zombies (`Toxic`, `FastToxic`, `ToxicHelmet`) come only from the `ToxicPoint` prefab, which now exists only on the hatches. The regular points (`RegularSpawnPoint`, `GroupSpawnPoint`) carry no toxic ids, so they are unchanged. There used to be 4 toxic points and now there are 6, so the cooldowns in `ToxicPoint.prefab` were multiplied by 1.5 to keep the same toxic rate (table in `Docs/GameDesignReview.md`). The usual distance window still applies: no spawns closer than 12.5 m or farther than 50 m from the player.

## Limits / not verified

- Not run in Play Mode yet. The builder has to be run in the editor. Check:
  - the hatches on the road and sidewalks, as seen from the camera;
  - the damage ticks and the green VFX on the player;
  - that toxic zombies spawn from the hatches and take no damage there.
- A mob standing on a hatch also stays immune when it is not toxic, because the zone is player-only.
- `DamageModifier.TryIterate` writes a `Debug.Log` on every tick (pre-existing), so standing on a hatch logs twice a second.
