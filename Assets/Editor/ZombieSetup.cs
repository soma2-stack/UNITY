using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// One-click zombie setup for "School Of The Dead".
///
/// Builds an Animator Controller (Idle / Move / Attack / Death) from the Nephilite
/// Zombie Animation Set, then assembles a Zombie prefab from the Humanoid
/// ZombieMale_AAB model (NavMeshAgent + CapsuleCollider + ZombieAgent + the
/// controller). Because the model and the clips are all Humanoid, the zombie
/// clips retarget onto ZombieMale_AAB automatically.
///
/// Re-runnable: rebuilds the controller and prefab each run. If a ZombieSpawner
/// exists in the scene it is pointed at the new prefab.
/// </summary>
public static class ZombieSetup
{
    private const string ScenePath = "Assets/Scenes/SchoolOfTheDead.unity";
    private const string AnimFolder = "Assets/Animations";
    private const string ControllerPath = "Assets/Animations/ZombieAI.controller";
    private const string PrefabFolder = "Assets/Prefabs";
    private const string PrefabPath = "Assets/Prefabs/Zombie.prefab";
    private const string ModelPrefab = "Assets/ZombieMale_AAB/Prefabs/URP/ZombieMale_AAB_URP.prefab";
    private const string ClipFolder = "Assets/Nephilite Studios/Zombie Animation Set/Animations/In Place";

    private const string IdleFbx = ClipFolder + "/zombie_aggro_idle_01.fbx";
    private const string RunFbx = ClipFolder + "/zombie_run_01.fbx";
    private const string AttackFbx = ClipFolder + "/zombie_light_attack_01.fbx";
    private const string DeathFbx = ClipFolder + "/zombie_death_FD_01.fbx";

    [MenuItem("Tools/School Of The Dead/Set Up Zombie")]
    public static void SetUpZombie()
    {
        // Idle and run must loop while the zombie stands / chases.
        SetClipLoop(IdleFbx, true);
        SetClipLoop(RunFbx, true);

        AnimationClip idle = LoadClip(IdleFbx);
        AnimationClip run = LoadClip(RunFbx);
        AnimationClip attack = LoadClip(AttackFbx);
        AnimationClip death = LoadClip(DeathFbx);

        if (idle == null || run == null || attack == null || death == null)
        {
            Debug.LogError("[ZombieSetup] Could not load one or more zombie clips. Check the Nephilite Zombie Animation Set is imported at:\n" + ClipFolder);
            return;
        }

        AnimatorController controller = BuildController(idle, run, attack, death);

        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefab);
        if (source == null)
        {
            Debug.LogError("[ZombieSetup] Missing ZombieMale_AAB URP prefab at: " + ModelPrefab);
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Assemble the prefab from a temporary instance.
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        instance.name = "Zombie";

        Animator animator = instance.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false; // NavMeshAgent drives movement; clips are in-place
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        }
        else
        {
            Debug.LogWarning("[ZombieSetup] No Animator found on the model - animations will not play.");
        }

        NavMeshAgent agent = GetOrAdd<NavMeshAgent>(instance);
        agent.speed = 3f;
        agent.angularSpeed = 320f;
        agent.acceleration = 12f;
        agent.stoppingDistance = 1.4f;
        agent.radius = 0.35f;
        agent.height = 1.8f;
        agent.baseOffset = 0f;

        CapsuleCollider col = GetOrAdd<CapsuleCollider>(instance);
        col.height = 1.8f;
        col.radius = 0.35f;
        col.center = new Vector3(0f, 0.9f, 0f);

        GetOrAdd<ZombieAgent>(instance);

        EnsureFolder(PrefabFolder);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
        Object.DestroyImmediate(instance);

        // Point an existing spawner at the new prefab, if there is one.
        ZombieSpawner spawner = Object.FindFirstObjectByType<ZombieSpawner>();
        bool assigned = false;
        if (spawner != null && prefab != null)
        {
            spawner.zombiePrefab = prefab;
            EditorUtility.SetDirty(spawner);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            assigned = true;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[ZombieSetup] Created " + ControllerPath + " and " + PrefabPath + ". " +
                  (assigned
                      ? "Assigned the prefab to the ZombieSpawner in the scene."
                      : "No ZombieSpawner found in the scene - drag Assets/Prefabs/Zombie.prefab onto your ZombieSpawner's Zombie Prefab field.") +
                  " Remember to BAKE a NavMesh and add spawn points.");
    }

    private static AnimatorController BuildController(AnimationClip idle, AnimationClip run, AnimationClip attack, AnimationClip death)
    {
        EnsureFolder(AnimFolder);
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
        {
            AssetDatabase.DeleteAsset(ControllerPath);
        }

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        AnimatorState sIdle = sm.AddState("Idle");
        sIdle.motion = idle;
        AnimatorState sMove = sm.AddState("Move");
        sMove.motion = run;
        AnimatorState sAttack = sm.AddState("Attack");
        sAttack.motion = attack;
        AnimatorState sDeath = sm.AddState("Death");
        sDeath.motion = death;

        sm.defaultState = sIdle;

        AnimatorStateTransition idleToMove = sIdle.AddTransition(sMove);
        idleToMove.hasExitTime = false;
        idleToMove.duration = 0.15f;
        idleToMove.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");

        AnimatorStateTransition moveToIdle = sMove.AddTransition(sIdle);
        moveToIdle.hasExitTime = false;
        moveToIdle.duration = 0.15f;
        moveToIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");

        AnimatorStateTransition anyToAttack = sm.AddAnyStateTransition(sAttack);
        anyToAttack.hasExitTime = false;
        anyToAttack.duration = 0.05f;
        anyToAttack.canTransitionToSelf = false;
        anyToAttack.AddCondition(AnimatorConditionMode.If, 0f, "Attack");

        AnimatorStateTransition attackToIdle = sAttack.AddTransition(sIdle);
        attackToIdle.hasExitTime = true;
        attackToIdle.exitTime = 0.85f;
        attackToIdle.duration = 0.1f;

        AnimatorStateTransition anyToDeath = sm.AddAnyStateTransition(sDeath);
        anyToDeath.hasExitTime = false;
        anyToDeath.duration = 0.05f;
        anyToDeath.canTransitionToSelf = false;
        anyToDeath.AddCondition(AnimatorConditionMode.If, 0f, "Die");

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
        Debug.LogWarning("[ZombieSetup] No AnimationClip found in: " + fbxPath);
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

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : go.AddComponent<T>();
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
