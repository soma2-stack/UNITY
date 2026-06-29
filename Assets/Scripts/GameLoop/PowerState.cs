using UnityEngine.SceneManagement;

/// <summary>
/// Tiny global flag for whether the map's POWER is on (Call of Duty Zombies style).
/// The Power Switch turns it on; the Mystery Box and Pack-a-Punch require it.
///
/// This is a plain static so any script can read/gate on it without a scene
/// reference. It auto-resets to OFF whenever a scene loads so a fresh run always
/// starts powered down.
/// </summary>
public static class PowerState
{
    /// <summary>True once the player has activated the power switch.</summary>
    public static bool IsOn { get; private set; }

    /// <summary>Turn the power on (idempotent). Called by the Power Switch.</summary>
    public static void TurnOn()
    {
        ApplyNetworkState(true);
        NetworkGameplayCoordinator.BroadcastPower();
    }

    /// <summary>Apply the authoritative power state pushed by the server.</summary>
    public static void ApplyNetworkState(bool isOn)
    {
        IsOn = isOn;
    }

    /// <summary>Reset the power back to off (used on scene (re)load).</summary>
    public static void Reset()
    {
        IsOn = false;
    }

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Reset();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Each (re)loaded scene begins powered down.
        Reset();
    }
}
