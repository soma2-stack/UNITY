using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// One-click gameplay setup for "School Of The Dead": bakes the NavMesh, creates a
/// GameManager (points + spawner + round manager), scatters spawn points on the
/// baked NavMesh, wires the Zombie prefab, and drops the Player into the scene.
///
/// Uses the AI Navigation package's NavMeshSurface so the bake happens from this
/// script (no manual Navigation-window steps). Re-runnable.
/// </summary>
public static class SchoolGameplaySetup
{
    private const string ScenePath = "Assets/Scenes/SchoolOfTheDead.unity";
    private const string ZombiePrefabPath = "Assets/Prefabs/Zombie.prefab";
    private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";

    private const int MaxSpawnPoints = 10;
    private const float MinSpawnSpacing = 6f;

    // Name of the doors-only layer the NavMesh bake EXCLUDES. Doors are baked-out so
    // every doorway is fully connected in the navmesh; closed doors then block at
    // runtime via their carving NavMeshObstacle (see Door.cs). Falls back gracefully
    // if the layer hasn't been added to the project.
    private const string DoorsLayerName = "Doors";
    private const string DoorsRootName = "Generated_Doors";
    private const string StairLinksRootName = "Generated_StairLinks";

    // Stair traversal: agent step height / max slope used when (re)baking so normal
    // stairs are walkable, plus NavMeshLink settings spanning each stairway.
    private const float AgentStepHeight = 0.4f;
    private const float AgentMaxSlope = 50f;
    private const float StairLinkWidth = 2.5f;

    [MenuItem("Tools/School Of The Dead/Setup Gameplay (NavMesh + Spawners)")]
    public static void SetupGameplay()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // 1a) Put every generated door on the dedicated "Doors" layer so the bake can
        //     EXCLUDE them. This is what makes the navmesh include every doorway with
        //     FULL connectivity; closed doors then carve those openings shut at runtime
        //     via their NavMeshObstacle, and Door.Open() removes the carve.
        int doorsExcludedLayer = MoveDoorsToLayer();

        // 1b) Bake the NavMesh off the level's render geometry (doors excluded).
        GameObject navObj = GameObject.Find("NavMesh Surface");
        if (navObj == null)
        {
            navObj = new GameObject("NavMesh Surface");
        }
        NavMeshSurface surface = navObj.GetComponent<NavMeshSurface>();
        if (surface == null)
        {
            surface = navObj.AddComponent<NavMeshSurface>();
        }
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;

        // Exclude the doors layer from collection so doorways bake open.
        if (doorsExcludedLayer >= 0)
        {
            surface.layerMask = ~(1 << doorsExcludedLayer);
        }
        else
        {
            surface.layerMask = ~0;
        }

        // Make normal stairs walkable: bump step height / max slope on the bake.
        ApplyStairFriendlyBuildSettings(surface);

        surface.BuildNavMesh();
        Debug.Log($"[GameplaySetup] NavMesh baked (doors excluded layer={doorsExcludedLayer}, " +
                  $"step={AgentStepHeight}, slope={AgentMaxSlope}).");

        // 1c) NavMeshLinks spanning each stairway so agents reliably traverse up/down,
        //     independent of the baked-in step climbing. Built right after the bake so
        //     both ends sample onto the fresh navmesh.
        int stairLinks = BuildStairLinks(scene, surface);
        Debug.Log($"[GameplaySetup] Created {stairLinks} stair NavMeshLink(s).");

        // 2) GameManager: points + spawner + round manager.
        GameObject gm = GameObject.Find("GameManager");
        if (gm == null)
        {
            gm = new GameObject("GameManager");
        }
        if (gm.GetComponent<PlayerPoints>() == null)
        {
            gm.AddComponent<PlayerPoints>();
        }
        ZombieSpawner spawner = gm.GetComponent<ZombieSpawner>();
        if (spawner == null)
        {
            spawner = gm.AddComponent<ZombieSpawner>();
        }
        if (gm.GetComponent<RoundManager>() == null)
        {
            gm.AddComponent<RoundManager>();
        }

