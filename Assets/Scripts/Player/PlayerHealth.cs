using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Player health for the School Of The Dead zombies mode, including a Call of
/// Duty Zombies style DOWN / REVIVE flow.
///
/// Server-authoritative in a networked session, with a local fallback so SOLO
/// (no NGO session / not network-spawned) keeps working exactly as before:
///   - SOLO / not spawned: all logic runs locally (the authority is "this peer").
///   - NETWORKED: only the SERVER mutates health/downed/dead and writes the
///     NetworkVariables; clients mirror those values (and fire the matching
///     events) for their HUD. TakeDamage from a client is routed to the server
///     via a ServerRpc.
///
/// NOTE: because this is a NetworkBehaviour it requires a NetworkObject on the
/// same GameObject. The networked player prefab has one; the solo scene player
/// needs one added (it stays unspawned in solo, so the local path is used).
/// </summary>
public class PlayerHealth : NetworkBehaviour
{
    [Header("Health")]
    [Tooltip("Maximum (and starting) health. Perks like Juggernog can raise this at runtime via SetMaxHealth().")]
    public int maxHealth = 100;

    [Header("Regeneration")]
    [Tooltip("If enabled the player slowly heals after not taking damage for a while.")]
    public bool enableRegen = true;
    [Tooltip("Seconds the player must avoid damage before regeneration starts.")]
    public float regenDelay = 4f;
    [Tooltip("Health restored per second once regeneration kicks in.")]
    public float regenPerSecond = 25f;

    [Header("Down / Revive")]
    [Tooltip("Seconds the player can stay downed before bleeding out (final death).")]
    public float bleedOutTime = 30f;
    [Tooltip("Fraction of max health restored on revive (0..1). 1 = full health.")]
    [Range(0f, 1f)] public float reviveHealthFraction = 0.25f;
    [Tooltip("Delay (seconds) before a Quick Revive solo self-revive completes once downed.")]
    public float quickReviveSelfReviveDelay = 3f;
    [Tooltip("If true the player is in solo mode and RescueRush self-revives. In multiplayer this is handled by the revive interaction script.")]
    public bool isSoloMode = true;

    [Header("UI")]
    [Tooltip("Draw a lightweight OnGUI health label/bar in the top-left corner.")]
    public bool showHud = true;

    /// <summary>Raised once when the player FINALLY dies (bleed-out with no revive). Kept for existing listeners.</summary>
    public event System.Action OnPlayerDied;

    /// <summary>Raised when the player enters the downed state.</summary>
    public event System.Action OnPlayerDowned;

    /// <summary>Raised when the player is revived (downed -> alive again).</summary>
    public event System.Action OnPlayerRevived;

    /// <summary>Raised when the player takes damage (passes the damage amount). For HUD vignette etc.</summary>
    public event System.Action<int> OnDamageTaken;

    /// <summary>Current health (read-only externally).</summary>
    public int CurrentHealth { get; private set; }

    /// <summary>True only after the player has FINALLY died (not while merely downed).</summary>
    public bool IsDead { get; private set; }

    /// <summary>True while the player is downed and awaiting revive / bleed-out.</summary>
    public bool IsDowned { get; private set; }

    /// <summary>
    /// True while the player is downed but not yet finally dead: in this state they may
    /// use ONLY their reduced-damage downed pistol. WeaponController reads this to know
    /// whether to apply the downed-gun damage penalty.
    /// </summary>
    public bool IsDownedGunActive => IsDowned && !IsDead;

    /// <summary>Seconds remaining before bleed-out while downed (0 when not downed).</summary>
    public float BleedOutRemaining { get; private set; }

    // --- Networked authoritative state (server-write, everyone-read) ---
    private readonly NetworkVariable<int> networkHealth =
        new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> networkIsDowned =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> networkIsDead =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private float lastDamageTime;
    private float regenAccumulator; // fractional health carried between frames
    private float downedAtTime;     // Time.time when the player went down

    // True in solo (not network-spawned) OR on the server in a session.
    private bool HasAuthority => !IsSpawned || IsServer;

