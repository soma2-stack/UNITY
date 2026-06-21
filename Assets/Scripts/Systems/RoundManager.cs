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
    [Tooltip("Round 1 base health (default 150). Per-round scaling is handled by an " +
             "exponential formula (x1.1 per round through round 9, then x1.5 per round); " +
             "zombiesPerRound affects only the COUNT, not health.")]
    public int baseZombieHealth = 150;
    [Tooltip("Base zombie speed in round 1.")]
    public float baseZombieSpeed = 3.0f;
    [Tooltip("Speed added per round, up to maxZombieSpeed.")]
    public float speedPerRound = 0.15f;
    [Tooltip("Hard cap on zombie speed so late rounds stay fair.")]
    public float maxZombieSpeed = 6.0f;

    [Header("UI")]
    [Tooltip("Draw a lightweight OnGUI 'Round: N' label in the top-right.")]
    public bool showHud = true;

    /// <summary>Raised whenever the round number changes (including the first round).</summary>
    public event System.Action<int> OnRoundChanged;

    /// <summary>The current round number.</summary>
    public int CurrentRound { get; private set; }

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
                    state = State.Intermission;
                }
                break;

            case State.Intermission:
                stateTimer -= Time.deltaTime;
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
        CurrentRound++;

        int count = baseZombieCount + (CurrentRound - 1) * zombiesPerRound;
        int hp = CalculateZombieHealth(CurrentRound);
        float speed = Mathf.Min(maxZombieSpeed, baseZombieSpeed + (CurrentRound - 1) * speedPerRound);

        if (spawner != null)
        {
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
    /// CoD-authentic exponential zombie health for the given round:
    ///   rounds 1-9: baseZombieHealth * 1.1^(round-1)
    ///   rounds 10+: baseZombieHealth * 1.1^9 * 1.5^(round-9)  (steeper post-9 ramp)
    /// Rounded to the nearest integer and clamped to a sane range.
    /// </summary>
    private int CalculateZombieHealth(int round)
    {
        round = Mathf.Max(1, round);

        double health = round <= 9
            ? baseZombieHealth * System.Math.Pow(1.1, round - 1)
            : baseZombieHealth * System.Math.Pow(1.1, 9) * System.Math.Pow(1.5, round - 9);

        // Round to nearest int; clamp so extreme late rounds can't overflow int.
        double rounded = System.Math.Round(System.Math.Min(health, int.MaxValue));
        return Mathf.Max(1, (int)rounded);
    }

    // The round number is now drawn centrally by GameHud (via CurrentRound / OnRoundChanged),
    // so RoundManager no longer draws its own OnGUI label.
}
