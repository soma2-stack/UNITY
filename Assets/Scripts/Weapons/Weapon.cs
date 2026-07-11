using UnityEngine;

// How a weapon's shot is resolved into damage.
//  Normal    - single hitscan ray (the classic behavior).
//  Shotgun   - several pellet rays in one trigger pull; damage sums per zombie.
//  Explosive - a hitscan impact point that deals radius damage to nearby zombies.
// Left at Normal by default so every existing weapon is unchanged; WeaponController can also
// infer the mode from the weapon name (Pump/Shotgun/Benelli, RPG/Rocket) when this is Normal.
public enum WeaponDamageMode
{
    Normal = 0,
    Shotgun = 1,
    Explosive = 2,
}

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

    [Header("Fire Mode")]
    // Normal by default so existing weapons are untouched. WeaponController falls back to
    // name-based inference (Pump/Shotgun/Benelli -> Shotgun, RPG/Rocket -> Explosive) when this
    // is Normal, so serialized weapons get the right behavior without editing the asset/scene.
    public WeaponDamageMode damageMode = WeaponDamageMode.Normal;
    // Shotgun: number of pellet rays per shot (>=2 to act as a shotgun). Default 1 = single ray.
    public int pelletCount = 1;
    // Shotgun: damage per pellet. 0 lets WeaponController apply a sensible default.
    public int pelletDamage = 0;
    // Explosive: blast radius in meters. 0 lets WeaponController apply a sensible default.
    public float explosionRadius = 0f;
    // Explosive: damage at the blast center (falls off to a minimum of 1 at the edge). 0 = default.
    public int explosionDamage = 0;

    [Header("Audio")]
    // Fire sound played when this weapon actually fires. Optional: if left null the
    // WeaponController resolves a matching clip from Resources/GunSounds by weapon name.
    public AudioClip fireSound;
    // Reload sound played when a reload starts. Optional: if left null the WeaponController
    // resolves a matching clip from Resources/ReloadSounds by weapon name.
    public AudioClip reloadSound;

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
            reloadSound = reloadSound,
            damageMode = damageMode,
            pelletCount = pelletCount,
            pelletDamage = pelletDamage,
            explosionRadius = explosionRadius,
            explosionDamage = explosionDamage,
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
