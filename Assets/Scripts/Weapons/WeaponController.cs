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

    [Tooltip("Aim assist: the shot's 'thickness' (sphere radius) for forgiving hit " +
             "registration on zombies, especially while moving. 0 = pinpoint raycast. " +
             "~0.25-0.4 feels good for a Zombies-style game.")]
    public float aimAssistRadius = 0.3f;

    [Header("View Model (First-Person)")]
    [Tooltip("Transform under the camera that the equipped weapon's model is spawned into. " +
             "Leave empty to auto-find a child named 'WeaponHolder' under the Main Camera " +
             "(one is created there if it doesn't exist).")]
    public Transform weaponHolder;
    [Tooltip("Local position applied to a spawned weapon model under the holder.")]
    public Vector3 weaponModelLocalPosition = Vector3.zero;
    [Tooltip("Extra euler rotation (degrees) applied ON TOP of the model prefab's own authored " +
             "rotation. 0 = keep the prefab's orientation (old weapons are authored upright, the " +
             "new Meshy guns bake their own forward rotation). Use this to nudge orientation.")]
    public Vector3 weaponModelLocalEuler = Vector3.zero;
    [Tooltip("Local scale applied to a spawned weapon model under the holder.")]
    public Vector3 weaponModelLocalScale = Vector3.one;
    [Tooltip("Rotate the spawned model by an extra correction AFTER the normal rotation. The Meshy " +
             "guns are authored barrel-along-X while the holder expects barrel-along-Z, so a Y turn " +
             "makes them face forward. Turn off for models that are already oriented correctly.")]
    public bool useWeaponVisualRotationCorrection = true;
    [Tooltip("The correction euler (degrees) applied when the toggle above is on. Try 0,90,0; if the " +
             "barrel points the wrong way, set 0,-90,0.")]
    public Vector3 weaponVisualRotationCorrectionEuler = new Vector3(0f, 90f, 0f);

    [Header("Muzzle Flash Fallback (visual only)")]
    [Tooltip("OPTIONAL muzzle-flash prefab. If left empty, a simple flash is created at RUNTIME so " +
             "muzzle FX still work with zero setup. Only used when the weapon model has no muzzle " +
             "ParticleSystem of its own. Shooting/hit detection are unaffected either way.")]
    public GameObject muzzleFlashFallbackPrefab;
    [Tooltip("Local position (under the WeaponHolder) of the fallback muzzle flash — near the front " +
             "of the gun. Tune later; it does not need to be exactly at the barrel tip.")]
    public Vector3 muzzleFlashLocalPosition = new Vector3(0.25f, -0.05f, 0.75f);
    [Tooltip("Local euler rotation of the fallback muzzle flash (aim it forward along the barrel).")]
    public Vector3 muzzleFlashLocalEuler = Vector3.zero;

    [Header("Melee / Knife")]
    [Tooltip("Key to perform an instant-kill knife/melee attack.")]
    public KeyCode meleeKey = KeyCode.V;
    [Tooltip("Range of the knife attack in world units.")]
    public float meleeRange = 2.15f;
    [Tooltip("Forgiveness radius for the knife's close-range sphere sweep, in world units. " +
             "Wider = easier to connect on nearby zombies without pinpoint aim.")]
    public float meleeRadius = 0.45f;
    [Tooltip("Radius of the overlap-sphere fallback used when the forward sweep misses. Kept " +
             "modest (not the full melee range) so the fallback stays a short-range grab, not a " +
             "wide vacuum.")]
    public float meleeFallbackRadius = 0.85f;
    [Tooltip("Cooldown between knife attacks in seconds.")]
    public float meleeCooldown = 0.8f;
    [Tooltip("Damage a single melee/knife hit deals (CoD-style). ~130 one-shots the earliest " +
             "rounds but not later ones, so melee stays useful without being permanently OP.")]
    public int meleeDamage = 130;

    [Header("Knife Model (optional, future)")]
    [Tooltip("Optional first-person knife model shown during the melee swing. Leave empty for no " +
             "visual — melee still works. Assign Assets/Prefabs/Weapons/Knife.prefab here once made.")]
    public GameObject knifeModelPrefab;
    [Tooltip("Local position of the knife wrapper under the WeaponHolder (first-person placement).")]
    public Vector3 knifeLocalPosition = new Vector3(0.25f, -0.20f, 0.45f);
    [Tooltip("Local euler rotation (degrees) of the knife wrapper.")]
    public Vector3 knifeLocalEuler = new Vector3(20f, 100f, -20f);
    [Tooltip("Local scale of the knife wrapper. Keep at 1,1,1 — the Meshy prefab keeps its own " +
             "authored scale; this is not multiplied by a big default. Tune in the Inspector.")]
    public Vector3 knifeLocalScale = Vector3.one;
    [Tooltip("Spawn the knife in CAMERA space instead of under the WeaponHolder. The holder can " +
             "carry its own offset/rotation that pushes a first-person model off-screen, so camera " +
             "space is the reliable default. Falls back to the holder when no camera is available.")]
    public bool knifeUseCameraSpace = true;
    [Tooltip("Local position of the knife wrapper in CAMERA space (used when knifeUseCameraSpace).")]
    public Vector3 knifeCameraLocalPosition = new Vector3(0.35f, -0.32f, 0.75f);
    [Tooltip("Local euler rotation (degrees) of the knife wrapper in CAMERA space.")]
    public Vector3 knifeCameraLocalEuler = new Vector3(20f, -35f, -20f);
    [Tooltip("Seconds the knife visual stays shown per swing. Visual only — does NOT change melee " +
             "damage, range, cooldown, or networking.")]
    public float knifeVisualDuration = 0.35f;

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
    private AudioSource _fireAudio;     // dedicated one-shot source for this player's gunfire (lazy-created)

    // Fire-sound clips shipped in Resources/GunSounds, indexed by fire-sound id. The id is what
    // travels over the network (the weapons list isn't a NetworkVariable, so remote replicas
    // can't derive the shooter's weapon on their own).
    private static readonly string[] FireSoundKeys =
    {
        "M1911_Fire",      // 0
        "Uzi_Fire",        // 1
        "MP5_Fire",        // 2
        "AK47_Fire",       // 3
        "M16_Fire",        // 4
        "PumpShotgun_Fire",// 5
        "Revolver_Fire",   // 6
        "BoltAction_Fire", // 7
    };
    private static readonly AudioClip[] _fireClipCache = new AudioClip[8];
    private static readonly bool[] _fireClipTried = new bool[8];
    private int _cameraResolveAttempts;  // capped retries so we stop searching for a missing camera
    private readonly Queue<GameObject> _bloodPool = new Queue<GameObject>(); // pooled blood-effect instances
    private GameObject _spawnedViewModel; // first-person model currently spawned under the holder
    private GameObject _spawnedMuzzleFallback; // fallback muzzle-flash instance (when the model has none)
    private GameObject _knifeModel;       // optional spawned knife view model (null until assigned)
    private bool _knifeVisualActive;      // true while the knife model is shown for a swing
    private bool _warnedNoMuzzle; // warn only once when a model has no muzzle and no fallback is set
    private bool initialized;

    // Pack-a-Punch upgrade multipliers — adjust here rather than hunting magic numbers.
    private const float PAPDamageMultiplier = 2f;
    private const float PAPFireRateMultiplier = 1.5f;
    private const int PAPReserveMinMagazines = 5;
    // Sanity bound only. The server trusts the shooter's reported camera origin (this is
    // co-op PvE, not competitive), because the server's replica of a remote player lags
    // behind by the network interpolation buffer — a tight bound here would silently drop
    // legitimate client shots while they move/sprint. We only reject origins that are
    // wildly off (garbage / teleport exploits), never normal play.
    private const float MaxServerShotOriginDistance = 100f;

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
        if (_spawnedMuzzleFallback != null)
        {
            Destroy(_spawnedMuzzleFallback);
            _spawnedMuzzleFallback = null;
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
            // 4. Position/orient/scale it for first person. Scale PRESERVES the prefab's own scale
            // and applies weaponModelLocalScale as a component-wise MULTIPLIER (was: it overwrote
            // the scale outright, which forced every model to the multiplier's default of (1,1,1)
            // and made the larger-authored new Meshy guns spawn tiny). Old weapons whose prefab
            // scale is (1,1,1) are unaffected — (1,1,1) * (1,1,1) stays (1,1,1). The multiplier is
            // still a live tuning knob (e.g. 2,2,2 doubles the model).
            Vector3 prefabScale = model.transform.localScale;
            Quaternion prefabRotation = model.transform.localRotation;
            model.transform.localPosition = weaponModelLocalPosition;
            // Rotation PRESERVES the prefab's authored orientation and applies weaponModelLocalEuler
            // as an offset (was: it overwrote the rotation, discarding the new Meshy models' baked
            // forward rotation -> they spawned sideways). Old weapons have identity prefab rotation,
            // so Euler(euler) * identity == the previous behaviour exactly.
            model.transform.localRotation = Quaternion.Euler(weaponModelLocalEuler) * prefabRotation;
            if (useWeaponVisualRotationCorrection)
            {
                // Extra correction so barrel-along-X Meshy models face forward (Z). Applied in the
                // holder's space, so 0,90,0 <-> 0,-90,0 flips the facing. Camera/aim are untouched.
                model.transform.localRotation =
                    Quaternion.Euler(weaponVisualRotationCorrectionEuler) * model.transform.localRotation;
            }
            model.transform.localScale = Vector3.Scale(prefabScale, weaponModelLocalScale);
            // 5. Make sure it's visible.
            model.SetActive(true);
            _spawnedViewModel = model;

            Debug.Log("[WeaponController] New view model spawned under WeaponHolder: " + model.name +
                      " ('" + cur.weaponName + "')");
            Debug.Log("[WeaponController] View model scale -> prefab " + prefabScale +
                      " * multiplier " + weaponModelLocalScale + " = " + model.transform.localScale +
                      "  (pos " + model.transform.localPosition + " euler " + model.transform.localEulerAngles + ")");

            // 6. Re-bind the recoil/muzzle/sound script to the spawned model.
            gunRecoil = model.GetComponentInChildren<SimpleGunRecoil>(true);
        }

        // Fallback to a shared rig elsewhere on the player if the model has no recoil script.
        if (gunRecoil == null)
        {
            gunRecoil = GetComponentInChildren<SimpleGunRecoil>(true);
        }

        // Rebind the MUZZLE FLASH from the newly spawned view model so each weapon flashes with
        // ITS OWN particle (not the pistol's).
        BindMuzzleFlash(cur);
    }

    // Conventional node names a designer might use for the muzzle particle inside a weapon
    // model. Matched loosely (case / spaces / underscores ignored) so "MuzzleFlash",
    // "Muzzle Flash", "Muzzle", "muzzle_flash" and "FirePoint" all resolve.
    private static bool IsMuzzleNodeName(string nodeName)
    {
        if (string.IsNullOrEmpty(nodeName))
        {
            return false;
        }
        string s = nodeName.Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
        return s.Contains("muzzle") || s.Contains("firepoint");
    }

    /// <summary>
    /// Find the muzzle ParticleSystem inside a spawned weapon model, INCLUDING inactive
    /// children (a muzzle flash prefab is usually disabled until it plays, so a plain active
    /// search misses it). Prefers a particle on/under a conventionally named node
    /// (MuzzleFlash / Muzzle / FirePoint); otherwise falls back to the first particle found.
    /// </summary>
    private ParticleSystem FindMuzzleParticle(GameObject model)
    {
        if (model == null)
        {
            return null;
        }

        ParticleSystem[] systems = model.GetComponentsInChildren<ParticleSystem>(true);
        if (systems == null || systems.Length == 0)
        {
            return null;
        }

        // Prefer a particle whose own node (or an ancestor within the model) is muzzle-named.
        foreach (ParticleSystem ps in systems)
        {
            if (ps == null)
            {
                continue;
            }
            Transform t = ps.transform;
            while (t != null)
            {
                if (IsMuzzleNodeName(t.name))
                {
                    return ps;
                }
                if (t == model.transform)
                {
                    break;
                }
                t = t.parent;
            }
        }

        // No named muzzle node: use the first particle the model carries.
        return systems[0];
    }

    /// <summary>
    /// Point the recoil rig's muzzle flash at the CURRENT weapon model's own particle. Always
    /// clears the previous reference first so a destroyed/previous model's particle is never
    /// reused (which is why only the pistol used to flash — every other weapon fell back to a
    /// rig still pointing at the pistol's now-destroyed particle). No-ops safely when there is
    /// no recoil rig, and never blocks shooting when a weapon simply has no particle.
    /// </summary>
    private void BindMuzzleFlash(Weapon cur)
    {
        string weaponName = cur != null ? cur.weaponName : "<none>";

        if (gunRecoil == null)
        {
            // No recoil rig to route a flash through; shooting still works, just no muzzle FX.
            return;
        }

        // Clear the stale reference before rebinding so we never play a destroyed model's
        // particle or leave the pistol's particle bound for a different gun.
        gunRecoil.muzzleFlash = null;

        ParticleSystem muzzle = FindMuzzleParticle(_spawnedViewModel);

        // Fallback: the model has no muzzle particle of its own (e.g. the clean Meshy guns). Use the
        // optional prefab if assigned, otherwise BUILD a simple flash at runtime (zero setup needed).
        // Both are parented under the (unscaled) holder at the tunable offset. Purely visual —
        // nothing about shooting/hit detection depends on it.
        if (muzzle == null && weaponHolder != null)
        {
            if (muzzleFlashFallbackPrefab != null)
            {
                _spawnedMuzzleFallback = Instantiate(muzzleFlashFallbackPrefab, weaponHolder);
                _spawnedMuzzleFallback.transform.localPosition = muzzleFlashLocalPosition;
                _spawnedMuzzleFallback.transform.localEulerAngles = muzzleFlashLocalEuler;
                muzzle = _spawnedMuzzleFallback.GetComponentInChildren<ParticleSystem>(true);
            }
            else
            {
                muzzle = CreateRuntimeMuzzleFlash(weaponHolder, muzzleFlashLocalPosition, muzzleFlashLocalEuler);
                _spawnedMuzzleFallback = muzzle != null ? muzzle.gameObject : null;
            }
        }

        if (muzzle != null)
        {
            gunRecoil.muzzleFlash = muzzle;
            Debug.Log("[WeaponController] Muzzle particle bound for '" + weaponName + "' -> " + muzzle.name +
                      (_spawnedMuzzleFallback != null ? " (runtime/fallback)" : "") + ".");
        }
        else if (!_warnedNoMuzzle)
        {
            // Warn once only, so a whole match of muzzle-less weapons doesn't spam the console.
            _warnedNoMuzzle = true;
            Debug.LogWarning("[WeaponController] Could not create a muzzle flash for '" + weaponName +
                             "'; muzzle flash is off (shooting still works).");
        }
    }

    // Build a simple, self-contained muzzle-flash ParticleSystem at runtime (no prefab/editor work).
    // A quick one-shot burst of bright unlit particles near the front of the gun. Visual only.
    private ParticleSystem CreateRuntimeMuzzleFlash(Transform parent, Vector3 localPosition, Vector3 localEuler)
    {
        if (parent == null)
        {
            return null;
        }

        GameObject go = new GameObject("RuntimeMuzzleFlash");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localEulerAngles = localEuler;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 0.08f;
        main.startLifetime = 0.05f;
        main.startSpeed = 1.5f;
        main.startSize = 0.25f;
        main.startColor = new Color(1f, 0.85f, 0.4f, 1f);
        main.maxParticles = 24;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)10) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = 0.03f;

        // A simple bright unlit material so the flash is visible (URP-safe shader fallbacks).
        ParticleSystemRenderer psr = go.GetComponent<ParticleSystemRenderer>();
        if (psr != null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) { shader = Shader.Find("Sprites/Default"); }
            if (shader == null) { shader = Shader.Find("Unlit/Color"); }
            if (shader != null)
            {
                psr.material = new Material(shader) { color = new Color(1f, 0.8f, 0.3f, 1f) };
            }
        }

        return ps;
    }

    // Reload eligibility, Infinite Ammo aware. Normally a weapon needs spare reserve to reload
    // (Weapon.CanReload). During Infinite Ammo the magazine is refilled for free, so a partial
    // (or empty) magazine can reload even at 0 reserve — the refill spends no reserve, so ammo
    // never goes negative. A full magazine is still never "reloadable".
    private static bool CanReloadNow(Weapon w)
    {
        if (w == null)
        {
            return false;
        }
        if (PowerupManager.InfiniteAmmoActive)
        {
            return w.ammoInMag < w.magazineSize;
        }
        return w.CanReload;
    }

    private void HandleReloadInput()
    {
        Weapon w = Current;
        if (w == null || isReloading)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.R) && CanReloadNow(w))
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
        if (PowerupManager.InfiniteAmmoActive)
        {
            // Infinite Ammo: top the magazine to full WITHOUT spending reserve (matches a
            // normal reload's magazine end-state; reserve is left valid and never negative).
            w.ammoInMag = Mathf.Max(0, w.magazineSize);
        }
        else
        {
            w.Reload();
        }
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

        // Restore the gun once the swing window closes (runs every frame regardless of the
        // early-returns below, so the knife visual never sticks on).
        if (_knifeVisualActive && !IsKnifing)
        {
            EndKnifeVisual();
        }

        if (playerHealth != null && (playerHealth.IsDowned || playerHealth.IsDead))
        {
            return;
        }

        if (cam == null || Time.time < nextMeleeTime || !Input.GetKeyDown(meleeKey))
        {
            return;
        }

        nextMeleeTime = Time.time + Mathf.Max(0.05f, meleeCooldown);
        knifeSwingEndTime = Time.time + Mathf.Max(0.05f, knifeVisualDuration);
        IsKnifing = true;
        StartKnifeVisual(); // optional: no-op when no knife prefab is assigned

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
        float range = Mathf.Max(0.1f, meleeRange);
        LogMelee("[WeaponController] Melee swing by client " + shooterClientId + ".");

        // 1) Forgiving forward sphere sweep for zombies, followed by the same line-of-sight guard
        //    as the overlap fallback so the knife cannot connect through walls or props.
        float radius = Mathf.Max(0.05f, meleeRadius);
        if (CastNonSelf(origin, radius, forward, range, true, out RaycastHit sweepHit))
        {
            ZombieAgent zombie = sweepHit.collider.GetComponentInParent<ZombieAgent>();
            if (zombie != null && !zombie.IsDead && HasMeleeLineOfSight(origin, zombie, sweepHit.collider, range))
            {
                LogMelee("[WeaponController] Melee (sweep) by client " + shooterClientId +
                          " hit zombie '" + zombie.name + "'.");
                // Apply melee DAMAGE (not an instant kill). A killing blow awards the melee kill
                // reward via ZombieAgent.Die(isMelee: true); a non-killing hit awards nothing.
                zombie.TakeMeleeDamage(meleeDamage, shooterClientId);
                return;
            }
        }

        // 2) Fallback: the nearest LIVING zombie inside a MODEST overlap sphere just in front of
        //    the player (its own radius, not the full melee range, so it stays a short grab).
        //    Guards line-of-sight so we never knife a zombie through a wall.
        Vector3 sphereCenter = origin + forward * (range * 0.55f);
        float fallbackRadius = Mathf.Max(0.05f, meleeFallbackRadius);
        ZombieAgent nearest = FindNearestZombieInSphere(sphereCenter, fallbackRadius, out Collider nearestCol);
        if (nearest != null && HasMeleeLineOfSight(origin, nearest, nearestCol, range))
        {
            LogMelee("[WeaponController] Melee (overlap) by client " + shooterClientId +
                      " hit zombie '" + nearest.name + "'.");
            nearest.TakeMeleeDamage(meleeDamage, shooterClientId);
            return;
        }

        LogMelee("[WeaponController] Melee by client " + shooterClientId + " missed.");
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private static void LogMelee(string message)
    {
        Debug.Log(message);
    }

    // --- Optional knife swing visual (purely cosmetic; never affects damage or its timing) ------

    // Lazily spawn the knife model under the WeaponHolder the first time it is needed. Null-safe:
    // does nothing (and melee still works) when no prefab is assigned or no holder exists yet.
    //
    // The visual root is an empty wrapper we fully control (KnifeVisualRoot). The Meshy prefab is
    // spawned as a CHILD, preserving its authored local transform, then its visible renderers are
    // recentered on the wrapper origin so internal Meshy offsets/scales can't push it off-camera.
    // Target world size (largest bounds dimension) the auto-fit aims the knife toward, and the
    // clamp on the fit multiplier so a mis-imported Meshy scale can never explode or vanish.
    private const float KnifeFitMinSize = 0.65f;
    private const float KnifeFitMaxSize = 0.85f;
    private const float KnifeFitScaleClampMin = 0.001f;
    private const float KnifeFitScaleClampMax = 1000f;

    private void EnsureKnifeModel()
    {
        if (_knifeModel != null || knifeModelPrefab == null)
        {
            return;
        }

        // Prefer CAMERA space: the WeaponHolder can carry its own offset/rotation that shoves a
        // first-person model off-screen, so parenting to the camera with dedicated camera-space
        // placement is reliable. Fall back to the holder only when no camera is available.
        Transform parent;
        Vector3 localPos;
        Vector3 localEuler;
        Transform camera = cam != null ? cam : ResolveCameraTransform();
        if (knifeUseCameraSpace && camera != null)
        {
            parent = camera;
            localPos = knifeCameraLocalPosition;
            localEuler = knifeCameraLocalEuler;
        }
        else
        {
            parent = weaponHolder;
            localPos = knifeLocalPosition;
            localEuler = knifeLocalEuler;
        }

        if (parent == null)
        {
            return; // no camera and no holder: no visual possible, but melee still works
        }

        // Wrapper: localPos/Euler/Scale drive THIS transform (not the prefab root), so placement is
        // independent of whatever the prefab bakes internally.
        GameObject wrapper = new GameObject("KnifeVisualRoot");
        wrapper.transform.SetParent(parent, false);
        wrapper.transform.localPosition = localPos;
        wrapper.transform.localRotation = Quaternion.Euler(localEuler);
        wrapper.transform.localScale = knifeLocalScale;

        // Spawn the prefab as a child, PRESERVING its authored local transform (worldPositionStays
        // = false), then center + auto-fit the visible mesh onto the wrapper origin.
        GameObject knife = Instantiate(knifeModelPrefab, wrapper.transform, false);
        FitKnifeRenderers(wrapper.transform, knife, parent.name, localPos, localEuler);

        _knifeModel = wrapper;
        _knifeModel.SetActive(false); // hidden until a swing shows it
    }

    // Center the spawned knife's visible mesh on the wrapper origin, then scale the wrapper so the
    // largest visible bounds dimension lands in [KnifeFitMinSize, KnifeFitMaxSize] — handling Meshy
    // models that import too tiny, too huge, or with weird internal offsets. Runs once per knife
    // spawn (editor/dev logging only), so it never spams. A rendererless prefab is warned about.
    private void FitKnifeRenderers(Transform wrapper, GameObject knife, string parentName, Vector3 localPos, Vector3 localEuler)
    {
        Renderer[] renderers = knife != null ? knife.GetComponentsInChildren<Renderer>(true) : null;
        if (renderers == null || renderers.Length == 0)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[WeaponController] Knife prefab '" +
                (knifeModelPrefab != null ? knifeModelPrefab.name : "<null>") +
                "' has no renderers — nothing will be visible during melee. Check the prefab.");
