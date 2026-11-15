# Known Issues — bug & optimization backlog

Result of a code audit (`Assets/_Scripts`, 2026-10). This lists what was **deliberately left
unfixed** and deferred to future decisions. Line numbers are as of the audit — verify against
the code before acting.

Already fixed during the audit (for context): leaderboard removed (`Remote/Leaderboard` + the
scene object with the token), `MoveSystem` writes `Direction` by `ref`, DoT ticks create a
separate `RequestDamageComponent`, damage/modifier/effect request targets are `EcsPackedEntity`,
`GunConfig.ShotDebuffs/ReloadDebuffs` are `[SerializeReference]`, `using NUnit` removed from
runtime, reload only with a non-full magazine and available reserve, grenadier retreat along a
NavMesh path, `InitializeAsync` in try/finally, defeat takes priority over victory, reload
progress in UI.

## General EcsLite notes

- The installed EcsLite version **resets the component to `default` on `Del`** (`components.cs`).
  Comments in the code like "EcsLite doesn't zero reusable structs" are wrong; manually clearing
  managed fields before `Del` is unnecessary.
- The real risk is **raw `int` entity ids** living longer than a frame. In DEBUG → "Cant touch
  destroyed entity"; without DEBUG → silently hitting a recycled id. Use `EcsPackedEntity`
  (`world.PackEntity` / `Unpack`) for anything that outlives a frame.
- A request that couldn't be processed should still be deleted — otherwise it hangs around
  forever.

## Bugs

### Combat / weapons
- **`BulletSystem.cs:83-96`** — one `Stack<Bullet>` for all calibers and for enemy bullets, while
  the prefab is picked by caliber (`ResolveProjectile`). A pooled bullet may come with someone
  else's mesh/trail/`MaxPierceCount`. Fix: `Dictionary<prefab|Caliber, Stack<Bullet>>`, like
  mobs/effects.
- **`BulletSystem.cs:95` + `BulletOverlapSystem.cs:84-89`** — the bullet's rotation is never set;
  the raycast check uses the stale `transform.forward`, OverlapBox uses the stale rotation.
  Fix: `rotation = LookRotation(direction)`, ray along `move.Direction`.
- **`CollisionSystem.cs:69-80`** — the last-pierce branch lacks `ContainsFixed`: the bullet hits
  the same mob again and dies before reaching the second one. `FixedList32Bytes<int>` holds 7
  ints — `MaxPierceCount > 8` will throw on `Add`. Fix: check before both branches + clamp to
  capacity.
- **`WeaponFireSystem.cs:29-35`** — an early `return` during single-load reload skips deleting
  `RequestFireComponent`, so requests pile up. `hasFiringRequest` in `WeaponReloadSystem`
  (reload interrupted by firing) relies on this leak. Fix: an explicit "interrupt" flag.
- **`EffectsSystem.cs:75-79`, `DecalSystem.cs:60-64`** — on an unknown id the request isn't
  deleted and logs every frame. `DecalSystem.cs:68-70` uses `decal` before the null check.
- **`DamageSystem`** — an `EndGameComponent` is created on every hit while player HP ≤ 0.
- **`GrenadeLauncher.cs:27-30`** — `Instantiate(null)` before the null check; the grenade
  `Count` was already decremented in `GrenadeThrowSystem:80`.
- **`GrenadeThrowSystem.cs:24`** — on pause/input lock `IsCharging` isn't reset: releasing the
  button during a pause → a throw immediately after unpause; the aim preview stays visible.
- **`ModifiersSystem`** — every applied DoT clone spawns its own VFX: N pellets = N effects on
  one mob. Fix: one effect per (owner, DamageType).

### Mobs / movement / AI
- **`MoveSystem.cs:53-66`** — waypoint reach threshold 0.05 with step > 0.1 (low FPS, speed
  buff) → jittering at a corner until the next repath. Fix: `if (dist <= step)` — snap and
  advance. The new waypoint after advancing isn't aligned to `y`.
- **`MobPathfindingSystem.cs:110-120`** — with a partial/failed path `MovePath` is deleted and
  the mob moves `MoveDirect` along the last direction; if it has left the navmesh, no path is
  ever built again. Fix: zero `Direction` or steer straight at the player.
- **Spawn: `GroupSpawnSystem.cs:245,269`, `SpawnPointSystem.cs:45,64`** — `mobCount` is read
  once per frame and doesn't grow: group spawn and several points in the same frame exceed
  `ActiveMobLimit`.
