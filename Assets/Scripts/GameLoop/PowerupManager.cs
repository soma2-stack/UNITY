using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The four iconic Call of Duty Zombies power-up drops.
/// </summary>
public enum PowerupType
{
    MaxAmmo,
    InstaKill,
    DoublePoints,
    Nuke,
    Carpenter,
}

/// <summary>
/// Spawns and tracks the classic Zombies power-up drops.
///
/// Self-bootstraps in the "SchoolOfTheDead" scene (mirrors PerkManager / GameHud),
/// subscribes to <see cref="ZombieAgent.OnAnyZombieKilled"/> and, on a small random
/// chance, spawns a <see cref="Powerup"/> pickup at the kill position. Pickups call
/// back into <see cref="Apply"/> when collected.
///
/// Active timed effects (Insta-Kill, Double Points) are exposed as statics so other
/// systems (e.g. WeaponController, PlayerPoints) can read them without a reference.
/// A tiny IMGUI line shows the active effects + remaining time (its own OnGUI; the
/// shared GameHud is untouched).
/// </summary>
public class PowerupManager : MonoBehaviour
{
    public static PowerupManager Instance { get; private set; }

    [Header("Drop Chance")]
    [Tooltip("Probability (0..1) a killed zombie drops a power-up.")]
    [Range(0f, 1f)] public float dropChance = 0.035f;

    [Header("Timers")]
    [Tooltip("Seconds Insta-Kill stays active.")]
    public float instaKillDuration = 30f;
    [Tooltip("Seconds Double Points stays active.")]
    public float doublePointsDuration = 30f;

    [Header("Nuke")]
    [Tooltip("Bonus points awarded to the player when a Nuke is collected.")]
    public int nukeBonusPoints = 400;

    [Header("Carpenter")]
    [Tooltip("Bonus points awarded when a Carpenter is collected (boards up barricades in classic CoD).")]
    public int carpenterBonusPoints = 200;

    [Header("Pickup")]
    [Tooltip("Seconds a dropped pickup stays in the world before despawning.")]
    public float pickupLifetime = 15f;

    // --- Active timed-effect state (static so anything can read it) ---

    /// <summary>True while Insta-Kill is active (read by WeaponController.Fire).</summary>
    public static bool InstaKillActive { get; private set; }

    /// <summary>True while Double Points is active.</summary>
    public static bool DoublePointsActive { get; private set; }

    private static float instaKillEndTime;
    private static float doublePointsEndTime;

    private const string GameplayScene = "SchoolOfTheDead";
    private static PowerupManager _runtimeInstance;

    private GUIStyle hudStyle;

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
        // Reset effects on any (re)load so a fresh run starts clean.
        ClearEffects();

        if (scene.name != GameplayScene)
        {
            if (_runtimeInstance != null)
            {
                Destroy(_runtimeInstance.gameObject);
                _runtimeInstance = null;
            }
            return;
        }

        if (_runtimeInstance != null || FindFirstObjectByType<PowerupManager>() != null)
        {
            return;
        }

