using UnityEngine;

// Plain data class describing a single weapon. NOT a MonoBehaviour.
// Configure each weapon's stats in the WeaponController inspector list.
[System.Serializable]
public class Weapon
{
    [Header("Identity")]
    public string weaponName = "Pistol";

    [Header("Stats")]
    public int damage = 25;
    public float fireRate = 5f;        // Shots per second
    public bool automatic = false;     // Hold to fire (true) vs click per shot (false)
    public float range = 100f;         // Max raycast distance in meters
    public float spread = 1f;          // Cone half-angle in degrees (0 = perfectly accurate)

    [Header("Ammo")]
    public int magazineSize = 12;      // Rounds per magazine
    public int reserveAmmo = 60;       // Rounds held in reserve
    public float reloadTime = 1.5f;    // Seconds to reload

    [Header("Model")]
    public GameObject weaponModel;     // In-hand model enabled when equipped (may be null)

    [Header("Audio")]
    // Fire sound played when this weapon actually fires. Optional: if left null the
    // WeaponController resolves a matching clip from Resources/GunSounds by weapon name.
    public AudioClip fireSound;

    // --- Runtime ammo state (not shown in inspector, set up at runtime) ---
    [System.NonSerialized] public int ammoInMag = -1;     // -1 = not yet initialized
    [System.NonSerialized] public int ammoInReserve = -1;
    [System.NonSerialized] public bool isUpgraded = false; // true once Pack-a-Punched

    /// <summary>
    /// Create a fresh runtime copy of this weapon's CONFIG, with runtime state (current ammo,
    /// upgraded flag) left uninitialised so <see cref="InitAmmo"/> seeds it fresh. Used by the
    /// Mystery Box (and any pool-based granter) so the shared template/pool weapon is never
    /// mutated by a player's usage — otherwise a later roll would inherit spent ammo or a
    /// prior Pack-a-Punch. The weaponModel reference is shared intentionally (it's an asset).
    /// </summary>
    public Weapon Clone()
    {
        return new Weapon
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
            weaponModel = weaponModel,
            fireSound = fireSound,
            // ammoInMag / ammoInReserve stay at -1 and isUpgraded at false (fresh);
            // InitAmmo() seeds the runtime ammo when the weapon is granted.
        };
    }

    // Call once before the weapon is first used to seed runtime ammo from the inspector values.
    public void InitAmmo()
    {
        if (ammoInMag < 0)
        {
            ammoInMag = Mathf.Max(0, magazineSize);
            ammoInReserve = Mathf.Max(0, reserveAmmo);
        }
    }

    public bool HasAmmoInMag => ammoInMag > 0;
    public bool CanReload => ammoInReserve > 0 && ammoInMag < magazineSize;

    // Consume one round from the magazine. Returns true if a shot was actually fired.
    public bool ConsumeRound()
    {
        if (ammoInMag <= 0)
        {
            return false;
        }

        ammoInMag--;
        return true;
    }

    // Move ammo from reserve into the magazine, respecting magazineSize.
    public void Reload()
    {
        int needed = magazineSize - ammoInMag;
        if (needed <= 0 || ammoInReserve <= 0)
        {
            return;
        }

        int taken = Mathf.Min(needed, ammoInReserve);
        ammoInMag += taken;
        ammoInReserve -= taken;
    }
}
