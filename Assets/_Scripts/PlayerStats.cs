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
		// Бары бонусов нормированы в 0..1 (доля оставшегося времени) и стартуют пустыми/скрытыми.
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
	/// Обновляет бар бонуса: fraction (0..1) — доля оставшегося времени (заполнение бара),
	/// secondsLeft — оставшиеся секунды в подписи бара (если у бара назначен UniText).
	/// </summary>
	public void SetBonus(BonusType type, float fraction, float secondsLeft)
	{
		var bar = GetBonusBar(type);
		if (bar == null)
			return;

		bar.ApplyValue(fraction)
		   .SetText(secondsLeft > 0f ? Mathf.CeilToInt(secondsLeft).ToString() : string.Empty);
	}

	/// <summary>Бонус закончился — прячем бар и подпись.</summary>
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
