# Finite suburb levels: "Тупик Мейпл-Корт" and "Задние дворы"

These are two finite campaign levels in the same liminal American-suburb style as Infinite Scroll, without the
recycling platforms. Build them with **Tools ▸ Crowds ▸ Build Suburb Levels (Cul-de-sac + Backyards)**
(`Editor/Levels/SuburbLevelsBuilder.cs`). Re-running is safe: prefabs and meshes are rewritten in place.

**Run order.** *Build Street Vehicles* must have run at least once, because the levels park its cars. The builder
rebuilds the suburb house kit itself: `SuburbBuilder.BuildKit()` rebuilds the fence, hedge and houses (same guids,
so `Level_01` gets the changes too). It does not touch `Level_01`.

## Difficulty parity with Infinite Scroll

| What | How |
|---|---|
| Stages | `_Data/Levels/Configs/Level_CulDeSac` / `Level_Backyards` are **copies of `Level1`**: same stages, the `Kill10` gate on stage 1, ammo fallback, `speedup` ban. The builder copies the asset only if it is missing, then enforces `SectorMode.Sliding`, radius 1, and `_spawnDistance` (10, 45). Later balancing in these copies survives re-runs. |
| Mobs | The same spawn-point prefabs as `Level_01`: `RegularSpawnPoint` (14 per level), `GroupSlowSpawnPoint` (1), and `ToxicPoint` on sewer hatches (4 / 3), so toxic zombies only crawl out of the sewers. |
| Loot | 3 clones of `Level_01`'s `LootPoint1 (1)` (map loot + ammo fallback, tag `AdditionalSpawn`), a clone of `BreakableSpawn1` (crate on stage 1), and pre-placed `MapLoot`: Shotgun, SpeedUp, Shield, 3 Grenades. |
| Menu | `LevelDefinition`s `Level_CulDeSac` / `Level_Backyards`, **appended** to `CampaignLevelLibrary`; then `CampaignSetup.Apply()` puts the tutorial first and assigns the drawn logos (`Assets/Sprites/LevelIcons`, see `Docs/TutorialLevelFeature.md`). |

## Layouts

Floor top is y = −0.5. Each level has one `FloorSector`: a single floor mesh, lawn-coloured, which receives decals. The street dressing on top is a baked `<Level>_Ground` prefab, visual only and ignored by the bake. The camera looks −X, so −X is the top of the screen.

**Level_04_CulDeSac** (floor x −36…34, z −50…56):

- **Street.** A 9 m street with sidewalks, curbs, a dashed line and joints runs along Z. It ends in a turnaround: asphalt R 13, sidewalk ring R 14.5, and a curb ring that is open toward the street. In the middle is a raised grass island R 4 with a tree.
- **Road closed.** At the left screen edge (z −50) two `Barricade`s with gaps close the street. Mobs spawn behind them, and the street runs on past them outside the fence.
- **Houses** stand on the floor, so they carve the navmesh (`NavMeshObstacle` = mesh bounds) and stop bullets:
  - far side: Ranch, Colonial and Cape, fronts at x −17;
  - camera side: Colonial, Ranch, and an empty lot with a `ForSaleSign`;
  - around the bulb: 3 houses facing its centre, fronts at R 21.
- Each house has a front walk, a mailbox, and a driveway with bins.
- **Cars:** 3 on driveways, 1 at the kerb, and the ice-cream van in the bulb.
- **Yards:** 2 pools (slow you down), 2 sheds, 2 trampolines, swings, grills, picnic tables, hedges on the lot lines, and trees.
- **Edge:** a privacy fence runs all round.

**Level_05_Backyards** (floor x −27…27, z −46…46):

- **Houses.** Two rows sit just off the floor with their **backs** to the yards: the back stoop touches the floor edge. The rows are W at the top of the screen and E on the camera side. Their fronts face streets drawn off the floor. Privacy fences at x ±26.8 close the gaps between the houses.
- **Yards.** 6 yards (3 per side). Privacy dividers at z ±15 each have a 3 m gate at |x| 13…16. Picket fences at x ±3.2 have a 3 m gate in the middle of each yard and open onto a **gravel service alley** (x −3…3).
- **Alley.** Barricades stand at both ends, with mob spawns behind them. It also has trash cans, lamps, a pickup and 3 sewer hatches.
- **Yard props:** 2 pools, 2 sheds, 2 trampolines, 2 swing sets, grills, picnic tables, hedges and trees.

The builder checks every spawn, loot and start point. It logs a warning when a point is off the floor or within 0.7 m of any `NavMeshObstacle`.

## New pieces

