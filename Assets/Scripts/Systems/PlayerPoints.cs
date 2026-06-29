using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Per-player economy / points API (Call of Duty Zombies style).
///
/// Each player has their OWN points balance. In a networked session the SERVER is
/// authoritative: it keeps one balance per connected client, and pushes the whole
/// table to everyone so each peer can show its own points (and its teammates').
/// Kills credit the shooter; purchases charge the buyer (routed through
/// <see cref="NetworkGameplayCoordinator"/>, which passes the requesting client id).
///
/// In solo (not network-spawned) there is a single local balance and every call
/// operates on it exactly as before — single-player behaviour is unchanged.
///
/// There should only ever be one instance in the scene.
/// </summary>
public class PlayerPoints : MonoBehaviour
{
    public static PlayerPoints Instance { get; private set; }

    /// <summary>Sentinel client id meaning "credit every connected player" (e.g. Nuke / Carpenter bonuses).</summary>
    public const ulong EveryoneClientId = ulong.MaxValue;

    [Header("Economy")]
    [Tooltip("Points each player starts the game with.")]
    [SerializeField] private int startingPoints = 500;

    /// <summary>This peer's own points (what the local HUD shows). Server = host's own balance.</summary>
    public int Points { get; private set; }

    /// <summary>
    /// Global multiplier applied to every <see cref="AddPoints(int)"/> (Double Points power-up).
    /// 1 = normal. Set/cleared by PowerupManager. Never below 1.
    /// </summary>
    public static int PointsMultiplier { get; set; } = 1;

    /// <summary>Raised whenever the LOCAL player's points change, passing the new total.</summary>
    public event System.Action<int> OnPointsChanged;

    /// <summary>One player's balance, used by the HUD to draw every player's points.</summary>
    public readonly struct Entry
    {
        public readonly ulong ClientId;
        public readonly int Points;
        public Entry(ulong clientId, int points)
        {
            ClientId = clientId;
            Points = points;
        }
    }

    // Server-only: authoritative balance per connected client.
    private readonly Dictionary<ulong, int> balances = new Dictionary<ulong, int>();

    // Replicated snapshot every peer holds (ordered by client id) so the HUD can show
    // each player's points. In solo this is a single synthetic entry.
    private readonly List<Entry> table = new List<Entry>();

    /// <summary>Every player's points (ordered), for the HUD. Read-only snapshot.</summary>
    public IReadOnlyList<Entry> Table => table;

    /// <summary>The local peer's client id (0 in solo / for the host).</summary>
    public ulong LocalClientId =>
        NetworkActive && NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;

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

        // ✅ PointsMultiplier reset guaranteed (static survives scene reloads).
        PointsMultiplier = 1;

