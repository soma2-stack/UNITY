#if UNITY_EDITOR
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor tool that auto-creates Assets/Resources/NetworkZombie.prefab from the existing
/// scene zombie prefab, so MultiplayerSessionController's
/// Resources.Load&lt;GameObject&gt;("NetworkZombie") registration succeeds without any manual
/// setup. Runs on editor load (and via the menu item). It copies the source zombie prefab
/// (which already has NetworkObject / NetworkTransform / ZombieAgent / NavMeshAgent /
/// collider) and makes sure its Animator uses ZombieAI.controller.
/// </summary>
[InitializeOnLoad]
public static class NetworkZombieSetup
{
    private const string SourcePath = "Assets/Prefabs/ZombieMale_AAB 1.prefab";
    private const string DestPath = "Assets/Resources/NetworkZombie.prefab";
    private const string ControllerPath = "Assets/Animations/ZombieAI.controller";

    static NetworkZombieSetup()
    {
        EditorApplication.delayCall += EnsureNetworkZombiePrefab;
    }

    [MenuItem("Tools/School of the Dead/Refresh Network Zombie Prefab")]
    public static void RebuildNetworkZombiePrefab()
    {
        AssetDatabase.DeleteAsset(DestPath);
        EnsureNetworkZombiePrefab();
    }

    private static void EnsureNetworkZombiePrefab()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(DestPath) != null)
        {
            return; // already exists
        }

        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
        if (source == null)
        {
            Debug.LogWarning("[NetworkZombieSetup] Source zombie prefab not found at '" + SourcePath +
                             "'. Cannot build NetworkZombie.prefab — assign your zombie prefab there or " +
                             "edit SourcePath.");
            return;
        }

        if (source.GetComponent<NetworkObject>() == null)
        {
            Debug.LogWarning("[NetworkZombieSetup] '" + SourcePath + "' has no NetworkObject; the " +
                             "NetworkZombie prefab will not replicate. Add a NetworkObject + NetworkTransform first.");
        }

        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }

        if (!AssetDatabase.CopyAsset(SourcePath, DestPath))
        {
            Debug.LogWarning("[NetworkZombieSetup] Failed to copy '" + SourcePath + "' -> '" + DestPath + "'.");
            return;
        }

        // Edit the copied prefab: ensure the ZombieAI controller, and add a NetworkAnimator
        // so the server-driven attack / hit / speed / death animations replicate to clients
        // (zombie AI only runs on the server, so clients need the animator synced).
        GameObject contents = PrefabUtility.LoadPrefabContents(DestPath);
        Animator animator = contents.GetComponentInChildren<Animator>(true);
        RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        if (animator != null && controller != null && animator.runtimeAnimatorController == null)
        {
            animator.runtimeAnimatorController = controller;
        }
        if (animator != null && contents.GetComponent<NetworkAnimator>() == null)
        {
            NetworkAnimator networkAnimator = contents.AddComponent<NetworkAnimator>();
            networkAnimator.Animator = animator;
        }
        PrefabUtility.SaveAsPrefabAsset(contents, DestPath);
        PrefabUtility.UnloadPrefabContents(contents);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[NetworkZombieSetup] Created '" + DestPath + "' from '" + SourcePath +
                  "'. ZombieSpawner will use it automatically.");
    }
}
#endif
