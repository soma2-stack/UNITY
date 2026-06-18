using UnityEngine;

/// <summary>
/// Simple player health component for the School Of The Dead zombies mode.
///
/// Put this on the player GameObject (the one with the CharacterController).
/// Zombies call <see cref="TakeDamage"/> to hurt the player. When health
/// reaches zero the player "dies": a Debug.Log fires and the
/// <see cref="OnPlayerDied"/> event is raised so other systems can react.
///
/// Health slowly regenerates after the player has not been hurt for
/// <see cref="regenDelay"/> seconds (classic CoD-style health). This can be
/// disabled by turning off <see cref="enableRegen"/>.
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    [Header("Health")]
    [Tooltip("Maximum (and starting) health.")]
    public int maxHealth = 100;

    [Header("Regeneration")]
    [Tooltip("If enabled the player slowly heals after not taking damage for a while.")]
    public bool enableRegen = true;
    [Tooltip("Seconds the player must avoid damage before regeneration starts.")]
    public float regenDelay = 4f;
    [Tooltip("Health restored per second once regeneration kicks in.")]
    public float regenPerSecond = 25f;

    [Header("UI")]
    [Tooltip("Draw a lightweight OnGUI health label/bar in the top-left corner.")]
    public bool showHud = true;

    /// <summary>Raised once when the player dies (health hits zero).</summary>
    public event System.Action OnPlayerDied;

    /// <summary>Current health (read-only externally).</summary>
    public int CurrentHealth { get; private set; }

    /// <summary>True once the player has died (prevents double death handling).</summary>
    public bool IsDead { get; private set; }

    private float lastDamageTime;
    private float regenAccumulator; // fractional health carried between frames

    private void Awake()
    {
        CurrentHealth = maxHealth;
    }

    private void Update()
    {
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
    /// </summary>
    public void TakeDamage(int amount)
    {
        if (IsDead || amount <= 0)
        {
            return;
        }

        CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
        lastDamageTime = Time.time;
        regenAccumulator = 0f;

        if (CurrentHealth <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// Instantly restore health (clamped to <see cref="maxHealth"/>).
    /// </summary>
    public void Heal(int amount)
    {
        if (IsDead || amount <= 0)
        {
            return;
        }

        CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
    }

    private void Die()
    {
        if (IsDead)
        {
            return;
        }

        IsDead = true;
        Debug.Log("Player died");
        OnPlayerDied?.Invoke();
    }

    private void OnGUI()
    {
        if (!showHud)
        {
            return;
        }

        // Drawn slightly lower so it sits under a points display (PlayerPoints).
        string label = IsDead ? "DEAD" : "Health: " + CurrentHealth + " / " + maxHealth;
        GUI.Label(new Rect(10, 30, 250, 24), label);
    }
}
