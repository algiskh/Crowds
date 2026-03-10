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
		_shieldbar.SetMaxValue(1f).SetVisible(true);
		_speedbar.SetMaxValue(1f).SetVisible(true);
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

	public void SetFragCount(int count)
	{
		_killsText.Text = $"Kills: {count}";
	}
}
