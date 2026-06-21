using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Simple hitscan (raycast) shooting system for "School Of The Dead".
// No animations are used — pure raycast firing plus enabling/disabling weapon models.
// Put this on the player root or the camera. Uses the legacy Input Manager only.
public class WeaponController : MonoBehaviour
{
    [Header("Weapons")]
    [Tooltip("Configure each weapon's stats and assign its in-hand model.")]
    public List<Weapon> weapons = new List<Weapon>();

    [Tooltip("Index of the weapon equipped at start.")]
    public int currentIndex = 0;

    [Tooltip("Maximum weapon slots (classic Zombies = 2). When full, GiveWeapon replaces the current slot.")]
    public int maxWeaponSlots = 2;

    [Header("Aiming")]
    [Tooltip("Optional. If left null, Camera.main (then any Camera) is used as the aim ray origin.")]
    public Transform aimCamera;

    [Tooltip("Optional layers the rays can hit. Leave as Everything to hit all.")]
    public LayerMask hitMask = ~0;

    [Header("Perk Multipliers")]
    [Tooltip("Fire-rate multiplier (Double Tap perk). Higher = faster firing. 1 = normal.")]
    public float fireRateMultiplier = 1f;
    [Tooltip("Reload-speed multiplier (Speed Cola perk). Higher = faster reloads. 1 = normal.")]
    public float reloadSpeedMultiplier = 1f;

    // --- Runtime state ---
    private Transform cam;          // Resolved aim transform
    private float nextFireTime;     // Time.time when the next shot is allowed
    private bool isReloading;
    private PlayerHealth playerHealth; // cached on the same GameObject/parent; gates firing while downed/dead

    private Weapon Current =>
        (weapons != null && currentIndex >= 0 && currentIndex < weapons.Count) ? weapons[currentIndex] : null;

    // --- Public read-only HUD getters (consumed by GameHud) ---

    /// <summary>Name of the currently equipped weapon, or "" if none.</summary>
    public string CurrentWeaponName => Current != null ? Current.weaponName : "";

    /// <summary>Rounds currently in the equipped weapon's magazine (0 if none).</summary>
    public int CurrentMagazineAmmo => Current != null ? Mathf.Max(0, Current.ammoInMag) : 0;

    /// <summary>Rounds held in reserve for the equipped weapon (0 if none).</summary>
    public int CurrentReserveAmmo => Current != null ? Mathf.Max(0, Current.ammoInReserve) : 0;

    /// <summary>True while the equipped weapon is reloading.</summary>
    public bool IsReloading => isReloading;

    /// <summary>True when a weapon is equipped (used by the HUD to decide whether to draw ammo).</summary>
    public bool HasWeapon => Current != null;

    // --- Game-loop public API (Mystery Box, Pack-a-Punch, power-ups, wall buys) ---

    /// <summary>
    /// Add a weapon and equip it (Mystery Box / wall buy). Respects
    /// <see cref="maxWeaponSlots"/>: if the player already has a slot for this
    /// exact weapon name its ammo is refilled instead; if all slots are full the
    /// currently-equipped slot is replaced. Null is ignored. The weapon's runtime
    /// ammo is initialised. Safe to call any time.
    /// </summary>
    public void GiveWeapon(Weapon weapon)
    {
        if (weapon == null)
        {
            return;
        }

        if (weapons == null)
        {
            weapons = new List<Weapon>();
        }

        weapon.InitAmmo();

        // Already own this exact weapon: just top its ammo back up and equip it.
        for (int i = 0; i < weapons.Count; i++)
        {
            Weapon w = weapons[i];
            if (w != null && w.weaponName == weapon.weaponName)
            {
                w.ammoInMag = Mathf.Max(0, w.magazineSize);
                w.ammoInReserve = Mathf.Max(0, w.reserveAmmo);
                SwitchTo(i);
                EquipCurrent();
                return;
            }
        }

        StopAllCoroutines();
        isReloading = false;

        int slotCap = Mathf.Max(1, maxWeaponSlots);
        if (weapons.Count < slotCap)
        {
            weapons.Add(weapon);
            currentIndex = weapons.Count - 1;
        }
        else
        {
            // Full: replace the slot the player is currently holding.
            int idx = Mathf.Clamp(currentIndex, 0, weapons.Count - 1);
            // Hide the outgoing weapon's model so it doesn't linger.
            if (weapons[idx] != null && weapons[idx].weaponModel != null)
            {
                weapons[idx].weaponModel.SetActive(false);
            }
            weapons[idx] = weapon;
            currentIndex = idx;
        }

        EquipCurrent();
        Debug.Log("[WeaponController] Gave weapon: " + weapon.weaponName);
    }