        GameObject zombiePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ZombiePrefabPath);
        if (zombiePrefab != null)
        {
            spawner.zombiePrefab = zombiePrefab;
        }
        else
        {
            Debug.LogWarning("[GameplaySetup] No Zombie.prefab found - run 'Set Up Zombie' first, then re-run this.");
        }

        // 3) Spawn points snapped onto the freshly baked NavMesh.
        Transform[] spawnPoints = BuildSpawnPoints(scene);
        spawner.spawnPoints = spawnPoints;
        Debug.Log($"[GameplaySetup] Created {spawnPoints.Length} spawn points on the NavMesh.");

        // 4) Player in the scene (only if there isn't one already).
        EnsurePlayer(scene);

        EditorUtility.SetDirty(spawner);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[GameplaySetup] Done. Press Play: zombies spawn and chase. " +
                  (zombiePrefab == null ? "(Assign a Zombie prefab and re-run.)" : ""));
    }

    /// <summary>
    /// Moves every object under Generated_Doors onto the dedicated "Doors" layer so
    /// the NavMeshSurface bake can exclude them (doorways bake fully connected).
    /// Returns the layer index, or -1 if the project has no "Doors" layer yet.
    /// </summary>
    private static int MoveDoorsToLayer()
    {
        int layer = LayerMask.NameToLayer(DoorsLayerName);
        if (layer < 0)
        {
            Debug.LogWarning($"[GameplaySetup] No '{DoorsLayerName}' layer found - doors will be " +
                             "baked into the navmesh and rely only on carving obstacles. Add a " +
                             $"'{DoorsLayerName}' layer (Project Settings > Tags and Layers) and re-run " +
                             "for fully-connected doorways.");
            return -1;
        }

        GameObject doorsRoot = GameObject.Find(DoorsRootName);
        if (doorsRoot == null)
        {
            return layer; // no doors yet - layer still valid for the mask
        }

        int count = 0;
        foreach (Transform t in doorsRoot.GetComponentsInChildren<Transform>(true))
        {
            if (t.gameObject.layer != layer)
            {
                t.gameObject.layer = layer;
                count++;
            }
        }
        Debug.Log($"[GameplaySetup] Moved {count} door object(s) to layer '{DoorsLayerName}'.");
        return layer;
    }

    /// <summary>
    /// Pushes stair-friendly step height / max slope onto the agent type the surface
    /// bakes against, so ordinary stairs bake into a continuous walkable surface. In
    /// com.unity.ai.navigation 2.0 the per-surface bake reads its agent type's settings
    /// (<see cref="NavMesh.GetSettingsByID"/>); those settings live in the project's
    /// Navigation Agent-Types asset, which we edit via its SerializedObject (no obsolete
    /// editor APIs). This is best-effort and non-fatal: the NavMeshLinks created by
    /// <see cref="BuildStairLinks"/> guarantee stair traversal regardless of the bake
    /// settings, so we only log if the settings can't be edited.
    /// </summary>
    private static void ApplyStairFriendlyBuildSettings(NavMeshSurface surface)
    {
        NavMeshBuildSettings current = NavMesh.GetSettingsByID(surface.agentTypeID);
        if (current.agentClimb >= AgentStepHeight && current.agentSlope >= AgentMaxSlope)
        {
            return; // already stair-friendly
        }

        // The agent-type settings are serialized assets under ProjectSettings; load and
        // edit the matching agent's climb/slope through SerializedObject.
        Object[] settingsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset");
        if (settingsAssets == null || settingsAssets.Length == 0)
        {
            // Older/newer layouts keep agent types in the NavMesh settings object instead.
            Debug.Log("[GameplaySetup] Agent bake settings asset not found - relying on stair " +
                      "NavMeshLinks for stair traversal.");
            return;
        }

        bool edited = false;
        foreach (Object asset in settingsAssets)
        {
            if (asset == null)
            {
                continue;
            }
            SerializedObject so = new SerializedObject(asset);
            SerializedProperty settingsArray = so.FindProperty("m_Settings");
            if (settingsArray == null || !settingsArray.isArray)
            {
                continue;
            }

            for (int i = 0; i < settingsArray.arraySize; i++)
            {
                SerializedProperty element = settingsArray.GetArrayElementAtIndex(i);
                SerializedProperty climb = element.FindPropertyRelative("agentClimb");
                SerializedProperty slope = element.FindPropertyRelative("agentSlope");
                if (climb == null && slope == null)
                {
                    continue;
                }
                if (climb != null)
                {
                    climb.floatValue = Mathf.Max(climb.floatValue, AgentStepHeight);
                }
                if (slope != null)
                {
                    slope.floatValue = Mathf.Max(slope.floatValue, AgentMaxSlope);
                }
                edited = true;
            }

            if (edited)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        if (edited)
        {
            Debug.Log($"[GameplaySetup] Set agent bake climb>={AgentStepHeight}, slope>={AgentMaxSlope}.");
        }
        else
        {
            Debug.Log("[GameplaySetup] Could not edit agent bake settings - relying on stair " +
                      "NavMeshLinks for stair traversal.");
        }
    }

    /// <summary>
    /// Creates a bidirectional <see cref="NavMeshLink"/> across each stairway so zombies
    /// can chase the player up and down stairs even where step geometry alone would not
    /// bake into a continuous surface. Stairways are detected by name: the stair geometry
    /// roots are the "*_Stairs" transforms (one per stairwell - stairwell, stairwell_2,
    /// east_stairwell, library_staircase) which parent the individual Step_N meshes. The
    /// link spans the bottom landing &lt;-&gt; top landing, with both ends sampled onto the
    /// freshly-baked navmesh. Re-runnable: the link root is rebuilt each call.
    /// </summary>
    private static int BuildStairLinks(Scene scene, NavMeshSurface surface)
    {
        GameObject root = GameObject.Find(StairLinksRootName);
        if (root != null)
        {
            Object.DestroyImmediate(root);
        }
        root = new GameObject(StairLinksRootName);

        // Find stair geometry roots by name. "*_Stairs" objects parent the Step_N meshes
        // (the steps themselves are NOT named with "stair"), so we read the combined
        // bounds of each root's child renderers to get the full vertical run. We also
        // accept any other transform whose name contains "stair"/"stairwell"/"staircase"
        // and that has child renderers, so additional stair groups are still covered.
        Dictionary<string, Bounds> groups = new Dictionary<string, Bounds>();
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            if (go.name == StairLinksRootName || go.name == DoorsRootName)
            {
                continue;
            }

            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                if (!IsStairGeometryRoot(t.gameObject.name))
                {
                    continue;
                }

                // Combine every child renderer (the Step_N meshes) into one bounds.
                Renderer[] childRenderers = t.GetComponentsInChildren<Renderer>(true);
                if (childRenderers.Length == 0)
                {
                    continue;
                }

                bool any = false;
                Bounds b = new Bounds();
                foreach (Renderer child in childRenderers)
                {
                    if (!any)
                    {
                        b = child.bounds;
                        any = true;
                    }
                    else
                    {
                        b.Encapsulate(child.bounds);
                    }
                }
                if (!any)
                {
                    continue;
                }

                string key = StairGroupKey(t.gameObject.name.ToLowerInvariant());
                if (string.IsNullOrEmpty(key))
                {
                    key = t.gameObject.name.ToLowerInvariant();
                }

                if (groups.TryGetValue(key, out Bounds existing))
                {
                    existing.Encapsulate(b);
                    groups[key] = existing;
                }
                else
                {
                    groups[key] = b;
                }
            }
        }

        int created = 0;
        foreach (KeyValuePair<string, Bounds> pair in groups)
        {
            if (TryCreateStairLink(pair.Key, pair.Value, root.transform))
            {
                created++;
            }
        }

        if (created == 0)
        {
            Object.DestroyImmediate(root);
        }
        return created;
    }

    /// <summary>
    /// True for the transform that roots a stairway's step geometry. Primary match is
    /// "*_stairs" (the parents of the Step_N meshes). We deliberately ignore walls,
    /// ceilings, floors, transoms, void backstops and blockers so each stairway yields
    /// exactly one group.
    /// </summary>
    private static bool IsStairGeometryRoot(string objectName)
    {
        string n = objectName.ToLowerInvariant();
        if (!n.Contains("stair"))
        {
            return false;
        }
        // The walkable stair root is named "..._stairs". Exclude the non-walkable parts.
        if (n.EndsWith("_stairs"))
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// Derives the stairway group key (e.g. "stairwell_2") from a stair object's
    /// lower-cased name by cutting at the trailing "_stairs"/structural suffix so all
    /// pieces of one stairwell share a key.
    /// </summary>
    private static string StairGroupKey(string lowerName)
    {
        // The stair roots are "<area>_stairs"; cut the trailing "_stairs" so every piece
        // of one stairwell shares the area key (e.g. "east_stairwell_stairs" -> "east_stairwell",
        // "stairwell_2_stairs" -> "stairwell_2"). Fall back to seg markers for any other shapes.
        string[] markers =
        {
            "_stairs", "_w_seg", "_e_seg", "_n_seg", "_s_seg", "_seg",
        };
        int cut = lowerName.Length;
        foreach (string marker in markers)
        {
            int idx = lowerName.IndexOf(marker, System.StringComparison.Ordinal);
            if (idx > 0 && idx < cut)
            {
                cut = idx;
            }
        }
        return lowerName.Substring(0, cut);
    }

    /// <summary>
    /// Builds one NavMeshLink across a stairway from its bottom end to its top end.
    /// The run direction is the larger horizontal extent of the stair bounds; the two
    /// endpoints sit just beyond each end of that axis (the landings) and are sampled
    /// onto the navmesh so the link actually connects.
    /// </summary>
    private static bool TryCreateStairLink(string key, Bounds bounds, Transform parent)
    {
        // Stairs run along whichever horizontal axis is longer; the two landings sit just
        // beyond each end of that axis. We don't assume which end ascends - we sample BOTH
        // ends onto the navmesh (across the stair's full height) and then call the lower
        // hit the bottom and the higher hit the top.
        bool runX = bounds.size.x >= bounds.size.z;
        float runHalf = (runX ? bounds.size.x : bounds.size.z) * 0.5f;
        if (runHalf < 0.5f)
        {
            return false; // degenerate / not a real staircase
        }

        Vector3 center = bounds.center;
        Vector3 runDir = runX ? Vector3.right : Vector3.forward;
        const float landingPush = 0.6f;

        Vector3 endA = center + runDir * (runHalf + landingPush);
        Vector3 endB = center - runDir * (runHalf + landingPush);

        if (!SampleStairEnd(endA, bounds, out NavMeshHit hitA) ||
            !SampleStairEnd(endB, bounds, out NavMeshHit hitB))
        {
            Debug.LogWarning($"[GameplaySetup] Stair '{key}': couldn't sample both ends onto the " +
                             "navmesh - no link created (check the stair landings are baked).");
            return false;
        }

        Vector3 bottom = hitA.position.y <= hitB.position.y ? hitA.position : hitB.position;
        Vector3 top = hitA.position.y <= hitB.position.y ? hitB.position : hitA.position;

        // Skip a "stair" that is actually flat (no real vertical connection to make).
        if (Mathf.Abs(top.y - bottom.y) < 0.2f)
        {
            return false;
        }

        GameObject linkObj = new GameObject($"StairLink_{key}");
        linkObj.transform.SetParent(parent);
        linkObj.transform.position = bottom;

        NavMeshLink link = linkObj.AddComponent<NavMeshLink>();
        // Endpoints are stored relative to the link transform.
        link.startPoint = Vector3.zero;
        link.endPoint = linkObj.transform.InverseTransformPoint(top);
        link.width = StairLinkWidth;
        link.bidirectional = true;
        link.area = 0; // Walkable
        link.UpdateLink();
        return true;
    }

    /// <summary>
    /// Samples a stairway end onto the navmesh. Tries several heights spanning the stair's
    /// vertical extent (bottom, middle, top) so the probe snaps to whichever landing exists
    /// at that end regardless of which way the stairs ascend.
    /// </summary>
    private static bool SampleStairEnd(Vector3 endXZ, Bounds bounds, out NavMeshHit hit)
    {
        float[] heights =
        {
            bounds.min.y + 0.2f,
            bounds.center.y,
            bounds.max.y + 0.2f,
        };

        foreach (float y in heights)
        {
            Vector3 probe = new Vector3(endXZ.x, y, endXZ.z);
            if (NavMesh.SamplePosition(probe, out hit, 2.5f, NavMesh.AllAreas))
            {
                return true;
            }
        }

        hit = default;
        return false;
    }

    private static Transform[] BuildSpawnPoints(Scene scene)
    {
        GameObject root = GameObject.Find("SpawnPoints");
        if (root != null)
        {
            Object.DestroyImmediate(root);
        }
        root = new GameObject("SpawnPoints");

        List<Vector3> placed = new List<Vector3>();
        List<Transform> result = new List<Transform>();

        foreach (GameObject go in scene.GetRootGameObjects())
        {
            foreach (MeshRenderer renderer in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (result.Count >= MaxSpawnPoints)
                {
                    break;
                }

                string n = renderer.gameObject.name.ToLowerInvariant();
                // Spawn in real rooms (their floors); skip hallways/stairs/generated stuff.
                if (!n.EndsWith("_floor") || n.Contains("hallway") || n.Contains("stair") || n.Contains("courtyard") || n.Contains("parking"))
                {
                    continue;
                }

                Vector3 c = renderer.bounds.center;
                c.y = renderer.bounds.max.y + 0.4f;

                if (!NavMesh.SamplePosition(c, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                {
                    continue;
                }

                bool tooClose = false;
                foreach (Vector3 p in placed)
                {
                    if (Vector3.Distance(p, hit.position) < MinSpawnSpacing)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose)
                {
                    continue;
                }

                GameObject sp = new GameObject("SpawnPoint_" + (result.Count + 1));
                sp.transform.SetParent(root.transform);
                sp.transform.position = hit.position;
                placed.Add(hit.position);
                result.Add(sp.transform);
            }
        }

        return result.ToArray();
    }

    private static void EnsurePlayer(Scene scene)
    {
        PlayerMovement existing = Object.FindFirstObjectByType<PlayerMovement>();
        if (existing != null)
        {
            // A player is already in the scene (e.g. the original "Capsule"). Make sure
            // it can actually take damage and shoot - add the missing gameplay bits.
            EnsurePlayerGameplay(existing.gameObject);
            Debug.Log("[GameplaySetup] Existing player '" + existing.gameObject.name + "' upgraded with health + weapon loadout.");
            return;
        }

        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (playerPrefab == null)
        {
            Debug.LogWarning("[GameplaySetup] No Player.prefab found - run 'Set Up Player' (and 'Set Up Weapons') first, then re-run this.");
            return;
        }

        // Find a starting floor (prefer the 'starter' room) and stand the player on it.
        Vector3 start = new Vector3(0f, 1f, 0f);
        MeshRenderer startFloor = FindFloor(scene, "starter_floor") ?? FindAnyFloor(scene);
        if (startFloor != null)
        {
            Vector3 c = startFloor.bounds.center;
            float top = startFloor.bounds.max.y;
            if (NavMesh.SamplePosition(new Vector3(c.x, top + 0.4f, c.z), out NavMeshHit hit, 6f, NavMesh.AllAreas))
            {
                start = hit.position + Vector3.up * 0.1f;
            }
            else
            {
                start = new Vector3(c.x, top + 1f, c.z);
            }
        }

        GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
        player.transform.position = start;
        EnsurePlayerGameplay(player); // Player.prefab already has these, but be safe.
        Debug.Log("[GameplaySetup] Placed Player at " + start + ".");
    }

    /// <summary>
    /// Make sure a player GameObject can take damage and shoot: adds PlayerHealth,
    /// WeaponController (+ a starter weapon loadout under the camera) and the
    /// first-person body hide if they are missing. Non-destructive.
    /// </summary>
    private static void EnsurePlayerGameplay(GameObject player)
    {
        if (player.GetComponent<PlayerHealth>() == null)
        {
            player.AddComponent<PlayerHealth>();
        }

        Camera cam = player.GetComponentInChildren<Camera>();
        Transform camT = cam != null ? cam.transform : null;
        if (cam != null && !cam.CompareTag("MainCamera"))
        {
            cam.tag = "MainCamera";
        }

        WeaponController wc = player.GetComponent<WeaponController>();
        if (wc == null)
        {
            wc = player.AddComponent<WeaponController>();
            if (camT != null)
            {
                BuildLoadout(wc, camT);
            }
            else
            {
                Debug.LogWarning("[GameplaySetup] Player has no Camera child - added WeaponController but couldn't mount weapon models.");
            }
        }

        if (player.GetComponent<FirstPersonView>() == null)
        {
            player.AddComponent<FirstPersonView>();
        }
    }

    private static void BuildLoadout(WeaponController wc, Transform camT)
    {
        Transform holderT = camT.Find("WeaponHolder");
        if (holderT == null)
        {
            GameObject holder = new GameObject("WeaponHolder");
            holder.transform.SetParent(camT, false);
            holder.transform.localPosition = new Vector3(0.25f, -0.25f, 0.5f);
            holderT = holder.transform;
        }

        var guns = new (string file, string display, WeaponLoadoutSetup.GunCategory cat)[]
        {
            ("M1911", "M1911", WeaponLoadoutSetup.GunCategory.Pistol),
            ("AK74", "AK74", WeaponLoadoutSetup.GunCategory.Rifle),
            ("Bennelli_M4", "Benelli M4", WeaponLoadoutSetup.GunCategory.Shotgun),
            ("M107", "M107", WeaponLoadoutSetup.GunCategory.Sniper),
        };

        wc.weapons = new List<Weapon>();
        int i = 0;
        foreach (var g in guns)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Low Poly Weapons VOL.1/Prefabs/{g.file}.prefab");
            if (prefab == null)
            {
                continue;
            }
            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.transform.SetParent(holderT, false);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            inst.SetActive(i == 0);

            Weapon w = WeaponLoadoutSetup.MakeWeapon(g.display, g.cat, inst);
            w.InitAmmo();
            wc.weapons.Add(w);
            i++;
        }

        wc.currentIndex = 0;
        wc.aimCamera = camT;
    }

    private static MeshRenderer FindFloor(Scene scene, string lowerName)
    {
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.gameObject.name.ToLowerInvariant() == lowerName)
                {
                    return r;
                }
            }
        }
        return null;
    }

    private static MeshRenderer FindAnyFloor(Scene scene)
    {
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                string n = r.gameObject.name.ToLowerInvariant();
                if (n.EndsWith("_floor") && !n.Contains("hallway"))
                {
                    return r;
                }
            }
        }
        return null;
    }
}
