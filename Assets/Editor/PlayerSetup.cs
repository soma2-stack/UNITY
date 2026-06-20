using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// One-click player setup for "School Of The Dead".
///
/// Builds a Humanoid locomotion Animator Controller (Idle / Move / Sprint / Crouch)
/// from the Kevin Iglesias human animation set, then assembles a Player prefab:
/// a chosen Humanoid character model + CharacterController + first-person camera +
/// PlayerMovement + CoDCamera + PlayerHealth + WeaponController + PlayerAnimator.
///
/// Model selection: prefers a Humanoid character model whose path mentions
/// western/cowboy/player/survivor/etc.; if none is found it falls back to the
/// Kevin Iglesias demo human so the tool still produces a working player. Once
/// the western models are imported, re-run this (or set ForcedModelPath).
///
/// Re-runnable: rebuilds the controller and prefab each run.
/// </summary>
public static class PlayerSetup
{
    private const string AnimFolder = "Assets/Animations";
    private const string ControllerPath = "Assets/Animations/PlayerLocomotion.controller";
    private const string PrefabFolder = "Assets/Prefabs";
    private const string PrefabPath = "Assets/Prefabs/Player.prefab";

    // Set this to the exact western model path to force it, e.g.
    // "Assets/Low Poly Western/Models/Cowboy.fbx". Leave empty to auto-detect.
    private const string ForcedModelPath = "";

    private const string KiBase = "Assets/Kevin Iglesias/Human Animations/Animations/Male";
    private const string IdleFbx = KiBase + "/Idles/HumanM@Idle01.fbx";
    private const string MoveFbx = KiBase + "/Movement/Run/HumanM@Run01_Forward.fbx";
    private const string SprintFbx = KiBase + "/Movement/Sprint/HumanM@Sprint01_Forward.fbx";
    private const string CrouchFbx = KiBase + "/Movement/Crouch/HumanM@Crouch01_Idle.fbx";
    private const string FallbackModel = "Assets/Kevin Iglesias/Human Animations/Models/HumanM_Model.fbx";

