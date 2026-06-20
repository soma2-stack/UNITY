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

    [MenuItem("Tools/School Of The Dead/Setup Gameplay (NavMesh + Spawners)")]
    public static void SetupGameplay()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // 1) Bake the NavMesh off the level's render geometry.
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
        surface.BuildNavMesh();
        Debug.Log("[GameplaySetup] NavMesh baked.");

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