        var go = new GameObject("PowerupManager (Runtime)");
        _runtimeInstance = go.AddComponent<PowerupManager>();
    }

    private static void ClearEffects()
    {
        InstaKillActive = false;
        DoublePointsActive = false;
        instaKillEndTime = 0f;
        doublePointsEndTime = 0f;
        PlayerPoints.PointsMultiplier = 1;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (_runtimeInstance == null)
        {
            _runtimeInstance = this;
        }
    }

    private void OnEnable()
    {
        ZombieAgent.OnAnyZombieKilled += HandleZombieKilled;
    }

    private void OnDisable()
    {
        ZombieAgent.OnAnyZombieKilled -= HandleZombieKilled;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        if (_runtimeInstance == this)
        {
            _runtimeInstance = null;
        }
    }

    private void Update()
    {
        // Expire timed effects.
        if (InstaKillActive && Time.time >= instaKillEndTime)
        {
            InstaKillActive = false;
        }
        if (DoublePointsActive && Time.time >= doublePointsEndTime)
        {
            DoublePointsActive = false;
            PlayerPoints.PointsMultiplier = 1;
        }
    }

    private void HandleZombieKilled(Vector3 position)
    {
        if (Random.value > Mathf.Clamp01(dropChance))
        {
            return;
        }

        PowerupType type = (PowerupType)Random.Range(0, System.Enum.GetValues(typeof(PowerupType)).Length);
        SpawnPickup(type, position + Vector3.up * 0.5f);
    }

    private void SpawnPickup(PowerupType type, Vector3 position)
    {
        Powerup.Spawn(type, position, pickupLifetime);
    }

    /// <summary>
    /// Public entry point to drop a power-up pickup at a world position. Used by systems
    /// like RoundManager for milestone round-end drops; mirrors the internal random-drop
    /// path and uses the same <see cref="pickupLifetime"/>.
    /// </summary>
    public void SpawnPowerupAt(PowerupType type, Vector3 position)
    {
        SpawnPickup(type, position);
    }

    /// <summary>Apply a power-up's effect. Called by a <see cref="Powerup"/> on collect.</summary>
    public void Apply(PowerupType type)
    {
        switch (type)
        {
            case PowerupType.MaxAmmo:
            {
                WeaponController wc = FindFirstObjectByType<WeaponController>();
                if (wc != null)
                {
                    wc.RefillAllAmmo();
                }
                break;
            }

            case PowerupType.InstaKill:
                InstaKillActive = true;
                instaKillEndTime = Time.time + Mathf.Max(0f, instaKillDuration);
                break;

            case PowerupType.DoublePoints:
                DoublePointsActive = true;
                doublePointsEndTime = Time.time + Mathf.Max(0f, doublePointsDuration);
                PlayerPoints.PointsMultiplier = 2;
                break;

            case PowerupType.Nuke:
            {
                ZombieAgent[] zombies = FindObjectsByType<ZombieAgent>(FindObjectsSortMode.None);
                foreach (ZombieAgent z in zombies)
                {
                    if (z != null)
                    {
                        z.TakeDamage(99999);
                    }
                }
                if (PlayerPoints.Instance != null && nukeBonusPoints > 0)
                {
                    PlayerPoints.Instance.Add(nukeBonusPoints);
                }
                break;
            }

            case PowerupType.Carpenter:
            {
                // No boardable-window system in this project, so Carpenter awards its
                // classic flat points bonus to the player.
                if (PlayerPoints.Instance != null && carpenterBonusPoints > 0)
                {
                    PlayerPoints.Instance.Add(carpenterBonusPoints);
                }
                break;
            }
        }

        Debug.Log("[PowerupManager] Collected power-up: " + type);
    }

    /// <summary>Display name + base colour for a power-up type (shared by pickups + HUD).</summary>
    public static string DisplayName(PowerupType type)
    {
        switch (type)
        {
            case PowerupType.MaxAmmo: return "MAX AMMO";
            case PowerupType.InstaKill: return "INSTA-KILL";
            case PowerupType.DoublePoints: return "DOUBLE POINTS";
            case PowerupType.Nuke: return "NUKE";
            case PowerupType.Carpenter: return "CARPENTER";
            default: return type.ToString();
        }
    }

    public static Color ColorOf(PowerupType type)
    {
        switch (type)
        {
            case PowerupType.MaxAmmo: return new Color(0.3f, 0.7f, 1f);      // blue
            case PowerupType.InstaKill: return new Color(1f, 0.85f, 0.2f);   // gold
            case PowerupType.DoublePoints: return new Color(1f, 0.3f, 0.3f); // red
            case PowerupType.Nuke: return new Color(0.4f, 1f, 0.4f);         // green
            case PowerupType.Carpenter: return new Color(0.7f, 0.45f, 0.2f); // wood brown
            default: return Color.white;
        }
    }

    private void OnGUI()
    {
        if (!InstaKillActive && !DoublePointsActive)
        {
            return;
        }

        if (hudStyle == null)
        {
            hudStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
        }

        float y = Screen.height * 0.18f;

        if (InstaKillActive)
        {
            DrawEffect("INSTA-KILL  " + Mathf.CeilToInt(instaKillEndTime - Time.time) + "s",
                ColorOf(PowerupType.InstaKill), ref y);
        }
        if (DoublePointsActive)
        {
            DrawEffect("DOUBLE POINTS  " + Mathf.CeilToInt(doublePointsEndTime - Time.time) + "s",
                ColorOf(PowerupType.DoublePoints), ref y);
        }
    }

    private void DrawEffect(string label, Color color, ref float y)
    {
        float w = 320f;
        float h = 26f;
        Rect rect = new Rect((Screen.width - w) * 0.5f, y, w, h);

        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.85f);
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), label, hudStyle);
        GUI.color = color;
        GUI.Label(rect, label, hudStyle);
        GUI.color = prev;

        y += h + 2f;
    }
}
