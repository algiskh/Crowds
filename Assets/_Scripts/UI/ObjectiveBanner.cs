using LightSide;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime top-of-screen objective line for scripted levels (driven by ECS.MissionSystem from
/// DifficultyStage.Objective). Own Canvas (sortingOrder 450: above the damage vignette, below the crosshair,
/// pause menu and fail veil), never blocks raycasts. A new text pops in with a short fade/scale.
/// The UniText font is borrowed from a label already in the scene (same approach as LoadingScreen — the editor-only
/// defaults are absent in builds). Lives in the scene → recreated on restart.
/// </summary>
public sealed class ObjectiveBanner
{
	private const int SortingOrder = 450;
	private const float PopTime = 0.35f;

	private GameObject _root;
	private CanvasGroup _group;
	private RectTransform _panel;
	private UniText _label;
	private string _text;
	private float _pop = 1f;

	public void EnsureCreated()
	{
		if (_root != null)
			return;

		_root = new GameObject("ObjectiveBanner");
		var canvas = _root.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = SortingOrder;
		var scaler = _root.AddComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1920f, 1080f);
		scaler.matchWidthOrHeight = 0.5f;
		_group = _root.AddComponent<CanvasGroup>();
		_group.interactable = false;
		_group.blocksRaycasts = false;
		_group.alpha = 0f;

		var panelGo = new GameObject("Panel");
		panelGo.transform.SetParent(_root.transform, false);
		var background = panelGo.AddComponent<Image>();
		background.color = new Color(0f, 0f, 0f, 0.45f);
		background.raycastTarget = false;
		_panel = background.rectTransform;
		_panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 1f);
		_panel.pivot = new Vector2(0.5f, 1f);
		_panel.sizeDelta = new Vector2(1000f, 84f);
		_panel.anchoredPosition = new Vector2(0f, -24f);

		var labelGo = new GameObject("Label");
		labelGo.transform.SetParent(panelGo.transform, false);
		_label = labelGo.AddComponent<UniText>();
		AssignFontFromScene(_label);
		_label.FontSize = 34f;
		_label.HorizontalAlignment = HorizontalAlignment.Center;
		_label.VerticalAlignment = VerticalAlignment.Middle;
		_label.color = new Color(1f, 0.93f, 0.75f, 1f);
		var labelRect = _label.rectTransform;
		labelRect.anchorMin = Vector2.zero;
		labelRect.anchorMax = Vector2.one;
		labelRect.offsetMin = new Vector2(16f, 4f);
		labelRect.offsetMax = new Vector2(-16f, -4f);
	}

	/// <summary>Shows <paramref name="text"/> (null/empty hides the banner). Same text again = no re-pop.</summary>
	public void SetText(string text)
	{
		if (_root == null || text == _text)
			return;
		string previousHeadline = FirstLine(_text);
		_text = text;
		if (string.IsNullOrEmpty(text))
		{
			_group.alpha = 0f;
			return;
		}
		_label.Text = text;
		// A new objective (different first line) pops; a counter update ("Осталось: N") just swaps the text.
		if (FirstLine(text) != previousHeadline)
			_pop = 0f;
	}

	/// <summary>Pop-in animation (unscaled time, so it also plays while the world is paused).</summary>
	public void Tick(float unscaledDt)
	{
		if (_root == null || string.IsNullOrEmpty(_text))
			return;
		if (_pop < 1f)
			_pop = Mathf.Min(1f, _pop + unscaledDt / PopTime);
		float t = Mathf.SmoothStep(0f, 1f, _pop);
		_group.alpha = t;
		_panel.localScale = Vector3.one * Mathf.Lerp(1.15f, 1f, t);
	}

	public void Hide()
	{
		_text = null;
		if (_group != null)
			_group.alpha = 0f;
	}

	private static string FirstLine(string s)
	{
		if (string.IsNullOrEmpty(s))
			return string.Empty;
		int i = s.IndexOf('\n');
		return i < 0 ? s : s.Substring(0, i);
	}

	private static void AssignFontFromScene(UniText label)
	{
		var candidates = Object.FindObjectsByType<UniText>(FindObjectsInactive.Include, FindObjectsSortMode.None);
		foreach (var candidate in candidates)
		{
			if (candidate == label || candidate.FontStack == null)
				continue;
			label.FontStack = candidate.FontStack;
			if (candidate.Appearance != null)
			{
				// The Appearance setter touches a provider that isn't created yet on a fresh component; the value is
				// stored before the NRE and picked up on init (see LoadingScreen.AssignFontFromSceneTemplate).
				try { label.Appearance = candidate.Appearance; }
				catch (System.NullReferenceException) { }
			}
			return;
		}
#if UNITY_EDITOR
		if (UniTextSettings.DefaultFontStack != null)
			label.FontStack = UniTextSettings.DefaultFontStack;
		if (UniTextSettings.DefaultAppearance != null)
		{
			try { label.Appearance = UniTextSettings.DefaultAppearance; }
			catch (System.NullReferenceException) { }
		}
#endif
	}
}
