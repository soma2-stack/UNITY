using UnityEngine;

/// <summary>
/// "False Alarm / Fire Drill" — a small, team-wide points easter egg.
///
/// Pull all <see cref="TotalAlarms"/> fire alarms around the school and every player is awarded
/// <see cref="RewardPoints"/> points. Progress is shared across the whole team, each alarm counts
/// only once, and the reward fires exactly once.
///
/// This class owns the tunable values and the easter egg's log/reward text. The team-wide
/// transport, dedup, once-only guard, late-join replay and per-session reset all live in
/// <see cref="NetworkGameplayCoordinator"/> (mirroring the existing secret-book easter egg), so no
/// multiplayer plumbing is duplicated.
/// </summary>
public static class FireAlarmEasterEgg
{
    /// <summary>Number of unique fire alarms that must be pulled to complete the egg.</summary>
    public const int TotalAlarms = 5;

    /// <summary>Points awarded to every player when the egg completes.</summary>
    public const int RewardPoints = 1500;

    /// <summary>
    /// Ask to activate a fire alarm. Routed through the coordinator so the count is team-wide and
    /// server-authoritative (solo applies it locally).
    /// </summary>
    public static void RequestActivate(FireAlarmInteractable alarm)
    {
        NetworkGameplayCoordinator.RequestFireAlarmActivate(alarm);
    }

    /// <summary>Log that an alarm lit up on this peer (called on every machine).</summary>
    public static void LogAlarmActivated()
    {
        Debug.Log("[FireDrill] Fire alarm activated");
    }

    /// <summary>Log team-wide progress. Server/solo only, where the authoritative count lives.</summary>
    public static void LogProgress(int uniqueCount)
    {
        Debug.Log("[FireDrill] Fire Drill progress " + uniqueCount + "/" + TotalAlarms);
    }

    /// <summary>
    /// Award the reward to every player and log completion. Server/solo only, guarded by the
    /// coordinator so it happens exactly once per session. <see cref="PlayerPoints.AddPointsToAll"/>
    /// is server-authoritative and updates each player's HUD (solo credits the local balance).
    /// </summary>
    public static void AwardCompletion()
    {
        Debug.Log("[FireDrill] Fire Drill Easter egg completed");
        PlayerPoints.Instance?.AddPointsToAll(RewardPoints);
        Debug.Log("[FireDrill] +" + RewardPoints + " points awarded to each player");
    }
}
