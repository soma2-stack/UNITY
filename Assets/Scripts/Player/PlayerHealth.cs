using UnityEngine;

/// <summary>
/// Player health for the School Of The Dead zombies mode, including a Call of
/// Duty Zombies style DOWN / REVIVE flow.
///
/// Put this on the player GameObject (the one with the CharacterController).
/// Zombies call <see cref="TakeDamage"/> to hurt the player.
///
/// Instead of dying instantly at 0 health the player enters a DOWNED state
/// (<see cref="IsDowned"/>): regeneration stops and a bleed-out timer starts.
///   - If the player owns the Quick Revive perk a SOLO self-revive happens after
///     a short delay (<see cref="quickReviveSelfReviveDelay"/>).
///   - Otherwise, when the bleed-out timer runs out the player finally dies:
///     <see cref="Die"/> fires <see cref="OnPlayerDied"/> (kept for existing
///     listeners) and <see cref="IsDead"/> becomes true.
///
/// Health slowly regenerates after the player has not been hurt for
/// <see cref="regenDelay"/> seconds (classic CoD-style health). This can be
/// disabled by turning off <see cref="enableRegen"/>. Regen never runs while
/// downed or dead.
/// </summary>
public class PlayerHealth : MonoBehaviour
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
    [Range(0f, 1f)] public float reviveHealthFraction = 1f;
    [Tooltip("Delay (seconds) before a Quick Revive solo self-revive completes once downed.")]
    public float quickReviveSelfReviveDelay = 3f;

    [Header("UI")]
    [Tooltip("Draw a lightweight OnGUI health label/bar in the top-left corner.")]
    public bool showHud = true;

    /// <summary>Raised once when the player FINALLY dies (bleed-out with no revive). Kept for existing listeners.</summary>
    public event System.Action OnPlayerDied;

    /// <summary>Raised when the player enters the downed state.</summary>
    public event System.Action OnPlayerDowned;

    /// <summary>Raised when the player is revived (downed -> alive again).</summary>
    public event System.Action OnPlayerRevived;

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

    private float lastDamageTime;
    private float regenAccumulator; // fractional health carried between frames
    private float downedAtTime;     // Time.time when the player went down

    private void Awake()
    {
        CurrentHealth = maxHealth;

        // CoD Zombies: losing a random perk each time you are revived.
        OnPlayerRevived += HandleRevivedLosePerk;
    }

    private void HandleRevivedLosePerk()
    {
        PerkManager.Instance?.LoseRandomPerk();
    }

    private void Update()
    {
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
        }
    }

    /// <summary>
    /// Apply damage to the player. Negative or zero amounts are ignored.
    /// The first lethal hit DOWNS the player; further damage while downed does
    /// not instantly kill (death comes from bleed-out instead).
    /// </summary>
    public void TakeDamage(int amount)
    {
        if (IsDead || amount <= 0)
        {
            return;
        }

        lastDamageTime = Time.time;
        regenAccumulator = 0f;

        if (IsDowned)
        {
            // Already on the ground: take the chip damage but don't end the run
            // early. Bleed-out (or a revive) decides the outcome.
            CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
            return;
        }

        CurrentHealth = Mathf.Max(0, CurrentHealth - amount);

        if (CurrentHealth <= 0)
        {
            EnterDowned();
        }
    }

    /// <summary>
    /// Instantly restore health (clamped to <see cref="maxHealth"/>).
    /// Ignored while dead. While downed this tops up health but does NOT by
    /// itself stand the player up - use <see cref="Revive"/> for that.
    /// </summary>
    public void Heal(int amount)
    {
        if (IsDead || amount <= 0)
        {
            return;
        }

        CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
    }

    /// <summary>
    /// Raise (or lower) the maximum health at runtime - used by Juggernog. When
    /// <paramref name="healToFull"/> is true the player is also healed to the new
    /// maximum. No-op once the player has finally died.
    /// </summary>
    public void SetMaxHealth(int newMax, bool healToFull)
    {
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
    }

    /// <summary>
    /// Revive the player from the downed state, restoring health to
    /// <see cref="reviveHealthFraction"/> of max. Safe to call when not downed
    /// (does nothing). Raises <see cref="OnPlayerRevived"/>.
    /// </summary>
    public void Revive()
    {
        if (!IsDowned || IsDead)
        {
            return;
        }

        IsDowned = false;
        BleedOutRemaining = 0f;
        lastDamageTime = Time.time;
        regenAccumulator = 0f;

        // Authentic CoD Zombies: a revive restores EXACTLY 25% of max health (the
        // reviveHealthFraction field is intentionally ignored to match base-game feel).
        int restored = Mathf.Max(1, Mathf.RoundToInt(maxHealth * 0.25f));
        CurrentHealth = Mathf.Clamp(restored, 1, maxHealth);

        Debug.Log("[PlayerHealth] Player revived (" + CurrentHealth + "/" + maxHealth + ")");
        OnPlayerRevived?.Invoke();
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
    }

    private void UpdateDowned()
    {
        if (IsDead)
        {
            return;
        }

        BleedOutRemaining = Mathf.Max(0f, BleedOutRemaining - Time.deltaTime);

        // Quick Revive: solo self-revive after a short delay (null-safe if no PerkManager).
        if (PerkManager.Instance != null && PerkManager.Instance.HasPerk(PerkType.QuickRevive))
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
            bool quickRevive = PerkManager.Instance != null && PerkManager.Instance.HasPerk(PerkType.QuickRevive);
            label = quickRevive
                ? "DOWNED - reviving... (" + BleedOutRemaining.ToString("0") + "s)"
                : "DOWNED - bleeding out: " + BleedOutRemaining.ToString("0") + "s";
        }
        else
        {
            label = "Health: " + CurrentHealth + " / " + maxHealth;
        }

        // Drawn slightly lower so it sits under a points display (PlayerPoints).
        GUI.Label(new Rect(10, 30, 320, 24), label);
    }
}
