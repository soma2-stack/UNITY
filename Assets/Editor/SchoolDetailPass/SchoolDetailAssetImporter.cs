using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Imports the supplemental props modelled in Blender (BlenderWork/Export/DetailAssets/*.fbx) into
/// Assets/SchoolOfTheDead_Detail, remaps their materials onto the project's existing
/// "Furniture - *" materials (plus a few new SOTDD_* ones) and wraps each in a prefab.
/// Re-runnable: models are overwritten, prefabs regenerated.
/// </summary>
public static class SchoolDetailAssetImporter
{
    public const string Root = "Assets/SchoolOfTheDead_Detail";
    public const string ModelDir = Root + "/Models";
    public const string MatDir = Root + "/Materials";
    public const string PrefabDir = Root + "/Prefabs";

    // name -> (base colour, emission colour or black, smoothness, metallic)
    static readonly Dictionary<string, (Color col, Color emit, float smooth, float metal)> NewMats = new()
    {
        { "SOTDD_LightDiffuser", (new Color(1f, 0.98f, 0.92f), new Color(1.6f, 1.55f, 1.4f), 0.4f, 0f) },
        { "SOTDD_ExitRed",       (new Color(1f, 0.1f, 0.06f), new Color(2.2f, 0.15f, 0.1f), 0.3f, 0f) },
        { "SOTDD_VendingPanel",  (new Color(0.3f, 0.6f, 0.9f), new Color(0.35f, 0.75f, 1.2f), 0.7f, 0f) },
        { "SOTDD_Cardboard",     (new Color(0.55f, 0.40f, 0.24f), Color.black, 0.1f, 0f) },
        { "SOTDD_SafetyOrange",  (new Color(0.95f, 0.38f, 0.05f), Color.black, 0.35f, 0f) },
        { "SOTDD_SafetyYellow",  (new Color(0.9f, 0.72f, 0.08f), Color.black, 0.3f, 0f) },
        { "SOTDD_PaintedBlue",   (new Color(0.13f, 0.22f, 0.38f), Color.black, 0.35f, 0.3f) },
        { "SOTDD_PaintedGreen",  (new Color(0.15f, 0.27f, 0.19f), Color.black, 0.3f, 0.3f) },
        { "SOTDD_MattressWhite", (new Color(0.66f, 0.65f, 0.6f), Color.black, 0.05f, 0f) },
        { "SOTDD_TrashBag",      (new Color(0.03f, 0.03f, 0.035f), Color.black, 0.75f, 0f) },
        { "SOTDD_FireRed",       (new Color(0.62f, 0.04f, 0.03f), Color.black, 0.55f, 0.1f) },
        { "SOTDD_Plank",         (new Color(0.45f, 0.34f, 0.22f), Color.black, 0.1f, 0f) },
        { "SOTDD_Cork",          (new Color(0.5f, 0.34f, 0.2f), Color.black, 0.05f, 0f) },
        { "SOTDD_Paper",         (new Color(0.78f, 0.76f, 0.7f), Color.black, 0.05f, 0f) },
        { "SOTDD_Canvas",        (new Color(0.78f, 0.75f, 0.66f), Color.black, 0.05f, 0f) },
        { "SOTDD_Sandbag",       (new Color(0.4f, 0.36f, 0.25f), Color.black, 0.05f, 0f) },
        { "SOTDD_White",         (new Color(0.8f, 0.8f, 0.78f), Color.black, 0.3f, 0f) },
    };

    // Props that should not get a collider (decor / overhead / tiny clutter).
    static readonly HashSet<string> NoCollider = new()
    {
        "SOTDD_CeilingLightPanel", "SOTDD_IndustrialLamp", "SOTDD_PipeRun_4m", "SOTDD_CeilingVent", "SOTDD_RoomSign",
        "SOTDD_BulletinBoard", "SOTDD_Whiteboard", "SOTDD_WallTV", "SOTDD_FireExtinguisher", "SOTDD_MedicineCabinet",
        "SOTDD_ElectricalPanel", "SOTDD_PlankBarricade_Door", "SOTDD_PlankBarricade_Wide", "SOTDD_BasketballHoop",
        "SOTDD_MusicStand", "SOTDD_TrashBag_A", "SOTDD_TrashBag_B", "SOTDD_CardboardBox_S",
        "SOTDD_Broom", "SOTDD_ProjectorScreen", "SOTDD_FallenCeilingTiles", "SOTDD_PlankPile", "SOTDD_BloodPool_A",
        "SOTDD_BloodPool_B", "SOTDD_BloodSmearWall", "SOTDD_Toolbox",
    };

    public static string ImportAll(string sourceDir)
    {
        foreach (string d in new[] { Root, ModelDir, MatDir, PrefabDir })
            if (!AssetDatabase.IsValidFolder(d))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(d).Replace('\\', '/'), Path.GetFileName(d));

        EnsureMaterials();
        var log = new List<string>();
        foreach (string src in Directory.GetFiles(sourceDir, "*.fbx"))
        {
            string name = Path.GetFileNameWithoutExtension(src);
            string dst = ModelDir + "/" + name + ".fbx";
            File.Copy(src, dst, true);
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceSynchronousImport);
            var imp = (ModelImporter)AssetImporter.GetAtPath(dst);
            imp.importCameras = false;
            imp.importLights = false;
            imp.importAnimation = false;
            imp.animationType = ModelImporterAnimationType.None;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
            imp.SaveAndReimport();
            imp.SearchAndRemapMaterials(ModelImporterMaterialName.BasedOnMaterialName, ModelImporterMaterialSearch.Everywhere);
            imp.SaveAndReimport();
            log.Add(BuildPrefab(name, dst));
        }
        AssetDatabase.SaveAssets();
        return string.Join("\n", log);
    }

    /// <summary>Wall posters built from the project's own poster artwork (Assets/Textures/SchoolOfTheDead/Posters).</summary>
    public static string BuildPosterPrefabs()
    {
        const string P = "Assets/Textures/SchoolOfTheDead/Posters/";
        var defs = new (string tex, float rot)[]
        {
            (P + "ChatGPT Image Jun 29, 2026, 06_46_05 PM.png", 90f),   // portrait file with landscape art rotated
            (P + "ChatGPT Image Jun 29, 2026, 06_56_26 PM.png", 0f),
            (P + "ChatGPT Image Jun 29, 2026, 07_36_22 PM.png", 0f),
            (P + "ChatGPT Image Jun 29, 2026, 07_44_20 PM.png", 0f),
            (P + "Gemini_Generated_Image_x1dkp5x1dkp5x1dk.png", 0f),
        };
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Mesh quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var log = new List<string>();
        for (int i = 0; i < defs.Length; i++)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(defs[i].tex);
            if (tex == null) { log.Add("missing " + defs[i].tex); continue; }
            string mp = MatDir + "/SOTDD_Poster_" + (i + 1) + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (m == null) { m = new Material(lit); AssetDatabase.CreateAsset(m, mp); }
            m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", new Color(0.85f, 0.85f, 0.85f)); m.SetFloat("_Smoothness", 0.15f);
            m.enableInstancing = true; EditorUtility.SetDirty(m);

            float aspect = tex.width / (float)tex.height;
            bool rotated = Mathf.Abs(defs[i].rot) > 1f;
            float shown = rotated ? 1f / aspect : aspect;          // displayed width / height
            float h = shown > 1.2f ? 0.68f : 0.85f, w = h * shown;

            var root = new GameObject("SOTDD_Poster_" + (i + 1));
            var q = new GameObject("Quad");
            q.transform.SetParent(root.transform, false);
            q.AddComponent<MeshFilter>().sharedMesh = quad;
            var mr = q.AddComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = ShadowCastingMode.Off;
            // Unity's quad faces -Z: turn it to face +Z (prop front), then spin about the normal for rotated art
            q.transform.localRotation = Quaternion.AngleAxis(defs[i].rot, Vector3.forward) * Quaternion.Euler(0, 180, 0);
            q.transform.localScale = rotated ? new Vector3(h, w, 1) : new Vector3(w, h, 1);
            q.transform.localPosition = new Vector3(0, 0, 0.004f);
            GameObjectUtility.SetStaticEditorFlags(root, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "/SOTDD_Poster_" + (i + 1) + ".prefab");
            Object.DestroyImmediate(root);
            log.Add("poster " + (i + 1) + " " + w.ToString("F2") + "x" + h.ToString("F2"));
        }
        AssetDatabase.SaveAssets();
        return string.Join("\n", log);
    }

    static void EnsureMaterials()
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        foreach (var kv in NewMats)
        {
            string p = MatDir + "/" + kv.Key + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { m = new Material(lit); AssetDatabase.CreateAsset(m, p); }
            m.SetColor("_BaseColor", kv.Value.col);
            m.SetFloat("_Smoothness", kv.Value.smooth);
            m.SetFloat("_Metallic", kv.Value.metal);
            if (kv.Value.emit.maxColorComponent > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", kv.Value.emit);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
        }
        AssetDatabase.SaveAssets();
    }

    static string BuildPrefab(string name, string modelPath)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
        inst.name = name;
        try
        {
            string missing = "";
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                foreach (var m in r.sharedMaterials)
                    if (m == null || AssetDatabase.GetAssetPath(m) == modelPath) missing += (m ? m.name : "null") + ",";
                r.shadowCastingMode = name.Contains("Ceiling") || name.Contains("Sign") || name.Contains("Vent") || name.Contains("Blood")
                    ? ShadowCastingMode.Off : ShadowCastingMode.On;
            }
            if (!NoCollider.Contains(name))
            {
                Bounds b = new Bounds(inst.transform.position, Vector3.zero); bool init = false;
                foreach (var r in inst.GetComponentsInChildren<Renderer>())
                { if (!init) { b = r.bounds; init = true; } else b.Encapsulate(r.bounds); }
                var bc = inst.AddComponent<BoxCollider>();
                bc.center = inst.transform.InverseTransformPoint(b.center);
                bc.size = b.size;
            }
            GameObjectUtility.SetStaticEditorFlags(inst,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            PrefabUtility.SaveAsPrefabAsset(inst, PrefabDir + "/" + name + ".prefab");
            return name + (missing.Length > 0 ? "  (unmapped materials: " + missing + ")" : "  ok");
        }
        finally { Object.DestroyImmediate(inst); }
    }
}
