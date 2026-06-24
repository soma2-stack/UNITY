using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tiny center-screen HIT MARKER (Call of Duty style): a small white "X" of four
/// diagonal strokes that flashes for ~0.12s whenever the player lands a hit on a
/// zombie. Self-bootstraps in the gameplay scene (mirrors GameHud / PowerupManager),
/// IMGUI only.
///
/// It does NOT draw a crosshair - GameHud owns the crosshair. Trigger it from gameplay
/// code with the static <see cref="Show"/> (safe to call even if no instance exists).
/// </summary>
public class HitMarkerHud : MonoBehaviour
{
    private const string GameplayScene = "SchoolOfTheDead";
    private static HitMarkerHud _instance;

    [Tooltip("Seconds the hit marker stays on screen after a hit.")]
    public float markerDuration = 0.12f;
    [Tooltip("Length of each diagonal stroke, in pixels.")]
    public float markerSize = 9f;
    [Tooltip("Stroke thickness, in pixels.")]
    public float markerThickness = 2f;
    [Tooltip("Gap from the exact center to the inner end of each stroke, in pixels " +
             "(frames the crosshair without covering it).")]
    public float markerGap = 4f;

    private float lastHitTime = -999f;
    private Texture2D whiteTex;

    /// <summary>Flash the hit marker now. Safe no-op if there is no live instance.</summary>
    public static void Show()
    {
        if (_instance != null)
        {
            _instance.lastHitTime = Time.unscaledTime;
        }
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

        if (_instance != null || FindFirstObjectByType<HitMarkerHud>() != null)
        {
            return;
        }

        var go = new GameObject("HitMarkerHud (Runtime)");
        _instance = go.AddComponent<HitMarkerHud>();
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
        float duration = Mathf.Max(0.01f, markerDuration);
        float age = Time.unscaledTime - lastHitTime;
        if (age > duration)
        {
            return;
        }

        if (whiteTex == null)
        {
            whiteTex = Texture2D.whiteTexture;
        }

        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.5f;
        float alpha = 1f - Mathf.Clamp01(age / duration); // fade out over its lifetime

        Color prevColor = GUI.color;
        Matrix4x4 prevMatrix = GUI.matrix;
        GUI.color = new Color(1f, 1f, 1f, alpha);

        // Classic four-stroke "X": two diagonals, each with a stroke on either side of
        // a small center gap so it frames (not covers) the crosshair.
        DrawStroke(cx, cy, 45f, +markerGap);
        DrawStroke(cx, cy, 45f, -(markerGap + markerSize));
        DrawStroke(cx, cy, 135f, +markerGap);
        DrawStroke(cx, cy, 135f, -(markerGap + markerSize));

        GUI.matrix = prevMatrix;
        GUI.color = prevColor;
    }

    // Draws one short bar of length markerSize starting at `offset` pixels along an
    // axis rotated by angleDeg about the screen center.
    private void DrawStroke(float cx, float cy, float angleDeg, float offset)
    {
        Matrix4x4 m = GUI.matrix;
        GUIUtility.RotateAroundPivot(angleDeg, new Vector2(cx, cy));
        GUI.DrawTexture(new Rect(cx + offset, cy - markerThickness * 0.5f, markerSize, markerThickness), whiteTex);
        GUI.matrix = m;
    }
}
