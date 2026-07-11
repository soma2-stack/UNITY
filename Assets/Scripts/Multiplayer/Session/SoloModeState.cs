/// <summary>
/// Tiny runtime flag marking that the current session is a SINGLE-PLAYER solo run, implemented as a
/// local (offline) Netcode host. Systems that must distinguish a 1-player local host from a real
/// multiplayer host/client — notably <see cref="PauseMenuController"/> — check this so solo still
/// pauses immediately (instead of showing the host "Pause Game" flow).
///
/// Set true by <see cref="MultiplayerSessionController.StartSolo"/> and cleared when the session ends
/// (or when a real online session begins). It changes NO gameplay — only how "solo" is detected.
/// </summary>
public static class SoloModeState
{
    public static bool IsSolo { get; set; }
}
