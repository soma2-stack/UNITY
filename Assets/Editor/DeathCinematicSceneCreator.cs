using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class DeathCinematicSceneCreator
{
    private const string ScenePath = "Assets/Scenes/DeathCinematic.unity";

    [MenuItem("Tools/School Of The Dead/Create Death Cinematic Scene")]
    public static void CreateDeathCinematicScene()
    {
        Directory.CreateDirectory("Assets/Scenes");

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "DeathCinematic";

        if (!EditorSceneManager.SaveScene(scene, ScenePath))
        {
            throw new IOException("Failed to save " + ScenePath);
        }

        AssetDatabase.ImportAsset(ScenePath);
        AssetDatabase.SaveAssets();
        UnityEngine.Debug.Log("[DeathCinematicSceneCreator] Created " + ScenePath);
    }

    public static void ValidateDeathCinematicScene()
    {
        if (!File.Exists(ScenePath))
        {
            throw new FileNotFoundException("Missing " + ScenePath);
        }

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (scene.name != "DeathCinematic")
        {
            throw new IOException("Unexpected scene name: " + scene.name);
        }

        UnityEngine.GameObject go = new UnityEngine.GameObject("DeathCinematicValidation");
        DeathCinematicSceneController controller = go.AddComponent<DeathCinematicSceneController>();
        MethodInfo start = typeof(DeathCinematicSceneController).GetMethod(
            "Start", BindingFlags.Instance | BindingFlags.NonPublic);
        if (start == null)
        {
            throw new MissingMethodException("DeathCinematicSceneController.Start");
        }

        start.Invoke(controller, null);

        RequireObject("Room");
        RequireObject("MonitorBank");
        RequireObject("Chalkboard");
        RequireObject("DeathCinematicUI");

        if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.Camera>() == null)
        {
            throw new IOException("No camera was created or found.");
        }

        UnityEngine.Debug.Log("[DeathCinematicSceneCreator] Validation passed for " + ScenePath);

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    private static void RequireObject(string objectName)
    {
        if (UnityEngine.GameObject.Find(objectName) == null)
        {
            throw new IOException("Expected runtime object was not created: " + objectName);
        }
    }

    // -----------------------------------------------------------------------------------------
    // Rebuild the EDITABLE death room into DeathCinematic.unity (Phase 3). Bakes the same objects
    // the runtime fallback would build (via the shared static builders on the controller), so the
    // scene contains real hand-editable GameObjects. Touches ONLY DeathCinematic.unity.
    // -----------------------------------------------------------------------------------------

    [MenuItem("Tools/Death Cinematic/Rebuild Editable Death Room")]
    public static void RebuildEditableDeathRoom()
    {
        // Standard Unity save prompt for the user's current work; they decide. Cancel => abort.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            UnityEngine.Debug.LogWarning("[DeathCinematic] Rebuild cancelled at the save prompt — nothing changed.");
            return;
        }

        if (!EnsureCinematicSceneOpen(out Scene scene))
        {
            return; // reason already logged
        }

        // Only replace existing tool-generated content on explicit confirmation.
        UnityEngine.GameObject existingRoot = FindInScene(scene, DeathCinematicSceneController.RootName);
        if (existingRoot != null)
        {
            bool replace = EditorUtility.DisplayDialog(
                "Rebuild Editable Death Room",
                "A '" + DeathCinematicSceneController.RootName + "' already exists in DeathCinematic.unity.\n\n" +
                "Replace it (and its DeathCinematicUI / EventSystem) with a freshly-built editable room?\n" +
                "Only these tool-generated objects are removed. Other scenes are untouched.",
                "Replace", "Cancel");
            if (!replace)
            {
                UnityEngine.Debug.Log("[DeathCinematic] Rebuild cancelled — existing death room left untouched.");
                return;
            }
            RemoveGenerated(scene);
        }

        // Build the editable hierarchy (shared with the runtime fallback) into the active scene.
        UnityEngine.Transform root = DeathCinematicSceneController.BuildEditableRoom(6, 83, 630, 8, 4200);
        EnsureEventSystem();

        // Add a wired controller so the scene owns its instance (the runtime bootstrap then won't
        // spawn a duplicate) and resolves these baked objects instead of rebuilding.
        DeathCinematicSceneController controller = root.gameObject.AddComponent<DeathCinematicSceneController>();
        controller.deathRoomRoot = root;

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        UnityEngine.Debug.Log("[DeathCinematic] Editable death room built into '" + scene.path + "'. " +
            (saved ? "Scene saved." : "SAVE FAILED — save it manually.") +
            " Only DeathCinematic.unity was modified.");
    }

    // Make DeathCinematic the active/open scene. Already-open => use in place; otherwise open Single
    // (safe — the save prompt ran above) so every new object lands in DeathCinematic, never elsewhere.
    private static bool EnsureCinematicSceneOpen(out Scene scene)
    {
        Scene active = EditorSceneManager.GetActiveScene();
        if (active.path == ScenePath)
        {
            scene = active;
            return true;
        }

        if (!File.Exists(ScenePath))
        {
            UnityEngine.Debug.LogError("[DeathCinematic] " + ScenePath + " not found. Run " +
                "'Tools > School Of The Dead > Create Death Cinematic Scene' first.");
            scene = default;
            return false;
        }

        scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            UnityEngine.Debug.LogError("[DeathCinematic] Could not open " + ScenePath + ".");
            return false;
        }
        return true;
    }

    // Remove ONLY this tool's own generated top-level objects so a rebuild starts clean.
    private static void RemoveGenerated(Scene scene)
    {
        DestroyIfPresent(scene, DeathCinematicSceneController.RootName);
        DestroyIfPresent(scene, DeathCinematicSceneController.UiName);
        DestroyIfPresent(scene, "EventSystem");
    }

    private static void DestroyIfPresent(Scene scene, string objectName)
    {
        UnityEngine.GameObject go = FindInScene(scene, objectName);
        if (go != null)
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    private static void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null)
        {
            return;
        }
        UnityEngine.GameObject go = new UnityEngine.GameObject("EventSystem");
        go.AddComponent<UnityEngine.EventSystems.EventSystem>();
        go.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
    }

    // Find a root-or-descendant GameObject by name within a specific scene (never across scenes).
    private static UnityEngine.GameObject FindInScene(Scene scene, string objectName)
    {
        if (!scene.IsValid())
        {
            return null;
        }
        foreach (UnityEngine.GameObject root in scene.GetRootGameObjects())
        {
            UnityEngine.GameObject found = FindRecursive(root.transform, objectName);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    private static UnityEngine.GameObject FindRecursive(UnityEngine.Transform t, string objectName)
    {
        if (t.name == objectName)
        {
            return t.gameObject;
        }
        foreach (UnityEngine.Transform child in t)
        {
            UnityEngine.GameObject found = FindRecursive(child, objectName);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }
}
