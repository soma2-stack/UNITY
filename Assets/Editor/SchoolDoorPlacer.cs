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
        "library_staircase",      // secret PaP stairwell - hidden behind the bookcase, no door
        "upper_hallway_e_seg1",   // requested removal (matched lower-case)
        "underground_tunnel_n_seg0", // requested removal (matched lower-case)
    };

    [MenuItem("Tools/School Of The Dead/Place Buyable Doors")]
    public static void PlaceBuyableDoors()
    {
        EnsureFolder("Assets/Materials");
        EnsureFolder(MaterialFolder);

        Material doorMaterial = CreateSolidMaterial("Buyable Door", new Color(0.36f, 0.22f, 0.12f, 1f));

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // NON-DESTRUCTIVE: keep the existing Generated_Doors root and the doors in it
        // (so any manual width/height/position tweaks are preserved). We only ADD
        // doorways that have no door yet, REMOVE doors that are now excluded, and
        // re-apply linking to whatever doors exist.
        GameObject doorsRoot = GetOrCreateRoot(DoorsRootName);

        // Collect every floor surface so each door can find the floor below its doorway.
        // Stairwells have no "floor" mesh - their walkable surface is steps/stairs/landings,
        // so accept those names too (lets stairwell_2 etc. find a base to stand the door on).
        List<Renderer> floorRenderers = new List<Renderer>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (IsWalkableSurfaceName(renderer.gameObject.name))
                {
                    floorRenderers.Add(renderer);
                }
            }
        }

        // Existing doors: index by doorId, seed the dedup positions, and drop any that
        // are now excluded (e.g. the requested removals or library_staircase).
        List<Door> allDoors = new List<Door>(doorsRoot.GetComponentsInChildren<Door>(true));
        HashSet<string> existingDoorIds = new HashSet<string>();
        List<Vector3> placedPositions = new List<Vector3>();
        int removedExcluded = 0;

        for (int i = allDoors.Count - 1; i >= 0; i--)
        {
            Door door = allDoors[i];
            if (door == null)
            {
                allDoors.RemoveAt(i);
                continue;
            }

            if (IsExcluded(door.doorId))
            {
                Object.DestroyImmediate(door.gameObject);
                allDoors.RemoveAt(i);
                removedExcluded++;
                continue;
            }

            existingDoorIds.Add(door.doorId);
            placedPositions.Add(door.transform.position);
        }

        int doorsCreated = 0;
        int skippedNoFloor = 0;
        int skippedExcluded = 0;
        int skippedDuplicate = 0;
        int keptExisting = allDoors.Count;

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

                // This doorway already has a door (kept as-is, manual edits preserved).
                if (existingDoorIds.Contains(t.gameObject.name))
                {
                    continue;
                }

                // The same opening can have a transom on both sides - only one door.
                if (IsDuplicatePosition(t.position, placedPositions))
                {
                    skippedDuplicate++;
                    continue;
                }

                if (TryCreateDoor(t, doorsRoot.transform, doorMaterial, floorRenderers, out Door createdDoor))
                {
                    placedPositions.Add(t.position);
                    existingDoorIds.Add(t.gameObject.name);
                    allDoors.Add(createdDoor);
                    doorsCreated++;
                }
                else
                {
                    skippedNoFloor++;
                }
            }
        }

        // Re-apply linking across ALL doors (existing + new) so connected doors open together.
        int linkedGroups = LinkConnectedDoors(allDoors);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Doors updated in {ScenePath}: {doorsCreated} added, {keptExisting} kept (your edits preserved), " +
                  $"{removedExcluded} removed (now excluded), {linkedGroups} linked group(s). " +
                  $"Skipped: {skippedExcluded} excluded openings, {skippedDuplicate} duplicate openings, {skippedNoFloor} with no usable floor.");
    }

    /// <summary>
    /// Links doors that connect the SAME area (e.g. both ends of a stairwell) so
    /// opening one opens them all. Doors are grouped by the room/area identifier
    /// parsed from their source transom name; any group with 2+ doors becomes a
    /// mutually-linked set. Returns the number of multi-door groups linked.
    /// </summary>
    private static int LinkConnectedDoors(List<Door> doors)
    {
        Dictionary<string, List<Door>> groups = new Dictionary<string, List<Door>>();

        foreach (Door door in doors)
        {
            if (door == null)
            {
                continue;
            }

            string key = ParseAreaKey(door.doorId);
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            if (!groups.TryGetValue(key, out List<Door> list))
            {
                list = new List<Door>();
                groups[key] = list;
            }
            list.Add(door);
        }

        int linkedGroups = 0;
        foreach (KeyValuePair<string, List<Door>> pair in groups)
        {
            List<Door> group = pair.Value;
            if (group.Count < 2)
            {
                continue;
            }

            linkedGroups++;
            foreach (Door door in group)
            {
                // Link each door to every OTHER door in the same area.
                List<Door> others = new List<Door>();
                foreach (Door other in group)
                {
                    if (other != door)
                    {
                        others.Add(other);
                    }
                }
                door.linkedDoors = others.ToArray();
            }
        }

        return linkedGroups;
    }

    /// <summary>
    /// Parses the room/area identifier from a transom name by stripping the trailing
    /// directional + segment suffix (e.g. "..._W_Seg2_Right_Transom" -> the area).
    /// Doors that share this key connect the same area and should open together.
    /// </summary>
    private static string ParseAreaKey(string transomName)
    {
        if (string.IsNullOrEmpty(transomName))
        {
            return null;
        }

        string name = transomName.ToLowerInvariant();

        // Cut at the first directional/segment marker so "stairwell_2_W_Seg1" and
        // "stairwell_2_E_Seg3" both reduce to "stairwell_2".
        string[] markers = { "_w_seg", "_e_seg", "_n_seg", "_s_seg", "_seg", "_transom" };
        int cut = name.Length;
        foreach (string marker in markers)
        {
            int idx = name.IndexOf(marker, System.StringComparison.Ordinal);
            if (idx >= 0 && idx < cut)
            {
                cut = idx;
            }
        }

        return name.Substring(0, cut);
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

    private static bool TryCreateDoor(Transform transom, Transform parent, Material material, List<Renderer> floorRenderers, out Door createdDoor)
    {
        createdDoor = null;
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

        createdDoor = doorComponent;
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
            // Prefer the lowest walkable surface in this room/stairwell so a door over
            // a staircase reaches the bottom step rather than floating on a landing.
            float lowest = float.PositiveInfinity;
            bool any = false;
            foreach (MeshRenderer renderer in current.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (IsWalkableSurfaceName(renderer.gameObject.name))
                {
                    float top = renderer.bounds.max.y;
                    if (top < lowest)
                    {
                        lowest = top;
                        any = true;
                    }
                }
            }

            if (any)
            {
                floorTopY = lowest;
                return true;
            }

            current = current.parent;
        }

        floorTopY = 0f;
        return false;
    }

    private static bool IsWalkableSurfaceName(string objectName)
    {
        string name = objectName.ToLowerInvariant();
        return name.Contains("floor") || name.Contains("stair") || name.Contains("step") ||
               name.Contains("landing");
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

    private static GameObject GetOrCreateRoot(string rootName)
    {
        GameObject existing = GameObject.Find(rootName);
        if (existing != null)
        {
            // Keep it (and its existing doors) so manual tweaks survive re-runs.
            return existing;
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
