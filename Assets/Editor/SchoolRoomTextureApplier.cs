using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SchoolRoomTextureApplier
{
    private const string ScenePath = "Assets/Scenes/SchoolOfTheDead.unity";
    private const string MaterialFolder = "Assets/Materials/SchoolOfTheDead";
    private const string TextureFolder = "Assets/Textures/SchoolOfTheDead";
    private const string GeneratedFixRootName = "Generated_Geometry_Fixes";
    private const string GymVoidFixRootName = "Gym_Void_Hole_Fixes";

    private static readonly Regex WallSegmentName = new Regex(@"_[nsew]_seg", RegexOptions.IgnoreCase);

    [MenuItem("Tools/School Of The Dead/Apply Room Textures")]
    public static void ApplyRoomTextures()
    {
        EnsureFolder("Assets/Materials");
        EnsureFolder(MaterialFolder);

        Dictionary<SurfaceType, Material> materials = new Dictionary<SurfaceType, Material>
        {
            { SurfaceType.Floor, CreateOrUpdateMaterial("Dirty School Tile Floor", "FLoor.png", new Vector2(4f, 4f), 0.8f) },
            { SurfaceType.HallwayWall, CreateOrUpdateMaterial("Grimy Hallway Walls", "Hallway walls.png", new Vector2(1f, 1f), 0.75f) },
            { SurfaceType.RoomWall, CreateSolidMaterial("Neutral Room Walls", new Color(0.45f, 0.43f, 0.38f, 1f)) },
            { SurfaceType.Ceiling, CreateOrUpdateMaterial("Stained Hallway Ceiling", "Hallway ceiling.png", new Vector2(3f, 3f), 0.7f) },
            { SurfaceType.Stairs, CreateOrUpdateMaterial("Worn Concrete Stairs", "Stairs.png", new Vector2(1f, 1f), 0.75f) },
            { SurfaceType.Trim, CreateTrimMaterial() },
        };

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int changedRenderers = 0;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.transform.root.name == GeneratedFixRootName || !TryGetSurfaceType(renderer.transform, out SurfaceType surfaceType))
                {
                    continue;
                }

                renderer.sharedMaterial = materials[surfaceType];
                changedRenderers++;
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Applied school room textures to {changedRenderers} renderers in {ScenePath}.");
    }

    [MenuItem("Tools/School Of The Dead/Fix Wall Glitching And Void Holes")]
    public static void FixWallGlitchingAndVoidHoles()
    {
        EnsureFolder("Assets/Materials");
        EnsureFolder(MaterialFolder);

        Material trimMaterial = CreateTrimMaterial();
        Material voidMaterial = CreateVoidMaterial();

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject fixRoot = RecreateGeneratedFixRoot();
        int coveredHeaders = 0;
        int voidBackstops = 0;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root == fixRoot)
            {
                continue;
            }

            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                string objectName = renderer.gameObject.name;
                if (IsHeaderOrTransom(objectName))
                {
                    CreateGeneratedPanel($"{objectName}_AntiZFightCover", renderer.transform, fixRoot.transform, trimMaterial, 0.1f, 0.04f, 0f);
                    renderer.enabled = false;
                    coveredHeaders++;
                    continue;
                }

                if (IsWallLike(objectName))
                {
                    CreateGeneratedPanel($"{objectName}_VoidBackstop", renderer.transform, fixRoot.transform, voidMaterial, 0.18f, 0.03f, -0.08f);
                    voidBackstops++;
                }
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Fixed wall/header visuals in {ScenePath}. Covered {coveredHeaders} header/transom renderers and added {voidBackstops} void backstops.");
    }

    [MenuItem("Tools/School Of The Dead/Fix Gym Void Holes")]
    public static void FixGymVoidHoles()
    {
        EnsureFolder("Assets/Materials");
        EnsureFolder(MaterialFolder);

        Material voidMaterial = CreateVoidMaterial();
        Material trimMaterial = CreateTrimMaterial();
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject fixRoot = GetOrCreateGeneratedFixRoot();
        GameObject gymFixRoot = RecreateGeneratedChild(fixRoot.transform, GymVoidFixRootName);

        Bounds gymBounds = new Bounds();
        bool hasBounds = false;
        int wallBackstops = 0;
        int coveredHeaders = 0;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root == fixRoot)
            {
                continue;
            }

            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!IsGymMainArea(renderer.transform))
                {
                    continue;
                }

                if (!hasBounds)
                {
                    gymBounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    gymBounds.Encapsulate(renderer.bounds);
                }

                string objectName = renderer.gameObject.name;
                if (IsHeaderOrTransom(objectName))
                {
                    CreateGeneratedPanel($"{objectName}_Gym_AntiZFightCover", renderer.transform, gymFixRoot.transform, trimMaterial, 0.2f, 0.06f, 0f);
                    renderer.enabled = false;
                    coveredHeaders++;
                }
                else if (IsWallLike(objectName))
                {
                    CreateGeneratedPanel($"{objectName}_Gym_VoidBackstop", renderer.transform, gymFixRoot.transform, voidMaterial, 0.7f, 0.08f, -0.14f);
                    wallBackstops++;
                }
            }
        }

        int shellPanels = 0;
        if (hasBounds)
        {
            gymBounds.Expand(new Vector3(2f, 1f, 2f));
            shellPanels = CreateGymVoidShell(gymBounds, gymFixRoot.transform, voidMaterial);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Fixed gym void holes in {ScenePath}. Added {wallBackstops} gym wall backstops, {coveredHeaders} header covers, and {shellPanels} shell panels.");
    }

    private static bool TryGetSurfaceType(Transform transform, out SurfaceType surfaceType)
    {
        string objectName = transform.gameObject.name;
        string name = objectName.ToLowerInvariant();

        if (IsHeaderOrTransom(name))
        {
            surfaceType = SurfaceType.Trim;
            return true;
        }

        if (IsStairSurface(name))
        {
            surfaceType = SurfaceType.Stairs;
            return true;
        }

        if (name.Contains("ceiling"))
        {
            surfaceType = SurfaceType.Ceiling;
            return true;
        }

        if (name.Contains("floor"))
        {
            surfaceType = SurfaceType.Floor;
            return true;
        }

        if (IsWallLike(name))
        {
            surfaceType = IsInsideHallway(transform) ? SurfaceType.HallwayWall : SurfaceType.RoomWall;
            return true;
        }

        surfaceType = SurfaceType.None;
        return false;
    }

    private static Material CreateOrUpdateMaterial(string materialName, string textureName, Vector2 tiling, float colorValue)
    {
        string texturePath = $"{TextureFolder}/{textureName}";
        ConfigureTexture(texturePath);

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture == null)
        {
            Debug.LogError($"Missing texture: {texturePath}");
            return null;
        }

        string materialPath = $"{MaterialFolder}/{materialName}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            material = new Material(shader)
            {
                name = materialName
            };
            AssetDatabase.CreateAsset(material, materialPath);
        }

        Color tint = new Color(colorValue, colorValue, colorValue, 1f);
        SetMaterialTexture(material, "_BaseMap", texture, tiling);
        SetMaterialTexture(material, "_MainTex", texture, tiling);

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", tint);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", tint);
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material CreateTrimMaterial()
    {
        Material material = CreateSolidMaterial("Dark Header Trim", new Color(0.16f, 0.14f, 0.12f, 1f));

        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", 0.15f);
        }

        return material;
    }

    private static Material CreateVoidMaterial()
    {
        return CreateSolidMaterial("Void Shadow Backstop", new Color(0.015f, 0.013f, 0.012f, 1f));
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

            material = new Material(shader)
            {
                name = materialName
            };
            AssetDatabase.CreateAsset(material, materialPath);
        }

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", null);
        }

        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", null);
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

    private static void ConfigureTexture(string texturePath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer == null)
        {
            AssetDatabase.ImportAsset(texturePath);
            return;
        }

        bool changed = false;
        if (importer.wrapMode != TextureWrapMode.Repeat)
        {
            importer.wrapMode = TextureWrapMode.Repeat;
            changed = true;
        }

        if (!importer.mipmapEnabled)
        {
            importer.mipmapEnabled = true;
            changed = true;
        }

        if (changed)
        {
            importer.SaveAndReimport();
        }
        else
        {
            AssetDatabase.ImportAsset(texturePath);
        }
    }

    private static GameObject RecreateGeneratedFixRoot()
    {
        GameObject existing = GameObject.Find(GeneratedFixRootName);
        if (existing != null)
        {
            Object.DestroyImmediate(existing);
        }

        GameObject fixRoot = new GameObject(GeneratedFixRootName);
        fixRoot.isStatic = true;
        return fixRoot;
    }

    private static GameObject GetOrCreateGeneratedFixRoot()
    {
        GameObject existing = GameObject.Find(GeneratedFixRootName);
        if (existing != null)
        {
            return existing;
        }

        GameObject fixRoot = new GameObject(GeneratedFixRootName);
        fixRoot.isStatic = true;
        return fixRoot;
    }

    private static GameObject RecreateGeneratedChild(Transform parent, string childName)
    {
        Transform existing = parent.Find(childName);
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
        }

        GameObject child = new GameObject(childName);
        child.transform.SetParent(parent);
        child.transform.localPosition = Vector3.zero;
        child.transform.localRotation = Quaternion.identity;
        child.transform.localScale = Vector3.one;
        child.isStatic = true;
        return child;
    }

    private static void CreateGeneratedPanel(string panelName, Transform source, Transform parent, Material material, float expandAmount, float thicknessPadding, float normalOffset)
    {
        GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = panelName;
        panel.transform.SetParent(parent);
        panel.transform.position = source.position + source.forward * normalOffset;
        panel.transform.rotation = source.rotation;

        Vector3 scale = source.lossyScale;
        scale.x = Mathf.Max(0.05f, Mathf.Abs(scale.x) + expandAmount);
        scale.y = Mathf.Max(0.05f, Mathf.Abs(scale.y) + expandAmount);
        scale.z = Mathf.Max(0.04f, Mathf.Abs(scale.z) + thicknessPadding);
        panel.transform.localScale = scale;

        Collider collider = panel.GetComponent<Collider>();
        if (collider != null)
        {
            Object.DestroyImmediate(collider);
        }

        MeshRenderer panelRenderer = panel.GetComponent<MeshRenderer>();
        panelRenderer.sharedMaterial = material;
        panel.isStatic = true;
    }

    private static int CreateGymVoidShell(Bounds bounds, Transform parent, Material material)
    {
        const float thickness = 0.35f;
        int createdPanels = 0;
        Vector3 center = bounds.center;
        Vector3 size = bounds.size;

        CreateWorldPanel("Gym_Back_VoidShell", parent, material, new Vector3(center.x, center.y, bounds.min.z - thickness), new Vector3(size.x, size.y, thickness));
        createdPanels++;
        CreateWorldPanel("Gym_Front_VoidShell", parent, material, new Vector3(center.x, center.y, bounds.max.z + thickness), new Vector3(size.x, size.y, thickness));
        createdPanels++;
        CreateWorldPanel("Gym_Left_VoidShell", parent, material, new Vector3(bounds.min.x - thickness, center.y, center.z), new Vector3(thickness, size.y, size.z));
        createdPanels++;
        CreateWorldPanel("Gym_Right_VoidShell", parent, material, new Vector3(bounds.max.x + thickness, center.y, center.z), new Vector3(thickness, size.y, size.z));
        createdPanels++;
        CreateWorldPanel("Gym_Ceiling_VoidShell", parent, material, new Vector3(center.x, bounds.max.y + thickness, center.z), new Vector3(size.x, thickness, size.z));
        createdPanels++;
        CreateWorldPanel("Gym_Floor_VoidShell", parent, material, new Vector3(center.x, bounds.min.y - thickness, center.z), new Vector3(size.x, thickness, size.z));
        createdPanels++;

        return createdPanels;
    }

    private static void CreateWorldPanel(string panelName, Transform parent, Material material, Vector3 position, Vector3 scale)
    {
        GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = panelName;
        panel.transform.SetParent(parent);
        panel.transform.position = position;
        panel.transform.rotation = Quaternion.identity;
        panel.transform.localScale = scale;

        Collider collider = panel.GetComponent<Collider>();
        if (collider != null)
        {
            Object.DestroyImmediate(collider);
        }

        MeshRenderer panelRenderer = panel.GetComponent<MeshRenderer>();
        panelRenderer.sharedMaterial = material;
        panel.isStatic = true;
    }

    private static bool IsHeaderOrTransom(string objectName)
    {
        string name = objectName.ToLowerInvariant();
        return name.Contains("transom") || name == "header" || name.Contains("_header");
    }

    private static bool IsWallLike(string objectName)
    {
        string name = objectName.ToLowerInvariant();
        return !IsHeaderOrTransom(name) && (name.Contains("wall") || WallSegmentName.IsMatch(name));
    }

    private static bool IsStairSurface(string objectName)
    {
        string name = objectName.ToLowerInvariant();
        return name.Contains("step") || name.EndsWith("_stairs") || name.EndsWith(" stairs") || name == "stairs";
    }

    private static bool IsGymMainArea(Transform transform)
    {
        Transform current = transform;
        while (current != null)
        {
            string name = current.gameObject.name.ToLowerInvariant();
            if (name.StartsWith("gym_north_hallway") || name.StartsWith("gym_east_connector"))
            {
                return false;
            }

            if (name == "gym" || name.StartsWith("gym_") || name.Contains("_gymnasium"))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static bool IsInsideHallway(Transform transform)
    {
        Transform current = transform;
        while (current != null)
        {
            string name = current.gameObject.name.ToLowerInvariant();
            if (name.Contains("hallway"))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static void SetMaterialTexture(Material material, string propertyName, Texture texture, Vector2 tiling)
    {
        if (!material.HasProperty(propertyName))
        {
            return;
        }

        material.SetTexture(propertyName, texture);
        material.SetTextureScale(propertyName, tiling);
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

    private enum SurfaceType
    {
        None,
        Floor,
        HallwayWall,
        RoomWall,
        Ceiling,
        Stairs,
        Trim
    }
}