    [MenuItem("Tools/School Of The Dead/Set Up Player")]
    public static void SetUpPlayer()
    {
        foreach (string p in new[] { IdleFbx, MoveFbx, SprintFbx, CrouchFbx })
        {
            SetClipLoop(p, true);
        }

        AnimationClip idle = LoadClip(IdleFbx);
        AnimationClip move = LoadClip(MoveFbx);
        AnimationClip sprint = LoadClip(SprintFbx);
        AnimationClip crouch = LoadClip(CrouchFbx);

        if (idle == null || move == null)
        {
            Debug.LogError("[PlayerSetup] Missing human locomotion clips - is the Kevin Iglesias Human Animations set imported under " + KiBase + "?");
            return;
        }

        AnimatorController controller = BuildController(idle, move, sprint, crouch);

        string modelPath = ResolveModelPath();
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (modelAsset == null)
        {
            Debug.LogError("[PlayerSetup] Could not load a character model. Import your western player models and set ForcedModelPath, then re-run.");
            return;
        }

        bool usingFallback = modelPath == FallbackModel;

        // Build the player from a temporary instance.
        GameObject root = new GameObject("Player");
        CharacterController cc = root.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.3f;
        cc.center = new Vector3(0f, 0.9f, 0f);

        // Visible character model (child).
        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
        model.name = "Model";
        model.transform.SetParent(root.transform, false);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;

        Animator animator = model.GetComponentInChildren<Animator>();
        if (animator == null)
        {
            animator = model.AddComponent<Animator>();
        }
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;

        // First-person camera at head height.
        GameObject camObj = new GameObject("Player Camera");
        camObj.tag = "MainCamera";
        camObj.transform.SetParent(root.transform, false);
        camObj.transform.localPosition = new Vector3(0f, 1.65f, 0f);
        Camera cam = camObj.AddComponent<Camera>();
        cam.fieldOfView = 90f;
        camObj.AddComponent<AudioListener>();

        // Player scripts.
        PlayerMovement movement = root.AddComponent<PlayerMovement>();
        movement.playerCamera = camObj.transform;

        CoDCamera codCam = camObj.AddComponent<CoDCamera>();
        codCam.playerBody = root.transform;

        root.AddComponent<PlayerHealth>();
        root.AddComponent<WeaponController>();
        root.AddComponent<PlayerAnimator>();

        EnsureFolder(PrefabFolder);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[PlayerSetup] Built " + ControllerPath + " and " + PrefabPath +
                  " using model: " + modelPath +
                  (usingFallback
                      ? "  (FALLBACK demo human - import your western models and re-run, or set ForcedModelPath)."
                      : ".") +
                  " Place Player.prefab in the scene (one per level / as the multiplayer player) and assign weapons on its WeaponController.");
    }

    private static string ResolveModelPath()
    {
        if (!string.IsNullOrEmpty(ForcedModelPath) && AssetDatabase.LoadAssetAtPath<GameObject>(ForcedModelPath) != null)
        {
            return ForcedModelPath;
        }

        string[] preferred = { "western", "cowboy", "gunslinger", "bandit", "sheriff", "outlaw", "player", "survivor" };
        string best = null;
        int bestScore = -1;
        string firstHumanoid = null;

        foreach (string guid in AssetDatabase.FindAssets("t:GameObject"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.ToLowerInvariant().EndsWith(".fbx"))
            {
                continue;
            }
            string lower = path.ToLowerInvariant();
            // Skip animation clips, the zombie, the weapon pack, and pure animation packs.
            if (lower.Contains("/animations/") || lower.Contains("zombiemale_aab") ||
                lower.Contains("nephilite") || lower.Contains("low poly weapons"))
            {
                continue;
            }

            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null || importer.animationType != ModelImporterAnimationType.Human)
            {
                continue;
            }

            if (firstHumanoid == null)
            {
                firstHumanoid = path;
            }

            int score = 0;
            foreach (string token in preferred)
            {
                if (lower.Contains(token))
                {
                    score++;
                }
            }
            if (score > bestScore)
            {
                bestScore = score;
                best = path;
            }
        }

        if (best != null && bestScore > 0)
        {
            return best;
        }
        if (firstHumanoid != null)
        {
            return firstHumanoid;
        }
        return FallbackModel;
    }

    private static AnimatorController BuildController(AnimationClip idle, AnimationClip move, AnimationClip sprint, AnimationClip crouch)
    {
        EnsureFolder(AnimFolder);
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
        {
            AssetDatabase.DeleteAsset(ControllerPath);
        }

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Sprint", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Crouch", AnimatorControllerParameterType.Bool);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        AnimatorState sIdle = sm.AddState("Idle");
        sIdle.motion = idle;
        AnimatorState sMove = sm.AddState("Move");
        sMove.motion = move;
        AnimatorState sSprint = sm.AddState("Sprint");
        sSprint.motion = sprint != null ? sprint : move;
        AnimatorState sCrouch = sm.AddState("Crouch");
        sCrouch.motion = crouch != null ? crouch : idle;

        sm.defaultState = sIdle;

        AnimatorStateTransition idleToMove = sIdle.AddTransition(sMove);
        idleToMove.hasExitTime = false;
        idleToMove.duration = 0.12f;
        idleToMove.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
        idleToMove.AddCondition(AnimatorConditionMode.IfNot, 0f, "Crouch");

        AnimatorStateTransition moveToIdle = sMove.AddTransition(sIdle);
        moveToIdle.hasExitTime = false;
        moveToIdle.duration = 0.12f;
        moveToIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");

        AnimatorStateTransition moveToSprint = sMove.AddTransition(sSprint);
        moveToSprint.hasExitTime = false;
        moveToSprint.duration = 0.12f;
        moveToSprint.AddCondition(AnimatorConditionMode.If, 0f, "Sprint");

        AnimatorStateTransition sprintToMove = sSprint.AddTransition(sMove);
        sprintToMove.hasExitTime = false;
        sprintToMove.duration = 0.12f;
        sprintToMove.AddCondition(AnimatorConditionMode.IfNot, 0f, "Sprint");

        AnimatorStateTransition sprintToIdle = sSprint.AddTransition(sIdle);
        sprintToIdle.hasExitTime = false;
        sprintToIdle.duration = 0.12f;
        sprintToIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");

        AnimatorStateTransition idleToCrouch = sIdle.AddTransition(sCrouch);
        idleToCrouch.hasExitTime = false;
        idleToCrouch.duration = 0.12f;
        idleToCrouch.AddCondition(AnimatorConditionMode.If, 0f, "Crouch");

        AnimatorStateTransition crouchToIdle = sCrouch.AddTransition(sIdle);
        crouchToIdle.hasExitTime = false;
        crouchToIdle.duration = 0.12f;
        crouchToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "Crouch");

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    private static AnimationClip LoadClip(string fbxPath)
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        foreach (Object asset in assets)
        {
            AnimationClip clip = asset as AnimationClip;
            if (clip != null && !clip.name.StartsWith("__preview__"))
            {
                return clip;
            }
        }
        return null;
    }

    private static void SetClipLoop(string fbxPath, bool loop)
    {
        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
        {
            return;
        }

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
        {
            clips = importer.defaultClipAnimations;
        }
        if (clips == null || clips.Length == 0)
        {
            return;
        }

        bool changed = false;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i].loopTime != loop)
            {
                clips[i].loopTime = loop;
                changed = true;
            }
        }
        if (changed)
        {
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }
        string parent = System.IO.Path.GetDirectoryName(folderPath)?.Replace("\\", "/");
        string name = System.IO.Path.GetFileName(folderPath);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }
        AssetDatabase.CreateFolder(parent, name);
    }
}
