using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Controller for the "SCHOOL OVERRUN" game-over cinematic (DeathCinematic scene). Used for SOLO
/// game over: GameOverController pushes the run's stats via <see cref="SetRunStats"/> and loads
/// this scene.
///
/// EDITABLE SCENE (Phase 3): the room is meant to be hand-editable in Unity. On Start this
/// controller RESOLVES existing scene objects by name (organised under <c>DeathRoom_Root</c>) and
/// only BUILDS the pieces that are missing. So:
///   * In the normal saved/baked scene it uses your editable GameObjects and never regenerates
///     the room.
///   * In an EMPTY scene (or direct Play with nothing authored) it falls back to building the
///     whole room at runtime, exactly as before, so it always previews.
///
/// Bake the editable objects with the editor tool: Tools > Death Cinematic > Rebuild Editable
/// Death Room (see DeathCinematicSceneCreator). The room construction lives in shared static
/// builders used by BOTH the runtime fallback and that editor tool, so they always match.
///
/// This controller never references any gameplay system — stats arrive as plain values.
/// Buttons load scenes directly (no multiplayer/session logic yet):
///   Restart Match -> SchoolOfTheDead, Main Menu -> MainMenu, Quit -> Application.Quit.
/// </summary>
public sealed class DeathCinematicSceneController : MonoBehaviour
{
    private const string CinematicScene = "DeathCinematic";
    private const string GameplayScene = "SchoolOfTheDead";
    private const string MainMenuScene = "MainMenu";

    // Well-known object names (the resolve-or-build contract shared with the editor baker). Rename
    // these objects in the scene at your own risk — the controller finds them by these names.
    public const string RootName = "DeathRoom_Root";
    public const string RoomName = "Room";
    public const string MonitorBankName = "MonitorBank";
    public const string ChalkboardName = "Chalkboard";
    public const string BoardTextName = "BoardText";
    public const string SetDressingName = "SetDressing";
    public const string LightsName = "Lights";
    public const string DustName = "DustMotes";
    public const string CameraName = "CinematicCamera";
    public const string UiName = "DeathCinematicUI";
    private const string CameraPointPrefix = "CameraPoint_";
    private const string LookTargetPrefix = "LookTarget_";
    private const int TourPointCount = 4;

    private static DeathCinematicSceneController _instance;

    // --- Static stats handoff (Phase 2) --------------------------------------------------
    private static bool _hasPendingStats;
    private static int _pendingRound;
    private static int _pendingKills;
    private static int _pendingPoints;
    private static int _pendingBestRound;
    private static int _pendingBestScore;
    private static bool _pendingNetworked;

    private bool _networkedRun;     // whether the handed-off run was networked (reserved for later)
    private bool _usedHandoffStats; // true when real run stats were applied (vs. placeholder preview)

    // --- Placeholder stats (used as preview defaults when no run stats were handed off) ---
    [Header("Placeholder Stats (preview defaults)")]
    public int roundSurvived = 6;
    public int zombiesKilled = 83;
    public int finalPoints = 630;
    public int bestRound = 8;
    public int bestScore = 4200;

    [Header("Reveal Timing (seconds from scene start)")]
    [Tooltip("When the big SCHOOL OVERRUN title starts fading in.")]
    public float titleRevealDelay = 1.2f;
    [Tooltip("When the subtitle starts fading in.")]
    public float subtitleRevealDelay = 2.6f;
    [Tooltip("When the stats panel + buttons start fading in (buttons become clickable once visible).")]
    public float buttonsRevealDelay = 5f;
    [Tooltip("Fade-in duration for each reveal group.")]
    public float revealFadeDuration = 0.8f;
    [Tooltip("Seconds the stat values take to count up from 0 once the panel reveals.")]
    public float statsCountUpDuration = 1.1f;

    [Header("Camera Tour")]
    [Tooltip("Seconds per dolly segment between tour points (4 points = 3 segments).")]
    public float segmentDuration = 5.5f;
    [Tooltip("Gentle idle sway amplitude once the tour settles on its final shot.")]
    public float idleSwayAmount = 0.06f;

    [Header("Monitors")]
    [Tooltip("Seconds between a monitor re-tuning to the next camera feed.")]
    public float retuneInterval = 4.5f;
    [Tooltip("Seconds of SIGNAL LOST static while a monitor re-tunes.")]
    public float staticDuration = 0.35f;

    [Header("Scene References (optional — auto-resolved by name / auto-built if empty)")]
    [Tooltip("Root of the editable death room. Wired by the editor baker; if left empty the " +
             "controller finds a '" + RootName + "' object or creates one, then resolves/builds " +
             "the rest under it.")]
    public Transform deathRoomRoot;

    // The fake school security feeds the monitors cycle through.
    private static readonly string[] FeedLabels =
    {
        "MAIN HALL", "CLASSROOM BLOCK", "CAFETERIA", "LIBRARY", "LOCKER HALL",
    };

    // --- Palette (matches the HUD's muted school-survival look) ---
    private static readonly Color RedAccent = new Color(0.78f, 0.12f, 0.10f, 1f);
    private static readonly Color OffWhite = new Color(0.92f, 0.90f, 0.84f, 1f);
    private static readonly Color PanelDark = new Color(0.07f, 0.08f, 0.09f, 0.88f);
    private static readonly Color ScreenBase = new Color(0.06f, 0.11f, 0.08f, 1f);
    private static readonly Color ScreenText = new Color(0.62f, 0.92f, 0.68f, 1f);

