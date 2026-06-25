using UnityEngine;

/// <summary>
/// LEGACY standalone melee/knife (V key, +130 on kill). WeaponController now owns
/// the knife input. This component self-disables in Awake() when a WeaponController
/// is present on the same object or a parent, so prefabs that still carry it never
/// produce a duplicate V-key knife. Kept only so existing prefab references don't
/// break; new players should rely on WeaponController's built-in melee.
/// </summary>
public class MeleeWeapon : MonoBehaviour
{
    [Header("Melee Settings")]
    [Tooltip("Key to trigger melee attack.")]
    public KeyCode meleeKey = KeyCode.V;
    [Tooltip("Range of the melee attack in meters.")]
    public float range = 2.5f;
    [Tooltip("Damage dealt by melee attack.")]
    public int damage = 100;
    [Tooltip("Cooldown between melee attacks in seconds.")]
    public float cooldown = 0.8f;
    [Tooltip("Layers the melee can hit.")]
    public LayerMask hitMask = ~0;

    [Header("Camera Reference")]
    [Tooltip("Optional. If null, uses Camera.main.")]
    public Transform aimCamera;

    private Transform cam;
    private float nextMeleeTime;

    private void Awake()
    {
        // WeaponController now owns the knife/melee input. Defer to it (and avoid a
        // duplicate V-key knife) by disabling this legacy component when one exists.
        if (GetComponentInParent<WeaponController>() != null)
        {
            enabled = false;
        }
    }

    private void Start()
    {
        ResolveCamera();
    }

    private void Update()
    {
        if (cam == null)
        {
            ResolveCamera();
        }

        if (Input.GetKeyDown(meleeKey) && Time.time >= nextMeleeTime)
        {
            TryMelee();
        }
    }

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

    private void TryMelee()
    {
        if (cam == null)
        {
            return;
        }

        nextMeleeTime = Time.time + cooldown;

        if (Physics.Raycast(cam.position, cam.forward, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore))
        {
            ZombieAgent zombie = hit.collider.GetComponentInParent<ZombieAgent>();
            if (zombie != null)
            {
                zombie.KillByMelee();
                Debug.Log("[MeleeWeapon] Melee kill on " + hit.collider.name);
            }
            else
            {
                Debug.Log("[MeleeWeapon] Hit " + hit.collider.name + " (not a zombie)");
            }
        }
        else
        {
            Debug.Log("[MeleeWeapon] Swung and missed");
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (cam == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawRay(cam.position, cam.forward * range);
    }
}