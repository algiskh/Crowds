using System;
using LightSide;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.Settings
{
	// Вью одной строки настроек управления в списке (ScrollRect -> Content с VerticalLayoutGroup).
	// Слева — имя контрола (напр. "Move Left"), затем назначенная клавиша/мышь
	// (кликабельно — запускает перепривязку), альтернативная клавиша и контрол геймпада.
	//
	// Флоу перепривязки:
	//   1) клик по слоту -> слот подсвечивается ("listening"), надпись "Press a key...";
	//   2) игрок жмёт клавишу/кнопку мыши -> она применяется, надпись обновляется, вызывается сохранение;
	//   3) Esc или повторный клик -> отмена, слот возвращается к прежнему виду.
	// На первом этапе перепривязывается только клавиатура/мышь; колонка геймпада — только показ.
	public class ControlBindingRow : MonoBehaviour
	{
		[SerializeField] private UniText _nameLabel;
		[SerializeField] private Button _keyboardButton;
		[SerializeField] private UniText _keyboardLabel;
		[SerializeField] private UniText _keyboardAlternativeLabel;
		[SerializeField] private UniText _gamepadLabel;

		[Header("Listening feedback")]
		[Tooltip("Графика слота, подсвечиваемая во время ожидания клавиши. " +
			"Если не задана — берётся targetGraphic кнопки.")]
		[SerializeField] private Graphic _listeningTarget;
		[SerializeField] private Color _listeningColor = new Color(1f, 0.55f, 0.15f, 1f);
		[SerializeField] private string _listeningText = "Press a key...";

		private const string EmptyText = "—";

		// Одновременно «слушает» только одна строка на всё меню.
		private static ControlBindingRow s_active;

		private InputAction _action;
		private int _keyboardBindingIndex = -1;
		private int _keyboardAlternativeBindingIndex = -1;
		private int _gamepadBindingIndex = -1;
		private Action _onChanged;
		private InputActionRebindingExtensions.RebindingOperation _rebind;

		private Graphic Highlight => _listeningTarget != null ? _listeningTarget : _keyboardButton?.targetGraphic;
		private Color _restColor;
		private bool _restColorCached;

		public void Setup(string displayName, InputAction action, int keyboardBindingIndex,
			int keyboardAlternativeBindingIndex, int gamepadBindingIndex, Action onChanged)
		{
			_action = action;
			_keyboardBindingIndex = keyboardBindingIndex;
			_keyboardAlternativeBindingIndex = keyboardAlternativeBindingIndex;
			_gamepadBindingIndex = gamepadBindingIndex;
			_onChanged = onChanged;

			if (_nameLabel != null)
				_nameLabel.Text = displayName;

			CacheRestColor();

			if (_keyboardButton != null)
			{
				_keyboardButton.onClick.RemoveListener(ToggleRebind);
				_keyboardButton.interactable = keyboardBindingIndex >= 0;
				if (keyboardBindingIndex >= 0)
					_keyboardButton.onClick.AddListener(ToggleRebind);
			}

			Refresh();
		}

		public void Refresh()
		{
			if (_keyboardLabel != null)
				_keyboardLabel.Text = _keyboardBindingIndex >= 0 ? DisplayString(_keyboardBindingIndex) : EmptyText;

			if (_keyboardAlternativeLabel != null)
				_keyboardAlternativeLabel.Text =
					_keyboardAlternativeBindingIndex >= 0 ? DisplayString(_keyboardAlternativeBindingIndex) : EmptyText;

			if (_gamepadLabel != null)
				_gamepadLabel.Text = _gamepadBindingIndex >= 0 ? GamepadDisplayString(_gamepadBindingIndex) : EmptyText;
		}

		private string DisplayString(int bindingIndex) =>
			_action.GetBindingDisplayString(bindingIndex, InputBinding.DisplayStringOptions.DontIncludeInteractions);

		// Стики не имеют читаемого display-string у Input System — подписываем их вручную.
		private string GamepadDisplayString(int bindingIndex)
		{
			string path = _action.bindings[bindingIndex].effectivePath;
			if (path.EndsWith("leftStick"))
				return "Left Stick";
			if (path.EndsWith("rightStick"))
				return "Right Stick";
			return DisplayString(bindingIndex);
		}

		#region REBIND
		private void ToggleRebind()
		{
			// Повторный клик по «слушающему» слоту — отмена.
			if (_rebind != null)
			{
				_rebind.Cancel();
				return;
			}

			StartRebind();
		}

		private void StartRebind()
		{
			if (_action == null || _keyboardBindingIndex < 0)
				return;

			// Прервать перепривязку, начатую в другой строке.
			if (s_active != null && s_active != this)
				s_active._rebind?.Cancel();

			s_active = this;

			// На время интерактивной перепривязки экшен должен быть отключён
			// (Start() делает это сам, но зафиксируем прежнее состояние для восстановления).
			bool wasEnabled = _action.enabled;

			EnterListeningState();

			_rebind = _action.PerformInteractiveRebinding(_keyboardBindingIndex)
				// Первый этап — только клавиатура и мышь: исключаем геймпад и «шумные» оси мыши.
				.WithControlsExcluding("<Gamepad>")
				.WithControlsExcluding("<Mouse>/position")
				.WithControlsExcluding("<Mouse>/delta")
				.WithControlsExcluding("<Mouse>/scroll")
				.WithCancelingThrough("<Keyboard>/escape")
				.OnComplete(op =>
				{
					FinishRebind(wasEnabled);
					Refresh();
					_onChanged?.Invoke();
				})
				.OnCancel(op =>
				{
					FinishRebind(wasEnabled);
					Refresh();
				})
				.Start();
		}

		private void FinishRebind(bool reEnableAction)
		{
			_rebind?.Dispose();
			_rebind = null;
			if (s_active == this)
				s_active = null;

			if (reEnableAction && _action != null)
				_action.Enable();

			ExitListeningState();
		}

		private void EnterListeningState()
		{
			if (_keyboardLabel != null)
				_keyboardLabel.Text = _listeningText;

			var g = Highlight;
			if (g != null)
			{
				CacheRestColor();
				g.color = _listeningColor;
			}
		}

		private void ExitListeningState()
		{
			var g = Highlight;
			if (g != null && _restColorCached)
				g.color = _restColor;
		}

		private void CacheRestColor()
		{
			if (_restColorCached)
				return;

			var g = Highlight;
			if (g != null)
			{
				_restColor = g.color;
				_restColorCached = true;
			}
		}
		#endregion

		private void OnDisable()
		{
			// Не оставляем висящую операцию, если строку выключили/уничтожили во время перепривязки.
			if (_rebind != null)
			{
				_rebind.Dispose();
				_rebind = null;
				if (s_active == this)
					s_active = null;
				ExitListeningState();
			}
		}
	}
}
