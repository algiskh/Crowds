# Street vehicles for Infinite Scroll (Level_01) — Reference

Parked and abandoned vehicles that dress the endless suburb (`Docs/SuburbFeature.md`) and work as **solid, indestructible obstacles**. They are primitive-style models after the pictures in `Assets/REFs`: `pickup1/2`, `sedan1/2` and `icecream_truck1-3`.

## Building

Run **Tools ▸ Crowds ▸ Build Street Vehicles (Infinite Scroll)** (`Editor/Levels/VehicleBuilder.cs`) **after** *Build Suburb*, because the driveway spots are read from the ranch houses. Re-running it is safe: meshes and prefabs are rewritten in place (same guids), and the material colours are enforced. The builder:

1. creates the materials in `Assets/_Materials/Vehicles/` (`Veh_*`);
2. builds the prefabs in `Assets/_Prefabs/Vehicles/` (the baked meshes go to `Meshes/`);
3. places the vehicles in `Level_01`. Every `FloorSector` gets a `Vehicles` child (re-created on every run), so the cars ride along when the sector recycles. It also **removes the untextured ProBuilder placeholder walls** (`Wall1`, `Wall1 (1)`).

## Prefabs

Each model is **one baked mesh** with one submesh per material. Colour variants share that mesh and only swap materials.

| Prefab | Model | Notes |
|---|---|---|
| `Pickup_Teal`, `Pickup_Red` | `Pickup` | Regular-cab fleetside, about 4.95 × 1.9 m, short hood (nose 1.5 m ahead of the windshield, front axle at z 1.36; pivot is not centred, footprint centre z −0.3). Two-tone: paint on top, a lower band below (teal + silver like `pickup1`, red + charcoal). Black grille in a chrome frame, stacked headlights, chrome bumpers, open bed with a dark liner. |
| `Sedan_Red`, `Sedan_Silver`, `Sedan_Navy` | `Sedan` | Compact 4-door, about 4.5 × 1.75 m. Raked greenhouse, trunk spoiler (`sedan2`), body-coloured mirrors. |
| `Truck_IceCream` | `Truck_IceCream` | Short step van, about 5.4 × 2.2 × 2.8 m. White body fading to blue in 3 bands (`icecream_truck1/2`). Serving window with an insect screen and counter on the **right (+X)** side (`icecream_truck3`), pink/white striped awning, menu tiles, and a giant waffle cone with two scoops on the roof, readable from the top-down camera. |
| `Truck_Mail` | `Truck_Mail` | The same step van, all white with black bumpers. Blue band over a thin red stripe on the sides and the back, plus a plain blue panel where the eagle would be. No logos or lettering. |

Model local frame: the front faces +Z, ground at y = 0.

### What every vehicle carries

| Component | Effect |
|---|---|
| `NavMeshObstacle` (box, **carving**, `carveOnlyStationary = false`) | Cuts a hole in the navmesh. The player (whose `NavMeshAgent` keeps it on the navmesh) and the mobs (`NavMesh.CalculatePath`) **can't pass**. The hole re-carves straight after the sector leap-frogs. |
| `BulletBlocker` (body box, `wall_spark`) | Bullets of both teams stop on the vehicle with sparks. |
| `NavMeshModifier.ignoreFromBuild` | The body isn't baked, so the roof never becomes walkable navmesh. The obstacle does the cutting. |
| *(no `Breakable`)* | Can't be damaged or destroyed. |

## Placement in Level_01

Sectors are sorted by Z (0 = z −50 / `Row_A`, 1 = z 0 / `Row_B` / player start, 2 = z +50 / `Row_C`). Coordinates are sector-local. The road spans x −11…3, and the houses are on the −X side.

| Sector | Vehicle | Where |
|---|---|---|
| 0 | `Truck_Mail` | Road, west lane (−7.3, 12), heading −Z |
| 0 | `Pickup_Teal` | Ranch driveway (x −23.4, z from the garage), nose to the garage |
| 1 | `Truck_IceCream` | Road, east lane (−0.6, −17), heading +Z, so the serving window faces the camera. It stands where the bare `Wall1` was. |
| 1 | `Sedan_Red` | Kerb on the houses' side (−9.95, −20.5) |
| 2 | `Truck_Mail` | Road, east lane (−0.7, −12), heading +Z |
| 2 | `Sedan_Silver` | Ranch driveway |
| 2 | `Pickup_Red` | Kerb on the houses' side (−9.95, −2) |

That is 3 vans on the road and 4 cars parked by the houses. `Sedan_Navy` is built but not placed; it's spare for other levels.

The spots keep clear of the sectors' spawn points, loot points, breakable spawns and the pre-placed loot. On every run the builder checks this again and logs a warning if any of those points is less than 2 m from a vehicle footprint. The `Placements` table at the top of `VehicleBuilder` sets the layout.

## Limits / not verified

- Not run in Play Mode yet. Check:
  - the look from the game camera, and that the cone and awning read well;
  - that the player and mobs are blocked and path around the vehicles, including just after a sector recycles;
  - that bullets spark on the bodies.
- Loot never lands inside a vehicle. `LootSystem.ResolveSpawnPosition` moves a drop point that falls in a navmesh hole to the nearest walkable spot (`Docs/LootFeature.md`).
- Vans are 2.8 m tall (3.8 m to the top of the cone), so they can briefly hide mobs on their −X side from the fixed camera.
