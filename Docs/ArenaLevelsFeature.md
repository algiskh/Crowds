# Arena levels: "Бейсбольное поле" and "Склад-магазин"

Two campaign levels sit between the Key level and the Ice arena. Both are closed arenas with **quota waves** (`DifficultyStage.MobQuota` + `MobsCleared`), the same mechanism Hockey uses. Breathers in between throw loot onto the floor.

| Level | Builder (menu) | Prefab / config / definition | Campaign slot |
|---|---|---|---|
| Бейсбольное поле | **Tools ▸ Crowds ▸ Build Baseball Level** (`BaseballLevelBuilder`) | `Level_06_Baseball` / `Level_Baseball` / `Level_Baseball` | after the Key level |
| Склад-магазин | **Tools ▸ Crowds ▸ Build Mall Level** (`MallLevelBuilder`) | `Level_07_Mall` / `Level_Mall` / `Level_Mall` | after Baseball, before Hockey |

Both builders are safe to re-run: assets are rewritten in place and keep their guids. Each one ends with `CampaignSetup.Apply()`, which sets the campaign order and assigns the logos `Sprites/LevelIcons/Level_Baseball.png` and `Level_Mall.png`. See `Docs/TutorialLevelFeature.md`.

**Prerequisite.** The MobOnly navmesh area must exist. **Build Hockey Level** creates it; until then the kit falls back to area index 3 and logs a warning.

## Shared kit: `Editor/Levels/VenueKit.cs`

- **Stages.** `Quota` (rest window + quota + `MobsCleared`), `Breather` (timer shown, no spawns) and `Finish`.
- **`Config`.** Writes the stages, the ammo fallback (`LowAmmoNoneAround`, 10 rounds), `Sliding` with one sector, the spawn window, and the mob drop filters.
- **`Begin` / `Finish`.** `Begin` creates the root, the floor slab (decal layers) and the standard children. `Finish` adds the player start, wires the `LevelRoot` and saves the prefab. `Definition` writes the `LevelDefinition`.
- **`WallSeg` / `WallWithGaps`.** Walls at any angle (a rotated cube with a unit-box `BulletBlocker` and sparks), optionally with door gaps.
- **`MobDoor`.** A dark vestibule outside a wall gap. Its floor is on the **MobOnly** navmesh area, so mobs walk out but the player can't walk in (the same rule as the Hockey tunnels). It has black sides, a back wall (which stops bullets) and a ceiling, plus a portal frame on the inner face. It returns the spawn position deep inside.
- **`Solid`.** An explicit carving `NavMeshObstacle` box, with an optional `BulletBlocker`, on a prop excluded from the bake.
- **`GroundPainter`.** Flat dressing (convex polys, rects, lines, discs, pies, bands, rings) baked into **one mesh** with a submesh per material. It is visual only and receives decals. Overlapping layers are kept ≥ 1 cm apart, to avoid the z-fighting and decal flicker seen in "Тупик".
- **`Combine`.** Bakes thousands of primitives (the mall racks) into one mesh asset.
- **`Bleachers`.** Visual stepped stands.

## Бейсбольное поле (Level_06_Baseball)

Home plate is near the camera at (32, 0); the outfield runs up the screen (−X). The floor slab is x −28…39, z −46…46.

- **Walls (padded green, 2.4 m, yellow top).**
  - the backstop at x = 37;
  - two foul-side walls at 45°, each with a dugout gap;
  - short returns;
  - the **outfield arc** (R 58 from home, ±49°) in ≈6° chords, with 3 door gaps at 152°, 180° and 208°.
- **Spawns (5).** Both dugouts (4.2 m wide, with a roof slab and benches) and the three outfield doors. All of them are MobOnly vestibules.
- **Paint (`Level_06_Baseball_Paint`).**
  - mowing stripes and the warning track;
  - the infield dirt wedge, the grass diamond, base paths, base cut-outs, the home circle, the mound and the rubber;
  - chalk foul lines, batter's boxes, bases, home plate and on-deck circles.