- **`SpawnPointSystem.cs:47-51`** — during the inter-spawn cooldown all point timers freeze.
- **Null checks**: `SpawnPointSystem.cs:84` (invalid `MobId`), `MobSpawnSystem.cs:201`
  (`ValueBar`), `PlayerSystem.cs:69,72` (`MeleeConfig` used before the check),
  `AgentDebugger.cs:192` (+ `Debug.Log` every frame at :217).
- **`PathRecalculationRequest` of formation followers is never consumed** (the pathfinding
  filter requires `PathRecalculation`) — harmless, a dead state.
- **System order (`EntryPoint.RegisterSystems`)** — `InputSystem`/`PlayerSystem` run after the
  weapon systems (+1 frame of input delay), `FollowSystem` before `PlayerMovementSystem`
  (camera chases last frame's position — jitter with a variable dt).

### World / loot / levels
- **`AdditionalLootSpawnSystem.cs:167-171` + `FragsConditionWrapper.cs:38-45`** — `CooldownMax`
  is never assigned (always 0), `FragsCondition` gets stuck in `IsFulfilled` forever (`Reset()`
  is never called) → after N frags loot respawns immediately after each pickup.
- **`FragsConditionWrapper.cs:53-55`** — `Dispose()` doesn't call `base.Dispose()`.
- **`AdditionalLootSpawnSystem.cs:18-19`** — `GetCopyUntyped()` may return null → an NRE in
  `Init` breaks all of `RegisterSystems().Init()`. **`LootSystem.cs:207`** — NRE on an unknown
  weapon id.
- **`BreakableSystem.cs:195-205`** — pooled breakables with the `Debris` outcome are never
  returned to the pool; repeated (non-`Once`) LevelEvents spawn new instances.
- **`BreakableSystem.cs:94-96`** — a breakable in an inactive sector (Sliding) has zero bounds →
  a probe at (0,0,0) can deal contact damage from mobs at the origin. Fix: skip
  `!view.isActiveAndEnabled`.
- **`BreakablesClearedConditionWrapper.cs:40-44`** — an empty `_configId` counts all level
  breakables, including far sectors → the stage won't end until the whole level is smashed.
- **`Utils/Extensions.cs:287-303`** — sector bounds are built from transformed min/max; for a
  sector rotated 90°/180°, min > max and the check is always false.

### UI / input / configs
- **`ControlBindingRow.cs:144`** — `WithControlsExcluding("<Mouse>/leftButton")`: attack can't
  be rebound to LMB except via "Reset All". No binding conflict checks.
- **`AimVisualizerSystem.cs:76`** — without a mouse (`Mouse.current == null`: gamepad, Steam
  Deck) NRE every frame.
- **`PlayerStats.cs:15`** — the HP bar maximum is hardcoded `100f` instead of
  `PlayerConfig.MaxHealth`. **`PlayerStats.cs:76`** — `SetFragCount` lacks a null check for
  `_killsText` (a duplicate of `SetKillsText`).
- **`ControlsSettingsMenu.cs:86,163-169`** — `effectivePath.StartsWith` without an empty-string
  check.
- **`MainMenuController.cs:148-162`** — `Play` has no re-entry guard (double click → two
  `LoadSceneAsync`).
- **`UILogSlot.cs:52,58`** — `Time.deltaTime`: the log "freezes" on pause.
- **`LocalizationHolder.cs:83`** — `GetKey` throws when `_entries == null`, no fallback to
  `Text`.
- **`using Unity.VisualScripting`** in `LocalizationManager.cs`, `Utils.cs:3`,
  `Utils/Extensions.cs:5`, `Components.cs` — an unnecessary dependency on the package;
  `LocalizationManager` is dead code.
- **`VatBakerWindow.cs`** — `CeilToInt((float)totalTexels / width)` loses precision beyond
  ~16.7M texels (:113); two `Color[]` arrays up to 16384² (:120-121) → OOM; `Shader.Find`
  without a null check (:297).

### Ending / loading / sectors
- **`CheckSectorSystem.cs:29-34` + `NavMeshManager.cs:124-150`** — in Sliding mode a synchronous
  `BuildNavMesh()` on every window change; the async bake on load is immediately overwritten by
  the synchronous one (all sectors still active); no hysteresis at the sector seam.

## Performance (by expected gain)

> **Done on 15.11.2026** (beyond this list): loading-time pool + shader warm-up (`PoolPrewarmer`, see
> `Docs/LoadingScreenFeature.md`), Preload Audio Data on all `_Sounds` SFX (first play no longer loads
> synchronously), Gunner's Animator culling → Cull Update Transforms (the other skinned mobs already had
> Cull Completely; Slow/Fast are VAT). Optimize Game Objects on mob FBX was deliberately **not** enabled:
> `WeaponHandIK` writes arm bones, prefabs parent weapons/helmets to bones, and VAT baking samples the rig.

