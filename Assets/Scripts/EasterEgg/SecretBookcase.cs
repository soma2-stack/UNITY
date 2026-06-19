using UnityEngine;

/// <summary>
/// The hidden bookcase that conceals the secret stairwell down to Pack-a-Punch.
/// It cannot be opened directly - only <see cref="SecretBookManager"/> opens it
/// once all 6 books are found. On open it slides aside and disables its blocker so
/// the player can descend.
/// </summary>
public class SecretBookcase : MonoBehaviour
{
    [Tooltip("Local-space movement applied when the bookcase opens (slides aside).")]
    public Vector3 openMoveOffset = new Vector3(2.5f, 0f, 0f);
    [Tooltip("How fast the bookcase slides open.")]
    public float openSpeed = 2f;
    [Tooltip("Optional object that blocks the stairwell while closed (disabled on open).")]
    public GameObject blocker;

    public bool IsOpen { get; private set; }

    private Vector3 closedLocalPosition;
    private Vector3 openLocalPosition;
    private bool isMoving;

    private void Awake()
    {
        closedLocalPosition = transform.localPosition;
        openLocalPosition = closedLocalPosition + openMoveOffset;
    }

    /// <summary>Slides the bookcase aside and opens the stairwell. Idempotent.</summary>
    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        IsOpen = true;
        isMoving = true;

        if (blocker != null)
        {
            Collider blockerCollider = blocker.GetComponent<Collider>();
            if (blockerCollider != null)
            {
                blockerCollider.enabled = false;
            }
            else
            {
                blocker.SetActive(false);
            }
        }
    }

    private void Update()
    {
        if (!isMoving)
        {
            return;
        }

        transform.localPosition = Vector3.MoveTowards(transform.localPosition, openLocalPosition, openSpeed * Time.deltaTime);
        if ((transform.localPosition - openLocalPosition).sqrMagnitude < 0.0001f)
        {
            transform.localPosition = openLocalPosition;
            isMoving = false;
        }
    }
}
