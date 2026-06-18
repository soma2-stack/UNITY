using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Places simple buyable-style placeholder doors into the EXISTING doorway gaps
/// of the School Of The Dead map. A doorway gap is marked by a "..._Transom"
/// object (the header above the opening). For each transom we drop a cube door
/// into the gap below it, give it a collider so it blocks the player, and add a
/// <see cref="Door"/> component so it can be opened with E at runtime.
///
/// This tool is ADDITIVE and re-runnable: it only creates objects under a single
/// "Generated_Doors" root (recreated each run) and never edits the existing
/// generated geometry, walls, ceilings, stairs or doorway openings.
/// </summary>
public static class SchoolDoorPlacer
{
    private const string ScenePath = "Assets/Scenes/SchoolOfTheDead.unity";
    private const string MaterialFolder = "Assets/Materials/SchoolOfTheDead";
    private const string DoorsRootName = "Generated_Doors";
    private const string GeneratedFixRootName = "Generated_Geometry_Fixes";

    // Two transoms (room side + hallway side) often mark the SAME opening, so we
    // skip a doorway if a door already exists within this distance (world units).
    private const float DuplicateDoorDistance = 0.5f;

    // Doorways that should stay OPEN (no door). Matched if the transom's name
    // CONTAINS any of these (case-insensitive). These use the existing room /
    // doorway names already in the scene - edit this list as you find more.
    private static readonly string[] ExcludedDoorways =
    {
        "teleporter_room",        // teleporter room - no door
        "nurses_office_backroom", // nurses backroom - no door for now (placeholder)
        "the_vault",              // assumed "fallout shelter" - no door  [VERIFY THIS]
        "gym_w_seg",              // gym doorway next to the stairs - stays open into the gym
        // "library_staircase",   // library stairs secret: left buyable so it can be the unlock
    };

