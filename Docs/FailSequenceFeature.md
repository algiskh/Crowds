# Fail Sequence (death cinematic) — Reference

When the player dies, the game plays a short staged cinematic instead of stopping instantly:
**block controls → red screen → menu + stop**, 0.5 s per phase. Win still resolves immediately.

## Flow

```
player HP ≤ 0 (DamageSystem) ──► EndGameComponent { isWin=false }
CheckEndSystem (fail branch) ──► FailSequenceComponent.Phase = BlockControls; InputLock.Locked = true
FailSequenceSystem advances, 0.5s each:
  t=0.0  BlockControls : player input ignored (InputLock); MOBS/WORLD KEEP RUNNING
  t=0.5  RedScreen     : red full-screen overlay fades 0 → 0.6 alpha over the phase
  t=1.0  Menu          : RequestPause(EndGame) + GameOverActions.StopAllMoves + open FailWindow
         Done
```

Win path is unchanged: `CheckEndSystem` pauses, opens `WinWindow`, and stops everything in the same
frame via the shared `GameOverActions.StopAllMoves`.

## Why a separate input lock (not the global pause)

Phase 1 must freeze **only the player** while the world keeps moving (zombies still swarm — that's
the drama). The global `PauseStateComponent` stops *every* gameplay system, so it can't be used until
phase 3. `InputLockComponent.Locked` is the player-only gate, honored by `InputSystem` (zeros
move/fire/melee) and `GrenadeThrowSystem`. At phase 3 the lock is released and `PauseStateComponent`
takes over.

Timing uses plain `Time.deltaTime` — the project pauses with a **flag**, not `Time.timeScale`, so
`FailSequenceSystem` keeps ticking through the pause and finishes the sequence.

## Components (`ECS/Components.cs`, `#region FailSequence`)

| Struct | Kind | Notes |
|--------|------|-------|
| `FailSequencePhase` | enum | `Inactive, BlockControls, RedScreen, Menu, Done` |
| `FailSequenceComponent` | singleton | `Phase`, `Timer` |
| `InputLockComponent` | singleton | `bool Locked` — player-only input gate (reusable for cutscenes) |
| `FailScreenOverlayComponent` | singleton | wraps the runtime `FailScreenOverlay` |

## Files

| File | Role |
|------|------|
| `ECS/FailSequenceSystem.cs` | Drives the phases + red fade. Also holds `GameOverActions.StopAllMoves` (shared by win & fail). `PhaseDuration = 0.5f`. Registered after `CheckEndSystem`. |
| `ECS/CheckEndSystem.cs` | Win → immediate stop + WinWindow. Fail → kick off the sequence (guarded so repeated death damage during the cinematic doesn't restart it). |
| `UI/FailScreenOverlay.cs` | Runtime-built full-screen red `Canvas`/`Image` (sortingOrder 1000, `raycastTarget` off so it never eats menu clicks). Created lazily; lives in the active scene → destroyed on restart. |
| `UI/FailWindow.cs` | `Show` bumps its canvas to sortingOrder 1001 (above the red tint). `Restart` resets `Time.timeScale` and reloads the active scene by build index. |
| `ECS/InputSystem.cs`, `ECS/GrenadeThrowSystem.cs` | Honor `InputLock.Locked` alongside `IsPaused`. |

## Tuning

- Phase length: `FailSequenceSystem.PhaseDuration` (0.5 s).
- Red intensity/colour: `RedAlpha` (0.6) and `RedColor` in `FailSequenceSystem`.

## Restart requirement

`FailWindow.Restart` calls `SceneManager.LoadScene(activeScene.buildIndex)` — the gameplay scene
**must be added to Build Settings** or the reload throws. Pause is flag-based (fresh `EcsWorld` on
load), so no other state leaks across a restart.

## Notes / extension

- `InputLockComponent` is generic — reuse it for intros/cutscenes that should freeze the player but
  keep the world live.
- For a fully opaque fade-to-black-then-red, raise `RedAlpha` toward 1 and/or darken `RedColor`.
- No automated tests — verify the 0.5/0.5/0.5 beat and that the menu is clickable in Play mode.
