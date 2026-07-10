using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tiny editor helper for the LIGHTING PREVIEW scene only. Drops a simple, self-contained
/// first-person test player into the currently-open SchoolOfTheDead_LightingPreview scene so you
/// can press Play and walk around to judge the lighting.
///
/// It builds the player from EXISTING standalone components (CharacterController + CoDMovement,
/// and a child Camera + CoDCamera) — no new runtime scripts, no networking, and it does NOT touch
/// any prefab, asset, or other scene. Nothing is written to disk: the player is added to the open
/// scene in memory (marked dirty) so you can save it if you want or just discard it. Undo removes it.
/// </summary>
public static class LightingPreviewTools
{
    private const string PreviewScene = "SchoolOfTheDead_LightingPreview";
    private const string MenuPath = "Tools/Lighting Preview/Add Test Player";

    [MenuItem(MenuPath)]
    private static void AddTestPlayer()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != PreviewScene)
        {
            EditorUtility.DisplayDialog(
                "Add Test Player",
                "Open the '" + PreviewScene + "' scene first.\n\nThis tool only adds a player to that lighting-preview scene.",
                "OK");
            return;
        }

        Vector3 pos = GuessSpawnPosition();

        // Body: CharacterController + the standalone CoDMovement (WASD + Shift sprint + Space jump).
        var root = new GameObject("LightingPreview_TestPlayer");
        root.transform.position = pos;

        var cc = root.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.3f;
        cc.center = new Vector3(0f, 0.9f, 0f);
        root.AddComponent<CoDMovement>();

        // Head: Camera + CoDCamera (mouse look; yaws the body, pitches the camera).
        var head = new GameObject("Head");
        head.transform.SetParent(root.transform, false);
        head.transform.localPosition = new Vector3(0f, 1.6f, 0f);

        var cam = head.AddComponent<Camera>();
        cam.depth = 100f; // render on top of any existing scene camera so the Game view shows this
        var look = head.AddComponent<CoDCamera>();
        look.playerBody = root.transform;

        // Only add an AudioListener if the scene has none, to avoid a duplicate-listener warning.
        if (Object.FindFirstObjectByType<AudioListener>() == null)
        {
            head.AddComponent<AudioListener>();
        }

        Undo.RegisterCreatedObjectUndo(root, "Add Test Player");
        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(scene);
        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.FrameSelected();
        }

        Debug.Log("[LightingPreviewTools] Added test player at " + pos +
            ". Press Play to walk: WASD + mouse look, Shift = sprint, Space = jump, Esc = unlock cursor. " +
            "Nothing was saved — save the scene only if you want to keep it (Undo removes it).");
    }

    // Grey the menu out unless the lighting-preview scene is the active one.
    [MenuItem(MenuPath, true)]
    private static bool AddTestPlayerValidate()
    {
        return SceneManager.GetActiveScene().name == PreviewScene;
    }

    // Prefer where the Scene view is looking; drop the player just above the floor beneath it.
    private static Vector3 GuessSpawnPosition()
    {
        Vector3 basePos = new Vector3(0f, 2f, 0f);
        SceneView view = SceneView.lastActiveSceneView;
        if (view != null)
        {
            basePos = view.pivot;
        }

        if (Physics.Raycast(basePos + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 200f))
        {
            return hit.point + Vector3.up * 0.1f; // feet just above the floor; gravity settles it
        }
        return basePos;
    }
}
