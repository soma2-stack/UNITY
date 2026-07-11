#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Editor tool that builds Assets/Resources/NetworkPlayer.prefab - the body each
/// connected client spawns as in 4-player co-op. It mirrors the single-player
/// Player.prefab (western Humanoid model + first-person camera + movement / health /
/// weapons / animator), but adds the Netcode pieces (NetworkObject,
/// OwnerNetworkTransform, NetworkPlayerAvatar) so ownership, visibility and
/// animation replicate correctly.
///
/// Per-player model variety: the prefab contains all three western bodies
/// (sheriff / gunman / outlow) as children of a "Model" holder; NetworkPlayerAvatar
/// enables exactly one per OwnerClientId at spawn and rebinds the animator to it.
///
/// Re-runnable: delete the asset and call EnsureNetworkPlayerPrefab (or use the menu
/// item). It depends on the assets the single-player tools produce
/// (Assets/Animations/PlayerLocomotion.controller and the western models); if those
/// are missing it leaves the existing prefab alone and logs how to fix it.
/// </summary>
[InitializeOnLoad]
public static class MultiplayerProjectSetup
{
    private const string PrefabPath = "Assets/Resources/NetworkPlayer.prefab";
    // The project ships PlayerAnimator.controller (not the older "PlayerLocomotion" name).
    private const string ControllerPath = "Assets/Animations/PlayerAnimator.controller";

    private const string WeaponPackPrefabFolder = "Assets/Low Poly Weapons VOL.1/Prefabs";
    private const string WeaponHolderName = "WeaponHolder";
    private static readonly Vector3 WeaponHolderLocalPosition = new Vector3(0.25f, -0.25f, 0.5f);

    // The three western Humanoid bodies. The first is the default (sheriff); the
    // avatar cycles through them per OwnerClientId for variety.
    private static readonly string[] ModelPaths =
    {
        "Assets/tt-3d/Low-PolyWesternStarterPack/Character/Models/sheriff.fbx",
        "Assets/tt-3d/Low-PolyWesternStarterPack/Character/Models/gunman.fbx",
        "Assets/tt-3d/Low-PolyWesternStarterPack/Character/Models/outlow.fbx",
    };

