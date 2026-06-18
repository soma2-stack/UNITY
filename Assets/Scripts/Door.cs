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

    public bool IsOpen { get; private set; }

    private Transform player;
    private Collider doorCollider;
    private Vector3 closedLocalPosition;
    private Vector3 openLocalPosition;
    private bool isMoving;
    private float nextPromptTime;

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
                return;
            }
        }

        float distance = Vector3.Distance(transform.position, player.position);
        if (distance > interactionRange)
        {
            return;
        }

        // Simple placeholder prompt (throttled so it does not spam the console).
        if (Time.time >= nextPromptTime)
        {
            Debug.Log("Press E to open door");
            nextPromptTime = Time.time + 1.5f;
        }

        if (Input.GetKeyDown(interactKey))
        {
            Open();
        }
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

        // Stop blocking the player immediately.
        if (doorCollider != null)
        {
            doorCollider.enabled = false;
        }

        Debug.Log("Door opened");
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
