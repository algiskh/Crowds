# Suburb dressing for Infinite Scroll (Level_01) — Reference

The Infinite Scroll level (`Level_01`, `LevelConfig.SectorMode = Recycling`) looks like an endless **liminal American suburb**. The player seems to run down one long street that never ends:

- road, narrow sidewalks, green lawns;
- one side closed off by a row of similar middle-class houses with white picket fences between them.

The walkable platforms are unchanged. They keep the same `FloorSector` slabs, area, colliders and navmesh, and nothing is placed **on** them except flat surface dressing.

## Building

Run **Tools ▸ Crowds ▸ Build Suburb (Infinite Scroll)** (`Editor/Levels/SuburbBuilder.cs`). Re-running it is safe: meshes and prefabs are rewritten in place (same guids), and materials are created once and their colours enforced. The builder:

1. creates the materials in `Assets/_Materials/Suburb/`;
2. builds the prefabs in `Assets/_Prefabs/Suburb/` (the baked meshes go to `Meshes/`);
3. dresses `Level_01`: every `FloorSector` instance gets a child `Suburb` = `Street_Section` + one of `Row_A/B/C`, assigned in order along Z. The dressing is a child of the sector, so it rides along when `CheckSectorSystem` leap-frogs the sector (`Docs/SectorFeature.md`).

Each prefab is **one baked mesh** with one submesh per material, built from primitives in code and then combined. That keeps it to a few draw calls per object. Static batching isn't possible because the sectors move.

## Cross-section of a sector

The sector slab is x −25.5…24, z −25.5…24.5, top y = −0.5. The street runs along **Z**, the scroll direction. The fixed camera looks −X, so the −X side (houses) is the top of the screen and its facades face the camera. The +X side (camera side) has a second, mirrored row of the same houses facing the street. They sit off the platform, behind and below the camera, so they only cover the action when the player is on the near lawn. There the `Crowds/Lit See-Through` shader cuts a soft hole around the player (`Docs/SeeThroughFeature.md`).

| x | Part | Notes |
|---|---|---|
| −48 … −25.5 | Yard ground (off-platform) | Lawn under the houses. It also hides the slab's west face. No navmesh. |
| −37.5 … −28.5 | **Houses** | Fronts at x = −28.5. Porches and steps end at ≈ −25.9, so nothing is on the platform. |
| −31 | **White picket fences** | Close every gap between houses and run to the sector ends, where they meet the neighbouring sector's fence. The drop behind is never visible. |
| −25.5 … −14 | Front lawns | Mowing stripes, 2.5 m bands. Front walks and driveways cross them. |
| −14 … −12.5 | Sidewalk | 1.5 m, expansion joints every 1.5625 m (32 per sector, so they tile). |
| −12.5 … −11 | Grass strip | |
| −11 … 3 | Road | Asphalt, curbs 0.25 m, dashed yellow centre line (3 m dash / 10 m period, tiles). |
| 3 … 4.5 | Grass strip | |
| 4.5 … 6 | Sidewalk | |
| 6 … 24 | Front lawns (camera side) | Front walks to the sidewalk (x 6), ranch driveways to the curb (x 3). |
| 24 … 46 | Yard ground (off-platform) | Lawn under the camera-side houses (it replaced the old soil face). |
| 24.45 … 27.5 | Porches/steps (camera side) | House fronts are at x = 27.5, facing −X (yaw −90). |
| 30 | **White picket fences** (camera side) | Mirrored: segments are yaw 90 and the rails face the street. |

Surfaces sit 0.6–6 cm above the slab top and are purely visual:

- `NavMeshModifier.ignoreFromBuild` on all dressing, so the navmesh stays on the slab;
- no colliders;
- `renderingLayerMask` = 1|4 (`KeyLevelBuilder.FloorRenderingLayers`), so blood decals still land on them. Decal projectors only draw on layer 4. The `FloorSector` prefab itself has 1, and `Level_01` overrides it to 5 per instance, so the mask is not read from the prefab.

Nothing on the street casts shadows.

## Prefabs

| Prefab | Content |
|---|---|
| `Fence_Picket` | 2 m segment: 2 posts, 2 rails, 9 pickets with pointed tops. Rows stretch segments slightly to fit a gap exactly. |
| `House_Ranch` | 1-story, 10 × 9 m. Gable roof, attached garage (garage door with panels) on its −X side, porch with 3 columns, picture window, shuttered window, chimney. |
| `House_Colonial` | 2-story, 10 × 8 m. Full-width porch with 4 columns, door in the middle, 2 + 3 shuttered windows, band board, exterior side chimney. |
| `House_Cape` | 1.5-story, 9.5 × 8 m. Steep roof with 2 dormers, small porch, shuttered windows, chimney. |
| `Street_Section` | One 50 m sector of street: everything in the table above, apart from houses, fences, walks and driveways. |
| `Hedge` | Boxy clipped hedge, 6 × 1.2 × 1.15 m. Dark body, lighter top, a few lumps on the faces. It has **no collider**: a `Hedge` ModifierZone (box footprint) gives a `SpeedModifier` 0.55 (`SpeedHedge`) to the player and mobs pushing through, like an oil slick. `NavMeshModifier.ignoreFromBuild`, so mobs path straight through it. |
| `Row_A_East`, `Row_B_East`, `Row_C_East` | The camera-side rows: the same prefabs in other orders and colours, mirrored (`BuildRow(east: true)`). They are instanced as `Suburb/Houses_East`. `VehicleBuilder` only takes driveways from `Suburb/Houses`. |
| `Row_A`, `Row_B`, `Row_C` | 3 lots of 16.7 m: the 3 house types in a different order and colours per row, fences, plus `Row_X_Ground` (front walks to the sidewalk, ranch driveway to the road). |

