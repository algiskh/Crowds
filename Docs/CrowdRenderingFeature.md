# Crowd Rendering (VAT) — Reference

Converts a regular rigged FBX mob (SkinnedMeshRenderer + Animator/Mecanim) into a **GPU-instanced
Vertex Animation Texture (VAT)** so a whole crowd of one mob type draws in ~1 draw call with **zero CPU
skinning and no Animator**. The animation is baked into a texture offline; at runtime a vertex shader
reads the pose from that texture and `Graphics.DrawMeshInstanced` draws every instance at once. This
doc is the source of truth so the code doesn't need re-exploring.

It is **opt-in per mob config and fully hybrid**: a mob uses VAT only if its `MobConfig` has a
`CrowdLibrary` assigned; every other mob keeps the classic skinned path unchanged. Movement, collision,
navmesh, health bars, death and pooling are untouched — only the *visual* layer is replaced.

## Model

| Thing | Where |
|---|---|
| `CrowdAnimationLibrary` | `Animation/Crowd/CrowdAnimationLibrary.cs` — baked asset: render mesh + position/normal maps + material + per-`AnimationType` clip table (`CrowdClip`). `GetFrame(clip, normTime)` → frame slot; `TryGetClip(type)`. |
| `CrowdClip` (struct) | same file — one baked clip: `Type`, `StartFrame`, `FrameCount`, `Loop` |
| `CrowdVat` shader | `Shader/CrowdVat.shader` — URP instanced; ForwardLit + ShadowCaster. Vertex stage reads the pose from `_PositionMap`. |
| `VatBakerWindow` | `_Scripts/Editor/CrowdVat/VatBakerWindow.cs` — the baker (**Tools ▸ Crowds ▸ VAT Baker**) |
| `CrowdInstanceComponent` | `ECS/Components.cs` — marks a VAT mob: `Library`, `CurrentClip`, `ClipTime`, `Initialized` |
| `CrowdRenderSystem` | `ECS/CrowdRenderSystem.cs` — batches VAT mobs per library, advances clips, issues `DrawMeshInstanced` |
| `MobConfig.CrowdLibrary` | `SO/MobConfig.cs` — optional `CrowdAnimationLibrary`; null ⇒ classic skinned path |
| `AnimationType` / `AnimationTypes` | `Animation/AnimationType.cs` — `TryFromStateName()` maps an Animator state name → `AnimationType` for the baker |
| `CrowdRenderTester` | `Animation/Crowd/CrowdRenderTester.cs` — standalone dev tool: draws a grid of instances to validate a bake without the game |
| baked assets | `_Data/Crowd/<Mob>/` — `<Mob>_VAT_Pos`, `<Mob>_VAT_Nrm`, `<Mob>_VAT_Mesh`, `<Mob>_VAT_Mat`, `<Mob>_CrowdLibrary` |

## VAT texture layout

The bake stores, per animation frame, every vertex's object-space position (and normal) as a texel.
Because a mob can have far more vertices than the 16384 GPU texture-width limit, the data is a **flat
`frame*vertexCount + vertexId` array wrapped into a fixed-width texture** (default width 8192):

```
texel index = frame * vertexCount + vertexId
   x = index % VatWidth      y = index / VatWidth      (point-sampled, clamp, RGBAHalf, linear)
```

- The render mesh carries the **raw vertex index in `uv2.x`**; the shader adds `frame*vertexCount`.
- Per-instance the shader gets `_Frame` (the current frame slot); the material carries
  `_VatWidth/_VatHeight/_VatVertexCount`. `CrowdRenderSystem` also pushes those three via the
  `MaterialPropertyBlock` each draw, so a stale/misserialized material can't silently break it (a 0
  there ⇒ `fmod(x,0)` = NaN ⇒ nothing renders).
- Positions/normals are baked in the **prefab-root local space** (the transform the runtime draws at),
  so the VAT mesh lines up with the mob's collider, health bar and the ground.

