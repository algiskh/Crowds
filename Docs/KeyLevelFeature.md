# Key Level ("Запертая дверь") and Mission Objects — Reference

This is the 2nd campaign level, after `Assets/REFs/LEVEL_KEY.png`. It has three platforms linked by gates, wave fights, a key, and an exit. The level sits on a small reusable "mission" layer on top of difficulty stages: gates, keys, zones, stage loot and objective text. Other scripted levels can reuse the same layer.

## Building

Run **Tools ▸ Crowds ▸ Build Key Level** (`Editor/Levels/KeyLevelBuilder.cs`). Re-running it rewrites everything in place and keeps the guids. Materials are created once.

| Output | Path |
|---|---|
| Level prefab | `_Prefabs/Levels/Level_02_Key.prefab` (+ floor mesh `_Prefabs/Levels/Meshes/Level_02_Key_Floor.asset`) |
| Level config | `_Data/Levels/Configs/Level_Key.asset` |
| Menu entry | `_Data/Levels/Definitions/Level_Key.asset`, inserted as **#2** in `CampaignLevelLibrary` |
| Props | `_Prefabs/Props/SewerHatch.prefab`, `KeyPickup.prefab`, `LevelGate.prefab`, `OilSlick.prefab` |
| Wall-hit effect | `_Prefabs/Effects/WallSpark.prefab`, registered in `Resources/FxHolder.asset` as `wall_spark` |
| Stage conditions | `_Data/SmartConditions/Mission/` (`MobsCleared`, `PlayerInZone_P2`, `PlayerInZone_Exit`, `GateOpened_G23`, `LowAmmoNoneAround`) |
| Materials | `_Materials/Level/` |

Hand edits to the level prefab are lost on re-run. To change the layout, edit the builder, or stop using it and edit the prefab by hand.

## Layout

Floor top is at y = -0.5. The whole floor is one `FloorSector` with `SectorMode.Sliding`. The floor renderer uses `renderingLayerMask = 5` (default + rendering layer 2). The Decal Projector prefab has `DecalLayerMask = 4`, so URP decals (blood, scorch) draw only on renderers that have bit 2 set. With the default mask of 1, the floor got no decals. Level_01 floors also use 5. Walls and props keep 1, so they get no decals.

