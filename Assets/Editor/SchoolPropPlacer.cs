using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Set-dressing tool for the School Of The Dead map. Scatters tasteful props
/// (posters, bookshelves, lockers, desks) so the rooms feel furnished and
/// lived-in.
///
/// This tool is ADDITIVE and re-runnable: it creates a single "Generated_Props"
/// root (destroyed and rebuilt each run) and never edits or deletes any existing
/// scene object. Props are auto-placed against detected wall segments and inside
/// larger rooms - precise hand-placement from code is impossible in a map this
/// size, so treat positions as a believable starting layout and nudge individual
/// props in the editor afterward (see the TODO logged at the end of a run).
/// </summary>
public static class SchoolPropPlacer
{
    private const string ScenePath = "Assets/Scenes/SchoolOfTheDead.unity";
    private const string MaterialFolder = "Assets/Materials/SchoolOfTheDead";
    private const string PropsRootName = "Generated_Props";

    // Roots we generate ourselves (or that other tools own) - never decorate these.
    private static readonly string[] GeneratedRootNames =
    {
        "Generated_Props",
        "Generated_Doors",
        "Generated_Geometry_Fixes",
        "Generated_SecretEgg",
        "Generated_Perks",
    };

    // Map wall segments are named with a direction+segment marker like "_w_seg".
    private static readonly Regex WallSegmentName = new Regex(@"_[nsew]_seg", RegexOptions.IgnoreCase);

    // Caps - keep the map furnished, not cluttered.
    private const int MaxPosters = 40;
    private const int MaxLockerBanks = 6;
    private const int MaxDesks = 5;
    private const int MaxShelves = 12;

    // Place a prop on every Nth eligible wall so they are spread out, not crammed.
    private const int PosterEveryNthWall = 3;
    private const int ShelfEveryNthWall = 7;
    private const int LockerEveryNthHallwayWall = 9;

    [MenuItem("Tools/School Of The Dead/Place Props")]
    public static void PlaceProps()
    {
        EnsureFolder("Assets/Materials");
        EnsureFolder(MaterialFolder);

        // Distinct, clearly-named prop materials so we never collide with the
        // texturing agent's surface materials.
        Material woodMaterial = CreateSolidMaterial("Prop - Wood", new Color(0.42f, 0.27f, 0.14f, 1f), 0.25f);
        Material metalMaterial = CreateSolidMaterial("Prop - Metal Locker", new Color(0.35f, 0.42f, 0.47f, 1f), 0.55f);
        Material deskTopMaterial = CreateSolidMaterial("Prop - Desk Top", new Color(0.55f, 0.40f, 0.24f, 1f), 0.30f);
        Material[] posterMaterials =
        {
            CreateSolidMaterial("Prop - Poster A", new Color(0.74f, 0.18f, 0.16f, 1f), 0.20f),
            CreateSolidMaterial("Prop - Poster B", new Color(0.16f, 0.36f, 0.70f, 1f), 0.20f),
            CreateSolidMaterial("Prop - Poster C", new Color(0.92f, 0.78f, 0.20f, 1f), 0.20f),
            CreateSolidMaterial("Prop - Poster D", new Color(0.20f, 0.58f, 0.36f, 1f), 0.20f),
        };

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Re-runnable: destroy and recreate the single props root each run.
        GameObject propsRoot = RecreatePropsRoot();

        // Collect candidate wall segments (skipping everything we/other tools own).
        List<MeshRenderer> wallSegments = new List<MeshRenderer>();
        List<MeshRenderer> hallwayWalls = new List<MeshRenderer>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (IsGeneratedRoot(root.name))
            {
                continue;
            }

            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!IsWallSegment(renderer.gameObject.name))
                {
                    continue;
                }

