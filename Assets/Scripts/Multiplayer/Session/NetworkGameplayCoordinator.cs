using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Small named-message bridge for scene gameplay objects that are not network prefabs
/// (doors, power, purchases, power-ups, game over). Solo mode keeps using direct calls.
/// </summary>
public sealed class NetworkGameplayCoordinator : MonoBehaviour
{
    private const string DoorRequestMessage = "SOTD_DOOR_REQUEST";
    private const string DoorSyncMessage = "SOTD_DOOR_SYNC";
    private const string PowerRequestMessage = "SOTD_POWER_REQUEST";
    private const string PowerSyncMessage = "SOTD_POWER_SYNC";
    private const string PurchaseRequestMessage = "SOTD_PURCHASE_REQUEST";
    private const string PurchaseGrantMessage = "SOTD_PURCHASE_GRANT";
    private const string BoxTransformMessage = "SOTD_BOX_TRANSFORM";
    private const string PerkStateMessage = "SOTD_PERK_STATE";
    private const string PowerupSpawnMessage = "SOTD_POWERUP_SPAWN";
    private const string PowerupCollectRequestMessage = "SOTD_POWERUP_COLLECT_REQUEST";
    private const string PowerupCollectedMessage = "SOTD_POWERUP_COLLECTED";
    private const string PowerupEffectMessage = "SOTD_POWERUP_EFFECT";
    private const string GameOverMessage = "SOTD_GAME_OVER";

    private enum PurchaseKind : byte
    {
        MysteryBox = 1,
        WallBuy = 2,
        PackAPunch = 3,
        Perk = 4,
    }

    private static NetworkGameplayCoordinator instance;
    private static NetworkManager registeredManager;
    private static bool registered;

    private int nextPowerupId = 1;

