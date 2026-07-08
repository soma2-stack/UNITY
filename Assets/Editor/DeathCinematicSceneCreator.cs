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
}
