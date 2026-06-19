using UnityEngine;

/// <summary>
/// Simple buyable-style door (Call of Duty Zombies feel).
/// For now this only handles BLOCKING the player while closed and OPENING on
/// interaction. No points / money / pricing system is wired up yet - the
/// <see cref="cost"/> field is only a placeholder value stored for later.
///
/// Doors are created and positioned in the real doorway gaps by the editor
/// tool "Tools/School Of The Dead/Place Buyable Doors" (SchoolDoorPlacer).
/// </summary>
[RequireComponent(typeof(Collider))]
public class Door : MonoBehaviour
{
    [Header("Placeholder For Future Buy System (not used yet)")]
    [Tooltip("Stored placeholder cost for the future points system. Does nothing yet.")]
    public int cost = 0;
    [Tooltip("Identifier (usually the source doorway name) for hooking up logic later.")]
    public string doorId = "";

    [Header("Interaction")]
    [Tooltip("How close the player must be (world units) to open this door.")]
    public float interactionRange = 3f;
    [Tooltip("Key the player presses to open the door.")]
    public KeyCode interactKey = KeyCode.E;

    [Header("Open Behaviour")]
    [Tooltip("Local-space movement applied when the door opens (e.g. slide down into the floor).")]
    public Vector3 openMoveOffset = new Vector3(0f, -3f, 0f);
    [Tooltip("How fast the door slides to its open position.")]
    public float openSpeed = 6f;

    [Header("Linked Doors")]
    [Tooltip("Other doors that open together with this one (e.g. both ends of a stairwell). " +
             "Opening this door opens all of these too. Set automatically by the door placer.")]
    public Door[] linkedDoors;

    public bool IsOpen { get; private set; }

    private Transform player;
    private Collider doorCollider;
    private Vector3 closedLocalPosition;
    private Vector3 openLocalPosition;
    private bool isMoving;
    private float nextPromptTime;
    private bool playerInRange;

    private void Awake()
    {
        doorCollider = GetComponent<Collider>();
        closedLocalPosition = transform.localPosition;
        openLocalPosition = closedLocalPosition + openMoveOffset;
    }

    private void Start()
    {
        FindPlayer();
    }

    private void Update()
    {
        if (IsOpen)
        {
            if (isMoving)
            {
                AnimateOpen();
            }
            return;
        }

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

        if (Input.GetKeyDown(interactKey))
        {
            TryOpen();
        }
    }

    private void OnGUI()
    {
        if (IsOpen || !playerInRange)
        {
            return;
        }

        string label = cost > 0 ? $"Press E   [{cost}]" : "Press E   Open Door";

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
        };

        float w = 360f;
        float h = 34f;
        Rect rect = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.62f, w, h);

        // Drop shadow then the bright label for readability over any background.
        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.85f);
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), label, style);
        GUI.color = new Color(0.96f, 0.93f, 0.86f, 1f);
        GUI.Label(rect, label, style);
        GUI.color = prev;
    }

    /// <summary>
    /// Input-path open: charges points if a cost is set, then opens the door.
    /// Opens for free when cost is non-positive or no economy is present.
    /// </summary>
    private void TryOpen()
    {
        if (cost > 0 && PlayerPoints.Instance != null)
        {
            if (!PlayerPoints.Instance.TrySpend(cost))
            {
                if (Time.time >= nextPromptTime)
                {
                    Debug.Log($"Need {cost} points to open this door");
                    nextPromptTime = Time.time + 1.5f;
                }
                return;
            }
        }

        Open();
    }

    /// <summary>
    /// Opens the door: stops blocking the player and slides it out of the way.
    /// Public so a future points/buy system can call it directly.
    /// </summary>
    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        IsOpen = true;
        isMoving = true;
        playerInRange = false;

        // Stop blocking the player immediately.
        if (doorCollider != null)
        {
            doorCollider.enabled = false;
        }

        // Open every linked door too (e.g. both ends of a stairwell). The IsOpen
        // guard at the top of Open() prevents mutual links from looping forever.
        if (linkedDoors != null)
        {
            foreach (Door linked in linkedDoors)
            {
                if (linked != null && !linked.IsOpen)
                {
                    linked.Open();
                }
            }
        }
    }

    private void AnimateOpen()
    {
        transform.localPosition = Vector3.MoveTowards(transform.localPosition, openLocalPosition, openSpeed * Time.deltaTime);
        if ((transform.localPosition - openLocalPosition).sqrMagnitude < 0.0001f)
        {
            transform.localPosition = openLocalPosition;
            isMoving = false;
        }
    }

    private void FindPlayer()
    {
        // Prefer the CharacterController player (CoDMovement / PlayerMovement use one).
        CharacterController controller = FindFirstObjectByType<CharacterController>();
        if (controller != null)
        {
            player = controller.transform;
            return;
        }

        if (Camera.main != null)
        {
            player = Camera.main.transform;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = IsOpen ? Color.green : Color.red;
        Gizmos.DrawWireSphere(transform.position, interactionRange);
    }
}