House local frame: the front faces +Z, the front wall is at z = 0, ground at y = 0. In a row the house is turned 90° so it faces the street (+X). Houses also carry:

- a `BulletBlocker` (mesh bounds, `wall_spark`), so bullets that fly off the platform hit the houses;
- `NavMeshModifier.ignoreFromBuild`.

**Colours.** Houses are built with base materials: siding `Suburb_Siding_White`, roof `Suburb_Roof_Charcoal`, door/shutters `Suburb_Door_Black`. Rows swap them per house as an instance override (`SuburbBuilder.Recolor`), using these palettes:

| Part | Options |
|---|---|
| Siding | Cream, SkyBlue, Sage, Beige, Grey, White, Yellow |
| Roof | Charcoal, Brown, Slate |
| Door + shutters | Red, Navy, Green, Black |

**Textures.** Walls, roofs and foundations are textured (`SuburbBuilder.TextureHouses`), same scheme as the mall (`LevelTextures`, `Docs/ArenaLevelsFeature.md`):
- 4 small greyscale tiles in `Assets/Textures`, all 256×256, seamless on both axes, low detail: `Siding_Lap` (clapboard, 8 boards per tile), `Siding_Panel` (wide panels, 2 per tile), `Stucco`, `Roof_Shingles` (8 rows per tile). Raw photos are in `ArtSource/Textures` (`suburban_wall1..3.jpg`, `suburb_roof1.jpg`, outside Assets). Processing: greyscale, crop a whole number of board/row periods, flatten the lighting, resize to 256, wrap-blend the overlap so both edges meet.
- Importer (`LevelTextures.Load`): max 256, mipmaps, block compression, repeat, aniso 1. One texture per look, the colour comes from the tint (`LevelTextures.TintFor`: wanted average colour / texture mean), so every material keeps its old average colour.
- Which: lap — White, Cream, Sage, Yellow; panels — SkyBlue, Grey; stucco — Beige and `Suburb_Foundation`; shingles — all 3 roofs (also the shed roof).
- **UVs.** Unity cube UVs are 0..1 per face, so a stretched box would stretch the texture. `Bake` gives the submeshes of these materials (`SuburbBuilder.MetricUV`) **metric planar UVs** (`PlanarUVs`): per face, U horizontal along it, V up it (on a roof slope, up the slope) — boards stay level and the scale is the same on every box. Material tiling = 1 / metres per tile: lap 2 m (0.25 m boards), panels 1.2 m, stucco 1.5 m, shingles 2 m.
- The house prefabs are shared, so Infinite Scroll gets the textures too (on whichever builder runs first: *Build Suburb* or *Build Suburb Levels*).

The three rows differ slightly ("almost the same houses, a bit different by platform"). With 3 recycled sectors the street repeats every 150 m, which suits the endless, déjà-vu feel.

Houses also have a back wall (2 windows per floor, back door with a stoop, `BackWall`). `SuburbBuilder.BuildKit()` rebuilds the materials, fence, hedge and houses for other builders (`Docs/SuburbLevelsFeature.md`) without touching `Level_01`.

## Hedges

`SuburbBuilder.PlaceHedges` puts at most 2 hedges per sector, 1 per side, under `Suburb/Hedges`:

- each one stands on the front lawn along a property line between two lots, perpendicular to the street, from 0.3 m off the slab edge toward the sidewalk;
- the sides alternate between the two lines per sector;
- a line closer than 1.5 m to a spawn/loot/breakable point or a vehicle is skipped, and the other line is tried (both are logged).

Walks sit mid-lot and driveways sit at the garages, so they don't cross the lines.

## Limits / not verified

- The camera-side houses and hedges have not been run in Play Mode yet. Check:
  - the near lawn with the see-through hole;
  - that the camera clears the colonial chimneys (camera y ≈ 10, chimney top ≈ 9);
  - the hedge slow-down.

- Not run in Play Mode yet. Check:
  - that the houses are visible at the top of the screen and look right from the camera angle;
  - that the fence lines meet across sector boundaries while sectors recycle;
  - that decals show on the street;
  - frame time: 9 houses + ~25 fence segments + 3 streets + 3 grounds ≈ 40 renderers, a few submeshes each.
- Driveways only for the ranch, which is the one with a garage. No mailboxes or trees: the empty, unpeopled look is part of the liminal mood.
- Vehicles (parked cars on driveways/kerb, abandoned vans on the road) are a separate pass: `Docs/VehiclesFeature.md` (*Build Street Vehicles*, run after this builder). It also removed `Level_01`'s old untextured `Wall1` / `Wall1 (1)` ProBuilder walls.
- Sewer hatches on the road and sidewalks (a light poison zone, and the only toxic-zombie spawns) are a third pass: `Docs/SewerHatchFeature.md` (*Build Sewer Hatches*, run after the vehicles).
- Spawn note: the one `RegularSpawnPoint` in `Level_01` with its own overrides (x 14.83, z −18.99) was reset to the shared rules (16.11.2026).
