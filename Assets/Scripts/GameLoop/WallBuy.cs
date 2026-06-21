using UnityEngine;

/// <summary>
/// A WALL BUY (Call of Duty Zombies style): a specific weapon bolted to a wall.
/// Press E to buy it (full price) the first time, or to refill its ammo (cheaper)
/// once you already own it — handled by <see cref="WeaponController.GiveWeapon"/>,
/// which tops up ammo when the player already has that weapon.
///
/// Configure the weapon's stats in the inspector (or via the editor placer).
/// </summary>
public class WallBuy : InteractableBase
{
    [Header("Wall Buy")]
    [Tooltip("Display name of the weapon sold here.")]
    public string weaponName = "Rifle";
    [Tooltip("Full price to buy the weapon the first time.")]
    public int buyCost = 1500;
    [Tooltip("Cheaper price to refill ammo once owned.")]
    public int ammoCost = 500;

    [Header("Weapon Stats")]
    public int damage = 40;
    public float fireRate = 8f;
    public bool automatic = true;
    public float range = 120f;
    public float spread = 1.5f;
    public int magazineSize = 30;
    public int reserveAmmo = 240;
    public float reloadTime = 2.2f;

    private bool Owns(WeaponController wc)
    {
        if (wc == null || wc.weapons == null)
        {
            return false;
        }
        foreach (Weapon w in wc.weapons)
        {
            if (w != null && w.weaponName == weaponName)
            {
                return true;
            }
        }
        return false;
    }

    protected override string GetPromptText()
    {
        WeaponController wc = FindFirstObjectByType<WeaponController>();
        bool owns = Owns(wc);
        int price = owns ? ammoCost : buyCost;
        string verb = owns ? "Buy Ammo" : "Buy";
        return "Press E   " + verb + " " + weaponName + "   [" + price + "]";
    }

    protected override void OnInteract()
    {
        WeaponController wc = FindFirstObjectByType<WeaponController>();
        if (wc == null)
        {
            Debug.LogWarning("[WallBuy] No WeaponController in scene.");
            return;
        }

        bool owns = Owns(wc);
        int price = owns ? ammoCost : buyCost;

        if (!TryCharge(price))
        {
            return;
        }

        if (owns)
        {
            // Already own it: top up RESERVES only (CoD wall-buy ammo never reloads
            // the current magazine).
            wc.RefillReserveAmmo(weaponName);
            Debug.Log("[WallBuy] Refilled reserves for " + weaponName);
        }
        else
        {
            Weapon weapon = new Weapon
            {
                weaponName = weaponName,
                damage = damage,
                fireRate = fireRate,
                automatic = automatic,
                range = range,
                spread = spread,
                magazineSize = magazineSize,
                reserveAmmo = reserveAmmo,
                reloadTime = reloadTime,
            };
            wc.GiveWeapon(weapon);
            Debug.Log("[WallBuy] Bought " + weaponName);
        }
    }
}
