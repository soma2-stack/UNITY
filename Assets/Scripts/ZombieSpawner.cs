using System.Collections.Generic;
using UnityEngine;

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
    [Tooltip("Maximum number of zombies alive at the same time.")]
    public int maxAlive = 12;
    [Tooltip("Seconds between individual spawns.")]
    public float spawnInterval = 2f;

    // Currently living zombies spawned by this spawner.
    private readonly List<ZombieAgent> aliveZombies = new List<ZombieAgent>();

    private int remainingToSpawn;       // how many still need to be spawned this round
    private int roundZombieHealth;      // health applied to each spawned zombie
    private float roundZombieSpeed;     // speed applied to each spawned zombie
    private float nextSpawnTime;
    private bool roundActive;

    /// <summary>Number of zombies currently alive.</summary>
    public int AliveCount => aliveZombies.Count;

    /// <summary>Number of zombies still queued to be spawned this round.</summary>
    public int RemainingToSpawn => remainingToSpawn;

    /// <summary>True while a round is still spawning or has zombies alive.</summary>
    public bool RoundActive => roundActive;

    private void Update()
    {
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
    /// Start a new round. Resets the spawn quota and the per-zombie stats.
    /// </summary>
    public void BeginRound(int totalToSpawn, int zombieHealth, float zombieSpeed)
    {
        remainingToSpawn = Mathf.Max(0, totalToSpawn);
        roundZombieHealth = Mathf.Max(1, zombieHealth);
        roundZombieSpeed = Mathf.Max(0.1f, zombieSpeed);
        roundActive = true;
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

        Transform point = spawnPoints[Random.Range(0, spawnPoints.Length)];
        if (point == null)
        {
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

        remainingToSpawn--;
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
