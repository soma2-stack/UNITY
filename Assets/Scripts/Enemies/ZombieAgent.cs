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

    [Header("Animation")]
    [Tooltip("Animator float parameter set to the zombie's current move speed (drives idle<->move).")]
    public string speedParam = "Speed";
    [Tooltip("Animator trigger fired when the zombie attacks.")]
    public string attackParam = "Attack";
    [Tooltip("Animator trigger fired when the zombie dies (plays the death animation).")]
    public string dieParam = "Die";
    [Tooltip("Animator trigger fired when the zombie is shot but survives (flinch).")]
    public string hitParam = "Hit";
    [Tooltip("Minimum seconds between hit-react flinches (prevents stun-locking).")]
    public float hitReactCooldown = 1.2f;
    [Tooltip("Seconds to keep the body after death so the death animation can play before it is removed.")]
    public float deathDestroyDelay = 3f;

    /// <summary>Raised when this zombie dies. Passes itself so listeners can untrack it.</summary>
    public event System.Action<ZombieAgent> OnDeath;

    /// <summary>Raised (static) at the world position where ANY zombie dies. Used by power-up drops.</summary>
    public static event System.Action<Vector3> OnAnyZombieKilled;

    /// <summary>True if the zombie is dead.</summary>
    public bool IsDead => isDead;

    private NavMeshAgent agent;
    private Transform player;
    private PlayerHealth playerHealth;
    private Animator animator;
    private bool hasSpeedParam;
    private bool hasAttackParam;
    private bool hasDieParam;
    private bool hasHitParam;
    private float nextRepathTime;
    private float nextAttackTime;
    private float nextHitReactTime;
    private bool isDead;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();
        CacheAnimatorParams();
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

        // Drive the locomotion blend (idle <-> move) from the agent's actual speed.
        if (animator != null && hasSpeedParam)
        {
            animator.SetFloat(speedParam, agent != null ? agent.velocity.magnitude : 0f);
        }

        TryAttack();
    }

    private void CacheAnimatorParams()
    {
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            return;
        }

        foreach (AnimatorControllerParameter p in animator.parameters)
        {
            if (p.name == speedParam) hasSpeedParam = true;
            else if (p.name == attackParam) hasAttackParam = true;
            else if (p.name == dieParam) hasDieParam = true;
            else if (p.name == hitParam) hasHitParam = true;
        }
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

        // Line-of-sight: don't attack through walls/floors/ceilings/closed doors.
        // Ray from chest height toward the player; if a non-player collider is in the
        // way, the player isn't actually reachable for a melee hit.
        Vector3 origin = transform.position + Vector3.up * 1f;
        Vector3 target = player.position + Vector3.up * 1f;
        Vector3 to = target - origin;
        float dist = to.magnitude;
        if (dist > 0.01f)
        {
            // Start just past our own body so we don't hit ourselves.
            Vector3 dir = to / dist;
            Vector3 rayStart = origin + dir * 0.5f;
            if (Physics.Raycast(rayStart, dir, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore))
            {
                // Blocked unless the first thing we hit is the player.
                if (hit.collider.GetComponentInParent<CharacterController>() == null)
                {
                    return;
                }
            }
        }

        nextAttackTime = Time.time + attackInterval;
        if (animator != null && hasAttackParam)
        {
            animator.SetTrigger(attackParam);
        }
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
    public void TakeDamage(int amount, bool isHeadshot = false)
    {
        if (isDead || amount <= 0)
        {
            return;
        }

        health -= amount;
        if (health <= 0)
        {
            Die(isHeadshot);
            return;
        }

        // Survived the hit: play a brief flinch (rate-limited so it can't be stun-locked).
        if (animator != null && hasHitParam && Time.time >= nextHitReactTime)
        {
            animator.SetTrigger(hitParam);
            nextHitReactTime = Time.time + hitReactCooldown;
        }
    }

    /// <summary>
    /// Kill this zombie via melee/knife. Awards bonus points.
    /// </summary>
    public void KillByMelee()
    {
        if (isDead)
        {
            return;
        }
        health = 0;
        Die(false, true);
    }

    private void Die(bool isHeadshot = false, bool isMelee = false)
    {
        if (isDead)
        {
            return;
        }

        isDead = true;

        // Award points based on kill type
        int reward = killReward; // Normal kill = 60
        if (isMelee)
        {
            reward = 130; // Melee/knife kill = 130
        }
        else if (isHeadshot)
        {
            reward = 100; // Headshot kill = 100
        }
        PlayerPoints.Instance?.AddPoints(reward);
        OnDeath?.Invoke(this);
        OnAnyZombieKilled?.Invoke(transform.position); // power-up drops, kill feeds, etc.

        // Play the death animation.
        if (animator != null && hasDieParam)
        {
            animator.SetTrigger(dieParam);
        }

        // Stop chasing/attacking and stop blocking the player, but keep the body for a
        // moment so the death animation can play before the object is removed.
        if (agent != null)
        {
            if (agent.isOnNavMesh)
            {
                agent.isStopped = true;
            }
            agent.enabled = false;
        }
        foreach (Collider c in GetComponentsInChildren<Collider>())
        {
            c.enabled = false;
        }

        Destroy(gameObject, Mathf.Max(0f, deathDestroyDelay));
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
