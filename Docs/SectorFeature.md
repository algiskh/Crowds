# Sector System — Reference

The floor under the player is split into **sectors** (`FloorSector` MonoBehaviour, caches its
child `MeshFilter[]`). `CheckSectorSystem` keeps the right sectors active/positioned around the
player as they move along **Z**. There are two modes, chosen per-level in `LevelConfig.SectorMode`.
This doc is the source of truth so the code doesn't need re-exploring.

## Components / files

| Thing | Where |
|---|---|
| `CheckSectorSystem` | `ECS/CheckSectorSystem.cs` — first system in `EntryPoint.RegisterSystems()` |
| `NavMeshManager` | `_Scripts/NavMeshManager.cs` — owns the sectors, bakes the NavMesh |
| `FloorSector` | `_Scripts/FloorSector.cs` — caches `MeshFilters` for bounds tests |
| `SectorMode` enum | declared in `_Scripts/LevelConfig.cs` |
| `LevelConfig.SectorMode` / `ActiveSectorRadius` | per-level config (`BoxGroup("Sectors")`) |
| `NavMeshManagerComponent`, `CurrentLevelConfigComponent` | singletons read by the system |

`IsWithinXZBoundsFromMeshes` (in `Utils/Extensions.cs`) is the XZ bounds test used to decide which
sector a position is on (combines every mesh-filter's world AABB).

## Mode 1 — `Recycling` (default, infinite scroll)

Three sectors (`CurrentSector` / `LeftSector` / `RightSector`, serialized on `NavMeshManager`) are
recycled forever. When the player crosses a sector boundary (+ `MainHolder.SectorUpdateOffset`
hysteresis), the sector left behind is leap-frogged to the front:

```
player crosses currentZ ± (DistanceBetweenSectors/2 + offset)
   ▼  CheckSectorSystem.RunRecycling  (looped, max 8 shifts/frame)
player.SetSector(Right/Left)
MoveStaticObjects(...)  -> decals / loot / mobs sitting on the trailing sector are teleported
                          +3·DistanceBetweenSectors forward/back (matches the sector's jump)
NavMeshManager.UpdateSectorsPosition(...) -> ShiftSectorPositions + rotate refs + RebuildNavMesh
```

- `DistanceBetweenSectors` = distance between two adjacent sectors (cached in `Awake`).
- The `×3` displacement equals the trailing sector's actual jump (it moves from one end to one past
  the other end = 3 spacings), so objects ride along with their sector.
- The loop (`MaxShiftsPerFrame = 8`) catches up if the player moves several sectors in one frame
  (speed bonus / low FPS) — without it the player could fall off the active NavMesh.
- `MoveStaticObjects` skips disposed loot/decals (`DisposableComponent.IsDisposed`) and dead mobs
  (`HealthComponent.CurrentHealth <= 0`).

## Mode 2 — `Sliding` (finite, pre-placed level)

Sectors are **pre-placed in the level**, assigned in order along Z to `NavMeshManager._sectors`.
Nothing is recycled or moved — the system just enables a window around the player and disables the
rest (`SetActive(false)`):

```
CheckSectorSystem.RunSliding
   ▼
NavMeshManager.UpdateActiveSectors(playerPos, LevelConfig.ActiveSectorRadius)
   - GetNearestSector(playerPos)  (sector containing the player, else nearest by Z)
   - enable [center-radius .. center+radius], disable the others
   - RebuildNavMesh() ONLY when the active set actually changed
   - returns the center sector -> player.SetSector(center)
```

- `ActiveSectorRadius = 1` → a 3-wide active window (parity with Recycling).
- `Awake` falls back to `_sectors[0..1]` distance for `DistanceBetweenSectors` when the recycling
  trio isn't assigned, so `SpawnPointSystem`'s spawn-distance gating keeps working.
- NavMesh bake honors active GameObjects, so disabled sectors are excluded automatically.

## Editor setup for `Sliding`

1. `LevelConfig` asset → **Sector Mode = Sliding**, set **Active Sector Radius**.
2. `NavMeshManager` → fill the **Sectors** list in order along Z.
3. Ensure the `NavMeshSurface` collects from active children (inactive sectors stay out of the bake).
