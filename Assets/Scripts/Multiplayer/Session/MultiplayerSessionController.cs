using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class MultiplayerSessionController : MonoBehaviour
{
    public static MultiplayerSessionController Instance { get; private set; }

    public const int MaximumPlayers = 4;
    public const float ReconnectWindowSeconds = 120f;

    private const string RosterMessage = "SOTD_ROSTER";
    private const string MatchStartMessage = "SOTD_MATCH_START";
    private const string GameplayScene = "SchoolOfTheDead";
    private const string MenuScene = "MainMenu";

    private readonly List<RosterEntry> roster = new List<RosterEntry>(MaximumPlayers);
    private readonly Dictionary<ulong, ConnectionPayload> pendingPayloads = new Dictionary<ulong, ConnectionPayload>();
    private readonly Dictionary<string, float> disconnectedAt = new Dictionary<string, float>();
    private readonly HashSet<string> lockedRoster = new HashSet<string>();
    private readonly HashSet<ulong> spawnedPlayers = new HashSet<ulong>();

    private NetworkManager networkManager;
    private UnityTransport transport;
    private TaskCompletionSource<bool> connectionCompletion;
    private bool handlersRegistered;
    private bool rosterLocked;
    private string localPlayerId;
    private string localDisplayName;
    private string lastJoinCode;

    public MultiplayerSessionState State { get; private set; } = MultiplayerSessionState.Offline;
    public string JoinCode { get; private set; } = string.Empty;
    public string LastError { get; private set; } = string.Empty;
    public bool IsHost => networkManager != null && networkManager.IsHost;
    public bool CanReconnect => !string.IsNullOrWhiteSpace(lastJoinCode);
    public IReadOnlyList<RosterEntry> Roster => roster;

    public event Action<MultiplayerSessionState> StateChanged;
    public event Action<IReadOnlyList<RosterEntry>> RosterChanged;
    public event Action<string> JoinCodeChanged;
    public event Action<string> ErrorRaised;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance == null)
        {
            new GameObject("Multiplayer Session").AddComponent<MultiplayerSessionController>();
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildNetworkManager();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public async Task HostAsync(string displayName)
    {
        if (State != MultiplayerSessionState.Offline && State != MultiplayerSessionState.Failed)
        {
            return;
        }

        try
        {
            SetState(MultiplayerSessionState.Authenticating);
            await EnsureAuthenticationAsync();
            localDisplayName = SanitizeDisplayName(displayName);
            ConfigureConnectionData();

            SetState(MultiplayerSessionState.Hosting);
            LoadingScreenController.Instance?.Show("ONLINE CO-OP", "CREATING RELAY...");
            LoadingScreenController.Instance?.SetProgress(0.2f);

            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MaximumPlayers - 1);
            JoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            lastJoinCode = JoinCode;
            Debug.Log("[MP] Join code created: " + JoinCode);
            JoinCodeChanged?.Invoke(JoinCode);
            transport.SetRelayServerData(allocation.ToRelayServerData("dtls"));

            RegisterNetworkCallbacks();
            if (!networkManager.StartHost())
            {
                throw new InvalidOperationException("Netcode could not start the host.");
            }
            Debug.Log("[MP] Host started");
            RegisterMessageHandlers();

            roster.Clear();
            roster.Add(new RosterEntry(NetworkManager.ServerClientId, localPlayerId, localDisplayName, true));
            NotifyRosterChanged();
            SetState(MultiplayerSessionState.Lobby);
            LoadingScreenController.Instance?.SetProgress(1f);
            LoadingScreenController.Instance?.Hide();
        }
        catch (Exception exception)
        {
            Fail("Unable to host: " + exception.Message);
        }
    }

    public async Task JoinAsync(string joinCode, string displayName)
    {
        if (State != MultiplayerSessionState.Offline && State != MultiplayerSessionState.Failed)
        {
            return;
        }

        string normalizedCode = NormalizeJoinCode(joinCode);
        if (normalizedCode.Length < 4)
        {
            Fail("Enter a valid Relay join code.");
            return;
        }

        try
        {
            SetState(MultiplayerSessionState.Authenticating);
            await EnsureAuthenticationAsync();
            localDisplayName = SanitizeDisplayName(displayName);
            lastJoinCode = normalizedCode;
            ConfigureConnectionData();

            SetState(MultiplayerSessionState.Joining);
            LoadingScreenController.Instance?.Show("ONLINE CO-OP", "JOINING RELAY...");
            LoadingScreenController.Instance?.SetProgress(0.2f);

            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(normalizedCode);
            transport.SetRelayServerData(allocation.ToRelayServerData("dtls"));
            RegisterNetworkCallbacks();

            connectionCompletion = new TaskCompletionSource<bool>();
            if (!networkManager.StartClient())
            {
                throw new InvalidOperationException("Netcode could not start the client.");
            }
            RegisterMessageHandlers();

            Task completedTask = await Task.WhenAny(connectionCompletion.Task, Task.Delay(TimeSpan.FromSeconds(15)));
            if (completedTask != connectionCompletion.Task || !connectionCompletion.Task.Result)
            {
                throw new TimeoutException("The host did not accept the connection.");
            }

            JoinCode = normalizedCode;
            JoinCodeChanged?.Invoke(JoinCode);
            SetState(MultiplayerSessionState.Lobby);
            LoadingScreenController.Instance?.SetProgress(1f);
            LoadingScreenController.Instance?.Hide();
        }
        catch (Exception exception)
        {
            networkManager.Shutdown();
            Fail("Unable to join: " + exception.Message);
        }
    }

    public async Task ReconnectAsync()
    {
        if (string.IsNullOrWhiteSpace(lastJoinCode))
        {
            Fail("No previous session is available to reconnect.");
            return;
        }

        string reconnectCode = lastJoinCode;
        string reconnectName = localDisplayName;
        networkManager.Shutdown();
        ResetTransientState(false);
        SetState(MultiplayerSessionState.Reconnecting);
        await Task.Yield();
        SetState(MultiplayerSessionState.Offline);
        await JoinAsync(reconnectCode, reconnectName);
    }

    public Task StartMatchAsync()
    {
        if (!IsHost || State != MultiplayerSessionState.Lobby)
        {
            return Task.CompletedTask;
        }

        rosterLocked = true;
        lockedRoster.Clear();
        foreach (RosterEntry entry in roster)
        {
            lockedRoster.Add(entry.playerId);
        }

        SetState(MultiplayerSessionState.Loading);
        LoadingScreenController.Instance?.Show("SCHOOL OF THE DEAD", "SYNCHRONIZING SURVIVORS...");
        LoadingScreenController.Instance?.SetProgress(0.35f);
        SendMatchStartMessage();

        SceneEventProgressStatus result = networkManager.SceneManager.LoadScene(GameplayScene, LoadSceneMode.Single);
        if (result != SceneEventProgressStatus.Started)
        {
            Fail("The network scene load could not start.");
        }
        else
        {
            Debug.Log("[MP] Network scene load started: " + GameplayScene);
        }

        return Task.CompletedTask;
    }

    public Task LeaveAsync()
    {
        if (networkManager != null && networkManager.IsListening)
        {
            networkManager.Shutdown();
        }

        ResetTransientState(true);
        SetState(MultiplayerSessionState.Offline);
        if (SceneManager.GetActiveScene().name != MenuScene)
        {
            SceneManager.LoadScene(MenuScene);
        }

        return Task.CompletedTask;
    }

    public static string SanitizeDisplayName(string value)
    {
        string cleaned = string.IsNullOrWhiteSpace(value) ? "Survivor" : value.Trim();
        cleaned = new string(cleaned.Where(character => !char.IsControl(character)).ToArray());
        if (cleaned.Length > 16)
        {
            cleaned = cleaned.Substring(0, 16);
        }

        while (cleaned.Length < 3)
        {
            cleaned += "_";
        }

        return cleaned;
    }

    private async Task EnsureAuthenticationAsync()
    {
        if (UnityServices.State == ServicesInitializationState.Uninitialized)
        {
            await UnityServices.InitializeAsync();
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        localPlayerId = AuthenticationService.Instance.PlayerId;
    }

    private void BuildNetworkManager()
    {
        NetworkManager existingManager = FindAnyObjectByType<NetworkManager>();
        if (existingManager != null)
        {
            networkManager = existingManager;
            transport = existingManager.GetComponent<UnityTransport>();
        }
        else
        {
            GameObject networkObject = new GameObject("Network Bootstrap");
            DontDestroyOnLoad(networkObject);
            transport = networkObject.AddComponent<UnityTransport>();
            networkManager = networkObject.AddComponent<NetworkManager>();
        }

        if (transport == null)
        {
            transport = networkManager.gameObject.AddComponent<UnityTransport>();
        }

        // A NetworkManager created from code (AddComponent) has no serialized
        // NetworkConfig - it is only populated by the Inspector on a prefab/scene
        // object. Without this guard "networkManager.NetworkConfig" is null and the
        // next line throws a NullReferenceException, so make sure one exists.
        if (networkManager.NetworkConfig == null)
        {
            networkManager.NetworkConfig = new NetworkConfig();
        }

        NetworkConfig config = networkManager.NetworkConfig;
        config.NetworkTransport = transport;
        config.ConnectionApproval = true;
        config.EnableSceneManagement = true;
        config.PlayerPrefab = Resources.Load<GameObject>("NetworkPlayer");
        if (config.PlayerPrefab == null)
        {
            Debug.LogError("[MP] Resources/NetworkPlayer.prefab is missing — players cannot spawn. " +
                "Let the editor rebuild it (Tools > School of the Dead > Refresh Network Player Prefab).");
        }

        // Register the networked zombie so the server can spawn it and clients replicate it.
        // NetworkZombieSetup auto-creates Assets/Resources/NetworkZombie.prefab in the editor.
        GameObject networkZombie = Resources.Load<GameObject>("NetworkZombie");
        if (networkZombie == null)
        {
            Debug.LogWarning("[MP] Resources/NetworkZombie.prefab not found — zombies will not replicate " +
                "in multiplayer. Use Tools > School of the Dead > Refresh Network Zombie Prefab.");
        }
        else if (networkZombie.GetComponent<NetworkObject>() == null)
        {
            Debug.LogWarning("[MP] Resources/NetworkZombie.prefab has no NetworkObject — it cannot be a " +
                "network prefab. Add NetworkObject + NetworkTransform to the source zombie prefab.");
        }
        else
        {
            networkManager.AddNetworkPrefab(networkZombie);
        }

        networkManager.ConnectionApprovalCallback = ApproveConnection;
    }

    private void RegisterNetworkCallbacks()
    {
        if (!handlersRegistered)
        {
            networkManager.OnClientConnectedCallback += OnClientConnected;
            networkManager.OnClientDisconnectCallback += OnClientDisconnected;
            handlersRegistered = true;
        }
    }

    private void RegisterMessageHandlers()
    {
        CustomMessagingManager messaging = networkManager.CustomMessagingManager;
        if (messaging == null)
        {
            throw new InvalidOperationException("Netcode messaging was not initialized.");
        }

        messaging.UnregisterNamedMessageHandler(RosterMessage);
        messaging.UnregisterNamedMessageHandler(MatchStartMessage);
        messaging.RegisterNamedMessageHandler(RosterMessage, ReceiveRosterMessage);
        messaging.RegisterNamedMessageHandler(MatchStartMessage, ReceiveMatchStartMessage);

        // Spawn each client's player body only after the gameplay scene finishes
        // loading for them (so no player exists while in the menu/lobby).
        if (networkManager.SceneManager != null)
        {
            networkManager.SceneManager.OnLoadComplete -= HandleNetworkSceneLoadComplete;
            networkManager.SceneManager.OnLoadComplete += HandleNetworkSceneLoadComplete;
        }
    }

    private void HandleNetworkSceneLoadComplete(ulong clientId, string sceneName, LoadSceneMode loadSceneMode)
    {
        if (networkManager == null || !networkManager.IsServer || sceneName != GameplayScene)
        {
            return;
        }

        SpawnPlayerObject(clientId);

        // Catch this just-loaded client up to the authoritative round + team points.
        RoundManager.Instance?.SendRoundToClient(clientId);
        PlayerPoints.Instance?.SendPointsToClient(clientId);
        NetworkGameplayCoordinator.SendGameplaySnapshotToClient(clientId);
    }

    private void SpawnPlayerObject(ulong clientId)
    {
        if (networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
            client.PlayerObject != null && client.PlayerObject.IsSpawned)
        {
            spawnedPlayers.Add(clientId);
            Debug.Log("[MP] Player already exists for clientId=" + clientId + "; skipping duplicate spawn.");
            return;
        }

        if (spawnedPlayers.Contains(clientId))
        {
            return;
        }

        GameObject prefab = networkManager.NetworkConfig.PlayerPrefab;
        if (prefab == null)
        {
            return;
        }

        GameObject instance = Instantiate(prefab);
        instance.name = "NetworkPlayer (" + clientId + ")";
        NetworkObject netObj = instance.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Destroy(instance);
            return;
        }

        netObj.SpawnAsPlayerObject(clientId, true);
        spawnedPlayers.Add(clientId);
        Debug.Log("[MP] Player spawned for clientId=" + clientId);
    }

    private void ConfigureConnectionData()
    {
        ConnectionPayload payload = new ConnectionPayload
        {
            playerId = localPlayerId,
            displayName = localDisplayName
        };
        networkManager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
    }

    private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        ConnectionPayload payload = DecodePayload(request.Payload);
        bool isDuplicate = roster.Any(entry => entry.connected && entry.playerId == payload.playerId);
        bool isReconnect = rosterLocked && lockedRoster.Contains(payload.playerId);
        bool reconnectExpired = isReconnect && disconnectedAt.TryGetValue(payload.playerId, out float time) &&
                                Time.realtimeSinceStartup - time > ReconnectWindowSeconds;
        bool isFull = roster.Count(entry => entry.connected) >= MaximumPlayers;

        response.Approved = !string.IsNullOrWhiteSpace(payload.playerId) &&
                            !isDuplicate &&
                            !reconnectExpired &&
                            (!rosterLocked || isReconnect) &&
                            (!isFull || isReconnect);
        // Do NOT auto-create the player object on connection. Otherwise hosting from
        // the menu immediately spawns a first-person player in the MENU scene. Players
        // are spawned by the server only once the gameplay scene has loaded
        // (see HandleNetworkSceneLoadComplete).
        response.CreatePlayerObject = false;
        response.Pending = false;
        response.Reason = response.Approved ? string.Empty : GetRejectionReason(isDuplicate, reconnectExpired, isFull);

        if (response.Approved)
        {
            pendingPayloads[request.ClientNetworkId] = payload;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (networkManager.IsServer && clientId != NetworkManager.ServerClientId && pendingPayloads.TryGetValue(clientId, out ConnectionPayload payload))
        {
            int existingIndex = roster.FindIndex(entry => entry.playerId == payload.playerId);
            RosterEntry entry = new RosterEntry(clientId, payload.playerId, payload.displayName, true);
            if (existingIndex >= 0)
            {
                roster[existingIndex] = entry;
            }
            else
            {
                roster.Add(entry);
            }

            disconnectedAt.Remove(payload.playerId);
            pendingPayloads.Remove(clientId);
            Debug.Log("[MP] Client joined: clientId=" + clientId + " name=" + payload.displayName);
            BroadcastRoster();
        }

        if (!networkManager.IsServer && clientId == networkManager.LocalClientId)
        {
            Debug.Log("[MP] Connected to host (local clientId=" + clientId + ")");
            connectionCompletion?.TrySetResult(true);
            LoadingScreenController.Instance?.SetStatus("CONNECTED. SYNCING LOBBY...");
            LoadingScreenController.Instance?.SetProgress(0.75f);
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (networkManager != null && networkManager.IsServer)
        {
            int index = roster.FindIndex(entry => entry.clientId == clientId);
            if (index >= 0)
            {
                RosterEntry entry = roster[index];
                if (rosterLocked)
                {
                    entry.connected = false;
                    roster[index] = entry;
                    disconnectedAt[entry.playerId] = Time.realtimeSinceStartup;
                }
                else
                {
                    roster.RemoveAt(index);
                }

                BroadcastRoster();
            }

            // Allow a re-spawn if this client comes back.
            spawnedPlayers.Remove(clientId);
            return;
        }

        if (clientId == networkManager.LocalClientId)
        {
            connectionCompletion?.TrySetResult(false);
            if (State == MultiplayerSessionState.InGame || State == MultiplayerSessionState.Loading || State == MultiplayerSessionState.Lobby)
            {
                Fail("The host ended the session.");
                if (SceneManager.GetActiveScene().name != MenuScene)
                {
                    SceneManager.LoadScene(MenuScene);
                }
            }
        }
    }

    private void BroadcastRoster()
    {
        NotifyRosterChanged();

        // Only a live, listening server can send messages. During shutdown (e.g. on
        // play-mode exit) CustomMessagingManager is gone, so guard against it.
        if (networkManager == null || !networkManager.IsServer || !networkManager.IsListening ||
            networkManager.CustomMessagingManager == null)
        {
            return;
        }

        RosterEnvelope envelope = new RosterEnvelope { entries = roster.ToArray() };
        FixedString4096Bytes json = new FixedString4096Bytes(JsonUtility.ToJson(envelope));
        using FastBufferWriter writer = new FastBufferWriter(4096, Allocator.Temp);
        writer.WriteValueSafe(json);
        networkManager.CustomMessagingManager.SendNamedMessageToAll(RosterMessage, writer, NetworkDelivery.ReliableSequenced);
    }

    private void ReceiveRosterMessage(ulong senderId, FastBufferReader reader)
    {
        if (networkManager.IsServer)
        {
            return;
        }

        reader.ReadValueSafe(out FixedString4096Bytes value);
        RosterEnvelope envelope = JsonUtility.FromJson<RosterEnvelope>(value.ToString());
        roster.Clear();
        if (envelope?.entries != null)
        {
            roster.AddRange(envelope.entries);
        }
        NotifyRosterChanged();
    }

    private void SendMatchStartMessage()
    {
        using FastBufferWriter writer = new FastBufferWriter(1, Allocator.Temp);
        writer.WriteByteSafe(1);
        networkManager.CustomMessagingManager.SendNamedMessageToAll(MatchStartMessage, writer, NetworkDelivery.ReliableSequenced);
    }

    private void ReceiveMatchStartMessage(ulong senderId, FastBufferReader reader)
    {
        if (networkManager.IsServer)
        {
            return;
        }

        SetState(MultiplayerSessionState.Loading);
        LoadingScreenController.Instance?.Show("SCHOOL OF THE DEAD", "HOST STARTED THE MATCH...");
        LoadingScreenController.Instance?.SetProgress(0.35f);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // There must be exactly ONE NetworkManager. We create the runtime one
        // (DontDestroyOnLoad), so destroy any extra NetworkManager placed in a loaded
        // scene (solo and multiplayer alike) to avoid the "multiple NetworkManager" conflict.
        if (NetworkManager.Singleton != null)
        {
            NetworkManager[] managers = FindObjectsByType<NetworkManager>(FindObjectsSortMode.None);
            foreach (NetworkManager manager in managers)
            {
                if (manager != null && manager != NetworkManager.Singleton)
                {
                    Debug.LogWarning("[MP] Destroying a duplicate NetworkManager found in scene '" + scene.name + "'.");
                    Destroy(manager.gameObject);
                }
            }
        }

        if (scene.name != GameplayScene || networkManager == null || !networkManager.IsListening)
        {
            return;
        }

        // Disable any pre-placed player left in the scene so it doesn't fight the
        // network-spawned players. Plain (non-networked) scene players are safe to hide;
        // a pre-placed NETWORK player can't be cleanly handled here, so warn to remove it.
        PlayerMovement[] scenePlayers = FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None);
        foreach (PlayerMovement scenePlayer in scenePlayers)
        {
            if (scenePlayer == null)
            {
                continue;
            }
            NetworkObject netObj = scenePlayer.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                scenePlayer.gameObject.SetActive(false); // solo/scene player, not for MP
            }
            else if (netObj.IsSceneObject == true)
            {
                Debug.LogWarning("[MP] A network player is pre-placed in the scene ('" + scenePlayer.name +
                    "'). Remove it from SchoolOfTheDead — pre-placed network players conflict with " +
                    "the players spawned for each connected client.");
            }
        }

        LoadingScreenController.Instance?.SetStatus("SURVIVORS READY");
        LoadingScreenController.Instance?.SetProgress(1f);
        LoadingScreenController.Instance?.Hide();
        SetState(MultiplayerSessionState.InGame);
    }

    private void NotifyRosterChanged()
    {
        Debug.Log("[MP] Roster updated: " + roster.Count + " entries");
        RosterChanged?.Invoke(roster.ToArray());
    }

    private void SetState(MultiplayerSessionState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    private void Fail(string message)
    {
        LastError = message;
        SetState(MultiplayerSessionState.Failed);
        ErrorRaised?.Invoke(message);
        LoadingScreenController.Instance?.ShowError(message);
    }

    private void ResetTransientState(bool clearReconnectData)
    {
        roster.Clear();
        pendingPayloads.Clear();
        lockedRoster.Clear();
        disconnectedAt.Clear();
        spawnedPlayers.Clear();
        rosterLocked = false;
        JoinCode = string.Empty;
        JoinCodeChanged?.Invoke(string.Empty);
        NotifyRosterChanged();
        if (clearReconnectData)
        {
            lastJoinCode = string.Empty;
        }
    }

    private static ConnectionPayload DecodePayload(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return new ConnectionPayload();
        }

        try
        {
            return JsonUtility.FromJson<ConnectionPayload>(Encoding.UTF8.GetString(bytes)) ?? new ConnectionPayload();
        }
        catch
        {
            return new ConnectionPayload();
        }
    }

    private static string NormalizeJoinCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        return new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }

    private static string GetRejectionReason(bool duplicate, bool reconnectExpired, bool full)
    {
        if (duplicate) return "This player is already connected.";
        if (reconnectExpired) return "The reconnect window has expired.";
        if (full) return "The lobby is full.";
        return "The match has already started.";
    }
}
