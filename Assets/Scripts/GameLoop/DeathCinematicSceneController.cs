using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// PHASE 1 (preview only) — self-contained "SCHOOL OVERRUN" game-over cinematic scene
/// controller for the DeathCinematic scene. NOT wired into the real death/game-over flow;
/// GameOverController is untouched and still owns the live game-over path.
///
/// Concept: an abandoned school security office / final broadcast room. A slow camera
/// dolly tours a small staged set — flickering CRT security monitors cycling fake school
/// feeds, a chalkboard "final report", pulsing red emergency lighting, drifting dust —
/// then the title, placeholder stats, and Restart / Main Menu / Quit buttons fade in.
///
/// EVERYTHING is built at runtime from primitives, code-created materials, lights,
/// TextMeshPro and uGUI (mirroring how SchoolOfTheDeadHud / PauseMenuController build
/// their UI), so the DeathCinematic.unity scene file can be completely EMPTY: create it
/// via File > New Scene (Empty) and save it as Assets/Scenes/DeathCinematic.unity. This
/// controller bootstraps itself only in that scene (exact name match), exactly like the
/// project's other RuntimeInitializeOnLoadMethod systems, and never runs in gameplay.
///
/// Phase 1 buttons load scenes directly (no multiplayer/session logic):
///   Restart Match -> SchoolOfTheDead, Main Menu -> MainMenu, Quit -> Application.Quit.
/// Stats are inspector-tunable placeholders for the preview.
/// </summary>
public sealed class DeathCinematicSceneController : MonoBehaviour
{
    private const string CinematicScene = "DeathCinematic";
    private const string GameplayScene = "SchoolOfTheDead";
    private const string MainMenuScene = "MainMenu";

    private static DeathCinematicSceneController _instance;

    // --- Placeholder stats (Phase 1 preview values; later phases will feed real data) ---
    [Header("Placeholder Stats (Phase 1 preview)")]
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

    // --- Runtime state -------------------------------------------------------------------
    private sealed class Monitor
    {
        public Material screenMat;
        public TextMeshPro label;
        public int feed;
        public float nextRetune;
        public float staticUntil;
        public bool showingStatic;
        public float flickerSeed;
    }

    private readonly List<Monitor> monitors = new List<Monitor>();

    private Transform camT;
    private Vector3[] camPoints;
    private Vector3[] lookPoints;
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
    // project's other scene-gated runtime systems. Harmless in every other scene.
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
    // Build
    // ---------------------------------------------------------------------------------------

    private void Start()
    {
        // Scene-local safety: arriving here should never inherit a paused timescale.
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        sceneStartTime = Time.time;

        ApplyAtmosphere();
        BuildRoom();
        BuildMonitorWall();
        BuildChalkboard();
        BuildSetDressing();
        BuildLights();
        BuildDust();
        SetupCamera();
        BuildUi();

        built = true;
        Debug.Log("[DeathCinematic] Scene built (Phase 1 preview; not wired to the real death flow).");
    }

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

