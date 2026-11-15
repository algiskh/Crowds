# Controls Settings (control rebinding)

User key rebinding on top of the Unity Input System, persisted to PlayerPrefs (JSON), with a
dedicated page in the settings menu. In the first phase only keyboard/mouse can be changed; the
gamepad is shown read-only.

## Settings store (`Assets/_Scripts/UI/Settings/ControlBindingsStore.cs`)

Settings are serialized with the Input System's own mechanism — **binding overrides**:
`InputActionAsset.SaveBindingOverridesAsJson()` produces a compact JSON of only the
user-changed bindings, and `LoadBindingOverridesFromJson(json)` applies them back. That is the
"serializable custom settings".

- `IControlSettingsStore` — the store abstraction (`HasData`/`Load`/`Save`/`Clear`). Lets you
  later move the JSON anywhere (file, cloud, backend) by swapping the implementation.
- `PlayerPrefsControlSettingsStore` — the default implementation, key `controls.bindings.v1`.
- `ControlSettings` — static facade:
  - `Apply(asset)` — load the saved JSON and apply it to the asset;
  - `Save(asset)` — serialize the current overrides and write them to the store;
  - `ResetAll(asset)` — remove all overrides and clear the store;
  - `SetStore(store)` — swap the store.

`ControlSettings.Apply` is called in two places so custom bindings work in both scenes:
- `EntryPoint.cs` (gameplay) — right after registering `InputActionsComponent`, before reading
  input;
- `ControlsSettingsMenu.Awake` (menu scene, which has no `EntryPoint`).

`ControlSettings.Save` is called after every successful rebind.

## UI

### `ControlBindingRow.cs` — a row for a single control
View for the row prefab: `UniText` control name (e.g. "Move Left"), a `Button` + `UniText` with
the primary key/mouse button (click starts rebinding), `UniText` `_keyboardAlternativeLabel`
with the alternative key of the same control (e.g. `W` / `UpArrow`, display only), `UniText`
with the gamepad control (display). Gamepad sticks are labeled manually ("Left Stick" / "Right
Stick") because they have no readable display string.

Rebinding flow (`PerformInteractiveRebinding`):
1. click the slot → the slot highlights with `_listeningColor`, the label changes to
   `_listeningText` ("Press a key..."); only one row "listens" at a time (static `s_active`);
2. the player presses a key/mouse button → the binding is applied, `Refresh` updates the label,
   the save callback (`ControlSettings.Save`) is invoked;
3. `Esc` or another click on the slot → cancel, the slot returns to its previous color/text.

Input is restricted to keyboard/mouse via `WithControlsExcluding("<Gamepad>")` (+ excluding
`position`/`delta`/`scroll` and the mouse **`leftButton`** — LMB is used to click the slot,
otherwise the same click is immediately caught by the rebinding); cancel via
`WithCancelingThrough("<Keyboard>/escape")`.
Before `Start()` the action must be `Disable()`d (otherwise `InvalidOperationException`), after
— `Enable()` if needed.
The highlight fields (`_listeningTarget`, `_listeningColor`, `_listeningText`) are optional: if
`_listeningTarget` isn't set, the button's `targetGraphic` is used.

### `ControlsSettingsMenu.cs` — building the list
Builds rows from the `InputActionAsset` (default map — `Player`) into the `Content` of a
`ScrollRect` (Content must carry a `VerticalLayoutGroup`). For each action it **groups** the
keyboard/mouse bindings of one control into a single row: the first is the primary one
(rebindable), the second is the alternative (into the `_keyboardAlternativeLabel` column), the
third+ are ignored. This removes duplicate rows (e.g. `W` and `UpArrow` for "Move Up"). The
grouping key: composite parts — by name (up/down/left/right), single bindings — all into one
group per action. The composite header is skipped. The gamepad column gets the matching binding
of the same action: for a composite part — the part with the same name, otherwise the single
gamepad binding (e.g. `Move` → `leftStick` → "Left Stick"). The Reset button calls
`ControlSettings.ResetAll` and rebuilds the list.

## What to assemble in Unity (editor)

The scripts don't create prefabs/scenes — that's done in the editor:

1. **Row prefab** (`Assets/_Prefabs/UI/Settings/ControlBindingRow.prefab`): root with
   `ControlBindingRow` + `LayoutElement`, inside — `UniText` (name), `Button` with a child
   `UniText` (primary key), `UniText` (alternative key), `UniText` (gamepad). Lay out
   horizontally (e.g. `HorizontalLayoutGroup`). Wire the references in the row's inspector
   (`_nameLabel`, `_keyboardButton`, `_keyboardLabel`, `_keyboardAlternativeLabel`,
   `_gamepadLabel`).
2. **Controls settings page**: `ScrollRect` → `Viewport` → `Content` with a
   `VerticalLayoutGroup` (+ `ContentSizeFitter`, Vertical = Preferred Size). On the object with
   `ControlsSettingsMenu` assign: `_actions` (the same `InputSystem_Actions` as in
   `EntryPoint`), `_scroll`, `_content` (= Content), `_rowPrefab`, optionally `_resetButton`.
3. Open the page from `MainMenuController` (the Settings button already leads to `_settingsPage`)
   — place `ControlsSettingsMenu` inside the Settings Page or as a separate sub-page.

> Important: `ControlsSettingsMenu._actions` must reference the same `InputSystem_Actions` asset
> used in gameplay, otherwise the overrides won't match.
