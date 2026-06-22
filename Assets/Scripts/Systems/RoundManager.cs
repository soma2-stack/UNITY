using UnityEngine;

/// <summary>
/// Drives the wave/round loop (Call of Duty Zombies style).
///
/// Each round it asks the <see cref="ZombieSpawner"/> to spawn a number of
/// zombies whose count, health and speed scale up with the round number. Once
/// every zombie of the round is dead it waits <see cref="timeBetweenRounds"/>
/// seconds, then advances to the next round.
/// </summary>
[RequireComponent(typeof(ZombieSpawner))]
public class RoundManager : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Spawner that produces this round's zombies. Auto-found if left empty.")]
    public ZombieSpawner spawner;

    [Tooltip("World point where round-end milestone power-ups (Max Ammo / Carpenter) drop. " +
             "If left unassigned, milestone drops are skipped with a warning.")]
    public Transform powerupDropPoint;

    [Header("Round Pacing")]
    [Tooltip("Round to start on (usually 1).")]
    public int startRound = 1;
    [Tooltip("Delay (seconds) after a round is cleared before the next begins.")]
    public float timeBetweenRounds = 5f;
    [Tooltip("Short delay before the very first round starts (lets the scene settle / NavMesh load).")]
    public float startDelay = 2f;

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

    private void Awake()
    {
        if (spawner == null)
        {
            spawner = GetComponent<ZombieSpawner>();
        }
    }

    private void Start()
    {
        CurrentRound = Mathf.Max(1, startRound) - 1; // BeginNextRound() will increment to startRound
        IntermissionActive = false; // fresh run starts with no intermission banner
        stateTimer = startDelay;
        state = State.Starting;
    }

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
                // Round is cleared when the spawner has nothing left to spawn or alive.
                if (spawner != null && !spawner.RoundActive &&
                    spawner.RemainingToSpawn <= 0 && spawner.AliveCount == 0)
                {
                    stateTimer = timeBetweenRounds;
                    IntermissionActive = true;
                    UpcomingRound = CurrentRound + 1;
                    IntermissionRemaining = stateTimer;
                    state = State.Intermission;
                }
                break;

            case State.Intermission:
                stateTimer -= Time.deltaTime;
                IntermissionRemaining = Mathf.Max(0f, stateTimer);
                if (stateTimer <= 0f)
                {
                    // Drop a milestone power-up for the round about to begin, before it starts.
                    TrySpawnMilestonePowerup(CurrentRound + 1);
                    BeginNextRound();
                }
                break;
        }
    }

    /// <summary>
    /// Drops a milestone power-up at <see cref="powerupDropPoint"/> for the incoming round:
    /// Max Ammo every 4th round, Carpenter every 3rd round. When a round satisfies both
    /// (e.g. round 12) Max Ammo is preferred. Skips with a warning if no drop point or no
    /// PowerupManager is available - never throws.
    /// </summary>
    private void TrySpawnMilestonePowerup(int incomingRound)
    {
        PowerupType type;
        if (incomingRound % 4 == 0)
        {
            type = PowerupType.MaxAmmo;   // prefer Max Ammo when both conditions match
        }
        else if (incomingRound % 3 == 0)
        {
            type = PowerupType.Carpenter;
        }
        else
        {
            return; // not a milestone round
        }

        if (powerupDropPoint == null)
        {
            Debug.LogWarning("RoundManager: powerupDropPoint is not assigned; skipping round-end power-up drop.");
            return;
        }

        if (PowerupManager.Instance == null)
        {
            Debug.LogWarning("RoundManager: no PowerupManager in the scene; skipping round-end power-up drop.");
            return;
        }

        PowerupManager.Instance.SpawnPowerupAt(type, powerupDropPoint.position);
        Debug.Log("RoundManager: dropped " + PowerupManager.DisplayName(type) + " for round " + incomingRound);
    }

    private void BeginNextRound()
    {
        IntermissionActive = false; // the round is starting now; hide the banner
        CurrentRound++;

        int count = Mathf.Min(maxZombiesPerRound, baseZombieCount + (CurrentRound - 1) * zombiesPerRound);
        int hp = CalculateZombieHealth(CurrentRound);
        float speed = Mathf.Min(maxZombieSpeed, baseZombieSpeed + (CurrentRound - 1) * speedPerRound);

        if (spawner != null)
        {
            // Per-round simultaneous-alive cap scales with the round (round 1 -> 6,
            // round 5 -> 14, round 10+ -> 24), applied before the round spawns.
            int aliveCap = Mathf.Clamp(4 + CurrentRound * 2, 6, 24);
            spawner.SetRoundAliveCap(aliveCap);
            spawner.BeginRound(count, hp, speed);
        }
        else
        {
            Debug.LogWarning("RoundManager: no ZombieSpawner assigned.");
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
