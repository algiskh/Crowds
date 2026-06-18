using LightSide;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class FailWindow : MonoBehaviour
{
	[SerializeField] private Button _restartButton;
	[SerializeField] private Button _quitButton;
	[SerializeField] private Canvas _canvas;
	[SerializeField] private UniText _scoreText;
	public void Awake()
	{
		_quitButton.onClick.AddListener(OnPressQuit);
		_restartButton.onClick.AddListener(OnPressRestart);
	}

	private void OnPressQuit()
	{
		Application.Quit();
	}

	private void OnPressRestart()
	{
		// Пауза у нас реализована флагом, а не timeScale, но сбрасываем на всякий случай,
		// чтобы перезагруженная сцена точно стартовала «живой».
		Time.timeScale = 1f;
		SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
	}

	public void Show(int score = 0)
	{
		gameObject.SetActive(true);
		_canvas.enabled = true;
		// Рисуем окно поверх красной пелены концовки (её Canvas имеет sortingOrder 1000).
		_canvas.overrideSorting = true;
		_canvas.sortingOrder = 1001;
		_scoreText.gameObject.SetActive(score > 0);
		_scoreText.Text = $"Total: {score} kills";
	}
}
