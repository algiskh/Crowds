# Loading Screen (scene-transition curtain) — Reference

A full-screen "curtain" covers the gap between leaving the menu (or restarting) and the gameplay
level being **fully initialized and rendered by the camera**. Without it the player sees a black/
frozen frame while `EntryPoint` instantiates the level and bakes the NavMesh. The curtain also let
us make the boot **non-blocking**, so the indicator keeps animating instead of freezing.

## Flow

```
MainMenu.Play  /  FailWindow.Restart  /  WinWindow.Restart
   └─ LoadingScreen.Show()                  ← persistent curtain (DontDestroyOnLoad), opaque
   └─ SceneManager.LoadSceneAsync(...)       ← async so the curtain renders BEFORE the heavy load

gameplay scene loads → EntryPoint.Awake:
   LoadingScreen.Show()                      ← idempotent (already up from the caller / editor direct-play)
   InitializeAsync() runs step-by-step, yielding a frame between steps:
     1. LoadLevel()               Instantiate level prefab            → progress 0.2
     2. SetupSpawnData()          pools/player/UI/spawn points        → progress 0.5
     3. RebuildNavMeshAsync()     async NavMesh bake (non-blocking)   → progress 0.8
     4. RegisterSystems()         _initialized = true (systems run)   → progress 1.0
     5. yield frame + WaitForEndOfFrame        ← camera renders the real level
   LoadingScreen.HideAndDestroy()            ← fade out (0.25 s) then Destroy
```

`Update` runs `_systems.Run()` **only after `_initialized`** — the world doesn't tick until the level
is built.

## Why the boot had to become async

The old boot did everything in one synchronous `EntryPoint.Awake` (`LoadLevel` → `SetupSpawnData`
→ `RegisterSystems`). While `Awake` blocks the main thread **nothing renders**, so any curtain would
just freeze on its last frame and jump. Two fixes make the curtain actually animate through the load:

1. **`Awake` no longer blocks** — it creates the world, shows the curtain, and kicks off
   `InitializeAsync().Forget()` (UniTask). Heavy steps are spread across frames with `UniTask.Yield`.
2. **The NavMesh bake is async** — the biggest single-frame hitch. `NavMeshManager.Configure(sectors,
   bake:false)` now only assigns sectors; `RebuildNavMeshAsync()` does the initial bake via
   `NavMeshSurface.UpdateNavMesh(navMeshData)` (returns an `AsyncOperation`, awaited with UniTask).

Runtime sector rebuilds during gameplay (`CheckSectorSystem`) still use the synchronous
`RebuildNavMesh()` — they're incremental and cheap.

The curtain animates on `Time.unscaledDeltaTime`, so it keeps spinning even at `Time.timeScale == 0`
(e.g. restart from the paused fail/win window).

## Files

| File | Role |
|------|------|
| `UI/LoadingScreen.cs` | The curtain. Runtime-built (no prefab), `DontDestroyOnLoad`, canvas `sortingOrder 30000` (above everything), `CanvasGroup.blocksRaycasts` on. Static API `Show()` / `SetProgress(0..1)` / `HideAndDestroy()`. Visuals: rotating **spinner** (builtin *Knob* sprite, `Filled`/`Radial360`), **UniText** "LOADING" with running dots, and a **progress bar**. |
| `ECS/EntryPoint.cs` | `Awake` → `LoadingScreen.Show()` + `InitializeAsync().Forget()`. `Update` gated on `_initialized`. `InitializeAsync` walks the steps, reports progress, and hides the curtain after `WaitForEndOfFrame`. |
| `NavMeshManager.cs` | `Configure(sectors, bake = true)` — pass `bake:false` to skip the sync bake. `RebuildNavMeshAsync()` returns the bake `AsyncOperation`. |
| `UI/MainMenuController.cs` | `Play` → `LoadingScreen.Show()` + `LoadSceneAsync`. |
| `UI/FailWindow.cs`, `WinWindow.cs` | `Restart` → `LoadingScreen.Show()` + `LoadSceneAsync`. |

## UniText at runtime (init-order caveat)

A `UniText` added by code does **not** auto-assign its font (that auto-fill is editor-only), so
`LoadingScreen` pulls `UniTextSettings.DefaultFontStack` / `DefaultAppearance` from the project's
`UniTextSettings.asset` (under `Assets/UniText/Resources/`). Both must stay assigned there or the
label renders nothing.

The `Appearance` **setter** touches the internal `fontProvider`, which is `null` until the
component's first rebuild — yet the renderer needs `Appearance` non-null on frame one
(`UniTextFontProvider.GetMaterials` dereferences it). The setter writes its backing field *before*
that null deref, so `LoadingScreen` assigns `Appearance` inside a `catch (NullReferenceException)`:
the field lands, the provider picks it up when it's built next frame, and the expected NRE is
swallowed. See the comment in `LoadingScreen.Build()`.

*Alternative if that internal dependency ever breaks:* make `LoadingScreen` a prefab under a
`Resources/` folder (wire the `UniText` + spinner in the editor) and have `Show()` do
`Resources.Load` + `Instantiate` instead of building the UI in code.

## Requirements

- **Both scenes in Build Settings** — `SampleScene` (loaded by name from the menu) and the reload
  targets (`FailWindow` by build index, `WinWindow` by name). A missing scene makes `LoadSceneAsync`
  throw.
- `UniTextSettings.asset` must keep a **default font stack + appearance** assigned (see above).

## Custom spinner sprite

The spinner uses your own sprite if one is found in a `Resources` folder at the path in
`SpinnerResourcePath` (default `"UI/LoadingSpinner"`), e.g. put the image at
`Assets/_Art/Resources/UI/LoadingSpinner.png` and set its **Texture Type = Sprite (2D and UI)**.
A custom sprite is rotated **as a whole** (`Image.Type.Simple`, `preserveAspect`), so use art with a
visible head/gradient/gap. If no sprite is found, it falls back to the builtin *Knob* circle with a
rotating radial-fill gap — nothing breaks either way.

## Tuning (`UI/LoadingScreen.cs`)

- `SpinnerResourcePath` (`"UI/LoadingSpinner"`) — Resources path of the custom spinner sprite.
- `SpinnerDegreesPerSecond` (220) — spin speed. Spinner size: the `_spinnerRect.sizeDelta` line (96×96).
- `SortingOrder` (30000) — keep above any in-game canvas.
- `FadeOutDuration` (0.25 s) — reveal fade.
- Progress milestones (0.2 / 0.5 / 0.8 / 1.0) are set in `EntryPoint.InitializeAsync`.

## Notes

- `LoadingScreen.Show()` is idempotent and the object survives scene loads, so the menu and the new
  scene's `EntryPoint` can both call it without double-spawning; whoever finishes init calls
  `HideAndDestroy()`.
- The level `Instantiate` and `PrewarmMobPool` are still single-frame operations (the spinner may
  briefly pause on them), but the screen stays covered. If they become a visible stall, chunk mob
  prewarm/spawning across frames next — the async NavMesh bake already removed the largest hitch.
- No automated tests — verify in Play mode: menu → level, and a fail/win → restart. Confirm the
  spinner spins, the UniText label renders, and the curtain fades only after the level is visible.
