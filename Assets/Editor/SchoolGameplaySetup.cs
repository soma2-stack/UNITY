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
        if (Object.FindFirstObjectByType<PlayerMovement>() != null)
        {
            return; // a player already exists
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
        Debug.Log("[GameplaySetup] Placed Player at " + start + ".");
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
