// ✅ INTERACTABLES AUDIT FIXES
using UnityEngine;

/// <summary>
/// Simple Call of Duty / Kino der Toten style teleport pad.
/// When the player stands on (or near) the pad and presses the interact key,
/// they are teleported to <see cref="destination"/>.
///
/// The player is expected to be a GameObject with a <see cref="CharacterController"/>
/// (see PlayerMovement). A CharacterController fights direct transform moves while
/// enabled, so we disable it, move the transform, then re-enable it.
///
/// Set up two pads (one in teleporter_room, one in the_vault) and assign each
/// other's <see cref="linkedReturn"/> to make a two-way teleporter. Both links are
/// optional and null-safe.
/// </summary>
public class Teleporter : MonoBehaviour
{
    [Header("Destination")]
    [Tooltip("Where the player ends up. Place an empty GameObject in the target room and assign it here.")]
    public Transform destination;
    [Tooltip("If true, the player's rotation is set to match the destination's rotation on arrival.")]
    public bool matchDestinationRotation = true;

    [Header("Interaction")]
    [Tooltip("How close the player must be (world units) to use this pad.")]
    public float interactionRange = 3f;
    [Tooltip("Key the player presses to teleport.")]
    public KeyCode interactKey = KeyCode.E;
    [Tooltip("Seconds before this pad can be used again after a teleport.")]
    public float cooldown = 1f;

    [Header("Optional Two-Way Link")]
    [Tooltip("Optional pad that sends the player back. Used to suppress instant re-teleport on arrival.")]
    public Teleporter linkedReturn;

    [Header("Buy Cost")]
    [Tooltip("Optional point cost. If > 0 and PlayerPoints exists, points are spent before teleporting. Defaults to free.")]
    public int cost = 0;

    [Header("UI")]
    [Tooltip("Show a small on-screen prompt while the player is in range.")]
    public bool showPrompt = true;

    private Transform[] players;
    private CharacterController[] playerControllers;
    private Transform nearestPlayer;
    private CharacterController nearestController;
    private float readyTime;
    private bool playerInRange;
    private GUIStyle promptStyle;

    private void Start()
    {
        FindPlayers();
    }

    private void Update()
    {
        if (players == null || players.Length == 0)
        {
            FindPlayers();
            if (players == null || players.Length == 0)
            {
                playerInRange = false;
                nearestPlayer = null;
                nearestController = null;
                return;
            }
        }

        // Co-op aware: find the closest player within range (and its controller).
        // TODO: in full co-op, each player needs individual interact input — for now nearest player triggers
        nearestPlayer = null;
        nearestController = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < players.Length; i++)
        {
            Transform p = players[i];
            if (p == null)
            {
                continue;
            }
            float distance = Vector3.Distance(transform.position, p.position);
            if (distance <= interactionRange && distance < bestDistance)
            {
                bestDistance = distance;
                nearestPlayer = p;
                nearestController = (playerControllers != null && i < playerControllers.Length)
                    ? playerControllers[i]
                    : null;
            }
        }

        playerInRange = nearestPlayer != null;
        if (!playerInRange)
        {
            return;
        }

        if (Time.time < readyTime)
        {
            return;
        }