    private void Awake()
    {
        CurrentHealth = maxHealth;

        // CoD Zombies: losing a random perk each time you are revived.
        OnPlayerRevived += HandleRevivedLosePerk;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Auto solo-mode from the connected client count (server-only data).
            isSoloMode = NetworkManager.Singleton == null ||
                         NetworkManager.Singleton.ConnectedClientsIds.Count <= 1;

            // Seed authoritative state from the current local values.
            networkHealth.Value = CurrentHealth;
            networkIsDowned.Value = IsDowned;
            networkIsDead.Value = IsDead;
        }
        else
        {
            isSoloMode = false;

            // Mirror the replicated values immediately (late-join safe).
            CurrentHealth = networkHealth.Value;
            IsDowned = networkIsDowned.Value;
            IsDead = networkIsDead.Value;

            networkHealth.OnValueChanged += OnNetHealthChanged;
            networkIsDowned.OnValueChanged += OnNetDownedChanged;
            networkIsDead.OnValueChanged += OnNetDeadChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer)
        {
            networkHealth.OnValueChanged -= OnNetHealthChanged;
            networkIsDowned.OnValueChanged -= OnNetDownedChanged;
            networkIsDead.OnValueChanged -= OnNetDeadChanged;
        }
    }

    // --- Client mirror handlers (clients update HUD + fire events) ---
    private void OnNetHealthChanged(int oldValue, int newValue)
    {
        CurrentHealth = newValue;
    }

    private void OnNetDownedChanged(bool oldValue, bool newValue)
    {
        IsDowned = newValue;
        if (newValue && !oldValue)
        {
            BleedOutRemaining = bleedOutTime;
            OnPlayerDowned?.Invoke();
        }
        else if (!newValue && !IsDead)
        {
            BleedOutRemaining = 0f;
            OnPlayerRevived?.Invoke();
        }
    }

    private void OnNetDeadChanged(bool oldValue, bool newValue)
    {
        IsDead = newValue;
        if (newValue && !oldValue)
        {
            IsDowned = false;
            BleedOutRemaining = 0f;
            OnPlayerDied?.Invoke();
        }
    }

    private void HandleRevivedLosePerk()
    {
        // Perk loss is decided by the authority so clients don't drop perks independently.
        if (!HasAuthority)
        {
            return;
        }
        PerkManager.Instance?.LoseRandomPerk();
    }

    private void Update()
    {
        // Only the authority simulates health (regen + bleed-out). Clients mirror state.
        if (!HasAuthority)
        {
            return;
        }

        if (IsDowned)
        {
            UpdateDowned();
            return;
        }

        if (IsDead || !enableRegen)
        {
            return;
        }

        if (CurrentHealth >= maxHealth)
        {
            return;
        }

        // Only regenerate after the grace period since the last hit.
        if (Time.time - lastDamageTime < regenDelay)
        {
            return;
        }

        regenAccumulator += regenPerSecond * Time.deltaTime;
        if (regenAccumulator >= 1f)
        {
            int healed = Mathf.FloorToInt(regenAccumulator);
            regenAccumulator -= healed;
            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + healed);
            SyncState();
        }
    }

    /// <summary>
    /// Apply damage to the player. Negative or zero amounts are ignored. From a client in
    /// a session this is routed to the server; on the server/solo it applies directly.
    /// The first lethal hit DOWNS the player; further damage while downed does not
    /// instantly kill (death comes from bleed-out instead).
    /// </summary>
    public void TakeDamage(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        if (IsSpawned && !IsServer)
        {
            TakeDamageServerRpc(amount);
            return;
        }

        ApplyDamage(amount);
    }

    [ServerRpc(RequireOwnership = false)]
    private void TakeDamageServerRpc(int amount)
    {
        ApplyDamage(amount);
    }

    // Authority-side damage application (server in a session, or local in solo).
    private void ApplyDamage(int amount)
    {
        if (IsDead || amount <= 0)
        {
            return;
        }

        lastDamageTime = Time.time;
        regenAccumulator = 0f;

        if (IsDowned)
        {
            // Already on the ground: take the chip damage but don't end the run early.
            CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
            SyncState();
            return;
        }

        CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
        SyncState();
        RaiseDamageTaken(amount);

        if (CurrentHealth <= 0)
        {
            EnterDowned();
        }
    }

    /// <summary>
    /// Instantly restore health (clamped to <see cref="maxHealth"/>). Ignored while dead.
    /// Authority-only in a session.
    /// </summary>
    public void Heal(int amount)
    {
        if (IsSpawned && !IsServer)
        {
            return;
        }
        if (IsDead || amount <= 0)
        {
            return;
        }

        CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
        SyncState();
    }

    /// <summary>
    /// Raise (or lower) the maximum health at runtime - used by Juggernog. Authority-only
    /// in a session. No-op once the player has finally died.
    /// </summary>
    public void SetMaxHealth(int newMax, bool healToFull)
    {
        if (IsSpawned && !IsServer)
        {
            return;
        }
        if (IsDead)
        {
            return;
        }

        maxHealth = Mathf.Max(1, newMax);
        if (healToFull)
        {
            CurrentHealth = maxHealth;
        }
        else
        {
            CurrentHealth = Mathf.Min(CurrentHealth, maxHealth);
        }
        SyncState();
    }

    /// <summary>
    /// Revive the player from the downed state, restoring health to
    /// <see cref="reviveHealthFraction"/> of max. Authority-only in a session (the revive
    /// interaction routes through the server in multiplayer). Raises <see cref="OnPlayerRevived"/>.
    /// </summary>
    public void Revive()
    {
        if (IsSpawned && !IsServer)
        {
            return;
        }
        if (!IsDowned || IsDead)
        {
            return;
        }

        IsDowned = false;
        BleedOutRemaining = 0f;
        lastDamageTime = Time.time;
        regenAccumulator = 0f;

        // Revive restores reviveHealthFraction of max health (default 0.25).
        int restored = Mathf.Max(1, Mathf.RoundToInt(maxHealth * reviveHealthFraction));
        CurrentHealth = Mathf.Clamp(restored, 1, maxHealth);

        Debug.Log("[PlayerHealth] Player revived (" + CurrentHealth + "/" + maxHealth + ")");
        OnPlayerRevived?.Invoke();
        SyncState();
    }

    /// <summary>
    /// Co-op revive entry point: a reviving client calls this so the SERVER authorizes and
    /// performs the revive on this (the downed) player. RequireOwnership=false because the
    /// reviver is a different player. Solo / not-networked callers should use Revive() directly.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void ReviveServerRpc()
    {
        Revive();
    }

    private void EnterDowned()
    {
        if (IsDowned || IsDead)
        {
            return;
        }

        IsDowned = true;
        downedAtTime = Time.time;
        BleedOutRemaining = Mathf.Max(0f, bleedOutTime);
        regenAccumulator = 0f;

        Debug.Log("[PlayerHealth] Player DOWNED - bleed-out in " + BleedOutRemaining.ToString("0") + "s");
        OnPlayerDowned?.Invoke();
        SyncState();
    }

    private void UpdateDowned()
    {
        if (IsDead)
        {
            return;
        }

        BleedOutRemaining = Mathf.Max(0f, BleedOutRemaining - Time.deltaTime);

        // Quick Revive: solo self-revive after a short delay (null-safe if no PerkManager).
        // Multiplayer: revive speed bonus is handled in the
        // revive interaction script, not here.
        if (isSoloMode && PerkManager.Instance != null &&
            PerkManager.Instance.HasPerk(PerkType.RescueRush))
        {
            float downedFor = Time.time - downedAtTime;
            if (downedFor >= Mathf.Max(0f, quickReviveSelfReviveDelay))
            {
                Revive();
                return;
            }
        }

        if (BleedOutRemaining <= 0f)
        {
            Die();
        }
    }

    private void Die()
    {
        if (IsDead)
        {
            return;
        }

        IsDead = true;
        IsDowned = false;
        BleedOutRemaining = 0f;
        CurrentHealth = 0;
        Debug.Log("Player died");
        OnPlayerDied?.Invoke();
        SyncState();
    }

    // Push authoritative state to the NetworkVariables (server in a session only).
    private void SyncState()
    {
        if (!IsSpawned || !IsServer)
        {
            return;
        }
        networkHealth.Value = CurrentHealth;
        networkIsDowned.Value = IsDowned;
        networkIsDead.Value = IsDead;
    }

    // Fire OnDamageTaken locally (authority/solo) and, in a session, on all clients.
    private void RaiseDamageTaken(int amount)
    {
        OnDamageTaken?.Invoke(amount);
        if (IsSpawned && IsServer)
        {
            DamageTakenClientRpc(amount);
        }
    }

    [ClientRpc]
    private void DamageTakenClientRpc(int amount)
    {
        if (IsServer)
        {
            return; // the host already raised it directly
        }
        OnDamageTaken?.Invoke(amount);
    }

    private void OnGUI()
    {
        if (!showHud)
        {
            return;
        }

        string label;
        if (IsDead)
        {
            label = "DEAD";
        }
        else if (IsDowned)
        {
            bool quickRevive = PerkManager.Instance != null && PerkManager.Instance.HasPerk(PerkType.RescueRush);
            label = quickRevive
                ? "DOWNED - reviving... (" + BleedOutRemaining.ToString("0") + "s)"
                : "DOWNED - bleeding out: " + BleedOutRemaining.ToString("0") + "s";
        }
        else
        {
            label = "Health: " + CurrentHealth + " / " + maxHealth;
        }

        // Bottom-left, clear of GameHud's top-left player-point rows and the
        // bottom-right weapon/ammo readout.
        GUI.Label(new Rect(10, Screen.height - 30f, 320, 24), label);
    }
}
