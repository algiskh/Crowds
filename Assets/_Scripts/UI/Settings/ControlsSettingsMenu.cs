using System.Collections.Generic;
using System.Text;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.Settings
{
	// Меню настроек управления. Строит список строк (ControlBindingRow) из InputActionAsset
	// и раскладывает их в Content у ScrollRect (Content должен нести VerticalLayoutGroup).
	// На первом этапе показываются и перепривязываются только биндинги клавиатуры/мыши;
	// колонка геймпада заполняется соответствующим биндингом «только для показа».
	public class ControlsSettingsMenu : MonoBehaviour
	{
		[Title("Input")]
		[SerializeField, Required] private InputActionAsset _actions;
		[SerializeField] private string _actionMapName = "Player";

		[Title("List")]
		[SerializeField] private ScrollRect _scroll;
		[SerializeField, Required, Tooltip("Content у ScrollRect с VerticalLayoutGroup.")]
		private Transform _content;
		[SerializeField, Required] private ControlBindingRow _rowPrefab;

		[Title("Buttons")]
		[SerializeField] private Button _resetButton;

		private readonly List<ControlBindingRow> _rows = new();

		private void Awake()
		{
			// Меню открывается в сцене главного меню, где нет EntryPoint — применяем сохранённые
			// оверрайды сами, чтобы список сразу показал актуальные (кастомные) биндинги.
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
				Debug.LogWarning("[ControlsSettings] Не заданы InputActionAsset / Content / Row Prefab — " +
					"список управления не построен.");
				return;
			}

			var map = _actions.FindActionMap(_actionMapName, throwIfNotFound: false);
			if (map == null)
			{
				Debug.LogWarning($"[ControlsSettings] Action map '{_actionMapName}' не найдена.");
				return;
			}

			foreach (var action in map.actions)
			{
				// Группируем клавиатурные/мышиные биндинги одного «контрола» в одну строку:
				// первый — основной (перепривязывается), второй — альтернативный (напр. W / UpArrow),
				// показывается в отдельной колонке. Так убираются строки-дубликаты.
				var groups = new List<Group>();
				var byKey = new Dictionary<string, Group>();

				var bindings = action.bindings;
				for (int i = 0; i < bindings.Count; i++)
				{
					var binding = bindings[i];

					// Композитный «заголовок» (напр. WASD) сам по себе не привязывается — пропускаем.
					if (binding.isComposite)
						continue;

					// На первом этапе — только строки клавиатуры/мыши.
					if (!IsKeyboardMouse(binding.effectivePath))
						continue;

					// Оси-«движения» (Look -> Mouse delta, position, scroll) не перепривязываются
					// как клавиши — их из списка исключаем.
					if (IsPointerMotion(binding.effectivePath))
						continue;

					// Ключ группировки: часть композита — по её имени (up/down/left/right),
					// одиночный биндинг — все в одну группу экшена.
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
					// Третий+ биндинг того же контрола игнорируем (места под альтернативу — одно).
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

			// На случай, если в Content остались чужие/старые дети.
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
			// Пользователь перепривязал клавишу — сохраняем весь набор оверрайдов в JSON.
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

		// Непривязываемые оси указателя: движение мыши (Look), скролл, позиция.
		private static bool IsPointerMotion(string path) =>
			path.EndsWith("/delta") || path.EndsWith("/position") || path.EndsWith("/scroll");

		private static bool IsGamepad(string path) =>
			path.StartsWith("<Gamepad>") || path.StartsWith("<XInputController>") ||
			path.StartsWith("<DualShock") || path.StartsWith("<DualSense");

		// Ищем «геймпадный аналог» биндинга для колонки показа.
		// Для части композита — сперва часть с тем же именем (up/down/left/right),
		// иначе одиночный геймпадный биндинг экшена (напр. Move -> leftStick "Left Stick").
		// Для одиночного биндинга — первый одиночный геймпадный биндинг того же экшена.
		private static int FindGamepadCounterpart(InputAction action, int keyboardIndex)
		{
			var bindings = action.bindings;
			var keyboardBinding = bindings[keyboardIndex];

			// Точное совпадение части композита.
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

			// Фолбэк / одиночный биндинг — первый одиночный геймпадный биндинг экшена.
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

		// "Move" + часть "left" -> "Move Left"; "Reload" -> "Reload".
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

		// Первая буква — заглавная, вставляем пробелы перед внутренними заглавными (camelCase).
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
