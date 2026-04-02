using LightSide;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class WinWindow : MonoBehaviour
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
		SceneManager.LoadScene(SceneManager.GetActiveScene().name);
	}

	public void Show(int score = 0)
	{
		gameObject.SetActive(true);
		_canvas.enabled = true;
		_scoreText.gameObject.SetActive(score > 0);
		_scoreText.Text = $"Total: {score} kills";
	}
}