        // Solo starts with a single local balance so the HUD shows one row.
        table.Clear();
        table.Add(new Entry(0, Points));
    }

    // --- Multiplayer authority -------------------------------------------
    private const string PointsSyncMessage = "SOTD_POINTS";

    private static bool NetworkActive =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    private static bool IsServerRole =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    // No-arg points operations target this peer's "own" balance: the host (server) in a
    // session, or the single local balance in solo. Networked clients can't mint/spend
    // directly (they route purchases through NetworkGameplayCoordinator).
    private static ulong LocalAuthorityId => NetworkActive ? NetworkManager.ServerClientId : 0;

    private void Start()
    {
        // Clients listen for the authoritative per-player table from the server.
        if (NetworkActive && !IsServerRole)
        {
            CustomMessagingManager messaging = NetworkManager.Singleton.CustomMessagingManager;
            if (messaging != null)
            {
                messaging.UnregisterNamedMessageHandler(PointsSyncMessage);
                messaging.RegisterNamedMessageHandler(PointsSyncMessage, OnPointsSyncMessage);
            }
        }
        else if (NetworkActive && IsServerRole)
        {
            // Seed the host's own balance and push the opening table.
            EnsureSeeded(NetworkManager.ServerClientId);
            AfterServerChange();
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.CustomMessagingManager != null)
        {
            NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(PointsSyncMessage);
        }
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // --- Server balance helpers ------------------------------------------

    private int GetBalance(ulong clientId)
    {
        return balances.TryGetValue(clientId, out int value) ? value : startingPoints;
    }

    private void EnsureSeeded(ulong clientId)
    {
        if (!balances.ContainsKey(clientId))
        {
            balances[clientId] = startingPoints;
        }
    }

    // Make sure every currently-connected client has a balance entry.
    private void EnsureAllConnectedSeeded()
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }
        foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
        {
            EnsureSeeded(id);
        }
    }

    // Called after the server mutates any balance: refresh the host's own displayed
    // total, rebuild the shared table, and broadcast it to all clients.
    private void AfterServerChange()
    {
        EnsureAllConnectedSeeded();

        Points = Mathf.Max(0, GetBalance(NetworkManager.ServerClientId));

        RebuildTableFromBalances();
        OnPointsChanged?.Invoke(Points);
        BroadcastTable();
    }

    private void RebuildTableFromBalances()
    {
        table.Clear();
        foreach (KeyValuePair<ulong, int> kv in balances)
        {
            table.Add(new Entry(kv.Key, Mathf.Max(0, kv.Value)));
        }
        table.Sort((a, b) => a.ClientId.CompareTo(b.ClientId));
    }

    // --- Network sync (server -> clients): the whole per-player table -----

    private void BroadcastTable()
    {
        if (!NetworkActive || !IsServerRole)
        {
            return;
        }
        CustomMessagingManager messaging = NetworkManager.Singleton.CustomMessagingManager;
        if (messaging == null)
        {
            return;
        }
        using FastBufferWriter writer = WriteTable();
        messaging.SendNamedMessageToAll(PointsSyncMessage, writer, NetworkDelivery.ReliableSequenced);
    }

    /// <summary>Server: push the current table to one just-loaded client (catch-up + seed).</summary>
    public void SendPointsToClient(ulong clientId)
    {
        if (!NetworkActive || !IsServerRole)
        {
            return;
        }

        // A newly joined client gets seeded so it (and everyone) sees its starting points.
        EnsureSeeded(clientId);
        AfterServerChange(); // reseats the table for everyone, including the new client
    }

    private FastBufferWriter WriteTable()
    {
        int size = sizeof(int) + balances.Count * (sizeof(ulong) + sizeof(int));
        FastBufferWriter writer = new FastBufferWriter(size, Allocator.Temp);
        writer.WriteValueSafe(table.Count);
        foreach (Entry entry in table)
        {
            writer.WriteValueSafe(entry.ClientId);
            writer.WriteValueSafe(entry.Points);
        }
        return writer;
    }

    private void OnPointsSyncMessage(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int count);
        table.Clear();
        ulong localId = LocalClientId;
        int localTotal = Points;
        for (int i = 0; i < count; i++)
        {
            reader.ReadValueSafe(out ulong clientId);
            reader.ReadValueSafe(out int points);
            table.Add(new Entry(clientId, Mathf.Max(0, points)));
            if (clientId == localId)
            {
                localTotal = Mathf.Max(0, points);
            }
        }

        Points = localTotal;
        OnPointsChanged?.Invoke(Points);
    }

    // --- Solo helpers -----------------------------------------------------

    private void SoloChanged()
    {
        Points = Mathf.Max(0, Points);
        table.Clear();
        table.Add(new Entry(0, Points));
        OnPointsChanged?.Invoke(Points);
    }

    // --- Public API: add points ------------------------------------------

    /// <summary>Adds points to THIS peer's own balance (solo, or the host). Networked clients no-op.</summary>
    public void AddPoints(int amount) => AddPointsInternal(LocalAuthorityId, amount);

    /// <summary>Alias for AddPoints for backward compatibility.</summary>
    public void Add(int amount) => AddPoints(amount);

    /// <summary>
    /// Server: add points to a specific client's balance (e.g. the shooter who got a kill).
    /// Pass <see cref="EveryoneClientId"/> to credit every connected player. In solo this
    /// simply adds to the single local balance regardless of the id.
    /// </summary>
    public void AddPoints(ulong clientId, int amount) => AddPointsInternal(clientId, amount);

    /// <summary>Server: add points to every connected player (Nuke / Carpenter bonuses).</summary>
    public void AddPointsToAll(int amount) => AddPointsInternal(EveryoneClientId, amount);

    private void AddPointsInternal(ulong clientId, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        // Double Points (and any future) global multiplier.
        int mult = Mathf.Max(1, PointsMultiplier);
        amount *= mult;

        if (!NetworkActive)
        {
            // Solo: one shared local balance.
            Points += amount;
            SoloChanged();
            return;
        }

        // Networked: only the server may mint points.
        if (!IsServerRole)
        {
            return;
        }

        if (clientId == EveryoneClientId)
        {
            EnsureAllConnectedSeeded();
            List<ulong> ids = new List<ulong>(balances.Keys);
            foreach (ulong id in ids)
            {
                balances[id] = Mathf.Max(0, GetBalance(id) + amount);
            }
        }
        else
        {
            EnsureSeeded(clientId);
            balances[clientId] = Mathf.Max(0, GetBalance(clientId) + amount);
        }

        AfterServerChange();
    }

    // --- Public API: spend / query ---------------------------------------

    /// <summary>Returns this peer's own points total.</summary>
    public int GetPoints() => Points;

    /// <summary>True if THIS peer can afford the given cost.</summary>
    public bool CanAfford(int cost) => Points >= cost;

    /// <summary>Server: true if the given client can afford the cost.</summary>
    public bool CanAfford(ulong clientId, int cost) => GetBalance(clientId) >= cost;

    /// <summary>
    /// Spend from THIS peer's own balance (solo, or the host). Returns true on success
    /// (or cost &lt;= 0). Networked clients return false — they route purchases through
    /// NetworkGameplayCoordinator, which spends server-side via the client-id overload.
    /// </summary>
    public bool SpendPoints(int cost) => TrySpendInternal(LocalAuthorityId, cost);

    /// <summary>Spend from this peer's own balance (legacy name).</summary>
    public bool TrySpend(int amount) => SpendPoints(amount);

    /// <summary>Server: spend from a specific client's balance (purchases). Returns true on success.</summary>
    public bool TrySpend(ulong clientId, int cost) => TrySpendInternal(clientId, cost);

    private bool TrySpendInternal(ulong clientId, int cost)
    {
        if (cost <= 0)
        {
            return true;
        }

        if (!NetworkActive)
        {
            // Solo: one shared local balance.
            if (Points >= cost)
            {
                Points -= cost;
                SoloChanged();
                return true;
            }
            return false;
        }

        // Networked: only the server may move points.
        if (!IsServerRole)
        {
            return false;
        }

        EnsureSeeded(clientId);
        int balance = GetBalance(clientId);
        if (balance >= cost)
        {
            balances[clientId] = Mathf.Max(0, balance - cost);
            AfterServerChange();
            return true;
        }

        return false;
    }
}
