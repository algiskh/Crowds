using System.Collections.Generic;
using System.Text;
using UnityEngine.InputSystem;

/// <summary>
/// Replaces action tokens in hint / objective text with the current key names: <c>{Attack}</c> → "ЛКМ",
/// <c>{Melee}</c> → "СКМ", <c>{Move}</c> → "WASD", <c>{Reload}</c> → "R"… Any action of the "Player" map works
/// (the token is the action name). Rebinds are respected (effective paths). Keyboard/mouse bindings are preferred;
/// with a gamepad as the last used device its bindings are used instead. Unknown tokens stay as they are.
/// Used by MissionSystem for DifficultyStage.Objective. See Docs/TutorialLevelFeature.md.
/// </summary>
public static class InputHintFormatter
{
	private static readonly Dictionary<string, string> PrettyControls = new()
	{
		{ "leftButton", "ЛКМ" },
		{ "rightButton", "ПКМ" },
		{ "middleButton", "СКМ (колесо)" },
		{ "space", "Пробел" },
		{ "leftShift", "Shift" },
		{ "rightShift", "Shift" },
		{ "leftCtrl", "Ctrl" },
		{ "escape", "Esc" },
		{ "tab", "Tab" },
		{ "enter", "Enter" },
		{ "upArrow", "↑" },
		{ "downArrow", "↓" },
		{ "leftArrow", "←" },
		{ "rightArrow", "→" },
	};

	public static string Format(string text, InputActionMap map)
	{
		if (string.IsNullOrEmpty(text) || map == null || text.IndexOf('{') < 0)
			return text;

		var sb = new StringBuilder(text.Length + 16);
		int i = 0;
		while (i < text.Length)
		{
			int open = text.IndexOf('{', i);
			if (open < 0)
			{
				sb.Append(text, i, text.Length - i);
				break;
			}
			int close = text.IndexOf('}', open + 1);
			if (close < 0)
			{
				sb.Append(text, i, text.Length - i);
				break;
			}
			sb.Append(text, i, open - i);
			string token = text.Substring(open + 1, close - open - 1);
			var action = map.FindAction(token, throwIfNotFound: false);
			string key = action != null ? KeyName(action) : null;
			if (string.IsNullOrEmpty(key))
				sb.Append(text, open, close - open + 1);
			else
				sb.Append(key);
			i = close + 1;
		}
		return sb.ToString();
	}

	/// <summary>Short display name of the action's binding for the active device family.</summary>
	public static string KeyName(InputAction action)
	{
		bool gamepad = UsesGamepad();
		string best = null;
		var bindings = action.bindings;
		for (int b = 0; b < bindings.Count; b++)
		{
			var binding = bindings[b];
			if (binding.isPartOfComposite)
				continue;
			string name;
			if (binding.isComposite)
			{
				if (!CompositeMatches(bindings, b, gamepad))
					continue;
				name = CompositeName(bindings, b);
			}
			else
			{
				if (IsGamepadPath(binding.effectivePath) != gamepad || !IsSupportedPath(binding.effectivePath, gamepad))
					continue;
				name = ControlName(binding.effectivePath);
			}
			if (string.IsNullOrEmpty(name))
				continue;
			best = name;
			break;
		}
		return best;
	}

	private static bool UsesGamepad()
	{
		var pad = Gamepad.current;
		var keyboard = Keyboard.current;
		if (pad == null)
			return false;
		return keyboard == null || pad.lastUpdateTime > keyboard.lastUpdateTime;
	}

	private static bool IsGamepadPath(string path) =>
		!string.IsNullOrEmpty(path) && (path.StartsWith("<Gamepad>") || path.StartsWith("<XInputController>") ||
		                                path.StartsWith("<DualShockGamepad>"));

	private static bool IsSupportedPath(string path, bool gamepad) =>
		gamepad ? IsGamepadPath(path) : path.StartsWith("<Keyboard>") || path.StartsWith("<Mouse>");

	private static bool CompositeMatches(IReadOnlyList<InputBinding> bindings, int composite, bool gamepad)
	{
		for (int p = composite + 1; p < bindings.Count && bindings[p].isPartOfComposite; p++)
			if (IsSupportedPath(bindings[p].effectivePath, gamepad))
				return true;
		return false;
	}

	// "WASD" for a 2D vector composite (first key of each part, in up/left/down/right order of the classic layout).
	private static string CompositeName(IReadOnlyList<InputBinding> bindings, int composite)
	{
		string up = null, down = null, left = null, right = null;
		for (int p = composite + 1; p < bindings.Count && bindings[p].isPartOfComposite; p++)
		{
			var part = bindings[p];
			string name = ControlName(part.effectivePath);
			switch (part.name)
			{
				case "up": up ??= name; break;
				case "down": down ??= name; break;
				case "left": left ??= name; break;
				case "right": right ??= name; break;
			}
		}
		if (up == null || down == null || left == null || right == null)
			return null;
		return up.Length == 1 && down.Length == 1 && left.Length == 1 && right.Length == 1
			? up + left + down + right
			: $"{up}/{left}/{down}/{right}";
	}

	private static string ControlName(string path)
	{
		if (string.IsNullOrEmpty(path))
			return null;
		int slash = path.LastIndexOf('/');
		string control = slash >= 0 ? path.Substring(slash + 1) : path;
		if (PrettyControls.TryGetValue(control, out var pretty))
			return pretty;
		if (control.Length == 1)
			return control.ToUpperInvariant();
		return InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
	}
}
