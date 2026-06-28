> **STATUS: IMPLEMENTED.** Files: `ECS/FormationTable.cs`, `ECS/FormationSystem.cs`,
> `ECS/GroupSpawnSystem.cs`, `SO/GroupSpawnConfig.cs`, `GroupSpawnPoint.cs`;
> components in `ECS/Components.cs`; `MobSpawnSystem.CreateMob` extracted as a shared
> static helper; systems + group-point registration wired in `ECS/EntryPoint.cs`.
> **Two deltas vs. the design below:** (1) followers reference the leader by
> `EcsPackedEntity` (generation-checked), **not** a `Transform` — this survives mob
> pooling/recycling, so a dead leader is detected even if its GameObject is reused.
> (2) `FormationLeaderComponent` was dropped — unused in v1 (followers pull from the
> leader; nothing reads a leader marker). Re-add it only when implementing §8 throttle.
> **Level dependence:** `GroupSpawnConfig` uses per-`DifficultyLevel` `SpawnPreset[]`
> (not a flat cooldown), and `GroupSpawnSystem` mirrors `SpawnPointSystem` — same
> difficulty-stage cooldown ramp, same distance-ring + `ActiveMobLimit` gating. A group
> spawns only on stages that have a preset, and only while the player is within
> `NavMeshManager.DistanceBetweenSectors` of the point.

# Minimal Group (Formation) Movement — Unity / EcsLite Implementation

A pragmatic, minimal subset of `FORMATION_MOVEMENT_INSTRUCTION.md`, adapted to the
Crowds codebase. Goal: spawn a **squad of mobs** that moves toward the player as a
cohesive formation (one leader + N followers), reusing the existing pathfinding and
movement systems.

**Deliberately dropped from the full algorithm** (add later if needed):
convoy/follow mode (§7b), full speed-regulation loop (§8), terrain-height snapping,
aircraft, roads, smoothed-facing math (§5 — we reuse the leader's already-smoothed
transform rotation instead).

Target shapes for v1: **Column**, **Wedge**, **Line** (from §2). Start with Wedge.

---

## 0. How it maps onto the existing code

| Instruction concept | Crowds equivalent |
|---|---|
| Leader pathfinds A→B (§7a) | A normal mob: `MobPathfindingSystem` → `MoveSystem` chase the **player** |
| `formationDir` smoothed facing (§5) | `leader.transform.forward` — `MoveSystem` already Slerps leader rotation |
| `worldSlot` (§6) | `leader.position + leader.rotation * slotOffset` |
| Follower steering `PositionPilot` (§7c) | New `FormationSystem` sets `MoveComponent.Direction`, runs before `MoveSystem` |
| Slot table + spacing (§2/§3/§4) | Computed once at spawn into `FormationFollowerComponent.SlotOffset` |
| Group manager (§11.2) | `FormationLeaderComponent` + `GroupSpawnConfig` SO |

System order (in `EntryPoint.RegisterSystems()`): insert `FormationSystem` **after**
`MobPathfindingSystem` and **before** `MoveSystem`, alongside `MeleeAttackerSystem`.

---

## 1. Data — slot tables & spacing

Add a small static table (no SO needed for v1). New file
`Assets/_Scripts/ECS/FormationTable.cs`:

