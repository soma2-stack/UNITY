using UnityEngine;

/// <summary>
/// Co-op revive: while standing near a DOWNED teammate, hold the revive key to fill a
/// progress bar; on completion the revive is routed through the SERVER
/// (<see cref="PlayerHealth.ReviveServerRpc"/>) so it stays authoritative. The progress
/// bar runs locally on the reviving client.
///
/// Solo players have no teammates, so this is a no-op in single-player (self-revive is
/// handled by PlayerHealth / RescueRush). Put this on the player alongside
/// PlayerMovement / PlayerHealth.
/// </summary>
public class ReviveInteraction : MonoBehaviour
{
    [Header("Revive")]
    [Tooltip("Key held to revive a downed teammate.")]
    public KeyCode reviveKey = KeyCode.E;
    [Tooltip("Max distance to a downed teammate to revive them.")]
    public float reviveRange = 3f;
    [Tooltip("Seconds of holding the key to complete a revive.")]
    public float reviveDuration = 4f;
    [Tooltip("Draw a simple on-screen revive progress bar while reviving.")]
    public bool showProgressBar = true;

    private PlayerHealth selfHealth;
    private NetworkPlayerAvatar avatar;
    private PlayerHealth currentTarget;
    private float progress;

    // Solo / not network-spawned: this peer always acts. In a session only the OWNER does.
    private bool IsLocalOwner
    {
        get
        {
            if (avatar == null)
            {
                avatar = GetComponent<NetworkPlayerAvatar>();
            }
            return avatar == null || !avatar.IsSpawned || avatar.IsOwner;
        }
    }

    private void Start()
    {
        selfHealth = GetComponent<PlayerHealth>();
    }

    private void Update()
    {
        if (!IsLocalOwner)
        {
            ResetProgress();
            return;
        }

        // Can't revive while you are downed or dead yourself.
        if (selfHealth != null && (selfHealth.IsDowned || selfHealth.IsDead))
        {
            ResetProgress();
            return;
        }

        PlayerHealth target = FindDownedTarget();
        if (target == null || !Input.GetKey(reviveKey))
        {
            ResetProgress();
            return;
        }

        if (target != currentTarget)
        {
            currentTarget = target;
            progress = 0f;
        }

        progress += Time.deltaTime;
        if (progress >= Mathf.Max(0.1f, reviveDuration))
        {
            CompleteRevive(target);
            ResetProgress();
        }
    }

    private void CompleteRevive(PlayerHealth target)
    {
        if (target.IsSpawned)
        {
            // Networked: the server authorizes and performs the revive on the target.
            target.ReviveServerRpc();
        }
        else
        {
            // Solo / not networked: revive directly.
            target.Revive();
        }
    }

    // Nearest downed (but not yet dead) OTHER player within range.
    private PlayerHealth FindDownedTarget()
    {
        PlayerHealth[] all = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
        PlayerHealth best = null;
        float bestDistance = float.MaxValue;
        foreach (PlayerHealth ph in all)
        {
            if (ph == null || ph == selfHealth)
            {
                continue;
            }
            if (!ph.IsDowned || ph.IsDead)
            {
                continue;
            }
            float distance = Vector3.Distance(transform.position, ph.transform.position);
            if (distance <= reviveRange && distance < bestDistance)
            {
                bestDistance = distance;
                best = ph;
            }
        }
        return best;
    }

    private void ResetProgress()
    {
        progress = 0f;
        currentTarget = null;
    }

    private void OnGUI()
    {
        if (!showProgressBar || currentTarget == null || progress <= 0f)
        {
            return;
        }

        float pct = Mathf.Clamp01(progress / Mathf.Max(0.1f, reviveDuration));
        const float w = 320f;
        const float h = 24f;
        float x = (Screen.width - w) * 0.5f;
        float y = Screen.height * 0.62f;

        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
        GUI.color = new Color(0.2f, 0.8f, 0.3f, 0.9f);
        GUI.DrawTexture(new Rect(x, y, w * pct, h), Texture2D.whiteTexture);
        GUI.color = prev;
        GUI.Label(new Rect(x, y - 22f, w, 20f), "Reviving teammate...");
    }
}
