using UnityEngine;

/// <summary>
/// Bottom-left on-screen message for the secret easter egg (e.g. "A Door has
/// opened", or book progress). Drawn with legacy IMGUI so it needs no Canvas.
/// Auto-creates itself on demand via <see cref="Ensure"/>.
/// </summary>
public class SecretEggHud : MonoBehaviour
{
    public static SecretEggHud Instance { get; private set; }

    private string message = string.Empty;
    private float hideAt;

    /// <summary>Returns the live HUD, creating one if it does not exist yet.</summary>
    public static SecretEggHud Ensure()
    {
        if (Instance == null)
        {
            GameObject go = new GameObject("SecretEggHud");
            Instance = go.AddComponent<SecretEggHud>();
        }
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>Shows a bottom-left message for the given number of seconds.</summary>
    public void ShowMessage(string text, float seconds)
    {
        message = text;
        hideAt = Time.time + Mathf.Max(0.1f, seconds);
    }

    private GUIStyle messageStyle;

    private void OnGUI()
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        if (Time.time >= hideAt)
        {
            message = string.Empty;
            return;
        }

        if (messageStyle == null)
        {
            messageStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.LowerLeft,
            };
        }
        GUIStyle style = messageStyle;

        float w = 720f;
        float h = 48f;
        Rect rect = new Rect(28f, Screen.height - h - 28f, w, h);

        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.85f);
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), message, style);
        GUI.color = new Color(0.93f, 0.16f, 0.12f, 1f);
        GUI.Label(rect, message, style);
        GUI.color = prev;
    }
}