```csharp
using UnityEngine;

namespace ECS
{
    public enum FormationType : byte { Column, Wedge, Line }

    /// One slot: parent index, local offset in formation-units (x=right, z=forward), settle angle.
    public readonly struct FormationSlot
    {
        public readonly int Base;     // parent slot index, -1 = leader/root
        public readonly float X, Z;   // formation units (scaled by spacing)
        public readonly float Angle;  // radians, settle facing offset (unused in v1 combat)
        public FormationSlot(int b, float x, float z, float a) { Base = b; X = x; Z = z; Angle = a; }
    }

    public static class FormationTable
    {
        const float Q = Mathf.PI / 2f;

        // Slot 0 is always the leader at origin. Up to 12 slots; extend as needed.
        static readonly FormationSlot[] Column =
        {
            new(-1, 0, 0, 0),
            new( 0, 0,-1, 0.25f*Q),
            new( 1, 0,-1,-0.25f*Q),
            new( 2, 0,-1, Q),
            new( 3, 0,-1, 0),
            new( 4, 0,-1, 0.25f*Q),
            new( 5, 0,-1,-0.25f*Q),
            new( 6, 0,-1, Q),
        };

        static readonly FormationSlot[] Wedge =
        {
            new(-1, 0, 0,    0),
            new( 0, 1,-1,    0.25f*Q),
            new( 0,-1,-1.33f,-0.25f*Q),
            new( 1, 1,-1,    0.5f*Q),
            new( 2,-1,-1.33f,-0.25f*Q),
            new( 3, 1,-1,    0.5f*Q),
            new( 4,-1,-1.33f,-0.25f*Q),
        };

        static readonly FormationSlot[] Line =
        {
            new(-1, 0, 0, 0),
            new( 0, 1, 0, 0),
            new( 0,-1, 0, 0),
            new( 1, 1, 0, 0),
            new( 2,-1, 0, 0),
            new( 3, 1, 0, 0),
            new( 4,-1, 0, 0),
        };

        public static FormationSlot[] Get(FormationType t) => t switch
        {
            FormationType.Column => Column,
            FormationType.Line   => Line,
            _                    => Wedge,
        };

        /// Compute local-space slot offsets (§4). spacingX/Z = meters per formation unit.
        /// Returns offsets indexed by slot; slot 0 (leader) is (0,0,0).
        public static Vector3[] ComputeOffsets(FormationType t, int count, float spacingX, float spacingZ)
        {
            var table = Get(t);
            count = Mathf.Min(count, table.Length);
            var offs = new Vector3[count];
            for (int j = 0; j < count; j++)
            {
                var s = table[j];
                if (s.Base >= 0 && s.Base < j)
                {
                    offs[j] = new Vector3(
                        offs[s.Base].x + spacingX * s.X, 0f,
                        offs[s.Base].z + spacingZ * s.Z);
                }
                else offs[j] = new Vector3(s.X, 0f, s.Z);
            }
            return offs;
        }
    }
}
```

> §4 averages the two units' spacing factors. v1 uses a single per-group spacing
> (all members same size), so the average collapses to one constant — fine for zombies.

---

## 2. Components (`Components.cs`)

```csharp
// On the leader mob (a normal mob that also leads a group).
public struct FormationLeaderComponent
{
    public FormationType Formation;
    public List<Transform> Followers;   // follower transforms, slot order (index 0 => slot 1)
}

// On each follower mob.
public struct FormationFollowerComponent
{
    public Transform Leader;     // leader's transform (null-checked; survives pooling unlike raw entity id)
    public Vector3 SlotOffset;   // local-space offset from leader (x right, z forward), precomputed
    public bool InFormation;     // hysteresis state (§7c)
}
```

> Store the **Transform**, not the EcsLite entity id — mobs are pooled and ids recycle.
> Null / `!activeSelf` on `Leader` means the leader died (see §6 lifecycle).

---

## 3. Group spawn config (SO) + spawn point

`Assets/_Scripts/SO/GroupSpawnConfig.cs`:

```csharp
using System;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "GroupSpawnConfig", menuName = "Scriptable Objects/GroupSpawnConfig")]
public class GroupSpawnConfig : ScriptableObject
{
    [SerializeField] private ECS.FormationType _formation = ECS.FormationType.Wedge;
    [SerializeField] private string _leaderMobId;
    [SerializeField] private FollowerEntry[] _followers;
    [SerializeField, MinValue(0.1f)] private float _spacingX = 1.6f;
    [SerializeField, MinValue(0.1f)] private float _spacingZ = 1.6f;
    [SerializeField, MinValue(0f)]   private float _cooldown = 12f;

    public ECS.FormationType Formation => _formation;
    public string LeaderMobId => _leaderMobId;
    public FollowerEntry[] Followers => _followers;
    public float SpacingX => _spacingX;
    public float SpacingZ => _spacingZ;
    public float Cooldown => _cooldown;

    [Serializable]
    public class FollowerEntry
    {
        public string MobId;
        [MinValue(1)] public int Count = 1;
    }
}
```

