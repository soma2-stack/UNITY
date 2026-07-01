// ✅ CHECKPOINT 3 — RoundManager (existing file enhanced, not duplicated):
// self-bootstraps in SchoolOfTheDead, exposes ZombiesRemainingThisRound, and
// guards a missing/empty ZombieSpawner so CurrentRound advances even before any
// enemies / NavMesh are wired up. Power-ups now drop randomly on kills
// (PowerupManager), so the old round-end milestone drop was removed.
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Drives the wave/round loop (Call of Duty Zombies style).
///
/// Each round it asks the <see cref="ZombieSpawner"/> to spawn a number of
/// zombies whose count, health and speed scale up with the round number. Once
/// every zombie of the round is dead it waits <see cref="timeBetweenRounds"/>
/// seconds, then advances to the next round.
///
/// Self-bootstraps in the "SchoolOfTheDead" scene (mirrors PerkManager / GameHud
/// / PowerupManager) so <see cref="CurrentRound"/> always has a live source even
/// with zero manual scene setup. If no functional spawner is wired yet it still
/// advances rounds on a fallback timer so the round counter keeps progressing.
/// </summary>
public class RoundManager : MonoBehaviour
{
    /// <summary>Singleton accessor (mirrors PerkManager / PowerupManager).</summary>
    public static RoundManager Instance { get; private set; }

    [Header("References")]
    [Tooltip("Spawner that produces this round's zombies. Auto-found if left empty.")]
    public ZombieSpawner spawner;

    [Header("Round Pacing")]
    [Tooltip("Round to start on (usually 1).")]
    public int startRound = 1;
    [Tooltip("Delay (seconds) after a round is cleared before the next begins.")]
    public float timeBetweenRounds = 10f;
    [Tooltip("Short delay before the very first round starts (lets the scene settle / NavMesh load).")]
    public float startDelay = 2f;
    [Tooltip("Fallback round length (seconds) used ONLY when no functional ZombieSpawner " +
             "is wired yet (no prefab / no spawn points). Lets CurrentRound keep advancing " +
             "during greyboxing before enemies exist; ignored once a spawner is functional.")]
    public float noEnemyRoundDuration = 8f;

    [Header("Scaling Formulas")]
    [Tooltip("Base number of zombies in round 1.")]
    public int baseZombieCount = 6;
    [Tooltip("Extra zombies added per round.")]
    public int zombiesPerRound = 2;
    [Tooltip("Hard ceiling on how many zombies a single round can spawn (CoD caps at 24).")]
    public int maxZombiesPerRound = 24;
    [Tooltip("LEGACY (no longer read): the per-round zombie health curve is defined in code " +
             "in CalculateZombieHealth() so the tuned early-round values apply everywhere.")]
    public int baseZombieHealth = 150;
    [Tooltip("LEGACY (no longer read): see CalculateZombieHealth() for the health curve.")]
    public int healthPerRound = 50;
    [Tooltip("Base zombie speed in round 1.")]
    public float baseZombieSpeed = 3.0f;
    [Tooltip("Speed added per round, up to maxZombieSpeed.")]
    public float speedPerRound = 0.1f;
    [Tooltip("Hard cap on zombie speed so late rounds stay fair.")]
    public float maxZombieSpeed = 6.0f;

    [Header("UI")]
    [Tooltip("Draw a lightweight OnGUI 'Round: N' label in the top-right.")]
    public bool showHud = true;

    /// <summary>Raised whenever the round number changes (including the first round).</summary>
    public event System.Action<int> OnRoundChanged;

    /// <summary>The current round number.</summary>
    public int CurrentRound { get; private set; }

    /// <summary>
    /// Zombies still to deal with this round (queued-to-spawn + currently alive).
    /// 0 when no spawner is wired. Exposed so the HUD can display it.
    /// </summary>
    public int ZombiesRemainingThisRound =>
        spawner != null ? Mathf.Max(0, spawner.RemainingToSpawn + spawner.AliveCount) : 0;

    /// <summary>True during the between-rounds intermission (the start banner shows then).</summary>
    public static bool IntermissionActive { get; private set; }
    /// <summary>The round number that begins after the current intermission.</summary>
    public static int UpcomingRound { get; private set; }
    /// <summary>Seconds remaining in the current intermission (drives the banner fade).</summary>
    public static float IntermissionRemaining { get; private set; }

    // Simple internal state machine.
    private enum State { Idle, Starting, InProgress, Intermission }
    private State state = State.Idle;
    private float stateTimer;

    private const string GameplayScene = "SchoolOfTheDead";
    private static RoundManager _runtimeInstance;

