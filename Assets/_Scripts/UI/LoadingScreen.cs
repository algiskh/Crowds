using System.Collections;
using LightSide;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Полноэкранный «занавес» загрузки между сменой сцен. Показывается из меню (Play) и окон
/// рестарта ПЕРЕД загрузкой геймплейной сцены, а снимается из <see cref="ECS.EntryPoint"/>,
/// когда уровень полностью проинициализирован и отрисован камерой.
///
/// Живёт между сценами (DontDestroyOnLoad), строится целиком в рантайме (без префаба — как
/// <see cref="FailScreenOverlay"/>) и анимируется на unscaledDeltaTime, поэтому работает даже
/// при Time.timeScale == 0. blocksRaycasts включён — под занавесом ничего не кликается.
/// Индикатор: вращающийся спиннер + надпись <see cref="UniText"/> с бегущим многоточием.
/// </summary>
public sealed class LoadingScreen : MonoBehaviour
{
    // Выше любого игрового/меню-канваса (окно поражения поднимается лишь до ~1001).
    private const int SortingOrder = 30000;
    private const float FadeOutDuration = 0.25f;
    private const float SpinnerDegreesPerSecond = 220f;
    // Путь к своему спрайту спиннера внутри любой папки Resources (без расширения).
    // Напр. файл Assets/_Art/Resources/UI/LoadingSpinner.png → путь "UI/LoadingSpinner".
    // Если спрайт не найден — рисуем встроенный круг с вращающейся «прорехой».
    private const string SpinnerResourcePath = "UI/LoadingSpinner";

    public static LoadingScreen Instance { get; private set; }

    private CanvasGroup _group;
    private UniText _label;
    private RectTransform _spinnerRect;
    private RectTransform _barFillRect;
    private float _barWidth;
    private float _dotsTimer;
    private bool _hiding;

    // Создаёт занавес, если его ещё нет. Идемпотентно — повторный вызов (напр. из EntryPoint
    // после того, как занавес уже показан из меню) ничего не делает.
    public static void Show()
    {
        if (Instance != null)
            return;

        var go = new GameObject("LoadingScreen");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<LoadingScreen>();
        Instance.Build();
    }

    // Задаёт прогресс полосы [0..1]. Безопасно вызывать без активного занавеса (no-op).
    public static void SetProgress(float value)
    {
        if (Instance != null)
            Instance.ApplyProgress(value);
    }

    // Плавно гасит занавес и уничтожает его. Безопасно вызывать без активного занавеса.
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
        _group.blocksRaycasts = true; // не пускаем клики в меню/игру под занавесом

        // Фон.
        var bg = CreateImage("Background", transform, new Color(0.04f, 0.04f, 0.05f, 1f));
        Stretch(bg.rectTransform);
        bg.raycastTarget = true;

        // Спиннер: свой спрайт из Resources, иначе — встроенный круг с вращающейся «прорехой».
        var spinner = CreateImage("Spinner", transform, Color.white);
        var customSpinner = Resources.Load<Sprite>(SpinnerResourcePath);
        if (customSpinner != null)
        {
            // Свою картинку крутим целиком (у типичного спиннера есть «голова»/градиент/разрыв).
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

        // Надпись через UniText (шрифт/оформление берём из настроек проекта).
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(transform, false);
        _label = labelGo.AddComponent<UniText>();
        // Шрифт/оформление у компонента, созданного в рантайме, не назначаются автоматически:
        // авто-подстановка дефолтов (UniTextSettings.DefaultFontStack/DefaultAppearance) живёт
        // под #if UNITY_EDITOR и в плеер-билде отсутствует (иначе CS0117 при сборке). Поэтому
        // «одалживаем» шрифт и оформление у уже существующего в загруженной сцене UniText —
        // и меню, и геймплейная сцена всегда содержат текстовые метки на дефолтном шрифте.
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

        // Полоса прогресса: фон + заливка (растёт слева направо через sizeDelta.x).
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

    // Копирует шрифт и оформление в наш рантайм-лейбл с любого другого UniText, уже
    // присутствующего в загруженной сцене. Работает и в редакторе, и в билде (в отличие от
    // UniTextSettings.Default*, доступных только под #if UNITY_EDITOR). Если подходящего
    // «донора» не нашлось — в редакторе падаем на дефолты из настроек проекта.
    private void AssignFontFromSceneTemplate()
    {
        var candidates = FindObjectsByType<UniText>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var candidate in candidates)
        {
            if (candidate == _label || candidate.FontStack == null)
                continue;

            _label.FontStack = candidate.FontStack;
            // Appearance-сеттер обращается к внутреннему fontProvider, который у только что
            // добавленного компонента ещё null (создаётся при первом ребилде). Значение всё
            // равно записывается в поле ДО обращения к провайдеру и подхватывается при
            // инициализации — поэтому ожидаемый NRE здесь безопасно проглатываем.
            if (candidate.Appearance != null)
            {
                try { _label.Appearance = candidate.Appearance; }
                catch (System.NullReferenceException) { /* провайдер ещё не создан — это ок */ }
            }
            return;
        }

#if UNITY_EDITOR
        // Фолбэк для редактора (напр. если занавес показан на пустой сцене без других UniText).
        if (UniTextSettings.DefaultFontStack != null)
            _label.FontStack = UniTextSettings.DefaultFontStack;
        if (UniTextSettings.DefaultAppearance != null)
        {
            try { _label.Appearance = UniTextSettings.DefaultAppearance; }
            catch (System.NullReferenceException) { /* провайдер ещё не создан — это ок */ }
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

        // Вращаем спиннер (по часовой — отрицательный поворот вокруг Z).
        if (_spinnerRect != null)
            _spinnerRect.Rotate(0f, 0f, -SpinnerDegreesPerSecond * dt);

        // Анимированное многоточие — движение есть даже пока прогресс стоит.
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