`Assets/_Scripts/GroupSpawnPoint.cs` (mirror of `SpawnPoint`):

```csharp
using UnityEngine;

public class GroupSpawnPoint : MonoBehaviour
{
    [SerializeField] private GroupSpawnConfig _config;
    public GroupSpawnConfig Config => _config;
}
```

Add a matching ECS singleton-ish component + per-point timer (mirror
`SpawnPointComponent`) in `Components.cs`:

```csharp
public struct GroupSpawnPointComponent
{
    public float Timer;
    public GroupSpawnPoint Value;
}
```

Register one entity per `GroupSpawnPoint` in `EntryPoint` setup (same place
`SpawnPointComponent`s are created): `FindObjectsByType<GroupSpawnPoint>()` →
`world.NewEntity()` + add `GroupSpawnPointComponent`.

---

## 4. Group spawn system

`Assets/_Scripts/ECS/GroupSpawnSystem.cs`. Spawns leader + all followers atomically,
computes slot offsets, links the components. Reuses the same NavMesh-snap +
component-init that `MobSpawnSystem` does — **factor `MobSpawnSystem`'s mob-creation
into a reusable helper** (e.g. `MobSpawnSystem.CreateMob(world, config, position)`)
and call it from both, to avoid duplicating component setup.

```csharp
using Leopotam.EcsLite;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ECS
{
    public sealed class GroupSpawnSystem : IEcsRunSystem
    {
        public void Run(IEcsSystems systems)
        {
            var world = systems.GetWorld();
            if (world.GetAsSingleton<PauseStateComponent>().IsPaused) return;
            if (!world.TryGetAsSingleton<PlayerComponent>(out var player)) return;

            ref var mainHolder = ref world.GetAsSingleton<MainHolderComponent>();
            var mobConfigHolder = mainHolder.Value.MobConfigHolder;

            float dt = Time.deltaTime;
            var ptPool      = world.GetPool<GroupSpawnPointComponent>();
            var leaderPool  = world.GetPool<FormationLeaderComponent>();
            var followPool  = world.GetPool<FormationFollowerComponent>();

            foreach (var e in world.Filter<GroupSpawnPointComponent>().End())
            {
                ref var pt = ref ptPool.Get(e);
                pt.Timer -= dt;
                if (pt.Timer > 0f || pt.Value == null || pt.Value.Config == null) continue;

                var cfg = pt.Value.Config;
                pt.Timer = cfg.Cooldown;

                // --- collect member configs in slot order (slot 0 = leader) ---
                var members = new List<MobConfig>();
                members.Add(mobConfigHolder.GetConfigById(cfg.LeaderMobId));
                foreach (var f in cfg.Followers)
                    for (int i = 0; i < f.Count; i++)
                        members.Add(mobConfigHolder.GetConfigById(f.MobId));

                var offsets = FormationTable.ComputeOffsets(
                    cfg.Formation, members.Count, cfg.SpacingX, cfg.SpacingZ);

                Vector3 origin = pt.Value.transform.position;
                Quaternion baseRot = pt.Value.transform.rotation;

                // --- spawn leader (slot 0) ---
                int leaderEntity = MobSpawnSystem.CreateMob(world, members[0], origin);
                var leaderTf = world.GetPool<MobComponent>().Get(leaderEntity).Value.transform;
                ref var lead = ref leaderPool.Add(leaderEntity);
                lead.Formation = cfg.Formation;
                lead.Followers = new List<Transform>();

                // --- spawn followers (slots 1..N) ---
                for (int j = 1; j < members.Count && j < offsets.Length; j++)
                {
                    Vector3 world0 = origin + baseRot * offsets[j];
                    if (NavMesh.SamplePosition(world0, out var hit, 3f, NavMesh.AllAreas))
                        world0 = hit.position;

                    int fe = MobSpawnSystem.CreateMob(world, members[j], world0);
                    var fTf = world.GetPool<MobComponent>().Get(fe).Value.transform;

                    ref var follow = ref followPool.Add(fe);
                    follow.Leader = leaderTf;
                    follow.SlotOffset = offsets[j];     // local; leader rotation applied each frame
                    follow.InFormation = false;
                    lead.Followers.Add(fTf);

                    // followers don't pathfind: drop the recalculation component so
                    // MobPathfindingSystem skips them and they steer directly.
                    var recalc = world.GetPool<PathRecalculation>();
                    if (recalc.Has(fe)) recalc.Del(fe);
                    var path = world.GetPool<MovePath>();
                    if (path.Has(fe)) path.Del(fe);
                }
            }
        }
    }
}
```