- **Cover.** Two rolled tarps in foul territory, two equipment carts, a batting cage (carves, the net lets bullets through), and 4 crates.
- **Outside the walls (visual).** Bleachers along both foul sides, yellow foul poles, two light towers (emissive banks) and a scoreboard behind centre field.

**Script and balance.**

| Stage | What happens |
|---|---|
| level1 | Quota: 20 Slow + 10 Fast. |
| level2 | Breather, 12 s: **Shotgun** + 12ga, 9mm and health on the mound. |
| level3 | Quota: 30 Fast + 10 Slow + **4 Fatman**. |
| level4 | Breather: loot at 1st, 2nd and 3rd base (12ga, health, grenades, a shield). |
| level5 | Quota: 35 Fast + 15 Slow + 7 Fatman + 1 FatmanStrong. |

Other settings:
- Mob weapon drops are limited to Pistol, SMG and Shotgun. The `speedup` bonus is banned.
- The spawn window is 6…70 m.
- The player starts at (16, 0).

## Склад-магазин (Level_07_Mall)

The sales floor of a members-only warehouse store, x −36…36 by z −28…28.

- **Walls.** The panel walls are 3.2 m high. The **storefront** (+X, near the camera) is a low 1.2 m parapet, so it doesn't hide the checkouts.
- **Spawns (7).** 3 loading-dock doors in the back wall (z −18 / 0 / 18), 2 emergency exits in the side walls (x −10), and 2 store entrances in the storefront (z ±12).
- **Racks.**
  - **8 blocks of double-sided pallet racking**: rows at z ±6, −14.5 and −23, spans x −24…−7 and −3…14. The +14.5 and +23 rows (screen right) are now the frozen-food department.
  - The camera yaw is −123°, so it looks toward −X/−Z and screen right is roughly +Z.
  - They leave a 9 m main aisle down the middle and a cross aisle at x ≈ −5.
  - Each block has orange uprights, blue beams, wire decks, and pallets of cartons or shrink-wrapped loads on three levels. The variation per bay is deterministic.
  - All racks are baked into one mesh (`Level_07_Mall_Racks`). Each block has its own solid box (carves the navmesh, stops bullets).
