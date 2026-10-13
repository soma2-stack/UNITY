using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds an inventory of the prop prefabs / models already in the project (dimensions,
/// materials, colliders, scene usage) for the School of the Dead detail pass. Read-only.
/// </summary>
public static class SchoolAssetInventory
{
    public static readonly string[] SearchFolders =
    {
        "Assets/ThirdParty/UnityStore/school/Prefabs",
        "Assets/ThirdParty/UnityStore/Lockers",
        "Assets/ThirdParty/UnityStore/Meshy",
        "Assets/ThirdParty/UnityStore/models",
        "Assets/ThirdParty/UnityStore/PolyRonin",
        "Assets/ThirdParty/UnityStore/Clock",
        "Assets/ThirdParty/UnityStore/Viverna_Assets/Globe",
        "Assets/ThirdParty/UnityStore/Assets_FenceChained",
        "Assets/ThirdParty/UnityStore/AddOns",
        "Assets/ThirdParty/UnityStore/Dry_Trees",
        "Assets/ThirdParty/UnityStore/YughuesFreeBushes2018",
        "Assets/ThirdParty/UnityStore/ARCADE - FREE Racing Car/Prefabs (With Colliders)",
        "Assets/POLYGON - FPS free/Prefabs/props",
        "Assets/Prefabs/Clusters",
        "Assets/Prefabs/Locker Prefabs",
        "Assets/Prefabs/SchoolOfTheDead",
        "Assets/Prefabs/chair1.prefab",
        "Assets/Prefabs/filing-cabinet.prefab",
        "Assets/SchoolOfTheDead_Detail/Prefabs",
    };

    public struct Info
    {
        public string path;
        public Vector3 size;       // world-space AABB size at identity yaw (includes prefab root rotation/scale)
        public Vector3 pivotToMin; // bounds.min - root position
        public int renderers, colliders, materials, tris;
        public string materialNames;
    }

    public static Info Measure(GameObject prefab)
    {
        var info = new Info { path = AssetDatabase.GetAssetPath(prefab) };
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            go.transform.position = Vector3.zero;
            var rs = go.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToArray();
            info.renderers = rs.Length;
            info.colliders = go.GetComponentsInChildren<Collider>(true).Length;
            var mats = new HashSet<string>();
            foreach (var r in rs) foreach (var m in r.sharedMaterials) if (m) mats.Add(m.name);
            info.materials = mats.Count;
            info.materialNames = string.Join("/", mats.Take(4));
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh) info.tris += (int)(mf.sharedMesh.triangles.Length / 3);
            if (rs.Length > 0)
            {
                Bounds b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                info.size = b.size;
                info.pivotToMin = b.min - go.transform.position;
            }
        }
        finally { Object.DestroyImmediate(go); }
        return info;
    }

    public static string WriteInventory(string outPath)
    {
        var usage = new Dictionary<string, int>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (!PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)) continue;
            var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
            if (!src) continue;
            string p = AssetDatabase.GetAssetPath(src);
            usage[p] = usage.TryGetValue(p, out int v) ? v + 1 : 1;
        }

        var sb = new StringBuilder();
        sb.AppendLine("| Asset | Type | Size W×H×D (m) | Tris | Mats | Colliders | Used in scene | Suggested rooms |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        int n = 0;
        foreach (string folder in SearchFolders)
        {
            IEnumerable<string> paths;
            if (folder.EndsWith(".prefab")) paths = new[] { folder };
            else if (AssetDatabase.IsValidFolder(folder))
                paths = AssetDatabase.FindAssets("t:Prefab", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath)
                    .Concat(AssetDatabase.FindAssets("t:Model", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath));
            else continue;
            foreach (string p in paths.Distinct().OrderBy(x => x))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (!go) continue;
                Info i = Measure(go);
                usage.TryGetValue(p, out int used);
                string type = p.EndsWith(".prefab") ? "Prefab" : "Model";
                sb.AppendLine($"| `{p.Replace("Assets/", "")}` | {type} | {i.size.x:F2}×{i.size.y:F2}×{i.size.z:F2} | {i.tris} | {i.materials} ({i.materialNames}) | {i.colliders} | {used} | {Suggest(p)} |");
                n++;
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
        File.WriteAllText(outPath, sb.ToString());
        return n + " assets -> " + outPath;
    }

    static string Suggest(string p)
    {
        string s = p.ToLowerInvariant();
        if (s.Contains("locker")) return "Hallways, gym";
        if (s.Contains("book")) return "Library, classrooms, offices";
        if (s.Contains("computer") || s.Contains("laptop")) return "Computer lab, offices, security, library";
        if (s.Contains("chair") || s.Contains("table") || s.Contains("desk")) return "Classrooms, offices, conference";
        if (s.Contains("board")) return "Classrooms, hallways (notices)";
        if (s.Contains("kitchen")) return "Kitchen, cafeteria";
        if (s.Contains("polyronin") || s.Contains("dumbbell") || s.Contains("bench")) return "Gym";
        if (s.Contains("fence") || s.Contains("chainlink")) return "Exterior";
        if (s.Contains("tree") || s.Contains("bush")) return "Exterior, courtyards";
        if (s.Contains("car")) return "Parking lot";
        if (s.Contains("paper") || s.Contains("notebook") || s.Contains("sheet")) return "Everywhere (floor clutter)";
        if (s.Contains("trash")) return "Hallways, cafeteria, exterior";
        if (s.Contains("rack")) return "Library, storage, offices";
        if (s.Contains("plybox") || s.Contains("petrol")) return "Storage, basement";
        if (s.Contains("food") || s.Contains("mg3d")) return "Cafeteria, kitchen";
        if (s.Contains("clock")) return "Classrooms, hallways";
        if (s.Contains("globe")) return "Classrooms (history/geography)";
        if (s.Contains("exit")) return "Hallways, stairwells";
        if (s.Contains("wet floor")) return "Hallways, bathrooms, kitchen";
        if (s.Contains("backpack")) return "Classrooms, hallways";
        if (s.Contains("trophy")) return "Hallways, gym";
        if (s.Contains("filing")) return "Offices";
        if (s.Contains("fan")) return "Offices, classrooms (ceiling)";
        return "";
    }
}
