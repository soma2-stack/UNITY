using System.Collections;
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
    // Team-wide Infinite Ammo (replaces the old, unused Carpenter drop). Kept at the
    // same enum position (4) so drop weights and network serialization stay compatible.
    InfiniteAmmo,
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
    [Tooltip("Seconds team-wide Infinite Ammo stays active.")]
    public float infiniteAmmoDuration = 15f;

    [Header("Nuke")]
    [Tooltip("Bonus points awarded to the player when a Nuke is collected.")]
    public int nukeBonusPoints = 400;
    [Tooltip("Flash the screen white when a Nuke fires (CoD-style). Disable to skip the flash.")]
    public bool nukeFlashEnabled = true;

    [Header("Legacy (unused)")]
    [Tooltip("Legacy Carpenter bonus points. Unused since the Carpenter slot became the " +
             "Infinite Ammo power-up; kept only so any scene-serialized PowerupManager " +
             "retains its stored value.")]
    public int carpenterBonusPoints = 200;

    [Header("Pickup")]
    [Tooltip("Seconds a dropped pickup stays in the world before despawning.")]
    public float pickupLifetime = 15f;
    [Tooltip("Metres the visual pickup is lifted above the drop/death position so the 3D model " +
             "floats at eye-catching height instead of sinking into the floor. Applied centrally " +
             "in SpawnPickup, so random drops and SpawnPowerupAt share the same height.")]
    public float pickupSpawnHeight = 1.25f;

    [Header("Pickup Prefabs (optional 3D models)")]
    [Tooltip("Optional 3D pickup model for each power-up type. When assigned, that prefab is " +
             "spawned instead of the generated coloured cube; leave one empty to keep the cube " +
             "fallback for that type. Purely visual — effects, timers, drop rates and networking " +
             "are unchanged. Assign these on the scene PowerupManager in the Inspector.")]
    public GameObject maxAmmoPrefab;
    public GameObject instaKillPrefab;
    public GameObject doublePointsPrefab;
    public GameObject nukePrefab;
    public GameObject infiniteAmmoPrefab;

    // --- Active timed-effect state (static so anything can read it) ---

    /// <summary>True while Insta-Kill is active (read by WeaponController.Fire).</summary>
    public static bool InstaKillActive { get; private set; }

    /// <summary>True while Double Points is active.</summary>
    public static bool DoublePointsActive { get; private set; }

    /// <summary>True while team-wide Infinite Ammo is active (read by WeaponController).</summary>
    public static bool InfiniteAmmoActive { get; private set; }

    private static float instaKillEndTime;
    private static float doublePointsEndTime;
    private static float infiniteAmmoEndTime;

    private bool nukeFlashActive;   // true while the nuke white flash is on screen
    private float nukeFlashEndTime;

    // Single source of truth for the Double Points multiplier value.
    private const int DoublePointsMultiplier = 2;

    private const string GameplayScene = "SchoolOfTheDead";
    private static PowerupManager _runtimeInstance;

    // Weighted drop table (CoD-style: common drops far more frequent than rare ones).
    // Index order MUST match the PowerupType enum: MaxAmmo=0, InstaKill=1,
    // DoublePoints=2, Nuke=3, InfiniteAmmo=4. Total is 100 so each weight is ~its %:
    //   MaxAmmo 35%, InstaKill 20%, DoublePoints 30%, Nuke 10%, InfiniteAmmo 5%.
    private static readonly float[] DropWeights = { 35f, 20f, 30f, 10f, 5f };

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
        InfiniteAmmoActive = false;
        instaKillEndTime = 0f;
        doublePointsEndTime = 0f;
        infiniteAmmoEndTime = 0f;
        PlayerPoints.PointsMultiplier = 1; // clear any DoublePointsMultiplier back to normal
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
        if (InfiniteAmmoActive && Time.time >= infiniteAmmoEndTime)
        {
            InfiniteAmmoActive = false;
        }
        if (nukeFlashActive && Time.time >= nukeFlashEndTime)
        {
            nukeFlashActive = false;
        }
    }

    /// <summary>
    /// Nuke: flash the screen white (0.3s), then kill every living zombie with a small
    /// stagger (~0.05s each) so they drop over roughly a second, then award the bonus.
    /// </summary>
    private IEnumerator NukeRoutine()
    {
        if (nukeFlashEnabled)
        {
            nukeFlashActive = true;
            nukeFlashEndTime = Time.time + 0.3f;
            yield return new WaitForSeconds(0.3f);
        }

        ZombieAgent[] zombies = FindObjectsByType<ZombieAgent>(FindObjectsSortMode.None);
        foreach (ZombieAgent z in zombies)
        {
            if (z != null && !z.IsDead)
            {
                z.TakeDamage(99999);
                yield return new WaitForSeconds(0.05f);
            }
        }

        if (PlayerPoints.Instance != null && nukeBonusPoints > 0)
        {
            // Classic Nuke bonus goes to every player (per-player economy).
            PlayerPoints.Instance.AddPointsToAll(nukeBonusPoints);
        }
    }

    private void HandleZombieKilled(Vector3 position)
    {
        if (NetworkGameplayCoordinator.IsNetworkActive && !NetworkGameplayCoordinator.IsServer)
        {
            return;
        }

        if (Random.value > Mathf.Clamp01(dropChance))
        {
            return;
        }

        PowerupType type = PickWeightedRandom();
        SpawnPickup(type, position); // SpawnPickup applies the shared pickupSpawnHeight lift
    }

    /// <summary>
    /// Picks a power-up type using <see cref="DropWeights"/>: sums the weights, rolls a
    /// random value in [0, total), then walks the table to find the selected type.
    /// </summary>
    private PowerupType PickWeightedRandom()
    {
        float total = 0f;
        for (int i = 0; i < DropWeights.Length; i++)
        {
            total += DropWeights[i];
        }

        float roll = Random.value * total;
        float cumulative = 0f;
        for (int i = 0; i < DropWeights.Length; i++)
        {
            cumulative += DropWeights[i];
            if (roll < cumulative)
            {
                return (PowerupType)i;
            }
        }

        return PowerupType.MaxAmmo; // fallback (only if weights are empty/zero)
    }

    private void SpawnPickup(PowerupType type, Vector3 position)
    {
        // Lift the visual pickup above the drop/death position centrally, so every spawn path
        // (random drops + SpawnPowerupAt) floats at the same height. The already-lifted transform
        // position is what the server broadcasts, so clients match without any payload change.
        Vector3 spawnPosition = position + Vector3.up * pickupSpawnHeight;

        bool networked = NetworkGameplayCoordinator.IsNetworkActive;
        int id = networked && NetworkGameplayCoordinator.IsServer
            ? NetworkGameplayCoordinator.AllocatePowerupId()
            : 0;
        Powerup powerup = Powerup.Spawn(type, spawnPosition, pickupLifetime, id, networked);
        if (networked && NetworkGameplayCoordinator.IsServer)
        {
            NetworkGameplayCoordinator.BroadcastPowerupSpawn(powerup);
        }
    }

    /// <summary>
    /// Public entry point to drop a power-up pickup at a world position. Used by systems
    /// like RoundManager for milestone round-end drops; mirrors the internal random-drop
    /// path and uses the same <see cref="pickupLifetime"/>.
    /// </summary>
    public void SpawnPowerupAt(PowerupType type, Vector3 position)
    {
        if (NetworkGameplayCoordinator.IsNetworkActive && !NetworkGameplayCoordinator.IsServer)
        {
            return;
        }
        SpawnPickup(type, position);
    }

    /// <summary>Apply a power-up's effect. Called by a <see cref="Powerup"/> on collect.</summary>
    public void Apply(PowerupType type)
    {
        Apply(type, ulong.MaxValue);
    }

    public void Apply(PowerupType type, ulong collectorClientId)
    {
        if (NetworkGameplayCoordinator.IsNetworkActive && !NetworkGameplayCoordinator.IsServer)
        {
            return;
        }

        switch (type)
        {
            case PowerupType.MaxAmmo:
            {
                WeaponController wc = LocalPlayer.Weapon;
                if (wc != null)
                {
                    wc.RefillAllAmmo();
                }
                NetworkGameplayCoordinator.BroadcastPowerupEffect(type, 0f);
                break;
            }

            case PowerupType.InstaKill:
                InstaKillActive = true;
                instaKillEndTime = Time.time + Mathf.Max(0f, instaKillDuration);
                NetworkGameplayCoordinator.BroadcastPowerupEffect(type, instaKillDuration);
                break;

            case PowerupType.DoublePoints:
                if (DoublePointsActive)
                {
                    // Already active: just refresh the timer, never re-stack the multiplier.
                    doublePointsEndTime = Time.time + Mathf.Max(0f, doublePointsDuration);
                    NetworkGameplayCoordinator.BroadcastPowerupEffect(type, doublePointsDuration);
                    Debug.Log("[PowerupManager] Double Points timer refreshed.");
                    return;
                }
                DoublePointsActive = true;
                doublePointsEndTime = Time.time + Mathf.Max(0f, doublePointsDuration);
                PlayerPoints.PointsMultiplier = DoublePointsMultiplier;
                NetworkGameplayCoordinator.BroadcastPowerupEffect(type, doublePointsDuration);
                Debug.Log("[PowerupManager] Double Points activated.");
                break;

            case PowerupType.Nuke:
                NetworkGameplayCoordinator.BroadcastPowerupEffect(type, 0.3f);
                // Flash the screen, then kill all zombies staggered over ~1s, then
                // award the bonus (handled in the coroutine).
                StartCoroutine(NukeRoutine());
                break;

            case PowerupType.InfiniteAmmo:
                // Team-wide Infinite Ammo: activate (or refresh) the timer and broadcast it so
                // every player — host and clients — fires without spending ammo for its duration.
                // Setting the end time unconditionally refreshes to a fresh window when picked up
                // again while already active (mirrors the Double Points refresh behaviour), so
                // repeated pickups never stack into duplicate timers.
                InfiniteAmmoActive = true;
                infiniteAmmoEndTime = Time.time + Mathf.Max(0f, infiniteAmmoDuration);
                NetworkGameplayCoordinator.BroadcastPowerupEffect(type, infiniteAmmoDuration);
                Debug.Log("[PowerupManager] Infinite Ammo activated/refreshed (" +
                          Mathf.Max(0f, infiniteAmmoDuration) + "s).");
                break;
        }

        Debug.Log("[PowerupManager] Collected power-up: " + type);
    }

    public static void ApplyNetworkEffect(PowerupType type, float remaining)
    {
        switch (type)
        {
            case PowerupType.MaxAmmo:
                LocalPlayer.Weapon?.RefillAllAmmo();
                break;
            case PowerupType.InstaKill:
                InstaKillActive = true;
                instaKillEndTime = Time.time + Mathf.Max(0f, remaining);
                break;
            case PowerupType.DoublePoints:
                DoublePointsActive = true;
                doublePointsEndTime = Time.time + Mathf.Max(0f, remaining);
                PlayerPoints.PointsMultiplier = DoublePointsMultiplier;
                break;
            case PowerupType.Nuke:
                if (Instance != null && Instance.nukeFlashEnabled)
                {
                    Instance.nukeFlashActive = true;
                    Instance.nukeFlashEndTime = Time.time + 0.3f;
                }
                break;
            case PowerupType.InfiniteAmmo:
                InfiniteAmmoActive = true;
                infiniteAmmoEndTime = Time.time + Mathf.Max(0f, remaining);
                break;
        }
    }

    public static void SendActiveEffectsToClient(ulong clientId)
    {
        if (!NetworkGameplayCoordinator.IsNetworkActive || !NetworkGameplayCoordinator.IsServer)
        {
            return;
        }

        if (InstaKillActive)
        {
            NetworkGameplayCoordinator.SendPowerupEffectToClient(clientId, PowerupType.InstaKill, Mathf.Max(0f, instaKillEndTime - Time.time));
        }
        if (DoublePointsActive)
        {
            NetworkGameplayCoordinator.SendPowerupEffectToClient(clientId, PowerupType.DoublePoints, Mathf.Max(0f, doublePointsEndTime - Time.time));
        }
        if (InfiniteAmmoActive)
        {
            NetworkGameplayCoordinator.SendPowerupEffectToClient(clientId, PowerupType.InfiniteAmmo, Mathf.Max(0f, infiniteAmmoEndTime - Time.time));
        }
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
            case PowerupType.InfiniteAmmo: return "INFINITE AMMO";
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
            case PowerupType.InfiniteAmmo: return new Color(0.7f, 0.45f, 0.2f); // placeholder (inherited Carpenter brown) — recolor later
            default: return Color.white;
        }
    }

    /// <summary>
    /// The assigned 3D pickup prefab for a power-up type, or null when none is set (the caller
    /// then uses the generated-cube fallback). Purely a visual model lookup.
    /// </summary>
    public GameObject GetPickupPrefab(PowerupType type)
    {
        switch (type)
        {
            case PowerupType.MaxAmmo: return maxAmmoPrefab;
            case PowerupType.InstaKill: return instaKillPrefab;
            case PowerupType.DoublePoints: return doublePointsPrefab;
            case PowerupType.Nuke: return nukePrefab;
            case PowerupType.InfiniteAmmo: return infiniteAmmoPrefab;
            default: return null;
        }
    }

    /// <summary>
    /// Null-safe resolver so <see cref="Powerup.Spawn"/> — which runs on every peer, including
    /// clients — can look up the LOCAL pickup prefab without a hard dependency on a live manager.
    /// Returns null when there is no manager or no prefab assigned, so the caller falls back to
    /// the generated cube. Never affects effects/timers/networking.
    /// </summary>
    public static GameObject ResolvePickupPrefab(PowerupType type)
    {
        return Instance != null ? Instance.GetPickupPrefab(type) : null;
    }

    private void OnGUI()
    {
        // Nuke white flash (fades out over its 0.3s window), drawn over everything.
        if (nukeFlashActive)
        {
            float remaining = Mathf.Clamp01((nukeFlashEndTime - Time.time) / 0.3f);
            Color prevC = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.7f * remaining);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prevC;
        }

        if (!InstaKillActive && !DoublePointsActive && !InfiniteAmmoActive)
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
        if (InfiniteAmmoActive)
        {
            DrawEffect("INFINITE AMMO  " + Mathf.CeilToInt(infiniteAmmoEndTime - Time.time) + "s",
                ColorOf(PowerupType.InfiniteAmmo), ref y);
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
