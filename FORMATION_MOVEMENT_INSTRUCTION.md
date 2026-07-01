# Formation Movement — Implementation Instruction

How to make a group of units move in formation from point A to point B.
Reverse‑engineered from the legacy Poseidon (Real Virtuality) AI code so it can be
re‑implemented as a task in Unity. **No engine code is reused — this is the algorithm only.**

Source references (legacy project, for cross‑checking only):
- `engine/Poseidon/AI/AISubgroup.cpp` — formation table, slot positions, direction, speed coef
- `engine/Poseidon/AI/AIUnit.cpp` — relative→absolute slot transform
- `engine/Poseidon/AI/AIUnit.hpp` — `FormInfo` struct, formation enum
- `engine/Poseidon/AI/VehicleAIPilot.cpp` — leader/follower steering loop

---

## 1. Core idea

A group has **one leader** (slot 0) and N followers. Movement works in two layers:

1. **Leader** path‑finds and drives to the destination (A → B) normally.
2. **Each follower** continuously computes a *target slot position* in world space
   (derived from the leader's position + facing + a fixed formation shape), then steers
   toward that slot. The slot moves with the leader, so following the slot = staying in formation.

Followers never path to B directly; they path to their **moving slot**. When the leader
arrives, the slots stop moving and everyone settles into the shape.

> Coordinate convention in the source: **X = right, Z = forward (the direction of travel),
> Y = up.** Unity uses the same handedness for X/Z/Y, so you can treat `position[0]=x`,
> `position[2]=z` directly. `H_PI` in the data = π/2 (90°).

---

## 2. Formation shapes (the slot table)

Each formation is a fixed table of up to 12 slots. Each slot is defined by **three values**:

| Field | Meaning |
|-------|---------|
| `base` | Index of the *parent* slot this slot is positioned relative to. `-1` = the leader/root (no parent). |
| `position` = `(x, z)` | Offset from the parent slot, in **formation units** (not meters yet). `x` = sideways, `z` = forward/back. Typically `z = -1` means "one rank behind the parent". |
| `angle` | Facing offset (radians) the unit should adopt once settled in the slot. |

Slots form a **chain**: slot 1 is placed relative to slot 0, slot 2 relative to slot 1 (or 0), etc.
This is why a formation naturally stretches/compresses as a unit.

### Slot tables (base, x, z, angle) — copy these verbatim

`q = π/2`. Slot 0 is always the leader at the origin.

**Column** (single file, each unit directly behind the previous):
```
0: base=-1  x= 0  z= 0   a= 0
1: base= 0  x= 0  z=-1   a= 0.25q
2: base= 1  x= 0  z=-1   a=-0.25q
3: base= 2  x= 0  z=-1   a= q
4: base= 3  x= 0  z=-1   a= 0
... pattern of angles repeats (0.25q, -0.25q, q, 0) for slots 5..11, each z=-1 behind prev
```

**Staggered Column** (column, but alternating left/right by one unit):
```
0: base=-1  x= 0  z= 0   a= 0
1: base= 0  x= 1  z=-1   a= 0.25q
2: base= 1  x=-1  z=-1   a=-0.25q
3: base= 2  x= 1  z=-1   a= q
4: base= 3  x=-1  z=-1   a= 0
... x alternates +1,-1; z=-1 each; angles repeat 0.25q,-0.25q,q,0
```

**Wedge** (leader at front tip, units fan out behind on both sides — "▼" pointing forward):
```
0: base=-1  x= 0  z= 0      a= 0
1: base= 0  x= 1  z=-1      a= 0.25q
2: base= 0  x=-1  z=-1.33   a=-0.25q
3: base= 1  x= 1  z=-1      a= 0.5q
4: base= 2  x=-1  z=-1.33   a=-0.25q
... right side chains off odd slots (x=+1), left side off even slots (x=-1, z=-1.33)
```

**Echelon Left** (diagonal line trailing to the left):
```
0: base=-1  x= 0  z= 0   a= 0
1: base= 0  x=-1  z=-1   a=-0.25q
2: base= 1  x=-1  z=-1   a=-0.25q
3: base= 2  x=-1  z=-1   a=-0.5q
... every slot x=-1, z=-1 off the previous
```

**Echelon Right** (mirror of Echelon Left, trailing to the right):
```
0: base=-1  x= 0  z= 0   a= 0
1: base= 0  x= 1  z=-1   a= 0.25q
2: base= 1  x= 1  z=-1   a= 0.25q
3: base= 2  x= 1  z=-1   a= 0.5q
... every slot x=+1, z=-1 off the previous
```

**Vee** (inverted wedge — opening points forward, leader at the rear notch):
```
0: base=-1  x= 0  z= 0   a=-0.25q
1: base= 0  x= 1  z= 0   a= 0.25q
2: base= 0  x=-1  z= 1   a=-0.25q
3: base= 1  x= 1  z= 1   a= 0.25q
4: base= 2  x=-1  z= 1   a=-0.25q
... two arms going FORWARD (z=+1) and out (x=±1)
```

**Line** (all units abreast, side by side on one rank):
```
0: base=-1  x= 0  z= 0   a= 0
1: base= 0  x= 1  z= 0   a= 0
2: base= 0  x=-1  z= 0   a= 0
3: base= 1  x= 1  z= 0   a= 0
4: base= 2  x=-1  z= 0   a= 0
... alternates right/left, all z=0 (same rank)
```

> You only need the shapes you actually use. Wedge is a good default for vehicles; Column for
> roads/convoys; Line for assaulting a position.

---

## 3. Per‑unit spacing factors

Each unit type carries two numbers that scale the formation to the unit's size (so tanks
spread out more than soldiers):

