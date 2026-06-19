using UnityEngine;

/// <summary>
/// Superseded by <see cref="GameHud"/>, which now draws the consolidated on-screen
/// HUD (points, weapon, round, zombie counter, crosshair) via a single OnGUI.
///
/// This component is intentionally left as a harmless no-op so existing scene
/// references / GameObjects do not break. It no longer draws anything.
/// Prefer adding <see cref="GameHud"/> instead.
/// </summary>
public class PointsHud : MonoBehaviour
{
    [Header("Layout (unused — see GameHud)")]
    [Tooltip("Legacy field kept for inspector compatibility. No longer used.")]
    public Rect labelRect = new Rect(10f, 10f, 200f, 30f);
    [Tooltip("Legacy field kept for inspector compatibility. No longer used.")]
    public int fontSize = 22;

    // No OnGUI: GameHud now owns all HUD drawing to avoid duplicate UI.
}
