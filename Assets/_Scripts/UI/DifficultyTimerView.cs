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
		_image.enabled = true;
		_timerText.enabled = true;
		_levelText.text = level.ToString().ToUpperInvariant();
		ShowSeconds(seconds);
	}

	public void Hide()
	{
		_image.enabled = false;
		_timerText.enabled = false;
	}

	private void ShowSeconds(float seconds)
	{
		var timeSpan = System.TimeSpan.FromSeconds(seconds);
		_timerText.text = timeSpan.ToString(@"hh\:mm\:ss");
	}
}
