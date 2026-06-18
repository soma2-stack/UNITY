using UnityEngine;

/// <summary>
/// Tiny on-screen points readout using legacy IMGUI (OnGUI).
/// No Canvas or prefab setup required. Reads from <see cref="PlayerPoints.Instance"/>.
/// </summary>
public class PointsHud : MonoBehaviour
{
    [Header("Layout")]
    [Tooltip("Screen-space rectangle (top-left origin) for the points label.")]
    public Rect labelRect = new Rect(10f, 10f, 200f, 30f);
    [Tooltip("Font size for the points label.")]
    public int fontSize = 22;

    private void OnGUI()
    {
        if (PlayerPoints.Instance == null)
        {
            return;
        }

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = fontSize;
        style.fontStyle = FontStyle.Bold;

        GUI.Label(labelRect, $"Points: {PlayerPoints.Instance.Points}", style);
    }
}