1. **A redundant `NavMeshAgent` on every mob** (`_Prefabs/Mobs/*.prefab`, `m_Enabled: 1`,
   avoidance high quality). `Mob._agent` is unused, movement goes through `transform`. The agent
   computes crowd/avoidance for nothing and fights the manual movement. Remove or disable it.
2. **`Debug.Log` in hot paths** — `UISystem.cs:106,111` (every frame),
   `FragsConditionWrapper.cs:22,32,48` (every frame), `Modifier.cs:130` (every DoT tick),
   `AgentDebugger`, `DifficultyTimerView.cs:22,32`, `WeaponReloadSystem.StartShuttering`.
   Allocation + stack trace, in builds too.
3. **`Utils/Extensions.cs:287,299` — `.mesh` instead of `.sharedMesh`** — clones floor meshes
   (leak, breaks batching) and recomputes bounds on every call (every frame + per decal/loot/mob
   when sectors shift). Fix: `sharedMesh` + cache bounds in `FloorSector`.
   `FloorSector.MeshFilters` = null if the sector was inactive at `Awake`.
4. **Synchronous `BuildNavMesh()`** (`NavMeshManager:149,172`) — frame freeze. Switch to async
   `UpdateNavMesh` (like `RebuildNavMeshAsync`).
5. **`MobPathfindingSystem` has no budget** — force-repaths (`EnterChase`, breakables) all land
   in one frame; computed for mobs in attack phases too, and on pause. Fix: round-robin queue of
   N paths per frame; since everyone heads to the player, consider a shared flow field.
6. **Collider→entity maps rebuilt every frame** — `BulletOverlapSystem.cs:48-65`,
   `ExplosionSystem.cs:44-61` (even when all grenades are still on the fuse),
   `BreakableSystem.cs:86-129`. One shared map updated on spawn/despawn. The 32/64 buffers
   overflow with `layerMask = ~0` in a crowd → some mobs take no damage: allocate a damageable
   layer.
7. **`AudioSource.PlayClipAtPoint`** per mob hit/shot (`MeleeSpawnSystem:148`,
   `RangedAttackerSystem:160`) — creates/destroys a GameObject. An AudioSource pool with a limit.
8. **GC every frame / per event:**
   - `DamageSystem.cs:20` — `new List<int>()` + `Contains` → a `HashSet<int>` field; plus a full
     mob scan to find the dead — collect in the request loop.
   - `DifficultyTimerView.cs:14-17,44-45` — `TimeSpan.ToString` every frame → format only when
     the second changes.
   - `PlayerStats.cs:56-57` — bonus timer `ToString` every frame; `ValueBar.ApplyValue` —
     Gradient + `SetActive` every frame.
   - `LoadingScreen.cs:214-219` — `"LOADING" + new string('.')` every frame.
   - `SpawnPoint.cs:39` / `SpawnPointSystem.cs:74` — LINQ with closures; for a point without a
     level config the timer never resets → every frame.
   - `CollisionSystem.cs:84,154,207` — `List` + `Clone()` per debuff hit.
   - `MeleeSpawnSystem.cs:153-188,218-220` — scan of all `HealthComponent` per hit,
     `GetAllModifiersAsCopies` + LINQ `Count()`.
   - `MoveSystem.cs:62` — `pathPool.Del` discards the waypoint list instead of returning it to
     the pool.
   - `MobSpawnSystem.cs:151,174,178` — `GetComponentsInChildren` per spawn (cache on `Mob`);
     `GroupSpawnSystem.cs:293` / `FormationTable.ComputeOffsets` — `new` per group spawn.
   - `LocalizationHolder.GetKey` — LINQ `FirstOrDefault` with a closure.
9. **Minor:**
   - `world.Filter<>().End()` and `GetAsSingleton` in `Run` — cache in `Init` (no allocations,
     but mask sort+hash). Unused singleton lookups: `LootSystem:44`, `BulletSystem:18`,
     `WeaponFireSystem:15`, `WeaponReloadSystem:22`, `DecalSystem:13,16`. `UISystem` — ~9
     `GetAsSingleton` + ~15 filters + 8 `DeleteAllWith` per frame.
   - `LookAtCameraSystem` — rotates healthbars hidden via `renderer.enabled`.
   - `CrowdRenderSystem` — no frustum culling; mobs of a group animate in sync (randomize the
     starting `ClipTime`).
   - `EffectsHolder.GetEffect` / `SoundHolder.GetClip` — linear string search → `Dictionary`.
