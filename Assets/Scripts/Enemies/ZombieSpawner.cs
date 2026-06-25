using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Spawns zombies for the current round at random spawn points, never exceeding
/// <see cref="maxAlive"/> at once, until the round's quota has been spawned.
/// Tracks living zombies by subscribing to <see cref="ZombieAgent.OnDeath"/>.
///
/// The <see cref="RoundManager"/> drives this via <see cref="BeginRound"/> and
/// reads <see cref="AliveCount"/> / <see cref="RemainingToSpawn"/> to know when
/// a round is finished.
/// </summary>
public class ZombieSpawner : MonoBehaviour
{
    [Header("Prefab & Spawn Points")]
    [Tooltip("Zombie prefab. Must have a ZombieAgent (and a NavMeshAgent) on it.")]
    public GameObject zombiePrefab;
    [Tooltip("Empty GameObjects marking where zombies appear. Should be on/near the NavMesh.")]
    public Transform[] spawnPoints;

    [Header("Tuning")]
    [Tooltip("Maximum zombies alive at once. Set per-round by RoundManager via " +
             "SetRoundAliveCap() (scales with the round); this default applies only " +
             "until the first round begins.")]
    public int maxAlive = 12;
    [Tooltip("Seconds between individual spawns.")]
    public float spawnInterval = 2f;
    [Tooltip("Lower bound for the (round-scaled) spawn interval.")]
    public float spawnIntervalMin = 0.4f;

    [Header("Reachability")]
    [Tooltip("Only spawn at points the player can currently reach by NavMesh path " +
             "(expands as doors are bought). Prevents spawning behind locked doors or " +
             "in walled-off/unreachable areas.")]
    public bool requireReachableFromPlayer = true;
    [Tooltip("How far (world units) a spawn point or the player may be from the NavMesh " +
             "and still be sampled onto it for the reachability test.")]
    public float navSampleRadius = 4f;
    [Tooltip("Seconds between refreshes of the cached player reference.")]
    public float playerRecheckInterval = 1f;

    // Currently living zombies spawned by this spawner.
    private readonly List<ZombieAgent> aliveZombies = new List<ZombieAgent>();

    // Scratch buffers reused every spawn attempt (no per-frame allocations).
    private readonly List<int> reachableCandidates = new List<int>();
    private NavMeshPath pathScratch;

    private Transform player;            // cached player (CharacterController) transform
    private float nextPlayerRecheckTime;

    private int remainingToSpawn;       // how many still need to be spawned this round
    private int roundZombieHealth;      // health applied to each spawned zombie
    private float roundZombieSpeed;     // speed applied to each spawned zombie
    private float nextSpawnTime;
    private bool roundActive;
    private float baseSpawnInterval;

    private void Awake()
    {
        baseSpawnInterval = spawnInterval;
    }

    /// <summary>Number of zombies currently alive.</summary>
    public int AliveCount => aliveZombies.Count;

    /// <summary>Number of zombies still queued to be spawned this round.</summary>
    public int RemainingToSpawn => remainingToSpawn;

    /// <summary>True while a round is still spawning or has zombies alive.</summary>
    public bool RoundActive => roundActive;

    private void Update()
    {
        // Server-authoritative spawning: clients never spawn zombies locally
        // (the replicated NetworkObjects arrive from the server instead).
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening &&
            !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        if (!roundActive)
        {
            return;
        }

        if (remainingToSpawn <= 0)
        {
            return;
        }

        if (aliveZombies.Count >= maxAlive)
        {
            return;
        }

        if (Time.time < nextSpawnTime)
        {
            return;
        }

        nextSpawnTime = Time.time + spawnInterval;
        SpawnOne();
    }

    /// <summary>
    /// Sets the simultaneous-alive cap for the upcoming round. Called by RoundManager
    /// each round before <see cref="BeginRound"/> so the cap scales with the round.
    /// </summary>
    public void SetRoundAliveCap(int cap)
    {
        maxAlive = Mathf.Max(1, cap);
    }

    /// <summary>
    /// Start a new round. Resets the spawn quota and the per-zombie stats.
    /// </summary>
    public void BeginRound(int totalToSpawn, int zombieHealth, float zombieSpeed)
    {
        // Drop any stale references left over from the previous round. Without this,
        // a lingering/destroyed entry could keep AliveCount above zero and either end
        // the new round prematurely (miscount) or leave it never-ending. Unsubscribe
        // each survivor first so we don't double-handle its later death event.
        foreach (ZombieAgent z in aliveZombies)
        {
            if (z != null)
            {
                z.OnDeath -= HandleZombieDeath;
            }
        }
        aliveZombies.Clear();

        remainingToSpawn = Mathf.Max(0, totalToSpawn);
        roundZombieHealth = Mathf.Max(1, zombieHealth);
        roundZombieSpeed = Mathf.Max(0.1f, zombieSpeed);
        roundActive = true;
        spawnInterval = Mathf.Max(spawnIntervalMin,
            baseSpawnInterval / (1f + totalToSpawn * 0.05f));
        // Spawn the first zombie almost immediately.
        nextSpawnTime = Time.time;
    }

