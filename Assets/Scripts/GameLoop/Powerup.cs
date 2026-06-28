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

    /// <summary>Create a power-up pickup at a world position. Returns the new instance.</summary>
    public static Powerup Spawn(PowerupType type, Vector3 position, float lifetime)
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

    private void Update()
    {
        // Spin around Y, and bob up/down around the spawn height, for visibility.
        transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f);
        float bob = Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.position = new Vector3(spawnPosition.x, spawnPosition.y + bob, spawnPosition.z);

        float age = Time.time - spawnTime;
        if (age >= lifetime)
        {
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
        if (PowerupManager.Instance != null)
        {
            PowerupManager.Instance.Apply(type);
        }
        Destroy(gameObject);
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
