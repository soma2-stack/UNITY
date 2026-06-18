#if UNITY_EDITOR
using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class MultiplayerProjectSetup
{
    private const string PrefabPath = "Assets/Resources/NetworkPlayer.prefab";

    static MultiplayerProjectSetup()
    {
        EditorApplication.delayCall += EnsureNetworkPlayerPrefab;
    }

    [MenuItem("Tools/School of the Dead/Refresh Network Player Prefab")]
    public static void RebuildNetworkPlayerPrefab()
    {
        AssetDatabase.DeleteAsset(PrefabPath);
        EnsureNetworkPlayerPrefab();
    }

    private static void EnsureNetworkPlayerPrefab()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existingPrefab != null)
        {
            EnsureNetworkObjectHash(existingPrefab);
            return;
        }

        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        root.name = "NetworkPlayer";
        Object.DestroyImmediate(root.GetComponent<CapsuleCollider>());

        CharacterController controller = root.AddComponent<CharacterController>();
        controller.height = 2f;
        controller.radius = 0.5f;
        controller.center = Vector3.zero;

        PlayerMovement movement = root.AddComponent<PlayerMovement>();
        root.AddComponent<NetworkObject>();
        root.AddComponent<OwnerNetworkTransform>();

        GameObject cameraObject = new GameObject("Player Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(root.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 0.6f, 0f);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 90f;
        cameraObject.AddComponent<AudioListener>();
        CoDCamera cameraController = cameraObject.AddComponent<CoDCamera>();
        cameraController.playerBody = root.transform;
        movement.playerCamera = cameraObject.transform;

        AttachStarterAssetsModelIfAvailable(root.transform);
        root.AddComponent<NetworkPlayerAvatar>();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        EnsureNetworkObjectHash(prefab);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Created Resources/NetworkPlayer.prefab for four-player co-op.");
    }

    private static void EnsureNetworkObjectHash(GameObject prefab)
    {
        NetworkObject networkObject = prefab != null ? prefab.GetComponent<NetworkObject>() : null;
        if (networkObject == null)
        {
            return;
        }

        MethodInfo validateMethod = typeof(NetworkObject).GetMethod(
            "OnValidate",
            BindingFlags.Instance | BindingFlags.NonPublic);
        validateMethod?.Invoke(networkObject, null);
        EditorUtility.SetDirty(networkObject);
        AssetDatabase.SaveAssetIfDirty(prefab);
    }

    private static void AttachStarterAssetsModelIfAvailable(Transform root)
    {
        string[] candidates = AssetDatabase.FindAssets("PlayerArmature t:Prefab");
        foreach (string guid in candidates)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.Contains("StarterAssets"))
            {
                continue;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                continue;
            }

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            model.name = "Remote Survivor Model";
            model.transform.SetParent(root, false);
            model.transform.localPosition = new Vector3(0f, -1f, 0f);
            model.transform.localRotation = Quaternion.identity;
            return;
        }

        Debug.LogWarning(
            "Starter Assets survivor model was not found. Multiplayer works with the capsule fallback; " +
            "import Unity Starter Assets and run Tools > School of the Dead > Refresh Network Player Prefab.");
    }
}
#endif
