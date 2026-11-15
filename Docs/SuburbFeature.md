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

The sector slab is x −25.5…24, z −25.5…24.5, top y = −0.5. The street runs along **Z**, the scroll direction. The fixed camera looks −X, so the −X side (houses) is the top of the screen and its facades face the camera. The +X side (camera) is left open as a drop-off, so houses there would not cover the action.

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
| 6 … 24 | Lawn | The "other side" of the street, whose houses dropped into the void. Intentionally liminal. |
| 24 | Soil face | Covers the slab edge on the camera side. |

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

The three rows differ slightly ("almost the same houses, a bit different by platform"). With 3 recycled sectors the street repeats every 150 m, which suits the endless, déjà-vu feel.

## Limits / not verified

- Not run in Play Mode yet. Check:
  - that the houses are visible at the top of the screen and look right from the camera angle;
  - that the fence lines meet across sector boundaries while sectors recycle;
  - that decals show on the street;
  - frame time: 9 houses + ~25 fence segments + 3 streets + 3 grounds ≈ 40 renderers, a few submeshes each.
- Driveways only for the ranch, which is the one with a garage. No cars, mailboxes or trees: the platforms must stay free of objects, and the empty, unpeopled look is part of the liminal mood.
- `Level_01`'s pre-existing `Wall1` / `Wall1 (1)` ProBuilder walls are untouched.
- Spawn note: the one `RegularSpawnPoint` in `Level_01` with its own overrides (x 14.83, z −18.99) was reset to the shared rules (16.11.2026).
