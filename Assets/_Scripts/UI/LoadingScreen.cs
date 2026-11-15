using System.Collections;
using LightSide;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen loading "curtain" between scene changes. Shown from the menu (Play) and restart
/// windows BEFORE the gameplay scene starts loading, and lifted from <see cref="ECS.EntryPoint"/>
/// once the level is fully initialized and rendered by the camera.
///
/// Lives across scenes (DontDestroyOnLoad), built entirely at runtime (no prefab — like
/// <see cref="FailScreenOverlay"/>) and animated on unscaledDeltaTime, so it works even
/// at Time.timeScale == 0. blocksRaycasts is on — nothing clickable under the curtain.
/// Indicator: a spinning spinner + a <see cref="UniText"/> label with animated dots.
/// </summary>
public sealed class LoadingScreen : MonoBehaviour
{
    // Above any gameplay/menu canvas (the defeat window rises only to ~1001).
    private const int SortingOrder = 30000;
    private const float FadeOutDuration = 0.25f;
    private const float SpinnerDegreesPerSecond = 220f;
    // Path to your own spinner sprite inside any Resources folder (no extension).
    // E.g. file Assets/_Art/Resources/UI/LoadingSpinner.png → path "UI/LoadingSpinner".
    // If the sprite isn't found — draw the built-in circle with a rotating "gap".
    private const string SpinnerResourcePath = "UI/LoadingSpinner";

    public static LoadingScreen Instance { get; private set; }

    private CanvasGroup _group;
    private UniText _label;
    private RectTransform _spinnerRect;
    private RectTransform _barFillRect;
    private float _barWidth;
    private float _dotsTimer;
    private bool _hiding;

    // Creates the curtain if it doesn't exist yet. Idempotent — a repeated call (e.g. from EntryPoint
    // after the curtain was already shown from the menu) does nothing.
    public static void Show()
    {
        if (Instance != null)
            return;

        var go = new GameObject("LoadingScreen");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<LoadingScreen>();
        Instance.Build();
    }

    // Sets the bar progress [0..1]. Safe to call with no active curtain (no-op).
    public static void SetProgress(float value)
    {
        if (Instance != null)
            Instance.ApplyProgress(value);
    }

    // Smoothly fades the curtain out and destroys it. Safe to call with no active curtain.
    public static void HideAndDestroy()
    {
        if (Instance != null)
            Instance.BeginHide();
    }

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        gameObject.AddComponent<GraphicRaycaster>();

        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 1f;
        _group.blocksRaycasts = true; // don't let clicks through to the menu/game under the curtain

        // Background.
        var bg = CreateImage("Background", transform, new Color(0.04f, 0.04f, 0.05f, 1f));
        Stretch(bg.rectTransform);
        bg.raycastTarget = true;

        // Spinner: own sprite from Resources, otherwise the built-in circle with a rotating "gap".
        var spinner = CreateImage("Spinner", transform, Color.white);
        var customSpinner = Resources.Load<Sprite>(SpinnerResourcePath);
        if (customSpinner != null)
        {
            // Spin our own image whole (a typical spinner has a "head"/gradient/gap).
            spinner.sprite = customSpinner;
            spinner.type = Image.Type.Simple;
            spinner.preserveAspect = true;
        }
        else
        {
            spinner.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
            spinner.color = new Color(0.9f, 0.9f, 0.95f, 1f);
            spinner.type = Image.Type.Filled;
            spinner.fillMethod = Image.FillMethod.Radial360;
            spinner.fillClockwise = true;
            spinner.fillAmount = 0.8f;
        }
        _spinnerRect = spinner.rectTransform;
        _spinnerRect.anchorMin = _spinnerRect.anchorMax = new Vector2(0.5f, 0.5f);
        _spinnerRect.sizeDelta = new Vector2(96f, 96f);
        _spinnerRect.anchoredPosition = new Vector2(0f, 70f);