    /// <summary>
    /// Auto-spawn the round manager when the gameplay scene loads (mirrors PerkManager /
    /// GameHud / PowerupManager) so CurrentRound is always available even without manual
    /// scene setup. Removed when leaving gameplay so it never lingers over the menu.
    /// </summary>
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
            if (_runtimeInstance != null)
            {
                Destroy(_runtimeInstance.gameObject);
                _runtimeInstance = null;
            }
            return;
        }

        // Don't add a second manager if the scene already has one wired up
        // (e.g. placed on the ZombieSpawner object).
        if (_runtimeInstance != null || FindFirstObjectByType<RoundManager>() != null)
        {
            return;
        }

        var go = new GameObject("RoundManager (Runtime)");
        _runtimeInstance = go.AddComponent<RoundManager>();
    }

    private void Awake()
    {
        // Enforce a single instance.
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

        // Prefer a spawner on this object, otherwise find one anywhere in the scene
        // (a runtime-bootstrapped manager has no spawner of its own).
        if (spawner == null)
        {
            spawner = GetComponent<ZombieSpawner>();
        }
        if (spawner == null)
        {
            spawner = FindFirstObjectByType<ZombieSpawner>();
        }
    }

    private void OnDestroy()
    {
        UnregisterRoundSync();
        if (Instance == this)
        {
            Instance = null;
        }
        if (_runtimeInstance == this)
        {
            _runtimeInstance = null;
        }
    }

    private void Start()
    {
        CurrentRound = Mathf.Max(1, startRound) - 1; // BeginNextRound() will increment to startRound
        IntermissionActive = false; // fresh run starts with no intermission banner
        stateTimer = startDelay;
        state = State.Starting;

        // Clients listen for authoritative round updates from the server.
        RegisterRoundSync();
    }

    /// <summary>
    /// True when a spawner is present AND able to actually produce zombies (has a prefab
    /// and at least one spawn point). When false we fall back to a timer so the round loop
    /// still advances during greyboxing before enemies / NavMesh are wired.
    /// </summary>
    private bool HasFunctionalSpawner =>
        spawner != null && spawner.zombiePrefab != null &&
        spawner.spawnPoints != null && spawner.spawnPoints.Length > 0;

    // --- Multiplayer authority -------------------------------------------
    // A live NGO session means the SERVER owns the round loop; clients receive the
    // round number over the network (see Step 2) and never advance it themselves.
    // With no active session (solo / single-player) the loop runs locally as before.
    private static bool NetworkSessionActive =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    private static bool IsServerRole =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    /// <summary>True in solo, or on the server in a networked session.</summary>
    public bool IsRoundAuthority => !NetworkSessionActive || IsServerRole;

    /// <summary>
    /// Apply a round number pushed from the server (clients only). Updates
    /// <see cref="CurrentRound"/> and fires <see cref="OnRoundChanged"/> so the HUD and
    /// other listeners react exactly as they do for a locally-advanced round. No-op on
    /// the authority or for stale/duplicate values.
    /// </summary>
    public void ApplyNetworkRound(int round)
    {
        if (IsRoundAuthority || round <= 0 || round == CurrentRound)
        {
            return;
        }

        CurrentRound = round;
        OnRoundChanged?.Invoke(CurrentRound);
    }

    // Named-message channel used to push the authoritative round number to clients.
    private const string RoundSyncMessage = "SOTD_ROUND";

    // Clients register to RECEIVE round updates; the server only sends.
    private void RegisterRoundSync()
    {
        if (!NetworkSessionActive || IsServerRole)
        {
            return;
        }

        CustomMessagingManager messaging = NetworkManager.Singleton.CustomMessagingManager;
        if (messaging == null)
        {
            return;
        }

        messaging.UnregisterNamedMessageHandler(RoundSyncMessage);
        messaging.RegisterNamedMessageHandler(RoundSyncMessage, OnRoundSyncMessage);
    }

    private void UnregisterRoundSync()
    {
        CustomMessagingManager messaging = NetworkManager.Singleton != null
            ? NetworkManager.Singleton.CustomMessagingManager
            : null;
        messaging?.UnregisterNamedMessageHandler(RoundSyncMessage);
    }

    private void OnRoundSyncMessage(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int round);
        ApplyNetworkRound(round);
    }

    // Server: push the current round to every client (called on each advance).
    private void BroadcastRound()
    {
        if (!NetworkSessionActive || !IsServerRole)
        {
            return;
        }

        CustomMessagingManager messaging = NetworkManager.Singleton.CustomMessagingManager;
        if (messaging == null)
        {
            return;
        }

        using FastBufferWriter writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(CurrentRound);
        messaging.SendNamedMessageToAll(RoundSyncMessage, writer, NetworkDelivery.ReliableSequenced);
    }

    /// <summary>
    /// Server: push the current round to a single just-loaded client so late arrivals
    /// catch up immediately. Called by MultiplayerSessionController per client on scene load.
    /// </summary>
    public void SendRoundToClient(ulong clientId)
    {
        if (!NetworkSessionActive || !IsServerRole || CurrentRound <= 0)
        {
            return;
        }

        CustomMessagingManager messaging = NetworkManager.Singleton.CustomMessagingManager;
        if (messaging == null)
        {
            return;
        }

        using FastBufferWriter writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(CurrentRound);
        messaging.SendNamedMessage(RoundSyncMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
    }

    private float nextRoundRebroadcast;

    private void Update()
    {
        // Clients in a networked session do not run the authoritative round loop; their
        // round number is driven by ApplyNetworkRound() from server messages.
        if (!IsRoundAuthority)
        {
            return;
        }

        // Periodically re-broadcast the current round so any client that missed the
        // one-shot catch-up (a registration-vs-message race on scene load) self-heals
        // within ~1s instead of showing a stale/zero round.
        if (NetworkSessionActive && IsServerRole && Time.unscaledTime >= nextRoundRebroadcast)
        {
            nextRoundRebroadcast = Time.unscaledTime + 1f;
            BroadcastRound();
        }

        switch (state)
        {
            case State.Starting:
                stateTimer -= Time.deltaTime;
                if (stateTimer <= 0f)
                {
                    BeginNextRound();
                }
                break;

            case State.InProgress:
                if (HasFunctionalSpawner)
                {
                    // Round is cleared when the spawner has nothing left to spawn or alive.
                    if (!spawner.RoundActive &&
                        spawner.RemainingToSpawn <= 0 && spawner.AliveCount == 0)
                    {
                        EnterIntermission();
                    }
                }
                else
                {
                    // TODO: no functional ZombieSpawner yet (no prefab / spawn points /
                    // NavMesh). Advance on a fallback timer so CurrentRound keeps
                    // progressing. Remove this branch once enemies are wired up.
                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0f)
                    {
                        EnterIntermission();
                    }
                }
                break;

            case State.Intermission:
                stateTimer -= Time.deltaTime;
                IntermissionRemaining = Mathf.Max(0f, stateTimer);
                if (stateTimer <= 0f)
                {
                    BeginNextRound();
                }
                break;
        }
    }

    private void EnterIntermission()
    {
        stateTimer = timeBetweenRounds;
        IntermissionActive = true;
        UpcomingRound = CurrentRound + 1;
        IntermissionRemaining = stateTimer;
        state = State.Intermission;
    }

    private void BeginNextRound()
    {
        // Server-authoritative: only the server (or solo) advances the round.
        if (!IsRoundAuthority)
        {
            return;
        }

        IntermissionActive = false; // the round is starting now; hide the banner
        CurrentRound++;

        int count = Mathf.Min(maxZombiesPerRound, baseZombieCount + (CurrentRound - 1) * zombiesPerRound);
        int hp = CalculateZombieHealth(CurrentRound);
        float speed = Mathf.Min(maxZombieSpeed, baseZombieSpeed + (CurrentRound - 1) * speedPerRound);

        if (HasFunctionalSpawner)
        {
            // Per-round simultaneous-alive cap scales with the round (round 1 -> 6,
            // round 5 -> 14, round 10+ -> 24), applied before the round spawns.
            int aliveCap = Mathf.Clamp(4 + CurrentRound * 2, 6, 24);
            spawner.SetRoundAliveCap(aliveCap);
            spawner.BeginRound(count, hp, speed);
        }
        else
        {
            // TODO: enemies not wired up yet - run the round on the fallback timer so the
            // counter still advances. Replace with real spawning once the spawner has a
            // prefab + spawn points and the NavMesh is baked.
            stateTimer = Mathf.Max(0.5f, noEnemyRoundDuration);
            Debug.LogWarning("RoundManager: no functional ZombieSpawner; running round " +
                CurrentRound + " on the fallback timer (" + stateTimer.ToString("0.0") + "s).");
        }

        state = State.InProgress;
        Debug.Log("Round " + CurrentRound + " started (" + count + " zombies, " + hp + " hp, " + speed.ToString("0.0") + " speed)");
        OnRoundChanged?.Invoke(CurrentRound);

        // Server: replicate the new round number to all connected clients.
        BroadcastRound();
    }

    /// <summary>
    /// Zombie health for the given round. Hand-tuned so the early rounds stay playable and
    /// fun: the old curve (150 + 50*(round-1)) made round 4-5 zombies ~300-350 HP, which felt
    /// far too tanky. New targets:
    ///   R1 100, R2 125, R3 150, R4 190, R5 230, R6 270, R7 310, R8 350, R9 390,
    ///   round 10+ : the round-9 value ramped x1.1 per round (keeps late rounds hard).
    /// This is computed in-code (not from the serialized baseZombieHealth / healthPerRound
    /// fields) so the tuned curve applies even where a scene-placed RoundManager still has the
    /// old high values serialized. Rounded and clamped so it can't overflow int.
    /// </summary>
    private int CalculateZombieHealth(int round)
    {
        round = Mathf.Max(1, round);

        if (round <= 9)
        {
            // +25 per round, with a small extra bump from round 4 onward.
            return 100 + (round - 1) * 25 + Mathf.Max(0, round - 3) * 15;
        }

        const int round9Health = 100 + 8 * 25 + 6 * 15; // 390
        double health = round9Health * System.Math.Pow(1.1, round - 9);
        double rounded = System.Math.Round(System.Math.Min(health, int.MaxValue));
        return Mathf.Max(1, (int)rounded);
    }

    // The round number is now drawn centrally by GameHud (via CurrentRound / OnRoundChanged),
    // so RoundManager no longer draws its own OnGUI label.
}