- **Frozen-food department** (`Props/FrozenFoods`, `MallLevelBuilder.FrozenFoods`), broken and iced over, in place of the +14.5 and +23 rack rows (z ≈ 8…28). All case visuals are baked into one mesh (`Level_07_Mall_FrozenFoods`); the solid boxes are separate objects.
  - Upright glass-door cases against the +Z side wall, x −24…−12.6 and −7.4…14, with a gap for the N emergency exit (x −10). They are 2.2 m high and 1.6 m deep.
  - Chest-freezer islands (`Islands`): a back row at z 21.5 (x −22.5…−13.5 and −3…12) and a front row on the old rack line at z 14.5 (x −24…−8 and 0…9). They are 0.95 m high, so the camera sees over them, and the cross aisle at x ≈ −5 and its columns stay clear.
  - Everything is solid: it carves the navmesh and stops bullets.
  - Decor is picked by a deterministic hash. Most lights are dead (`Mall_FreezerDead`) and a few still glow. Some doors hang open, some lids are slid half open with frost mounds spilling out. There are frost crusts on the glass, frost runs down the sides, icicles under the headers, and ice chunks on the floor.
  - **Ice patch** (`IcePatch`) spreads over the whole department and sends tongues toward the main aisle. It is built from 17 overlapping `OilSlick` blobs, each with its own seed, a non-uniform scale and 0.35 irregularity.
    - **Zones.** Each blob is a `ModifierZone` with `TractionModifier` (`TractionIce`, grip 3.5, a bit less slick than the hockey rink's 3). They share one Id, so the effect doesn't stack. Players and mobs both slide. The blob's own renderer is off.
    - **Look.** The same blob outlines (`OilSlick.RadiusAt`, after `RebuildShape` via reflection) are painted into one `GroundPainter` mesh, `Level_07_Mall_Ice`. Matte `Mall_IceFrost` sits at +3 cm and glossy `Mall_IceGlaze` at +4 cm. UVs are world-planar, so where blobs of one layer overlap the pixels are identical: no z-fighting and no growing step. The patch is one renderer, ignored by the bake, and it receives decals.
- **Other solids.**
  - 14 steel columns with yellow guards;
  - 6 checkout counters (belt, register, candy rack, lane light);
  - freezer cases along the back wall (emissive glass doors);
  - 6 pallet stacks and a forklift. Each stack is **two breakable pallets** (see below);
  - 4 flatbed carts, which carve the navmesh but let bullets through.
- **Breakable pallets** (`BuildPalletBreakables`). There are two prefabs, `_Prefabs/Breakables/PalletBoxes` (cartons, 2×2 with 2 on top) and `PalletWrapped` (a blue shrink-wrapped load), and they alternate in the stacks.
  - They share one config, `_Data/Breakables/PalletBoxes`. It is a copy of `Crate` (same drop table) with its own id, 60 HP and 1 loot roll. Bullets, explosions and melee damage it, and a bullet that hits it is stopped.
  - On destruction a pallet leaves debris: the knocked-askew pallet and flattened scraps, with no collider and no obstacle, so the navmesh frees up.
  - Intact and debris visuals are each one baked mesh (one renderer each).
  - Effect `box_break` (`_Prefabs/Effects/BoxBreak`, registered in FxHolder): Epic Toon FX `GibletExplodeWood` (splinters + dust) at 0.7 scale, played at the `Burst` point 0.6 m up. ETFX's world collision (a physics query per particle) is switched to one analytic floor plane.
- **Textures** (`LevelTextures`, `Assets/Textures`; raw sources in `ArtSource/Textures`, outside Assets). All are low-detail power-of-two textures: the ice is 512, `Cardboard_Marked` is 512×256, and the rest are 256. `Apply` sets the importer (max size, mipmaps, compression, repeat, aniso 1).
  - Pallet wood (the slat gaps are darkened, opaque);
  - cardboard: with handling marks for `Mall_BoxKraft`, plain and darker for `Mall_BoxKraftDark`;
  - one grey shrink-film texture, tinted blue (`Mall_ShrinkWrapBlue`) or red (`Mall_BoxRed`);
  - ice on `Mall_IceFrost`/`IceGlaze`/`Frost` and on the hockey `Level_Ice`.
  - The tints keep the old average colours.
- **Paint.** Yellow safety lines along the racks and the main aisle, red and white keep-clear hatching at the docks, a dark checkout band, and entrance mats.

**Script and balance.**

| Stage | What happens |
|---|---|
| level1 | Quota: 25 Slow + 15 Fast. |
| level2 | Breather: **AR** + 5.56, 9mm and health in the main aisle. |
| level3 | Quota: 35 Fast + 10 Slow + 6 Fatman + 6 ToxicHelmet. |
| level4 | Breather: health, grenade and ammo by the checkouts; Shotgun, 12ga and Rage in the back aisle. |
| level5 | Quota: 40 Fast + 20 Slow + 8 Fatman + 2 FatmanStrong + 3 GunnerBare. |

Other settings:
- Mob weapon drops are limited to Pistol, SMG, AR and Shotgun. The `speedup` bonus is banned.
- The spawn window is 6…60 m.
- The player starts at (23, 0).

## Campaign order

`CampaignSetup.KnownOrder`, by difficulty: Tutorial → Infinite Scroll → Key → **Baseball** → **Mall** → Hockey → Cul-de-sac → Backyards. Known levels that are built are placed in this order, and any other library entries follow.

## Not verified yet

- the breakable pallets: the splinter burst size against the floor plane, the debris look, and the loot balance (12 pallets, 1 roll each from the Crate table);
- the frozen-food department in play mode: how the ice blobs read from the camera, the slide strength (3.5), and the navmesh between the islands, the wall cases and the N exit;

Neither level has been built in Unity or run in Play Mode yet. Check:
- the navmesh at the door gaps and vestibules, around the arc chords, and in the rack aisles;
- that the vestibule floors really get the MobOnly area;
- the wave balance against the Key level and Hockey;
- the camera view over the 3 m racks. The see-through shader should cut them near the player, because all the materials use Lit See-Through.
