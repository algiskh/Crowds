using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DifficultyTimerView : MonoBehaviour
{
	[SerializeField] private Image _image;
	[SerializeField] private TMP_Text _timerText;
	[SerializeField] private TMP_Text _levelText;

	public bool IsActive => _image.enabled;

	public void UpdateView(float fill, float seconds)
	{
		_image.fillAmount = fill;
		ShowSeconds(seconds);
	}

	public void Show(DifficultyLevel level, float seconds)
	{
		Debug.Log($"{nameof(DifficultyTimerView)}: Show level {level}, seconds {seconds}");
		_image.fillAmount = 1;
		_image.enabled = true;
		_timerText.enabled = true;
		_levelText.text = level.ToString().ToUpperInvariant();
		ShowSeconds(seconds);
	}

	public void Hide()
	{
		Debug.Log($"{nameof(DifficultyTimerView)}: Hide");
		_image.enabled = false;
		_timerText.enabled = false;
	}

	private void ShowSeconds(float seconds)
	{
		if (seconds < 0)
		{
			_timerText.text = "00:00:00";
			return;
		}
		var timeSpan = System.TimeSpan.FromSeconds(seconds);
		_timerText.text = timeSpan.ToString(@"hh\:mm\:ss");
	}
}