    /// <summary>
    /// Marker + authoring data placed on each editable monitor object. Holds the references the
    /// controller needs to drive that monitor's flicker/feed at runtime, so you can move, rename,
    /// or restyle the monitor freely as long as these two fields stay wired.
    /// </summary>
    public sealed class DeathCinematicMonitor : MonoBehaviour
    {
        [Tooltip("The CRT screen quad whose material colour is driven for the glow/flicker/static.")]
        public Renderer screenRenderer;
        [Tooltip("The feed label (camera name) shown on this monitor.")]
        public TMP_Text feedLabel;
    }

    // --- Runtime monitor state (one per resolved DeathCinematicMonitor) ---
    private sealed class MonitorState
    {
        public Material screenMat;
        public TMP_Text label;
        public int feed;
        public float nextRetune;
        public float staticUntil;
        public bool showingStatic;
        public float flickerSeed;
    }

    private readonly List<MonitorState> monitors = new List<MonitorState>();

    private Transform camT;
    private Transform[] camPointT;
    private Transform[] lookPointT;
    private float sceneStartTime;

    private Light emergencyLight;
    private Transform beaconPivot;
    private Light monitorGlow;

    private CanvasGroup titleGroup;
    private CanvasGroup subtitleGroup;
    private CanvasGroup panelGroup;
    private TMP_Text[] statValueTexts;
    private int[] statTargets;
    private bool statsCountDone;
    private bool built;

