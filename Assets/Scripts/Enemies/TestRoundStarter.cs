using UnityEngine;

/// <summary>
/// DEBUG-ONLY helper: kicks off a single round directly on the spawner, bypassing
/// <see cref="RoundManager"/>. Disabled by default and auto-skips when a RoundManager
/// is present, so it can never compete with the real round flow (which would clear the
/// alive list / desync round-clear detection).
/// </summary>
public class TestRoundStarter : MonoBehaviour
{
    [Tooltip("DEBUG ONLY. Leave OFF in the real game - RoundManager drives rounds. " +
             "If a RoundManager exists in the scene this is ignored even when on.")]
    public bool enableTestRound = false;

    public ZombieSpawner spawner;

    void Start()
    {
        if (!enableTestRound)
        {
            return;
        }

        // Never fight the real round system.
        if (FindFirstObjectByType<RoundManager>() != null)
        {
            Debug.LogWarning("[TestRoundStarter] RoundManager present - skipping test round to avoid conflict.");
            return;
        }

        if (spawner != null)
        {
            spawner.BeginRound(6, 150, 3.5f);
        }
    }
}
