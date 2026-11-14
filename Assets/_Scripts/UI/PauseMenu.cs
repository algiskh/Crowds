using LightSide;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Pause menu (Esc / gamepad Start): dimmed screen, title and two buttons — Continue and Main menu.
/// Built entirely at runtime (no prefab, like <see cref="LoadingScreen"/> / <see cref="FailScreenOverlay"/>);
/// opened/closed by ECS.PauseMenuSystem, which also owns the game pause. The buttons only raise flags
/// that the system reads next frame, so all pause state changes stay in ECS.
/// </summary>
public sealed class PauseMenu : MonoBehaviour
{
	// Above gameplay UI, below the fail sequence veil (1000) and the loading curtain.
	private const int SortingOrder = 900;
	private static readonly Color DimColor = new(0f, 0f, 0f, 0.6f);
	private static readonly Color ButtonColor = new(0.16f, 0.16f, 0.18f, 0.95f);
	private static readonly Color ButtonHover = new(0.28f, 0.28f, 0.32f, 1f);

	private Canvas _canvas;
	private Button _continueButton;

	public bool ContinueRequested { get; private set; }
	public bool MainMenuRequested { get; private set; }
	public bool IsOpen => _canvas != null && _canvas.enabled;

	public static PauseMenu Create()
	{
		var go = new GameObject("PauseMenu");
		var menu = go.AddComponent<PauseMenu>();
		menu.Build();
		menu.SetOpen(false);
		return menu;
	}

	public void SetOpen(bool open)
	{
		ContinueRequested = false;
		MainMenuRequested = false;
		_canvas.enabled = open;
		if (open && EventSystem.current != null)
			EventSystem.current.SetSelectedGameObject(_continueButton.gameObject); // gamepad navigation
	}

	private void Build()
	{
		_canvas = gameObject.AddComponent<Canvas>();
		_canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		_canvas.sortingOrder = SortingOrder;
		var scaler = gameObject.AddComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1920f, 1080f);
		scaler.matchWidthOrHeight = 0.5f;
		gameObject.AddComponent<GraphicRaycaster>();
		EnsureEventSystem();

		// Full-screen dim: also swallows clicks so nothing under the menu reacts.
		var dim = CreateImage("Dim", transform, DimColor);
		var dimRect = dim.rectTransform;
		dimRect.anchorMin = Vector2.zero;
		dimRect.anchorMax = Vector2.one;
		dimRect.offsetMin = dimRect.offsetMax = Vector2.zero;

		CreateLabel("Title", transform, "ПАУЗА", 72f, new Vector2(0f, 170f), new Vector2(800f, 110f));
		_continueButton = CreateButton("Continue", "Продолжить", new Vector2(0f, 20f), () => ContinueRequested = true);
		var mainMenu = CreateButton("MainMenu", "Главное меню", new Vector2(0f, -110f), () => MainMenuRequested = true);

		// Explicit vertical navigation for the gamepad.
		var nav = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = mainMenu, selectOnUp = mainMenu };
		_continueButton.navigation = nav;
		nav.selectOnDown = nav.selectOnUp = _continueButton;
		mainMenu.navigation = nav;
	}

	private static void EnsureEventSystem()
	{
		if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null)
			return;
		new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
	}

	private Button CreateButton(string name, string text, Vector2 position, UnityEngine.Events.UnityAction onClick)
	{
		var image = CreateImage(name, transform, ButtonColor);
		var rect = image.rectTransform;
		rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
		rect.sizeDelta = new Vector2(460f, 100f);
		rect.anchoredPosition = position;

		var button = image.gameObject.AddComponent<Button>();
		button.targetGraphic = image;
		// The button tint multiplies the image color, so the image is white and the tints are the real colors.
		image.color = Color.white;
		var colors = button.colors;
		colors.normalColor = ButtonColor;
		colors.highlightedColor = ButtonHover;
		colors.selectedColor = ButtonHover;
		colors.pressedColor = ButtonColor * 0.8f;
		button.colors = colors;
		button.onClick.AddListener(onClick);

		CreateLabel("Label", image.transform, text, 44f, Vector2.zero, rect.sizeDelta);
		return button;
	}

	private UniText CreateLabel(string name, Transform parent, string text, float size, Vector2 position, Vector2 boxSize)
	{
		var go = new GameObject(name);
		go.transform.SetParent(parent, false);
		var label = go.AddComponent<UniText>();
		AssignFont(label);
		label.FontSize = size;
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.VerticalAlignment = VerticalAlignment.Middle;
		label.color = Color.white;
		label.raycastTarget = false;
		label.Text = text;
		var rect = label.rectTransform;
		rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
		rect.sizeDelta = boxSize;
		rect.anchoredPosition = position;
		return label;
	}

	// A runtime UniText gets no font: borrow it from a label already in the scene (same as LoadingScreen).
	private static void AssignFont(UniText label)
	{
		foreach (var candidate in FindObjectsByType<UniText>(FindObjectsInactive.Include, FindObjectsSortMode.None))
		{
			if (candidate == label || candidate.FontStack == null)
				continue;
			label.FontStack = candidate.FontStack;
			if (candidate.Appearance != null)
			{
				try { label.Appearance = candidate.Appearance; }
				catch (System.NullReferenceException) { /* provider not created yet — the value is kept */ }
			}
			return;
		}
#if UNITY_EDITOR
		if (UniTextSettings.DefaultFontStack != null)
			label.FontStack = UniTextSettings.DefaultFontStack;
#endif
	}

	private static Image CreateImage(string name, Transform parent, Color color)
	{
		var go = new GameObject(name);
		go.transform.SetParent(parent, false);
		var image = go.AddComponent<Image>();
		image.color = color;
		return image;
	}
}