    [MenuItem("Tools/School Of The Dead/Place Buyable Doors")]
    public static void PlaceBuyableDoors()
    {
        EnsureFolder("Assets/Materials");
        EnsureFolder(MaterialFolder);

        Material doorMaterial = CreateSolidMaterial("Buyable Door", new Color(0.36f, 0.22f, 0.12f, 1f));

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject doorsRoot = RecreateRoot(DoorsRootName);

        // Collect every floor surface so each door can find the floor below its doorway.
        List<Renderer> floorRenderers = new List<Renderer>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.gameObject.name.ToLowerInvariant().Contains("floor"))
                {
                    floorRenderers.Add(renderer);
                }
            }
        }

        int doorsCreated = 0;
        int skippedNoFloor = 0;
        int skippedExcluded = 0;
        int skippedDuplicate = 0;
        List<Vector3> placedPositions = new List<Vector3>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root == doorsRoot)
            {
                continue;
            }

            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                Transform t = renderer.transform;

                // Skip anything we generate ourselves.
                if (t.root.name == DoorsRootName || t.root.name == GeneratedFixRootName)
                {
                    continue;
                }

                if (!IsDoorwayTransom(t.gameObject.name))
                {
                    continue;
                }

                // Doorways the designer wants left open (teleporter, gym-by-stairs, etc.).
                if (IsExcluded(t.gameObject.name))
                {
                    skippedExcluded++;
                    continue;
                }

                // The same opening can have a transom on both sides - only one door.
                if (IsDuplicatePosition(t.position, placedPositions))
                {
                    skippedDuplicate++;
                    continue;
                }

                if (TryCreateDoor(t, doorsRoot.transform, doorMaterial, floorRenderers))
                {
                    placedPositions.Add(t.position);
                    doorsCreated++;
                }
                else
                {
                    skippedNoFloor++;
                }
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Placed {doorsCreated} buyable doors in {ScenePath}. " +
                  $"Skipped: {skippedExcluded} excluded (left open), {skippedDuplicate} duplicate openings, {skippedNoFloor} with no usable floor.");
    }

    private static bool IsDoorwayTransom(string objectName)
    {
        string name = objectName.ToLowerInvariant();
        // Real doorway headers are "..._transom". The anti-z-fight covers added by
        // the geometry-fix tool also contain "transom" - ignore those so we only
        // get one door per real opening.
        return name.Contains("transom") && !name.Contains("antizfightcover");
    }

    private static bool IsExcluded(string objectName)
    {
        string name = objectName.ToLowerInvariant();
        foreach (string excluded in ExcludedDoorways)
        {
            if (name.Contains(excluded))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDuplicatePosition(Vector3 position, List<Vector3> placedPositions)
    {
        foreach (Vector3 placed in placedPositions)
        {
            if (Vector3.Distance(position, placed) < DuplicateDoorDistance)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryCreateDoor(Transform transom, Transform parent, Material material, List<Renderer> floorRenderers)
    {
        // Transoms are upright (yaw-only) boxes, so their vertical extent is just the Y scale.
        Vector3 worldScale = transom.lossyScale;
        float width = Mathf.Abs(worldScale.x);
        float thickness = Mathf.Max(0.12f, Mathf.Abs(worldScale.z));
        // Run the door all the way up to the TOP of the header so there is never a
        // gap of light between the door and the header above it.
        float doorTopY = transom.position.y + Mathf.Abs(worldScale.y) * 0.5f;

        // Find the floor for THIS doorway. Primary: the floor that belongs to the
        // transom's own room (walk up the hierarchy). Fallback: nearest floor below.
        float floorTopY;
        bool foundFloor = TryGetRoomFloorTop(transom, out floorTopY);
        if (!foundFloor)
        {
            foundFloor = TryGetFloorTopBelow(transom.position, doorTopY, floorRenderers, out floorTopY);
        }

        // No reliable floor -> skip rather than spawn a floating door.
        if (!foundFloor)
        {
            return false;
        }

        float height = doorTopY - floorTopY;

        // Sanity guard: ignore openings that are not normal door-height (e.g. a high
        // window header with no real gap, or a bad floor match) so we never float.
        if (height < 0.3f || height > 8f)
        {
            return false;
        }

        float centerY = floorTopY + height * 0.5f;

        GameObject door = GameObject.CreatePrimitive(PrimitiveType.Cube);
        door.name = $"Door_{transom.gameObject.name}";
        door.transform.SetParent(parent);
        door.transform.position = new Vector3(transom.position.x, centerY, transom.position.z);
        door.transform.rotation = transom.rotation;
        door.transform.localScale = new Vector3(width, height, thickness);
        door.isStatic = false;

        MeshRenderer doorRenderer = door.GetComponent<MeshRenderer>();
        doorRenderer.sharedMaterial = material;

        // Keep the BoxCollider that comes with the primitive so the door blocks the player.
        Door doorComponent = door.AddComponent<Door>();
        doorComponent.doorId = transom.gameObject.name;
        doorComponent.cost = 0; // placeholder only - no buy system yet
        // Slide the door straight down into the floor when opened, fully clearing the gap.
        doorComponent.openMoveOffset = new Vector3(0f, -(height + 0.1f), 0f);

        return foundFloor;
    }

    private static bool TryGetRoomFloorTop(Transform transom, out float floorTopY)
    {
        // Walk up from the doorway until we reach the room that owns it, then use
        // that room's own floor. This ties each door to the correct floor level
        // (deterministic, and correct for multi-storey areas).
        Transform current = transom.parent;
        while (current != null)
        {
            foreach (MeshRenderer renderer in current.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.gameObject.name.ToLowerInvariant().Contains("floor"))
                {
                    floorTopY = renderer.bounds.max.y;
                    return true;
                }
            }

            current = current.parent;
        }

        floorTopY = 0f;
        return false;
    }

    private static bool TryGetFloorTopBelow(Vector3 worldPos, float belowY, List<Renderer> floorRenderers, out float floorTopY)
    {
        const float margin = 0.5f;
        bool found = false;
        floorTopY = float.NegativeInfinity;

        foreach (Renderer floor in floorRenderers)
        {
            Bounds b = floor.bounds;
            if (worldPos.x < b.min.x - margin || worldPos.x > b.max.x + margin)
            {
                continue;
            }
            if (worldPos.z < b.min.z - margin || worldPos.z > b.max.z + margin)
            {
                continue;
            }
            if (b.max.y > belowY + 0.2f)
            {
                continue; // floor is above the doorway header - wrong level
            }

            if (b.max.y > floorTopY)
            {
                floorTopY = b.max.y;
                found = true;
            }
        }

        return found;
    }

    private static GameObject RecreateRoot(string rootName)
    {
        GameObject existing = GameObject.Find(rootName);
        if (existing != null)
        {
            Object.DestroyImmediate(existing);
        }

        GameObject root = new GameObject(rootName);
        root.transform.position = Vector3.zero;
        root.transform.rotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;
        return root;
    }

    private static Material CreateSolidMaterial(string materialName, Color color)
    {
        string materialPath = $"{MaterialFolder}/{materialName}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            material = new Material(shader) { name = materialName };
            AssetDatabase.CreateAsset(material, materialPath);
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string parentFolder = System.IO.Path.GetDirectoryName(folderPath)?.Replace("\\", "/");
        string folderName = System.IO.Path.GetFileName(folderPath);

        if (!string.IsNullOrEmpty(parentFolder) && !AssetDatabase.IsValidFolder(parentFolder))
        {
            EnsureFolder(parentFolder);
        }

        AssetDatabase.CreateFolder(parentFolder, folderName);
    }
}