| Area | Bounds (x, z) | Contents |
|---|---|---|
| P1 (east) | x 14…42, z −12…12 | Start point (38, 0), facing west. 5 open wall pipes, each with a spawn point in front: north and south walls at x = 22 and 34, east wall at z = −7. |
| P2 (west, long hall) | x −30…14, z −12…12 | All 6 sewer hatches, mirror-symmetric about x = −8 and z = 0: (−20, ±6.5), (−8, ±8), (4, ±6.5). 5 crates on the west side. The key at (−8, 2.5). |
| P3 (north of P2's west half) | x −30…−4, z 12…38 | 5 open wall pipes with spawn points: west and east walls at z = 20 and 31, north wall at x = −26. 3 oil slicks at (−24, 27), (−10, 23.5) and (−14, 32.5). |
| Exit corridor | x −20…−14, z 38…48 | Exit zone at (−17, 45) with a green floor marker. |

Gates:
- `g12` sits in the P1/P2 wall at x = 14. It opens at level3 and **shuts behind the player** once they are in zone `p2` and at least 4 m from it.
- `g23` sits in the P2/P3 wall at (−17, 12) and needs the key. It shuts behind the player once they are in zone `p3`.
- `g3n` sits in the P3 north wall at (−17, 38).

Zones: `p1`, `p2`, `p3` (each platform, inset about 1 m from the walls) and `exit`.

Walls are 2.2 m high and use the Default layer. They have no colliders: the navmesh bake cuts them out, and the player's `NavMeshAgent` keeps the player on the navmesh. Every wall and every gate door carries a `BulletBlocker`, so **bullets of both teams stop on walls and closed gates**. Each hit plays `wall_spark`: the Epic Toon FX `SparkExplosion`, scaled to 0.35. The ETFX systems use Hierarchy scaling, so size, speed and spread shrink together.

**Heating decor (P1 + P2).** The outer walls of P1 and P2 form one rectangle, and a single heating circuit runs inside it along the walls:
- Two lines: supply (rusty red, 1.55 m high) and return (steel, 1.1 m high).
- The lines pass through the P1/P2 wall, with collars on both sides.
- Over the key gate `g23` they climb on risers and bridge above the door.
- It is kept simple: plain runs and elbows, with no flanges, brackets or radiators.
- One valve on the supply line near the boiler.
- A boiler on the P1 east wall at (40.85, 5), with a flue. The loop's east run passes through it.

The `Heating` root has a `NavMeshModifier` set to Ignore From Build. The boiler sits under `Props` with no modifier, so the bake cuts it out and agents walk around it. Pipes have no colliders and don't block bullets.

**Open wall pipes (P1, P3).** Each is a 1 m drain pipe sticking 0.8 m out of the wall at floor level: a shell, a lip, a black mouth, and a slimy puddle in front. The pipes are solid in the bake, so agents walk around them. A spawn point sits 2.4 m from the wall centre line, in front of each mouth. These are the only spawn points on P1 and P3.

**Oil slicks (P3).** Prefab `_Prefabs/Props/OilSlick.prefab`. Anyone standing on a slick, the player or a mob, moves 25% slower.
- `OilSlick` (`OilSlick.cs`, `[ExecuteAlways]`) is a `ModifierZone` subclass (see `Docs/HockeyLevelFeature.md`). It builds an irregular black blob mesh on enable. The radius wobbles around `_radius` by seeded harmonics (`_irregularity`, `_seed`), and `Contains()` uses the same function, so the slow zone matches the visible shape. Its zone modifier is a `SpeedModifier` (id `SpeedOilSlick`, 0.75) with `_linger` 0.25 s.
- `ModifierZoneSystem` (registered right before `ModifiersSystem`) gives the modifier to the player or mob standing on a slick. It refreshes the modifier every frame instead of stacking new ones, and `ModifiersSystem` removes it after the linger time. `MoveSystem` and `PlayerMovementSystem` apply it through `GetModifier<SpeedModifier>`.
- The slick mesh is regenerated at runtime and isn't saved as an asset. The renderer keeps rendering layer 1, so decals don't draw over the oil.

## Stage script (`Level_Key.asset`)

| Stage | What happens | Ends when |
|---|---|---|
| level1 | P1 wave 1/2: Slow + Fast. Spawns for 30 s. 9mm ammo at the start. | All mobs are dead (`MobsCleared`). |
| level2 | P1 wave 2/2: spawns for 40 s, denser. Ammo, health and a grenade appear. | All mobs are dead. |
| level3 | Breather: gate `g12` opens. On P2: Shotgun, 12ga and 9mm ammo, health. | At least 10 s have passed **and** the player is on P2 (`PlayerInZone_P2`). `g12` shuts behind them. |
| level4 | P2 wave 1/2: Toxic only, out of the 6 sewer hatches (the only P2 spawn points). 35 s, 6 s of rest at the start. | All mobs are dead. |
| level5 | P2 wave 2/2: FastToxic only. 45 s. | All mobs are dead. |
| level6 | The key appears with an effect, and reward loot spawns. Objective: "pick up the key, open the door". | Gate `g23` is open (the player brought the key to it). |
| level7 | P3 wave 1/2: Fast + Fatman. Ammo for the current weapon and a grenade appear. `g23` shuts once the player is on P3. | All mobs are dead. |
| level8 | P3 final wave: Fast + Fatman + ToxicHelmet. | All mobs are dead. |
| level9 | Gate `g3n` opens. Objective: "leave". | The player is in the `exit` zone. |
| finish | — | Victory (`DifficultySystem`). |

Other rules:
- **Mob weapon drops.** `LevelConfig.AllowedMobWeaponDrops = [Rifle, SMG]`. Other weapons in mob loot tables roll as "no loot". The Shotgun appears only as scripted loot on P2.
- **Start weapon.** The player starts with the Pistol, the default start weapon.
- **Spawn points.** Each point only has presets for its own platform's stages. On P1 and P3, zombies come out of the open wall pipes. On P2, they climb out of the sewer hatches. The P2 cooldown is 5.5 s per hatch.
- **Spawn distance.** `LevelConfig.SpawnDistance` = 7…45 m. The default window is based on sector size and is too coarse for an arena.
- **Ammo fallback**, the same mechanism as the endless level: `AdditionalLoot` with the condition `LowAmmoNoneAround` (`NoAmmoAroundCondition`, `_sameCaliberOnly = true`, `_lowAmmoThreshold = 3`). When the current weapon's magazine + reserve is 3 or less and no ammo of that caliber lies on the map, 10 rounds of the current caliber drop at an `AdditionalSpawn`-tagged point. There are 2 points per platform (`AmmoFallbackPoints`). `AdditionalLootSpawnSystem` picks a free point inside the player's zone.

## Mission layer (reusable)

| Piece | File | Role |
|---|---|---|
| `DifficultyStage.SpawnOnlyWhileTimer` | `LevelConfig.cs` | Wave mode. Spawning happens only while the stage timer runs (`IsSpawning`). After that, the stage waits for its `EndConditions`. Used by `SpawnPointSystem` and `GroupSpawnSystem`. |
| `DifficultyStage.Objective` | `LevelConfig.cs` | Text for the objective banner. |
| `LevelConfig._spawnDistance` / `_allowedMobWeaponDrops` | `LevelConfig.cs` | Spawn ring override, and the mob weapon-drop whitelist (filtered in `LootSystem`). |
| `LevelRoot._playerStart` | `LevelRoot.cs` | Player start pose. The camera keeps its offset (`EntryPoint.PlacePlayerAtLevelStart`). |
| `LevelGate` | `Mission/LevelGate.cs` | Door with a carving `NavMeshObstacle`. While closed, it blocks both the player and mobs. Opening modes: `OnStage` (at the start of a stage), `WithKey` (the player is within `KeyOpenRadius` and holds `RequiredKey`), or `Manual`. When it opens, the panel sinks into the floor and the obstacle switches off. **Re-closing:** `CloseWhenPlayerInZone` (+ `CloseMinDistance`, `CloseMessage`). Once the player is in that zone and far enough from the gate, `Close()` turns the obstacle back on at once and raises the panel. The gate stays shut for good (`IsSealed`; it never reopens). The prefab root has a `NavMeshModifier` with Ignore From Build. A red lamp is shown on key gates. |
| `BulletBlocker` | `BulletBlocker.cs` | Box (local space; the defaults fit a scaled unit cube) that stops bullets of both teams. It needs no collider or layer. `BulletOverlapSystem` tests each bullet's per-frame path segment against every active blocker. On a hit, the bullet is disposed at the hit point, and the optional `ImpactEffectId` effect plays there. A static registry is kept by `OnEnable`/`OnDisable`, so a hidden gate panel stops blocking. |
| `LevelKeyPickup` | `Mission/LevelKeyPickup.cs` | Starts inactive. Appears on `AppearOnStage`, then spins and bobs. Picked up by proximity, which adds its id to `MissionComponent.CollectedKeys`. |
| `LevelZone` | `Mission/LevelZone.cs` | Named XZ box. Used by `PlayerInZoneCondition` and by gate re-closing, and makes the ammo fallback platform-aware. |
| `StageLoot` | `Mission/StageLoot.cs` | Loot spawned once at the start of a stage (`RequestLootSpawn`, Source = MapLoot, so it never despawns). |
| `MissionComponent` | `Components.cs` | Singleton holding the lists above plus `CollectedKeys` and `OpenedGates`. `OpenedGates` keeps a gate even after it re-closes. Filled by `EntryPoint.RegisterMissionObjects`, including inactive objects. It isn't created if the level has no mission objects. |
| `MissionSystem` | `ECS/MissionSystem.cs` | Runs after `LevelEventSystem`. Reacts to stage changes (opens gates, shows keys, spawns loot, sets the objective). Every frame it handles key pickup, key gates and gate re-closing. In wave mode, once spawning stops, it adds "Осталось: N" to the banner. `MissionSystem.IsInZone` is a public helper. |
| `ObjectiveBanner` | `UI/ObjectiveBanner.cs` | Runtime top banner (UniText). The font is borrowed from the scene. Canvas `sortingOrder` is 450. |
| `MobsClearedCondition` / `GateOpenedCondition` / `PlayerInZoneCondition` | `SmartConditions/*ConditionWrapper.cs` | Stage end conditions. `MobsCleared` counts alive mobs plus pending spawn requests (`_maxAlive`, default 0). `PlayerInZone` latches once the zone is reached. |
| `NoAmmoAroundCondition` options | `SmartConditions/NoAmmoAroundConditionWrapper.cs` | `_sameCaliberOnly` makes only ammo loot of the current caliber (or `None`) count as "ammo around". With `_lowAmmoThreshold` > 0, the condition triggers on magazine + reserve ≤ threshold. The defaults keep the endless level's original rule. |
| Zone-aware fallback points | `ECS/AdditionalLootSpawnSystem.cs` | If a `MissionComponent` with zones exists and the player stands in a zone that holds `AdditionalSpawn` points, only that zone's free points are used. If none of them is free, the system waits. Outside such zones, it uses the free point nearest the player. Levels without mission zones keep the old "first free point" rule. |

## Props

- **SewerHatch.** An iron lid with a grate, a rim, and the Toxic zombie's green fog (`FogCalmGrey` from `ToxicMob.prefab`, copied with its overrides). It is visual only: it does no damage. `NavMeshModifier` Ignore From Build keeps it out of the bake.
- **KeyPickup.** A big flat golden key that reads well from the top-down camera, plus a floor glow and a point light.
- **LevelGate.** A 5 m steel panel with hazard stripes and a carving obstacle measuring 5.4 × 1.4. The door carries a `BulletBlocker`.
- **Crates on the level.** These are `Crate.prefab` instances with an added `NavMeshModifier` set to Ignore From Build. Without it, a crate placed before the bake would leave a permanent hole in the navmesh after it breaks.

## Not verified / limits

- Not run in Play Mode yet.
- Wave sizes come from paper math and need tuning after a playtest. Spawn cooldowns are in the builder (`p1Waves` / `p2Waves` / `p3Waves`). The fallback ammo amount is `FallbackAmmoCount` (10).
- Grenades and explosions ignore walls.
- If a mob stands in a doorway when its gate re-closes, the carving obstacle can push it off the navmesh. In this script the gates close only after the waves of that platform are cleared.
