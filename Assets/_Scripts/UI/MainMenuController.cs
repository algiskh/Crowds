using System;
using LightSide;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Главное меню с несколькими страницами.
// Стартовая страница: Campaign / Survival / Settings / Exit.
// Campaign и Survival открывают одну и ту же страницу со скролл-списком превью уровней
// (LevelSelectButton в Content у ScrollRect), но наполняют её из своей LevelLibrary.
// По клику уровень и режим запоминаются в GameSession, после чего грузится геймплейная
// сцена — EntryPoint там читает GameSession.SelectedLevel.
public class MainMenuController : MonoBehaviour
{
	#region FIELDS
	[Title("Scenes")]
	[SerializeField, BoxGroup("Scenes"),
	 Tooltip("Имя геймплейной сцены (обязательно добавить в Build Settings).")]
	private string _gameplaySceneName = "SampleScene";

	[Title("Libraries")]
	[SerializeField, Required, BoxGroup("Libraries"),
	 Tooltip("Уровни кампании.")]
	private LevelLibrary _campaignLibrary;
	[SerializeField, Required, BoxGroup("Libraries"),
	 Tooltip("Уровни режима выживания.")]
	private LevelLibrary _survivalLibrary;

	[Title("Pages")]
	[SerializeField, Required, BoxGroup("Pages")] private GameObject _mainPage;
	[SerializeField, Required, BoxGroup("Pages")] private GameObject _levelListPage;
	[SerializeField, BoxGroup("Pages")] private GameObject _settingsPage;

	[Title("Main page")]
	[SerializeField, BoxGroup("Main page")] private Button _campaignButton;
	[SerializeField, BoxGroup("Main page")] private Button _survivalButton;
	[SerializeField, BoxGroup("Main page")] private Button _settingsButton;
	[SerializeField, BoxGroup("Main page")] private Button _exitButton;

	[Title("Level list page")]
	[SerializeField, BoxGroup("Level list page")] private UniText _levelListTitle;
	[SerializeField, BoxGroup("Level list page"),
	 Tooltip("ScrollRect списка уровней (для сброса прокрутки в начало при открытии).")]
	private ScrollRect _levelScroll;
	[SerializeField, BoxGroup("Level list page"),
	 Tooltip("Куда инстанцируются кнопки уровней. Обычно это Content у ScrollRect.")]
	private Transform _buttonsContainer;
	[SerializeField, Required, BoxGroup("Level list page")] private LevelSelectButton _buttonPrefab;
	[SerializeField, BoxGroup("Level list page")] private Button _levelListBackButton;
	[SerializeField, BoxGroup("Level list page")] private string _campaignTitle = "Campaign";
	[SerializeField, BoxGroup("Level list page")] private string _survivalTitle = "Survival";

	[Title("Settings page")]
	[SerializeField, BoxGroup("Settings page")] private Button _settingsBackButton;
	#endregion

	private GameMode _currentMode;

	private void Start()
	{
		Wire(_campaignButton, () => OpenLevelList(GameMode.Campaign));
		Wire(_survivalButton, () => OpenLevelList(GameMode.Survival));
		Wire(_settingsButton, OpenSettings);
		Wire(_exitButton, Quit);
		Wire(_levelListBackButton, ShowMainPage);
		Wire(_settingsBackButton, ShowMainPage);

		ShowMainPage();
	}

	private static void Wire(Button button, Action handler)
	{
		if (button == null)
			return;

		button.onClick.RemoveAllListeners();
		button.onClick.AddListener(() => handler());
	}

	#region NAVIGATION
	private void ShowMainPage() => ShowPage(_mainPage);

	private void OpenSettings()
	{
		if (_settingsPage == null)
		{
			Debug.LogWarning("[MainMenu] Settings Page не задана — кнопка Settings ничего не открывает.");
			return;
		}

		ShowPage(_settingsPage);
	}

	private void OpenLevelList(GameMode mode)
	{
		_currentMode = mode;

		var library = mode == GameMode.Survival ? _survivalLibrary : _campaignLibrary;

		if (_levelListTitle != null)
			_levelListTitle.Text = mode == GameMode.Survival ? _survivalTitle : _campaignTitle;

		BuildLevelList(library);
		ShowPage(_levelListPage);

		// Прокрутить список в начало при каждом открытии.
		if (_levelScroll != null)
			_levelScroll.verticalNormalizedPosition = 1f;
	}

	private void ShowPage(GameObject page)
	{
		if (_mainPage != null) _mainPage.SetActive(page == _mainPage);
		if (_levelListPage != null) _levelListPage.SetActive(page == _levelListPage);
		if (_settingsPage != null) _settingsPage.SetActive(page == _settingsPage);
	}
	#endregion

	#region LEVEL LIST
	private void BuildLevelList(LevelLibrary library)
	{
		var container = _buttonsContainer != null
			? _buttonsContainer
			: (_levelScroll != null ? _levelScroll.content : null);

		if (library == null || container == null || _buttonPrefab == null)
		{
			Debug.LogWarning("[MainMenu] Не заданы LevelLibrary / Buttons Container / Button Prefab — " +
				"список уровней не построен.");
			return;
		}

		// Очистить ранее созданные кнопки (список перестраивается под выбранный режим).
		for (int i = container.childCount - 1; i >= 0; i--)
			Destroy(container.GetChild(i).gameObject);

		foreach (var level in library.Levels)
		{
			if (level == null)
				continue;

			var button = Instantiate(_buttonPrefab, container);
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

		GameSession.Select(_currentMode, level);
		Time.timeScale = 1f;
		// Показываем занавес и грузим сцену асинхронно: занавес успевает отрисоваться поверх меню
		// и накрывает переход, а EntryPoint снимет его, когда уровень готов и отрисован.
		LoadingScreen.Show();
		SceneManager.LoadSceneAsync(_gameplaySceneName);
	}
	#endregion

	private void Quit()
	{
		Application.Quit();
#if UNITY_EDITOR
		UnityEditor.EditorApplication.isPlaying = false;
#endif
	}
}
