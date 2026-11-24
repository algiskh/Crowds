using System.Collections;
using LightSide;
using MPUIKIT;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Pause menu (Esc / gamepad Start): dimmed screen, title and two buttons — Continue and Main menu.
/// Built entirely at runtime (no prefab, like <see cref="LoadingScreen"/> / <see cref="FailScreenOverlay"/>);
/// opened/closed by ECS.PauseMenuSystem, which also owns the game pause. The buttons only raise flags
/// that the system reads next frame, so all pause state changes stay in ECS.
/// The menu sits in a rounded frame like the win/fail windows (MPImage, radius 25, 5 px black border) whose
/// inside is frosted glass: on open the screen is captured once at the end of the frame, blurred at 1/4
/// resolution (<c>Hidden/Crowds/UIFrostBlur</c>, a few dual-Kawase passes) and shown through the rounded mask.
/// The world is frozen while paused, so the single snapshot stays valid and the blur costs nothing per frame.
/// </summary>
public sealed class PauseMenu : MonoBehaviour
{
	// Above gameplay UI, below the fail sequence veil (1000) and the loading curtain.
	private const int SortingOrder = 900;
	private static readonly Color DimColor = new(0f, 0f, 0f, 0.45f);
	private static readonly Color GlassTint = new(0.06f, 0.06f, 0.08f, 0.45f);
	private static readonly Color FrameColor = Color.black;
	private static readonly Color ButtonColor = new(0.16f, 0.16f, 0.18f, 0.95f);
	private static readonly Color ButtonHover = new(0.28f, 0.28f, 0.32f, 1f);
	private static readonly Vector2 PanelSize = new(640f, 500f);
	private static readonly Vector2 PanelPosition = new(0f, 35f);
	// Same look as the Panel of FailWindow/WinWindow.
	private const float CornerRadius = 25f;
	private const float FrameWidth = 5f;
	private const int BlurDownsample = 4;
	private const int BlurIterations = 3;
	private const string BlurShaderPath = "Shaders/UIFrostBlur";
	private static readonly int OffsetId = Shader.PropertyToID("_Offset");

	private Canvas _canvas;
	private Button _continueButton;
	private RectTransform _panel;
	private RawImage _glass;
	private Material _blurMaterial;
	private RenderTexture _blurred;
	private Coroutine _openRoutine;
	private bool _open;

	public bool ContinueRequested { get; private set; }
	public bool MainMenuRequested { get; private set; }
	public bool IsOpen => _open;

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
		_open = open;
		if (_openRoutine != null)
		{
			StopCoroutine(_openRoutine);
			_openRoutine = null;
		}

