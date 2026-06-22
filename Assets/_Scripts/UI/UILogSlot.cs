using System;
using LightSide;
using UnityEngine;

/// <summary>
/// One line in the UI log. Shows a message, holds for its lifetime, then fades its <see cref="CanvasGroup"/>
/// out and reports back via the <c>onDone</c> callback so the owning <see cref="UILogView"/> can recycle it.
/// Self-contained presentation — no ECS involvement.
/// </summary>
public class UILogSlot : MonoBehaviour
{
	[SerializeField] private UniText _text;
	[SerializeField] private CanvasGroup _group;

	private float _holdRemaining;
	private float _fadeDuration;
	private float _fadeRemaining;
	private bool _running;
	private Action<UILogSlot> _onDone;

	public void Show(string message, float lifetime, float fadeDuration, Action<UILogSlot> onDone)
	{
		if (_text != null)
			_text.Text = message;

		_fadeDuration = Mathf.Max(0f, fadeDuration);
		_holdRemaining = Mathf.Max(0f, lifetime - _fadeDuration);
		_fadeRemaining = _fadeDuration;
		_onDone = onDone;
		_running = true;

		if (_group != null)
			_group.alpha = 1f;

		gameObject.SetActive(true);
	}

	/// <summary>Stop and recycle immediately (used when the view evicts the oldest slot on overflow).</summary>
	public void ForceFinish()
	{
		if (_running)
			Finish();
	}

	private void Update()
	{
		if (!_running)
			return;

		if (_holdRemaining > 0f)
		{
			_holdRemaining -= Time.deltaTime;
			return;
		}

		if (_fadeDuration > 0f)
		{
			_fadeRemaining -= Time.deltaTime;
			if (_group != null)
				_group.alpha = Mathf.Clamp01(_fadeRemaining / _fadeDuration);
			if (_fadeRemaining > 0f)
				return;
		}

		Finish();
	}

	private void Finish()
	{
		_running = false;
		var callback = _onDone;
		_onDone = null;
		callback?.Invoke(this);
	}
}
