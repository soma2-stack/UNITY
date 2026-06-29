// ✅ WEAPONS AUDIT FIXES
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Simple hitscan (raycast) shooting system for "School Of The Dead".
// No animations are used — pure raycast firing plus enabling/disabling weapon models.
// Put this on the player root or the camera. Uses the legacy Input Manager only.
//
// Server-authoritative in a networked session: a client's shot is sent to the server
// (FireServerRpc), the SERVER does the spread + raycast + damage, and hit feedback is
// broadcast back to clients (ClientRpc). In solo (not network-spawned) everything runs
// locally exactly as before.
public class WeaponController : NetworkBehaviour
{
    [Header("Weapons")]
    [Tooltip("Configure each weapon's stats and assign its in-hand model.")]
    public List<Weapon> weapons = new List<Weapon>();

    [Tooltip("Index of the weapon equipped at start.")]
    public int currentIndex = 0;

    [Tooltip("Maximum weapon slots (classic Zombies = 2). When full, GiveWeapon replaces the current slot.")]
    public int maxWeaponSlots = 2;

    [Tooltip("Legacy toggle. Empty loadouts still receive fallback M1911 stats/ammo so players never spawn unarmed.")]
    public bool startWithPistol = true;

    [Header("Default Starting Weapon")]
    [Tooltip("Stats for the pistol handed out when startWithPistol is true and no weapons are " +
             "configured. Editable here instead of being hardcoded. Use the component's Reset to " +
             "restore the classic M1911 values.")]
    public Weapon defaultPistol = new Weapon
    {
        weaponName = "M1911",
        damage = 40,
        fireRate = 3f,
        automatic = false,
        range = 80f,
        spread = 1f,
        magazineSize = 8,
        reserveAmmo = 48,
        reloadTime = 1.8f,
    };

    [Header("Aiming")]
    [Tooltip("Optional. In multiplayer this must be a camera under the same owned player. Solo may fall back to Camera.main.")]
    public Transform aimCamera;

    [Tooltip("Optional layers the rays can hit. Leave as Everything to hit all.")]
    public LayerMask hitMask = ~0;

    [Header("View Model (First-Person)")]
    [Tooltip("Transform under the camera that the equipped weapon's model is spawned into. " +
             "Leave empty to auto-find a child named 'WeaponHolder' under the Main Camera " +
             "(one is created there if it doesn't exist).")]
    public Transform weaponHolder;
    [Tooltip("Local position applied to a spawned weapon model under the holder.")]
    public Vector3 weaponModelLocalPosition = Vector3.zero;
    [Tooltip("Local euler rotation (degrees) applied to a spawned weapon model under the holder.")]
    public Vector3 weaponModelLocalEuler = Vector3.zero;
    [Tooltip("Local scale applied to a spawned weapon model under the holder.")]
    public Vector3 weaponModelLocalScale = Vector3.one;

    [Header("Melee / Knife")]
    [Tooltip("Key to perform an instant-kill knife/melee attack.")]
    public KeyCode meleeKey = KeyCode.V;
    [Tooltip("Range of the knife attack in world units.")]
    public float meleeRange = 2.5f;
    [Tooltip("Cooldown between knife attacks in seconds.")]
    public float meleeCooldown = 0.8f;

    [Header("Hit Feedback")]
    [Tooltip("Optional blood/hit particle prefab, spawned at the impact point only when a ZombieAgent is shot. Leave empty for no blood.")]
    public GameObject bloodHitEffect;
    [Tooltip("Seconds before a spawned blood effect is destroyed (1-2 is typical).")]
    public float bloodEffectLifetime = 1.5f;

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
    private float nextMeleeTime;        // earliest Time.time the next knife is allowed
    private float knifeSwingEndTime;    // IsKnifing stays true until this time after a swing
    private SimpleGunRecoil gunRecoil;  // Optional FPS gun kickback script found on child weapon model
    private int _cameraResolveAttempts;  // capped retries so we stop searching for a missing camera
    private readonly Queue<GameObject> _bloodPool = new Queue<GameObject>(); // pooled blood-effect instances
    private GameObject _spawnedViewModel; // first-person model currently spawned under the holder
    private bool initialized;