> Register `GroupSpawnSystem` right after `SpawnPointSystem` in `RegisterSystems()`.

---

## 5. Formation follower system (the steering core)

`Assets/_Scripts/ECS/FormationSystem.cs`. Runs **after** `MobPathfindingSystem`,
**before** `MoveSystem` (next to `MeleeAttackerSystem`).

```csharp
using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
    public sealed class FormationSystem : IEcsRunSystem
    {
        const float FormationTime = 1.5f;   // §7c look-ahead: closer => slower
        const float Precision     = 1.0f;   // §7c base tolerance (meters)
        const float MinSpeedFrac  = 0.3f;   // never crawl below this fraction of base speed

        public void Run(IEcsSystems systems)
        {
            var world = systems.GetWorld();
            if (world.GetAsSingleton<PauseStateComponent>().IsPaused) return;

            var followPool = world.GetPool<FormationFollowerComponent>();
            var movePool   = world.GetPool<MoveComponent>();
            var mobPool    = world.GetPool<MobComponent>();

            foreach (var e in world.Filter<FormationFollowerComponent>()
                                   .Inc<MoveComponent>().Inc<MobComponent>().End())
            {
                ref var follow = ref followPool.Get(e);
                ref var move   = ref movePool.Get(e);
                ref var mob    = ref mobPool.Get(e);
                if (mob.Value == null || !mob.Value.gameObject.activeSelf) continue;

                // --- leader lost? revert to normal player-chasing (§6) ---
                if (follow.Leader == null || !follow.Leader.gameObject.activeSelf)
                {
                    Promote(world, e, followPool);
                    continue;
                }

                // §6: worldSlot = leader.pos + leader.rotation * slotOffset
                Vector3 worldSlot = follow.Leader.position + follow.Leader.rotation * follow.SlotOffset;
                Vector3 self = mob.Value.transform.position;
                worldSlot.y = self.y;

                Vector3 toSlot = worldSlot - self;
                toSlot.y = 0f;
                float dist = toSlot.magnitude;

                // §7c hysteresis tolerance
                float tol = follow.InFormation ? Precision * 2f : Precision * 0.5f;

                if (dist <= tol)
                {
                    follow.InFormation = true;
                    move.Direction = Vector3.zero;        // settled: hold slot
                    // face same way as leader so the squad looks aligned
                    mob.Value.transform.rotation = follow.Leader.rotation;
                }
                else
                {
                    follow.InFormation = false;
                    Vector3 dir = toSlot / dist;
                    move.Direction = dir;                 // MoveSystem moves along Direction
                    mob.Value.transform.rotation = Quaternion.Slerp(
                        mob.Value.transform.rotation, Quaternion.LookRotation(dir),
                        move.Speed * Time.deltaTime);

                    // §7c proportional speed: closer => slower. Scale base Speed by a 0..1 factor.
                    float frac = Mathf.Clamp(dist / (FormationTime * Mathf.Max(move.Speed, 0.01f)),
                                             MinSpeedFrac, 1f);
                    move.Direction = dir * frac;          // MoveDirect multiplies Speed by |Direction|
                }
            }
        }

        // Leader died: detach follower, let it chase the player like a normal mob.
        static void Promote(EcsWorld world, int e, EcsPool<FormationFollowerComponent> followPool)
        {
            followPool.Del(e);
            var recalc = world.GetPool<PathRecalculation>();
            if (!recalc.Has(e))
            {
                ref var r = ref recalc.Add(e);
                r.Interval = world.GetAsSingleton<MainHolderComponent>().Value.PathRecalculationInterval;
            }
            var req = world.GetPool<PathRecalculationRequest>();
            if (!req.Has(e)) req.Add(e);
        }
    }
}
```