| Prefab (`_Prefabs/Suburb`) | Notes |
|---|---|
| `Prop_PrivacyFence` | A 2.4 m segment of vertical boards, 1.85 m tall. Runs are built by `FenceRun`: segments are stretched to fit, and the run root carries one carving obstacle + `BulletBlocker` (`wall_spark`). Picket runs (`Fence_Picket`) get an obstacle only, so you can shoot over them. |
| `Breakables/PrivacyFenceBroken` | **Breakable privacy-fence section** (Backyards). The same 2.4 m frame as `Prop_PrivacyFence`, but it looks damaged so the player can tell it apart: 3 boards missing, one knocked sideways, one hanging off a rail into the gap, one snapped in half, one leaning out of the line (visible from above), the cap rail broken over the gap, two boards on the grass. Config `_Data/Breakables/PrivacyFence` (id `PrivacyFence`): 80 HP, bullets / explosions / melee, `box_break` splinters, no loot, `Debris` (posts, stumps, boards on the ground; no collider, the carving is removed → the gap is walkable). Built by `BuildBreakableFence`. In a run, `FenceRun(breakAt: …)` puts it in place of a segment: it has its own collider + carving obstacle, and the solid stretches either side get a `Solid_i-j` child with obstacle + `BulletBlocker` (no blocker over the breakable, so bullets reach it). Backyards: one per yard-divider line (4), the middle segment of the house half or of the alley half, alternating — a shortcut between yards. |
| `Prop_Pool` | 9 × 4.5 m water with coping, deck, ladder, diving board and loungers. It is walk-through: a `ShallowWater` ModifierZone (box) gives a `SpeedModifier` 0.5 (`SpeedWater`) to the player and mobs. Waders also **sink visually** (see below). |
| `Prop_Shed` | Red, with a gable roof and double doors. Solid: carves the navmesh + `BulletBlocker`. |
| `Prop_Tree` | Boxy canopy. The trunk carves (capsule obstacle). |
| `Prop_Trampoline` | Carves (capsule R 2.05). You can shoot over it. |
| `Prop_PicnicTable`, `Prop_Barricade` | Carve. You can shoot over them. |
| `Prop_Mailbox`, `Prop_StreetLamp` (emissive head), `Prop_SwingSet`, `Prop_Grill`, `Prop_TrashCans`, `Prop_ForSaleSign` | Decor. Mobs walk through them. |

Materials are `_Materials/Suburb/Suburb_*`, on Lit See-Through.

**Biohazard response site (Backyards, yard W3 + the alley).** It replaces the W3 picnic table and grill.
- **`Prop_HazmatTent`.** A yellow decontamination tent, 4.2 × 3.2 m, with an open front, rolled flaps, biohazard emblems on the roof, a table inside and a shower arch with a drip tray. It carves the navmesh; bullets pass through.
- **`Prop_HazmatBags`.** Piles of yellow waste bags after `REFs/toxic_plastic_bag.jpg` (procedural lathe mesh `BuildBagMesh`: squarish sagging body, folds into a twisted neck, knot + flared tuft; CAUTION/BIOHAZARD label = emblem + black text bars on one side; one bag lies on top label-up), plus a hazard drum with an emblem. You can walk through them, but they **stink**: a `SewerHatch` zone (R 1.7, 1 HP / 0.5 s, player only) and the Toxic zombie's green fog. There are 3 piles in the yard and 1 in the alley.
- **`Vehicles/Truck_Hazmat`.** A prefab **variant of `Truck_Mail`**: hazmat yellow with black and orange bands, and biohazard emblems on both sides and the roof. It is solid and stops bullets, like every van. It is parked in the alley at z 24, so *Build Street Vehicles* must have run first.
- **Emblem.** `Assets/Textures/Hazmat/Biohazard.png`, drawn by a script: a black symbol on a yellow disc with a black rim. Material `Suburb_Biohazard` is Lit See-Through with alpha clip.
- **Spawns.** 3 `SpawnPoint`s with **FastToxic** only, two by the tent and one behind the truck. Cooldowns are per Level1-copy stage: 14 s at stage 3, down to 8 s at stage 8.

**Wading (`ECS/WadeSystem.cs`).** Anyone standing in a `ShallowWater` pool sinks visually, which shows why they move slower:
- Depth: `ShallowWater.SinkDepth` (0.6 m, about 1/3 of the player's height) in the middle. The sink ramps in over `_sinkRamp` (0.9 m) from the edges, like steps. It eases at 2.5 m/s.
- What moves: only the visual. For skinned models, the player's `Body` and the mob model with the `SimpleAnimator` get a lower local Y. For VAT crowds, `CrowdRenderSystem` lowers the draw matrix by `WadeComponent.Depth`.
- What stays: the root, agent, colliders and muzzle stay on the floor.
- How it looks: the opaque water surface hides the sunk part.
- Pooled mobs that died in a pool are put back up when reused.
- On levels without pools the system does nothing.

**Ground layer heights.** Overlapping dressing layers are at least 1 cm apart:
- the order is stripes 0.01 < front paths 0.02 < sidewalk 0.03 < road 0.04 / joints < marks / drives 0.05 < curbs 0.065 < island 0.075;
- inside the pool, the deep-water slab is 1 cm above the shallow water.

Closer surfaces z-fought at the camera distance, and the blood/scorch decals projected on both of them flickered. That was the "Тупик" decal flicker.

All houses now have a **back wall**: two windows per floor plus a back door with a stoop (`SuburbBuilder.BackWall`).

## Not verified yet

- Neither level has been run in Play Mode yet. Check:
  - the navmesh: carved houses and fences, no mobs stuck at spawns;
  - the camera-side houses with the see-through hole;
  - that the stage pacing feels like Infinite Scroll on a closed map. The spawn window is 10…45 m.
- The house textures (scale/tints on screen) and the breakable fence sections (readable as damaged, break by shots/melee/grenade, the gap becomes walkable for mobs) have not been checked in the Editor yet.