- `FormationX` — desired left/right spacing (meters per formation unit)
- `FormationZ` — desired front/back spacing (meters per formation unit)

Pick sensible values per unit type, e.g. infantry `FormationX = FormationZ = 5`, cars `~10`,
tanks `~15`.

---

## 4. Computing each slot's *relative* position (per frame, or when group changes)

Build the relative positions by walking slots **in order of unit ID (0..N)**. For each slot `j`:

```
info = table[formation][j]          // base, (x,z), angle from §2
if info.base >= 0:
    base = slot[info.base]
    factorX = 0.5 * (FormationX(baseUnit) + FormationX(thisUnit))
    factorZ = 0.5 * (FormationZ(baseUnit) + FormationZ(thisUnit))
    relPos[j].x = relPos[info.base].x + factorX * info.x
    relPos[j].z = relPos[info.base].z + factorZ * info.z
else:
    relPos[j].x = info.x            // root/leader = origin
    relPos[j].z = info.z
relPos[j].y = 0
thisUnit.formationAngle = info.angle
```

Key point: the metric spacing between two chained slots is the **average** of the two units'
spacing factors — so a big unit behind a small one gets a gap sized between them.

This produces `relPos[j]` for every unit, expressed in the leader's local frame (X right,
Z forward), with the **leader at slot 0**.

---

## 5. Formation facing direction (smoothed)

The whole shape is oriented to the leader's **movement direction**, but rotated toward it
*gradually* so the formation doesn't snap around when the leader jitters.

Each frame:
```
leaderVel = leader.velocity
if |leaderVel|^2 > (1 m/s)^2:                 // only update while actually moving
    targetAngle = atan2(leaderVel.x, leaderVel.z)
    currentAngle = atan2(formationDir.x, formationDir.z)
    delta = angleDifference(targetAngle, currentAngle)   // shortest signed angle
    maxStep = 0.3 rad/s * dt                              // rate limit
    delta = clamp(delta, -maxStep, +maxStep)
    formationDir = rotateY(currentAngle + delta)          // unit vector
```
Keep `formationDir` as a normalized XZ vector. When the leader is stopped, it holds its last value.

---

## 6. Converting a slot to a world target position

For any follower unit, its world‑space slot target is:

```
M = TRS(position = leader.position,
        rotation = lookRotation(formationDir, up),
        scale    = 1)

worldSlot = M.TransformPoint( relPos[thisUnit] - relPos[leader] )   // = relPos[thisUnit] since leader is origin
worldSlot.y = terrainHeightAt(worldSlot.x, worldSlot.z)             // snap to ground
```

(For aircraft, instead clamp `y` to at least `terrain + 25 m` rather than snapping.)

`relPos[leader]` is the slot‑0 origin, so the subtraction just re‑centers on the leader.
The resulting `worldSlot` is where this unit *should* be right now.

---

## 7. The movement loop

### 7a. Leader
- Receives destination **B**. Runs normal pathfinding/navmesh to B and drives along the path.
- **Throttles its own speed** so the group stays together (see §8). This is the single most
  important behavior for a believable formation — without it the leader runs off and leaves
  everyone behind.

### 7b. Followers — two modes

**Convoy / follow mode** (default for vehicles, and always when the leader is on a road):
Instead of chasing an abstract slot, each unit follows the **previous unit in the group**
(by ID order, skipping the leader) at a safe distance. This keeps vehicles nose‑to‑tail on
roads where a geometric slot would put them in a ditch.