## Runtime flow

```
MobSpawnSystem.CreateMob (mobConfig.CrowdLibrary != null)
   │  add CrowdInstanceComponent { Library, CurrentClip=Run }
   │  DisableSkinnedView(mob)  → SkinnedMeshRenderer.enabled=false, Animator.enabled=false
   ▼
per frame:
   MoveSystem            moves/rotates the mob root transform (unchanged)
   AnimationSystem       SKIPS entities with CrowdInstanceComponent
   CrowdRenderSystem     requested = AnimationStateComponent.Requested (else Run)
                         if clip changed → reset ClipTime; else ClipTime += dt
                         frame = library.GetFrame(clip, ClipTime / clipDuration)
                         batch mob.transform.localToWorldMatrix + frame per library
                         DrawMeshInstanced(mesh, mat, matrices, count, mpb)   // ≤1023 per call
   ▼
DamageSystem (death)  → pool push + SetActive(false) + world.DelEntity
                         (entity gone ⇒ instance drops out of the crowd draw automatically)
```

`CrowdRenderSystem` is registered right after `AnimationSystem` in `EntryPoint.RegisterSystems()`, so
transforms are current for the frame. Order matters: it must run after movement.

## How to convert a mob to VAT

1. **Bake.** Open **Tools ▸ Crowds ▸ VAT Baker**. Assign the mob **prefab** (the one that gets spawned,
   e.g. `Mob.prefab` — *not* the raw FBX; the bake is stored in that prefab's root space). Set FPS
   (30 is fine), Bake Normals on, leave Texture Width 8192. Click **Bake**. Assets land in
   `_Data/Crowd/<prefab>/`.
   - The baker enumerates the Animator **controller states**, maps each state name to an `AnimationType`
     (`idle/walk/run/attack/die/throw/throw_cooldown`), and bakes each distinct clip once. States that
     reuse a clip share its rows.
2. **(Optional) Validate** with `CrowdRenderTester`: put it on an empty GameObject, assign the
   `<Mob>_CrowdLibrary`, pick a clip, Play — you should see a grid of animated instances.
3. **Wire it in.** Assign `<Mob>_CrowdLibrary` to the mob's `MobConfig.CrowdLibrary` field (e.g.
   `Slow.asset`). Play — that mob type now renders via VAT; its Animator + SkinnedMeshRenderer are
   switched off at spawn.

## Constraints & gotchas

- **Vertex count = GPU cost.** VAT removes CPU skinning and collapses draw calls, but the vertex shader
  still processes `instances × vertexCount` verts. The big crowd win comes from baking a **decimated
  low-poly** skinned mesh (a few thousand verts). The baker reads whatever SkinnedMeshRenderer the
  prefab has, so decimation is content-side — **no code change**: point it at a low-poly prefab, re-bake,
  reassign.
- **Bake from the spawned prefab**, not the FBX, or the mesh won't align with the runtime root
  (scale/rotation/offset drift → floating or mis-sized mob).
- **Two Animators.** A prefab that nests an FBX can have a controller-less Animator; the baker picks the
  Animator that actually has a controller (searching inactive children too).
- **No live bone sockets.** A VAT mob can't expose a runtime bone (e.g. a grenade throw origin, a held
  weapon). Such mobs stay on the classic path — the hybrid is intentional. `Grenadier` is a current
  example that should NOT get a `CrowdLibrary`.
- **No cross-fade.** Clip changes are a hard cut, not a Mecanim blend. Fine for run→attack→die.
- **One-shot clips** (attack/die/throw) clamp on the last frame; looping clips (idle/walk/run) wrap.
- **Per-instance tint.** `_InstColor` is plumbed (defaults to white in `CrowdRenderSystem`); a hit-flash
  would set a per-instance color here.
- **Culling** is per-instance via the render mesh bounds (baked to cover all frames). Good enough for
  top-down; there is no GPU culling yet.
