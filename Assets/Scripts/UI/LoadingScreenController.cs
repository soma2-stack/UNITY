using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class LoadingScreenController : MonoBehaviour
{
    public static LoadingScreenController Instance { get; private set; }

    private const float MinimumVisibleSeconds = 0.75f;
    private static readonly Color DefaultStatusColor = new Color(0.92f, 0.9f, 0.84f, 1f);

    private CanvasGroup canvasGroup;
    private TMP_Text mapTitle;
    private TMP_Text statusText;
    private Image progressFill;
    private float shownAt;
    private Coroutine hideRoutine;

    public bool IsVisible => canvasGroup != null && canvasGroup.alpha > 0.001f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance == null)
        {
            new GameObject("Loading Screen").AddComponent<LoadingScreenController>();
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildInterface();
        SetVisible(false);
    }

    public void Show(string title = "SCHOOL OF THE DEAD", string status = "PREPARING...")
    {
        if (hideRoutine != null)
        {
            StopCoroutine(hideRoutine);
            hideRoutine = null;
        }

        mapTitle.text = string.IsNullOrWhiteSpace(title) ? "SCHOOL OF THE DEAD" : title.Trim().ToUpperInvariant();
        statusText.text = string.IsNullOrWhiteSpace(status) ? "PREPARING..." : status.Trim().ToUpperInvariant();
        statusText.color = DefaultStatusColor;
        SetProgress(0f);
        shownAt = Time.realtimeSinceStartup;
        SetVisible(true);
    }

    public void SetStatus(string status)
    {
        if (statusText != null && !string.IsNullOrWhiteSpace(status))
        {
            statusText.text = status.Trim().ToUpperInvariant();
        }
    }

    public void SetProgress(float normalizedProgress)
    {
        if (progressFill != null)
        {
            progressFill.fillAmount = Mathf.Clamp01(normalizedProgress);
        }
    }

    public void ShowError(string message)
    {
        Show("CONNECTION FAILED", message);
        statusText.color = new Color(1f, 0.38f, 0.28f, 1f);
        SetProgress(1f);
    }

    public void Hide()
    {
        if (hideRoutine != null)
        {
            StopCoroutine(hideRoutine);
        }

        hideRoutine = StartCoroutine(HideAfterMinimumTime());
    }

    private IEnumerator HideAfterMinimumTime()
    {
        float remaining = MinimumVisibleSeconds - (Time.realtimeSinceStartup - shownAt);
        if (remaining > 0f)
        {
            yield return new WaitForSecondsRealtime(remaining);
        }

        float startAlpha = canvasGroup.alpha;
        float elapsed = 0f;
        const float fadeDuration = 0.25f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, elapsed / fadeDuration);
            yield return null;
        }

        SetVisible(false);
        statusText.color = DefaultStatusColor;
        hideRoutine = null;
    }

    private void BuildInterface()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 2000;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();
        canvasGroup = gameObject.AddComponent<CanvasGroup>();

        GameObject backgroundObject = CreateUiObject("School Hallway", transform);
        Stretch(backgroundObject.GetComponent<RectTransform>());
        Image background = backgroundObject.AddComponent<Image>();
        background.sprite = Resources.Load<Sprite>("UI/LoadingSchoolHallway");
        background.color = Color.white;
        background.preserveAspect = false;
        background.raycastTarget = false;

        GameObject shadeObject = CreateUiObject("Readability Shade", transform);
        Stretch(shadeObject.GetComponent<RectTransform>());
        Image shade = shadeObject.AddComponent<Image>();
        shade.color = new Color(0.01f, 0.012f, 0.015f, 0.48f);
        shade.raycastTarget = true;

        GameObject lowerBandObject = CreateUiObject("Lower Band", transform);
        RectTransform lowerBandRect = lowerBandObject.GetComponent<RectTransform>();
        lowerBandRect.anchorMin = Vector2.zero;
        lowerBandRect.anchorMax = new Vector2(1f, 0.3f);
        lowerBandRect.offsetMin = Vector2.zero;
        lowerBandRect.offsetMax = Vector2.zero;
        lowerBandObject.AddComponent<Image>().color = new Color(0.015f, 0.018f, 0.02f, 0.94f);

        mapTitle = CreateText("Map Title", lowerBandObject.transform, 54f, FontStyles.Bold);
        RectTransform titleRect = mapTitle.rectTransform;
        titleRect.anchorMin = new Vector2(0.055f, 0.44f);
        titleRect.anchorMax = new Vector2(0.7f, 0.9f);
        titleRect.offsetMin = Vector2.zero;
        titleRect.offsetMax = Vector2.zero;
        mapTitle.alignment = TextAlignmentOptions.BottomLeft;
        mapTitle.color = new Color(0.94f, 0.92f, 0.86f, 1f);

        statusText = CreateText("Status", lowerBandObject.transform, 20f, FontStyles.Bold);
        RectTransform statusRect = statusText.rectTransform;
        statusRect.anchorMin = new Vector2(0.055f, 0.25f);
        statusRect.anchorMax = new Vector2(0.7f, 0.44f);
        statusRect.offsetMin = Vector2.zero;
        statusRect.offsetMax = Vector2.zero;
        statusText.alignment = TextAlignmentOptions.MidlineLeft;
        statusText.color = DefaultStatusColor;

        GameObject progressTrackObject = CreateUiObject("Progress Track", lowerBandObject.transform);
        RectTransform trackRect = progressTrackObject.GetComponent<RectTransform>();
        trackRect.anchorMin = new Vector2(0.055f, 0.13f);
        trackRect.anchorMax = new Vector2(0.945f, 0.17f);
        trackRect.offsetMin = Vector2.zero;
        trackRect.offsetMax = Vector2.zero;
        progressTrackObject.AddComponent<Image>().color = new Color(0.2f, 0.22f, 0.22f, 1f);

        GameObject fillObject = CreateUiObject("Progress", progressTrackObject.transform);
        Stretch(fillObject.GetComponent<RectTransform>());
        progressFill = fillObject.AddComponent<Image>();
        progressFill.color = new Color(0.82f, 0.13f, 0.08f, 1f);
        progressFill.type = Image.Type.Filled;
        progressFill.fillMethod = Image.FillMethod.Horizontal;
        progressFill.fillOrigin = 0;
        progressFill.fillAmount = 0f;
        progressFill.raycastTarget = false;
    }

    private void SetVisible(bool visible)
    {
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, float size, FontStyles style)
    {
        GameObject target = CreateUiObject(name, parent);
        TextMeshProUGUI text = target.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.fontStyle = style;
        text.characterSpacing = 0f;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject target = new GameObject(name, typeof(RectTransform));
        target.layer = LayerMask.NameToLayer("UI");
        target.transform.SetParent(parent, false);
        return target;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
