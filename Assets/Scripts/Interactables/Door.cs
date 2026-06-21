using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Buyable-style door (Call of Duty Zombies feel): blocks the player while closed
/// and, on interaction, charges <see cref="cost"/> points (if any) via
/// <see cref="PlayerPoints"/> before opening. Buying a door also rewards points
/// (+10 per 100 spent), and any <see cref="linkedDoors"/> open along with it.
///
/// Doors are created and positioned in the real doorway gaps by the editor
/// tool "Tools/School Of The Dead/Place Buyable Doors" (SchoolDoorPlacer).
/// </summary>
[RequireComponent(typeof(Collider))]
public class Door : MonoBehaviour
{
    [Header("Buy Cost")]
    [Tooltip("Points required to open this door. 0 = opens for free. Buying spends this " +
             "many points (via PlayerPoints) and rewards +10 points per 100 spent.")]
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
    private NavMeshObstacle navObstacle;
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

        SetupNavObstacle();
    }

    /// <summary>
    /// Adds (or reuses) a carving <see cref="NavMeshObstacle"/> so a CLOSED door
    /// cuts a hole in the baked NavMesh - zombies cannot path through it. The
    /// obstacle is sized to the door's own footprint (the door cube is scaled to
    /// the doorway), and carving is enabled so the navmesh is actually removed
    /// rather than just steered around. <see cref="Open"/> disables this obstacle
    /// so the navmesh reconnects and zombies can chase through the opening.
    ///
    /// The NavMesh is baked WITH every doorway open (the door geometry is excluded
    /// from the bake via a dedicated layer in SchoolGameplaySetup); these carving
    /// obstacles are what actually block the closed doorways at runtime.
    /// </summary>
    private void SetupNavObstacle()
    {
        navObstacle = GetComponent<NavMeshObstacle>();
        if (navObstacle == null)
        {
            navObstacle = gameObject.AddComponent<NavMeshObstacle>();
        }

        navObstacle.shape = NavMeshObstacleShape.Box;
        // size is local-space and is scaled by the transform's lossyScale. The door
        // cube's localScale already matches the doorway (width, height, thickness),
        // so a unit box centred on the door carves exactly the opening. Pad the depth
        // a little so the carve reliably spans the doorway gap.
        navObstacle.center = Vector3.zero;
        navObstacle.size = new Vector3(1f, 1f, 1.5f);
        navObstacle.carving = true;
        // Carve immediately and keep it carved while closed (it never moves).
        navObstacle.carveOnlyStationary = false;
        navObstacle.enabled = !IsOpen;
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

        string label = cost > 0 ? $"Press E   Buy Door   [{cost}]" : "Press E   Open Door";

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

    /// <summary>
    /// Input-path open: for a paid door, blocks unless the player can afford it,
    /// spends the points, then opens and rewards +10 points per 100 spent. Free
    /// doors (cost &lt;= 0) just open. Opens for free when no economy is present.
    /// </summary>
    private void TryOpen()
    {
        // Free door: open immediately, no charge or reward.
        if (cost <= 0)
        {
            Open();
            return;
        }

        // Paid door: must be able to afford it (when an economy exists).
        if (PlayerPoints.Instance != null)
        {
            if (!PlayerPoints.Instance.TrySpend(cost))
            {
                if (Time.time >= nextPromptTime)
                {
                    Debug.Log($"Need {cost} points to open this door");
                    nextPromptTime = Time.time + 1.5f;
                }
                return; // can't afford: block the open
            }
        }

        Open();

        // Classic CoD door-buy reward: +10 points per 100 spent (e.g. 750 -> 75).
        int reward = Mathf.RoundToInt(cost / 10f);
        if (reward > 0)
        {
            PlayerPoints.Instance?.AddPoints(reward);
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
        playerInRange = false;

        // Stop blocking the player immediately.
        if (doorCollider != null)
        {
            doorCollider.enabled = false;
        }

        // Stop blocking zombies: disabling the carving obstacle lets the baked
        // NavMesh reconnect across this doorway so agents can path through.
        if (navObstacle != null)
        {
            navObstacle.enabled = false;
        }

        // The doorway just opened: force every live, on-mesh zombie to recompute its
        // path immediately so none stay stuck on the now-stale blocked route.
        ZombieAgent[] zombies = FindObjectsByType<ZombieAgent>(FindObjectsSortMode.None);
        foreach (ZombieAgent z in zombies)
        {
            if (z != null && !z.IsDead && z.IsOnNavMesh)
            {
                z.ForceRepath();
            }
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
