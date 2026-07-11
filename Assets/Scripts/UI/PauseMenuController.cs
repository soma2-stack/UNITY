using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Runtime pause menu + match-stats overlay for School Of The Dead. Self-bootstraps in the
/// "SchoolOfTheDead" scene (mirrors GameOverController) and is destroyed when leaving it.
///
/// PAUSE MODEL (host-authoritative in multiplayer):
///  - SOLO: Escape opens a local menu and freezes the game (Time.timeScale = 0). Resume restores.
///  - MULTIPLAYER: Escape opens a LOCAL menu only — it does NOT pause the match; the match keeps
///    running for that client (their own input is blocked while their menu is open). Only the HOST
///    can pause the whole match, via the "PAUSE GAME" button, which routes through
///    <see cref="NetworkGameplayCoordinator.SetHostPause"/> (server-authoritative, synced to every
///    client + replayed to late joiners). When the host pauses, every peer freezes
///    (Time.timeScale = 0) and shows a "MATCH PAUSED BY HOST" banner. Clients cannot pause the match.
///
/// Time.timeScale = 0 is safe here because Unity keeps calling Update() (so the NGO NetworkManager /
/// transport keep polling and the connection stays alive; only tick/physics advancement freezes).
///
/// UI is programmatic uGUI + TextMeshPro, matching the Settings / Online Co-op style. Settings is the
/// EXISTING <see cref="SettingsMenuController"/> (reused, not duplicated); opened from here it returns
/// to the pause menu. No gameplay systems are modified — local input is blocked by toggling the
/// enabled flag of the local player's Camera/Movement/Weapon components (reversible).
/// </summary>
public class PauseMenuController : MonoBehaviour
{
    private const string GameplayScene = "SchoolOfTheDead";
    private const string MainMenuScene = "MainMenu";

    // ---- Palette (matches SettingsMenuController / Online Co-op) ----
    private static readonly Color ColorDim = new Color(0f, 0f, 0f, 0.72f);
    private static readonly Color ColorPanel = new Color(0.06f, 0.065f, 0.08f, 0.97f);
    private static readonly Color ColorStatsBox = new Color(0.09f, 0.10f, 0.12f, 0.96f);
    private static readonly Color ColorAccentRed = new Color(0.79f, 0.13f, 0.08f, 1f);
    private static readonly Color ColorAmber = new Color(0.95f, 0.73f, 0.27f, 1f);
    private static readonly Color ColorTextOffWhite = new Color(0.92f, 0.90f, 0.86f, 1f);
    private static readonly Color ColorTextDim = new Color(0.62f, 0.60f, 0.57f, 1f);
    private static readonly Color ColorButton = new Color(0.13f, 0.13f, 0.15f, 1f);
    private static readonly Color ColorButtonHover = new Color(0.20f, 0.20f, 0.23f, 1f);

    private static PauseMenuController _instance;

    private GameObject _dim;
    private GameObject _pausePanel;
    private GameObject _hostPausedBanner;
    private GameObject _settingsOverlay;

    private Button _hostPauseButton;
    private TMP_Text _hostPauseLabel;
    private GameObject _restartButton;
    private TMP_Text _mainMenuLabel;

    private TMP_Text _statRound, _statZombies, _statPoints, _statTeam, _statHealth, _statWeapon, _statKills;

    private bool _built;
    private bool _localMenuOpen;
    private bool _settingsOpen;
    private bool _inputBlocked;

    // Cached local-player input components so we can restore their exact enabled state on close.
    private CoDCamera _cachedCamera;
    private PlayerMovement _cachedMovement;
    private WeaponController _cachedWeapon;
    private bool _camWasEnabled, _moveWasEnabled, _weaponWasEnabled;

    private RoundManager _round;
    private ZombieSpawner _spawner;

