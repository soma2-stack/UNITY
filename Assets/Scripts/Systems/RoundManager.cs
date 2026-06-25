// ✅ CHECKPOINT 3 — RoundManager (existing file enhanced, not duplicated):
// self-bootstraps in SchoolOfTheDead, exposes ZombiesRemainingThisRound, and
// guards a missing/empty ZombieSpawner so CurrentRound advances even before any
// enemies / NavMesh are wired up. Power-ups now drop randomly on kills
// (PowerupManager), so the old round-end milestone drop was removed.
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
    [Tooltip("Round 1 base health (default 150). Rounds 1-9 add healthPerRound each; " +
             "round 10+ ramps multiplicatively so late rounds stay hard.")]
    public int baseZombieHealth = 150;
    [Tooltip("Health added per round during rounds 1-9 (R1=150, R2=200, ...).")]
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
    }

    /// <summary>
    /// True when a spawner is present AND able to actually produce zombies (has a prefab
    /// and at least one spawn point). When false we fall back to a timer so the round loop
    /// still advances during greyboxing before enemies / NavMesh are wired.
    /// </summary>
    private bool HasFunctionalSpawner =>
        spawner != null && spawner.zombiePrefab != null &&
        spawner.spawnPoints != null && spawner.spawnPoints.Length > 0;

    private void Update()
    {
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
    }

    /// <summary>
    /// Zombie health for the given round:
    ///   rounds 1-9 : baseZombieHealth + (round-1) * healthPerRound   (R1=150, R2=200, ...)
    ///   rounds 10+ : the round-9 value ramped x1.1 per round          (keeps late rounds hard)
    /// Rounded to the nearest integer and clamped so it can't overflow int.
    /// </summary>
    private int CalculateZombieHealth(int round)
    {
        round = Mathf.Max(1, round);

        if (round <= 9)
        {
            return Mathf.Max(1, baseZombieHealth + (round - 1) * healthPerRound);
        }

        int round9Health = baseZombieHealth + 8 * healthPerRound;
        double health = round9Health * System.Math.Pow(1.1, round - 9);
        double rounded = System.Math.Round(System.Math.Min(health, int.MaxValue));
        return Mathf.Max(1, (int)rounded);
    }

    // The round number is now drawn centrally by GameHud (via CurrentRound / OnRoundChanged),
    // so RoundManager no longer draws its own OnGUI label.
}
