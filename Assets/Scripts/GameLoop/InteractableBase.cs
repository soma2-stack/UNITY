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
    [Header("Interaction")]
    [Tooltip("How close the player must be (world units) to interact.")]
    public float interactionRange = 3f;
    [Tooltip("Key the player presses to interact.")]
    public KeyCode interactKey = KeyCode.E;

    protected Transform player;
    protected bool playerInRange;
    private float nextMessageTime;
    private GUIStyle promptStyle;

    /// <summary>Prompt shown when in range. Return null/empty to hide the prompt.</summary>
    protected abstract string GetPromptText();

    /// <summary>Called when the player presses the interact key while in range.</summary>
    protected abstract void OnInteract();

    protected virtual void Start()
    {
        FindPlayer();
    }

    protected virtual void Update()
    {
        if (player == null)
        {
            FindPlayer();
            if (player == null)
            {
                playerInRange = false;
                return;
            }
        }

        playerInRange = Vector3.Distance(transform.position, player.position) <= interactionRange;
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

    protected virtual void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactionRange);
    }
}
