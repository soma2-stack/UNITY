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
    /// Global multiplier applied to every <see cref="Add"/> (Double Points power-up).
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
    }

    /// <summary>Adds points to the player's total. Ignores non-positive amounts.</summary>
    public void Add(int amount)
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
        Debug.Log($"Added {amount} points (total: {Points})");
        OnPointsChanged?.Invoke(Points);
    }

    /// <summary>
    /// Attempts to spend points.
    /// Treats a non-positive amount as free (returns true, no change).
    /// Returns true and subtracts if the player can afford it; otherwise false.
    /// </summary>
    public bool TrySpend(int amount)
    {
        if (amount <= 0)
        {
            return true;
        }

        if (Points >= amount)
        {
            Points -= amount;
            Debug.Log($"Spent {amount} points (total: {Points})");
            OnPointsChanged?.Invoke(Points);
            return true;
        }

        return false;
    }
}
