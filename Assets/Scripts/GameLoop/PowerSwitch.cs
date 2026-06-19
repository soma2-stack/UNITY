using UnityEngine;

/// <summary>
/// The map POWER SWITCH (Call of Duty Zombies style). Press E to turn the power on.
/// Once on, the Mystery Box and Pack-a-Punch become usable (they gate on
/// <see cref="PowerState.IsOn"/>). Activating is free by default.
/// </summary>
public class PowerSwitch : InteractableBase
{
    [Header("Power Switch")]
    [Tooltip("Optional cost to flip the switch (usually free / 0).")]
    public int cost = 0;

    protected override string GetPromptText()
    {
        if (PowerState.IsOn)
        {
            return "POWER  (on)";
        }
        return cost > 0
            ? "Press E   Turn On Power   [" + cost + "]"
            : "Press E   Turn On Power";
    }

    protected override void OnInteract()
    {
        if (PowerState.IsOn)
        {
            return;
        }

        if (!TryCharge(cost))
        {
            return;
        }

        PowerState.TurnOn();
        Debug.Log("[PowerSwitch] Power is now ON.");
    }
}