    // Pack-a-Punch upgrade multipliers — adjust here rather than hunting magic numbers.
    private const float PAPDamageMultiplier = 2f;
    private const float PAPFireRateMultiplier = 1.5f;
    private const int PAPReserveMinMagazines = 5;
    private const float MaxServerShotOriginDistance = 2.5f;

    /// <summary>True for a short window while a knife swing is in progress (HUD/animator can react).</summary>
    public bool IsKnifing { get; private set; }

    /// <summary>Raised when the player tries to fire a truly empty gun (no mag ammo and no reserve to reload).</summary>
    public event System.Action OnDryFire;

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

        // Mule Kick raises the carry cap by ExtraWeaponSlots (read dynamically).
        int extra = PerkManager.Instance != null ? PerkManager.Instance.ExtraWeaponSlots : 0;
        int slotCap = Mathf.Max(1, maxWeaponSlots + extra);
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
#if UNITY_EDITOR
        Debug.Log("[WeaponController] Gave weapon: " + weapon.weaponName);
#endif
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

        // Already Pack-a-Punched: never upgrade twice. Uses a dedicated flag rather
        // than a fragile name-suffix check.
        if (w.isUpgraded)
        {
            return;
        }

        if (!w.weaponName.EndsWith(" +"))
        {
            w.weaponName += " +"; // visual marker for the HUD only - not the upgrade gate
        }
        w.damage = Mathf.Max(1, Mathf.RoundToInt(w.damage * PAPDamageMultiplier));
        w.fireRate = w.fireRate * PAPFireRateMultiplier;
        w.reserveAmmo = Mathf.Max(w.reserveAmmo, w.magazineSize * PAPReserveMinMagazines);
        w.ammoInMag = Mathf.Max(0, w.magazineSize);
        w.ammoInReserve = Mathf.Max(0, w.reserveAmmo);
        w.isUpgraded = true;

#if UNITY_EDITOR
        Debug.Log("[WeaponController] Pack-a-Punched: " + w.weaponName + " (dmg " + w.damage + ")");
#endif
    }

    /// <summary>
    /// Called when the player loses Mule Kick. If they are over the base slot cap
    /// (i.e. carrying the extra Mule Kick weapon), drop the most recently acquired
    /// weapon (the last slot): hide its model, null it, and trim the list back down
    /// to maxWeaponSlots. Re-clamps the equipped index afterwards.
    /// </summary>
    public void RemoveExtraWeaponSlot()
    {
        if (weapons == null)
        {
            return;
        }

        int baseCap = Mathf.Max(1, maxWeaponSlots);
        while (weapons.Count > baseCap)
        {
            int last = weapons.Count - 1;
            Weapon w = weapons[last];
            if (w != null && w.weaponModel != null)
            {
                w.weaponModel.SetActive(false);
            }
            weapons[last] = null;
            weapons.RemoveAt(last);
        }

        currentIndex = weapons.Count > 0 ? Mathf.Clamp(currentIndex, 0, weapons.Count - 1) : 0;
        StopAllCoroutines();
        isReloading = false;
        EquipCurrent();
    }

    /// <summary>
    /// Refill ONLY the reserve ammo for the named weapon back to its configured
    /// reserveAmmo (CoD wall-buy ammo). Never touches ammoInMag, so it does not
    /// reload the current magazine. No-op if the weapon isn't owned.
    /// </summary>
    public void RefillReserveAmmo(string weaponName)
    {
        if (weapons == null)
        {
            return;
        }

        foreach (Weapon w in weapons)
        {
            if (w != null && w.weaponName == weaponName)
            {
                w.ammoInReserve = Mathf.Max(0, w.reserveAmmo);
                return;
            }
        }
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

#if UNITY_EDITOR
        Debug.Log("[WeaponController] Max Ammo: all weapons refilled.");
#endif
    }

    private void OnEnable()
    {
        if (!IsSpawned || !IsOwner)
        {
            return;
        }

        InitializeRuntime();
    }

    void Start()
    {
        InitializeRuntime();
    }

    private void InitializeRuntime()
    {
        if (initialized)
        {
            ResolveCamera();
            ResolveWeaponHolder();
            return;
        }

        if (IsSpawned && !IsOwner)
        {
            return;
        }

        ResolveCamera();
        ResolveWeaponHolder();
        gunRecoil = GetComponentInChildren<SimpleGunRecoil>(true);

        // Cache the player's health (same GameObject or a parent) so we can block
        // firing/switching while downed or dead, and cancel reloads on the way down.
        playerHealth = GetComponentInParent<PlayerHealth>();
        if (playerHealth != null)
        {
            playerHealth.OnPlayerDowned -= HandlePlayerDowned;
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

        // Guaranteed starting weapon: if nothing was configured, give the inspector-
        // configurable default pistol so the player never spawns unarmed.
        if (weapons == null || weapons.Count == 0 || AllWeaponsNull())
        {
#if UNITY_EDITOR
            Debug.Log("[WeaponController] No weapons configured — giving default starting pistol.");
#endif
            if (weapons == null)
            {
                weapons = new List<Weapon>();
            }
            else
            {
                weapons.Clear();
            }

            // Use the serialized defaultPistol; fall back to a hardcoded M1911 only if
            // it was cleared in the inspector so the player is never unarmed.
            Weapon pistol = defaultPistol ?? CreateFallbackPistol();
            SanitizeFallbackPistol(pistol);
            pistol.InitAmmo();

            // Clear, actionable warning instead of a silent invisible gun.
            if (pistol.weaponModel == null)
            {
                Debug.LogWarning("[WeaponController] Default Pistol ('" + pistol.weaponName + "') has no Weapon Model " +
                    "assigned — it will be invisible in first person. Assign the M1911 prefab to " +
                    "WeaponController > Default Starting Weapon > Weapon Model.");
            }

            weapons.Add(pistol);
            currentIndex = 0;
        }

        // Clamp the starting index and show only the equipped model.
        if (weapons != null && weapons.Count > 0)
        {
            currentIndex = Mathf.Clamp(currentIndex, 0, weapons.Count - 1);
            if (weapons[currentIndex] == null)
            {
                int firstWeaponIndex = FirstValidWeaponIndex();
                if (firstWeaponIndex >= 0)
                {
                    currentIndex = firstWeaponIndex;
                }
            }
        }
        EquipCurrent();

        // Pre-warm the blood-effect pool so the first hits don't hitch on Instantiate.
        if (bloodHitEffect != null && _bloodPool.Count == 0)
        {
            for (int i = 0; i < 5; i++)
            {
                GameObject fx = Instantiate(bloodHitEffect);
                fx.SetActive(false);
                _bloodPool.Enqueue(fx);
            }
        }

        initialized = true;
    }

    private bool AllWeaponsNull()
    {
        if (weapons == null)
        {
            return true;
        }

        foreach (Weapon weapon in weapons)
        {
            if (weapon != null)
            {
                return false;
            }
        }

        return true;
    }

    private int FirstValidWeaponIndex()
    {
        if (weapons == null)
        {
            return -1;
        }

        for (int i = 0; i < weapons.Count; i++)
        {
            if (weapons[i] != null)
            {
                return i;
            }
        }

        return -1;
    }

    private static Weapon CreateFallbackPistol()
    {
        return new Weapon
        {
            weaponName = "M1911",
            damage = 40,
            fireRate = 3f,
            automatic = false,
            range = 80f,
            spread = 1f,
            magazineSize = 8,
            reserveAmmo = 48,
            reloadTime = 1.8f,
        };
    }

    private static void SanitizeFallbackPistol(Weapon pistol)
    {
        if (pistol == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(pistol.weaponName))
        {
            pistol.weaponName = "M1911";
        }
        if (pistol.damage <= 0)
        {
            pistol.damage = 40;
        }
        if (pistol.fireRate <= 0f)
        {
            pistol.fireRate = 3f;
        }
        if (pistol.range <= 0f)
        {
            pistol.range = 80f;
        }
        if (pistol.magazineSize <= 0)
        {
            pistol.magazineSize = 8;
        }
        if (pistol.reserveAmmo < 0)
        {
            pistol.reserveAmmo = 48;
        }
        if (pistol.reloadTime <= 0f)
        {
            pistol.reloadTime = 1.8f;
        }
    }

    // Editor-only: give a freshly added (or reset) component the classic M1911
    // starting-pistol defaults so scenes get the correct starting gun automatically.
    private void Reset()
    {
        defaultPistol = new Weapon
        {
            weaponName = "M1911",
            damage = 40,
            fireRate = 3f,
            automatic = false,
            range = 80f,
            spread = 1f,
            magazineSize = 8,
            reserveAmmo = 48,
            reloadTime = 1.8f,
        };
    }

    void Update()
    {
        if (IsSpawned && !IsOwner)
        {
            return;
        }

        // Retry resolving the camera only a capped number of times so a permanently
        // missing camera doesn't trigger an expensive scene search every frame.
        if (cam == null && _cameraResolveAttempts < 10)
        {
            ResolveCamera();
        }

        HandleWeaponSwitching();
        HandleReloadInput();
        HandleMelee();
        HandleFiring();
    }

    // Resolve the aim camera: explicit same-player field -> same-player child camera.
    // Solo/non-networked play may still fall back to scene cameras.
    private void ResolveCamera()
    {
        if (aimCamera != null)
        {
            if (IsAllowedPlayerTransform(aimCamera))
            {
                cam = aimCamera;
                _cameraResolveAttempts = 0;
                return;
            }

            Debug.LogWarning("[WeaponController] Ignoring aimCamera outside this player: " + aimCamera.name);
            aimCamera = null;
            cam = null;
        }

        Camera localCamera = GetComponentInChildren<Camera>(true);
        if (localCamera != null)
        {
            aimCamera = localCamera.transform;
            cam = aimCamera;
            _cameraResolveAttempts = 0;
            return;
        }

        if (!IsSpawned)
        {
            Camera main = Camera.main;
            if (main == null)
            {
                main = FindFirstObjectByType<Camera>();
            }

            if (main != null)
            {
                cam = main.transform;
                _cameraResolveAttempts = 0;
                return;
            }
        }

        // No camera this attempt: count it, and after 10 tries warn once and stop
        // retrying (Update gates further calls on this counter).
        _cameraResolveAttempts++;
        if (_cameraResolveAttempts >= 10)
        {
            Debug.LogWarning("WeaponController: No camera found after 10 attempts — firing disabled.");
        }
    }

    private const string WeaponHolderName = "WeaponHolder";

    // In a spawned network player, camera/holder references must belong to this
    // player. Solo/non-networked play can still fall back to scene cameras.
    private Transform ResolveCameraTransform()
    {
        if (aimCamera != null && IsAllowedPlayerTransform(aimCamera))
        {
            return aimCamera;
        }

        if (cam != null && IsAllowedPlayerTransform(cam))
        {
            return cam;
        }

        Camera localCamera = GetComponentInChildren<Camera>(true);
        if (localCamera != null)
        {
            return localCamera.transform;
        }

        if (!IsSpawned)
        {
            Camera sceneCamera = Camera.main;
            if (sceneCamera == null)
            {
                sceneCamera = FindFirstObjectByType<Camera>();
            }

            return sceneCamera != null ? sceneCamera.transform : null;
        }

        return null;
    }

    private void ResolveWeaponHolder()
    {
        if (weaponHolder != null)
        {
            if (IsAllowedPlayerTransform(weaponHolder))
            {
                return;
            }

            Debug.LogWarning("[WeaponController] Ignoring WeaponHolder outside this player: " + weaponHolder.name);
            weaponHolder = null;
        }

        Transform cameraTransform = ResolveCameraTransform();
        if (cameraTransform == null)
        {
            Debug.LogWarning("[WeaponController] WeaponHolder could not be resolved: no camera found yet.");
            return;
        }

        Transform existing = FindDirectChild(cameraTransform, WeaponHolderName);
        if (existing != null)
        {
            weaponHolder = existing;
            Debug.Log("[WeaponController] WeaponHolder found under '" + cameraTransform.name + "'.");
            return;
        }

        GameObject holder = new GameObject(WeaponHolderName);
        holder.transform.SetParent(cameraTransform, false);
        holder.transform.localPosition = Vector3.zero;
        holder.transform.localRotation = Quaternion.identity;
        holder.transform.localScale = Vector3.one;
        weaponHolder = holder.transform;
        Debug.Log("[WeaponController] WeaponHolder created under '" + cameraTransform.name + "'.");
    }

    private bool IsAllowedPlayerTransform(Transform candidate)
    {
        return candidate != null && (!IsSpawned || candidate == transform || candidate.IsChildOf(transform));
    }

    private static Transform FindDirectChild(Transform parent, string childName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == childName)
            {
                return child;
            }
        }

        return null;
    }

    // Cancels any in-progress reload and drops to the pistol (slot 0) the instant the
    // player goes down - downed players may use only their starting pistol.
    private void HandlePlayerDowned()
    {
        StopAllCoroutines();
        isReloading = false;
        if (weapons != null && weapons.Count > 0)
        {
            currentIndex = 0;
            EquipCurrent();
        }
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

    // Spawn the equipped weapon's model under the WeaponHolder, removing the previously
    // spawned one, so the first-person view model switches with the weapon.
    private void EquipCurrent()
    {
        if (weapons == null)
        {
            return;
        }

        ResolveWeaponHolder();

        Weapon cur = Current;
        Debug.Log("[WeaponController] Equipping: " + (cur != null ? cur.weaponName : "<none>"));

        // 1. Remove the previously spawned view model.
        if (_spawnedViewModel != null)
        {
            Debug.Log("[WeaponController] Old view model removed: " + _spawnedViewModel.name);
            Destroy(_spawnedViewModel);
            _spawnedViewModel = null;
        }

        // Hide any legacy IN-SCENE weapon models so they don't linger alongside the
        // spawned view model (prefab-asset references are unaffected by this).
        for (int i = 0; i < weapons.Count; i++)
        {
            Weapon w = weapons[i];
            if (w != null && w.weaponModel != null && w.weaponModel.scene.IsValid())
            {
                w.weaponModel.SetActive(false);
            }
        }

        gunRecoil = null;

        // 2. Get the equipped weapon's model prefab.
        if (cur == null || cur.weaponModel == null)
        {
            if (cur != null)
            {
                Debug.LogWarning("[WeaponController] '" + cur.weaponName + "' has no Weapon Model assigned; nothing to show.");
            }
        }
        else if (weaponHolder == null)
        {
            Debug.LogWarning("[WeaponController] WeaponHolder is null; cannot spawn the view model for '" + cur.weaponName + "'.");
        }
        else
        {
            Debug.Log("[WeaponController] Weapon Model prefab found: " + cur.weaponModel.name);

            // 3. Instantiate it under the holder.
            GameObject model = Instantiate(cur.weaponModel, weaponHolder);
            // 4. Position/orient/scale it for first person.
            model.transform.localPosition = weaponModelLocalPosition;
            model.transform.localEulerAngles = weaponModelLocalEuler;
            model.transform.localScale = weaponModelLocalScale;
            // 5. Make sure it's visible.
            model.SetActive(true);
            _spawnedViewModel = model;

            Debug.Log("[WeaponController] New view model spawned under WeaponHolder: " + model.name);
            Debug.Log("[WeaponController] View model local TRS -> pos " + model.transform.localPosition +
                      " euler " + model.transform.localEulerAngles + " scale " + model.transform.localScale);

            // 6. Re-bind the recoil/muzzle/sound script to the spawned model.
            gunRecoil = model.GetComponentInChildren<SimpleGunRecoil>(true);
        }

        // Fallback to a shared rig elsewhere on the player if the model has no recoil script.
        if (gunRecoil == null)
        {
            gunRecoil = GetComponentInChildren<SimpleGunRecoil>(true);
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
#if UNITY_EDITOR
        Debug.Log("[WeaponController] Reloading " + w.weaponName + "...");
#endif

        // Speed Cola: shorten the reload wait (guard against zero/negative multiplier).
        float reloadMul = Mathf.Max(0.01f, reloadSpeedMultiplier);
        yield return new WaitForSeconds(Mathf.Max(0f, w.reloadTime) / reloadMul);

        // Only refill if this is still the equipped weapon (switch cancels via StopAllCoroutines).
        w.Reload();
        isReloading = false;
#if UNITY_EDITOR
        Debug.Log("[WeaponController] Reloaded " + w.weaponName + " (" + w.ammoInMag + "/" + w.ammoInReserve + ")");
#endif
    }

    // Instant-kill knife on the melee key: a short raycast that kills any zombie it
    // hits regardless of health (CoD knife). Rate-limited; blocked while downed/dead.
    private void HandleMelee()
    {
        // Keep IsKnifing true for a brief swing window so the HUD/animator can react.
        IsKnifing = Time.time < knifeSwingEndTime;

        if (playerHealth != null && (playerHealth.IsDowned || playerHealth.IsDead))
        {
            return;
        }

        if (cam == null || Time.time < nextMeleeTime || !Input.GetKeyDown(meleeKey))
        {
            return;
        }

        nextMeleeTime = Time.time + Mathf.Max(0.05f, meleeCooldown);
        knifeSwingEndTime = Time.time + 0.2f;
        IsKnifing = true;

        if (IsSpawned && !IsServer)
        {
            MeleeServerRpc(cam.position, cam.forward);
            return;
        }

        PerformMelee(cam.position, cam.forward, IsSpawned ? OwnerClientId : 0);
    }

    [ServerRpc(RequireOwnership = false)]
    private void MeleeServerRpc(Vector3 origin, Vector3 forward, ServerRpcParams rpcParams = default)
    {
        ulong shooter = rpcParams.Receive.SenderClientId;
        if (!ValidateServerShotRequest(shooter, origin, forward, 1, meleeRange))
        {
            return;
        }

        PerformMelee(origin, forward, shooter);
    }

    private void PerformMelee(Vector3 origin, Vector3 forward, ulong shooterClientId)
    {
        forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : transform.forward;
        if (Physics.Raycast(origin, forward, out RaycastHit hit, Mathf.Max(0.1f, meleeRange), hitMask, QueryTriggerInteraction.Ignore))
        {
            ZombieAgent zombie = hit.collider.GetComponentInParent<ZombieAgent>();
            if (zombie != null)
            {
                Debug.Log("[WeaponController] Melee by client " + shooterClientId + " killed zombie '" + zombie.name + "'.");
                zombie.KillByMelee(); // instant kill; ZombieAgent.Die() awards the 130 melee reward
            }
        }
        // Miss or non-zombie: silent (no effect), per spec.
    }

    private void HandleFiring()
    {
        // Dead: no firing at all. Downed: firing is allowed but only the pistol
        // (forced to slot 0 on the way down) and at reduced damage (applied in Fire()).
        if (playerHealth != null && playerHealth.IsDead)
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

        // Out of ammo in the magazine: auto-reload if possible, otherwise dry-fire.
        if (!w.HasAmmoInMag)
        {
            if (w.CanReload)
            {
                StartCoroutine(ReloadRoutine(w));
            }
            else
            {
                // Truly empty: nothing in the mag and nothing in reserve to reload.
                OnDryFire?.Invoke();
            }
            return;
        }

        // Respect fire rate (guard against a zero/negative rate).
        // Double Tap: fireRateMultiplier scales the effective rate up (faster firing).
        float rate = Mathf.Max(0.01f, w.fireRate) * Mathf.Max(0.01f, fireRateMultiplier);
        nextFireTime = Time.time + 1f / rate;

        Fire(w);
    }

    // Reuse a pooled blood-effect instance (re-activating it) or instantiate a fresh
    // one when the pool is empty. Caller positions it before use.
    private GameObject GetBloodEffect()
    {
        GameObject fx = null;
        while (_bloodPool.Count > 0 && fx == null)
        {
            // Skip any pooled entry destroyed externally (e.g. scene teardown).
            fx = _bloodPool.Dequeue();
        }

        if (fx == null)
        {
            fx = Instantiate(bloodHitEffect);
        }

        fx.SetActive(true);
        return fx;
    }

    // Deactivate a finished blood effect and return it to the pool for reuse.
    private void ReturnBloodEffect(GameObject fx)
    {
        if (fx == null)
        {
            return;
        }
        fx.SetActive(false);
        _bloodPool.Enqueue(fx);
    }

    // Returns the effect to the pool after bloodEffectLifetime instead of destroying it.
    private IEnumerator ReturnBloodEffectAfterDelay(GameObject fx)
    {
        yield return new WaitForSeconds(Mathf.Max(0.1f, bloodEffectLifetime));
        ReturnBloodEffect(fx);
    }

    [ServerRpc(RequireOwnership = false)]
    private void FireServerRpc(Vector3 origin, Vector3 forward, int baseDamage, float range, float spread, ServerRpcParams rpcParams = default)
    {
        ulong shooter = rpcParams.Receive.SenderClientId;
        if (!ValidateServerShotRequest(shooter, origin, forward, baseDamage, range))
        {
            return;
        }

        PerformShot(origin, forward, baseDamage, range, spread, shooter, false);
        // Other clients play this gun's muzzle/sound for the shot.
        FireEffectsClientRpc(shooter);
    }

    private bool ValidateServerShotRequest(ulong shooterClientId, Vector3 origin, Vector3 forward, int baseDamage, float range)
    {
        if (!IsSpawned)
        {
            return true;
        }

        if (shooterClientId != OwnerClientId)
        {
            Debug.LogWarning("[WeaponController] Rejected shot: sender client " + shooterClientId +
                " tried to fire player owned by " + OwnerClientId + ".");
            return false;
        }

        if (!IsFinite(origin) || !IsFinite(forward) || forward.sqrMagnitude < 0.0001f ||
            baseDamage <= 0 || range <= 0f)
        {
            Debug.LogWarning("[WeaponController] Rejected malformed shot from client " + shooterClientId + ".");
            return false;
        }

        Transform expectedOrigin = ResolveServerShotOrigin();
        Vector3 expectedPosition = expectedOrigin != null
            ? expectedOrigin.position
            : transform.position + Vector3.up * 1.6f;

        float distance = Vector3.Distance(origin, expectedPosition);
        if (distance > MaxServerShotOriginDistance)
        {
            Debug.LogWarning("[WeaponController] Rejected shot from client " + shooterClientId +
                ": origin was " + distance.ToString("0.00") + "m from that player's camera/root.");
            return false;
        }

        return true;
    }

    private Transform ResolveServerShotOrigin()
    {
        if (aimCamera != null && IsAllowedPlayerTransform(aimCamera))
        {
            return aimCamera;
        }

        Camera localCamera = GetComponentInChildren<Camera>(true);
        return localCamera != null ? localCamera.transform : transform;
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
               !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
    }

    [ClientRpc]
    private void FireEffectsClientRpc(ulong shooterClientId)
    {
        // The host (server) and the shooter already played their own effects.
        if (IsServer)
        {
            return;
        }
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == shooterClientId)
        {
            return;
        }
        if (gunRecoil != null)
        {
            gunRecoil.Kick();
        }
    }

    // The authoritative shot: server (or solo) applies spread, raycasts, deals damage,
    // and pushes hit feedback. `origin`/`forward` come from the shooter's camera.
    private void PerformShot(Vector3 origin, Vector3 forward, int baseDamage, float range, float spread, ulong shooterClientId, bool localShooter)
    {
        forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        Debug.Log("[WeaponController] Shot requested by client " + shooterClientId +
            " origin=" + origin + " forward=" + forward + " range=" + range.ToString("0.0") + ".");

        Vector3 dir = forward;
        if (spread > 0f)
        {
            // Orthonormal basis from forward (the server doesn't have the client's cam basis).
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            right = right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
            Vector3 up = Vector3.Cross(forward, right);

            float maxRad = Mathf.Tan(spread * Mathf.Deg2Rad);
            Vector2 offset = Random.insideUnitCircle * maxRad;
            dir = (forward + right * offset.x + up * offset.y).normalized;
        }

        if (!Physics.Raycast(origin, dir, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore))
        {
            Debug.Log("[WeaponController] Shot by client " + shooterClientId + " hit nothing.");
            return;
        }

        Debug.Log("[WeaponController] Shot by client " + shooterClientId +
            " hit object '" + hit.collider.name + "' on '" + hit.collider.transform.root.name + "'.");

        ZombieAgent zombie = hit.collider.GetComponentInParent<ZombieAgent>();
        if (zombie == null)
        {
            Debug.Log("[WeaponController] Shot by client " + shooterClientId +
                " found no ZombieAgent on hit object '" + hit.collider.name + "'.");
            return;
        }

        bool isHeadshot = hit.collider.CompareTag("Head");
        int damage = ComputeDamage(baseDamage, shooterClientId);

        // Authority applies damage directly (server in a session, or this peer in solo).
        Debug.Log("[WeaponController] Applying " + damage + " damage to zombie '" + zombie.name +
            "' from shooter client " + shooterClientId + " headshot=" + isHeadshot + ".");
        zombie.TakeDamage(damage, isHeadshot);

        // Hit feedback: in a session the server broadcasts blood to everyone and a hit
        // marker to the shooter; in solo it's all local.
        if (IsSpawned)
        {
            SpawnBloodClientRpc(hit.point, hit.normal);
            if (localShooter)
            {
                HitMarkerHud.Show(); // host fired its own shot
            }
            else
            {
                HitMarkerClientRpc(new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { shooterClientId } }
                });
            }
        }
        else
        {
            SpawnBloodLocal(hit.point, hit.normal);
            HitMarkerHud.Show();
        }
    }

    // Damage with Insta-Kill / Rapid Ruin / downed-pistol modifiers (authority-side state).
    private int ComputeDamage(int baseDamage, ulong shooterClientId)
    {
        if (PowerupManager.InstaKillActive)
        {
            return 99999;
        }
        int damage = baseDamage;
        bool rapidRuin = IsSpawned
            ? PerkManager.ClientHasPerk(shooterClientId, PerkType.RapidRuin)
            : PerkManager.Instance != null && PerkManager.Instance.HasPerk(PerkType.RapidRuin);
        if (rapidRuin)
        {
            damage = Mathf.RoundToInt(damage * 2f);
        }
        if (playerHealth != null && playerHealth.IsDownedGunActive)
        {
            damage = Mathf.Max(1, damage / 4);
        }
        return damage;
    }

    [ClientRpc]
    private void HitMarkerClientRpc(ClientRpcParams rpcParams = default)
    {
        HitMarkerHud.Show();
    }

    [ClientRpc]
    private void SpawnBloodClientRpc(Vector3 point, Vector3 normal)
    {
        SpawnBloodLocal(point, normal);
    }

    private void SpawnBloodLocal(Vector3 point, Vector3 normal)
    {
        if (bloodHitEffect == null)
        {
            return;
        }
        Quaternion fxRot = normal.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(normal) : Quaternion.identity;
        GameObject fx = GetBloodEffect();
        fx.transform.SetPositionAndRotation(point, fxRot);
        StartCoroutine(ReturnBloodEffectAfterDelay(fx));
    }

    private void Fire(Weapon w)
    {
        if (!w.ConsumeRound())
        {
            return;
        }

        // Immediate first-person feedback for the shooter (recoil / muzzle / sound).
        if (gunRecoil != null)
        {
            gunRecoil.Kick();
        }

        Vector3 origin = cam.position;
        Vector3 forward = cam.forward;

        // MP client: the authoritative shot (spread + raycast + damage) runs on the server.
        if (IsSpawned && !IsServer)
        {
            FireServerRpc(origin, forward, w.damage, w.range, w.spread);
            return;
        }

        // Solo or server (host): perform the authoritative shot directly.
        PerformShot(origin, forward, w.damage, w.range, w.spread, 0, true);
    }

    // HUD drawing is handled centrally by GameHud (which reads the public getters above),
    // so WeaponController no longer draws its own ammo readout or crosshair.
}
