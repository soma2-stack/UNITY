using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A single power-up pickup dropped by a killed zombie. A small spinning, bobbing pickup
/// the player collects by walking over it (trigger) or getting close.
///
/// Spawned via <see cref="Spawn"/>: it uses the assigned 3D model prefab for the power-up
/// type (resolved from the local <see cref="PowerupManager"/>) when one exists, and otherwise
/// falls back to a code-generated glowing cube, so pickups still work with no prefab set. On
/// collect it tells <see cref="PowerupManager"/> to apply its effect, then destroys itself.
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
    [Tooltip("Spin speed in degrees/second (120 = one full rotation every 3 seconds).")]
    public float spinSpeed = 120f;

    [Header("Aura (runtime-only glow)")]
    [Tooltip("Point-light range for the coloured glow built around the pickup at runtime.")]
    public float auraLightRange = 3f;
    [Tooltip("Point-light intensity for the coloured glow.")]
    public float auraLightIntensity = 1.6f;

    private float spawnTime;
    private Transform player;
    private Renderer[] renderers;  // all renderers under this pickup (cube = one; model = many)
    private Vector3 spawnPosition; // captured once so the bob oscillates without drifting
    private bool networked;
    private int networkId;

    // Runtime-only aura (never modifies prefab assets/materials). Built once in Start.
    private Light auraLight;
    private GameObject auraObject;
    private bool auraBuilt;

    public int NetworkId => networkId;
    public bool IsNetworked => networked;
    public float RemainingLifetime => Mathf.Max(0f, lifetime - (Time.time - spawnTime));
    public static IEnumerable<Powerup> ActiveNetworkedPowerups => NetworkedPowerups;

    /// <summary>Create a power-up pickup at a world position. Returns the new instance.</summary>
    public static Powerup Spawn(PowerupType type, Vector3 position, float lifetime, int networkId = 0, bool networked = false)
    {
        // Prefer the assigned 3D pickup model (resolved from the LOCAL PowerupManager, so server
        // and clients each use their own reference). Fall back to the original generated cube when
        // no prefab is assigned or no manager is present — pickups never break if refs are missing.
        GameObject prefab = PowerupManager.ResolvePickupPrefab(type);
        GameObject go;
        if (prefab != null)
        {
            // Instantiate the model AS-IS: its own rotation, scale, colliders and materials are
            // preserved (the visual model is never modified). Only move it to the spawn position.
            go = Object.Instantiate(prefab);
            go.transform.position = position;

            // The Powerup component (added below) lives on the ROOT, so OnTriggerEnter only fires
            // if the root carries a trigger collider. The prefabs ship with their own colliders, so
            // this ONLY adds a small fallback when the root itself has none — nothing is overwritten.
            EnsureRootTriggerCollider(go);
        }
        else
        {
            go = BuildFallbackCube(type, position);
        }

        go.name = "Powerup_" + type;

        // Reuse the prefab's own Powerup component if it already has one; otherwise add it at
        // runtime (the cube path never has one). Never duplicate it.
        Powerup p = go.GetComponent<Powerup>();
        if (p == null)
        {
            p = go.AddComponent<Powerup>();
        }
        p.type = type;
        p.lifetime = Mathf.Max(1f, lifetime);
        p.networkId = networkId;
        p.networked = networked;
        // Initialise spawnTime HERE (not in Start, which runs a frame later) so that
        // RemainingLifetime is already valid when the server broadcasts this powerup's spawn
        // on the same frame. Otherwise the broadcast reads spawnTime=0 and sends a bogus
        // (near-zero) lifetime, causing clients to despawn the pickup almost immediately.
        p.spawnTime = Time.time;
        p.spawnPosition = position;
        p.RegisterNetworked();
        return p;
    }

    // Original generated glowing cube — the fallback used when a type has no assigned prefab.
    // Behaviour is identical to the pre-prefab implementation.
    private static GameObject BuildFallbackCube(PowerupType type, Vector3 position)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
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

        return go;
    }

    // Ensure the pickup's ROOT has a trigger collider for OnTriggerEnter. If the root already
    // carries any trigger collider (as the pickup prefabs and the cube do), it is left untouched;
    // otherwise a small trigger sphere is added on the root. The sphere is a pickup volume only —
    // it does not resize or alter the visual model, and child colliders are never modified.
    private static void EnsureRootTriggerCollider(GameObject go)
    {
        Collider[] rootColliders = go.GetComponents<Collider>();
        foreach (Collider c in rootColliders)
        {
            if (c != null && c.isTrigger)
            {
                return;
            }
        }

        SphereCollider trigger = go.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 0.5f;
    }

    private void Start()
    {
        // Build the runtime aura BEFORE gathering renderers, so its ParticleSystemRenderer is
        // included in the flashed set and blinks together with the model in the final seconds.
        BuildAura();
        // Gather EVERY renderer under the pickup so the despawn flash works for multi-renderer
        // model prefabs as well as the single-renderer cube fallback (now including the aura).
        renderers = GetComponentsInChildren<Renderer>(true);
        // Capture the spawn position ONCE so the bob oscillates around it instead of
        // accumulating (which would make the pickup drift upward forever).
        spawnPosition = transform.position;
        FindPlayer();
    }

    // Build a small, subtle, runtime-only glow around the pickup: a coloured Point Light plus a
    // gentle looping particle aura. Created entirely in code (no prefab/material asset is touched)
    // and coloured from PowerupManager.ColorOf(type). Guarded so a re-enable never duplicates it.
    private void BuildAura()
    {
        if (auraBuilt)
        {
            return;
        }
        auraBuilt = true;

        Color color = PowerupManager.ColorOf(type);

        auraObject = new GameObject("Powerup_Aura");
        auraObject.transform.SetParent(transform, false);
        auraObject.transform.localPosition = Vector3.zero;

        // Coloured point-light glow.
        auraLight = auraObject.AddComponent<Light>();
        auraLight.type = LightType.Point;
        auraLight.color = color;
        auraLight.range = Mathf.Max(0.5f, auraLightRange);
        auraLight.intensity = Mathf.Max(0f, auraLightIntensity);
        auraLight.shadows = LightShadows.None;

        // Subtle looping particle aura (soft rising sparks in the power-up's colour).
        ParticleSystem ps = auraObject.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = 0.9f;
        main.startSpeed = 0.35f;
        main.startSize = 0.12f;
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 24;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 8f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.35f;

        // Bright unlit additive-ish material so the sparks read as a glow (URP-safe fallbacks).
        ParticleSystemRenderer psr = auraObject.GetComponent<ParticleSystemRenderer>();
        if (psr != null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) { shader = Shader.Find("Sprites/Default"); }
            if (shader == null) { shader = Shader.Find("Unlit/Color"); }
            if (shader != null)
            {
                psr.material = new Material(shader) { color = color };
            }
        }

        ps.Play();
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

        // Flash in the final 3 seconds — toggle ALL renderers (root + children, including the
        // aura's particle renderer) plus the aura light, so the glow blinks with the model and
        // never lights up while the pickup is invisible.
        if (renderers != null && lifetime - age < 3f)
        {
            bool visible = Mathf.FloorToInt(age * 6f) % 2 == 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].enabled = visible;
                }
            }
            if (auraLight != null)
            {
                auraLight.enabled = visible;
            }
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
