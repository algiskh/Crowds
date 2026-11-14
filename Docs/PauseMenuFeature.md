# Pause menu

Esc (or gamepad Start) during gameplay pauses the game and opens a menu with **Продолжить** (continue) and
**Главное меню** (main menu). Esc again or Continue closes it.

| Part | File | Role |
|---|---|---|
| `Pause` action | `Assets/InputSystem_Actions.inputactions` (Player map) | `<Keyboard>/escape`, `<Gamepad>/start`. Optional: without it `PauseMenuSystem` falls back to `Keyboard.escapeKey`. Shows up in the controls settings list like the other actions. |
| `PauseMenu` | `_Scripts/UI/PauseMenu.cs` | Runtime-built UGUI overlay (no prefab, like `LoadingScreen`): dim, title, two buttons with `UniText` labels (font borrowed from a scene label), sorting order 900 (below the fail veil 1000). Creates an `EventSystem` + `InputSystemUIInputModule` if the scene has none. Buttons only raise `ContinueRequested` / `MainMenuRequested`. |
| `PauseMenuSystem` | `_Scripts/ECS/PauseMenuSystem.cs` | Owns the menu (`PauseMenuComponent` singleton). Toggle → `RequestPauseComponent` / `RequestUnpauseComponent` with `SignalSource.UI`. Registered right before `PauseSystem`, so the pause applies the same frame. Doesn't open when the game is already paused (win/fail windows), during the fail sequence or while `InputLockComponent.Locked`. Main menu: `Time.timeScale = 1`, `SceneManager.LoadScene("MainMenu")`. |

The pause itself is the existing flag (`PauseStateComponent.IsPaused`, timeScale stays 1): every system already checks
it; the cursor is shown by `AimVisualizerSystem`, the player body freezes via `PlayerAnimator.SetPaused`.

## World freeze

The pause menu pause (`PauseStateComponent.PreviousSource == SignalSource.UI`) freezes the world:
`world.IsWorldFrozen()` / `world.GameDeltaTime()` (`ECS/Extensions.cs`, 0 while frozen) are used by `MoveSystem` and
`FollowSystem` (early return), `BulletSystem`, `BulletOverlapSystem`, `ModifiersSystem` (DoT/buff timers), `LootSystem`
(despawn timers), `EffectsSystem` (lifetimes/delays), `DecalSystem`, `CrowdRenderSystem` (VAT clip time);
`AnimationSystem` skips (its `SetAnimation` would reset `Animator.speed`). `WorldFreezeSystem` (registered last) sets
every playing `Animator` (except the player body — `PlayerAnimator` pauses itself) to speed 0 and pauses playing
`ParticleSystem`s, restoring exactly those on resume. Win/fail pauses (`SignalSource.EndGame`) don't freeze: mobs
finish switching to Idle there. Trails still fade during the freeze (their age is wall-clock).
