using LightSide;
using Scene.UI;
using TMPro;
using UnityEngine;

public class PlayerStats : MonoBehaviour
{
	[SerializeField] private ValueBar _healthbar;
	[SerializeField] private ValueBar _shieldbar;
	[SerializeField] private ValueBar _speedbar;
	[SerializeField] private UniText _killsText;

	private void Awake()
	{
		_healthbar.SetMaxValue(100f).SetVisible(true);
		// Bonus bars are normalized to 0..1 (remaining-time fraction) and start empty/hidden.
		_shieldbar.SetMaxValue(1f).ApplyValue(0f).SetText(string.Empty);
		_speedbar.SetMaxValue(1f).ApplyValue(0f).SetText(string.Empty);
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
		return null;
	}

	public void SetFragCount(int count)
	{
		_killsText.Text = $"Kills: {count}";
	}
}
