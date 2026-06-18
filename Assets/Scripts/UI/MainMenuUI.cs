using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Programmatic main-menu front-end for "School Of The Dead".
/// Built entirely in code (uGUI + TextMeshPro) so it does not depend on fragile
/// scene-YAML wiring. Spawns itself automatically when the "MainMenu" scene loads
/// (see <see cref="Bootstrap"/>) and renders on a high-sortingOrder canvas so it is
/// authoritative over any leftover UI in the scene.
///
/// Buttons:
///   PLAY        -> loads the gameplay scene ("SchoolOfTheDead").
///   PLAY ONLINE -> opens the MultiplayerMenuController overlay (host/join/lobby).
///   SETTINGS    -> opens the SettingsMenuController overlay.
///   QUIT        -> quits the application.
///
/// Visual language matches MultiplayerMenuController: near-black background, red accent,
/// amber warm highlights, off-white text.
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    // ----- Shared aesthetic (mirrors MultiplayerMenuController) -----
    private static readonly Color BackgroundColor = new Color(0.04f, 0.04f, 0.05f, 1f);
    private static readonly Color AccentColor = new Color(0.79f, 0.13f, 0.08f, 1f);
    private static readonly Color AccentPressColor = new Color(0.55f, 0.08f, 0.05f, 1f);
    private static readonly Color WarmColor = new Color(0.95f, 0.62f, 0.18f, 1f);
    private static readonly Color TextColor = new Color(0.92f, 0.90f, 0.86f, 1f);
    private static readonly Color ButtonIdleColor = new Color(0.13f, 0.13f, 0.15f, 0.95f);
    private static readonly Color ButtonDisabledColor = new Color(0.10f, 0.10f, 0.11f, 0.6f);

    private const string MenuSceneName = "MainMenu";
    private const string GameplaySceneName = "SchoolOfTheDead";
    private const int CanvasSortingOrder = 100;

    private static MainMenuUI _instance;

    private Canvas _canvas;
    private GameObject _mainPanel;       // the title + button column
    private Button _firstButton;

    private GameObject _multiplayerOverlay;
    private GameObject _settingsOverlay;

    // ------------------------------------------------------------------
    // Bootstrap: spawn automatically in the MainMenu scene only.
    // ------------------------------------------------------------------
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        // Avoid duplicate subscriptions across domain reloads.
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;

        // AfterSceneLoad fires once the first scene is already active, so handle it now.
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
            // Left the menu: tear down any lingering instance so it never shows in gameplay.
            if (_instance != null)
            {
                Destroy(_instance.gameObject);
                _instance = null;
            }
            return;
        }

        if (_instance != null)
        {
            return; // already present
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
        // Escape closes any open overlay and returns to the main menu.
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            bool anyOverlayOpen =
                (_multiplayerOverlay != null && _multiplayerOverlay.activeSelf) ||
                (_settingsOverlay != null && _settingsOverlay.activeSelf);

            if (anyOverlayOpen)
            {
                CloseOverlays();
                ShowMainMenu();
            }
        }
    }

    // ------------------------------------------------------------------
    // Canvas / EventSystem
    // ------------------------------------------------------------------
    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            // Scene-local EventSystem; the legacy StandaloneInputModule matches the
            // project's legacy Input Manager setup.
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }
    }

    private void BuildCanvas()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = CanvasSortingOrder; // render above any leftover scene canvas

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();
    }

    // ------------------------------------------------------------------
    // Main menu construction
    // ------------------------------------------------------------------
    private void BuildMainMenu()
    {
        _mainPanel = CreateFullScreen("MainPanel", transform);

        // Full-screen gritty background.
        var bg = _mainPanel.AddComponent<Image>();
        bg.color = BackgroundColor;
        bg.raycastTarget = true;

        // Subtle dark vignette band behind the content column for readability.
        var content = CreateChild("Content", _mainPanel.transform);
        var contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0.5f, 0.5f);
        contentRect.anchorMax = new Vector2(0.5f, 0.5f);
        contentRect.pivot = new Vector2(0.5f, 0.5f);
        contentRect.sizeDelta = new Vector2(640f, 760f);
        contentRect.anchoredPosition = Vector2.zero;

        var layout = content.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.spacing = 18f;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.padding = new RectOffset(0, 0, 0, 0);

        // Title.
        var title = CreateLabel("Title", content.transform, "SCHOOL OF THE DEAD", 64, FontStyles.Bold);
        title.color = TextColor;
        title.alignment = TextAlignmentOptions.Center;
        SetLayoutHeight(title.gameObject, 110f);

        // Accent rule under the title.
        var rule = CreateChild("Rule", content.transform);
        var ruleImg = rule.AddComponent<Image>();
        ruleImg.color = AccentColor;
        SetLayoutHeight(rule, 4f);

        // Subtitle.
        var subtitle = CreateLabel("Subtitle", content.transform, "SURVIVE THE HALLS", 22, FontStyles.Normal);
        subtitle.color = WarmColor;
        subtitle.alignment = TextAlignmentOptions.Center;
        subtitle.characterSpacing = 6f;
        SetLayoutHeight(subtitle.gameObject, 40f);

        // Spacer before buttons.
        var spacer = CreateChild("Spacer", content.transform);
        SetLayoutHeight(spacer, 30f);

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
        HideMainMenu();

        if (_multiplayerOverlay == null)
        {
            // Build lazily. Returning from BACK re-shows the main menu.
            _multiplayerOverlay = MultiplayerMenuController.Create(transform, ReturnToMainMenu);
        }

        if (_multiplayerOverlay != null)
        {
            _multiplayerOverlay.SetActive(true);
        }
        else
        {
            // Overlay unavailable for some reason; fall back to the menu.
            ShowMainMenu();
        }
    }

    private void OnSettings()
    {
        HideMainMenu();

        if (_settingsOverlay == null)
        {
            _settingsOverlay = SettingsMenuController.Create(transform, ReturnToMainMenu);
        }

        if (_settingsOverlay != null)
        {
            _settingsOverlay.SetActive(true);
        }
        else
        {
            ShowMainMenu();
        }
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
    }

    private void ShowMainMenu()
    {
        // Only one overlay visible at a time: ensure overlays are hidden.
        CloseOverlays();

        if (_mainPanel != null)
        {
            _mainPanel.SetActive(true);
        }

        // Keyboard/controller navigation: select the first button.
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
    // UI builder helpers (matching MultiplayerMenuController's static pattern)
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
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        return label;
    }

    private Button CreateMenuButton(Transform parent, string text, Action onClick)
    {
        var go = CreateChild(text + "Button", parent);
        SetLayoutHeight(go, 66f);

        var img = go.AddComponent<Image>();
        img.color = ButtonIdleColor;

        var button = go.AddComponent<Button>();
        button.targetGraphic = img;
        button.transition = Selectable.Transition.ColorTint;

        var colors = button.colors;
        colors.normalColor = ButtonIdleColor;
        colors.highlightedColor = AccentColor;
        colors.pressedColor = AccentPressColor;
        colors.selectedColor = AccentColor;
        colors.disabledColor = ButtonDisabledColor;
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.1f;
        button.colors = colors;

        // Left accent stripe for a gritty COD-zombies feel.
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

        var label = CreateLabel("Label", go.transform, text, 30, FontStyles.Bold);
        label.color = TextColor;
        label.alignment = TextAlignmentOptions.Center;
        var labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

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
