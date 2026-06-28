using UnityEngine;

/// <summary>
/// A collectible book for the secret Pack-a-Punch easter egg. Press E within range
/// to pick it up; it reports to <see cref="SecretBookManager"/> and disappears.
/// Gently spins and bobs so the player can spot it.
/// </summary>
[RequireComponent(typeof(Collider))]
public class BookPickup : MonoBehaviour
{
    [Tooltip("How close the player must be (world units) to pick up the book.")]
    public float interactionRange = 3f;
    [Tooltip("Key the player presses to pick up the book.")]
    public KeyCode interactKey = KeyCode.E;

    [Header("Idle Motion")]
    public float spinSpeed = 45f;
    public float bobHeight = 0.12f;
    public float bobSpeed = 2f;

    private Transform player;
    private bool collected;
    private bool playerInRange;
    private Vector3 basePosition;

    private void Start()
    {
        basePosition = transform.position;
        FindPlayer();
    }

    private void Update()
    {
        if (collected)
        {
            return;
        }

        // Idle spin + bob for visibility.
        transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        transform.position = basePosition + Vector3.up * (Mathf.Sin(Time.time * bobSpeed) * bobHeight);

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
            Collect();
        }
    }

    private void Collect()
    {
        collected = true;
        playerInRange = false;

        if (SecretBookManager.Instance != null)
        {
            SecretBookManager.Instance.CollectBook(this);
        }

        gameObject.SetActive(false);
    }

    private GUIStyle promptStyle;

    private void OnGUI()
    {
        if (collected || !playerInRange)
        {
            return;
        }

        if (promptStyle == null)
        {
            promptStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
        }
        GUIStyle style = promptStyle;

        float w = 360f;
        float h = 30f;
        Rect rect = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.66f, w, h);

        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.85f);
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), "Press E   Pick up book", style);
        GUI.color = new Color(0.96f, 0.93f, 0.86f, 1f);
        GUI.Label(rect, "Press E   Pick up book", style);
        GUI.color = prev;
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