    // ---------------------------------------------------------------------
    // Bootstrap (gameplay scene only)
    // ---------------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnAnySceneLoaded;
        SceneManager.sceneLoaded += OnAnySceneLoaded;
        SpawnIfGameplayScene(SceneManager.GetActiveScene());
    }

    private static void OnAnySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SpawnIfGameplayScene(scene);
    }

    private static void SpawnIfGameplayScene(Scene scene)
    {
        if (scene.name != GameplayScene)
        {
            if (_instance != null)
            {
                Destroy(_instance.gameObject);
                _instance = null;
            }
            return;
        }

        if (_instance != null || FindFirstObjectByType<PauseMenuController>() != null)
        {
            return;
        }

        var go = new GameObject("PauseMenuController (Runtime)");
        _instance = go.AddComponent<PauseMenuController>();
    }

    private void Start()
    {
        try
        {
            Build();
            _built = true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[PauseMenu] Failed to build overlay: " + e.Message);
            return;
        }

        NetworkGameplayCoordinator.HostPauseChanged += OnHostPauseChanged;
        // Sync to any pause already in effect (e.g. joined into a host-paused match).
        OnHostPauseChanged(NetworkGameplayCoordinator.IsHostPaused);
    }

    private void OnDestroy()
    {
        NetworkGameplayCoordinator.HostPauseChanged -= OnHostPauseChanged;
        if (_instance == this)
        {
            _instance = null;
        }
        // Safety net: never leave the game frozen when this controller goes away.
        Time.timeScale = 1f;
    }

    private void Update()
    {
        if (!_built)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (_settingsOpen)
            {
                CloseSettingsReturnToPause();
            }
            else if (_localMenuOpen)
            {
                CloseLocalMenu();
            }
            else
            {
                OpenLocalMenu();
            }
        }

        if (_localMenuOpen && !_settingsOpen)
        {
            RefreshStats();
        }
    }

    // ---------------------------------------------------------------------
    // Open / close local menu
    // ---------------------------------------------------------------------

    private void OpenLocalMenu()
    {
        _localMenuOpen = true;
        _dim.SetActive(true);
        _pausePanel.SetActive(true);
        ConfigureButtonsForRole();
        RefreshState();
        RefreshStats();
    }

    private void CloseLocalMenu()
    {
        _localMenuOpen = false;
        _dim.SetActive(false);
        _pausePanel.SetActive(false);
        RefreshState();
    }

    // Single place that applies the derived state: input blocking, freeze, and cursor.
    private void RefreshState()
    {
        ApplyInputBlocking();
        ApplyTimeScale();
        SetCursor(!_localMenuOpen); // cursor is free only while the local menu is open
    }

    // A REAL multiplayer match — i.e. networked AND not a single-player local-host solo run. Solo is
    // implemented as a local host, so IsNetworkActive is true there; SoloModeState lets us treat it
    // as solo (immediate pause, no host-pause button).
    private static bool IsNetworkedMatch =>
        NetworkGameplayCoordinator.IsNetworkActive && !SoloModeState.IsSolo;

    // Freeze in SOLO whenever the local menu is open, or (solo or MP) whenever the HOST has paused.
    // A client's own local menu never freezes the networked match.
    private void ApplyTimeScale()
    {
        bool networked = IsNetworkedMatch;
        bool freeze = NetworkGameplayCoordinator.IsHostPaused || (!networked && _localMenuOpen);
        Time.timeScale = freeze ? 0f : 1f;
    }

    // Block this peer's own input while its local menu is open OR the host has paused the match.
    private void ApplyInputBlocking()
    {
        bool blocked = _localMenuOpen || NetworkGameplayCoordinator.IsHostPaused;
        if (blocked && !_inputBlocked)
        {
            DisableLocalInput();
            _inputBlocked = true;
        }
        else if (!blocked && _inputBlocked)
        {
            RestoreLocalInput();
            _inputBlocked = false;
        }
    }

    private void OnHostPauseChanged(bool paused)
    {
        if (_hostPausedBanner != null)
        {
            _hostPausedBanner.SetActive(paused);
        }
        if (_hostPauseButton != null && _localMenuOpen)
        {
            ConfigureButtonsForRole();
        }
        RefreshState();
    }

    // ---------------------------------------------------------------------
    // Local input gating (reversible; toggles .enabled only, no script edits)
    // ---------------------------------------------------------------------

    private void DisableLocalInput()
    {
        _cachedCamera = LocalPlayer.Camera;
        _cachedMovement = LocalPlayer.Movement;
        _cachedWeapon = LocalPlayer.Weapon;

        if (_cachedCamera != null) { _camWasEnabled = _cachedCamera.enabled; _cachedCamera.enabled = false; }
        if (_cachedMovement != null) { _moveWasEnabled = _cachedMovement.enabled; _cachedMovement.enabled = false; }
        if (_cachedWeapon != null) { _weaponWasEnabled = _cachedWeapon.enabled; _cachedWeapon.enabled = false; }
    }

    private void RestoreLocalInput()
    {
        if (_cachedCamera != null) { _cachedCamera.enabled = _camWasEnabled; }
        if (_cachedMovement != null) { _cachedMovement.enabled = _moveWasEnabled; }
        if (_cachedWeapon != null) { _cachedWeapon.enabled = _weaponWasEnabled; }
        _cachedCamera = null;
        _cachedMovement = null;
        _cachedWeapon = null;
    }

    private static void SetCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    // ---------------------------------------------------------------------
    // Buttons
    // ---------------------------------------------------------------------

    private void ConfigureButtonsForRole()
    {
        bool networked = IsNetworkedMatch;
        bool isServer = NetworkGameplayCoordinator.IsServer;

        // Host "Pause Game" button: multiplayer only; disabled + labelled for clients.
        _hostPauseButton.gameObject.SetActive(networked);
        if (networked)
        {
            if (isServer)
            {
                _hostPauseButton.interactable = true;
                _hostPauseLabel.text = NetworkGameplayCoordinator.IsHostPaused ? "RESUME GAME" : "PAUSE GAME";
                _hostPauseLabel.color = ColorTextOffWhite;
            }
            else
            {
                _hostPauseButton.interactable = false;
                _hostPauseLabel.text = "PAUSE — HOST ONLY";
                _hostPauseLabel.color = ColorTextDim;
            }
        }

        // Restart is host/solo only (a client shouldn't restart the whole match).
        _restartButton.SetActive(!networked || isServer);

        // In multiplayer a non-host leaves the match; solo/host returns to the main menu.
        _mainMenuLabel.text = (networked && !isServer) ? "LEAVE MATCH" : "MAIN MENU";
    }

    private void OnResume()
    {
        CloseLocalMenu();
    }

    private void OnHostPauseToggle()
    {
        if (!NetworkGameplayCoordinator.IsServer)
        {
            return;
        }
        NetworkGameplayCoordinator.SetHostPause(!NetworkGameplayCoordinator.IsHostPaused);
        ConfigureButtonsForRole();
    }

    private void OnSettings()
    {
        if (_settingsOverlay == null)
        {
            _settingsOverlay = SettingsMenuController.Create(transform, CloseSettingsReturnToPause);
        }
        _settingsOpen = true;
        _pausePanel.SetActive(false);
        _settingsOverlay.SetActive(true);
        _settingsOverlay.transform.SetAsLastSibling();
    }

    private void CloseSettingsReturnToPause()
    {
        _settingsOpen = false;
        if (_settingsOverlay != null)
        {
            _settingsOverlay.SetActive(false);
        }
        _pausePanel.SetActive(true);
    }

    // Close the overlay and restore input + time before leaving/reloading the scene.
    private void TearDownForExit()
    {
        _localMenuOpen = false;
        _settingsOpen = false;
        if (_dim != null) { _dim.SetActive(false); }
        if (_pausePanel != null) { _pausePanel.SetActive(false); }
        if (_settingsOverlay != null) { _settingsOverlay.SetActive(false); }
        RestoreLocalInput();
        _inputBlocked = false;
        Time.timeScale = 1f;
    }

    private void OnRestart()
    {
        TearDownForExit();
        ZombieAgent.ResetKillCount();

        if (NetworkGameplayCoordinator.IsNetworkActive)
        {
            NetworkGameplayCoordinator.RequestRestartMatch();
        }
        else
        {
            SceneManager.LoadScene(GameplayScene);
        }
    }

    private void OnMainMenu()
    {
        TearDownForExit();
        ZombieAgent.ResetKillCount();

        if (NetworkGameplayCoordinator.IsNetworkActive && MultiplayerSessionController.Instance != null)
        {
            _ = MultiplayerSessionController.Instance.LeaveAsync();
        }
        else
        {
            SceneManager.LoadScene(MainMenuScene);
        }
    }

    private void OnQuit()
    {
        TearDownForExit();
        Application.Quit();
    }

    // ---------------------------------------------------------------------
    // Stats (read-only)
    // ---------------------------------------------------------------------

    private void RefreshStats()
    {
        if (_round == null) { _round = FindFirstObjectByType<RoundManager>(); }
        if (_spawner == null) { _spawner = FindFirstObjectByType<ZombieSpawner>(); }

        if (_statRound != null)
        {
            _statRound.text = _round != null ? Mathf.Max(0, _round.CurrentRound).ToString() : "--";
        }
        if (_statZombies != null)
        {
            int left = CountZombies();
            _statZombies.text = left >= 0 ? left.ToString() : "--";
        }

        PlayerPoints points = PlayerPoints.Instance;
        if (_statPoints != null)
        {
            _statPoints.text = points != null ? points.Points.ToString("N0") : "0";
        }
        if (_statTeam != null)
        {
            _statTeam.text = TeamPoints(points).ToString("N0");
        }

        PlayerHealth health = LocalPlayer.Health;
        if (_statHealth != null)
        {
            _statHealth.text = health != null
                ? Mathf.Max(0, health.CurrentHealth) + " / " + Mathf.Max(1, health.maxHealth)
                : "--";
        }

        WeaponController weapon = LocalPlayer.Weapon;
        if (_statWeapon != null)
        {
            _statWeapon.text = weapon != null && weapon.HasWeapon
                ? weapon.CurrentWeaponName + "   " + weapon.CurrentMagazineAmmo + " / " + weapon.CurrentReserveAmmo
                : "—";
        }

        if (_statKills != null)
        {
            _statKills.text = ZombieAgent.TotalKillsThisRun.ToString();
        }
    }

    // Sum of every tracked player's CURRENT points (project has no separate lifetime-earned total).
    private static int TeamPoints(PlayerPoints points)
    {
        if (points == null)
        {
            return 0;
        }
        var table = points.Table;
        if (table == null || table.Count == 0)
        {
            return points.Points;
        }
        int total = 0;
        foreach (PlayerPoints.Entry entry in table)
        {
            total += entry.Points;
        }
        return total;
    }

    private int CountZombies()
    {
        bool networkedClient = NetworkGameplayCoordinator.IsNetworkActive && !NetworkGameplayCoordinator.IsServer;
        if (!networkedClient)
        {
            return _spawner != null ? Mathf.Max(0, _spawner.AliveCount + _spawner.RemainingToSpawn) : -1;
        }

        int alive = 0;
        ZombieAgent[] zombies = FindObjectsByType<ZombieAgent>(FindObjectsSortMode.None);
        foreach (ZombieAgent z in zombies)
        {
            if (z != null && !z.IsDead)
            {
                alive++;
            }
        }
        return alive;
    }

    // ---------------------------------------------------------------------
    // Build UI
    // ---------------------------------------------------------------------

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // above the HUD (50)

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();

        var root = gameObject.GetComponent<RectTransform>();

        // Full-screen dim that blocks clicks behind the menu (toggled with the panel).
        _dim = CreateImage("Dim", root, ColorDim).gameObject;
        Stretch(_dim.GetComponent<RectTransform>());
        _dim.GetComponent<Image>().raycastTarget = true;
        _dim.SetActive(false);

        BuildPausePanel(root);
        _pausePanel.SetActive(false); // hidden until the player presses Escape (was left visible at load)
        BuildHostPausedBanner(root);
    }

    private void BuildPausePanel(RectTransform root)
    {
        var panel = CreateImage("PausePanel", root, ColorPanel);
        var panelRect = panel.rectTransform;
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(900f, 600f);
        _pausePanel = panel.gameObject;

        var title = CreateText("Title", panel.transform, "PAUSED", 56, ColorTextOffWhite, TextAlignmentOptions.Center);
        title.fontStyle = FontStyles.Bold;
        var titleRect = title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -26f);
        titleRect.sizeDelta = new Vector2(-80f, 70f);

        var accent = CreateImage("TitleAccent", panel.transform, ColorAccentRed);
        var accentRect = accent.rectTransform;
        accentRect.anchorMin = new Vector2(0.5f, 1f);
        accentRect.anchorMax = new Vector2(0.5f, 1f);
        accentRect.pivot = new Vector2(0.5f, 1f);
        accentRect.anchoredPosition = new Vector2(0f, -98f);
        accentRect.sizeDelta = new Vector2(120f, 4f);

        BuildButtonColumn(panel.transform);
        BuildStatsColumn(panel.transform);
    }

    private void BuildButtonColumn(Transform panel)
    {
        var col = new GameObject("Buttons", typeof(RectTransform));
        col.transform.SetParent(panel, false);
        var colRect = col.GetComponent<RectTransform>();
        colRect.anchorMin = new Vector2(0f, 0.5f);
        colRect.anchorMax = new Vector2(0f, 0.5f);
        colRect.pivot = new Vector2(0f, 0.5f);
        colRect.sizeDelta = new Vector2(400f, 420f);
        colRect.anchoredPosition = new Vector2(40f, -40f);

        var layout = col.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateButton(col.transform, "RESUME", OnResume, ColorAccentRed, out _, 58f);
        CreateButton(col.transform, "SETTINGS", OnSettings, ColorAmber, out _, 54f);
        _hostPauseButton = CreateButton(col.transform, "PAUSE GAME", OnHostPauseToggle, ColorAccentRed, out _hostPauseLabel, 54f);
        _restartButton = CreateButton(col.transform, "RESTART MATCH", OnRestart, ColorButton, out _, 54f).gameObject;
        CreateButton(col.transform, "MAIN MENU", OnMainMenu, ColorButton, out _mainMenuLabel, 54f);
        CreateButton(col.transform, "QUIT TO DESKTOP", OnQuit, ColorButton, out _, 54f);
    }

    private void BuildStatsColumn(Transform panel)
    {
        var box = CreateImage("StatsBox", panel, ColorStatsBox);
        var boxRect = box.rectTransform;
        boxRect.anchorMin = new Vector2(1f, 0.5f);
        boxRect.anchorMax = new Vector2(1f, 0.5f);
        boxRect.pivot = new Vector2(1f, 0.5f);
        boxRect.sizeDelta = new Vector2(400f, 420f);
        boxRect.anchoredPosition = new Vector2(-40f, -40f);

        var heading = CreateText("StatsHeading", box.transform, "MATCH STATS", 22, ColorAccentRed, TextAlignmentOptions.Left);
        heading.fontStyle = FontStyles.Bold;
        heading.characterSpacing = 4f;
        var headingRect = heading.rectTransform;
        headingRect.anchorMin = new Vector2(0f, 1f);
        headingRect.anchorMax = new Vector2(1f, 1f);
        headingRect.pivot = new Vector2(0.5f, 1f);
        headingRect.anchoredPosition = new Vector2(0f, -18f);
        headingRect.sizeDelta = new Vector2(-40f, 32f);

        var rows = new GameObject("StatRows", typeof(RectTransform));
        rows.transform.SetParent(box.transform, false);
        var rowsRect = rows.GetComponent<RectTransform>();
        rowsRect.anchorMin = new Vector2(0f, 0f);
        rowsRect.anchorMax = new Vector2(1f, 1f);
        rowsRect.offsetMin = new Vector2(20f, 20f);
        rowsRect.offsetMax = new Vector2(-20f, -60f);
        var rowsLayout = rows.AddComponent<VerticalLayoutGroup>();
        rowsLayout.spacing = 10f;
        rowsLayout.childControlWidth = true;
        rowsLayout.childControlHeight = true;
        rowsLayout.childForceExpandWidth = true;
        rowsLayout.childForceExpandHeight = false;

        _statRound = CreateStatRow(rows.transform, "Round");
        _statZombies = CreateStatRow(rows.transform, "Zombies Left");
        _statPoints = CreateStatRow(rows.transform, "Your Points");
        _statTeam = CreateStatRow(rows.transform, "Team Points");
        _statHealth = CreateStatRow(rows.transform, "Health");
        _statWeapon = CreateStatRow(rows.transform, "Weapon");
        _statKills = CreateStatRow(rows.transform, "Zombies Killed");
    }

    private TMP_Text CreateStatRow(Transform parent, string label)
    {
        var row = new GameObject("Stat_" + label, typeof(RectTransform));
        row.transform.SetParent(parent, false);

        var labelText = CreateText("Label", row.transform, label, 20, ColorTextDim, TextAlignmentOptions.Left);
        var labelRect = labelText.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(0.5f, 1f);
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        var value = CreateText("Value", row.transform, "--", 20, ColorAmber, TextAlignmentOptions.Right);
        value.fontStyle = FontStyles.Bold;
        var valueRect = value.rectTransform;
        valueRect.anchorMin = new Vector2(0.5f, 0f);
        valueRect.anchorMax = new Vector2(1f, 1f);
        valueRect.offsetMin = Vector2.zero;
        valueRect.offsetMax = Vector2.zero;

        var le = row.AddComponent<LayoutElement>();
        le.minHeight = 30f;
        le.preferredHeight = 30f;

        return value;
    }

    private void BuildHostPausedBanner(RectTransform root)
    {
        var banner = CreateImage("HostPausedBanner", root, ColorAccentRed);
        var bannerRect = banner.rectTransform;
        bannerRect.anchorMin = new Vector2(0.5f, 1f);
        bannerRect.anchorMax = new Vector2(0.5f, 1f);
        bannerRect.pivot = new Vector2(0.5f, 1f);
        bannerRect.sizeDelta = new Vector2(560f, 56f);
        bannerRect.anchoredPosition = new Vector2(0f, -24f);
        banner.raycastTarget = false;

        var text = CreateText("Text", banner.transform, "MATCH PAUSED BY HOST", 26, Color.white, TextAlignmentOptions.Center);
        text.fontStyle = FontStyles.Bold;
        Stretch(text.rectTransform);

        _hostPausedBanner = banner.gameObject;
        _hostPausedBanner.SetActive(false);
    }

    // ---------------------------------------------------------------------
    // UI helpers
    // ---------------------------------------------------------------------

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static TMP_Text CreateText(string name, Transform parent, string content, int fontSize, Color color, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    private Button CreateButton(Transform parent, string label, Action onClick, Color accent, out TMP_Text labelText, float height)
    {
        var go = new GameObject("Button_" + label, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var image = go.AddComponent<Image>();
        image.color = ColorButton;

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.normalColor = ColorButton;
        colors.highlightedColor = ColorButtonHover;
        colors.pressedColor = accent;
        colors.selectedColor = ColorButtonHover;
        colors.disabledColor = ColorButton;
        colors.fadeDuration = 0.1f;
        button.colors = colors;
        if (onClick != null)
        {
            button.onClick.AddListener(() => onClick());
        }

        labelText = CreateText("Text", go.transform, label, 24, ColorTextOffWhite, TextAlignmentOptions.Center);
        labelText.fontStyle = FontStyles.Bold;
        Stretch(labelText.rectTransform);

        var strip = CreateImage("AccentStrip", go.transform, accent);
        var stripRect = strip.rectTransform;
        stripRect.anchorMin = new Vector2(0f, 0f);
        stripRect.anchorMax = new Vector2(1f, 0f);
        stripRect.pivot = new Vector2(0.5f, 0f);
        stripRect.sizeDelta = new Vector2(0f, 3f);
        strip.raycastTarget = false;

        var le = go.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;

        return button;
    }

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null)
        {
            return;
        }
        var go = new GameObject("EventSystem");
        go.AddComponent<UnityEngine.EventSystems.EventSystem>();
        go.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
    }
}