    /// <summary>
    /// Pack-a-Punch the currently equipped weapon: roughly doubles its damage,
    /// renames it with a trailing " +", and refills its ammo. No-op if there is no
    /// current weapon or it is already upgraded.
    /// </summary>
    public void UpgradeCurrentWeapon()
    {
        Weapon w = Current;
        if (w == null)
        {
            return;
        }

        if (!w.weaponName.EndsWith(" +"))
        {
            w.weaponName += " +";
        }
        w.damage = Mathf.Max(1, w.damage * 2);
        w.reserveAmmo = Mathf.Max(w.reserveAmmo, w.magazineSize * 5);
        w.ammoInMag = Mathf.Max(0, w.magazineSize);
        w.ammoInReserve = Mathf.Max(0, w.reserveAmmo);

        Debug.Log("[WeaponController] Pack-a-Punched: " + w.weaponName + " (dmg " + w.damage + ")");
    }

    /// <summary>Refill magazine and reserve ammo for every weapon (Max Ammo power-up).</summary>
    public void RefillAllAmmo()
    {
        if (weapons == null)
        {
            return;
        }

        foreach (Weapon w in weapons)
        {
            if (w == null)
            {
                continue;
            }
            w.InitAmmo();
            w.ammoInMag = Mathf.Max(0, w.magazineSize);
            w.ammoInReserve = Mathf.Max(0, w.reserveAmmo);
        }

        Debug.Log("[WeaponController] Max Ammo: all weapons refilled.");
    }

    void Start()
    {
        ResolveCamera();

        // Cache the player's health (same GameObject or a parent) so we can block
        // firing/switching while downed or dead, and cancel reloads on the way down.
        playerHealth = GetComponentInParent<PlayerHealth>();
        if (playerHealth != null)
        {
            playerHealth.OnPlayerDowned += HandlePlayerDowned;
        }

        // Seed runtime ammo for every weapon so values persist across switches.
        if (weapons != null)
        {
            foreach (Weapon w in weapons)
            {
                if (w != null)
                {
                    w.InitAmmo();
                }
            }
        }

        // Clamp the starting index and show only the equipped model.
        if (weapons != null && weapons.Count > 0)
        {
            currentIndex = Mathf.Clamp(currentIndex, 0, weapons.Count - 1);
        }
        EquipCurrent();
    }

    void Update()
    {
        if (cam == null)
        {
            ResolveCamera();
        }

        HandleWeaponSwitching();
        HandleReloadInput();
        HandleFiring();
    }

    // Resolve the aim camera: explicit field -> Camera.main -> any camera in scene.
    private void ResolveCamera()
    {
        if (aimCamera != null)
        {
            cam = aimCamera;
            return;
        }

        Camera main = Camera.main;
        if (main == null)
        {
            main = FindFirstObjectByType<Camera>();
        }

        if (main != null)
        {
            cam = main.transform;
        }
    }

    // Cancels any in-progress reload the instant the player goes down.
    private void HandlePlayerDowned()
    {
        StopAllCoroutines();
        isReloading = false;
    }

    private void OnDestroy()
    {
        if (playerHealth != null)
        {
            playerHealth.OnPlayerDowned -= HandlePlayerDowned;
        }
    }

    private void HandleWeaponSwitching()
    {
        // No weapon switching while downed or dead.
        if (playerHealth != null && (playerHealth.IsDowned || playerHealth.IsDead))
        {
            return;
        }

        if (weapons == null || weapons.Count == 0)
        {
            return;
        }

        // Number keys 1..9 select a weapon directly.
        int max = Mathf.Min(weapons.Count, 9);
        for (int i = 0; i < max; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                SwitchTo(i);
                return;
            }
        }

