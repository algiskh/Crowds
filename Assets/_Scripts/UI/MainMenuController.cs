using System;
using LightSide;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Main menu with several pages.
// Start page: Campaign / Survival / Settings / Exit.
// Campaign and Survival open the same page with a scroll list of level previews
// (LevelSelectButton in the ScrollRect's Content), but fill it from their own LevelLibrary.
// On click, the level and mode are remembered in GameSession, after which the gameplay
// scene loads — EntryPoint there reads GameSession.SelectedLevel.
public class MainMenuController : MonoBehaviour
{
	#region FIELDS
	[Title("Scenes")]
	[SerializeField, BoxGroup("Scenes"),
		Tooltip("Gameplay scene name (must be added to Build Settings).")]
	private string _gameplaySceneName = "SampleScene";

	[Title("Libraries")]
	[SerializeField, Required, BoxGroup("Libraries"),
		Tooltip("Campaign levels.")]
	private LevelLibrary _campaignLibrary;
	[SerializeField, Required, BoxGroup("Libraries"),
		Tooltip("Survival mode levels.")]
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
		Tooltip("Level list ScrollRect (to reset the scroll to the top on open).")]
	private ScrollRect _levelScroll;
	[SerializeField, BoxGroup("Level list page"),
		Tooltip("Where the level buttons are instantiated. Usually the ScrollRect's Content.")]
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
			Debug.LogWarning("[MainMenu] Settings Page not assigned — the Settings button opens nothing.");
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

		// Scroll the list to the top on every open.
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
			Debug.LogWarning("[MainMenu] LevelLibrary / Buttons Container / Button Prefab not assigned — " +
				"level list not built.");
			return;
		}

		EnsureListLayout(container);

		// Clear previously created buttons (the list is rebuilt for the selected mode).
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

	/// <summary>
	/// The list container needs a layout, otherwise every button is instantiated at the same spot and they overlap —
	/// only one level is visible. If the scene's Content has no LayoutGroup, a vertical one (+ ContentSizeFitter so
	/// the ScrollRect can scroll) is added at runtime; a layout set up in the scene is left as is.
	/// </summary>
	private static void EnsureListLayout(Transform container)
	{
		if (container.GetComponent<LayoutGroup>() != null)
			return;

		var layout = container.gameObject.AddComponent<VerticalLayoutGroup>();
		layout.childAlignment = TextAnchor.UpperCenter;
		layout.spacing = 24f;
		layout.padding = new RectOffset(0, 0, 16, 16);
		// Buttons keep their prefab size (500×200); the group only stacks them.
		layout.childControlWidth = false;
		layout.childControlHeight = false;
		layout.childForceExpandWidth = false;
		layout.childForceExpandHeight = false;

		if (container.GetComponent<ContentSizeFitter>() == null)
		{
			var fitter = container.gameObject.AddComponent<ContentSizeFitter>();
			fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
		}
	}

	private void Play(LevelDefinition level)
	{
		if (level == null)
		{
			Debug.LogError("[MainMenu] Level not selected / not assigned in LevelLibrary.");
			return;
		}

		GameSession.Select(_currentMode, level);
		Time.timeScale = 1f;
		// Show the curtain and load the scene asynchronously: the curtain manages to render over the menu
		// and covers the transition, and EntryPoint lifts it once the level is ready and rendered.
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