    private static bool NetworkActive =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    private static bool IsServerRole =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    public static bool IsNetworkActive => NetworkActive;
    public static bool IsServer => IsServerRole;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        Ensure();
    }

    public static NetworkGameplayCoordinator Ensure()
    {
        if (instance != null)
        {
            instance.TryRegisterMessages();
            return instance;
        }

        GameObject go = new GameObject("Network Gameplay Coordinator");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<NetworkGameplayCoordinator>();
        instance.TryRegisterMessages();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        if (instance == this)
        {
            instance = null;
        }
        UnregisterMessages();
    }

    private void Update()
    {
        TryRegisterMessages();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TryRegisterMessages();
    }

    private void TryRegisterMessages()
    {
        if (!NetworkActive || NetworkManager.Singleton.CustomMessagingManager == null)
        {
            return;
        }

        if (registered && registeredManager == NetworkManager.Singleton)
        {
            return;
        }

        UnregisterMessages();
        CustomMessagingManager messaging = NetworkManager.Singleton.CustomMessagingManager;
        messaging.RegisterNamedMessageHandler(DoorRequestMessage, ReceiveDoorRequest);
        messaging.RegisterNamedMessageHandler(DoorSyncMessage, ReceiveDoorSync);
        messaging.RegisterNamedMessageHandler(PowerRequestMessage, ReceivePowerRequest);
        messaging.RegisterNamedMessageHandler(PowerSyncMessage, ReceivePowerSync);
        messaging.RegisterNamedMessageHandler(PurchaseRequestMessage, ReceivePurchaseRequest);
        messaging.RegisterNamedMessageHandler(PurchaseGrantMessage, ReceivePurchaseGrant);
        messaging.RegisterNamedMessageHandler(BoxTransformMessage, ReceiveBoxTransform);
        messaging.RegisterNamedMessageHandler(PerkStateMessage, ReceivePerkState);
        messaging.RegisterNamedMessageHandler(PowerupSpawnMessage, ReceivePowerupSpawn);
        messaging.RegisterNamedMessageHandler(PowerupCollectRequestMessage, ReceivePowerupCollectRequest);
        messaging.RegisterNamedMessageHandler(PowerupCollectedMessage, ReceivePowerupCollected);
        messaging.RegisterNamedMessageHandler(PowerupEffectMessage, ReceivePowerupEffect);
        messaging.RegisterNamedMessageHandler(GameOverMessage, ReceiveGameOver);
        registered = true;
        registeredManager = NetworkManager.Singleton;
    }

    private static void UnregisterMessages()
    {
        if (!registered || registeredManager == null || registeredManager.CustomMessagingManager == null)
        {
            registered = false;
            registeredManager = null;
            return;
        }

        CustomMessagingManager messaging = registeredManager.CustomMessagingManager;
        messaging.UnregisterNamedMessageHandler(DoorRequestMessage);
        messaging.UnregisterNamedMessageHandler(DoorSyncMessage);
        messaging.UnregisterNamedMessageHandler(PowerRequestMessage);
        messaging.UnregisterNamedMessageHandler(PowerSyncMessage);
        messaging.UnregisterNamedMessageHandler(PurchaseRequestMessage);
        messaging.UnregisterNamedMessageHandler(PurchaseGrantMessage);
        messaging.UnregisterNamedMessageHandler(BoxTransformMessage);
        messaging.UnregisterNamedMessageHandler(PerkStateMessage);
        messaging.UnregisterNamedMessageHandler(PowerupSpawnMessage);
        messaging.UnregisterNamedMessageHandler(PowerupCollectRequestMessage);
        messaging.UnregisterNamedMessageHandler(PowerupCollectedMessage);
        messaging.UnregisterNamedMessageHandler(PowerupEffectMessage);
        messaging.UnregisterNamedMessageHandler(GameOverMessage);
        registered = false;
        registeredManager = null;
    }

    public static void RequestDoorOpen(Door door)
    {
        if (door == null)
        {
            return;
        }

        Ensure();
        if (!NetworkActive)
        {
            door.TryOpenOffline();
            return;
        }

        if (IsServerRole)
        {
            instance.ServerTryOpenDoor(NetworkManager.Singleton.LocalClientId, door.NetworkKey);
            return;
        }

        using FastBufferWriter writer = new FastBufferWriter(512, Allocator.Temp);
        WriteString(writer, door.NetworkKey);
        SendToServer(DoorRequestMessage, writer);
    }

    private void ReceiveDoorRequest(ulong senderId, FastBufferReader reader)
    {
        if (!IsServerRole)
        {
            return;
        }

        string key = ReadString(reader);
        ServerTryOpenDoor(senderId, key);
    }

    private void ServerTryOpenDoor(ulong senderClientId, string key)
    {
        if (!Door.TryFind(key, out Door door) || door.IsOpen)
        {
            return;
        }

        int cost = Mathf.Max(0, door.Cost);
        if (cost > 0 && PlayerPoints.Instance != null && !PlayerPoints.Instance.TrySpend(cost))
        {
            Debug.Log("[Door] Client " + senderClientId + " could not afford door '" + key + "'.");
            return;
        }

        door.OpenFromNetwork();
        int reward = Mathf.RoundToInt(cost / 10f);
        if (reward > 0)
        {
            PlayerPoints.Instance?.AddPoints(reward);
        }
        BroadcastDoorOpen(key);
    }

    public static void BroadcastDoorOpen(string key)
    {
        if (!NetworkActive || !IsServerRole)
        {
            return;
        }

        using FastBufferWriter writer = new FastBufferWriter(512, Allocator.Temp);
        WriteString(writer, key);
        writer.WriteValueSafe(true);
        SendToAll(DoorSyncMessage, writer);
    }

    private void ReceiveDoorSync(ulong senderId, FastBufferReader reader)
    {
        string key = ReadString(reader);
        reader.ReadValueSafe(out bool open);
        if (open && Door.TryFind(key, out Door door))
        {
            door.OpenFromNetwork();
        }
    }

    public static void RequestPowerOn(PowerSwitch powerSwitch)
    {
        if (powerSwitch == null)
        {
            return;
        }

        Ensure();
        if (!NetworkActive)
        {
            powerSwitch.TryTurnOnOffline();
            return;
        }

        if (IsServerRole)
        {
            instance.ServerTryPowerOn(NetworkManager.Singleton.LocalClientId, powerSwitch.NetworkKey);
            return;
        }

        using FastBufferWriter writer = new FastBufferWriter(512, Allocator.Temp);
        WriteString(writer, powerSwitch.NetworkKey);
        SendToServer(PowerRequestMessage, writer);
    }

    private void ReceivePowerRequest(ulong senderId, FastBufferReader reader)
    {
        if (!IsServerRole)
        {
            return;
        }

        ServerTryPowerOn(senderId, ReadString(reader));
    }

    private void ServerTryPowerOn(ulong senderClientId, string key)
    {
        if (PowerState.IsOn)
        {
            SendPowerToClient(senderClientId);
            return;
        }

        if (!InteractableBase.TryFind(key, out PowerSwitch powerSwitch))
        {
            return;
        }

        int cost = Mathf.Max(0, powerSwitch.cost);
        if (cost > 0 && PlayerPoints.Instance != null && !PlayerPoints.Instance.TrySpend(cost))
        {
            return;
        }

        PowerState.ApplyNetworkState(true);
        BroadcastPower();
        Debug.Log("[PowerSwitch] Power is now ON by client " + senderClientId + ".");
    }

    public static void BroadcastPower()
    {
        if (!NetworkActive || !IsServerRole)
        {
            return;
        }

        using FastBufferWriter writer = new FastBufferWriter(sizeof(bool), Allocator.Temp);
        writer.WriteValueSafe(PowerState.IsOn);
        SendToAll(PowerSyncMessage, writer);
    }

    private void SendPowerToClient(ulong clientId)
    {
        if (!NetworkActive || !IsServerRole)
        {
            return;
        }

        using FastBufferWriter writer = new FastBufferWriter(sizeof(bool), Allocator.Temp);
        writer.WriteValueSafe(PowerState.IsOn);
        SendToClient(PowerSyncMessage, clientId, writer);
    }

    private void ReceivePowerSync(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out bool isOn);
        PowerState.ApplyNetworkState(isOn);
    }

    public static void RequestMysteryBox(MysteryBox box)
    {
        RequestPurchase(PurchaseKind.MysteryBox, box != null ? box.NetworkKey : string.Empty, 0);
    }

    public static void RequestWallBuy(WallBuy wallBuy, bool ownsWeapon)
    {
        RequestPurchase(PurchaseKind.WallBuy, wallBuy != null ? wallBuy.NetworkKey : string.Empty, ownsWeapon ? 1 : 0);
    }

    public static void RequestPackAPunch(PackAPunchMachine machine)
    {
        RequestPurchase(PurchaseKind.PackAPunch, machine != null ? machine.NetworkKey : string.Empty, 0);
    }

    public static void RequestPerk(PerkMachine machine)
    {
        RequestPurchase(PurchaseKind.Perk, machine != null ? machine.NetworkKey : string.Empty, 0);
    }

    private static void RequestPurchase(PurchaseKind kind, string key, int aux)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        Ensure();
        if (!NetworkActive)
        {
            ApplyLocalPurchase(kind, key, aux);
            return;
        }

        if (IsServerRole)
        {
            instance.ServerTryPurchase(NetworkManager.Singleton.LocalClientId, kind, key, aux);
            return;
        }

        using FastBufferWriter writer = new FastBufferWriter(512, Allocator.Temp);
        writer.WriteValueSafe((byte)kind);
        WriteString(writer, key);
        writer.WriteValueSafe(aux);
        SendToServer(PurchaseRequestMessage, writer);
    }

    private void ReceivePurchaseRequest(ulong senderId, FastBufferReader reader)
    {
        if (!IsServerRole)
        {
            return;
        }

        reader.ReadValueSafe(out byte kindValue);
        string key = ReadString(reader);
        reader.ReadValueSafe(out int aux);
        ServerTryPurchase(senderId, (PurchaseKind)kindValue, key, aux);
    }

    private void ServerTryPurchase(ulong senderClientId, PurchaseKind kind, string key, int aux)
    {
        switch (kind)
        {
            case PurchaseKind.MysteryBox:
                ServerTryMysteryBox(senderClientId, key);
                break;
            case PurchaseKind.WallBuy:
                ServerTryWallBuy(senderClientId, key, aux != 0);
                break;
            case PurchaseKind.PackAPunch:
                ServerTryPackAPunch(senderClientId, key);
                break;
            case PurchaseKind.Perk:
                ServerTryPerk(senderClientId, key);
                break;
        }
    }

    private void ServerTryMysteryBox(ulong senderClientId, string key)
    {
        if (!InteractableBase.TryFind(key, out MysteryBox box) ||
            (box.requirePower && !PowerState.IsOn) ||
            !TrySpend(box.cost))
        {
            return;
        }

        bool teddy = box.RollTeddy();
        int weaponIndex = teddy ? -1 : box.RollWeaponIndex();
        if (teddy)
        {
            box.RelocateBox();
            BroadcastBoxTransform(box);
        }

        SendPurchaseGrant(senderClientId, PurchaseKind.MysteryBox, key, weaponIndex);
    }

    private void ServerTryWallBuy(ulong senderClientId, string key, bool ownsWeapon)
    {
        if (!InteractableBase.TryFind(key, out WallBuy wallBuy))
        {
            return;
        }

        int price = ownsWeapon ? wallBuy.ammoCost : wallBuy.buyCost;
        if (!TrySpend(price))
        {
            return;
        }

        SendPurchaseGrant(senderClientId, PurchaseKind.WallBuy, key, ownsWeapon ? 1 : 0);
    }

    private void ServerTryPackAPunch(ulong senderClientId, string key)
    {
        if (!InteractableBase.TryFind(key, out PackAPunchMachine machine) ||
            (machine.requirePower && !PowerState.IsOn) ||
            !TrySpend(machine.cost))
        {
            return;
        }

        SendPurchaseGrant(senderClientId, PurchaseKind.PackAPunch, key, 0);
    }

    private void ServerTryPerk(ulong senderClientId, string key)
    {
        if (!PerkMachine.TryFind(key, out PerkMachine machine) ||
            !PowerState.IsOn ||
            PerkManager.ClientHasPerk(senderClientId, machine.perk) ||
            !TrySpend(machine.cost))
        {
            return;
        }

        PerkManager.ServerGrantClientPerk(senderClientId, machine.perk);
        ApplyServerPerkEffect(senderClientId, machine.perk);
        SendPurchaseGrant(senderClientId, PurchaseKind.Perk, key, (int)machine.perk);
        BroadcastPerkState(senderClientId, machine.perk, true);
    }

    private static bool TrySpend(int cost)
    {
        return cost <= 0 || PlayerPoints.Instance == null || PlayerPoints.Instance.TrySpend(cost);
    }

    private static void ApplyLocalPurchase(PurchaseKind kind, string key, int aux)
    {
        switch (kind)
        {
            case PurchaseKind.MysteryBox:
                if (InteractableBase.TryFind(key, out MysteryBox box))
                {
                    box.ApplyMysteryResult(aux);
                }
                break;
            case PurchaseKind.WallBuy:
                if (InteractableBase.TryFind(key, out WallBuy wallBuy))
                {
                    wallBuy.ApplyPurchaseResult(aux != 0);
                }
                break;
            case PurchaseKind.PackAPunch:
                LocalPlayer.Weapon?.UpgradeCurrentWeapon();
                break;
            case PurchaseKind.Perk:
                PerkManager.Instance?.TryGrant((PerkType)aux);
                break;
        }
    }

    private void SendPurchaseGrant(ulong clientId, PurchaseKind kind, string key, int aux)
    {
        if (NetworkActive && IsServerRole && clientId != NetworkManager.Singleton.LocalClientId)
        {
            using FastBufferWriter writer = new FastBufferWriter(512, Allocator.Temp);
            writer.WriteValueSafe((byte)kind);
            WriteString(writer, key);
            writer.WriteValueSafe(aux);
            SendToClient(PurchaseGrantMessage, clientId, writer);
            return;
        }

        ApplyLocalPurchase(kind, key, aux);
    }

    private void ReceivePurchaseGrant(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte kindValue);
        string key = ReadString(reader);
        reader.ReadValueSafe(out int aux);
        ApplyLocalPurchase((PurchaseKind)kindValue, key, aux);
    }

    public static void BroadcastBoxTransform(MysteryBox box)
    {
        if (box == null || !NetworkActive || !IsServerRole)
        {
            return;
        }

        using FastBufferWriter writer = new FastBufferWriter(1024, Allocator.Temp);
        WriteString(writer, box.NetworkKey);
        writer.WriteValueSafe(box.transform.position);
        writer.WriteValueSafe(box.transform.rotation);
        SendToAll(BoxTransformMessage, writer);
    }

    private void ReceiveBoxTransform(ulong senderId, FastBufferReader reader)
    {
        string key = ReadString(reader);
        reader.ReadValueSafe(out Vector3 position);
        reader.ReadValueSafe(out Quaternion rotation);
        if (InteractableBase.TryFind(key, out MysteryBox box))
        {
            box.ApplyNetworkTransform(position, rotation);
        }
    }

    private static void ApplyServerPerkEffect(ulong clientId, PerkType perk)
    {
        if (NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client) ||
            client.PlayerObject == null)
        {
            return;
        }

        GameObject player = client.PlayerObject.gameObject;
        switch (perk)
        {
            case PerkType.VitalBoost:
                PlayerHealth health = player.GetComponent<PlayerHealth>();
                PerkManager manager = PerkManager.Instance;
                health?.SetMaxHealth(manager != null ? manager.juggernogMaxHealth : 250, true);
                break;
        }
    }

    public static void SendPerkStateToClient(ulong clientId)
    {
        if (!NetworkActive || !IsServerRole)
        {
            return;
        }

        foreach (KeyValuePair<ulong, HashSet<PerkType>> pair in PerkManager.ServerPerks)
        {
            foreach (PerkType perk in pair.Value)
            {
                SendPerkState(clientId, pair.Key, perk, true);
            }
        }
    }

    private static void BroadcastPerkState(ulong ownerClientId, PerkType perk, bool hasPerk)
    {
        if (!NetworkActive || !IsServerRole)
        {
            return;
        }
        using FastBufferWriter writer = BuildPerkStateWriter(ownerClientId, perk, hasPerk);
        SendToAll(PerkStateMessage, writer);
    }

    private static void SendPerkState(ulong targetClientId, ulong ownerClientId, PerkType perk, bool hasPerk)
    {
        using FastBufferWriter writer = BuildPerkStateWriter(ownerClientId, perk, hasPerk);
        SendToClient(PerkStateMessage, targetClientId, writer);
    }

    private static FastBufferWriter BuildPerkStateWriter(ulong ownerClientId, PerkType perk, bool hasPerk)
    {
        FastBufferWriter writer = new FastBufferWriter(sizeof(ulong) + sizeof(int) + sizeof(bool), Allocator.Temp);
        writer.WriteValueSafe(ownerClientId);
        writer.WriteValueSafe((int)perk);
        writer.WriteValueSafe(hasPerk);
        return writer;
    }

    private void ReceivePerkState(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong ownerClientId);
        reader.ReadValueSafe(out int perkValue);
        reader.ReadValueSafe(out bool hasPerk);
        PerkManager.ApplyNetworkPerkState(ownerClientId, (PerkType)perkValue, hasPerk);
    }

    public static int AllocatePowerupId()
    {
        Ensure();
        return instance.nextPowerupId++;
    }

    public static void BroadcastPowerupSpawn(Powerup powerup)
    {
        if (powerup == null || !NetworkActive || !IsServerRole)
        {
            return;
        }

        SendPowerupSpawnToAll(powerup);
    }

    private static void SendPowerupSpawnToAll(Powerup powerup)
    {
        using FastBufferWriter writer = BuildPowerupSpawnWriter(powerup);
        SendToAll(PowerupSpawnMessage, writer);
    }

    private static void SendPowerupSpawnToClient(ulong clientId, Powerup powerup)
    {
        using FastBufferWriter writer = BuildPowerupSpawnWriter(powerup);
        SendToClient(PowerupSpawnMessage, clientId, writer);
    }

    private static FastBufferWriter BuildPowerupSpawnWriter(Powerup powerup)
    {
        FastBufferWriter writer = new FastBufferWriter(64, Allocator.Temp);
        writer.WriteValueSafe(powerup.NetworkId);
        writer.WriteValueSafe((int)powerup.type);
        writer.WriteValueSafe(powerup.transform.position);
        writer.WriteValueSafe(powerup.RemainingLifetime);
        return writer;
    }

    private void ReceivePowerupSpawn(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int id);
        reader.ReadValueSafe(out int typeValue);
        reader.ReadValueSafe(out Vector3 position);
        reader.ReadValueSafe(out float lifetime);
        if (Powerup.TryFind(id, out _))
        {
            return;
        }
        Powerup.Spawn((PowerupType)typeValue, position, lifetime, id, true);
    }

    public static void RequestPowerupCollect(Powerup powerup)
    {
        if (powerup == null)
        {
            return;
        }

        Ensure();
        if (!NetworkActive)
        {
            powerup.CollectOffline();
            return;
        }

        if (IsServerRole)
        {
            instance.ServerTryCollectPowerup(NetworkManager.Singleton.LocalClientId, powerup.NetworkId);
            return;
        }

        using FastBufferWriter writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(powerup.NetworkId);
        SendToServer(PowerupCollectRequestMessage, writer);
    }

    private void ReceivePowerupCollectRequest(ulong senderId, FastBufferReader reader)
    {
        if (!IsServerRole)
        {
            return;
        }
        reader.ReadValueSafe(out int id);
        ServerTryCollectPowerup(senderId, id);
    }

    private void ServerTryCollectPowerup(ulong senderClientId, int id)
    {
        if (!Powerup.TryFind(id, out Powerup powerup))
        {
            return;
        }

        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.ConnectedClients.TryGetValue(senderClientId, out NetworkClient client) &&
            client.PlayerObject != null)
        {
            float distance = Vector3.Distance(client.PlayerObject.transform.position, powerup.transform.position);
            if (distance > powerup.collectRange + 1.5f)
            {
                return;
            }
        }

        PowerupType type = powerup.type;
        PowerupManager.Instance?.Apply(type, senderClientId);
        BroadcastPowerupCollected(id);
        Destroy(powerup.gameObject);
    }

    public static void BroadcastPowerupCollected(int id)
    {
        if (!NetworkActive || !IsServerRole)
        {
            return;
        }

        using FastBufferWriter writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(id);
        SendToAll(PowerupCollectedMessage, writer);
    }

    private void ReceivePowerupCollected(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int id);
        if (Powerup.TryFind(id, out Powerup powerup))
        {
            Destroy(powerup.gameObject);
        }
    }

    public static void BroadcastPowerupEffect(PowerupType type, float remaining)
    {
        if (!NetworkActive || !IsServerRole)
        {
            return;
        }

        PowerupManager.ApplyNetworkEffect(type, remaining);
        using FastBufferWriter writer = new FastBufferWriter(sizeof(int) + sizeof(float), Allocator.Temp);
        writer.WriteValueSafe((int)type);
        writer.WriteValueSafe(remaining);
        SendToAll(PowerupEffectMessage, writer);
    }

    public static void SendPowerupEffectToClient(ulong clientId, PowerupType type, float remaining)
    {
        if (!NetworkActive || !IsServerRole)
        {
            return;
        }

        using FastBufferWriter writer = new FastBufferWriter(sizeof(int) + sizeof(float), Allocator.Temp);
        writer.WriteValueSafe((int)type);
        writer.WriteValueSafe(remaining);
        SendToClient(PowerupEffectMessage, clientId, writer);
    }

    private void ReceivePowerupEffect(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int typeValue);
        reader.ReadValueSafe(out float remaining);
        PowerupManager.ApplyNetworkEffect((PowerupType)typeValue, remaining);
    }

    public static void BroadcastGameOver(int round, int score, int kills)
    {
        if (!NetworkActive || !IsServerRole)
        {
            GameOverController.ShowLocalGameOver(round, score, kills, false);
            return;
        }

        using FastBufferWriter writer = new FastBufferWriter(sizeof(int) * 3, Allocator.Temp);
        writer.WriteValueSafe(round);
        writer.WriteValueSafe(score);
        writer.WriteValueSafe(kills);
        SendToAll(GameOverMessage, writer);
        GameOverController.ShowLocalGameOver(round, score, kills, true);
    }

    private void ReceiveGameOver(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int round);
        reader.ReadValueSafe(out int score);
        reader.ReadValueSafe(out int kills);
        GameOverController.ShowLocalGameOver(round, score, kills, true);
    }

    public static void SendGameplaySnapshotToClient(ulong clientId)
    {
        if (!NetworkActive || !IsServerRole)
        {
            return;
        }

        Ensure();
        instance.SendPowerToClient(clientId);
        foreach (Door door in Door.AllDoors)
        {
            if (door != null && door.IsOpen)
            {
                using FastBufferWriter writer = new FastBufferWriter(512, Allocator.Temp);
                WriteString(writer, door.NetworkKey);
                writer.WriteValueSafe(true);
                SendToClient(DoorSyncMessage, clientId, writer);
            }
        }
        foreach (MysteryBox box in InteractableBase.FindAll<MysteryBox>())
        {
            if (box == null)
            {
                continue;
            }
            using FastBufferWriter writer = new FastBufferWriter(1024, Allocator.Temp);
            WriteString(writer, box.NetworkKey);
            writer.WriteValueSafe(box.transform.position);
            writer.WriteValueSafe(box.transform.rotation);
            SendToClient(BoxTransformMessage, clientId, writer);
        }
        foreach (Powerup powerup in Powerup.ActiveNetworkedPowerups)
        {
            if (powerup != null)
            {
                SendPowerupSpawnToClient(clientId, powerup);
            }
        }
        PowerupManager.SendActiveEffectsToClient(clientId);
        SendPerkStateToClient(clientId);
    }

    private static void SendToServer(string message, FastBufferWriter writer)
    {
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
            message,
            NetworkManager.ServerClientId,
            writer,
            NetworkDelivery.ReliableSequenced);
    }

    private static void SendToAll(string message, FastBufferWriter writer)
    {
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll(
            message,
            writer,
            NetworkDelivery.ReliableSequenced);
    }

    private static void SendToClient(string message, ulong clientId, FastBufferWriter writer)
    {
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
            message,
            clientId,
            writer,
            NetworkDelivery.ReliableSequenced);
    }

    private static void WriteString(FastBufferWriter writer, string value)
    {
        FixedString512Bytes fixedValue = value ?? string.Empty;
        writer.WriteValueSafe(fixedValue);
    }

    private static string ReadString(FastBufferReader reader)
    {
        reader.ReadValueSafe(out FixedString512Bytes value);
        return value.ToString();
    }
}