#endif
            return;
        }

        // Make sure every visible mesh is actually enabled, then measure combined world bounds.
        Bounds bounds = default;
        bool hasBounds = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null)
            {
                continue;
            }
            r.enabled = true;
            if (!hasBounds)
            {
                bounds = r.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        // Center: shift the knife child so the mesh bounds-center sits at the wrapper origin. Both
        // values are in wrapper-local space; scaling the wrapper afterwards is about that origin,
        // so the mesh stays centered.
        Vector3 localCenter = wrapper.InverseTransformPoint(bounds.center);
        knife.transform.localPosition -= localCenter;

        // Auto-fit: if the largest world dimension is outside the target band, scale the wrapper so
        // it hits the band midpoint. Clamped so a bad import can't produce an insane scale.
        float targetSize = (KnifeFitMinSize + KnifeFitMaxSize) * 0.5f;
        float largestDim = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        if (largestDim > 0.0001f && (largestDim < KnifeFitMinSize || largestDim > KnifeFitMaxSize))
        {
            float scaleMul = Mathf.Clamp(targetSize / largestDim, KnifeFitScaleClampMin, KnifeFitScaleClampMax);
            wrapper.localScale = knifeLocalScale * scaleMul;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log("[WeaponController] Knife visual spawned: prefab='" + knifeModelPrefab.name +
            "' parent='" + parentName + "' renderers=" + renderers.Length +
            " localPos=" + localPos + " localEuler=" + localEuler +
            " wrapperScale=" + wrapper.localScale + " boundsSize=" + bounds.size);
#endif
    }

    // Show the knife for the swing and hide the current gun view model. No-op (melee still works)
    // when no knife prefab is assigned; safe when there is no gun model to hide.
    private void StartKnifeVisual()
    {
        EnsureKnifeModel();
        if (_knifeModel == null)
        {
            return; // no knife assigned yet: gameplay melee still ran, just no swing visual
        }

        if (_spawnedViewModel != null)
        {
            _spawnedViewModel.SetActive(false);
        }
        _knifeModel.SetActive(true);
        _knifeVisualActive = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log("[WeaponController] Knife visual active (V pressed) for " +
                  Mathf.Max(0.05f, knifeVisualDuration).ToString("0.00") + "s.");
#endif
    }

    // Hide the knife and restore the current gun view model after the swing window. Null-safe, so a
    // weapon switch mid-swing (which respawns _spawnedViewModel already active) never permanently
    // hides the gun.
    private void EndKnifeVisual()
    {
        _knifeVisualActive = false;
        if (_knifeModel != null)
        {
            _knifeModel.SetActive(false);
        }
        if (_spawnedViewModel != null)
        {
            _spawnedViewModel.SetActive(true);
        }
    }

    // Nearest living zombie within an overlap sphere, ignoring this player's own colliders.
    // Uses the reusable buffer so it never allocates. Returns null when none is in reach.
    private ZombieAgent FindNearestZombieInSphere(Vector3 center, float radius, out Collider hitCollider)
    {
        hitCollider = null;
        ZombieAgent nearest = null;
        float bestSqr = float.MaxValue;

        int count = Physics.OverlapSphereNonAlloc(center, Mathf.Max(0.05f, radius), _meleeOverlap, hitMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider c = _meleeOverlap[i];
            if (c == null || c.transform.IsChildOf(transform))
            {
                continue; // never our own body
            }
            ZombieAgent z = c.GetComponentInParent<ZombieAgent>();
            if (z == null || z.IsDead)
            {
                continue;
            }
            float sqr = (c.bounds.center - center).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                nearest = z;
                hitCollider = c;
            }
        }
        return nearest;
    }

    // True when nothing solid (a non-zombie collider) sits between the player and the fallback
    // target, so the knife can't reach a zombie through a wall. A clear line, or a zombie being
    // the first thing hit, both count as line-of-sight.
    private bool HasMeleeLineOfSight(Vector3 origin, ZombieAgent target, Collider targetCol, float range)
    {
        Vector3 targetPoint = targetCol != null ? targetCol.bounds.center : target.transform.position;
        Vector3 toTarget = targetPoint - origin;
        float dist = toTarget.magnitude;
        if (dist <= 0.0001f)
        {
            return true;
        }

        Vector3 dir = toTarget / dist;
        if (CastNonSelf(origin, 0f, dir, Mathf.Min(dist + 0.1f, range + 0.5f), false, out RaycastHit hit))
        {
            // If the first solid thing on the way is NOT a zombie, the path is blocked.
            if (hit.collider.GetComponentInParent<ZombieAgent>() == null)
            {
                return false;
            }
        }
        return true;
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
            if (CanReloadNow(w))
            {
                StartCoroutine(ReloadRoutine(w));
            }
            else
            {
                // Truly empty: nothing in the mag and nothing in reserve to reload (and no
                // Infinite Ammo to refill it for free).
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
        // Other clients play this gun's muzzle for the shot. This legacy path has no
        // weapon reference, so pass -1 (no fire sound resolved).
        FireEffectsClientRpc(shooter, -1);
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
    private void FireEffectsClientRpc(ulong shooterClientId, int fireSoundId)
    {
        bool isShooter = NetworkManager.Singleton != null &&
                         NetworkManager.Singleton.LocalClientId == shooterClientId;

        // Everyone except the shooter (who already played it locally) hears the shot.
        // This runs even on the host so it can hear remote clients' guns.
        if (!isShooter)
        {
            PlayFireSound(GetFireClip(fireSoundId));
        }

        // The host (server) and the shooter already played their own muzzle/recoil.
        if (IsServer)
        {
            return;
        }
        if (isShooter)
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
        zombie.TakeDamage(damage, isHeadshot, shooterClientId);

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

    // Map a weapon name to a fire-sound id (index into FireSoundKeys). Tolerant of spacing,
    // dashes, underscores, case and Pack-a-Punch "+" suffixes, with per-family aliases.
    // Returns -1 when no family matches (caller then plays no sound).
    private static int ResolveFireSoundId(string weaponName)
    {
        if (string.IsNullOrEmpty(weaponName))
        {
            return -1;
        }

        string n = weaponName.ToLowerInvariant()
            .Replace(" ", "").Replace("-", "").Replace("_", "").Replace("+", "");

        if (n.Contains("m1911") || n.Contains("1911")) return 0;
        if (n.Contains("uzi")) return 1;
        if (n.Contains("mp5")) return 2;
        if (n.Contains("ak47") || n.Contains("ak74") || n.Contains("ak")) return 3;
        if (n.Contains("m16")) return 4;
        if (n.Contains("pump") || n.Contains("shotgun")) return 5;
        if (n.Contains("revolver") || n.Contains("magnum")) return 6;
        if (n.Contains("bolt") || n.Contains("sniper")) return 7;
        return -1;
    }

    // Load (and cache) the fire clip for an id from Resources/GunSounds. Returns null for an
    // out-of-range id or a missing asset; the miss is warned once so it never spams per shot.
    private static AudioClip GetFireClip(int id)
    {
        if (id < 0 || id >= FireSoundKeys.Length)
        {
            return null;
        }
        if (!_fireClipTried[id])
        {
            _fireClipTried[id] = true;
            _fireClipCache[id] = Resources.Load<AudioClip>("GunSounds/" + FireSoundKeys[id]);
            if (_fireClipCache[id] == null)
            {
                Debug.LogWarning("[WeaponController] Missing fire clip Resources/GunSounds/" +
                    FireSoundKeys[id] + " — that weapon will fire silently.");
            }
        }
        return _fireClipCache[id];
    }

    // Resolve the clip to play for a weapon: an explicitly-assigned fireSound wins, otherwise
    // fall back to the name-matched Resources clip.
    private AudioClip ResolveFireClip(Weapon w)
    {
        if (w == null)
        {
            return null;
        }
        return w.fireSound != null ? w.fireSound : GetFireClip(ResolveFireSoundId(w.weaponName));
    }

    // Lazily create the dedicated gunfire AudioSource on a child object so it never fights the
    // player's other audio. 2D for the local shooter (always audible), 3D for remote replicas
    // so peers hear it positioned at the firing player.
    private AudioSource EnsureFireAudioSource()
    {
        if (_fireAudio != null)
        {
            return _fireAudio;
        }

        var go = new GameObject("GunFireAudio");
        go.transform.SetParent(transform, false);
        _fireAudio = go.AddComponent<AudioSource>();
        _fireAudio.playOnAwake = false;
        _fireAudio.loop = false;

        bool local = !IsSpawned || IsOwner;
        if (local)
        {
            _fireAudio.spatialBlend = 0f; // 2D: the shooter always hears their own gun.
        }
        else
        {
            _fireAudio.spatialBlend = 1f; // 3D: positioned at the remote shooter.
            _fireAudio.rolloffMode = AudioRolloffMode.Linear;
            _fireAudio.minDistance = 3f;
            _fireAudio.maxDistance = 60f;
        }
        return _fireAudio;
    }

    // Play a fire clip as a one-shot so rapid fire overlaps naturally. No-op when clip is null
    // (blocked/dry/unmatched shots), so callers never gate on it.
    private void PlayFireSound(AudioClip clip)
    {
        if (clip == null)
        {
            return;
        }
        EnsureFireAudioSource().PlayOneShot(clip);
    }

    private void Fire(Weapon w)
    {
        // Infinite Ammo (team power-up): fire freely without draining the magazine. Fire()
        // is only reached when the mag already has a round (HandleFiring gates on
        // HasAmmoInMag), so skipping the decrement simply keeps the count where it is —
        // ammo never goes negative and normal consumption resumes when the effect ends.
        if (!PowerupManager.InfiniteAmmoActive)
        {
            if (!w.ConsumeRound())
            {
                return;
            }
        }

        // Immediate first-person feedback for the shooter (recoil / muzzle / sound).
        if (gunRecoil != null)
        {
            gunRecoil.Kick();
        }
        // The shooter hears their own gun immediately, no network round-trip.
        PlayFireSound(ResolveFireClip(w));

        // CLIENT-SIDE HIT DETECTION. The shooter raycasts in ITS OWN view, where the
        // zombies are actually rendered, so a hit always matches what the player aimed at.
        // (The previous design raycast on the SERVER using the client's aim, but against
        // the server's authoritative zombie positions — which differ from the client's
        // interpolated view — so remote clients' shots constantly missed.) The resolved
        // target is then sent to the server, which applies the authoritative damage.
        //
        // AIM = SCREEN CENTRE. Build the ray from the rendering camera's viewport centre (the
        // crosshair) rather than the camera transform's forward. This makes the shot go exactly
        // where the crosshair points at any pitch — fixing the "have to aim lower up/down stairs"
        // offset — and starts the ray on the near plane so it never clips the player's own body
        // when aiming down. Falls back to the transform if the Camera component isn't resolvable.
        Camera aimCam = cam != null ? cam.GetComponent<Camera>() : null;
        Vector3 origin;
        Vector3 aimForward;
        if (aimCam != null)
        {
            Ray aimRay = aimCam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            origin = aimRay.origin;
            aimForward = aimRay.direction;
        }
        else
        {
            origin = cam.position;
            aimForward = cam.forward;
        }
        Vector3 dir = ApplySpread(aimForward, w.spread);

        ResolveShot(origin, dir, Mathf.Max(0.1f, w.range),
            out ZombieAgent zombie, out bool isHeadshot, out Vector3 point, out Vector3 normal);

        // Instant hit-marker for the shooter (no round-trip).
        if (zombie != null)
        {
            HitMarkerHud.Show();
        }

        // Solo: detect, apply damage, and show blood all locally.
        if (!IsSpawned)
        {
            if (zombie != null)
            {
                zombie.TakeDamage(ComputeDamage(w.damage, 0), isHeadshot, 0);
                SpawnBloodLocal(point, normal);
            }
            return;
        }

        // Host (server is also the shooter): apply authoritative damage directly and let
        // every client (including the host) show blood + this gun's muzzle effects.
        if (IsServer)
        {
            if (zombie != null)
            {
                zombie.TakeDamage(ComputeDamage(w.damage, OwnerClientId), isHeadshot, OwnerClientId);
                SpawnBloodClientRpc(point, normal);
            }
            FireEffectsClientRpc(OwnerClientId, ResolveFireSoundId(w.weaponName));
            return;
        }

        // Remote client: send the resolved target (by NetworkObjectId) to the server.
        ulong targetId = 0;
        bool hasTarget = false;
        if (zombie != null)
        {
            NetworkObject zno = zombie.GetComponentInParent<NetworkObject>();
            if (zno != null && zno.IsSpawned)
            {
                targetId = zno.NetworkObjectId;
                hasTarget = true;
            }
        }
        FireDamageServerRpc(hasTarget, targetId, isHeadshot, w.damage, ResolveFireSoundId(w.weaponName));
    }

    // Reusable buffer so the shot cast never allocates.
    private static readonly RaycastHit[] _shotHits = new RaycastHit[16];

    // Reusable buffer for the melee fallback overlap so it never allocates.
    private static readonly Collider[] _meleeOverlap = new Collider[16];

    // Resolve a shot into a zombie hit (if any) plus an impact point, handling the two
    // problems that made shooting unreliable:
    //  1) SELF-BLOCK: the first-person camera sits inside this player's CharacterController
    //     capsule, so a naive cast self-hits (worst aiming downward). We ignore our own
    //     colliders everywhere.
    //  2) THIN HITBOXES / MOVING: a pinpoint ray against a thin zombie collider misses on
    //     the slightest aim error (which moving amplifies). So if the precise ray doesn't
    //     hit a zombie, we do a forgiving zombie-only sphere sweep for aim assist.
    // Walls still block: the sphere assist is limited to the distance of whatever solid the
    // precise ray hit, so you can't shoot zombies through walls.
    private bool ResolveShot(Vector3 origin, Vector3 dir, float range,
        out ZombieAgent zombie, out bool isHeadshot, out Vector3 point, out Vector3 normal)
    {
        zombie = null;
        isHeadshot = false;
        point = origin + dir * range;
        normal = -dir;

        // Step 1: precise ray from the camera, ignoring our own body.
        bool hit = CastNonSelf(origin, 0f, dir, range, false, out RaycastHit precise);
        float blockDistance = range;
        if (hit)
        {
            point = precise.point;
            normal = precise.normal;
            zombie = precise.collider.GetComponentInParent<ZombieAgent>();
            if (zombie != null)
            {
                isHeadshot = precise.collider.CompareTag("Head");
                return true; // direct, precise zombie hit
            }
            blockDistance = precise.distance; // hit a wall/prop — can't shoot a zombie past it
        }

        // Step 2: aim assist. Sphere-sweep for ZOMBIES ONLY, started just past our own
        // capsule so the sphere never begins overlapping the player (which makes SphereCast
        // unreliable). Capped at the wall distance so it can't reach through cover.
        float radius = Mathf.Max(0f, aimAssistRadius);
        if (radius > 0f)
        {
            float gap = Mathf.Min(0.6f, blockDistance);
            Vector3 assistOrigin = origin + dir * gap;
            float assistRange = Mathf.Max(0f, blockDistance - gap);
            if (assistRange > 0f &&
                CastNonSelf(assistOrigin, radius, dir, assistRange, true, out RaycastHit zHit))
            {
                zombie = zHit.collider.GetComponentInParent<ZombieAgent>();
                isHeadshot = zHit.collider.CompareTag("Head");
                point = zHit.point.sqrMagnitude > 0.0001f ? zHit.point : assistOrigin + dir * zHit.distance;
                normal = zHit.normal.sqrMagnitude > 0.0001f ? zHit.normal : -dir;
                return true;
            }
        }

        return hit; // a wall/prop (or nothing)
    }

    // Nearest collider along a ray/sphere sweep that is NOT part of this player. When
    // zombieOnly is true, only zombie colliders count (so the player's own body — never a
    // zombie — is skipped even if the sphere starts overlapping it). radius 0 = raycast.
    private bool CastNonSelf(Vector3 origin, float radius, Vector3 dir, float range, bool zombieOnly, out RaycastHit best)
    {
        best = default;
        int count = radius > 0f
            ? Physics.SphereCastNonAlloc(origin, radius, dir, _shotHits, range, hitMask, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(origin, dir, _shotHits, range, hitMask, QueryTriggerInteraction.Ignore);

        float bestDistance = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit candidate = _shotHits[i];
            if (candidate.collider == null || candidate.collider.transform.IsChildOf(transform))
            {
                continue;
            }
            if (zombieOnly && candidate.collider.GetComponentInParent<ZombieAgent>() == null)
            {
                continue;
            }
            if (candidate.distance < bestDistance)
            {
                bestDistance = candidate.distance;
                best = candidate;
                found = true;
            }
        }
        return found;
    }

    // Build a fire direction from the aim forward, applying the weapon's spread cone.
    private Vector3 ApplySpread(Vector3 forward, float spread)
    {
        forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : transform.forward;
        if (spread <= 0f)
        {
            return forward;
        }

        Vector3 right = Vector3.Cross(Vector3.up, forward);
        right = right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
        Vector3 up = Vector3.Cross(forward, right);

        float maxRad = Mathf.Tan(spread * Mathf.Deg2Rad);
        Vector2 offset = Random.insideUnitCircle * maxRad;
        return (forward + right * offset.x + up * offset.y).normalized;
    }

    // A client reports which zombie it hit (resolved in its own view). The server validates
    // the shooter owns this player, then applies the authoritative damage to that zombie and
    // broadcasts blood / muzzle effects. Trusting the client's hit is fine for co-op PvE.
    [ServerRpc(RequireOwnership = false)]
    private void FireDamageServerRpc(bool hasTarget, ulong targetNetworkObjectId, bool isHeadshot, int baseDamage, int fireSoundId, ServerRpcParams rpcParams = default)
    {
        ulong shooter = rpcParams.Receive.SenderClientId;
        if (shooter != OwnerClientId || baseDamage <= 0)
        {
            return;
        }

        if (hasTarget && NetworkManager != null && NetworkManager.SpawnManager != null &&
            NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject zno) &&
            zno != null)
        {
            ZombieAgent zombie = zno.GetComponentInParent<ZombieAgent>();
            if (zombie == null)
            {
                zombie = zno.GetComponentInChildren<ZombieAgent>();
            }
            if (zombie != null)
            {
                zombie.TakeDamage(ComputeDamage(baseDamage, shooter), isHeadshot, shooter);
                SpawnBloodClientRpc(zombie.transform.position + Vector3.up, Vector3.up);
            }
        }

        FireEffectsClientRpc(shooter, fireSoundId);
    }

    // HUD drawing is handled centrally by GameHud (which reads the public getters above),
    // so WeaponController no longer draws its own ammo readout or crosshair.
}
