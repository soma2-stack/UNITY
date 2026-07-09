using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Local, cosmetic-only Mystery Box reveal. When the purchasing player is granted a box weapon,
/// this flashes a small center-screen overlay that cycles weapon names quickly for ~1.5s and then
/// lands on the REAL prize the server already chose.
///
/// It never selects, rerolls, or grants anything — the authoritative prize is passed in as
/// <c>finalName</c> and the weapon is granted by <see cref="MysteryBox.ApplyMysteryResult"/>
/// independently of this display. Self-bootstraps in the gameplay scene (mirrors HitMarkerHud),
/// IMGUI only, so no Inspector/prefab/TextMeshPro setup is required. Trigger it with the static
/// <see cref="Show"/>, which is a safe no-op if no instance exists (the box still works without it).
/// </summary>
public class MysteryBoxRevealHud : MonoBehaviour
{
    private const string GameplayScene = "SchoolOfTheDead";
    private static MysteryBoxRevealHud _instance;

    // Timing (seconds, unscaled): fast name cycling, then a short "landed" hold on the real prize.
    private const float RollDuration = 1.15f;
    private const float LandDuration = 0.4f;
    private const float CycleInterval = 0.07f;
    private const float TotalDuration = RollDuration + LandDuration;

    private float startTime = -999f;
    private string finalName = "";
    private readonly List<string> rollNames = new List<string>();
    private Texture2D backTex;

    /// <summary>
    /// Play the reveal, landing on <paramref name="finalName"/> (the authoritative prize). The
    /// optional <paramref name="candidateNames"/> only feed the cosmetic cycling. Safe no-op if
    /// there is no live instance.
    /// </summary>
    public static void Show(string finalName, IReadOnlyList<string> candidateNames)
    {
        if (_instance != null)
        {
            _instance.Begin(finalName, candidateNames);
        }
    }

    private void Begin(string prizeName, IReadOnlyList<string> candidateNames)
    {
        finalName = string.IsNullOrEmpty(prizeName) ? "???" : prizeName;

        rollNames.Clear();
        if (candidateNames != null)
        {
            for (int i = 0; i < candidateNames.Count; i++)
            {
                if (!string.IsNullOrEmpty(candidateNames[i]))
                {
                    rollNames.Add(candidateNames[i]);
                }
            }
        }
        if (rollNames.Count == 0)
        {
            rollNames.Add(finalName);
        }

        startTime = Time.unscaledTime;
    }

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
            if (_instance != null)
            {
                Destroy(_instance.gameObject);
                _instance = null;
            }
            return;
        }

        if (_instance != null || FindFirstObjectByType<MysteryBoxRevealHud>() != null)
        {
            return;
        }

        var go = new GameObject("MysteryBoxRevealHud (Runtime)");
        _instance = go.AddComponent<MysteryBoxRevealHud>();
    }

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
        }
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    private void OnGUI()
    {
        float age = Time.unscaledTime - startTime;
        if (age < 0f || age > TotalDuration)
        {
            return; // idle / finished: nothing to draw
        }

        bool landed = age >= RollDuration;
        // During the roll, flick through candidate names; once landed, lock to the real prize.
        string label = landed
            ? finalName
            : rollNames[(int)(age / CycleInterval) % rollNames.Count];

        // Fade the whole overlay out over the last part of the landed hold.
        float alpha = 1f;
        if (landed)
        {
            alpha = 1f - Mathf.Clamp01((age - RollDuration) / Mathf.Max(0.01f, LandDuration));
            alpha = Mathf.Clamp01(alpha + 0.35f); // hold mostly-opaque, then fade at the very end
        }

        if (backTex == null)
        {
            backTex = Texture2D.whiteTexture;
        }

        // Centered panel just above the middle of the screen.
        float w = Mathf.Min(Screen.width * 0.6f, 520f);
        float h = 96f;
        float x = (Screen.width - w) * 0.5f;
        float y = Screen.height * 0.32f;

        Color prev = GUI.color;

        // Dim backing box.
        GUI.color = new Color(0f, 0f, 0f, 0.55f * alpha);
        GUI.DrawTexture(new Rect(x, y, w, h), backTex);

        // "Mystery Box" caption.
        var caption = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperCenter,
            fontSize = 16,
            fontStyle = FontStyle.Bold,
        };
        GUI.color = new Color(1f, 1f, 1f, 0.8f * alpha);
        GUI.Label(new Rect(x, y + 8f, w, 24f), "MYSTERY BOX", caption);

        // The rolling / landed weapon name.
        var name = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = landed ? 34 : 28,
            fontStyle = FontStyle.Bold,
        };
        Color landedColor = new Color(1f, 0.86f, 0.35f, alpha); // gold on landing
        Color rollColor = new Color(1f, 1f, 1f, 0.9f * alpha);
        GUI.color = landed ? landedColor : rollColor;
        GUI.Label(new Rect(x, y + 30f, w, h - 34f), label, name);

        GUI.color = prev;
    }
}
