using System;
using LightSide;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.Settings
{
	// View of a single control-settings row in the list (ScrollRect -> Content with VerticalLayoutGroup).
	// On the left — the control name (e.g. "Move Left"), then the assigned key/mouse button
	// (clickable — starts rebinding), the alternative key and the gamepad control.
	//
	// Rebinding flow:
	//   1) click the slot -> the slot highlights ("listening"), the label shows "Press a key...";
	//   2) the player presses a key/mouse button -> it's applied, the label updates, saving is invoked;
	//   3) Esc or another click -> cancel, the slot returns to its previous state.
	// In the first phase only keyboard/mouse is rebound; the gamepad column is display-only.
	public class ControlBindingRow : MonoBehaviour
	{
		[SerializeField] private UniText _nameLabel;
		[SerializeField] private Button _keyboardButton;
		[SerializeField] private UniText _keyboardLabel;
		[SerializeField] private UniText _keyboardAlternativeLabel;
		[SerializeField] private UniText _gamepadLabel;

		[Header("Listening feedback")]
		[Tooltip("The slot graphic highlighted while waiting for a key. " +
			"If not assigned — the button's targetGraphic is used.")]
		[SerializeField] private Graphic _listeningTarget;
		[SerializeField] private Color _listeningColor = new Color(1f, 0.55f, 0.15f, 1f);
		[SerializeField] private string _listeningText = "Press a key...";

		private const string EmptyText = "—";

		// Only one row "listens" at a time across the whole menu.
		private static ControlBindingRow s_active;

		private InputAction _action;
		private int _keyboardBindingIndex = -1;
		private int _keyboardAlternativeBindingIndex = -1;
		private int _gamepadBindingIndex = -1;
		private Action _onChanged;
		private InputActionRebindingExtensions.RebindingOperation _rebind;
		private bool _actionWasEnabled;
		private int _lastFinishFrame = -1;

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

		// Sticks have no readable display string in the Input System — label them manually.
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
			// A repeated click on a "listening" slot — cancel (the slot returns to showing the current key).
			if (_rebind != null)
				CancelRebind();
			else
				StartRebind();
		}

		private void StartRebind()
		{
			if (_action == null || _keyboardBindingIndex < 0)
				return;

			// Guard against a repeated onClick in the same frame (double click event): don't let
			// re-entering the listening state happen in the frame where the rebind was just canceled.
			if (Time.frameCount == _lastFinishFrame)
				return;

			// Interrupt the rebind started in another row.
			if (s_active != null && s_active != this)
				s_active.CancelRebind();

			s_active = this;

			// During interactive rebinding the action MUST be disabled:
			// PerformInteractiveRebinding().Start() throws InvalidOperationException if it's enabled.
			_actionWasEnabled = _action.enabled;
			_action.Disable();

			EnterListeningState();

			_rebind = _action.PerformInteractiveRebinding(_keyboardBindingIndex)
				// First phase — keyboard and mouse only: exclude the gamepad and "noisy" mouse axes.
				.WithControlsExcluding("<Gamepad>")
				.WithControlsExcluding("<Mouse>/position")
				.WithControlsExcluding("<Mouse>/delta")
				.WithControlsExcluding("<Mouse>/scroll")
				// LMB is the click that opens the slot. If we don't exclude it, that same click
				// is immediately "caught" by the rebinding (the slot doesn't enter the listening state the first time).
				.WithControlsExcluding("<Mouse>/leftButton")
				.WithCancelingThrough("<Keyboard>/escape")
				.OnComplete(_ =>
				{
					FinishRebind();
					_onChanged?.Invoke();
				})
				.OnCancel(_ => FinishRebind())
				.Start();
		}

		// Explicit cancel (repeated click on the slot / switching to another row): kill the operation and
		// synchronously return the slot to showing the current key, not relying on the callback alone.
		private void CancelRebind()
		{
			if (_rebind == null)
				return;

			var op = _rebind;
			_rebind = null; // so the OnCancel callback doesn't go a second round
			op.Cancel();
			op.Dispose();
			FinishRebind();
		}

		private void FinishRebind()
		{
			_rebind?.Dispose();
			_rebind = null;
			if (s_active == this)
				s_active = null;

			if (_actionWasEnabled && _action != null)
				_action.Enable();

			_lastFinishFrame = Time.frameCount;

			ExitListeningState();
			Refresh(); // always return the label to the actual key
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
			// Don't leave a dangling operation (and a disabled action) if the row was disabled/destroyed
			// during rebinding.
			CancelRebind();
		}
	}
}
