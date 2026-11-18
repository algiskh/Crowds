using LightSide;
using Scene.UI;
using TMPro;
using UnityEngine;

public class PlayerStats : MonoBehaviour
{
	[SerializeField] private ValueBar _healthbar;
	[SerializeField] private ValueBar _shieldbar;
	[SerializeField] private ValueBar _speedbar;
	[Tooltip("Rage (melee strength) bonus bar. Optional: if empty, a copy of the shield bar is created at runtime " +
	         "one step below it (same spacing as speed → shield).")]
	[SerializeField] private ValueBar _strengthbar;
	[SerializeField] private UniText _killsText;

	private void Awake()
	{
		_healthbar.SetMaxValue(100f).SetVisible(true);
		// Bonus bars are normalized to 0..1 (remaining-time fraction) and start empty/hidden.
		_shieldbar.SetMaxValue(1f).ApplyValue(0f).SetText(string.Empty);
		_speedbar.SetMaxValue(1f).ApplyValue(0f).SetText(string.Empty);
		if (_strengthbar == null)
			_strengthbar = CreateStrengthBarFallback();
		_strengthbar?.SetMaxValue(1f).ApplyValue(0f).SetText(string.Empty);
	}

	/// <summary>
	/// Placeholder until a dedicated bar is laid out in the scene: clones the shield bar, places it one row
	/// further (the speed → shield offset, unless a LayoutGroup arranges the children) and tints it red-orange.
	/// </summary>
	private ValueBar CreateStrengthBarFallback()
	{
		if (_shieldbar == null)
			return null;

		var bar = Instantiate(_shieldbar, _shieldbar.transform.parent);
		bar.name = "StrengthBar (auto)";
		bar.transform.SetSiblingIndex(_shieldbar.transform.GetSiblingIndex() + 1);

		var parent = _shieldbar.transform.parent;
		bool hasLayout = parent != null && parent.GetComponent<UnityEngine.UI.LayoutGroup>() != null;
		if (!hasLayout && _speedbar != null &&
		    bar.transform is RectTransform rt &&
		    _shieldbar.transform is RectTransform shieldRt &&
		    _speedbar.transform is RectTransform speedRt)
		{
			Vector2 step = shieldRt.anchoredPosition - speedRt.anchoredPosition;
			if (step.sqrMagnitude < 1f)
				step = new Vector2(0f, -shieldRt.rect.height * 1.2f);
			rt.anchoredPosition = shieldRt.anchoredPosition + step;
		}

		var gradient = new Gradient();
		gradient.SetKeys(
			new[] { new GradientColorKey(new Color(0.75f, 0.1f, 0.05f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.1f), 1f) },
			new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
		bar.SetGradient(gradient);
		return bar;
	}

	public void SetHealthValue(float value)
	{
		_healthbar.ApplyValue(value);
	}

	public void SetKillsText(int count)
	{
		if (_killsText != null)
		{
			_killsText.Text = $"Kills: {count}";
		}
	}

	public void SetBonusValue(BonusType type, float value)
	{
		if (type == BonusType.Speed)
		{
			_speedbar.ApplyValue(value);
		}
		else if (type == BonusType.Shield)
		{
			_shieldbar.ApplyValue(value);
		}
	}

	/// <summary>
	/// Updates the bonus bar: fraction (0..1) — remaining-time fraction (bar fill),
	/// secondsLeft — remaining seconds in the bar's caption (if the bar has a UniText assigned).
	/// </summary>
	public void SetBonus(BonusType type, float fraction, float secondsLeft)
	{
		var bar = GetBonusBar(type);
		if (bar == null)
			return;

		bar.ApplyValue(fraction)
		   .SetText(secondsLeft > 0f ? Mathf.CeilToInt(secondsLeft).ToString() : string.Empty);
	}

	/// <summary>The bonus ended — hide the bar and the caption.</summary>
	public void ClearBonus(BonusType type)
	{
		var bar = GetBonusBar(type);
		bar?.ApplyValue(0f).SetText(string.Empty);
	}

	private ValueBar GetBonusBar(BonusType type)
	{
		if (type == BonusType.Speed) return _speedbar;
		if (type == BonusType.Shield) return _shieldbar;
		if (type == BonusType.Strength) return _strengthbar;
		return null;
	}

	public void SetFragCount(int count)
	{
		_killsText.Text = $"Kills: {count}";
	}
}
