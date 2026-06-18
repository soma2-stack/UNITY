using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Runtime main-menu front-end for "School Of The Dead".
///
/// The MainMenu scene already provides the atmosphere (a VideoPlayer background and
/// a "Bloody Overlay"). This controller renders a single clean, COD-zombies styled
/// button column OVER that video (never covering it with an opaque panel) and hosts
/// the online + settings overlays on a top canvas with a dim scrim.
///
/// It also repairs two things that broke the old setup:
///   * Dead clicks  -> ensures the EventSystem has a working legacy StandaloneInputModule.
///   * Double menu  -> disables the leftover scene menu widgets (buttons / panels / title)
///                     while KEEPING the video background and blood overlay.
///
/// Spawns automatically when the "MainMenu" scene loads and tears itself down elsewhere.
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    // ----- Aesthetic -----
    private static readonly Color AccentColor = new Color(0.79f, 0.13f, 0.08f, 1f);
    private static readonly Color AccentPressColor = new Color(0.55f, 0.08f, 0.05f, 1f);
    private static readonly Color WarmColor = new Color(0.95f, 0.62f, 0.18f, 1f);
    private static readonly Color TextColor = new Color(0.93f, 0.91f, 0.87f, 1f);
    private static readonly Color ButtonIdleColor = new Color(0.08f, 0.08f, 0.09f, 0.72f);
    private static readonly Color ButtonHoverColor = new Color(0.79f, 0.13f, 0.08f, 0.92f);
    private static readonly Color ButtonDisabledColor = new Color(0.10f, 0.10f, 0.11f, 0.5f);
    private static readonly Color ScrimColor = new Color(0.02f, 0.02f, 0.03f, 0.88f);
    private static readonly Color LegibilityScrim = new Color(0f, 0f, 0f, 0.55f);

    private const string MenuSceneName = "MainMenu";
    private const string GameplaySceneName = "SchoolOfTheDead";
    private const int CanvasSortingOrder = 100;

    // Leftover scene menu widgets to hide so we don't show a double menu.
    // The VideoPlayer background and the "Bloody Overlay" are intentionally NOT listed.
    private static readonly string[] LegacyMenuObjects =
    {
        "ButtonContanier", // (sic) the old left button column
        "GameTitle",
        "ONLINEpanel",
        "SettingsPanel",
    };

    private static MainMenuUI _instance;

    private Canvas _canvas;
    private GameObject _mainPanel;       // the title + button column (transparent over the video)
    private GameObject _overlayHost;     // parent for the online / settings overlays
    private GameObject _scrim;           // full-screen dim behind an open overlay
    private Button _firstButton;

    private GameObject _multiplayerOverlay;
    private GameObject _settingsOverlay;

    // ------------------------------------------------------------------
    // Bootstrap: spawn automatically in the MainMenu scene only.
    // ------------------------------------------------------------------
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SpawnIfMenuScene(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SpawnIfMenuScene(scene);
    }

    private static void SpawnIfMenuScene(Scene scene)
    {
        if (scene.name != MenuSceneName)
        {
            if (_instance != null)
            {
                Destroy(_instance.gameObject);
                _instance = null;
            }
            return;
        }

        if (_instance != null)
        {
            return;
        }

        var go = new GameObject("MainMenuUI (Runtime)");
        _instance = go.AddComponent<MainMenuUI>();
    }

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------
    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;

        EnsureEventSystem();
        HideLegacyMenu();
        BuildCanvas();
        BuildMainMenu();
        ShowMainMenu();
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            bool anyOverlayOpen =
                (_multiplayerOverlay != null && _multiplayerOverlay.activeSelf) ||
                (_settingsOverlay != null && _settingsOverlay.activeSelf);

            if (anyOverlayOpen)
            {
                ReturnToMainMenu();
            }
        }
    }

    // ------------------------------------------------------------------
    // Repair: EventSystem + leftover scene menu
    // ------------------------------------------------------------------
    private static void EnsureEventSystem()
    {
        var existing = FindFirstObjectByType<EventSystem>();
        if (existing == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            return;
        }

        existing.gameObject.SetActive(true);
        existing.enabled = true;

        // This project uses the legacy Input Manager. If the EventSystem has no
        // StandaloneInputModule (e.g. it carries a non-functional new-Input-System
        // module instead), UI clicks silently do nothing. Disable any other modules
        // and guarantee a working legacy one.
        var legacy = existing.GetComponent<StandaloneInputModule>();
        if (legacy == null)
        {
            foreach (var module in existing.GetComponents<BaseInputModule>())
            {
                module.enabled = false;
            }
            legacy = existing.gameObject.AddComponent<StandaloneInputModule>();
        }
        legacy.enabled = true;
    }

    private static void HideLegacyMenu()
    {
        var scene = SceneManager.GetActiveScene();
        foreach (string objectName in LegacyMenuObjects)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = FindDeep(root.transform, objectName);
                if (found != null)
                {
                    found.gameObject.SetActive(false);
                    break;
                }
            }
        }
    }

    private static Transform FindDeep(Transform parent, string targetName)
    {
        if (parent.name == targetName)
        {
            return parent;
        }
        foreach (Transform child in parent)
        {
            var result = FindDeep(child, targetName);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    // ------------------------------------------------------------------
    // Canvas
    // ------------------------------------------------------------------
    private void BuildCanvas()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = CanvasSortingOrder; // above the scene's video canvas

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        // Overlay host (online / settings) lives above the menu column.
        _overlayHost = CreateFullScreen("OverlayHost", transform);

        // Dim scrim shown only while an overlay is open (blocks clicks + focuses the panel).
        _scrim = CreateFullScreen("Scrim", _overlayHost.transform);
        var scrimImg = _scrim.AddComponent<Image>();
        scrimImg.color = ScrimColor;
        scrimImg.raycastTarget = true;
        _scrim.SetActive(false);
    }

    // ------------------------------------------------------------------
    // Main menu construction (transparent over the video, left-aligned)
    // ------------------------------------------------------------------
    private void BuildMainMenu()
    {
        _mainPanel = CreateFullScreen("MainPanel", transform);
        // NOTE: no opaque background here on purpose - the scene's video shows through.

        // Soft dark gradient down the left third so the text stays readable over the video.
        var legibility = CreateChild("LeftScrim", _mainPanel.transform);
        var legRect = legibility.GetComponent<RectTransform>();
        legRect.anchorMin = new Vector2(0f, 0f);
        legRect.anchorMax = new Vector2(0f, 1f);
        legRect.pivot = new Vector2(0f, 0.5f);
        legRect.sizeDelta = new Vector2(760f, 0f);
        legRect.anchoredPosition = Vector2.zero;
        var legImg = legibility.AddComponent<Image>();
        legImg.color = LegibilityScrim;
        legImg.raycastTarget = false;

        // Left-aligned content column (classic zombies menu layout).
        var content = CreateChild("Content", _mainPanel.transform);
        var contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 0.5f);
        contentRect.anchorMax = new Vector2(0f, 0.5f);
        contentRect.pivot = new Vector2(0f, 0.5f);
        contentRect.sizeDelta = new Vector2(440f, 720f);
        contentRect.anchoredPosition = new Vector2(110f, 0f);

        var layout = content.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.spacing = 14f;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // Title.
        var title = CreateLabel("Title", content.transform, "SCHOOL OF\nTHE DEAD", 62, FontStyles.Bold);
        title.color = TextColor;
        title.alignment = TextAlignmentOptions.Left;
        title.lineSpacing = -8f;
        SetLayoutHeight(title.gameObject, 168f);

        // Accent rule under the title.
        var rule = CreateChild("Rule", content.transform);
        rule.AddComponent<Image>().color = AccentColor;
        SetLayoutHeight(rule, 4f);

        // Subtitle.
        var subtitle = CreateLabel("Subtitle", content.transform, "SURVIVE THE HALLS", 20, FontStyles.Normal);
        subtitle.color = WarmColor;
        subtitle.alignment = TextAlignmentOptions.Left;
        subtitle.characterSpacing = 8f;
        SetLayoutHeight(subtitle.gameObject, 34f);

        // Spacer.
        SetLayoutHeight(CreateChild("Spacer", content.transform), 26f);

        // Buttons.
        _firstButton = CreateMenuButton(content.transform, "PLAY", OnPlaySolo);
        CreateMenuButton(content.transform, "PLAY ONLINE", OnPlayOnline);
        CreateMenuButton(content.transform, "SETTINGS", OnSettings);
        CreateMenuButton(content.transform, "QUIT", OnQuit);
    }

    // ------------------------------------------------------------------
    // Button actions
    // ------------------------------------------------------------------
    private void OnPlaySolo()
    {
        var manager = FindFirstObjectByType<MainMenuManager>();
        if (manager != null)
        {
            manager.PlayGame();
        }
        else
        {
            SceneManager.LoadScene(GameplaySceneName);
        }
    }

    private void OnPlayOnline()
    {
        OpenOverlay(ref _multiplayerOverlay, MultiplayerMenuController.Create);
    }

    private void OnSettings()
    {
        OpenOverlay(ref _settingsOverlay, SettingsMenuController.Create);
    }

    private void OnQuit()
    {
        var manager = FindFirstObjectByType<MainMenuManager>();
        if (manager != null)
        {
            manager.QuitGame();
            return;
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------------
    // Overlay / menu visibility
    // ------------------------------------------------------------------
    private void OpenOverlay(ref GameObject overlay, Func<Transform, Action, GameObject> factory)
    {
        HideMainMenu();
        if (_scrim != null)
        {
            _scrim.SetActive(true);
        }

        if (overlay == null && factory != null)
        {
            overlay = factory(_overlayHost.transform, ReturnToMainMenu);
        }

        if (overlay != null)
        {
            overlay.transform.SetAsLastSibling(); // render above the scrim
            overlay.SetActive(true);
        }
        else
        {
            ReturnToMainMenu();
        }
    }

    private void ReturnToMainMenu()
    {
        CloseOverlays();
        ShowMainMenu();
    }

    private void CloseOverlays()
    {
        if (_multiplayerOverlay != null)
        {
            _multiplayerOverlay.SetActive(false);
        }
        if (_settingsOverlay != null)
        {
            _settingsOverlay.SetActive(false);
        }
        if (_scrim != null)
        {
            _scrim.SetActive(false);
        }
    }

    private void ShowMainMenu()
    {
        CloseOverlays();

        if (_mainPanel != null)
        {
            _mainPanel.SetActive(true);
        }

        if (_firstButton != null && EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(_firstButton.gameObject);
        }
    }

    private void HideMainMenu()
    {
        if (_mainPanel != null)
        {
            _mainPanel.SetActive(false);
        }
    }

    // ------------------------------------------------------------------
    // UI builder helpers
    // ------------------------------------------------------------------
    private static GameObject CreateFullScreen(string name, Transform parent)
    {
        var go = CreateChild(name, parent);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return go;
    }

    private static GameObject CreateChild(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static TextMeshProUGUI CreateLabel(string name, Transform parent, string text, float fontSize, FontStyles style)
    {
        var go = CreateChild(name, parent);
        var label = go.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.color = TextColor;
        label.alignment = TextAlignmentOptions.Left;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        return label;
    }

    private Button CreateMenuButton(Transform parent, string text, Action onClick)
    {
        var go = CreateChild(text + "Button", parent);
        SetLayoutHeight(go, 60f);

        var img = go.AddComponent<Image>();
        img.color = ButtonIdleColor;

        var button = go.AddComponent<Button>();
        button.targetGraphic = img;
        button.transition = Selectable.Transition.ColorTint;

        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        colors.selectedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
        colors.disabledColor = ButtonDisabledColor;
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        // Left accent stripe (turns the menu into a gritty zombies list).
        var stripe = CreateChild("Stripe", go.transform);
        var stripeRect = stripe.GetComponent<RectTransform>();
        stripeRect.anchorMin = new Vector2(0f, 0f);
        stripeRect.anchorMax = new Vector2(0f, 1f);
        stripeRect.pivot = new Vector2(0f, 0.5f);
        stripeRect.sizeDelta = new Vector2(6f, 0f);
        stripeRect.anchoredPosition = Vector2.zero;
        var stripeImg = stripe.AddComponent<Image>();
        stripeImg.color = AccentColor;
        stripeImg.raycastTarget = false;

        var label = CreateLabel("Label", go.transform, text, 28, FontStyles.Bold);
        label.color = TextColor;
        label.alignment = TextAlignmentOptions.Left;
        var labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(28f, 0f);
        labelRect.offsetMax = new Vector2(-12f, 0f);

        if (onClick != null)
        {
            button.onClick.AddListener(() => onClick());
        }

        return button;
    }

    private static void SetLayoutHeight(GameObject go, float height)
    {
        var le = go.GetComponent<LayoutElement>();
        if (le == null)
        {
            le = go.AddComponent<LayoutElement>();
        }
        le.preferredHeight = height;
        le.minHeight = height;
        le.flexibleHeight = 0f;
    }
}