> **Note on `MoveDirect`:** today it does `position += Speed * modifier * dt * Direction`,
> assuming `Direction` is unit length. We multiply `Direction` by `frac` (0..1) to get the
> proportional speed for free. Verify `MoveDirect` doesn't re-normalize `Direction`; if it
> does, add a separate `SpeedScale` field to `MoveComponent` instead.

---

## 6. Lifecycle / cleanup (must-do, not optional)

Mobs are pooled (`MobPoolComponent`) and reused. When a mob dies / despawns
(wherever the death path returns a mob to its pool — check `DamageSystem` /
`MobSpawnSystem`'s despawn), **strip formation components** so a recycled mob does
not inherit stale data:

```csharp
if (followPool.Has(entity))  followPool.Del(entity);
if (leaderPool.Has(entity))  leaderPool.Del(entity); // (followers handle leader loss via Promote)
```

For a dying **leader**, followers self-detect via the null/`!activeSelf` check in
`FormationSystem.Promote` — no central bookkeeping needed for v1. (If you later want a
promotion-to-new-leader instead of disband, reassign slot 0 to the front follower and
recompute offsets.)

---

## 7. Registration checklist

1. `Components.cs`: add `FormationLeaderComponent`, `FormationFollowerComponent`,
   `GroupSpawnPointComponent`. Add `FormationType` (or keep in `FormationTable.cs`).
2. New files: `FormationTable.cs`, `FormationSystem.cs`, `GroupSpawnSystem.cs`,
   `GroupSpawnConfig.cs` (SO), `GroupSpawnPoint.cs` (MonoBehaviour).
3. Refactor `MobSpawnSystem` so mob creation is a reusable
   `static int CreateMob(EcsWorld, MobConfig, Vector3)` and call it from `GroupSpawnSystem`.
4. `EntryPoint`: register `GroupSpawnPointComponent` entities for each scene
   `GroupSpawnPoint`; prewarm pools for leader + follower mob ids (extend `PrewarmMobPool`).
5. `EntryPoint.RegisterSystems()`:
   - `GroupSpawnSystem` after `SpawnPointSystem`.
   - `FormationSystem` after `MobPathfindingSystem`, before `MoveSystem`
     (next to `MeleeAttackerSystem`).
6. Don't forget `.meta` files: create the new scripts/SOs **through Unity** so Unity
   generates the `.meta` (per project convention — never add them by hand).

---

## 8. Test / tuning order

1. Place one `GroupSpawnPoint` with a `GroupSpawnConfig`: Wedge, 1 leader + 4 followers,
   spacing 1.6. Enter Play. Verify the squad spawns in a "▼" and the followers track the
   leader as it chases the player.
2. Tune `SpacingX/Z` (shape size), `FormationTime` (tightness — lower = snappier),
   `Precision` (settle tolerance).
3. Kill the leader → confirm followers revert to chasing the player (`Promote`).
4. Kill a follower → confirm the recycled mob spawns clean later (no leftover formation).
5. Only after this is solid: optionally add §8 leader speed-throttle (leader slows when a
   follower lags) and §9 anti-overlap nudge. Both are independent add-ons.

---

## 9. Known limitations of this minimal version (by design)

- Followers don't pathfind → can clip walls/obstacles on the straight line to their slot.
  Acceptable for open top-down maps; add a navmesh fallback (§7c "request a planned path")
  when the direct line is blocked if it becomes a problem.
- No leader speed-regulation (§8) → a fast leader can briefly outrun followers; the
  proportional follower speed plus `FormationTime` mostly hides this. Add §8 if groups
  visibly stretch.
- No inter-mob separation beyond slot geometry; relies on slots being spaced enough.
- Single uniform spacing per group (no per-unit `FormationX/Z` averaging).