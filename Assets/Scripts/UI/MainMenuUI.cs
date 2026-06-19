using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Lightweight controller for the hand-built MainMenu scene.
///
/// It does NOT draw its own menu and does NOT hide the scene's menu - the
/// designer's textured SOLO / ONLINE / SETTINGS / QUIT buttons, GameTitle,
/// VideoPlayer background and Bloody Overlay all stay exactly as authored.
///
/// What it does:
///   * Repairs dead clicks by guaranteeing the EventSystem has an enabled legacy
///     StandaloneInputModule.
///   * Re-wires the existing scene buttons to the correct actions and opens the
///     improved online / settings overlays (replacing the old placeholder panels).
///   * Hosts those overlays on a top canvas with a dim scrim.
///
/// Spawns automatically when the "MainMenu" scene loads and tears itself down
/// elsewhere.
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    private const string MenuSceneName = "MainMenu";
    private const string GameplaySceneName = "SchoolOfTheDead";
    private const int CanvasSortingOrder = 100;

    private static readonly Color ScrimColor = new Color(0.02f, 0.02f, 0.03f, 0.82f);

    // Scene button object names (from the authored MainMenu scene).
    private const string SoloButtonName = "SoloButton";
    private const string OnlineButtonName = "Online";
    private const string SettingsButtonName = "Settings";
    private const string QuitButtonName = "QuitButton";

    // Old placeholder panels we replace with the improved overlays.
    private static readonly string[] LegacyPanels = { "ONLINEpanel", "SettingsPanel" };

    private static MainMenuUI _instance;

    private Canvas _canvas;
    private GameObject _overlayHost;
    private GameObject _scrim;
    private GameObject _multiplayerOverlay;
    private GameObject _settingsOverlay;

    // ------------------------------------------------------------------
    // Bootstrap
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

        var go = new GameObject("MainMenuController (Runtime)");
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
        BuildOverlayHost();
        WireSceneMenu();
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
    // EventSystem repair (fixes "no buttons work")
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

        // Legacy Input Manager project: make sure a working StandaloneInputModule is
        // present and enabled. If the EventSystem carries a non-functional module
        // (e.g. the new Input System one), disable the others and add the legacy one.
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

    // ------------------------------------------------------------------
    // Wire the existing scene buttons
    // ------------------------------------------------------------------
    private void WireSceneMenu()
    {
        RewireButton(SoloButtonName, OnPlaySolo);
        RewireButton(OnlineButtonName, OnPlayOnline);
        RewireButton(SettingsButtonName, OnSettings);
        RewireButton(QuitButtonName, OnQuit);

        // The old placeholder panels are replaced by the improved overlays.
        foreach (string panel in LegacyPanels)
        {
            var t = FindInScene(panel);
            if (t != null)
            {
                t.gameObject.SetActive(false);
            }
        }
    }

    private void RewireButton(string objectName, Action action)
    {
        var t = FindInScene(objectName);
        if (t == null)
        {
            Debug.LogWarning($"[MainMenuUI] Scene button '{objectName}' not found - cannot wire it.");
            return;
        }

        var button = t.GetComponent<Button>();
        if (button == null)
        {
            Debug.LogWarning($"[MainMenuUI] '{objectName}' has no Button component.");
            return;
        }

        // Turn off any Inspector-wired (persistent) onClick calls so the button does
        // exactly what we want, then add our handler. This keeps the button's visuals
        // (normal / bloody sprite swap) untouched - only its click action changes.
        int persistentCount = button.onClick.GetPersistentEventCount();
        for (int i = 0; i < persistentCount; i++)
        {
            button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
        }
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => action());
    }

    private Transform FindInScene(string objectName)
    {
        var scene = SceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
        {
            // Skip our own overlay host so we never match controls we created.
            if (root == gameObject)
            {
                continue;
            }
            var found = FindDeep(root.transform, objectName);
            if (found != null)
            {
                return found;
            }
        }
        return null;
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
    // Overlay host
    // ------------------------------------------------------------------
    private void BuildOverlayHost()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = CanvasSortingOrder;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        _overlayHost = new GameObject("OverlayHost", typeof(RectTransform));
        _overlayHost.transform.SetParent(transform, false);
        StretchFull(_overlayHost.GetComponent<RectTransform>());

        _scrim = new GameObject("Scrim", typeof(RectTransform));
        _scrim.transform.SetParent(_overlayHost.transform, false);
        StretchFull(_scrim.GetComponent<RectTransform>());
        var scrimImg = _scrim.AddComponent<Image>();
        scrimImg.color = ScrimColor;
        scrimImg.raycastTarget = true;
        _scrim.SetActive(false);
    }

    private static void StretchFull(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
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
    // Overlay visibility
    // ------------------------------------------------------------------
    private void OpenOverlay(ref GameObject overlay, Func<Transform, Action, GameObject> factory)
    {
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
            overlay.transform.SetAsLastSibling(); // above the scrim
            overlay.SetActive(true);
        }
        else
        {
            ReturnToMainMenu();
        }
    }

    private void ReturnToMainMenu()
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
}
