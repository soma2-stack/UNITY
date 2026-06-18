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
    [Tooltip("Base zombie health in round 1.")]
    public int baseZombieHealth = 150;
    [Tooltip("Health added to each zombie per round.")]
    public int healthPerRound = 100;
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
                    BeginNextRound();
                }
                break;
        }
    }

    private void BeginNextRound()
    {
        CurrentRound++;

        int count = baseZombieCount + (CurrentRound - 1) * zombiesPerRound;
        int hp = baseZombieHealth + (CurrentRound - 1) * healthPerRound;
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

    private void OnGUI()
    {
        if (!showHud)
        {
            return;
        }

        GUI.Label(new Rect(Screen.width - 110, 10, 100, 24), "Round: " + Mathf.Max(0, CurrentRound));
    }
}