                wallSegments.Add(renderer);
                if (IsInsideHallway(renderer.transform))
                {
                    hallwayWalls.Add(renderer);
                }
            }
        }

        // Deterministic ordering so re-runs reproduce the same layout.
        wallSegments.Sort(CompareByName);
        hallwayWalls.Sort(CompareByName);

        int posters = 0;
        int shelves = 0;
        int lockerBanks = 0;
        int desks = 0;

        // 1) POSTERS + 2) SHELVES against a subset of wall segments.
        for (int i = 0; i < wallSegments.Count; i++)
        {
            MeshRenderer wall = wallSegments[i];

            if (posters < MaxPosters && i % PosterEveryNthWall == 0)
            {
                Material posterMat = posterMaterials[posters % posterMaterials.Length];
                if (CreatePoster(wall, propsRoot.transform, posterMat, posters))
                {
                    posters++;
                }
            }

            if (shelves < MaxShelves && i % ShelfEveryNthWall == 0)
            {
                if (CreateShelf(wall, propsRoot.transform, woodMaterial, shelves))
                {
                    shelves++;
                }
            }
        }

        // 3) LOCKER BANKS along hallway walls.
        for (int i = 0; i < hallwayWalls.Count && lockerBanks < MaxLockerBanks; i++)
        {
            if (i % LockerEveryNthHallwayWall != 0)
            {
                continue;
            }

            if (CreateLockerBank(hallwayWalls[i], propsRoot.transform, metalMaterial, lockerBanks))
            {
                lockerBanks++;
            }
        }

        // 4) DESKS clustered in the centre of larger non-hallway rooms.
        desks = PlaceDeskClusters(scene, propsRoot.transform, deskTopMaterial, woodMaterial);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Placed school props in {ScenePath}: {posters} posters, {shelves} shelves, " +
                  $"{lockerBanks} locker bank(s), {desks} desk cluster(s) under '{PropsRootName}'. " +
                  "Re-running this tool rebuilds Generated_Props from scratch (additive-safe). " +
                  "TODO: props are auto-placed against detected wall/room geometry - select any " +
                  "prop under Generated_Props and nudge its position/rotation to taste.");
    }

    // --- Prop builders -----------------------------------------------------

    /// <summary>
    /// A thin decorative quad (poster) laid flat against a wall, offset along the
    /// wall's facing normal so it never z-fights. No collider (purely visual).
    /// </summary>
    private static bool CreatePoster(MeshRenderer wall, Transform parent, Material material, int index)
    {
        Bounds b = wall.bounds;
        // Walls are tall thin boxes; the thin axis is the wall's facing normal.
        Vector3 normal = WallFacingNormal(wall.transform, b);

        const float posterWidth = 1.2f;
        const float posterHeight = 1.6f;
        const float posterThickness = 0.03f;

        // Centre the poster horizontally on the wall, at a believable eye-ish height.
        float wallTop = b.max.y;
        float wallBottom = b.min.y;
        float wallHeight = wallTop - wallBottom;
        if (wallHeight < posterHeight + 0.2f)
        {
            return false; // wall too short to host a poster cleanly
        }

        float centerY = wallBottom + Mathf.Min(wallHeight - posterHeight * 0.5f - 0.2f, wallHeight * 0.55f);
        Vector3 surface = new Vector3(b.center.x, centerY, b.center.z);
        // Sit just off the wall face so it reads as stuck-on, no z-fighting.
        Vector3 pos = surface + normal * (HalfThicknessAlongNormal(b, normal) + posterThickness);

        GameObject poster = GameObject.CreatePrimitive(PrimitiveType.Cube);
        poster.name = $"Poster_{index}_{wall.gameObject.name}";
        poster.transform.SetParent(parent);
        poster.transform.position = pos;
        poster.transform.rotation = Quaternion.LookRotation(normal, Vector3.up);
        poster.transform.localScale = new Vector3(posterWidth, posterHeight, posterThickness);

        // Decorative only - remove the auto BoxCollider so it never blocks the player.
        Collider collider = poster.GetComponent<Collider>();
        if (collider != null)
        {
            Object.DestroyImmediate(collider);
        }

        poster.GetComponent<MeshRenderer>().sharedMaterial = material;
        poster.isStatic = true;
        return true;
    }

    /// <summary>
    /// A simple bookshelf: two uprights + three horizontal shelves, wood-colored,
    /// stood against a wall. Keeps its colliders (solid prop).
    /// </summary>
    private static bool CreateShelf(MeshRenderer wall, Transform parent, Material material, int index)
    {
        Bounds b = wall.bounds;
        Vector3 normal = WallFacingNormal(wall.transform, b);
        Vector3 along = Vector3.Cross(Vector3.up, normal).normalized;

        const float shelfWidth = 1.6f;
        const float shelfHeight = 1.8f;
        const float shelfDepth = 0.35f;
        const float boardThickness = 0.05f;

        float floorY = b.min.y;
        // Stand it just in front of the wall face.
        Vector3 baseCenter = new Vector3(b.center.x, floorY, b.center.z)
                             + normal * (HalfThicknessAlongNormal(b, normal) + shelfDepth * 0.5f);

        GameObject shelf = new GameObject($"Bookshelf_{index}_{wall.gameObject.name}");
        shelf.transform.SetParent(parent);
        shelf.transform.position = baseCenter;
        shelf.transform.rotation = Quaternion.LookRotation(normal, Vector3.up);

        // Two uprights (left/right) running floor to top.
        float halfWidth = shelfWidth * 0.5f - boardThickness * 0.5f;
        AddBoxChild(shelf.transform, "Upright_L", material,
            along * -halfWidth + Vector3.up * (shelfHeight * 0.5f),
            new Vector3(boardThickness, shelfHeight, shelfDepth));
        AddBoxChild(shelf.transform, "Upright_R", material,
            along * halfWidth + Vector3.up * (shelfHeight * 0.5f),
            new Vector3(boardThickness, shelfHeight, shelfDepth));

        // Three horizontal shelves (bottom, middle, top).
        for (int s = 0; s < 3; s++)
        {
            float y = Mathf.Lerp(0.1f, shelfHeight - 0.1f, s / 2f);
            AddBoxChild(shelf.transform, $"Shelf_{s}", material,
                Vector3.up * y,
                new Vector3(shelfWidth, boardThickness, shelfDepth));
        }

        shelf.isStatic = true;
        return true;
    }

    /// <summary>
    /// A bank of 3-4 tall metal lockers stood side by side against a hallway wall.
    /// Each locker keeps its collider (solid prop).
    /// </summary>
    private static bool CreateLockerBank(MeshRenderer wall, Transform parent, Material material, int index)
    {
        Bounds b = wall.bounds;
        Vector3 normal = WallFacingNormal(wall.transform, b);
        Vector3 along = Vector3.Cross(Vector3.up, normal).normalized;

        const float lockerWidth = 0.45f;
        const float lockerHeight = 1.9f;
        const float lockerDepth = 0.45f;

        int count = 3 + (index % 2); // banks of 3 or 4
        float floorY = b.min.y;
        Vector3 wallBase = new Vector3(b.center.x, floorY, b.center.z)
                           + normal * (HalfThicknessAlongNormal(b, normal) + lockerDepth * 0.5f);

        GameObject bank = new GameObject($"LockerBank_{index}_{wall.gameObject.name}");
        bank.transform.SetParent(parent);
        bank.transform.position = wallBase;
        bank.transform.rotation = Quaternion.LookRotation(normal, Vector3.up);

        float totalWidth = count * lockerWidth;
        float start = -totalWidth * 0.5f + lockerWidth * 0.5f;
        for (int i = 0; i < count; i++)
        {
            AddBoxChild(bank.transform, $"Locker_{i}", material,
                along * (start + i * lockerWidth) + Vector3.up * (lockerHeight * 0.5f),
                new Vector3(lockerWidth * 0.95f, lockerHeight, lockerDepth),
                keepCollider: true);
        }

        bank.isStatic = true;
        return true;
    }

    /// <summary>
    /// A desk: a flat desktop cube on four thin legs. Keeps colliders (solid prop).
    /// </summary>
    private static void CreateDesk(Transform parent, Material topMaterial, Material legMaterial, Vector3 position, float yaw, string label)
    {
        const float deskWidth = 1.1f;
        const float deskDepth = 0.6f;
        const float deskTopThickness = 0.06f;
        const float deskHeight = 0.75f;
        const float legThickness = 0.06f;

        GameObject desk = new GameObject($"Desk_{label}");
        desk.transform.SetParent(parent);
        desk.transform.position = position;
        desk.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        // Desktop.
        AddBoxChild(desk.transform, "Top", topMaterial,
            Vector3.up * (deskHeight - deskTopThickness * 0.5f),
            new Vector3(deskWidth, deskTopThickness, deskDepth),
            keepCollider: true);

        // Four legs.
        float lx = deskWidth * 0.5f - legThickness;
        float lz = deskDepth * 0.5f - legThickness;
        float legY = (deskHeight - deskTopThickness) * 0.5f;
        Vector3 legScale = new Vector3(legThickness, deskHeight - deskTopThickness, legThickness);
        AddBoxChild(desk.transform, "Leg_FL", legMaterial, new Vector3(-lx, legY, lz), legScale, keepCollider: true);
        AddBoxChild(desk.transform, "Leg_FR", legMaterial, new Vector3(lx, legY, lz), legScale, keepCollider: true);
        AddBoxChild(desk.transform, "Leg_BL", legMaterial, new Vector3(-lx, legY, -lz), legScale, keepCollider: true);
        AddBoxChild(desk.transform, "Leg_BR", legMaterial, new Vector3(lx, legY, -lz), legScale, keepCollider: true);

        desk.isStatic = true;
    }

    /// <summary>
    /// Finds larger non-hallway rooms (by floor footprint) and drops a small cluster
    /// of desks near the centre of each. Returns the number of clusters created.
    /// </summary>
    private static int PlaceDeskClusters(Scene scene, Transform parent, Material topMaterial, Material legMaterial)
    {
        // Group floor renderers by their owning room transform and measure footprint.
        List<KeyValuePair<Transform, Bounds>> rooms = new List<KeyValuePair<Transform, Bounds>>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (IsGeneratedRoot(root.name))
            {
                continue;
            }

            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.gameObject.name.ToLowerInvariant().Contains("floor"))
                {
                    continue;
                }

                if (IsInsideHallway(renderer.transform))
                {
                    continue; // desks go in rooms, not corridors
                }

                rooms.Add(new KeyValuePair<Transform, Bounds>(renderer.transform, renderer.bounds));
            }
        }

        // Largest footprints first, deterministically.
        rooms.Sort((a, b) =>
        {
            float areaA = a.Value.size.x * a.Value.size.z;
            float areaB = b.Value.size.x * b.Value.size.z;
            int cmp = areaB.CompareTo(areaA);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.Key.name, b.Key.name);
        });

        int clusters = 0;
        for (int r = 0; r < rooms.Count && clusters < MaxDesks; r++)
        {
            Bounds floor = rooms[r].Value;
            // Only big enough rooms host a desk cluster.
            if (floor.size.x < 4f || floor.size.z < 4f)
            {
                continue;
            }

            float topY = floor.max.y;
            Vector3 center = new Vector3(floor.center.x, topY, floor.center.z);

            // A tidy 2x2 cluster of desks facing the same way.
            const float spacingX = 1.6f;
            const float spacingZ = 1.2f;
            float yaw = (clusters % 2 == 0) ? 0f : 90f;
            int n = 0;
            for (int gx = 0; gx < 2; gx++)
            {
                for (int gz = 0; gz < 2; gz++)
                {
                    Vector3 offset = new Vector3((gx - 0.5f) * spacingX, 0f, (gz - 0.5f) * spacingZ);
                    CreateDesk(parent, topMaterial, legMaterial, center + offset, yaw, $"{clusters}_{n}_{rooms[r].Key.name}");
                    n++;
                }
            }

            clusters++;
        }

        return clusters;
    }

    // --- Helpers -----------------------------------------------------------

    private static GameObject AddBoxChild(Transform parent, string name, Material material, Vector3 localPos, Vector3 localScale, bool keepCollider = true)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = localPos;
        box.transform.localRotation = Quaternion.identity;
        box.transform.localScale = localScale;

        if (!keepCollider)
        {
            Collider collider = box.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }
        }

        box.GetComponent<MeshRenderer>().sharedMaterial = material;
        box.isStatic = true;
        return box;
    }

    /// <summary>
    /// Returns a horizontal unit normal pointing along the wall's THIN axis (its
    /// face direction), chosen from the renderer bounds so it works regardless of
    /// how the wall is rotated/named.
    /// </summary>
    private static Vector3 WallFacingNormal(Transform wall, Bounds bounds)
    {
        // Compare world-bounds extents: the smallest horizontal extent is the
        // thickness axis, which is the direction the wall faces.
        Vector3 size = bounds.size;
        Vector3 normal = (size.x <= size.z) ? Vector3.right : Vector3.forward;

        // Use the transform's matching axis for the correct sign where possible.
        Vector3 axisGuess = (size.x <= size.z) ? wall.right : wall.forward;
        axisGuess.y = 0f;
        if (axisGuess.sqrMagnitude > 0.0001f)
        {
            normal = axisGuess.normalized;
        }

        return normal;
    }

    /// <summary>Half the bounds extent measured along the (horizontal) normal.</summary>
    private static float HalfThicknessAlongNormal(Bounds bounds, Vector3 normal)
    {
        Vector3 ext = bounds.extents;
        return Mathf.Abs(normal.x) * ext.x + Mathf.Abs(normal.z) * ext.z;
    }

    private static bool IsWallSegment(string objectName)
    {
        string name = objectName.ToLowerInvariant();
        // Skip headers/transoms - those are door openings, not paintable wall faces.
        if (name.Contains("transom") || name.Contains("header"))
        {
            return false;
        }

        return WallSegmentName.IsMatch(name);
    }

    private static bool IsInsideHallway(Transform transform)
    {
        Transform current = transform;
        while (current != null)
        {
            if (current.gameObject.name.ToLowerInvariant().Contains("hallway"))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static bool IsGeneratedRoot(string rootName)
    {
        foreach (string generated in GeneratedRootNames)
        {
            if (rootName == generated)
            {
                return true;
            }
        }

        return false;
    }

    private static int CompareByName(MeshRenderer a, MeshRenderer b)
    {
        return string.CompareOrdinal(a.gameObject.name, b.gameObject.name);
    }

    private static GameObject RecreatePropsRoot()
    {
        GameObject existing = GameObject.Find(PropsRootName);
        if (existing != null)
        {
            Object.DestroyImmediate(existing);
        }

        GameObject root = new GameObject(PropsRootName);
        root.transform.position = Vector3.zero;
        root.transform.rotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;
        return root;
    }

    private static Material CreateSolidMaterial(string materialName, Color color, float smoothness)
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
        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", smoothness);
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
