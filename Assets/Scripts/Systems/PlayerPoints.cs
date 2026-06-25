using UnityEngine;

/// <summary>
/// Shared economy / points API (Call of Duty Zombies style).
/// Other systems (doors, weapons, perks, etc.) call into this singleton to add
/// or spend points. There should only ever be one instance in the scene.
/// </summary>
public class PlayerPoints : MonoBehaviour
{
    public static PlayerPoints Instance { get; private set; }

    [Header("Economy")]
    [Tooltip("Points the player starts the game with.")]
    [SerializeField] private int startingPoints = 500;

    public int Points { get; private set; }

    /// <summary>
    /// Global multiplier applied to every <see cref="AddPoints"/> (Double Points power-up).
    /// 1 = normal. Set/cleared by PowerupManager. Never below 1.
    /// </summary>
    public static int PointsMultiplier { get; set; } = 1;

    /// <summary>Raised whenever Points changes, passing the new total.</summary>
    public event System.Action<int> OnPointsChanged;

    private void Awake()
    {
        // Enforce a single instance.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        Points = startingPoints;

        // ✅ CHECKPOINT 2 — PointsMultiplier reset guaranteed
        // PointsMultiplier is static, so it survives scene reloads for the whole
        // process lifetime. Resetting it here — in the points singleton's own
        // startup — guarantees every fresh run begins at 1x regardless of
        // bootstrap order. PowerupManager.ClearEffects() still resets it too, but
        // that can run before this object exists; this line makes the reset
        // unconditional so a stale Double Points multiplier can never carry over.
        PointsMultiplier = 1;
    }

    /// <summary>Adds points to the player's total. Ignores non-positive amounts.</summary>
    public void AddPoints(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        // Double Points (and any future) global multiplier, applied here so every
        // points source (kills, doors, etc.) benefits without changes elsewhere.
        int mult = Mathf.Max(1, PointsMultiplier);
        amount *= mult;

        Points += amount;
        Points = Mathf.Max(0, Points);
        Debug.Log($"Added {amount} points (total: {Points})");
        OnPointsChanged?.Invoke(Points);
    }

    /// <summary>Alias for AddPoints for backward compatibility.</summary>
    public void Add(int amount) => AddPoints(amount);

    /// <summary>Returns the current points total.</summary>
    public int GetPoints() => Points;

    /// <summary>Returns true if the player can afford the given cost.</summary>
    public bool CanAfford(int cost) => Points >= cost;

    /// <summary>
    /// Attempts to spend points. Returns true if successful (or cost <= 0).
    /// Points never go below zero.
    /// </summary>
    public bool SpendPoints(int cost)
    {
        if (cost <= 0)
        {
            return true;
        }

        if (Points >= cost)
        {
            Points -= cost;
            Points = Mathf.Max(0, Points);
            Debug.Log($"Spent {cost} points (total: {Points})");
            OnPointsChanged?.Invoke(Points);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Attempts to spend points (legacy name).
    /// Treats a non-positive amount as free (returns true, no change).
    /// Returns true and subtracts if the player can afford it; otherwise false.
    /// </summary>
    public bool TrySpend(int amount) => SpendPoints(amount);
}
