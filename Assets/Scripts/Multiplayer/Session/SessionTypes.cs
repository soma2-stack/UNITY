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
    // Reserved character portrait index (host-authoritative), or -1 for none. Serialized in the
    // roster envelope so every client sees who has locked which character.
    public int characterIndex;

    public RosterEntry(ulong clientId, string playerId, string displayName, bool connected)
    {
        this.clientId = clientId;
        this.playerId = playerId;
        this.displayName = displayName;
        this.connected = connected;
        this.characterIndex = -1;
    }
}

[Serializable]
internal sealed class ConnectionPayload
{
    public string playerId;
    public string displayName;
    // The character the joining player picked locally (portrait index), or -1 for none. The host
    // reserves it on connect if it is still free.
    public int characterIndex = -1;
}

[Serializable]
internal sealed class RosterEnvelope
{
    public RosterEntry[] entries;
}