    // ---------------------------------------------------------------------------------------
    // Bootstrap: auto-create ONLY in the DeathCinematic scene (exact name), mirroring the
    // project's other scene-gated runtime systems. Harmless in every other scene. If the baked
    // scene already carries a controller instance, this never adds a duplicate.
    // ---------------------------------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnAnySceneLoaded;
        SceneManager.sceneLoaded += OnAnySceneLoaded;
        SpawnIfCinematicScene(SceneManager.GetActiveScene());
    }

    private static void OnAnySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SpawnIfCinematicScene(scene);
    }

    private static void SpawnIfCinematicScene(Scene scene)
    {
        if (scene.name != CinematicScene)
        {
            if (_instance != null)
            {
                Destroy(_instance.gameObject);
                _instance = null;
            }
            return;
        }

        if (_instance != null || FindFirstObjectByType<DeathCinematicSceneController>() != null)
        {
            return;
        }

        GameObject go = new GameObject("DeathCinematic (Runtime)");
        _instance = go.AddComponent<DeathCinematicSceneController>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    // ---------------------------------------------------------------------------------------
    // Stats handoff
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Hand off the finished run's stats to the cinematic BEFORE loading the DeathCinematic scene.
    /// Static so the caller (e.g. <c>GameOverController</c>) needs no live instance and this
    /// controller never touches gameplay systems. If it is never called, the inspector placeholder
    /// values are kept so opening the scene directly in Play Mode still previews correctly.
    /// </summary>
    public static void SetRunStats(int round, int kills, int points, int bestRound, int bestScore, bool networked)
    {
        _pendingRound = round;
        _pendingKills = kills;
        _pendingPoints = points;
        _pendingBestRound = bestRound;
        _pendingBestScore = bestScore;
        _pendingNetworked = networked;
        _hasPendingStats = true;
    }

    private void ApplyPendingStats()
    {
        if (!_hasPendingStats)
        {
            return;
        }
        roundSurvived = _pendingRound;
        zombiesKilled = _pendingKills;
        finalPoints = _pendingPoints;
        bestRound = _pendingBestRound;
        bestScore = _pendingBestScore;
        _networkedRun = _pendingNetworked;
        _usedHandoffStats = true;
        _hasPendingStats = false; // consumed: a later direct-open falls back to placeholders
    }

    // ---------------------------------------------------------------------------------------
    // Start / resolve-or-build
    // ---------------------------------------------------------------------------------------

    private void Start()
    {
        // Scene-local safety: arriving here should never inherit a paused timescale.
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        sceneStartTime = Time.time;

        // Apply real run stats (if handed off) BEFORE anything reads them (chalkboard + UI).
        ApplyPendingStats();

        ApplyAtmosphere();
        ResolveOrBuildScene();

        built = true;
        Debug.Log("[DeathCinematic] Scene ready. Stats: " +
                  (_usedHandoffStats ? "run handoff" : "placeholder preview") +
                  " — round " + roundSurvived + ", kills " + zombiesKilled + ", points " + finalPoints + ".");
    }

    // Prefer existing editable scene objects; build only the pieces that are missing. Every
    // section is resolved by name under the death-room root, so the normal baked scene uses your
    // hand-edited objects while an empty scene still builds a full room.
    private void ResolveOrBuildScene()
    {
        Transform root = ResolveOrCreateRoot();

        // Room shell.
        if (FindChildByName(root, RoomName) == null)
        {
            BuildRoom(root);
        }

        // Monitor bank: resolve the marker components, building the bank first if there are none.
        DeathCinematicMonitor[] monitorComps = root.GetComponentsInChildren<DeathCinematicMonitor>(true);
        if (monitorComps == null || monitorComps.Length == 0)
        {
            BuildMonitorBank(root);
            monitorComps = root.GetComponentsInChildren<DeathCinematicMonitor>(true);
        }
        BindMonitors(monitorComps);

        // Chalkboard: build if absent, then always refresh its text from the current stats.
        Transform chalkboard = FindChildByName(root, ChalkboardName);
        if (chalkboard == null)
        {
            chalkboard = BuildChalkboard(root, BuildStatsReport());
        }
        RefreshChalkboardText(chalkboard);

        // Set dressing.
        if (FindChildByName(root, SetDressingName) == null)
        {
            BuildSetDressing(root);
        }

        // Lights: build if absent, then resolve the three animated lights.
        Transform lights = FindChildByName(root, LightsName);
        if (lights == null)
        {
            lights = BuildLights(root);
        }
        BindLights(lights);

        // Dust.
        if (FindChildByName(root, DustName) == null)
        {
            BuildDust(root);
        }

        // Camera + tour rig.
        ResolveOrBuildCamera(root);
        ResolveOrBuildTourPoints(root);
        SnapCameraToStart();

        // UI overlay.
        ResolveOrBuildUi();
    }

    private Transform ResolveOrCreateRoot()
    {
        if (deathRoomRoot != null)
        {
            return deathRoomRoot;
        }
        GameObject existing = GameObject.Find(RootName);
        deathRoomRoot = existing != null ? existing.transform : new GameObject(RootName).transform;
        return deathRoomRoot;
    }

    private void BindMonitors(DeathCinematicMonitor[] comps)
    {
        monitors.Clear();
        if (comps == null)
        {
            return;
        }
        for (int i = 0; i < comps.Length; i++)
        {
            DeathCinematicMonitor dm = comps[i];
            if (dm == null)
            {
                continue;
            }

            // Prefer the wired marker refs; fall back to child objects named "Screen"/"FeedLabel"
            // so a hand-built (or un-wired) monitor still animates.
            Renderer screen = dm.screenRenderer;
            if (screen == null)
            {
                Transform st = FindChildByName(dm.transform, "Screen");
                screen = st != null ? st.GetComponent<Renderer>() : null;
            }
            TMP_Text feedLabel = dm.feedLabel;
            if (feedLabel == null)
            {
                Transform lt = FindChildByName(dm.transform, "FeedLabel");
                feedLabel = lt != null ? lt.GetComponent<TMP_Text>() : null;
            }

            // .material instances the (possibly scene-baked) material for play mode only — the
            // authored material asset is never mutated.
            Material mat = screen != null ? screen.material : null;
            MonitorState state = new MonitorState
            {
                screenMat = mat,
                label = feedLabel,
                feed = i % FeedLabels.Length,
                nextRetune = Time.time + retuneInterval + i * 1.3f,
                flickerSeed = i * 17.31f,
            };
            SetFeedLabel(state);
            monitors.Add(state);
        }
    }

    private void RefreshChalkboardText(Transform chalkboard)
    {
        if (chalkboard == null)
        {
            return;
        }
        Transform textT = FindChildByName(chalkboard, BoardTextName);
        TMP_Text board = textT != null ? textT.GetComponent<TMP_Text>() : chalkboard.GetComponentInChildren<TMP_Text>(true);
        if (board != null)
        {
            board.text = BuildStatsReport();
        }
    }

    private void BindLights(Transform lights)
    {
        if (lights == null)
        {
            return;
        }
        emergencyLight = GetLight(lights, "EmergencyLight");
        Transform pivot = FindChildByName(lights, "BeaconPivot");
        beaconPivot = pivot;
        monitorGlow = GetLight(lights, "MonitorGlow");
    }

    private void ResolveOrBuildCamera(Transform root)
    {
        Camera cam = null;
        GameObject named = GameObject.Find(CameraName);
        if (named != null)
        {
            cam = named.GetComponent<Camera>();
        }
        if (cam == null)
        {
            cam = Camera.main;
        }
        if (cam == null)
        {
            cam = FindFirstObjectByType<Camera>();
        }
        if (cam == null)
        {
            cam = BuildCinematicCamera(root);
        }
        else
        {
            ConfigureCinematicCamera(cam);
        }
        camT = cam != null ? cam.transform : null;
    }

    private void ResolveOrBuildTourPoints(Transform root)
    {
        camPointT = ResolvePoints(CameraPointPrefix, TourPointCount);
        lookPointT = ResolvePoints(LookTargetPrefix, TourPointCount);
        if (camPointT == null || lookPointT == null)
        {
            BuildTourPoints(root, out camPointT, out lookPointT);
        }
    }

    private void SnapCameraToStart()
    {
        if (camT == null || camPointT == null || lookPointT == null || camPointT.Length == 0 || lookPointT.Length == 0)
        {
            return;
        }
        camT.position = camPointT[0].position;
        Vector3 dir = lookPointT[0].position - camPointT[0].position;
        if (dir.sqrMagnitude > 0.0001f)
        {
            camT.rotation = Quaternion.LookRotation(dir);
        }
    }

    private void ResolveOrBuildUi()
    {
        EnsureEventSystem();

        GameObject uiGo = GameObject.Find(UiName);
        if (uiGo == null)
        {
            uiGo = BuildUi();
        }

        titleGroup = GetCanvasGroup(uiGo.transform, "TitleGroup");
        subtitleGroup = GetCanvasGroup(uiGo.transform, "SubtitleGroup");
        panelGroup = GetCanvasGroup(uiGo.transform, "PanelGroup");

        statValueTexts = new TMP_Text[5];
        for (int i = 0; i < statValueTexts.Length; i++)
        {
            Transform t = FindChildByName(uiGo.transform, "StatValue_" + i);
            statValueTexts[i] = t != null ? t.GetComponent<TMP_Text>() : null;
        }
        statTargets = new[] { roundSurvived, zombiesKilled, finalPoints, bestRound, bestScore };
        statsCountDone = false;

        WireButton(uiGo.transform, "Btn_Restart", () => SceneManager.LoadScene(GameplayScene));
        WireButton(uiGo.transform, "Btn_MainMenu", () => SceneManager.LoadScene(MainMenuScene));
        WireButton(uiGo.transform, "Btn_Quit", Application.Quit);

        // Everything starts invisible; UpdateReveals fades the groups in on schedule.
        if (titleGroup != null) titleGroup.alpha = 0f;
        if (subtitleGroup != null) subtitleGroup.alpha = 0f;
        if (panelGroup != null)
        {
            panelGroup.alpha = 0f;
            panelGroup.interactable = false;
            panelGroup.blocksRaycasts = false;
        }
    }

    private string BuildStatsReport()
    {
        return "FINAL REPORT\n" +
               "ROUND SURVIVED ......... " + roundSurvived + "\n" +
               "ZOMBIES KILLED ......... " + zombiesKilled + "\n" +
               "FINAL POINTS ........... " + finalPoints + "\n" +
               "BEST ROUND ............. " + bestRound + "\n" +
               "BEST SCORE ............. " + bestScore;
    }

    // ---------------------------------------------------------------------------------------
    // Per-frame animation
    // ---------------------------------------------------------------------------------------

    private void Update()
    {
        if (!built)
        {
            return;
        }

        float elapsed = Time.time - sceneStartTime;
        UpdateCameraTour(elapsed);
        UpdateMonitors();
        UpdateLights();
        UpdateReveals(elapsed);
    }

    private void UpdateCameraTour(float elapsed)
    {
        if (camT == null || camPointT == null || lookPointT == null ||
            camPointT.Length < 2 || lookPointT.Length < camPointT.Length)
        {
            return;
        }

        int segments = camPointT.Length - 1;
        float segDur = Mathf.Max(0.5f, segmentDuration);
        float total = segments * segDur;

        Vector3 pos;
        Vector3 look;
        if (elapsed >= total)
        {
            // Settled on the final shot: breathe with a slow, subtle sway.
            Vector3 sway = new Vector3(
                Mathf.Sin(Time.time * 0.33f),
                Mathf.Sin(Time.time * 0.21f) * 0.6f,
                0f) * idleSwayAmount;
            pos = camPointT[segments].position + sway;
            look = lookPointT[segments].position + sway * 0.4f;
        }
        else
        {
            int i = Mathf.Min(segments - 1, (int)(elapsed / segDur));
            float t = SmoothStep01((elapsed - i * segDur) / segDur);
            pos = Vector3.Lerp(camPointT[i].position, camPointT[i + 1].position, t);
            look = Vector3.Lerp(lookPointT[i].position, lookPointT[i + 1].position, t);
        }

        camT.position = pos;
        Vector3 dir = look - pos;
        if (dir.sqrMagnitude > 0.0001f)
        {
            camT.rotation = Quaternion.LookRotation(dir);
        }
    }

    private void UpdateMonitors()
    {
        float now = Time.time;
        for (int i = 0; i < monitors.Count; i++)
        {
            MonitorState m = monitors[i];
            if (m.screenMat == null)
            {
                continue;
            }

            if (m.showingStatic)
            {
                if (now >= m.staticUntil)
                {
                    m.showingStatic = false;
                    SetFeedLabel(m); // static over: show the new feed
                }
                else
                {
                    // Harsh static: bright random screen flashes.
                    float flash = 0.35f + Random.value * 0.65f;
                    m.screenMat.color = new Color(flash * 0.7f, flash, flash * 0.75f);
                    continue;
                }
            }
            else if (now >= m.nextRetune)
            {
                // Re-tune to the next feed behind a short burst of static.
                m.feed = (m.feed + 1) % FeedLabels.Length;
                m.showingStatic = true;
                m.staticUntil = now + Mathf.Max(0.05f, staticDuration);
                m.nextRetune = now + Mathf.Max(1f, retuneInterval) + i * 0.4f;
                if (m.label != null)
                {
                    m.label.text = "<size=55%>. . .</size>\nSIGNAL LOST";
                }
                continue;
            }

            // Idle CRT shimmer: gentle per-monitor Perlin flicker of the screen glow.
            float glow = 0.8f + 0.4f * Mathf.PerlinNoise(m.flickerSeed, now * 5f);
            m.screenMat.color = new Color(ScreenBase.r * glow * 2.2f, ScreenBase.g * glow * 2.2f, ScreenBase.b * glow * 2.2f);
        }
    }

    private void UpdateLights()
    {
        float now = Time.time;
        if (emergencyLight != null)
        {
            // Slow alarm pulse with a hint of instability.
            float pulse = Mathf.Pow(Mathf.Abs(Mathf.Sin(now * 1.9f)), 3f);
            float stutter = 0.9f + 0.2f * Mathf.PerlinNoise(3.7f, now * 7f);
            emergencyLight.intensity = (0.6f + 1.8f * pulse) * stutter;
        }
        if (beaconPivot != null)
        {
            beaconPivot.Rotate(0f, 42f * Time.deltaTime, 0f);
        }
        if (monitorGlow != null)
        {
            monitorGlow.intensity = 0.65f + 0.35f * Mathf.PerlinNoise(9.1f, now * 4f);
        }
    }

    private void UpdateReveals(float elapsed)
    {
        FadeGroup(titleGroup, elapsed, titleRevealDelay);
        FadeGroup(subtitleGroup, elapsed, subtitleRevealDelay);
        FadeGroup(panelGroup, elapsed, buttonsRevealDelay);

        if (panelGroup != null)
        {
            bool interactive = panelGroup.alpha >= 0.95f;
            panelGroup.interactable = interactive;
            panelGroup.blocksRaycasts = interactive;
        }

        // Count the stat values up once the panel starts revealing.
        if (!statsCountDone && statValueTexts != null && statTargets != null && elapsed >= buttonsRevealDelay)
        {
            float t = Mathf.Clamp01((elapsed - buttonsRevealDelay) / Mathf.Max(0.05f, statsCountUpDuration));
            float eased = SmoothStep01(t);
            for (int i = 0; i < statValueTexts.Length && i < statTargets.Length; i++)
            {
                if (statValueTexts[i] != null)
                {
                    statValueTexts[i].text = Mathf.RoundToInt(Mathf.Lerp(0f, statTargets[i], eased)).ToString("N0");
                }
            }
            statsCountDone = t >= 1f;
        }
    }

    private void FadeGroup(CanvasGroup group, float elapsed, float delay)
    {
        if (group != null)
        {
            group.alpha = Mathf.Clamp01((elapsed - delay) / Mathf.Max(0.05f, revealFadeDuration));
        }
    }

    private void SetFeedLabel(MonitorState m)
    {
        if (m.label != null)
        {
            m.label.text = "<size=55%>CAM 0" + (m.feed + 1) + "  <color=#FF3020>● REC</color></size>\n" + FeedLabels[m.feed];
        }
    }

    private static float SmoothStep01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    // ---------------------------------------------------------------------------------------
    // Static builders — shared by the runtime fallback AND the editor baker (see
    // DeathCinematicSceneCreator) so a hand-built scene and a runtime-built one match exactly.
    // Nothing here touches any project asset; all materials/objects are created fresh.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Build the entire editable death-room hierarchy under a fresh <see cref="RootName"/> plus the
    /// UI, using the given placeholder stats for the baked chalkboard/UI text. Returns the room
    /// root. Used by the editor baker; the runtime fallback builds the same pieces individually.
    /// </summary>
    public static Transform BuildEditableRoom(int round, int kills, int points, int bestRound, int bestScore)
    {
        ApplyAtmosphereStatic();

        Transform root = new GameObject(RootName).transform;
        BuildRoom(root);
        BuildMonitorBank(root);
        BuildChalkboard(root, BuildStatsReport(round, kills, points, bestRound, bestScore));
        BuildSetDressing(root);
        BuildLights(root);
        BuildDust(root);
        BuildCinematicCamera(root);
        BuildTourPoints(root, out _, out _);
        BuildUi(); // DeathCinematicUI (own root object)
        return root;
    }

    private static string BuildStatsReport(int round, int kills, int points, int bestRound, int bestScore)
    {
        return "FINAL REPORT\n" +
               "ROUND SURVIVED ......... " + round + "\n" +
               "ZOMBIES KILLED ......... " + kills + "\n" +
               "FINAL POINTS ........... " + points + "\n" +
               "BEST ROUND ............. " + bestRound + "\n" +
               "BEST SCORE ............. " + bestScore;
    }

    // Dark, oppressive room mood: near-black ambient plus a thin red-grey exponential fog.
    private void ApplyAtmosphere()
    {
        ApplyAtmosphereStatic();
    }

    public static void ApplyAtmosphereStatic()
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.05f, 0.06f, 0.08f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.05f;
        RenderSettings.fogColor = new Color(0.09f, 0.035f, 0.03f);
    }

    // Small security-office box: floor, ceiling, four walls, and a desk under the monitors.
    private static Transform BuildRoom(Transform parent)
    {
        Transform room = new GameObject(RoomName).transform;
        room.SetParent(parent, false);

        Material floorMat = MakeLitMaterial("Floor", new Color(0.15f, 0.15f, 0.16f));
        Material ceilMat = MakeLitMaterial("Ceiling", new Color(0.09f, 0.09f, 0.10f));
        Material wallMat = MakeLitMaterial("Walls", new Color(0.15f, 0.17f, 0.15f));
        Material deskMat = MakeLitMaterial("Desk", new Color(0.22f, 0.16f, 0.11f));

        MakeBox("Floor", room, new Vector3(0f, -0.05f, 0f), new Vector3(10f, 0.1f, 8f), floorMat);
        MakeBox("Ceiling", room, new Vector3(0f, 3.05f, 0f), new Vector3(10f, 0.1f, 8f), ceilMat);
        MakeBox("Wall_N", room, new Vector3(0f, 1.5f, 4.05f), new Vector3(10f, 3.2f, 0.1f), wallMat);
        MakeBox("Wall_S", room, new Vector3(0f, 1.5f, -4.05f), new Vector3(10f, 3.2f, 0.1f), wallMat);
        MakeBox("Wall_W", room, new Vector3(-5.05f, 1.5f, 0f), new Vector3(0.1f, 3.2f, 8f), wallMat);
        MakeBox("Wall_E", room, new Vector3(5.05f, 1.5f, 0f), new Vector3(0.1f, 3.2f, 8f), wallMat);

        MakeBox("DeskTop", room, new Vector3(0f, 0.78f, 2.9f), new Vector3(4.2f, 0.08f, 0.9f), deskMat);
        MakeBox("DeskLegL", room, new Vector3(-1.95f, 0.38f, 2.9f), new Vector3(0.1f, 0.76f, 0.8f), deskMat);
        MakeBox("DeskLegR", room, new Vector3(1.95f, 0.38f, 2.9f), new Vector3(0.1f, 0.76f, 0.8f), deskMat);
        return room;
    }

    // Five CRT-style security monitors on the north wall. Each is its own editable object
    // (Monitor_01..05) carrying a DeathCinematicMonitor marker with its screen + feed-label refs.
    private static Transform BuildMonitorBank(Transform parent)
    {
        Transform bank = new GameObject(MonitorBankName).transform;
        bank.SetParent(parent, false);
        Material bezelMat = MakeLitMaterial("Bezel", new Color(0.06f, 0.06f, 0.07f));

        // Layout: three monitors on top, two below, centered on the north wall (world z ~ 3.9).
        Vector2[] slots =
        {
            new Vector2(-1.25f, 2.05f), new Vector2(0f, 2.05f), new Vector2(1.25f, 2.05f),
            new Vector2(-0.62f, 1.3f), new Vector2(0.62f, 1.3f),
        };

        for (int i = 0; i < slots.Length; i++)
        {
            Vector2 s = slots[i];

            // Per-monitor parent at the screen plane; children are local so moving this object
            // moves the whole monitor as a unit.
            GameObject monGo = new GameObject("Monitor_0" + (i + 1));
            monGo.transform.SetParent(bank, false);
            monGo.transform.localPosition = new Vector3(s.x, s.y, 3.90f);
            Transform mon = monGo.transform;

            MakeBox("Bezel", mon, new Vector3(0f, 0f, 0.06f), new Vector3(1.05f, 0.72f, 0.08f), bezelMat);

            Material screenMat = MakeUnlitMaterial("Screen", ScreenBase);
            GameObject screen = MakeBox("Screen", mon, Vector3.zero, new Vector3(0.92f, 0.6f, 0.02f), screenMat);

            TextMeshPro label = MakeWorldText("FeedLabel", mon, new Vector3(0f, 0f, -0.03f), Quaternion.identity,
                new Vector2(0.86f, 0.54f), ScreenText, TextAlignmentOptions.Center);
            label.text = "<size=55%>CAM 0" + (i % FeedLabels.Length + 1) + "  <color=#FF3020>● REC</color></size>\n" +
                         FeedLabels[i % FeedLabels.Length];

            DeathCinematicMonitor dm = monGo.AddComponent<DeathCinematicMonitor>();
            dm.screenRenderer = screen.GetComponent<Renderer>();
            dm.feedLabel = label;
        }

        // Strip label above the bank.
        MakeWorldText("FeedStrip", bank, new Vector3(0f, 2.62f, 3.9f), Quaternion.identity,
            new Vector2(3.4f, 0.22f), RedAccent, TextAlignmentOptions.Center)
            .text = "SECURITY FEED — <color=#FF4030>● LIVE</color>";
        return bank;
    }

    // Chalkboard "final report" on the west wall. Text is set here for the baked look and refreshed
    // from the real run stats at runtime.
    private static Transform BuildChalkboard(Transform parent, string reportText)
    {
        Transform boardRoot = new GameObject(ChalkboardName).transform;
        boardRoot.SetParent(parent, false);
        Material boardMat = MakeLitMaterial("Board", new Color(0.09f, 0.16f, 0.11f));
        Material frameMat = MakeLitMaterial("BoardFrame", new Color(0.25f, 0.18f, 0.10f));

        MakeBox("BoardFrame", boardRoot, new Vector3(-4.96f, 1.6f, 0.4f), new Vector3(0.05f, 1.85f, 3.0f), frameMat);
        MakeBox("BoardFace", boardRoot, new Vector3(-4.92f, 1.6f, 0.4f), new Vector3(0.04f, 1.65f, 2.8f), boardMat);

        TextMeshPro chalk = MakeWorldText(BoardTextName, boardRoot,
            new Vector3(-4.88f, 1.62f, 0.4f), Quaternion.Euler(0f, -90f, 0f),
            new Vector2(2.5f, 1.45f), new Color(0.88f, 0.90f, 0.86f, 0.95f), TextAlignmentOptions.Left);
        chalk.text = reportText;
        return boardRoot;
    }

    // A little chaos so the office feels abandoned mid-crisis: a toppled chair + papers.
    private static Transform BuildSetDressing(Transform parent)
    {
        Transform props = new GameObject(SetDressingName).transform;
        props.SetParent(parent, false);
        Material chairMat = MakeLitMaterial("Chair", new Color(0.12f, 0.13f, 0.15f));
        Material paperMat = MakeLitMaterial("Paper", new Color(0.72f, 0.70f, 0.64f));

        GameObject chair = MakeBox("FallenChair", props, new Vector3(1.7f, 0.24f, 1.7f), new Vector3(0.45f, 0.5f, 0.45f), chairMat);
        chair.transform.localRotation = Quaternion.Euler(0f, 25f, 82f);

        MakeBox("Paper_0", props, new Vector3(-0.7f, 0.006f, 1.4f), new Vector3(0.21f, 0.01f, 0.3f), paperMat)
            .transform.localRotation = Quaternion.Euler(0f, 24f, 0f);
        MakeBox("Paper_1", props, new Vector3(0.4f, 0.006f, 0.6f), new Vector3(0.21f, 0.01f, 0.3f), paperMat)
            .transform.localRotation = Quaternion.Euler(0f, -51f, 0f);
        MakeBox("Paper_2", props, new Vector3(-1.6f, 0.006f, -0.5f), new Vector3(0.21f, 0.01f, 0.3f), paperMat)
            .transform.localRotation = Quaternion.Euler(0f, 133f, 0f);
        return props;
    }

    // Emergency-red pulse + a slowly sweeping beacon + faint cool fill + CRT glow.
    private static Transform BuildLights(Transform parent)
    {
        Transform lights = new GameObject(LightsName).transform;
        lights.SetParent(parent, false);

        GameObject red = new GameObject("EmergencyLight");
        red.transform.SetParent(lights, false);
        red.transform.localPosition = new Vector3(0f, 2.7f, 1.2f);
        Light emergency = red.AddComponent<Light>();
        emergency.type = LightType.Point;
        emergency.color = new Color(1f, 0.12f, 0.08f);
        emergency.range = 14f;
        emergency.intensity = 1.4f;

        GameObject pivot = new GameObject("BeaconPivot");
        pivot.transform.SetParent(lights, false);
        pivot.transform.localPosition = new Vector3(0f, 2.85f, 0.6f);
        GameObject beacon = new GameObject("BeaconSpot");
        beacon.transform.SetParent(pivot.transform, false);
        beacon.transform.localRotation = Quaternion.Euler(38f, 0f, 0f);
        Light spot = beacon.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.color = new Color(1f, 0.22f, 0.12f);
        spot.range = 16f;
        spot.spotAngle = 55f;
        spot.intensity = 3.2f;

        GameObject fill = new GameObject("CoolFill");
        fill.transform.SetParent(lights, false);
        fill.transform.localPosition = new Vector3(-2.2f, 2.2f, -1.6f);
        Light fillLight = fill.AddComponent<Light>();
        fillLight.type = LightType.Point;
        fillLight.color = new Color(0.30f, 0.38f, 0.52f);
        fillLight.range = 11f;
        fillLight.intensity = 0.5f;

        GameObject glow = new GameObject("MonitorGlow");
        glow.transform.SetParent(lights, false);
        glow.transform.localPosition = new Vector3(0f, 1.7f, 3.2f);
        Light glowLight = glow.AddComponent<Light>();
        glowLight.type = LightType.Point;
        glowLight.color = new Color(0.35f, 0.8f, 0.5f);
        glowLight.range = 5f;
        glowLight.intensity = 0.8f;
        return lights;
    }

    // Slow drifting dust motes.
    private static Transform BuildDust(Transform parent)
    {
        GameObject go = new GameObject(DustName);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, 1.6f, 0.8f);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = 9f;
        main.startSpeed = 0.05f;
        main.startSize = 0.02f;
        main.startColor = new Color(0.75f, 0.72f, 0.66f, 0.22f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 160;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 14f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(8.5f, 2.6f, 6.5f);

        ParticleSystemRenderer psr = go.GetComponent<ParticleSystemRenderer>();
        if (psr != null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) { shader = Shader.Find("Sprites/Default"); }
            if (shader == null) { shader = Shader.Find("Unlit/Color"); }
            if (shader != null)
            {
                psr.material = new Material(shader) { color = new Color(0.8f, 0.76f, 0.7f, 0.25f) };
            }
        }

        ps.Play();
        return go.transform;
    }

    private static Camera BuildCinematicCamera(Transform parent)
    {
        GameObject go = new GameObject(CameraName);
        if (parent != null)
        {
            go.transform.SetParent(parent, false);
        }
        Camera cam = go.AddComponent<Camera>();
        ConfigureCinematicCamera(cam);
        return cam;
    }

    private static void ConfigureCinematicCamera(Camera cam)
    {
        if (cam == null)
        {
            return;
        }
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.01f, 0.01f, 0.015f);
        cam.fieldOfView = 55f;
        cam.nearClipPlane = 0.05f;
        if (cam.GetComponent<AudioListener>() == null && FindFirstObjectByType<AudioListener>() == null)
        {
            cam.gameObject.AddComponent<AudioListener>();
        }
    }

    // Editable empty transforms driving the 4-point / 3-segment dolly. Positions match the
    // original hardcoded tour so the shot framing is unchanged.
    private static void BuildTourPoints(Transform parent, out Transform[] points, out Transform[] looks)
    {
        Vector3[] camPos =
        {
            new Vector3(3.4f, 1.75f, -3.1f),  // doorway corner: wide establishing shot
            new Vector3(1.5f, 1.5f, 0.9f),    // dolly toward the monitor bank
            new Vector3(-1.9f, 1.5f, 0.4f),   // swing to the chalkboard report
            new Vector3(3.8f, 2.15f, -3.3f),  // settle: SE pull-back looking NW so BOTH the
                                              // west-wall chalkboard (center-left, above the stats
                                              // card) and the monitor bank (center-right) are fully
                                              // framed — the old (2.5,1.85,-2.3) clipped the board.
        };
        Vector3[] lookPos =
        {
            new Vector3(0f, 1.3f, 3.5f),
            new Vector3(0.4f, 1.7f, 3.95f),
            new Vector3(-4.9f, 1.55f, 0.4f),
            new Vector3(-1.4f, 1.75f, 2.4f),  // aim center-left between board + monitors, slightly up
        };

        points = new Transform[TourPointCount];
        looks = new Transform[TourPointCount];
        for (int i = 0; i < TourPointCount; i++)
        {
            points[i] = MakeMarker(CameraPointPrefix + (i + 1).ToString("00"), parent, camPos[i]);
            looks[i] = MakeMarker(LookTargetPrefix + (i + 1).ToString("00"), parent, lookPos[i]);
        }
    }

    private static Transform MakeMarker(string name, Transform parent, Vector3 position)
    {
        GameObject go = new GameObject(name);
        if (parent != null)
        {
            go.transform.SetParent(parent, false);
        }
        go.transform.localPosition = position;
        return go.transform;
    }

    // ---------------------------------------------------------------------------------------
    // UI overlay (title, subtitle, stats report, buttons) — built like the project's HUD.
    // ---------------------------------------------------------------------------------------

    private static GameObject BuildUi()
    {
        GameObject canvasGo = new GameObject(UiName);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        RectTransform root = canvas.GetComponent<RectTransform>();

        CanvasGroup titleGroup = MakeGroup("TitleGroup", root);
        TMP_Text title = MakeUiText("Title", (RectTransform)titleGroup.transform, "SCHOOL OVERRUN", RedAccent, 92,
            TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0f, -130f), new Vector2(1400f, 110f));
        title.fontStyle = FontStyles.Bold;

        CanvasGroup subtitleGroup = MakeGroup("SubtitleGroup", root);
        TMP_Text subtitle = MakeUiText("Subtitle", (RectTransform)subtitleGroup.transform,
            "The final bell rang. Nobody answered.", OffWhite, 30,
            TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0f, -218f), new Vector2(1200f, 44f));
        subtitle.fontStyle = FontStyles.Italic;

        CanvasGroup panelGroup = MakeGroup("PanelGroup", root);
        RectTransform panelRoot = (RectTransform)panelGroup.transform;

        RectTransform card = MakeUiPanel("StatsCard", panelRoot, PanelDark,
            new Vector2(0f, 0f), new Vector2(40f, 40f), new Vector2(430f, 320f));
        MakeUiText("StatsHeader", card, "FINAL REPORT", RedAccent, 30, TextAlignmentOptions.Center,
            new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(380f, 40f));

        string[] statNames = { "ROUND SURVIVED", "ZOMBIES KILLED", "FINAL POINTS", "BEST ROUND", "BEST SCORE" };
        for (int i = 0; i < statNames.Length; i++)
        {
            float y = -86f - i * 44f;
            MakeUiText("StatName_" + i, card, statNames[i], OffWhite, 22, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(28f, y), new Vector2(260f, 34f));
            MakeUiText("StatValue_" + i, card, "0", OffWhite, 24, TextAlignmentOptions.Right,
                new Vector2(1f, 1f), new Vector2(-28f, y), new Vector2(130f, 34f));
        }

        MakeButton(panelRoot, "Restart", "Restart Match", new Vector2(-280f, 60f));
        MakeButton(panelRoot, "MainMenu", "Main Menu", new Vector2(0f, 60f));
        MakeButton(panelRoot, "Quit", "Quit to Desktop", new Vector2(280f, 60f));

        return canvasGo;
    }

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null)
        {
            return;
        }
        GameObject go = new GameObject("EventSystem");
        go.AddComponent<UnityEngine.EventSystems.EventSystem>();
        go.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
    }

    private static CanvasGroup MakeGroup(string name, RectTransform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return go.AddComponent<CanvasGroup>();
    }

    private static RectTransform MakeUiPanel(string name, RectTransform parent, Color color,
        Vector2 anchor, Vector2 anchoredPos, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        Image img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return rt;
    }

    private static TMP_Text MakeUiText(string name, RectTransform parent, string text, Color color, int fontSize,
        TextAlignmentOptions align, Vector2 anchor, Vector2 anchoredPos, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.color = color;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = align;
        tmp.enableWordWrapping = false;
        tmp.raycastTarget = false;
        return tmp;
    }

    // Build a button (no onClick — the controller wires it at runtime by its stable name so a baked
    // scene needs no serialized UnityEvents). GameObject is named "Btn_<id>".
    private static void MakeButton(RectTransform parent, string id, string label, Vector2 anchoredPos)
    {
        GameObject go = new GameObject("Btn_" + id, typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(250f, 58f);

        Image img = go.GetComponent<Image>();
        img.color = new Color(0.10f, 0.10f, 0.11f, 0.92f);

        Button button = go.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.6f, 0.65f, 0.6f, 1f);
        colors.pressedColor = new Color(0.7f, 0.35f, 0.32f, 1f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        MakeUiText("Label", rt, label, OffWhite, 24, TextAlignmentOptions.Center,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240f, 40f));
    }

    // ---------------------------------------------------------------------------------------
    // Small world-building + resolve helpers (runtime-only materials/objects; no assets touched)
    // ---------------------------------------------------------------------------------------

    private static GameObject MakeBox(string name, Transform parent, Vector3 localCenter, Vector3 size, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localCenter;
        go.transform.localScale = size;
        Renderer r = go.GetComponent<Renderer>();
        if (r != null && mat != null)
        {
            r.material = mat;
        }
        return go;
    }

    private static Material MakeLitMaterial(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }
        Material mat = new Material(shader) { name = "Cinematic_" + name };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        return mat;
    }

    private static Material MakeUnlitMaterial(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) { shader = Shader.Find("Sprites/Default"); }
        if (shader == null) { shader = Shader.Find("Unlit/Color"); }
        Material mat = new Material(shader) { name = "Cinematic_" + name };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        mat.color = color;
        return mat;
    }

    private static TextMeshPro MakeWorldText(string name, Transform parent, Vector3 localPosition, Quaternion localRotation,
        Vector2 size, Color color, TextAlignmentOptions align)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = localRotation;

        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        tmp.rectTransform.sizeDelta = size;
        tmp.text = string.Empty;
        tmp.color = color;
        tmp.alignment = align;
        tmp.fontStyle = FontStyles.Bold;
        tmp.enableWordWrapping = true;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.2f;
        tmp.fontSizeMax = 6f;
        return tmp;
    }

    // Recursive by-name find (so authored objects can sit anywhere under a parent).
    private static Transform FindChildByName(Transform parent, string name)
    {
        if (parent == null)
        {
            return null;
        }
        if (parent.name == name)
        {
            return parent;
        }
        foreach (Transform child in parent)
        {
            Transform found = FindChildByName(child, name);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    private static Light GetLight(Transform parent, string name)
    {
        Transform t = FindChildByName(parent, name);
        return t != null ? t.GetComponent<Light>() : null;
    }

    private static CanvasGroup GetCanvasGroup(Transform parent, string name)
    {
        Transform t = FindChildByName(parent, name);
        return t != null ? t.GetComponent<CanvasGroup>() : null;
    }

    // Resolve an ordered set of marker transforms by name (CameraPoint_01.. / LookTarget_01..).
    // Returns null if any is missing so the caller can build the full set.
    private static Transform[] ResolvePoints(string prefix, int count)
    {
        Transform[] arr = new Transform[count];
        for (int i = 0; i < count; i++)
        {
            GameObject go = GameObject.Find(prefix + (i + 1).ToString("00"));
            if (go == null)
            {
                return null;
            }
            arr[i] = go.transform;
        }
        return arr;
    }

    private void WireButton(Transform uiRoot, string buttonName, UnityEngine.Events.UnityAction action)
    {
        Transform t = FindChildByName(uiRoot, buttonName);
        if (t == null)
        {
            return;
        }
        Button b = t.GetComponent<Button>();
        if (b == null)
        {
            return;
        }
        b.onClick.RemoveAllListeners();
        b.onClick.AddListener(action);
    }
}
