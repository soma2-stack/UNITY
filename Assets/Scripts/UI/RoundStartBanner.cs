using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Shows a centred "ROUND [N] STARTING" banner during the between-rounds intermission
/// (Call of Duty Zombies style), fading out over the last second as the next round
/// begins. Self-bootstraps in the "SchoolOfTheDead" gameplay scene (mirrors
/// PowerupManager / GameOverController) and is removed when leaving gameplay. IMGUI
/// only — no Canvas or manual scene setup required.
/// </summary>
public class RoundStartBanner : MonoBehaviour
{
    private const string GameplayScene = "SchoolOfTheDead";
    private static RoundStartBanner _runtimeInstance;

    [Tooltip("Seconds before the round starts at which the banner begins fading out.")]
    public float fadeStartThreshold = 1f;

    private GUIStyle bannerStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnAnySceneLoaded;
        SceneManager.sceneLoaded += OnAnySceneLoaded;
        SpawnIfGameplayScene(SceneManager.GetActiveScene());
    }

    private static void OnAnySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SpawnIfGameplayScene(scene);
    }

    private static void SpawnIfGameplayScene(Scene scene)
    {
        if (scene.name != GameplayScene)
        {
            if (_runtimeInstance != null)
            {
                Destroy(_runtimeInstance.gameObject);
                _runtimeInstance = null;
            }
            return;
        }

        if (_runtimeInstance != null || FindFirstObjectByType<RoundStartBanner>() != null)
        {
            return;
        }

        var go = new GameObject("RoundStartBanner (Runtime)");
        _runtimeInstance = go.AddComponent<RoundStartBanner>();
    }

    private void OnDestroy()
    {
        if (_runtimeInstance == this)
        {
            _runtimeInstance = null;
        }
    }

    private void OnGUI()
    {
        if (!RoundManager.IntermissionActive)
        {
            return;
        }

        EnsureStyle();

        // Full opacity for most of the intermission, fading to 0 over the last
        // fadeStartThreshold seconds so it smoothly disappears as the round begins.
        float threshold = Mathf.Max(0.01f, fadeStartThreshold);
        float alpha = Mathf.Clamp01(RoundManager.IntermissionRemaining / threshold);

        string text = "ROUND " + RoundManager.UpcomingRound + " STARTING";

        float w = 800f;
        float h = 90f;
        Rect rect = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.32f, w, h);

        Color prev = GUI.color;
        DrawShadowedLabel(rect, text, alpha);
        GUI.color = prev;
    }

    // Black drop shadow offset 2px, then the white banner on top.
    private void DrawShadowedLabel(Rect rect, string text, float alpha)
    {
        GUI.color = new Color(0f, 0f, 0f, alpha);
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, bannerStyle);
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.Label(rect, text, bannerStyle);
    }

    private void EnsureStyle()
    {
        if (bannerStyle == null)
        {
            bannerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 58,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
        }
    }
}