    // Dark, oppressive room mood: near-black ambient plus a thin red-grey exponential fog
    // for the dusty "everything has gone wrong" haze. Runtime-only (nothing is saved).
    private void ApplyAtmosphere()
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.05f, 0.06f, 0.08f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.05f;
        RenderSettings.fogColor = new Color(0.09f, 0.035f, 0.03f);
    }

    // Small security-office box: floor, ceiling, four walls, and a desk under the monitors.
    private void BuildRoom()
    {
        Transform room = new GameObject("Room").transform;

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

        // Security desk in front of the monitor wall.
        MakeBox("DeskTop", room, new Vector3(0f, 0.78f, 2.9f), new Vector3(4.2f, 0.08f, 0.9f), deskMat);
        MakeBox("DeskLegL", room, new Vector3(-1.95f, 0.38f, 2.9f), new Vector3(0.1f, 0.76f, 0.8f), deskMat);
        MakeBox("DeskLegR", room, new Vector3(1.95f, 0.38f, 2.9f), new Vector3(0.1f, 0.76f, 0.8f), deskMat);
    }

    // Five CRT-style security monitors on the north wall, each with a live feed label the
    // controller cycles/flickers at runtime.
    private void BuildMonitorWall()
    {
        Transform bank = new GameObject("MonitorBank").transform;
        Material bezelMat = MakeLitMaterial("Bezel", new Color(0.06f, 0.06f, 0.07f));

        // Layout: three monitors on top, two below, centered on the north wall.
        Vector2[] slots =
        {
            new Vector2(-1.25f, 2.05f), new Vector2(0f, 2.05f), new Vector2(1.25f, 2.05f),
            new Vector2(-0.62f, 1.3f), new Vector2(0.62f, 1.3f),
        };

        for (int i = 0; i < slots.Length; i++)
        {
            Vector2 s = slots[i];
            MakeBox("MonitorBezel_" + i, bank, new Vector3(s.x, s.y, 3.96f), new Vector3(1.05f, 0.72f, 0.08f), bezelMat);

            // Screen: an UNLIT face so it reads as a glowing CRT without needing bloom.
            Material screenMat = MakeUnlitMaterial("Screen_" + i, ScreenBase);
            MakeBox("MonitorScreen_" + i, bank, new Vector3(s.x, s.y, 3.90f), new Vector3(0.92f, 0.6f, 0.02f), screenMat);

            // Feed label on the glass (readable from inside the room).
            TextMeshPro label = MakeWorldText("FeedLabel_" + i, bank,
                new Vector3(s.x, s.y, 3.87f), Quaternion.identity,
                new Vector2(0.86f, 0.54f), ScreenText, TextAlignmentOptions.Center);

            Monitor m = new Monitor
            {
                screenMat = screenMat,
                label = label,
                feed = i % FeedLabels.Length, // all five feeds visible initially
                nextRetune = Time.time + retuneInterval + i * 1.3f, // staggered re-tunes
                flickerSeed = i * 17.31f,
            };
            SetFeedLabel(m);
            monitors.Add(m);
        }

        // Strip label above the bank.
        MakeWorldText("FeedStrip", bank, new Vector3(0f, 2.62f, 3.9f), Quaternion.identity,
            new Vector2(3.4f, 0.22f), RedAccent, TextAlignmentOptions.Center)
            .text = "SECURITY FEED — <color=#FF4030>● LIVE</color>";
    }

    // Chalkboard "final report" on the west wall with the placeholder run stats.
    private void BuildChalkboard()
    {
        Transform boardRoot = new GameObject("Chalkboard").transform;
        Material boardMat = MakeLitMaterial("Board", new Color(0.09f, 0.16f, 0.11f));
        Material frameMat = MakeLitMaterial("BoardFrame", new Color(0.25f, 0.18f, 0.10f));

        MakeBox("BoardFrame", boardRoot, new Vector3(-4.96f, 1.6f, 0.4f), new Vector3(0.05f, 1.85f, 3.0f), frameMat);
        MakeBox("BoardFace", boardRoot, new Vector3(-4.92f, 1.6f, 0.4f), new Vector3(0.04f, 1.65f, 2.8f), boardMat);

        // Chalk text faces into the room (+X side of the west wall).
        TextMeshPro chalk = MakeWorldText("BoardText", boardRoot,
            new Vector3(-4.88f, 1.62f, 0.4f), Quaternion.Euler(0f, -90f, 0f),
            new Vector2(2.5f, 1.45f), new Color(0.88f, 0.90f, 0.86f, 0.95f), TextAlignmentOptions.Left);
        chalk.text =
            "FINAL REPORT\n" +
            "ROUND SURVIVED ......... " + roundSurvived + "\n" +
            "ZOMBIES KILLED ......... " + zombiesKilled + "\n" +
            "FINAL POINTS ........... " + finalPoints + "\n" +
            "BEST ROUND ............. " + bestRound + "\n" +
            "BEST SCORE ............. " + bestScore;
    }

    // A little chaos so the office feels abandoned mid-crisis: a toppled chair + papers.
    private void BuildSetDressing()
    {
        Transform props = new GameObject("SetDressing").transform;
        Material chairMat = MakeLitMaterial("Chair", new Color(0.12f, 0.13f, 0.15f));
        Material paperMat = MakeLitMaterial("Paper", new Color(0.72f, 0.70f, 0.64f));

        GameObject chair = MakeBox("FallenChair", props, new Vector3(1.7f, 0.24f, 1.7f), new Vector3(0.45f, 0.5f, 0.45f), chairMat);
        chair.transform.rotation = Quaternion.Euler(0f, 25f, 82f);

        MakeBox("Paper_0", props, new Vector3(-0.7f, 0.006f, 1.4f), new Vector3(0.21f, 0.01f, 0.3f), paperMat)
            .transform.rotation = Quaternion.Euler(0f, 24f, 0f);
        MakeBox("Paper_1", props, new Vector3(0.4f, 0.006f, 0.6f), new Vector3(0.21f, 0.01f, 0.3f), paperMat)
            .transform.rotation = Quaternion.Euler(0f, -51f, 0f);
        MakeBox("Paper_2", props, new Vector3(-1.6f, 0.006f, -0.5f), new Vector3(0.21f, 0.01f, 0.3f), paperMat)
            .transform.rotation = Quaternion.Euler(0f, 133f, 0f);
    }

    // Emergency-red pulse + a slowly sweeping beacon + faint cool fill + CRT glow.
    private void BuildLights()
    {
        Transform lights = new GameObject("Lights").transform;

        GameObject red = new GameObject("EmergencyLight");
        red.transform.SetParent(lights, false);
        red.transform.position = new Vector3(0f, 2.7f, 1.2f);
        emergencyLight = red.AddComponent<Light>();
        emergencyLight.type = LightType.Point;
        emergencyLight.color = new Color(1f, 0.12f, 0.08f);
        emergencyLight.range = 14f;
        emergencyLight.intensity = 1.4f;

        // Rotating beacon: a red spot swinging around the ceiling for moving shadows/mood.
        GameObject pivot = new GameObject("BeaconPivot");
        pivot.transform.SetParent(lights, false);
        pivot.transform.position = new Vector3(0f, 2.85f, 0.6f);
        beaconPivot = pivot.transform;
        GameObject beacon = new GameObject("BeaconSpot");
        beacon.transform.SetParent(beaconPivot, false);
        beacon.transform.localRotation = Quaternion.Euler(38f, 0f, 0f);
        Light spot = beacon.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.color = new Color(1f, 0.22f, 0.12f);
        spot.range = 16f;
        spot.spotAngle = 55f;
        spot.intensity = 3.2f;

        // Faint cool fill so the room is never pure black between red pulses.
        GameObject fill = new GameObject("CoolFill");
        fill.transform.SetParent(lights, false);
        fill.transform.position = new Vector3(-2.2f, 2.2f, -1.6f);
        Light fillLight = fill.AddComponent<Light>();
        fillLight.type = LightType.Point;
        fillLight.color = new Color(0.30f, 0.38f, 0.52f);
        fillLight.range = 11f;
        fillLight.intensity = 0.5f;

        // Sickly green CRT spill near the monitor bank.
        GameObject glow = new GameObject("MonitorGlow");
        glow.transform.SetParent(lights, false);
        glow.transform.position = new Vector3(0f, 1.7f, 3.2f);
        monitorGlow = glow.AddComponent<Light>();
        monitorGlow.type = LightType.Point;
        monitorGlow.color = new Color(0.35f, 0.8f, 0.5f);
        monitorGlow.range = 5f;
        monitorGlow.intensity = 0.8f;
    }

    // Slow drifting dust motes; same runtime-particle approach as the gun's fallback flash.
    private void BuildDust()
    {
        GameObject go = new GameObject("DustMotes");
        go.transform.position = new Vector3(0f, 1.6f, 0.8f);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
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
    }

    // Camera: reuse whatever camera the (possibly non-empty template) scene has, else create
    // one. Tour: 4 points / 3 eased dolly segments, then settle on the final shot with sway.
    private void SetupCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            cam = FindFirstObjectByType<Camera>();
        }
        if (cam == null)
        {
            GameObject go = new GameObject("CinematicCamera");
            cam = go.AddComponent<Camera>();
        }
        if (FindFirstObjectByType<AudioListener>() == null)
        {
            cam.gameObject.AddComponent<AudioListener>();
        }

        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.01f, 0.01f, 0.015f);
        cam.fieldOfView = 55f;
        cam.nearClipPlane = 0.05f;
        camT = cam.transform;

        camPoints = new[]
        {
            new Vector3(3.4f, 1.75f, -3.1f),  // doorway corner: wide establishing shot
            new Vector3(1.5f, 1.5f, 0.9f),    // dolly toward the monitor bank
            new Vector3(-1.9f, 1.5f, 0.4f),   // swing to the chalkboard report
            new Vector3(2.5f, 1.85f, -2.3f),  // settle: composed wide (monitors + board)
        };
        lookPoints = new[]
        {
            new Vector3(0f, 1.3f, 3.5f),
            new Vector3(0.4f, 1.7f, 3.95f),
            new Vector3(-4.9f, 1.55f, 0.4f),
            new Vector3(-0.4f, 1.35f, 2.2f),
        };

        camT.position = camPoints[0];
        camT.rotation = Quaternion.LookRotation(lookPoints[0] - camPoints[0]);
    }

    // ---------------------------------------------------------------------------------------
    // Per-frame animation
    // ---------------------------------------------------------------------------------------

    private void UpdateCameraTour(float elapsed)
    {
        if (camT == null || camPoints == null || camPoints.Length < 2)
        {
            return;
        }

        int segments = camPoints.Length - 1;
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
            pos = camPoints[segments] + sway;
            look = lookPoints[segments] + sway * 0.4f;
        }
        else
        {
            int i = Mathf.Min(segments - 1, (int)(elapsed / segDur));
            float t = SmoothStep01((elapsed - i * segDur) / segDur);
            pos = Vector3.Lerp(camPoints[i], camPoints[i + 1], t);
            look = Vector3.Lerp(lookPoints[i], lookPoints[i + 1], t);
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
            Monitor m = monitors[i];
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
        if (!statsCountDone && statValueTexts != null && elapsed >= buttonsRevealDelay)
        {
            float t = Mathf.Clamp01((elapsed - buttonsRevealDelay) / Mathf.Max(0.05f, statsCountUpDuration));
            float eased = SmoothStep01(t);
            for (int i = 0; i < statValueTexts.Length; i++)
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

    private void SetFeedLabel(Monitor m)
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
    // UI overlay (title, subtitle, stats report, buttons) — built like the project's HUD.
    // ---------------------------------------------------------------------------------------

    private void BuildUi()
    {
        EnsureEventSystem();

        GameObject canvasGo = new GameObject("DeathCinematicUI");
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

        // Title (top-center), hidden until its reveal.
        titleGroup = MakeGroup("TitleGroup", root);
        TMP_Text title = MakeUiText("Title", (RectTransform)titleGroup.transform, "SCHOOL OVERRUN", RedAccent, 92,
            TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0f, -130f), new Vector2(1400f, 110f));
        title.fontStyle = FontStyles.Bold;

        subtitleGroup = MakeGroup("SubtitleGroup", root);
        TMP_Text subtitle = MakeUiText("Subtitle", (RectTransform)subtitleGroup.transform,
            "The final bell rang. Nobody answered.", OffWhite, 30,
            TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0f, -218f), new Vector2(1200f, 44f));
        subtitle.fontStyle = FontStyles.Italic;

        // Stats report + buttons share one late-reveal group.
        panelGroup = MakeGroup("PanelGroup", root);
        RectTransform panelRoot = (RectTransform)panelGroup.transform;

        // Stats report card, lower-left.
        RectTransform card = MakeUiPanel("StatsCard", panelRoot, PanelDark,
            new Vector2(0f, 0f), new Vector2(40f, 40f), new Vector2(430f, 320f));
        MakeUiText("StatsHeader", card, "FINAL REPORT", RedAccent, 30, TextAlignmentOptions.Center,
            new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(380f, 40f));

        string[] statNames = { "ROUND SURVIVED", "ZOMBIES KILLED", "FINAL POINTS", "BEST ROUND", "BEST SCORE" };
        statTargets = new[] { roundSurvived, zombiesKilled, finalPoints, bestRound, bestScore };
        statValueTexts = new TMP_Text[statNames.Length];
        for (int i = 0; i < statNames.Length; i++)
        {
            float y = -86f - i * 44f;
            MakeUiText("StatName_" + i, card, statNames[i], OffWhite, 22, TextAlignmentOptions.Left,
                new Vector2(0f, 1f), new Vector2(28f, y), new Vector2(260f, 34f));
            statValueTexts[i] = MakeUiText("StatValue_" + i, card, "0", OffWhite, 24, TextAlignmentOptions.Right,
                new Vector2(1f, 1f), new Vector2(-28f, y), new Vector2(130f, 34f));
        }

        // Buttons row, bottom-center.
        MakeButton(panelRoot, "Restart Match", new Vector2(-280f, 60f), () => SceneManager.LoadScene(GameplayScene));
        MakeButton(panelRoot, "Main Menu", new Vector2(0f, 60f), () => SceneManager.LoadScene(MainMenuScene));
        MakeButton(panelRoot, "Quit to Desktop", new Vector2(280f, 60f), Application.Quit);

        // Everything starts invisible; UpdateReveals fades the groups in on schedule.
        titleGroup.alpha = 0f;
        subtitleGroup.alpha = 0f;
        panelGroup.alpha = 0f;
        panelGroup.interactable = false;
        panelGroup.blocksRaycasts = false;
    }

    // Same guarantee the pause menu / main menu use so buttons always click.
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

    private static void MakeButton(RectTransform parent, string label, Vector2 anchoredPos, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("Button_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
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
        button.onClick.AddListener(onClick);

        MakeUiText("Label", rt, label, OffWhite, 24, TextAlignmentOptions.Center,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240f, 40f));
    }

    // ---------------------------------------------------------------------------------------
    // Small world-building helpers (runtime-only materials/objects; no assets touched)
    // ---------------------------------------------------------------------------------------

    private static GameObject MakeBox(string name, Transform parent, Vector3 center, Vector3 size, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = center;
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

    private static TextMeshPro MakeWorldText(string name, Transform parent, Vector3 position, Quaternion rotation,
        Vector2 size, Color color, TextAlignmentOptions align)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(position, rotation);

        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        tmp.rectTransform.sizeDelta = size;
        tmp.text = string.Empty;
        tmp.color = color;
        tmp.alignment = align;
        tmp.fontStyle = FontStyles.Bold;
        tmp.enableWordWrapping = true;
        // Auto-size to the world-space rect so text is deterministic without hand-tuned sizes.
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.2f;
        tmp.fontSizeMax = 6f;
        return tmp;
    }
}