        if (Input.GetKeyDown(interactKey))
        {
            TryTeleport();
        }
    }

    /// <summary>
    /// Attempts the teleport: charges points if a cost is set and PlayerPoints
    /// exists, then moves the player. Public so other systems can trigger it.
    /// </summary>
    public void TryTeleport()
    {
        if (destination == null)
        {
            Debug.LogWarning("Teleporter '" + name + "' has no destination assigned; ignoring teleport.");
            return;
        }

        // PlayerPoints.Instance is null-safe; if no economy exists the teleport is free.
        if (cost > 0 && !TrySpendPoints(cost))
        {
            Debug.Log("Not enough points to teleport (need " + cost + ").");
            return;
        }

        TeleportPlayerTo(destination);

        // Cool down this pad. Also cool down the linked return pad so the player
        // does not immediately get sent back from the destination.
        readyTime = Time.time + cooldown;
        if (linkedReturn != null)
        {
            linkedReturn.BlockFor(cooldown);
        }

        Debug.Log("Teleported");
    }

    /// <summary>
    /// Prevents this pad from teleporting for the given number of seconds.
    /// Used by a linked pad so the player is not bounced straight back.
    /// </summary>
    public void BlockFor(float seconds)
    {
        readyTime = Mathf.Max(readyTime, Time.time + seconds);
    }

    /// <summary>
    /// Moves the nearest player to the target transform in a CharacterController-safe
    /// way: disable the controller, set position/rotation, then re-enable it. Setting
    /// position while the controller is enabled would be fought by its own movement.
    /// </summary>
    private void TeleportPlayerTo(Transform target)
    {
        if (nearestPlayer == null || target == null)
        {
            return;
        }

        bool controllerWasEnabled = false;
        if (nearestController != null)
        {
            controllerWasEnabled = nearestController.enabled;
            nearestController.enabled = false;
        }

        nearestPlayer.position = target.position;
        if (matchDestinationRotation)
        {
            // Only yaw the body; pitch is owned by the child camera.
            Vector3 euler = nearestPlayer.eulerAngles;
            euler.y = target.eulerAngles.y;
            nearestPlayer.eulerAngles = euler;
        }

        if (nearestController != null)
        {
            nearestController.enabled = controllerWasEnabled;
        }
    }

    /// <summary>
    /// Tries to spend points via the <see cref="PlayerPoints"/> singleton. Returns
    /// true (free) when no economy is present, so the teleporter still works in a
    /// scene without a PlayerPoints instance.
    /// </summary>
    private static bool TrySpendPoints(int amount)
    {
        // PlayerPoints.Instance is null-safe; if no economy exists the teleport is free.
        if (PlayerPoints.Instance == null)
        {
            return true; // No economy present: teleport is free.
        }

        return PlayerPoints.Instance.TrySpend(amount);
    }

    private void FindPlayers()
    {
        // Co-op aware: gather EVERY CharacterController player (and its transform) so
        // any player can use the pad. The arrays stay index-aligned.
        CharacterController[] controllers = FindObjectsByType<CharacterController>(FindObjectsSortMode.None);
        if (controllers != null && controllers.Length > 0)
        {
            playerControllers = controllers;
            players = new Transform[controllers.Length];
            for (int i = 0; i < controllers.Length; i++)
            {
                players[i] = controllers[i] != null ? controllers[i].transform : null;
            }
            return;
        }

        // Fallback so the pad still works if no CharacterController is present.
        if (Camera.main != null)
        {
            players = new[] { Camera.main.transform };
            playerControllers = new CharacterController[] { null };
        }
    }

    // Lazily build the prompt style once (mirrors Door.EnsureStyles) so OnGUI
    // doesn't allocate a new GUIStyle every frame the player is in range.
    private void EnsureStyles()
    {
        if (promptStyle == null)
        {
            promptStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
        }
    }

    private void OnGUI()
    {
        if (!showPrompt || !playerInRange || Time.time < readyTime)
        {
            return;
        }

        EnsureStyles();

        string label = cost > 0 ? $"Press E   Teleport   [{cost}]" : "Press E   Teleport";
        GUIStyle style = promptStyle;

        float w = 360f;
        float h = 34f;
        Rect rect = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.62f, w, h);

        // Drop shadow then the bright label for readability over any background.
        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.85f);
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), label, style);
        GUI.color = new Color(0.96f, 0.93f, 0.86f, 1f);
        GUI.Label(rect, label, style);

        // Can't afford it: a red "NEED MORE POINTS" warning beneath the prompt.
        if (cost > 0 && PlayerPoints.Instance != null && !PlayerPoints.Instance.CanAfford(cost))
        {
            Rect warn = new Rect(rect.x, rect.y + h, w, 28f);
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(warn.x + 2f, warn.y + 2f, warn.width, warn.height), "NEED MORE POINTS", style);
            GUI.color = new Color(0.95f, 0.25f, 0.25f, 1f);
            GUI.Label(warn, "NEED MORE POINTS", style);
        }

        GUI.color = prev;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactionRange);
        if (destination != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, destination.position);
            Gizmos.DrawWireSphere(destination.position, 0.5f);
        }
    }
}