        // Label via UniText (font/style borrowed from the project's settings).
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(transform, false);
        _label = labelGo.AddComponent<UniText>();
        // A component created at runtime doesn't get a font/style assigned automatically:
        // the default auto-substitution (UniTextSettings.DefaultFontStack/DefaultAppearance) lives
        // under #if UNITY_EDITOR and is absent in player builds (otherwise CS0117 when compiling). So we
        // "borrow" the font and style from a UniText already present in the loaded scene —
        // both the menu and the gameplay scene always contain text labels with the default font.
        AssignFontFromSceneTemplate();
        _label.FontSize = 48f;
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        _label.VerticalAlignment = VerticalAlignment.Middle;
        _label.color = Color.white;
        _label.Text = "LOADING";
        var labelRect = _label.rectTransform;
        labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.sizeDelta = new Vector2(600f, 80f);
        labelRect.anchoredPosition = new Vector2(0f, -20f);

        // Progress bar: background + fill (grows left to right via sizeDelta.x).
        _barWidth = 400f;
        var barBack = CreateImage("BarBackground", transform, new Color(1f, 1f, 1f, 0.15f));
        var barBackRect = barBack.rectTransform;
        barBackRect.anchorMin = barBackRect.anchorMax = new Vector2(0.5f, 0.5f);
        barBackRect.sizeDelta = new Vector2(_barWidth, 8f);
        barBackRect.anchoredPosition = new Vector2(0f, -70f);

        var barFill = CreateImage("BarFill", barBack.transform, new Color(0.9f, 0.9f, 0.95f, 1f));
        _barFillRect = barFill.rectTransform;
        _barFillRect.anchorMin = new Vector2(0f, 0f);
        _barFillRect.anchorMax = new Vector2(0f, 1f);
        _barFillRect.pivot = new Vector2(0f, 0.5f);
        _barFillRect.anchoredPosition = Vector2.zero;
        ApplyProgress(0f);
    }

    // Copies the font and style into our runtime label from any other UniText already
    // present in the loaded scene. Works both in the editor and in builds (unlike
    // UniTextSettings.Default*, available only under #if UNITY_EDITOR). If no suitable
    // "donor" is found — in the editor we fall back to the project settings defaults.
    private void AssignFontFromSceneTemplate()
    {
        var candidates = FindObjectsByType<UniText>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var candidate in candidates)
        {
            if (candidate == _label || candidate.FontStack == null)
                continue;

            _label.FontStack = candidate.FontStack;
            // The Appearance setter touches the internal fontProvider, which is still null on a just-
            // added component (created on the first rebuild). The value is still written to the field
            // BEFORE touching the provider and is picked up on initialization — so the expected NRE is safely
            // swallowed here.
            if (candidate.Appearance != null)
            {
                try { _label.Appearance = candidate.Appearance; }
                catch (System.NullReferenceException) { /* provider not created yet — that's ok */ }
            }
            return;
        }

#if UNITY_EDITOR
        // Editor fallback (e.g. if the curtain is shown on an empty scene without other UniText).
        if (UniTextSettings.DefaultFontStack != null)
            _label.FontStack = UniTextSettings.DefaultFontStack;
        if (UniTextSettings.DefaultAppearance != null)
        {
            try { _label.Appearance = UniTextSettings.DefaultAppearance; }
            catch (System.NullReferenceException) { /* provider not created yet — that's ok */ }
        }
#endif
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private void ApplyProgress(float value)
    {
        if (_barFillRect != null)
            _barFillRect.sizeDelta = new Vector2(_barWidth * Mathf.Clamp01(value), 0f);
    }

    private void Update()
    {
        if (_hiding)
            return;

        float dt = Time.unscaledDeltaTime;

        // Spin the spinner (clockwise — negative rotation around Z).
        if (_spinnerRect != null)
            _spinnerRect.Rotate(0f, 0f, -SpinnerDegreesPerSecond * dt);

        // Animated dots — there's motion even while progress is stalled.
        if (_label != null)
        {
            _dotsTimer += dt;
            int dots = (int)(_dotsTimer * 2f) % 4;
            _label.Text = "LOADING" + new string('.', dots);
        }
    }

    private void BeginHide()
    {
        if (_hiding)
            return;

        _hiding = true;
        ApplyProgress(1f);
        StartCoroutine(FadeOutRoutine());
    }

    private IEnumerator FadeOutRoutine()
    {
        float t = 0f;
        while (t < FadeOutDuration)
        {
            t += Time.unscaledDeltaTime;
            if (_group != null)
                _group.alpha = Mathf.Clamp01(1f - t / FadeOutDuration);
            yield return null;
        }

        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