    private void SpawnOne()
    {
        if (zombiePrefab == null)
        {
            Debug.LogWarning("ZombieSpawner: zombiePrefab is not assigned.");
            return;
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning("ZombieSpawner: no spawnPoints assigned.");
            return;
        }

        Transform point = PickSpawnPoint();
        if (point == null)
        {
            // No reachable spawn point right now (e.g. the player is still walled off
            // behind closed doors). Don't consume the spawn or the timer cooldown -
            // retry promptly next tick once a door is opened or the player moves.
            nextSpawnTime = Time.time;
            return;
        }

        GameObject obj = Instantiate(zombiePrefab, point.position, point.rotation);
        ZombieAgent zombie = obj.GetComponent<ZombieAgent>();
        if (zombie == null)
        {
            Debug.LogWarning("ZombieSpawner: spawned prefab has no ZombieAgent component.");
            Destroy(obj);
            return;
        }

        zombie.Configure(roundZombieHealth, roundZombieSpeed);
        zombie.OnDeath += HandleZombieDeath;
        aliveZombies.Add(zombie);

        // Networked session: replicate the zombie to all clients (only the server
        // reaches here). The prefab must have a NetworkObject + be a registered
        // network prefab. No-op in solo.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkObject netObj = obj.GetComponent<NetworkObject>();
            if (netObj != null && !netObj.IsSpawned)
            {
                netObj.Spawn(true);
            }
        }

        remainingToSpawn--;
    }

    /// <summary>
    /// Chooses a spawn point. When <see cref="requireReachableFromPlayer"/> is on,
    /// only points with a COMPLETE NavMesh path to the player are eligible, so
    /// zombies never appear behind a still-closed door or in a walled-off area.
    /// Returns null when nothing is currently reachable (the caller retries later).
    /// When the check is off (or no player/NavMesh is available), falls back to a
    /// plain random point so spawning never silently stops.
    /// </summary>
    private Transform PickSpawnPoint()
    {
        if (!requireReachableFromPlayer)
        {
            return RandomValidPoint();
        }

        EnsurePlayer();
        if (player == null)
        {
            // No player to path to yet - fall back so the round still gets going.
            return RandomValidPoint();
        }

        // Sample the player onto the NavMesh once for all candidate tests.
        if (!NavMesh.SamplePosition(player.position, out NavMeshHit playerHit, navSampleRadius, NavMesh.AllAreas))
        {
            return RandomValidPoint();
        }

        reachableCandidates.Clear();
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            if (spawnPoints[i] != null && IsReachable(spawnPoints[i].position, playerHit.position))
            {
                reachableCandidates.Add(i);
            }
        }

        if (reachableCandidates.Count == 0)
        {
            return null; // nothing reachable this tick
        }

        int pick = reachableCandidates[Random.Range(0, reachableCandidates.Count)];
        return spawnPoints[pick];
    }

    /// <summary>
    /// True if there is a COMPLETE NavMesh path from a spawn position to the player
    /// position (both already on / sampled onto the NavMesh). A partial or invalid
    /// path means the spawn point is currently cut off (closed door, etc.).
    /// </summary>
    private bool IsReachable(Vector3 spawnPos, Vector3 playerOnNavMesh)
    {
        if (!NavMesh.SamplePosition(spawnPos, out NavMeshHit spawnHit, navSampleRadius, NavMesh.AllAreas))
        {
            return false;
        }

        pathScratch ??= new NavMeshPath();
        if (!NavMesh.CalculatePath(spawnHit.position, playerOnNavMesh, NavMesh.AllAreas, pathScratch))
        {
            return false;
        }

        return pathScratch.status == NavMeshPathStatus.PathComplete;
    }

    private Transform RandomValidPoint()
    {
        // A couple of quick tries to skip any null entries in the array.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            Transform t = spawnPoints[Random.Range(0, spawnPoints.Length)];
            if (t != null)
            {
                return t;
            }
        }
        return null;
    }

    /// <summary>
    /// Caches the player transform (found via its CharacterController, the project
    /// convention) and refreshes it periodically so a respawned/late player is picked
    /// up without searching every frame.
    /// </summary>
    private void EnsurePlayer()
    {
        if (player != null && Time.time < nextPlayerRecheckTime)
        {
            return;
        }

        nextPlayerRecheckTime = Time.time + Mathf.Max(0.1f, playerRecheckInterval);

        CharacterController controller = FindFirstObjectByType<CharacterController>();
        if (controller != null)
        {
            player = controller.transform;
        }
    }

    private void HandleZombieDeath(ZombieAgent zombie)
    {
        if (zombie != null)
        {
            zombie.OnDeath -= HandleZombieDeath;
        }
        aliveZombies.Remove(zombie);

        // Round is fully cleared once nothing is left to spawn and nothing alive.
        if (roundActive && remainingToSpawn <= 0 && aliveZombies.Count == 0)
        {
            roundActive = false;
        }
    }
}
