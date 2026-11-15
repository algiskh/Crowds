# Damage Feedback — Reference

On-screen reaction to the player getting hurt (GameDesignReview item 5, "Получение урона"):

- **Red edge vignette.** Flashes on every hit. Strength depends on damage as a share of max HP. Poison ticks flash weaker.
- **Low-health pulse.** Below 25% HP the vignette keeps pulsing, and the pulse gets stronger as HP drops.
- **Camera shake.** About 0.1 s on direct hits; amplitude scales with the hit.
- **Hit-direction arc.** An arc on a ring around the screen centre points toward the hit's source (mob, shooter, explosion).

Not done yet from item 5: hit marker on the crosshair and a kill marker.

## Flow

```
CollisionSystem / ExplosionSystem / MeleeSpawnSystem ──► RequestDamageComponent { HasSource, SourcePosition }
DamageSystem (player branch, after invulnerability + shield, damage > 0)
   └─► PlayerDamagedEventComponent { Damage, CurrentHealth, MaxHealth, IsPeriodic, HasSource, SourcePosition }
DamageFeedbackSystem (right after DamageSystem) ──► DamageFeedbackOverlay (vignette alpha, arcs)
                                                └─► CameraShakeSystem.Shake(world, duration, amplitude)
CameraShakeSystem(apply:false) — before FollowSystem: removes last frame's offset
CameraShakeSystem(apply:true)  — last system: applies this frame's offset (the frame renders shaken)
```

- **Hit sources.**
  - Enemy bullet: a point back along the bullet's direction.
  - Mob contact: the mob's position.
  - Mob melee: the strike centre.
  - Explosion: its centre.
  - DoT ticks (`ModifiersSystem`): `HasSource = false`, so no arc.
- **Invulnerability.** Hits blocked by `PlayerConfig.HitInvulnerabilityTime` raise no event, so they get no feedback either.
- **Pause.** Everything freezes: no fade and no shake.
- **Fail sequence.** Once it starts, the overlay hides and events are dropped. The fail veil takes over.

## Shake and FollowSystem

`FollowSystem` moves the camera with `MoveTowards` from its *current* position. A naive offset would be "corrected" by following, and it would drift.

The fix is to register the shake system twice:

1. The first instance removes the offset before following.
2. The second instance adds a new offset at the end of the frame.

`LookAtCursorSystem` runs after following, so the aim ray uses the clean camera position. A weaker shake never cuts a stronger one that is still running.

## Overlay

`UI/DamageFeedbackOverlay.cs` is a plain C# class, created lazily by the system:

- Its Canvas (Screen Space Overlay) has `sortingOrder` 400. That is below the crosshair (500), the pause menu (900) and the fail veil (1000).
- The vignette and arc textures are generated in code (`Texture2D`), so no assets are needed.
- It has 4 pooled arcs. When all are busy, the oldest one is reused.
- It lives in the scene, so a restart recreates it.

## Settings — `PlayerConfig.DamageFeedback` (`DamageFeedbackSettings`)

| Field | Default | Meaning |
|---|---|---|
| `VignetteColor` | (0.75, 0, 0) | colour (alpha ignored) |
| `MaxFlashAlpha` / `MinFlashAlpha` | 0.65 / 0.2 | flash of a "full" hit / of the smallest direct hit |
| `FullFlashDamageShare` | 0.25 | damage/maxHP giving the full flash |
| `PeriodicFlashScale` | 0.35 | DoT tick flash multiplier |
| `FlashFadeTime` | 0.45 | fade seconds |
| `LowHealthThreshold` / `LowHealthPulseAlpha` / `LowHealthPulseRate` | 0.25 / 0.4 / 1.4 Hz | low-HP pulse |
| `ShakeDuration` / `ShakeAmplitude` | 0.12 s / 0.18 m | shake (scaled 0.4…1 by hit strength) |
| `ShowHitDirection` / `HitDirectionTime` | on / 0.8 s | direction arc |

## Files

`ECS/DamageFeedbackSystem.cs`, `ECS/CameraShakeSystem.cs`, `UI/DamageFeedbackOverlay.cs`. Changes in other files:

- `Components.cs`: `PlayerDamagedEventComponent`, `CameraShakeComponent`, and `HasSource`/`SourcePosition` on `RequestDamageComponent`.
- `DamageSystem`: raises the event.
- `PlayerConfig`: the settings block.
- `EntryPoint.RegisterSystems`: 3 registrations.
- Damage sources: `CollisionSystem`, `ExplosionSystem`, `MeleeSpawnSystem`, `ModifiersSystem`.
