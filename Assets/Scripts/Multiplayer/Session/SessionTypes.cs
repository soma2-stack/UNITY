using System;

public enum MultiplayerSessionState
{
    Offline,
    Authenticating,
    Hosting,
    Joining,
    Lobby,
    Loading,
    InGame,
    Reconnecting,
    Failed
}

[Serializable]
public struct RosterEntry
{
    public ulong clientId;
    public string playerId;
    public string displayName;
    public bool connected;

    public RosterEntry(ulong clientId, string playerId, string displayName, bool connected)
    {
        this.clientId = clientId;
        this.playerId = playerId;
        this.displayName = displayName;
        this.connected = connected;
    }
}

[Serializable]
internal sealed class ConnectionPayload
{
    public string playerId;
    public string displayName;
}

[Serializable]
internal sealed class RosterEnvelope
{
    public RosterEntry[] entries;
}