```
follow = previousUnitInGroup(thisUnit)        // the unit with the next-lower ID
factorZ = 0.5 * (FormationZ(thisUnit) + FormationZ(follow))
followPos = follow.position transformed by relative offset (0,0,-0.4*factorZ)   // just behind it
// lead the target by the predecessor's velocity so we aim where it's going:
followPos += follow.velocity * formationTime * (onRoad ? 0.3 : 0.6)
target = followPos
```
`formationTime` is a small look‑ahead constant per unit type (≈ how many seconds it takes the
unit to close a formation gap; ~1–3 s is reasonable).

**Formation mode** (used for cautious/combat units): target = the `worldSlot` from §6 directly,
optionally led ahead by the leader's velocity. Use this when you want a strict geometric shape
(e.g. a line assault) rather than a convoy.

### 7c. Steering to the target (both modes) — `PositionPilot`
Given a `target` position and a `precision` tolerance (≈ `max(4, unitPrecision)` meters):

```
tol = precision
if alreadyInFormation: tol *= 2      // hysteresis: easy to stay
else:                  tol *= 0.5    // harder to first acquire

if distance(unit, target) < tol:
    inFormation = true
    speedWanted = 0
    turn to formationDir (or to fire target if engaging); if basically aligned and stopped -> idle
else:
    inFormation = false
    freePos = findNearestEmpty(target)         // avoid stacking on another unit (see §9)
    if simplePath(unit -> freePos):            // line of sight, no obstacle
        steer straight at freePos
        speedWanted = distance * (1/formationTime)   // closer = slower
        clampMin(speedWanted, 1.5)
        (allow reversing if target is behind and close)
    else:
        request a planned path to freePos and drive it (navmesh)
```

The `speedWanted = distance / formationTime` rule is the proportional controller that makes
units accelerate when far from slot and ease in as they arrive.

---

## 8. Group speed regulation (leader holds back for stragglers)

Every ~second the leader recomputes a `formationCoef ∈ [0.1, 1.5]` that limits its own speed:

```
for each follower:
    behind = dot( (slotWorldPos(follower) - follower.position), leaderDir )  // how far back along travel axis
    track the worst (largest) 'behind', weighted by that unit's max speed
maxDelay = worstBehind - noDelay        // noDelay = 1.0 in combat, else 0.5  (deadband)

wantedCoef = (slowestUnitMaxSpeed - maxDelay/timeToEqual) / leaderMaxSpeed   // timeToEqual ≈ 3 s
// move formationCoef toward wantedCoef, but no faster than 0.1 per second:
formationCoef += clamp(wantedCoef - formationCoef, -0.1*dt, +0.1*dt)
clamp(formationCoef, 0.1, 1.5)

leaderSpeedLimit = formationCoef * leaderMaxSpeed
```

So: if a follower falls behind, `maxDelay` grows, `wantedCoef` drops, and the leader slows
until the group closes up. The change is rate‑limited (0.1/s) so the leader doesn't lurch.

Follower spacing within convoy mode also self‑regulates: a follower that is **closer than the
wanted distance slows down**, one that is **further speeds up (catch‑up)**, and additionally a
unit will slow if the unit *behind* it has fallen too far back (so it waits for the tail).

---

## 9. Anti‑overlap (`findNearestEmpty`)

Before committing to a slot target, nudge it to the nearest position not already occupied by
another unit (simple circle/radius check against other units' positions, push out along the
separating vector). Prevents two units fighting over the same point. If the ideal slot can't
be made free, mark the unit "in formation" anyway so it stops trying.

---

## 10. "Arrived" / settle condition

A unit is **in formation** when it is within `tol` (§7c) of its slot. The group has **arrived
at B** when the leader is at B *and* all followers report in‑formation. At that point each unit
turns to its `formationAngle` (§2) relative to `formationDir` and goes idle (engine off / stand).

---

## 11. Minimal build order for Unity

1. **Data:** the §2 slot tables (as `ScriptableObject` or static arrays) + per‑type
   `FormationX/Z`, `formationTime`, `precision`.
2. **Group manager:** holds leader + ordered follower list and current formation enum.
3. Per frame: update `formationDir` (§5) → compute `relPos[]` (§4) → for each follower compute
   `worldSlot`/`followPos` (§6/§7b).
4. **Leader controller:** navmesh to B, speed‑limited by `formationCoef` (§8).
5. **Follower controller:** `PositionPilot` steering (§7c) toward its target, with
   `findNearestEmpty` (§9).
6. **Settle:** detect arrival (§10), face `formationAngle`, idle.

Start with **Wedge** + **formation mode** on flat ground to validate slot geometry, then add
convoy mode and the speed‑regulation loop. Tune `formationTime`, `precision`, the `0.1/s`
coef rate, and `FormationX/Z` to taste.
```
```
