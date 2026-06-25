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

    [Header("Placeholder For Future Buy System (default 0 = free)")]
    [Tooltip("Optional point cost. If > 0 and PlayerPoints exists, points are spent before teleporting. Defaults to free.")]
    public int cost = 0;

    [Header("UI")]
    [Tooltip("Show a small on-screen prompt while the player is in range.")]
    public bool showPrompt = true;

    private Transform player;
    private CharacterController playerController;
    private float nextPromptTime;
    private float readyTime;
    private bool playerInRange;

    private void Start()
    {
        FindPlayer();
    }

    private void Update()
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

        float distance = Vector3.Distance(transform.position, player.position);
        playerInRange = distance <= interactionRange;

        if (!playerInRange)
        {
            return;
        }

        if (Time.time < readyTime)
        {
            return;
        }

        // Throttled console prompt so it does not spam.
        if (Time.time >= nextPromptTime)
        {
            Debug.Log("Press " + interactKey + " to teleport");
            nextPromptTime = Time.time + 1.5f;
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

        // Optional cost. Only enforced if a positive cost is set AND a points
        // system is present, otherwise teleporting is free. Resolved via
        // reflection so this script compiles whether or not a PlayerPoints type
        // (public static PlayerPoints Instance; bool TrySpend(int)) exists.
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
    /// Moves the player to the target transform in a CharacterController-safe way:
    /// disable the controller, set position/rotation, then re-enable it. Setting
    /// position while the controller is enabled would be fought by its own movement.
    /// </summary>
    private void TeleportPlayerTo(Transform target)
    {
        if (player == null || target == null)
        {
            return;
        }

        bool controllerWasEnabled = false;
        if (playerController != null)
        {
            controllerWasEnabled = playerController.enabled;
            playerController.enabled = false;
        }

        player.position = target.position;
        if (matchDestinationRotation)
        {
            // Only yaw the body; pitch is owned by the child camera.
            Vector3 euler = player.eulerAngles;
            euler.y = target.eulerAngles.y;
            player.eulerAngles = euler;
        }

        if (playerController != null)
        {
            playerController.enabled = controllerWasEnabled;
        }
    }

    /// <summary>
    /// Tries to spend points via the <see cref="PlayerPoints"/> singleton. Returns
    /// true (free) when no economy is present, so the teleporter still works in a
    /// scene without a PlayerPoints instance.
    /// </summary>
    private static bool TrySpendPoints(int amount)
    {
        if (PlayerPoints.Instance == null)
        {
            return true; // No economy present: teleport is free.
        }

        return PlayerPoints.Instance.TrySpend(amount);
    }

    private void FindPlayer()
    {
        // The player is a GameObject with a CharacterController and a child camera.
        playerController = FindFirstObjectByType<CharacterController>();
        if (playerController != null)
        {
            player = playerController.transform;
            return;
        }

        // Fallback so the pad still works if no CharacterController is present.
        if (Camera.main != null)
        {
            player = Camera.main.transform;
        }
    }

    private void OnGUI()
    {
        if (!showPrompt || !playerInRange || Time.time < readyTime)
        {
            return;
        }

        const float width = 260f;
        const float height = 26f;
        Rect rect = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.75f, width, height);
        GUI.Label(rect, "Press " + interactKey + " to teleport");
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
