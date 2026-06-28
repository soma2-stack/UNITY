using UnityEngine;

/// <summary>
/// The PACK-A-PUNCH machine (Call of Duty Zombies style). Press E (costs 5000) to
/// upgrade the currently equipped weapon via
/// <see cref="WeaponController.UpgradeCurrentWeapon"/> (~2x damage, refilled ammo,
/// renamed with a trailing " +").
///
/// Requires the map power to be on. Optionally gated behind the secret-book easter
/// egg (<see cref="SecretBookManager.Unlocked"/>) — shows "locked" until then.
/// </summary>
public class PackAPunchMachine : InteractableBase
{
    [Header("Pack-a-Punch")]
    [Tooltip("Cost to upgrade the current weapon.")]
    public int cost = 5000;
    [Tooltip("Require the map power to be on before it can be used.")]
    public bool requirePower = true;
    [Tooltip("Only works once the secret-book easter egg is solved (SecretBookManager.Unlocked).")]
    public bool requireSecretBooks = false;

    protected override string GetPromptText()
    {
        if (requirePower && !PowerState.IsOn)
        {
            return "Pack-a-Punch   (turn on power)";
        }
        if (requireSecretBooks && !IsBooksUnlocked())
        {
            return "Pack-a-Punch   (locked)";
        }
        return "Press E   Pack-a-Punch   [" + cost + "]";
    }

    protected override void OnInteract()
    {
        if (requirePower && !PowerState.IsOn)
        {
            Debug.Log("[PackAPunch] Power is off.");
            return;
        }
        if (requireSecretBooks && !IsBooksUnlocked())
        {
            Debug.Log("[PackAPunch] Locked (collect the secret books first).");
            return;
        }

        WeaponController wc = LocalPlayer.Weapon;
        if (wc == null || !wc.HasWeapon)
        {
            Debug.LogWarning("[PackAPunch] No equipped weapon to upgrade.");
            return;
        }

        if (!TryCharge(cost))
        {
            return;
        }

        wc.UpgradeCurrentWeapon();
        Debug.Log("[PackAPunch] Upgraded weapon: " + wc.CurrentWeaponName);
    }

    private static bool IsBooksUnlocked()
    {
        return SecretBookManager.Instance != null && SecretBookManager.Instance.Unlocked;
    }
}
