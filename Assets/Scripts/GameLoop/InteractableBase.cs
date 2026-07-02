using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared base for "Press E" world interactables (Mystery Box, Pack-a-Punch, Power
/// Switch, Wall Buy). Mirrors the project convention used by Door / PerkMachine:
/// find the player via the CharacterController, show an OnGUI prompt when in range,
/// and act on KeyCode.E. Charging is handled by subclasses via PlayerPoints.
///
/// Everything is null-safe: with no player / no PlayerPoints the interactable still
/// works (charging is treated as free when PlayerPoints is missing).
/// </summary>
public abstract class InteractableBase : MonoBehaviour
{
    private static readonly Dictionary<string, InteractableBase> Registry = new Dictionary<string, InteractableBase>();

    [Header("Interaction")]
    [Tooltip("How close the player must be (world units) to interact.")]
    public float interactionRange = 3f;
    [Tooltip("Key the player presses to interact.")]
    public KeyCode interactKey = KeyCode.E;

    protected Transform player;
    protected bool playerInRange;
    private float nextMessageTime;
    private GUIStyle promptStyle;
    private string networkKey;

    public string NetworkKey
    {
        get
        {
            if (string.IsNullOrEmpty(networkKey))
            {
                networkKey = BuildNetworkKey(transform);
            }
            return networkKey;
        }
    }

    /// <summary>Prompt shown when in range. Return null/empty to hide the prompt.</summary>
    protected abstract string GetPromptText();

    /// <summary>Called when the player presses the interact key while in range.</summary>
    protected abstract void OnInteract();

    protected virtual void OnEnable()
    {
        Registry[NetworkKey] = this;
    }

    protected virtual void OnDisable()
    {
        if (!string.IsNullOrEmpty(networkKey) &&
            Registry.TryGetValue(networkKey, out InteractableBase registered) &&
            registered == this)
        {
            Registry.Remove(networkKey);
        }
    }

    protected virtual void Start()
    {
        FindPlayer();
    }

    protected virtual void Update()
    {
        // Always track the LOCAL player. In multiplayer the local player spawns/registers
        // AFTER this scene object's first Update, so we must keep re-resolving — otherwise
        // we latch onto a stale/remote transform (or Camera.main) and the in-range check
        // never matches the real player, so the prompt never shows and E does nothing.
        FindPlayer();
        if (player == null)
        {
            playerInRange = false;
            return;
        }

        playerInRange = InRange(transform.position, player.position, interactionRange);
        if (!playerInRange)
        {
            return;
        }

        if (Input.GetKeyDown(interactKey))
        {
            OnInteract();
        }
    }

    /// <summary>Charge points if affordable. Returns true if paid (or free). Logs on failure.</summary>
    protected bool TryCharge(int cost)
    {
        if (cost <= 0 || PlayerPoints.Instance == null)
        {
            return true;
        }

        if (PlayerPoints.Instance.TrySpend(cost))
        {
            return true;
        }

        if (Time.time >= nextMessageTime)
        {
            Debug.Log("[" + GetType().Name + "] Need " + cost + " points.");
            nextMessageTime = Time.time + 1.5f;
        }
        return false;
    }

    protected virtual void OnGUI()
    {
        if (!playerInRange)
        {
            return;
        }

        string label = GetPromptText();
        if (string.IsNullOrEmpty(label))
        {
            return;
        }

        // Cache the style once instead of allocating a GUIStyle every OnGUI frame.
        if (promptStyle == null)
        {
            promptStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
        }
        GUIStyle style = promptStyle;

        float w = 520f;
        float h = 34f;
        Rect rect = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.62f, w, h);

        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.85f);
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), label, style);
        GUI.color = new Color(0.96f, 0.93f, 0.86f, 1f);
        GUI.Label(rect, label, style);
        GUI.color = prev;
    }

    /// <summary>
    /// Interaction proximity that tolerates the interactable's pivot being at a very
    /// different height than the floor-standing player. Door / machine pivots sit 1.5-6m
    /// up (or even below the floor), while the player stands at ~y=0, so a plain 3D
    /// distance blows past the small interaction range even when you're right next to it.
    /// This uses HORIZONTAL (XZ) distance against the range, plus a vertical tolerance so
    /// you still can't reach an interactable a whole floor above/below you.
    /// </summary>
    public static bool InRange(Vector3 selfPos, Vector3 playerPos, float range, float verticalTolerance = 4f)
    {
        float dx = selfPos.x - playerPos.x;
        float dz = selfPos.z - playerPos.z;
        float horizontal = Mathf.Sqrt(dx * dx + dz * dz);
        return horizontal <= range && Mathf.Abs(selfPos.y - playerPos.y) <= verticalTolerance;
    }

    protected void FindPlayer()
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

    public static bool TryFind<T>(string key, out T interactable) where T : InteractableBase
    {
        if (!string.IsNullOrEmpty(key) &&
            Registry.TryGetValue(key, out InteractableBase found) &&
            found is T typed)
        {
            interactable = typed;
            return true;
        }

        interactable = null;
        return false;
    }

    public static IEnumerable<T> FindAll<T>() where T : InteractableBase
    {
        foreach (InteractableBase interactable in Registry.Values)
        {
            if (interactable is T typed)
            {
                yield return typed;
            }
        }
    }

    public static string BuildNetworkKey(Transform target)
    {
        if (target == null)
        {
            return string.Empty;
        }

        string key = target.name;
        Transform parent = target.parent;
        while (parent != null)
        {
            key = parent.name + "/" + key;
            parent = parent.parent;
        }
        return key;
    }

    protected virtual void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactionRange);
    }
}
