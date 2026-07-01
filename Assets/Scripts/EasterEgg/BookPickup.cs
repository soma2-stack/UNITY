using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A collectible book for the secret Pack-a-Punch easter egg. Press E within range
/// to pick it up; it reports to <see cref="SecretBookManager"/> and disappears.
/// Gently spins and bobs so the player can spot it.
///
/// In multiplayer the pickup is TEAM-WIDE: collecting is routed through
/// <see cref="NetworkGameplayCoordinator"/> so the server counts each book once for the
/// whole team and every peer hides the same book and advances the same progress. Solo
/// keeps collecting directly (the coordinator falls back to a local apply).
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
    private bool collected;   // this book has been picked up (input disabled)
    private bool counted;     // this book has been counted with SecretBookManager (once)
    private bool playerInRange;
    private Vector3 basePosition;

    // Stable, cross-peer key so the server/clients agree on WHICH book was collected.
    // Every peer loads the identical scene, so the hierarchy path + authored position
    // resolve to the same key on all machines.
    private static readonly Dictionary<string, BookPickup> Registry = new Dictionary<string, BookPickup>();
    private string networkKey;

    public string NetworkKey
    {
        get
        {
            if (string.IsNullOrEmpty(networkKey))
            {
                networkKey = BuildNetworkKey();
            }
            return networkKey;
        }
    }

    private void Awake()
    {
        // Capture the authored position BEFORE the idle bob moves it, so the key is stable.
        basePosition = transform.position;
        networkKey = BuildNetworkKey();
        Registry[networkKey] = this;
    }

    private void OnDestroy()
    {
        if (!string.IsNullOrEmpty(networkKey) &&
            Registry.TryGetValue(networkKey, out BookPickup registered) &&
            registered == this)
        {
            Registry.Remove(networkKey);
        }
    }

    private void Start()
    {
        FindPlayer();
    }

    public static bool TryFind(string key, out BookPickup book)
    {
        if (!string.IsNullOrEmpty(key) && Registry.TryGetValue(key, out book) && book != null)
        {
            return true;
        }
        book = null;
        return false;
    }

    private string BuildNetworkKey()
    {
        string path = name;
        Transform parent = transform.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        // Append the rounded authored position so books that share a name/parent path are
        // still distinct (and identical across peers).
        return "BOOK:" + path + "@" +
               Mathf.RoundToInt(basePosition.x * 10f) + "," +
               Mathf.RoundToInt(basePosition.y * 10f) + "," +
               Mathf.RoundToInt(basePosition.z * 10f);
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

        // Re-resolve the LOCAL player every frame — in multiplayer the player spawns after
        // this pickup, so a one-time cache latches onto nothing/Camera.main and the book
        // can never be collected.
        FindPlayer();
        if (player == null)
        {
            playerInRange = false;
            return;
        }

        // Horizontal distance + vertical tolerance so the book's bob height / elevated
        // placement doesn't push the floor-standing player out of range.
        playerInRange = InteractableBase.InRange(transform.position, player.position, interactionRange);
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
        if (collected)
        {
            return;
        }

        // Stop this book re-triggering while the request is in flight. The AUTHORITATIVE,
        // team-wide count + hide comes back through ApplyCollected (via the coordinator),
        // which dedups so a book is only ever counted once for the whole team. In solo the
        // coordinator applies it immediately.
        collected = true;
        playerInRange = false;
        NetworkGameplayCoordinator.RequestBookCollect(this);
    }

    /// <summary>
    /// Apply the (team-wide) collected state to THIS peer: count the book with the local
    /// <see cref="SecretBookManager"/> exactly once and hide it. Called by the coordinator
    /// on every peer when the server confirms the collection (and directly in solo).
    /// Idempotent — safe to call more than once (e.g. late-join catch-up).
    /// </summary>
    public void ApplyCollected()
    {
        collected = true;
        playerInRange = false;

        if (!counted)
        {
            counted = true;
            if (SecretBookManager.Instance != null)
            {
                SecretBookManager.Instance.CollectBook(this);
            }
        }

        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }
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
