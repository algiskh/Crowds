using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Главное меню: строит список уровней из LevelLibrary, по клику запоминает выбор в GameSession
// и грузит геймплейную сцену. EntryPoint в геймплейной сцене читает GameSession.SelectedLevel.
public class MainMenuController : MonoBehaviour
{
	[SerializeField, Required, BoxGroup("Data")] private LevelLibrary _levelLibrary;
	[SerializeField, BoxGroup("Data"),
	 Tooltip("Имя геймплейной сцены (обязательно добавить в Build Settings).")]
	private string _gameplaySceneName = "SampleScene";

	[SerializeField, BoxGroup("Level list")] private Transform _buttonsContainer;
	[SerializeField, BoxGroup("Level list")] private LevelSelectButton _buttonPrefab;

	[SerializeField, BoxGroup("Optional")] private Button _quickPlayButton; // грузит первый уровень
	[SerializeField, BoxGroup("Optional")] private Button _quitButton;

	private void Start()
	{
		BuildLevelList();

		if (_quickPlayButton != null)
			_quickPlayButton.onClick.AddListener(OnQuickPlay);
		if (_quitButton != null)
			_quitButton.onClick.AddListener(Application.Quit);
	}

	private void OnQuickPlay()
	{
		var level = _levelLibrary != null ? _levelLibrary.First : null;
		if (level == null)
			Debug.LogWarning("[MainMenu] Quick Play: в LevelLibrary нет ни одного уровня.");
		Play(level);
	}

	private void BuildLevelList()
	{
		if (_levelLibrary == null || _buttonsContainer == null || _buttonPrefab == null)
		{
			Debug.LogWarning("[MainMenu] Не заданы LevelLibrary / Buttons Container / Button Prefab — " +
				"список уровней не построен.");
			return;
		}

		foreach (var level in _levelLibrary.Levels)
		{
			if (level == null)
				continue;

			var button = Instantiate(_buttonPrefab, _buttonsContainer);
			button.Setup(level, Play);
		}
	}

	private void Play(LevelDefinition level)
	{
		if (level == null)
		{
			Debug.LogError("[MainMenu] Уровень не выбран / не задан в LevelLibrary.");
			return;
		}

		GameSession.Select(level);
		Time.timeScale = 1f;
		SceneManager.LoadScene(_gameplaySceneName);
	}
}
