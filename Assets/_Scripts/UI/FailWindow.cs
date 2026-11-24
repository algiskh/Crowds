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

	// "Exit" returns to the main menu (it used to call Application.Quit: nothing in the editor, the whole game
	// closed in a build). Same as the pause menu: timeScale reset, plain load — the main menu has no EntryPoint to
	// lift a loading curtain.
	private void OnPressQuit()
	{
		Time.timeScale = 1f;
		SceneManager.LoadScene(ECS.PauseMenuSystem.MainMenuScene);
	}

	private void OnPressRestart()
	{
		// Pause is implemented via a flag, not timeScale, but we reset it just in case,
		// so the reloaded scene definitely starts "alive".
		Time.timeScale = 1f;
		// Loading curtain for the level restart (EntryPoint lifts it once the scene is ready).
		LoadingScreen.Show();
		SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().buildIndex);
	}

	public void Show(int score = 0)
	{
		gameObject.SetActive(true);
		_canvas.enabled = true;
		// Draw the window above the fail sequence's red veil (its Canvas has sortingOrder 1000).
		_canvas.overrideSorting = true;
		_canvas.sortingOrder = 1001;
		_scoreText.gameObject.SetActive(score > 0);
		_scoreText.Text = $"Total: {score} kills";
	}
}