    // The starting loadout for the network player, mirroring the single-player
    // "Set Up Weapons" tool (reuses WeaponLoadoutSetup.MakeWeapon presets).
    private static readonly (string display, string prefab, WeaponLoadoutSetup.GunCategory category)[] StartingLoadout =
    {
        ("M1911", "M1911", WeaponLoadoutSetup.GunCategory.Pistol),
        ("AK74", "AK74", WeaponLoadoutSetup.GunCategory.Rifle),
        ("Benelli M4", "Bennelli_M4", WeaponLoadoutSetup.GunCategory.Shotgun),
        ("M107", "M107", WeaponLoadoutSetup.GunCategory.Sniper),
    };

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
            // Auto-upgrade a stale capsule prefab (no western model variants wired) to
            // the western co-op body the first time the assets are available.
            if (IsLegacyCapsulePrefab(existingPrefab) &&
                AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            {
                AssetDatabase.DeleteAsset(PrefabPath);
            }
            else
            {
                EnsureNetworkObjectHash(existingPrefab);
                return;
            }
        }

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogWarning(
                "[MultiplayerProjectSetup] " + ControllerPath + " not found. Run " +
                "Tools > School Of The Dead > Set Up Player first, then " +
                "Tools > School of the Dead > Refresh Network Player Prefab to build the western co-op body.");
            return;
        }

        // --- Root: CharacterController + movement/health/weapons/animator + Netcode. ---
        GameObject root = new GameObject("NetworkPlayer");

        CharacterController controllerComponent = root.AddComponent<CharacterController>();
        controllerComponent.height = 1.8f;
        controllerComponent.radius = 0.3f;
        controllerComponent.center = new Vector3(0f, 0.9f, 0f);

        PlayerMovement movement = root.AddComponent<PlayerMovement>();
        root.AddComponent<PlayerHealth>();
        WeaponController weaponController = root.AddComponent<WeaponController>();
        root.AddComponent<PlayerAnimator>();
        // Co-op revive: hold-to-revive a downed teammate. Owner-gated inside the component.
        root.AddComponent<ReviveInteraction>();

        root.AddComponent<NetworkObject>();
        root.AddComponent<OwnerNetworkTransform>();
        NetworkPlayerAvatar avatar = root.AddComponent<NetworkPlayerAvatar>();

        // --- Model holder with the three western bodies as children. ---
        GameObject modelHolder = new GameObject("Model");
        modelHolder.transform.SetParent(root.transform, false);

        List<GameObject> variants = new List<GameObject>();
        Animator defaultAnimator = null;
        foreach (string modelPath in ModelPaths)
        {
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelAsset == null)
            {
                Debug.LogWarning("[MultiplayerProjectSetup] Western model missing: " + modelPath + " - skipping.");
                continue;
            }

            GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            body.name = System.IO.Path.GetFileNameWithoutExtension(modelPath);
            body.transform.SetParent(modelHolder.transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;

            Animator bodyAnimator = body.GetComponentInChildren<Animator>(true);
            if (bodyAnimator == null)
            {
                bodyAnimator = body.AddComponent<Animator>();
            }
            bodyAnimator.runtimeAnimatorController = controller;
            bodyAnimator.applyRootMotion = false;

            // Only the first (default) body is active in the prefab; the avatar enables
            // the right one per OwnerClientId at runtime.
            body.SetActive(variants.Count == 0);
            if (variants.Count == 0)
            {
                defaultAnimator = bodyAnimator;
            }
            variants.Add(body);
        }

        if (variants.Count == 0)
        {
            Object.DestroyImmediate(root);
            Debug.LogError("[MultiplayerProjectSetup] No western models found under " +
                           "Assets/tt-3d/Low-PolyWesternStarterPack. Import them and re-run.");
            return;
        }

        // --- First-person camera at head height. ---
        GameObject cameraObject = new GameObject("Player Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(root.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 90f;
        camera.nearClipPlane = 0.02f;
        AudioListener audioListener = cameraObject.AddComponent<AudioListener>();
        CoDCamera cameraController = cameraObject.AddComponent<CoDCamera>();
        cameraController.playerBody = root.transform;
        movement.playerCamera = cameraObject.transform;

        // --- Weapons: first-person loadout under the camera. ---
        Renderer[] defaultRenderers = variants[0].GetComponentsInChildren<Renderer>(true);
        BuildWeaponLoadout(weaponController, cameraObject.transform);

        // --- Wire NetworkPlayerAvatar serialized (private) fields via SerializedObject. ---
        SerializedObject so = new SerializedObject(avatar);
        so.FindProperty("movement").objectReferenceValue = movement;
        so.FindProperty("playerCamera").objectReferenceValue = camera;
        so.FindProperty("audioListener").objectReferenceValue = audioListener;
        so.FindProperty("animator").objectReferenceValue = defaultAnimator;

        SerializedProperty renderersProp = so.FindProperty("survivorRenderers");
        renderersProp.arraySize = defaultRenderers.Length;
        for (int i = 0; i < defaultRenderers.Length; i++)
        {
            renderersProp.GetArrayElementAtIndex(i).objectReferenceValue = defaultRenderers[i];
        }

        SerializedProperty variantsProp = so.FindProperty("modelVariants");
        variantsProp.arraySize = variants.Count;
        for (int i = 0; i < variants.Count; i++)
        {
            variantsProp.GetArrayElementAtIndex(i).objectReferenceValue = variants[i];
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        EnsureNetworkObjectHash(prefab);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[MultiplayerProjectSetup] Built western co-op body at " + PrefabPath +
                  " with " + variants.Count + " model variant(s) + weapon loadout.");
    }

    private static void BuildWeaponLoadout(WeaponController controller, Transform cameraTransform)
    {
        if (controller == null || cameraTransform == null)
        {
            return;
        }

        if (controller.weapons == null)
        {
            controller.weapons = new List<Weapon>();
        }
        controller.weapons.Clear();

        GameObject holder = new GameObject(WeaponHolderName);
        holder.transform.SetParent(cameraTransform, false);
        holder.transform.localPosition = WeaponHolderLocalPosition;
        holder.transform.localRotation = Quaternion.identity;
        holder.transform.localScale = Vector3.one;

        controller.aimCamera = cameraTransform;

        int added = 0;
        foreach (var entry in StartingLoadout)
        {
            string gunPrefabPath = WeaponPackPrefabFolder + "/" + entry.prefab + ".prefab";
            GameObject gunPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(gunPrefabPath);
            if (gunPrefab == null)
            {
                Debug.LogWarning("[MultiplayerProjectSetup] Missing weapon prefab: " + gunPrefabPath +
                                 " - skipping " + entry.display + ".");
                continue;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(gunPrefab);
            instance.name = entry.display;
            instance.transform.SetParent(holder.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            instance.SetActive(added == 0);

            Weapon weapon = WeaponLoadoutSetup.MakeWeapon(entry.display, entry.category, instance);
            weapon.InitAmmo();
            controller.weapons.Add(weapon);
            added++;
        }

        controller.currentIndex = 0;

        if (added == 0)
        {
            Debug.LogWarning("[MultiplayerProjectSetup] No weapon prefabs found under " +
                             WeaponPackPrefabFolder + ". Network player loadout is empty.");
        }
    }

    // A prefab built by the old code is a capsule with no model variants wired on its
    // NetworkPlayerAvatar. Detect that so we can rebuild it as the western body.
    private static bool IsLegacyCapsulePrefab(GameObject prefab)
    {
        NetworkPlayerAvatar avatar = prefab.GetComponent<NetworkPlayerAvatar>();
        if (avatar == null)
        {
            return true;
        }

        SerializedObject so = new SerializedObject(avatar);
        SerializedProperty variantsProp = so.FindProperty("modelVariants");
        return variantsProp == null || variantsProp.arraySize == 0;
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
}
#endif
