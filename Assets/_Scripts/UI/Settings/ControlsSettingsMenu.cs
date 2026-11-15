using System.Collections.Generic;
using System.Text;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.Settings
{
	// Control settings menu. Builds a list of rows (ControlBindingRow) from the InputActionAsset
	// and lays them out into the ScrollRect's Content (Content must carry a VerticalLayoutGroup).
	// In the first phase only keyboard/mouse bindings are shown and rebound;
	// the gamepad column is filled with the matching binding "for display only".
	public class ControlsSettingsMenu : MonoBehaviour
	{
		[Title("Input")]
		[SerializeField, Required] private InputActionAsset _actions;
		[SerializeField] private string _actionMapName = "Player";

		[Title("List")]
		[SerializeField] private ScrollRect _scroll;
		[SerializeField, Required, Tooltip("The ScrollRect's Content with a VerticalLayoutGroup.")]
		private Transform _content;
		[SerializeField, Required] private ControlBindingRow _rowPrefab;

		[Title("Buttons")]
		[SerializeField] private Button _resetButton;

		private readonly List<ControlBindingRow> _rows = new();

		private void Awake()
		{
			// The menu opens in the main menu scene, which has no EntryPoint — we apply the saved
			// overrides ourselves so the list immediately shows the actual (custom) bindings.
			ControlSettings.Apply(_actions);
		}

		private void OnEnable()
		{
			Build();

			if (_resetButton != null)
			{
				_resetButton.onClick.RemoveListener(ResetAll);
				_resetButton.onClick.AddListener(ResetAll);
			}
		}

		#region BUILD
		private void Build()
		{
			Clear();

			if (_actions == null || _content == null || _rowPrefab == null)
			{
				Debug.LogWarning("[ControlsSettings] InputActionAsset / Content / Row Prefab not assigned — " +
					"control list not built.");
				return;
			}

			var map = _actions.FindActionMap(_actionMapName, throwIfNotFound: false);
			if (map == null)
			{
				Debug.LogWarning($"[ControlsSettings] Action map '{_actionMapName}' not found.");
				return;
			}

			foreach (var action in map.actions)
			{
				// Group the keyboard/mouse bindings of one "control" into a single row:
				// the first is primary (rebindable), the second is alternative (e.g. W / UpArrow),
				// shown in a separate column. This removes duplicate rows.
				var groups = new List<Group>();
				var byKey = new Dictionary<string, Group>();

				var bindings = action.bindings;
				for (int i = 0; i < bindings.Count; i++)
				{
					var binding = bindings[i];

					// The composite "header" (e.g. WASD) isn't bound by itself — skip it.
					if (binding.isComposite)
						continue;

					// First phase — keyboard/mouse rows only.
					if (!IsKeyboardMouse(binding.effectivePath))
						continue;

					// Pointer "motion" axes (Look -> Mouse delta, position, scroll) aren't rebound
					// as keys — exclude them from the list.
					if (IsPointerMotion(binding.effectivePath))
						continue;

					// Grouping key: composite part — by its name (up/down/left/right),
					// single binding — all into one group per action.
					string key = binding.isPartOfComposite ? "part:" + binding.name : "single";

					if (!byKey.TryGetValue(key, out var group))
					{
						group = new Group
						{
							Name = BuildDisplayName(action.name, binding),
							PrimaryIndex = i,
							AlternativeIndex = -1,
							GamepadIndex = FindGamepadCounterpart(action, i),
						};
						byKey.Add(key, group);
						groups.Add(group);
					}
					else if (group.AlternativeIndex < 0)
					{
						group.AlternativeIndex = i;
					}
					// Third+ binding of the same control is ignored (there's room for one alternative).
				}

				foreach (var group in groups)
				{
					var row = Instantiate(_rowPrefab, _content);
					row.Setup(group.Name, action, group.PrimaryIndex, group.AlternativeIndex,
						group.GamepadIndex, OnBindingChanged);
					_rows.Add(row);
				}
			}

			if (_scroll != null)
				_scroll.verticalNormalizedPosition = 1f;
		}

		private void Clear()
		{
			for (int i = _rows.Count - 1; i >= 0; i--)
			{
				if (_rows[i] != null)
					Destroy(_rows[i].gameObject);
			}
			_rows.Clear();

			// In case foreign/stale children remain in the Content.
			if (_content != null)
			{
				for (int i = _content.childCount - 1; i >= 0; i--)
					Destroy(_content.GetChild(i).gameObject);
			}
		}
		#endregion

		#region CALLBACKS
		private void OnBindingChanged()
		{
			// The user rebound a key — save the whole override set to JSON.
			ControlSettings.Save(_actions);
		}

		private void ResetAll()
		{
			ControlSettings.ResetAll(_actions);
			Build();
		}
		#endregion

		#region HELPERS
		private static bool IsKeyboardMouse(string path) =>
			path.StartsWith("<Keyboard>") || path.StartsWith("<Mouse>") ||
			path.StartsWith("<Pointer>") || path.StartsWith("<Pen>");

		// Non-rebindable pointer axes: mouse movement (Look), scroll, position.
		private static bool IsPointerMotion(string path) =>
			path.EndsWith("/delta") || path.EndsWith("/position") || path.EndsWith("/scroll");

		private static bool IsGamepad(string path) =>
			path.StartsWith("<Gamepad>") || path.StartsWith("<XInputController>") ||
			path.StartsWith("<DualShock") || path.StartsWith("<DualSense");

		// Find the "gamepad counterpart" of a binding for the display column.
		// For a composite part — first the part with the same name (up/down/left/right),
		// otherwise the action's single gamepad binding (e.g. Move -> leftStick "Left Stick").
		// For a single binding — the first single gamepad binding of the same action.
		private static int FindGamepadCounterpart(InputAction action, int keyboardIndex)
		{
			var bindings = action.bindings;
			var keyboardBinding = bindings[keyboardIndex];

			// Exact composite-part match.
			if (keyboardBinding.isPartOfComposite)
			{
				for (int j = 0; j < bindings.Count; j++)
				{
					var b = bindings[j];
					if (b.isComposite || !IsGamepad(b.effectivePath) || !b.isPartOfComposite)
						continue;

					if (string.Equals(b.name, keyboardBinding.name, System.StringComparison.OrdinalIgnoreCase))
						return j;
				}
			}

			// Fallback / single binding — the first single gamepad binding of the action.
			for (int j = 0; j < bindings.Count; j++)
			{
				var b = bindings[j];
				if (b.isComposite || b.isPartOfComposite || !IsGamepad(b.effectivePath))
					continue;

				return j;
			}

			return -1;
		}

		private class Group
		{
			public string Name;
			public int PrimaryIndex;
			public int AlternativeIndex;
			public int GamepadIndex;
		}

		// "Move" + part "left" -> "Move Left"; "Reload" -> "Reload".
		private static string BuildDisplayName(string actionName, InputBinding binding)
		{
			var sb = new StringBuilder();
			AppendPretty(sb, actionName);

			if (binding.isPartOfComposite && !string.IsNullOrEmpty(binding.name))
			{
				sb.Append(' ');
				AppendPretty(sb, binding.name);
			}

			return sb.ToString();
		}

		// First letter uppercase, insert spaces before internal capitals (camelCase).
		private static void AppendPretty(StringBuilder sb, string raw)
		{
			if (string.IsNullOrEmpty(raw))
				return;

			for (int i = 0; i < raw.Length; i++)
			{
				char c = raw[i];
				if (i == 0)
					sb.Append(char.ToUpperInvariant(c));
				else
				{
					if (char.IsUpper(c) && !char.IsUpper(raw[i - 1]))
						sb.Append(' ');
					sb.Append(c);
				}
			}
		}
		#endregion
	}
}