		// On open the canvas stays hidden until the end of this frame, so the snapshot doesn't contain the menu.
		_canvas.enabled = false;
		if (open)
			_openRoutine = StartCoroutine(OpenRoutine());
		else
			ReleaseBlur();
	}

	private IEnumerator OpenRoutine()
	{
		yield return new WaitForEndOfFrame();
		CaptureBlur();
		_canvas.enabled = true;
		_openRoutine = null;
		if (EventSystem.current != null)
			EventSystem.current.SetSelectedGameObject(_continueButton.gameObject); // gamepad navigation
	}

	private void OnDestroy()
	{
		ReleaseBlur();
		if (_blurMaterial != null)
			Destroy(_blurMaterial);
	}

	// One-off: full screen → 1/4 copy → Kawase down/up chain → _blurred, shown in the panel with uvRect = the
	// panel's screen rect, so the glass shows exactly what lies behind it.
	private void CaptureBlur()
	{
		ReleaseBlur();
		if (_blurMaterial == null)
		{
			var shader = Resources.Load<Shader>(BlurShaderPath);
			if (shader == null || !shader.isSupported)
			{
				_glass.enabled = false; // no blur: the tinted glass alone still reads as a panel
				return;
			}
			_blurMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
		}

		int width = Mathf.Max(1, Screen.width);
		int height = Mathf.Max(1, Screen.height);
		var screen = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
		ScreenCapture.CaptureScreenshotIntoRenderTexture(screen);

		int w = Mathf.Max(1, width / BlurDownsample);
		int h = Mathf.Max(1, height / BlurDownsample);
		var chain = new RenderTexture[BlurIterations + 1];
		chain[0] = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
		// The capture is upside down where UV starts at the top (D3D/Metal/Vulkan).
		if (SystemInfo.graphicsUVStartsAtTop)
			Graphics.Blit(screen, chain[0], new Vector2(1f, -1f), new Vector2(0f, 1f));
		else
			Graphics.Blit(screen, chain[0]);
		RenderTexture.ReleaseTemporary(screen);

		// Down chain (each half size), then back up: a wide, smooth blur for a handful of tiny blits.
		_blurMaterial.SetFloat(OffsetId, 1f);
		for (int i = 1; i <= BlurIterations; i++)
		{
			chain[i] = RenderTexture.GetTemporary(Mathf.Max(1, w >> i), Mathf.Max(1, h >> i), 0, RenderTextureFormat.ARGB32);
			Graphics.Blit(chain[i - 1], chain[i], _blurMaterial, 0);
		}

		_blurred = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "PauseMenuBlur" };
		_blurMaterial.SetFloat(OffsetId, 0.5f);
		for (int i = BlurIterations; i >= 1; i--)
			Graphics.Blit(chain[i], i == 1 ? _blurred : chain[i - 1], _blurMaterial, 1);
		for (int i = 0; i <= BlurIterations; i++)
			RenderTexture.ReleaseTemporary(chain[i]);

		_glass.texture = _blurred;
		_glass.enabled = true;
		_glass.uvRect = PanelScreenUv();
	}

	private Rect PanelScreenUv()
	{
		// Screen Space Overlay: world corners are screen pixels.
		Canvas.ForceUpdateCanvases();
		var corners = new Vector3[4];
		_panel.GetWorldCorners(corners);
		float sw = Mathf.Max(1, Screen.width);
		float sh = Mathf.Max(1, Screen.height);
		return Rect.MinMaxRect(corners[0].x / sw, corners[0].y / sh, corners[2].x / sw, corners[2].y / sh);
	}

	private void ReleaseBlur()
	{
		if (_glass != null)
			_glass.texture = null;
		if (_blurred == null)
			return;
		_blurred.Release();
		Destroy(_blurred);
		_blurred = null;
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
		Stretch(dim.rectTransform);

		BuildPanel();

		CreateLabel("Title", transform, "ПАУЗА", 72f, new Vector2(0f, 170f), new Vector2(800f, 110f));
		_continueButton = CreateButton("Continue", "Продолжить", new Vector2(0f, 20f), () => ContinueRequested = true);
		var mainMenu = CreateButton("MainMenu", "Главное меню", new Vector2(0f, -110f), () => MainMenuRequested = true);

		// Explicit vertical navigation for the gamepad.
		var nav = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = mainMenu, selectOnUp = mainMenu };
		_continueButton.navigation = nav;
		nav.selectOnDown = nav.selectOnUp = _continueButton;
		mainMenu.navigation = nav;
	}

	// Rounded glass panel: MPImage mask (shape only) → blurred snapshot → dark tint, then a stroke-only MPImage on
	// top as the frame (it also hides the mask's hard stencil edge).
	private void BuildPanel()
	{
		var maskShape = CreateRoundedRect("Panel", transform, Color.white, 0f);
		_panel = maskShape.rectTransform;
		_panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
		_panel.sizeDelta = PanelSize;
		_panel.anchoredPosition = PanelPosition;
		maskShape.gameObject.AddComponent<Mask>().showMaskGraphic = false;

		var glassGo = new GameObject("Glass", typeof(RectTransform));
		glassGo.transform.SetParent(_panel, false);
		_glass = glassGo.AddComponent<RawImage>();
		_glass.raycastTarget = false;
		Stretch(_glass.rectTransform);

		var tint = CreateImage("Tint", _panel, GlassTint);
		tint.raycastTarget = false;
		Stretch(tint.rectTransform);

		var frame = CreateRoundedRect("Frame", transform, FrameColor, FrameWidth);
		frame.raycastTarget = false;
		var frameRect = frame.rectTransform;
		frameRect.anchorMin = frameRect.anchorMax = new Vector2(0.5f, 0.5f);
		frameRect.sizeDelta = PanelSize;
		frameRect.anchoredPosition = PanelPosition;
	}

	private static MPImage CreateRoundedRect(string name, Transform parent, Color color, float strokeWidth)
	{
		var go = new GameObject(name, typeof(RectTransform));
		go.transform.SetParent(parent, false);
		var image = go.AddComponent<MPImage>();
		image.DrawShape = DrawShape.Rectangle;
		var rectangle = image.Rectangle;
		rectangle.CornerRadius = new Vector4(CornerRadius, CornerRadius, CornerRadius, CornerRadius);
		image.Rectangle = rectangle;
		image.StrokeWidth = strokeWidth;
		image.OutlineWidth = 0f;
		image.color = color;
		return image;
	}

	private static void Stretch(RectTransform rect)
	{
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.one;
		rect.offsetMin = rect.offsetMax = Vector2.zero;
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
