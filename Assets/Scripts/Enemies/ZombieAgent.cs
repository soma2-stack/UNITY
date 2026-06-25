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
    [Tooltip("Total points for a body gun-kill, including the killing hit's +10 (e.g. 60 = +10 hit + +50 kill bonus).")]
    public int killReward = 60;
    [Tooltip("Award +10 points per bullet hit (CoD economy). Disable for zombie types that shouldn't pay out per hit.")]
    public bool awardHitPoints = true;

    // Points awarded per bullet hit (CoD: +10). The kill bonus is computed so the
    // killing shot's hit + bonus total the intended body/headshot reward.
    private const int HitPoints = 10;

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

    /// <summary>True if the agent currently exists and is on the baked NavMesh.</summary>
    public bool IsOnNavMesh => agent != null && agent.isOnNavMesh;

    /// <summary>Total zombies killed this run (reset on a fresh run via ResetKillCount).</summary>
    public static int TotalKillsThisRun { get; private set; }

    /// <summary>Reset the run kill counter so a fresh run starts at zero.</summary>
    public static void ResetKillCount()
    {
        TotalKillsThisRun = 0;
    }

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
    private float nextRecoveryTime; // throttles off-mesh recovery attempts
    private bool isDead;
    private bool pointsAwarded; // guard: the kill reward may be granted at most once

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

        // FIX C: if the agent has fallen off the NavMesh, sample a nearby point and
        // warp back onto the surface. Throttled so it can't run every frame.
        if (agent != null && !isDead && !agent.isOnNavMesh && Time.time >= nextRecoveryTime)
        {
            nextRecoveryTime = Time.time + 0.5f;
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit recoverHit, 2f, NavMesh.AllAreas))
            {
                agent.Warp(recoverHit.position);
            }
        }

        // Throttled path recompute toward the player.
        if (Time.time >= nextRepathTime)
        {
            nextRepathTime = Time.time + repathInterval;
            if (agent != null && agent.isOnNavMesh)
            {
                agent.SetDestination(player.position);

                // FIX B: a partial/invalid path means the target is currently
                // unreachable (e.g. a door state just changed) - drop the stale path
                // and retry quickly instead of freezing on it.
                if (agent.pathStatus == NavMeshPathStatus.PathPartial ||
                    agent.pathStatus == NavMeshPathStatus.PathInvalid)
                {
                    agent.ResetPath();
                    nextRepathTime = Time.time + 0.1f;
                }
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
            Vector3 rayStart = origin + dir * 0.15f;
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

        // Authentic CoD: hitting a DOWNED player only chips 1 damage (death comes from
        // the bleed-out timer, so hits slow the drain rather than accelerating a kill),
        // and no points are involved in a zombie striking the player.
        int dealt = playerHealth.IsDowned ? 1 : attackDamage;
        playerHealth.TakeDamage(dealt);
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
    /// Immediately recompute the path to the player. Called when a door opens so
    /// zombies don't sit on a now-stale, blocked route. Also clears the repath timer
    /// so the next regular repath fires right away.
    /// </summary>
    public void ForceRepath()
    {
        nextRepathTime = 0f;
        if (player != null && agent != null && agent.isOnNavMesh)
        {
            agent.ResetPath();
            agent.SetDestination(player.position);
        }
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

        // +10 per bullet hit (CoD economy), awarded for EVERY hit regardless of
        // whether it kills. Melee (KillByMelee) bypasses TakeDamage, so it never
        // receives this hit award - only its full kill reward in Die().
        if (awardHitPoints)
        {
            PlayerPoints.Instance?.AddPoints(HitPoints);
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
        TotalKillsThisRun++; // count this kill toward the run total (shown on game over)

        // ---------------------------------------------------------------------
        // POINTS OWNERSHIP: ZombieAgent owns the whole point economy. TakeDamage()
        // awards +10 per bullet hit; Die() awards the KILL BONUS below so the
        // killing bullet totals +60 body / +100 headshot (its +10 hit + the bonus),
        // never +70. A melee kill bypasses TakeDamage, so it awards the full +130
        // here. The pointsAwarded guard ensures the bonus is granted at most once.
        // ---------------------------------------------------------------------
        if (!pointsAwarded)
        {
            pointsAwarded = true;

            int reward;
            if (isMelee)
            {
                reward = 130; // melee kill: full reward, no separate hit award
            }
            else if (isHeadshot)
            {
                reward = Mathf.Max(0, 100 - HitPoints); // +90 -> 100 total headshot kill
            }
            else
            {
                reward = Mathf.Max(0, killReward - HitPoints); // +50 -> 60 total body kill
            }
            PlayerPoints.Instance?.AddPoints(reward);
        }

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
