# Helmets (Headwear) — Reference

Mobs can wear a helmet. The helmets are primitive-style models without textures, built in code like the guns (`Docs/WeaponModels.md`).

| Prefab | Look | Who wears it |
|---|---|---|
| `_Prefabs/Headwear/HardHat_Construction.prefab` | orange construction hard hat: dome, centre ridge + 2 side ribs, brim with a front peak | `ToxicHelmet`: the slow Toxic zombie (same `ToxicMob.prefab`) with **x2 HP** (400), **scale 1.25** (`MobConfig._scale`) and hit radius 1.25. The helmet is fitted to the head bones, so it scales with the mob. Spawns from `ToxicPoint` at stages level4–level6. |
| `_Prefabs/Headwear/Helmet_PASGT.prefab` | **black** PASGT ("Fritz", shape after `Assets/REFs/pasgt.jpg`, material `Helmet_PASGT_Black`, metal `Helmet_Bolt` bolts): shell sized close to the skull, lifted by `Lift` = 2.4 cm and the brow cut at 68° so the eyes stay open, rim nearly straight over the brow, dropping at the temples over the ears and nape (`BuildShell` `sideMaxDeg` profile), flared skirt/visor lip, side bolts, black V chin-strap harness with buckles. No cover band (the old `PASGT_Band` mesh is deleted by the builder) | `GunnerMob` |

## Building

Run **Tools ▸ Crowds ▸ Build Helmet Models** (`Editor/Headwear/HelmetModelBuilder.cs`). It does the following:

1. **Meshes.** Builds the shells in code (an outer and an inner surface plus a rim, so they look solid from any side) into `_Prefabs/Headwear/Meshes/*.asset`. It overwrites them in place, so the guids stay the same.
2. **Materials.** Creates URP/Lit materials in `_Materials/Headwear/` once and never overwrites them.
3. **Prefabs.** Rebuilds only the children of each prefab. The root and its `Headwear` component, with `_offset` and `_extraScale`, survive a re-run.
4. **`ToxicHelmet` config.** On first run it copies `Toxic.asset` to `_Data/Mobs/ToxicHelmet.asset` (id `ToxicHelmet`, HP ×2) and adds it to `MobConfigHolder`.
5. **Assigning helmets.** Sets `MobConfig._headwear` on `GunnerMob` and `ToxicHelmet`.

> Run it **before Play**. `ToxicPoint.prefab` already references the `ToxicHelmet` id. Until the config exists, `MobConfigHolder.GetConfigById` logs a warning and stage 4+ toxics with a hard hat won't spawn.

## Runtime fitting

`Headwear.AttachTo(mob, prefab)` is called once per new mob instance from `MobSpawnSystem.SetupNewInstance`. That covers both `SpawnMob` and the pool prewarm in `EntryPoint`. Steps:

1. Find `*:Head` and `*:HeadTop_End` (Mixamo generic rig).
2. Measure the Head→HeadTop distance and scale the prefab by `distance / ReferenceHeadHeight (0.2 m)`. One prefab therefore fits any Mixamo rig and any `MobConfig.Scale`.
3. Orient the helmet: up = Head→HeadTop, forward = the mob root's forward. This relies on a fresh instance being in bind pose.
4. Parent it to the Head bone, so it follows run, attack and death animations.

Other details:

- **Prefab convention.** Root = the Head bone pivot (skull base), +Y toward the head top, +Z = the face. Units are metres for a 0.2 m head.
- **Tuning.** Use `Headwear._offset` and `_extraScale` on the prefab root. Don't move the children: the builder rebuilds them.
- **No Head bone.** The helmet goes on the root at about 1.6 m, and a warning is logged.
- **VAT crowd mobs** (`CrowdLibrary` set): the helmet is skipped with a warning, because their bones don't animate.
- **Tint.** `MobConfig.Tint` doesn't affect helmets: they use `MeshRenderer`, while the tint only applies to `SkinnedMeshRenderer`.
- **Optimize Game Objects.** If you enable it on mob FBX imports (see performance notes), add `Head` and `HeadTop_End` to *Extra Transforms to Expose*.

## Gunner changes (same task)

`GunnerMob`:

- `WeaponOverride` = `_Prefabs/Weapons/AR.prefab` (an M4 instead of the Mini-14). The left-hand IK follows `Mob.Weapon`, so it survives the swap.
- `BurstCount` = 3, `BurstInterval` = 0.1 s.
- `Cooldown` 0.8 → 1.0 s.
- `Health` 50 → **75** (+25 for the helmet, 16.11.2026).

`GunnerBare` (no helmet, see `Docs/HockeyLevelFeature.md`) keeps the original rifleman: the Gunner prefab's own Mini-14 (no `WeaponOverride`), single shots, cooldown 0.8 s, 50 HP. `HockeyLevelBuilder.EnsureGunnerBare` enforces this on every run.

The shot still uses `ZombieGun` (12 dmg, accuracy 0.9).

Note: with the player's `HitInvulnerabilityTime` = 0.4 s, a 0.3 s burst deals at most **one** hit (12). The 2nd and 3rd bullets are absorbed. A burst is more likely to land than a single shot, but it doesn't triple the damage. If you want the burst to hurt more, raise `ZombieGun` damage, or let enemy bullets bypass invulnerability.

## Files

`Headwear.cs`, `Editor/Headwear/HelmetModelBuilder.cs`. Changes in other files:

- `MobConfig._headwear`.
- `MobSpawnSystem.SetupNewInstance`. This also fixes a bug: prewarmed pool instances never got `RangedMobConfig.WeaponOverride`.
- `EntryPoint.PrewarmMobPool`.
- `GunnerMob.asset`.
- `ToxicPoint.prefab`.