        // Mouse scroll cycles through the list.
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll > 0.01f)
        {
            SwitchTo((currentIndex + 1) % weapons.Count);
        }
        else if (scroll < -0.01f)
        {
            SwitchTo((currentIndex - 1 + weapons.Count) % weapons.Count);
        }
    }

    private void SwitchTo(int index)
    {
        if (weapons == null || index < 0 || index >= weapons.Count || index == currentIndex)
        {
            return;
        }

        // Cancel an in-progress reload when swapping weapons.
        StopAllCoroutines();
        isReloading = false;

        currentIndex = index;
        EquipCurrent();
    }

    // Enable the current weapon's model and disable all the others.
    private void EquipCurrent()
    {
        if (weapons == null)
        {
            return;
        }

        for (int i = 0; i < weapons.Count; i++)
        {
            Weapon w = weapons[i];
            if (w != null && w.weaponModel != null)
            {
                w.weaponModel.SetActive(i == currentIndex);
            }
        }
    }

    private void HandleReloadInput()
    {
        Weapon w = Current;
        if (w == null || isReloading)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.R) && w.CanReload)
        {
            StartCoroutine(ReloadRoutine(w));
        }
    }

    private IEnumerator ReloadRoutine(Weapon w)
    {
        isReloading = true;
        Debug.Log("[WeaponController] Reloading " + w.weaponName + "...");

        // Speed Cola: shorten the reload wait (guard against zero/negative multiplier).
        float reloadMul = Mathf.Max(0.01f, reloadSpeedMultiplier);
        yield return new WaitForSeconds(Mathf.Max(0f, w.reloadTime) / reloadMul);

        // Only refill if this is still the equipped weapon (switch cancels via StopAllCoroutines).
        w.Reload();
        isReloading = false;
        Debug.Log("[WeaponController] Reloaded " + w.weaponName + " (" + w.ammoInMag + "/" + w.ammoInReserve + ")");
    }

    private void HandleFiring()
    {
        // No firing while downed or dead.
        if (playerHealth != null && (playerHealth.IsDowned || playerHealth.IsDead))
        {
            return;
        }

        Weapon w = Current;
        if (w == null || isReloading || cam == null)
        {
            return;
        }

        // Semi-auto fires on the click; full-auto fires while held.
        bool wantsToFire = w.automatic ? Input.GetMouseButton(0) : Input.GetMouseButtonDown(0);
        if (!wantsToFire || Time.time < nextFireTime)
        {
            return;
        }

        // Out of ammo in the magazine: auto-reload if possible, otherwise do nothing.
        if (!w.HasAmmoInMag)
        {
            if (w.CanReload)
            {
                StartCoroutine(ReloadRoutine(w));
            }
            return;
        }

        // Respect fire rate (guard against a zero/negative rate).
        // Double Tap: fireRateMultiplier scales the effective rate up (faster firing).
        float rate = Mathf.Max(0.01f, w.fireRate) * Mathf.Max(0.01f, fireRateMultiplier);
        nextFireTime = Time.time + 1f / rate;

        Fire(w);
    }

    private void Fire(Weapon w)
    {
        if (!w.ConsumeRound())
        {
            return;
        }

        // Apply random spread inside a cone around the camera forward direction.
        Vector3 dir = cam.forward;
        if (w.spread > 0f)
        {
            float maxRad = Mathf.Tan(w.spread * Mathf.Deg2Rad);
            Vector2 offset = Random.insideUnitCircle * maxRad;
            dir = (cam.forward + cam.right * offset.x + cam.up * offset.y).normalized;
        }

        if (Physics.Raycast(cam.position, dir, out RaycastHit hit, w.range, hitMask, QueryTriggerInteraction.Ignore))
        {
            // Look up the chain in case the collider is on a child of the zombie root.
            ZombieAgent zombie = hit.collider.GetComponentInParent<ZombieAgent>();
            if (zombie != null)
            {
                bool isHeadshot = hit.collider.CompareTag("Head");
                // Insta-Kill power-up: any hit is lethal.
                int damage = PowerupManager.InstaKillActive ? 99999 : w.damage;
                bool wasAlive = !zombie.IsDead;
                zombie.TakeDamage(damage, isHeadshot);
                
                if (wasAlive)
                {
                    if (zombie.IsDead)
                    {
                        Debug.Log($"[WeaponController] Killed zombie '{hit.collider.name}' {(isHeadshot ? "(HEADSHOT)" : "")} for {damage} damage.");
                    }
                    else
                    {
                        // -----------------------------------------------------
                        // POINTS OWNERSHIP: WeaponController owns ONLY the +10
                        // non-lethal HIT bonus, awarded here when a bullet hits a
                        // zombie that survives. The KILL reward (60/100/130) is
                        // owned exclusively by ZombieAgent.Die() - never awarded
                        // from the weapon - so the two can never double-count.
                        // -----------------------------------------------------
                        PlayerPoints.Instance?.AddPoints(10);
                        Debug.Log($"[WeaponController] Hit zombie '{hit.collider.name}' for {damage} damage. (+10 points)");
                    }
                }
            }
            else
            {
                Debug.Log("[WeaponController] Hit '" + hit.collider.name + "' at " + hit.point + ".");
            }
        }
        else
        {
            Debug.Log("[WeaponController] Shot missed (no hit within " + w.range + "m).");
        }
    }

    // HUD drawing is handled centrally by GameHud (which reads the public getters above),
    // so WeaponController no longer draws its own ammo readout or crosshair.
}
