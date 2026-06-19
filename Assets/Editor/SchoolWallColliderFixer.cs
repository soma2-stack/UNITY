using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ensures the School Of The Dead geometry actually blocks the player.
///
/// The generated walls / floors / ceilings / stairs are plain MeshRenderers with
/// no Collider, so the player can walk through them. This tool walks the whole
/// scene and adds a (non-convex) MeshCollider to every mesh that does not already
/// have a Collider, skipping the generated doors (which manage their own colliders)
/// and any object that already has a collider.
///
/// Re-runnable and safe: it never removes or changes existing colliders.
/// </summary>
public static class SchoolWallColliderFixer
{
    private const string ScenePath = "Assets/Scenes/SchoolOfTheDead.unity";
    private const string DoorsRootName = "Generated_Doors";

    [MenuItem("Tools/School Of The Dead/Fix Wall Colliders")]
    public static void FixWallColliders()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        int added = 0;
        int alreadyHad = 0;
        int skipped = 0;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                GameObject go = renderer.gameObject;

                // Doors handle their own (toggled) colliders - leave them alone.
                if (go.transform.root.name == DoorsRootName)
                {
                    skipped++;
                    continue;
                }

                // Already collidable - nothing to do.
                if (go.GetComponent<Collider>() != null)
                {
                    alreadyHad++;
                    continue;
                }

                MeshFilter filter = go.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                {
                    skipped++;
                    continue;
                }

                MeshCollider collider = go.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = false; // static level geometry
                added++;
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Wall colliders: added {added} MeshColliders, {alreadyHad} already had a collider, {skipped} skipped (doors / no mesh).");
    }
}
