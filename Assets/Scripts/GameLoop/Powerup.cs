using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A single power-up pickup dropped by a killed zombie. A small glowing, spinning
/// cube the player collects by walking over it (trigger) or pressing E nearby.
///
/// Created entirely from code via <see cref="Spawn"/> (no prefab needed). On collect
/// it tells <see cref="PowerupManager"/> to apply its effect, then destroys itself.
/// It despawns automatically after a lifetime, flashing near the end.
/// </summary>
public class Powerup : MonoBehaviour
{
    private static readonly Dictionary<int, Powerup> Registry = new Dictionary<int, Powerup>();
    private static readonly List<Powerup> NetworkedPowerups = new List<Powerup>();

    public PowerupType type = PowerupType.MaxAmmo;

    [Tooltip("How close the player must be to auto-collect / press E.")]
    public float collectRange = 1.6f;
    [Tooltip("Seconds before the pickup despawns on its own.")]
    public float lifetime = 15f;

    [Header("Pickup Motion")]
    [Tooltip("Bob cycles per second.")]
    public float bobSpeed = 2f;
    [Tooltip("Bob amplitude in world units (peak offset above/below the spawn height).")]
    public float bobHeight = 0.2f;
    [Tooltip("Spin speed in degrees/second (90 = one full rotation every 4 seconds).")]
    public float spinSpeed = 90f;

    private float spawnTime;
    private Transform player;
    private Renderer rend;
    private Vector3 spawnPosition; // captured once so the bob oscillates without drifting
    private bool networked;
    private int networkId;

    public int NetworkId => networkId;
    public bool IsNetworked => networked;
    public float RemainingLifetime => Mathf.Max(0f, lifetime - (Time.time - spawnTime));
    public static IEnumerable<Powerup> ActiveNetworkedPowerups => NetworkedPowerups;

    /// <summary>Create a power-up pickup at a world position. Returns the new instance.</summary>
    public static Powerup Spawn(PowerupType type, Vector3 position, float lifetime, int networkId = 0, bool networked = false)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Powerup_" + type;
        go.transform.position = position;
        go.transform.localScale = Vector3.one * 0.6f;

        // Trigger collider so the player can walk through and collect.
        Collider col = go.GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }

        Color color = PowerupManager.ColorOf(type);
        Renderer r = go.GetComponent<Renderer>();
        if (r != null)
        {
            // Use a fresh material instance and make it emissive where supported.
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            Material mat = new Material(shader) { name = "Powerup_" + type };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * 2f);
            }
            r.material = mat;
        }

        Powerup p = go.AddComponent<Powerup>();
        p.type = type;
        p.lifetime = Mathf.Max(1f, lifetime);
        p.networkId = networkId;
        p.networked = networked;
        p.RegisterNetworked();
        return p;
    }

    private void Start()
    {
        spawnTime = Time.time;
        rend = GetComponent<Renderer>();
        // Capture the spawn position ONCE so the bob oscillates around it instead of
        // accumulating (which would make the pickup drift upward forever).
        spawnPosition = transform.position;
        FindPlayer();
    }

    private void OnEnable()
    {
        RegisterNetworked();
    }

    private void RegisterNetworked()
    {
        if (networked && networkId != 0)
        {
            Registry[networkId] = this;
            if (!NetworkedPowerups.Contains(this))
            {
                NetworkedPowerups.Add(this);
            }
        }
    }

    private void OnDisable()
    {
        if (networkId != 0 &&
            Registry.TryGetValue(networkId, out Powerup registered) &&
            registered == this)
        {
            Registry.Remove(networkId);
        }
        NetworkedPowerups.Remove(this);
    }

    private void Update()
    {
        // Spin around Y, and bob up/down around the spawn height, for visibility.
        transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f);
        float bob = Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.position = new Vector3(spawnPosition.x, spawnPosition.y + bob, spawnPosition.z);

        float age = Time.time - spawnTime;
        if (age >= lifetime)
        {
            if (networked && NetworkGameplayCoordinator.IsNetworkActive && NetworkGameplayCoordinator.IsServer)
            {
                NetworkGameplayCoordinator.BroadcastPowerupCollected(networkId);
            }
            Destroy(gameObject);
            return;
        }

        // Flash in the final 3 seconds.
        if (rend != null && lifetime - age < 3f)
        {
            bool visible = Mathf.FloorToInt(age * 6f) % 2 == 0;
            rend.enabled = visible;
        }

        if (player == null)
        {
            FindPlayer();
        }

        if (player != null)
        {
            float dist = Vector3.Distance(transform.position, player.position);
            if (dist <= collectRange)
            {
                // Auto-collect on contact, or on E.
                Collect();
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null)
        {
            return;
        }
        // Collect when the player's controller touches it.
        if (other.GetComponentInParent<CharacterController>() != null)
        {
            Collect();
        }
    }

    private void Collect()
    {
        if (networked && NetworkGameplayCoordinator.IsNetworkActive)
        {
            NetworkGameplayCoordinator.RequestPowerupCollect(this);
            return;
        }

        CollectOffline();
    }

    public void CollectOffline()
    {
        if (PowerupManager.Instance != null)
        {
            PowerupManager.Instance.Apply(type);
        }
        Destroy(gameObject);
    }

    public static bool TryFind(int id, out Powerup powerup)
    {
        return Registry.TryGetValue(id, out powerup);
    }

    private void FindPlayer()
    {
        Transform local = LocalPlayer.Transform;
        if (local != null)
        {
            player = local;
            return;
        }
        if (Camera.main != null)
        {
            player = Camera.main.transform;
        }
    }
}
