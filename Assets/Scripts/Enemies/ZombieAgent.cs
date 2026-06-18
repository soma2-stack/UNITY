using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Drives a single zombie: chases the player using Unity's built-in NavMesh,
/// attacks the player on a cooldown when close enough, takes damage and dies.
///
/// Requires a baked NavMesh in the scene (the user bakes this in-editor) and a
/// <see cref="NavMeshAgent"/> on the same GameObject.
///
/// On death this awards points to the player (via the optional PlayerPoints
/// singleton) and raises <see cref="OnDeath"/> so the spawner can keep an
/// accurate alive-count, then destroys itself.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class ZombieAgent : MonoBehaviour
{
    [Header("Stats")]
    [Tooltip("Starting health. Can be overridden per round via Configure().")]
    public int health = 150;
    [Tooltip("NavMeshAgent move speed. Can be overridden per round via Configure().")]
    public float moveSpeed = 3.5f;

    [Header("Attack")]
    [Tooltip("Damage dealt to the player per attack.")]
    public int attackDamage = 15;
    [Tooltip("How close (world units) the zombie must be to attack the player.")]
    public float attackRange = 2f;
    [Tooltip("Seconds between attacks.")]
    public float attackInterval = 1.2f;

    [Header("Rewards")]
    [Tooltip("Points awarded to the player when this zombie is killed.")]
    public int killReward = 60;

    [Header("Pathing")]
    [Tooltip("How often (seconds) to recompute the path to the player. Throttled for performance.")]
    public float repathInterval = 0.2f;

    /// <summary>Raised when this zombie dies. Passes itself so listeners can untrack it.</summary>
    public event System.Action<ZombieAgent> OnDeath;

    private NavMeshAgent agent;
    private Transform player;
    private PlayerHealth playerHealth;
    private float nextRepathTime;
    private float nextAttackTime;
    private bool isDead;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    private void Start()
    {
        ApplyAgentSpeed();
        AcquirePlayer();
    }

    private void Update()
    {
        if (isDead)
        {
            return;
        }

        if (player == null)
        {
            // Player may not exist yet (or was destroyed); keep trying occasionally.
            if (Time.time >= nextRepathTime)
            {
                nextRepathTime = Time.time + repathInterval;
                AcquirePlayer();
            }
            return;
        }

        // Throttled path recompute toward the player.
        if (Time.time >= nextRepathTime)
        {
            nextRepathTime = Time.time + repathInterval;
            if (agent != null && agent.isOnNavMesh)
            {
                agent.SetDestination(player.position);
            }
        }

        TryAttack();
    }

    private void TryAttack()
    {
        if (playerHealth == null || playerHealth.IsDead)
        {
            return;
        }

        float distance = Vector3.Distance(transform.position, player.position);
        if (distance > attackRange)
        {
            return;
        }

        if (Time.time < nextAttackTime)
        {
            return;
        }

        nextAttackTime = Time.time + attackInterval;
        playerHealth.TakeDamage(attackDamage);
    }

    /// <summary>
    /// Apply round-scaled stats. Call right after spawning, before/just after Start.
    /// </summary>
    public void Configure(int newHealth, float newSpeed)
    {
        health = Mathf.Max(1, newHealth);
        moveSpeed = Mathf.Max(0.1f, newSpeed);
        ApplyAgentSpeed();
    }

    /// <summary>
    /// Damage this zombie. When health reaches zero the zombie dies.
    /// </summary>
    public void TakeDamage(int amount)
    {
        if (isDead || amount <= 0)
        {
            return;
        }

        health -= amount;
        if (health <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        if (isDead)
        {
            return;
        }

        isDead = true;

        // Award points (PlayerPoints is created by another system; null-safe).
        PlayerPoints.Instance?.Add(killReward);

        // Let the spawner / round system know one zombie is gone.
        OnDeath?.Invoke(this);

        // Stop the agent so it does not keep moving while being torn down.
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = true;
        }

        Destroy(gameObject);
    }

    private void ApplyAgentSpeed()
    {
        if (agent != null)
        {
            agent.speed = moveSpeed;
        }
    }

    private void AcquirePlayer()
    {
        // Find the player via its CharacterController (project convention).
        CharacterController controller = FindFirstObjectByType<CharacterController>();
        if (controller == null)
        {
            return;
        }

        player = controller.transform;
        playerHealth = controller.GetComponent<PlayerHealth>();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
