using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor-only setup helper for the DeathCinematic scene (Phase 2). Everything here is
/// NON-DESTRUCTIVE and runs ONLY when invoked from the menu — nothing runs automatically:
///
///   Tools > Death Cinematic > Setup Scene + Build Settings
///
///   1. Creates an EMPTY <c>Assets/Scenes/DeathCinematic.unity</c> ONLY if it does not already
///      exist. The runtime <see cref="DeathCinematicSceneController"/> builds the entire scene
///      at play time, so an empty scene is all that is needed. An existing scene is NEVER
///      overwritten, and the user's currently-open scene(s) are never disturbed or saved (the
///      empty scene is created additively, saved, then closed).
///   2. Ensures that scene is present + enabled in Build Settings, APPENDED to the end without
///      removing, reordering, or disabling any existing scene.
///
/// Nothing else in the project is touched.
/// </summary>
public static class DeathCinematicSetup
{
    private const string ScenePath = "Assets/Scenes/DeathCinematic.unity";
    private const string MenuPath = "Tools/Death Cinematic/Setup Scene + Build Settings";

    [MenuItem(MenuPath)]
    public static void SetupSceneAndBuildSettings()
    {
        bool createdScene = EnsureSceneExists();
        bool changedBuild = EnsureInBuildSettings();

        Debug.Log("[DeathCinematicSetup] Done. " +
                  (createdScene ? "Created empty " + ScenePath + ". " : ScenePath + " already existed. ") +
                  (changedBuild ? "Added it to Build Settings (enabled)." : "Build Settings already contained it."));
    }

    // Create an empty scene at ScenePath only when it is missing. Never overwrites existing
    // content, and never opens/modifies/saves the user's currently-open scene(s).
    private static bool EnsureSceneExists()
    {
        if (File.Exists(ScenePath))
        {
            return false;
        }

        string dir = Path.GetDirectoryName(ScenePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Additive empty scene: does not replace the active scene, so nothing the user has open is
        // touched. Save it to the asset path, then close it back out.
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
        EditorSceneManager.CloseScene(scene, true);
        AssetDatabase.Refresh();
        return saved;
    }

    // Append the scene to Build Settings (enabled) if it is not already listed. Existing entries
    // are left exactly as-is — no removal, reordering, or disabling.
    private static bool EnsureInBuildSettings()
    {
        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        for (int i = 0; i < scenes.Length; i++)
        {
            if (scenes[i] != null && scenes[i].path == ScenePath)
            {
                return false; // already present — leave everything untouched
            }
        }

        List<EditorBuildSettingsScene> list = new List<EditorBuildSettingsScene>(scenes);
        list.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
        return true;
    }
}
