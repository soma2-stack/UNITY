using System.Collections;
using Unity.Netcode;
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
    public int attackDamage = 50;
    [Tooltip("How close (world units) the zombie must be to attack the player.")]
    public float attackRange = 1.35f;
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
    [Tooltip("How often (seconds) to re-check all players and switch to the nearest reachable living target.")]
    public float targetRecheckInterval = 0.75f;

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
    private float nextTargetRecheckTime;
    private float nextAttackTime;
    private float nextHitReactTime;
    private float nextRecoveryTime; // throttles off-mesh recovery attempts
    private NavMeshPath targetPathScratch;
    private bool isDead;
    private bool pointsAwarded; // guard: the kill reward may be granted at most once
    private bool isClientReplica; // true on non-server peers: no AI, only a death collider watch
    private bool clientDeathHandled; // guard: disable the corpse's colliders at most once on a client

    [Header("Audio")]
    [Tooltip("Volume of a surviving-hit grunt (0-1). Kept low so it's audible but not in-your-ear.")]
    [Range(0f, 1f)] public float zombieHitVolume = 0.28f;
    [Tooltip("Volume of the death sound (0-1).")]
    [Range(0f, 1f)] public float zombieDeathVolume = 0.36f;
    [Tooltip("3D min distance: full volume within this radius, then it starts to fall off.")]
    public float zombieAudioMinDistance = 1.5f;
    [Tooltip("3D max distance: the sound reaches near-silence by here, so it stays in the world.")]
    public float zombieAudioMaxDistance = 20f;
    [Tooltip("3D distance rolloff. Logarithmic gives a natural, quickly-quieting falloff.")]
    public AudioRolloffMode zombieAudioRolloffMode = AudioRolloffMode.Logarithmic;
    [Tooltip("Minimum seconds between hit grunts on ONE zombie, so rapid fire can't stack them.")]
    public float zombieHitSoundCooldown = 0.3f;

    // --- Hit / death audio ---
    // Sounds are driven off the REPLICATED animator state (the same NetworkAnimator-synced
    // "Hit"/"Death" states ClientDeathWatch relies on), so every peer near the zombie plays
    // its own copy exactly once with no extra networking and no double-play across machines.
    private AudioSource hitAudio;          // lazily-created 3D source for surviving-hit grunts
    private int hitStateHash;              // Animator.StringToHash("Hit"), cached
    private int deathStateHash;            // Animator.StringToHash("Death"), cached
    private bool wasInHitState;            // rising-edge guard so a dwelt flinch fires once
    private bool deathSoundPlayed;         // guard: play the death sound at most once on this peer
    private float nextHitSoundTime;        // floor so re-entered flinches can't machine-gun

    // Shared, lazily-loaded clip pools (Resources/ZombieSounds/*), keyed by name like the guns.
    private static readonly string[] HitSoundKeys = { "Zombie_Hit_01", "Zombie_Hit_02", "Zombie_Hit_03" };
    private static readonly string[] DeathSoundKeys = { "Zombie_Death_01", "Zombie_Death_02", "Zombie_Death_03" };
    private static AudioClip[] _hitClips;
    private static AudioClip[] _deathClips;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();
        CacheAnimatorParams();
    }

    // --- Authority (multiplayer) ---
    private static bool NetworkActive =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    private static bool IsServerRole =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
    /// <summary>True in solo, or on the server in a networked session.</summary>
    private bool HasAuthority => !NetworkActive || IsServerRole;

    private void Start()
    {
        // In a networked session only the SERVER runs zombie AI. Clients let the
        // NetworkTransform drive the body and disable the agent so it doesn't fight.
        if (NetworkActive && !IsServerRole)
        {
            if (agent != null)
            {
                agent.enabled = false;
            }
            // Keep this component ENABLED (don't disable it) purely so it can watch for the
            // replicated death and drop the corpse's colliders locally — otherwise a dead
            // body keeps blocking client movement/bullets until the server despawns it.
            isClientReplica = true;
            return;
        }

        ApplyAgentSpeed();
        AcquirePlayer();
    }

    private void Update()
    {
        // Runs on EVERY peer (server + clients) while the body exists: plays hit/death
        // sounds off the replicated animator state so all nearby players hear them once.
        UpdateSoundWatch();

        // Client replicas run no AI — only a death watch that disables the corpse's colliders
        // once the server-driven death animation ("Death") has replicated here.
        if (isClientReplica)
        {
            ClientDeathWatch();
            return;
        }

        if (isDead)
        {
            return;
        }

        if (Time.time >= nextTargetRecheckTime)
        {
            nextTargetRecheckTime = Time.time + Mathf.Max(0.1f, targetRecheckInterval);
            AcquirePlayer();
        }

        if (!IsCurrentTargetValid())
        {
            // No target yet, or the current one died/downed/left/became unreachable:
            // re-acquire the nearest reachable living player.
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
                    ClearTarget();
                    nextRepathTime = Time.time + 0.1f;
                    nextTargetRecheckTime = 0f;
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
        // Cached once for the sound watch. shortNameHash equals StringToHash(stateName),
        // so these match the "Hit"/"Death" states on the base layer (same states the
        // NetworkAnimator replicates to clients).
        hitStateHash = Animator.StringToHash("Hit");
        deathStateHash = Animator.StringToHash("Death");

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
        if (!IsCurrentTargetValid())
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
                CharacterController hitController = hit.collider.GetComponentInParent<CharacterController>();
                if (hitController == null)
                {
                    return;
                }

                PlayerHealth hitHealth = hitController.GetComponentInParent<PlayerHealth>();
                if (hitHealth != playerHealth)
                {
                    // Another player/body is in the way. Do not damage the stale target.
                    nextTargetRecheckTime = 0f;
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
    public void TakeDamage(int amount, bool isHeadshot = false, ulong attackerClientId = PlayerPoints.EveryoneClientId)
    {
        // Server-authoritative: in a session, clients route damage to the server
        // (see WeaponController) so only the authority mutates health/awards points.
        if (!HasAuthority || isDead || amount <= 0)
        {
            return;
        }

        // +10 per bullet hit (CoD economy), awarded for EVERY hit regardless of
        // whether it kills, and credited to the SHOOTER (per-player economy). Melee
        // (KillByMelee) bypasses TakeDamage, so it never receives this hit award -
        // only its full kill reward in Die(). attackerClientId defaults to "everyone"
        // for un-attributed damage (e.g. the Nuke power-up); in solo the id is ignored.
        if (awardHitPoints)
        {
            PlayerPoints.Instance?.AddPoints(attackerClientId, HitPoints);
        }

        health -= amount;
        if (health <= 0)
        {
            Die(isHeadshot, false, attackerClientId);
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
    public void KillByMelee(ulong attackerClientId = PlayerPoints.EveryoneClientId)
    {
        if (isDead)
        {
            return;
        }
        health = 0;
        Die(false, true, attackerClientId);
    }

    /// <summary>
    /// Apply melee/knife DAMAGE (not an instant kill). Ignores dead zombies and subtracts the
    /// amount from health. When the hit drops health to 0 the zombie dies via Die(isMelee: true),
    /// awarding the full melee kill reward (130) to the attacker — exactly like KillByMelee, but
    /// ONLY on the killing blow. A non-killing hit awards no points (knife hits don't pay a
    /// per-hit bonus) and plays a brief flinch. Server-authoritative, mirroring TakeDamage.
    /// </summary>
    public void TakeMeleeDamage(int amount, ulong attackerClientId = PlayerPoints.EveryoneClientId)
    {
        if (!HasAuthority || isDead || amount <= 0)
        {
            return;
        }

        health -= amount;
        if (health <= 0)
        {
            Die(false, true, attackerClientId);
            return;
        }

        // Survived the hit: brief flinch (rate-limited so it can't be stun-locked). No points.
        if (animator != null && hasHitParam && Time.time >= nextHitReactTime)
        {
            animator.SetTrigger(hitParam);
            nextHitReactTime = Time.time + hitReactCooldown;
        }
    }

    private void Die(bool isHeadshot = false, bool isMelee = false, ulong attackerClientId = PlayerPoints.EveryoneClientId)
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
            // Credit the killing shooter (per-player economy). Melee passes its own
            // attacker id; the Nuke / unattributed kills credit everyone.
            PlayerPoints.Instance?.AddPoints(attackerClientId, reward);
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
        DisableDeadColliders();

        float despawnDelay = Mathf.Max(0f, deathDestroyDelay);
        NetworkObject netObj = NetworkActive ? GetComponent<NetworkObject>() : null;
        if (netObj != null && netObj.IsSpawned)
        {
            // Networked: keep the body briefly for the death anim, then despawn across
            // the network (only the server ever reaches Die()).
            StartCoroutine(DespawnAfter(netObj, despawnDelay));
        }
        else
        {
            Destroy(gameObject, despawnDelay);
        }
    }

    private IEnumerator DespawnAfter(NetworkObject netObj, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (netObj != null && netObj.IsSpawned)
        {
            netObj.Despawn(true);
        }
    }

    // Disable every collider on the zombie so a dead body stops blocking player movement,
    // absorbing bullets, or registering further hits. Used on the server (in Die) and on
    // clients (via ClientDeathWatch) so dead hitboxes deactivate on every peer.
    private void DisableDeadColliders()
    {
        foreach (Collider c in GetComponentsInChildren<Collider>())
        {
            if (c != null)
            {
                c.enabled = false;
            }
        }
    }

    // Client-only: the server drives death via the "Die" trigger (replicated by
    // NetworkAnimator). Once this replica enters/transitions to the "Death" state we drop
    // its colliders locally so the corpse stops blocking movement/bullets before the server
    // despawns it. No AI, points, or death logic runs here — those stay server-authoritative.
    private void ClientDeathWatch()
    {
        if (clientDeathHandled || animator == null)
        {
            return;
        }

        bool dying = animator.GetCurrentAnimatorStateInfo(0).IsName("Death") ||
                     (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName("Death"));
        if (!dying)
        {
            return;
        }

        clientDeathHandled = true;
        DisableDeadColliders();
        enabled = false; // nothing left to watch; the server despawns the body shortly
    }

    // Play hit/death sounds by watching the animator states the server drives and the
    // NetworkAnimator replicates. This runs identically on the server (host) and on every
    // client replica, so each machine plays its own copy exactly once — no RPCs, no
    // double-play. Purely observational: it never touches health, AI, points, or death logic.
    private void UpdateSoundWatch()
    {
        if (animator == null)
        {
            return;
        }

        // Death: fire once, the first frame this replica enters the "Death" state.
        if (!deathSoundPlayed && IsEnteringOrInState(deathStateHash))
        {
            deathSoundPlayed = true;
            PlayDeathSound();
        }

        // Surviving hit: fire on the rising edge of the "Hit" flinch state (the server
        // rate-limits the flinch itself; the small cooldown guards against a re-entered
        // flinch machine-gunning). Never overlaps a death.
        bool inHit = !deathSoundPlayed && IsEnteringOrInState(hitStateHash);
        if (inHit && !wasInHitState && Time.time >= nextHitSoundTime)
        {
            nextHitSoundTime = Time.time + Mathf.Max(0f, zombieHitSoundCooldown);
            PlayHitSound();
        }
        wasInHitState = inHit;
    }

    // True if the base-layer animator is in, or transitioning into, the given state.
    private bool IsEnteringOrInState(int stateHash)
    {
        if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == stateHash)
        {
            return true;
        }
        return animator.IsInTransition(0) &&
               animator.GetNextAnimatorStateInfo(0).shortNameHash == stateHash;
    }

    // A surviving hit plays on the zombie's own 3D source (the body lingers for the death
    // delay, so it's never cut mid-grunt). PlayOneShot lets a rare overlap layer cleanly.
    private void PlayHitSound()
    {
        AudioClip clip = PickRandom(ref _hitClips, HitSoundKeys);
        if (clip == null)
        {
            return;
        }
        // volumeScale keeps the grunt low even if the source volume is later changed.
        EnsureHitAudioSource().PlayOneShot(clip, Mathf.Clamp01(zombieHitVolume));
    }

    // Death plays on a DETACHED temporary 3D source at the zombie's position so it finishes
    // even though the body despawns/destroys shortly after (rule: don't cut the death sound).
    private void PlayDeathSound()
    {
        AudioClip clip = PickRandom(ref _deathClips, DeathSoundKeys);
        if (clip == null)
        {
            return;
        }
        PlayClipDetached3D(clip, transform.position, Mathf.Clamp01(zombieDeathVolume));
    }

    // Lazily create a 3D one-shot AudioSource on the zombie for hit grunts, configured from
    // the zombieAudio* tuning fields (quiet, spatial, quickly-quieting rolloff).
    private AudioSource EnsureHitAudioSource()
    {
        if (hitAudio != null)
        {
            return hitAudio;
        }
        hitAudio = gameObject.AddComponent<AudioSource>();
        hitAudio.playOnAwake = false;
        hitAudio.loop = false;
        hitAudio.spatialBlend = 1f; // 3D: positioned in the world
        // Per-shot volume is applied via PlayOneShot's volumeScale (below), so the source
        // stays at 1 and picks up runtime tweaks to zombieHitVolume without rebuilding.
        hitAudio.rolloffMode = zombieAudioRolloffMode;
        hitAudio.minDistance = Mathf.Max(0.01f, zombieAudioMinDistance);
        hitAudio.maxDistance = Mathf.Max(hitAudio.minDistance + 0.1f, zombieAudioMaxDistance);
        return hitAudio;
    }

    // Spawn a self-destroying 3D AudioSource at a world point (like AudioSource.PlayClipAtPoint
    // but with explicit 3D falloff + volume), so the clip outlives the zombie GameObject.
    private void PlayClipDetached3D(AudioClip clip, Vector3 position, float volume)
    {
        var go = new GameObject("ZombieDeathSound");
        go.transform.position = position;
        AudioSource src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.spatialBlend = 1f; // 3D
        src.volume = Mathf.Clamp01(volume);
        src.rolloffMode = zombieAudioRolloffMode;
        src.minDistance = Mathf.Max(0.01f, zombieAudioMinDistance);
        src.maxDistance = Mathf.Max(src.minDistance + 0.1f, zombieAudioMaxDistance);
        src.Play();
        Destroy(go, clip.length + 0.1f);
    }

    // Load (once) and return a random clip from a Resources/ZombieSounds pool. Missing clips
    // are skipped; returns null only if the whole pool failed to load (then no sound plays).
    private static AudioClip PickRandom(ref AudioClip[] cache, string[] keys)
    {
        if (cache == null)
        {
            cache = new AudioClip[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                cache[i] = Resources.Load<AudioClip>("ZombieSounds/" + keys[i]);
                if (cache[i] == null)
                {
                    Debug.LogWarning("[ZombieAgent] Missing clip Resources/ZombieSounds/" + keys[i] + ".");
                }
            }
        }

        // Pick among the clips that actually loaded.
        int start = Random.Range(0, cache.Length);
        for (int n = 0; n < cache.Length; n++)
        {
            AudioClip c = cache[(start + n) % cache.Length];
            if (c != null)
            {
                return c;
            }
        }
        return null;
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
        // Target the nearest reachable, non-downed player (multiplayer aware).
        // PlayerHealth sits on the player root, so this also reliably wires up the
        // health reference the attack uses.
        PlayerHealth[] players = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
        PlayerHealth nearest = null;
        float bestDistance = float.MaxValue;
        foreach (PlayerHealth ph in players)
        {
            if (!IsTargetable(ph))
            {
                continue;
            }

            if (!HasCompletePathTo(ph.transform))
            {
                continue;
            }

            float distance = Vector3.Distance(transform.position, ph.transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = ph;
            }
        }

        if (nearest != null)
        {
            player = nearest.transform;
            playerHealth = nearest;
        }
        else
        {
            ClearTarget();
        }
    }

    private bool IsCurrentTargetValid()
    {
        return player != null &&
               playerHealth != null &&
               IsTargetable(playerHealth);
    }

    private static bool IsTargetable(PlayerHealth health)
    {
        return health != null &&
               health.gameObject.activeInHierarchy &&
               !health.IsDead &&
               !health.IsDowned;
    }

    private bool HasCompletePathTo(Transform target)
    {
        if (target == null)
        {
            return false;
        }

        if (agent == null || !agent.enabled)
        {
            return true;
        }

        Vector3 from = transform.position;
        if (agent.isOnNavMesh)
        {
            from = agent.transform.position;
        }
        else if (NavMesh.SamplePosition(transform.position, out NavMeshHit sampledFrom, 2f, NavMesh.AllAreas))
        {
            from = sampledFrom.position;
        }
        else
        {
            return true;
        }

        if (!NavMesh.SamplePosition(target.position, out NavMeshHit sampledTarget, 2f, NavMesh.AllAreas))
        {
            return false;
        }

        targetPathScratch ??= new NavMeshPath();
        if (!NavMesh.CalculatePath(from, sampledTarget.position, NavMesh.AllAreas, targetPathScratch))
        {
            return false;
        }

        return targetPathScratch.status == NavMeshPathStatus.PathComplete;
    }

    private void ClearTarget()
    {
        player = null;
        playerHealth = null;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
