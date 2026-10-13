using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Random = System.Random;

/// <summary>
/// Core of the School of the Dead detail pass: prop library, room analysis and validated placement.
///
/// Everything this kit creates lives under one scene root ("SOTD_DetailPass"), grouped by floor and
/// room, so it can be inspected, re-generated per room, or removed without touching the original map.
/// Placement is validated: each prop is grounded by its renderer bounds, kept inside the room, kept
/// out of doorway / interactable / zombie-spawn clearance zones, and rejected if it overlaps any
/// existing collider or another placed prop.
/// </summary>
public static partial class SchoolDetailKit
{
    public const string RootName = "SOTD_DetailPass";
    const string MapRootName = "School Map - Zombies Scale";

    // ------------------------------------------------------------------ prop library

    public enum Kind { Furniture, Clutter, Wall, Ceiling, Surface }

    public class PropDef
    {
        public string path;
        public Vector3 baseEuler;     // extra correction applied before yaw (most prefabs need none)
        public float fitHeight;       // >0: uniform scale so height == fitHeight
        public float fitMax;          // >0: uniform scale so largest horizontal size == fitMax
        public string mat;            // single material override (null = keep prefab materials)
        public float facing;          // yaw offset so that the prop's "front" faces local +Z
        public Kind kind;
        public bool addCollider;      // add a BoxCollider if the prefab has none
        public GameObject prefab;
        public Material material;
    }

    static Dictionary<string, PropDef> lib;
    const string S = "Assets/ThirdParty/UnityStore/school/Prefabs/props/";
    const string D = "Assets/SchoolOfTheDead_Detail/Prefabs/";
    const string M = "Assets/Materials/SchoolOfTheDead/";

    static void Def(string key, string path, Kind kind, float fitH = 0, float fitMax = 0, string mat = null,
        float facing = 0, Vector3 baseEuler = default, bool collider = false)
    {
        lib[key] = new PropDef { path = path, kind = kind, fitHeight = fitH, fitMax = fitMax, mat = mat, facing = facing,
            baseEuler = baseEuler, addCollider = collider };
    }

    public static Dictionary<string, PropDef> Lib
    {
        get
        {
            if (lib != null) return lib;
            lib = new Dictionary<string, PropDef>();
            // --- classroom furniture (existing school pack, materials overridden like the rest of the scene)
            Def("studentDesk", "Assets/Prefabs/chair1.prefab", Kind.Furniture, fitH: 0.95f, mat: M + "Furniture - Wood Mid.mat", collider: true);
            Def("chair", S + "chair.prefab", Kind.Furniture, mat: M + "Furniture - Plastic Dark.mat");
            Def("chairWood", S + "chair.prefab", Kind.Furniture, mat: M + "Furniture - Wood Dark.mat");
            Def("teacherDesk", S + "table3.prefab", Kind.Furniture, mat: M + "Furniture - Wood Dark.mat");
            Def("labBench", S + "table1.prefab", Kind.Furniture, mat: M + "Furniture - Concrete.mat", facing: 180);
            Def("cafeTable", S + "table2.prefab", Kind.Furniture, mat: M + "Furniture - Metal Grey.mat");
            Def("swivelChair", "Assets/ThirdParty/UnityStore/models/swivel-chair.fbx", Kind.Furniture, fitH: 1.0f, mat: M + "Furniture - Plastic Dark.mat", collider: true);
            Def("shelfWood", S + "rack.prefab", Kind.Furniture, fitH: 2.2f, mat: M + "Furniture - Wood Dark.mat");
            Def("shelfBooks", S + "rack1.prefab", Kind.Furniture, fitH: 2.2f, mat: M + "Furniture - Wood Dark.mat");
            Def("sofa", S + "showcase.prefab", Kind.Furniture, mat: M + "Furniture - Fabric Dark.mat", facing: 180);
            Def("filingCabinet", "Assets/Prefabs/filing-cabinet.prefab", Kind.Furniture, fitH: 1.32f, mat: M + "Furniture - Metal Grey.mat", facing: 90, collider: true);
            Def("lockerTall", S + "locker_1.prefab", Kind.Furniture, fitH: 2.0f, mat: M + "Prop - Metal Locker.mat");
            Def("lockerGray", "Assets/Prefabs/Locker Prefabs/Gray Locker.prefab", Kind.Furniture, fitH: 1.95f);
            Def("lockerGreen", "Assets/Prefabs/Locker Prefabs/Green Locker.prefab", Kind.Furniture, fitH: 1.95f);
            Def("lockerRusty", "Assets/Prefabs/Locker Prefabs/rusty gray locker.prefab", Kind.Furniture, fitH: 1.95f);
            Def("monitor", S + "computer.prefab", Kind.Surface, fitH: 0.42f, mat: M + "Furniture - Plastic Dark.mat", facing: 180);
            Def("keyboard", S + "computer2.prefab", Kind.Surface, fitMax: 0.45f, mat: M + "Furniture - Plastic Dark.mat");
            Def("laptop", S + "a laptop.prefab", Kind.Surface, fitMax: 0.36f, mat: M + "Furniture - Plastic Dark.mat");
            Def("book", S + "book3.prefab", Kind.Surface, fitH: 0.24f, mat: M + "Prop - Wood.mat");
            Def("tray", S + "tray.prefab", Kind.Surface, fitMax: 0.45f, mat: M + "Furniture - Metal Grey.mat");
            Def("posterA", D + "SOTDD_Poster_2.prefab", Kind.Wall);
            Def("posterB", D + "SOTDD_Poster_3.prefab", Kind.Wall);
            Def("posterC", D + "SOTDD_Poster_4.prefab", Kind.Wall);
            Def("posterD", D + "SOTDD_Poster_1.prefab", Kind.Wall);
            Def("posterE", D + "SOTDD_Poster_5.prefab", Kind.Wall);
            Def("chalkboard", S + "board2.prefab", Kind.Wall, fitMax: 3.6f, mat: M + "Classroom - Chalkboard.mat");
            Def("clock", "Assets/ThirdParty/UnityStore/Clock/Prefabs/Clock.prefab", Kind.Wall, fitMax: 0.42f, mat: M + "Library - Clock Face.mat");
            Def("speaker", S + "speaker.prefab", Kind.Wall, mat: M + "Furniture - Plastic Dark.mat");
            Def("globe", "Assets/ThirdParty/UnityStore/Viverna_Assets/Globe/Prefabs/Globe.prefab", Kind.Surface);
            // --- Meshy-generated assets already in the project
            Def("trashCan", "Assets/ThirdParty/UnityStore/Meshy/Trash/Meshy_AI_model.fbx", Kind.Furniture, fitH: 0.68f, mat: "Assets/ThirdParty/UnityStore/Meshy/Trash/Materials/Meshy_AI_Dirty_Trash_Can_0708233410_texture.mat", collider: true);
            Def("backpack", "Assets/ThirdParty/UnityStore/Meshy/Backpack/Meshy_AI_model.fbx", Kind.Clutter, fitMax: 0.42f, mat: D0 + "SOTDD_Meshy_Backpack.mat");
            Def("wetFloor", "Assets/ThirdParty/UnityStore/Meshy/wet floor sign/Meshy_AI_model.fbx", Kind.Furniture, fitH: 0.66f, mat: D0 + "SOTDD_Meshy_WetFloor.mat");
            Def("exitSign", "Assets/ThirdParty/UnityStore/Meshy/Exit sign/Meshy_AI_model.fbx", Kind.Wall, fitMax: 0.5f, mat: D0 + "SOTDD_Meshy_ExitSign.mat");
            Def("trophyCase", "Assets/ThirdParty/UnityStore/Meshy/Trophy case/Meshy_AI_model.fbx", Kind.Furniture, fitH: 1.9f, mat: D0 + "SOTDD_Meshy_TrophyCase.mat", collider: true);
            Def("papers", "Assets/Prefabs/Clusters/Papers.prefab", Kind.Clutter, fitMax: 0.9f);
            Def("notebooks", "Assets/Prefabs/Clusters/scattered notebooks.prefab", Kind.Clutter, fitMax: 1.5f);
            Def("paperCluster", "Assets/Prefabs/Clusters/Paper cluster.prefab", Kind.Clutter, fitMax: 2.0f);
            Def("crate", "Assets/POLYGON - FPS free/Prefabs/props/PlyBox_prefab.prefab", Kind.Furniture);
            Def("petrol", "Assets/POLYGON - FPS free/Prefabs/props/petrol_prefab.prefab", Kind.Clutter, mat: D0 + "SOTDD_FireRed.mat");
            // --- gym
            const string G = "Assets/ThirdParty/UnityStore/PolyRonin/Old School Gym Equipment/Prefabs/";
            Def("gymBench", G + "Bench.prefab", Kind.Furniture, collider: true);
            Def("benchPress", G + "BenchPress.prefab", Kind.Furniture, collider: true);
            Def("dumbbellRack", G + "DumbbellsRack.prefab", Kind.Furniture, collider: true);
            Def("squatRack", G + "SquatRack.prefab", Kind.Furniture, collider: true);
            Def("pullUp", G + "PullUp-Dips-Abs.prefab", Kind.Furniture, collider: true);
            Def("platesRack", G + "PlatesRack.prefab", Kind.Furniture, collider: true);
            Def("kettlebell", G + "Kettlebell2.prefab", Kind.Clutter);
            Def("dumbbell", G + "Dumbbell3.prefab", Kind.Clutter);
            // --- kitchen set (project prefabs)
            const string K = "Assets/Prefabs/SchoolOfTheDead/Kitchen/";
            Def("fridge", K + "Kitchen_Refrigerator.prefab", Kind.Furniture);
            Def("prepCounter", K + "Kitchen_PrepCounter.prefab", Kind.Furniture);
            Def("sinkUnit", K + "Kitchen_SinkUnit.prefab", Kind.Furniture);
            Def("rollingCart", K + "Kitchen_RollingCart.prefab", Kind.Furniture);
            Def("mopBucket", K + "Kitchen_MopBucket.prefab", Kind.Furniture);
            Def("wallShelf", K + "Kitchen_WallShelf.prefab", Kind.Wall);
            Def("urn", K + "Kitchen_Urn.prefab", Kind.Surface);
            Def("cupStack", K + "Kitchen_CupStack.prefab", Kind.Surface);
            Def("plateStack", K + "Kitchen_PlateStack.prefab", Kind.Surface);
            // --- exterior
            Def("bush1", "Assets/ThirdParty/UnityStore/YughuesFreeBushes2018/Prefabs/P_Bush01.prefab", Kind.Furniture, fitH: 1.6f);
            Def("bush2", "Assets/ThirdParty/UnityStore/YughuesFreeBushes2018/Prefabs/P_Bush02.prefab", Kind.Furniture, fitH: 1.4f);
            Def("bush5", "Assets/ThirdParty/UnityStore/YughuesFreeBushes2018/Prefabs/P_Bush05.prefab", Kind.Furniture, fitH: 1.2f);
            Def("deadTree", "Assets/ThirdParty/UnityStore/Dry_Trees/Prefab/Dry3333.prefab", Kind.Furniture, fitH: 9f);
            Def("fenceLong", "Assets/ThirdParty/UnityStore/Assets_FenceChained/prefabs/chainlink_group-1.prefab", Kind.Furniture, mat: D0 + "SOTDD_ChainlinkFence.mat");
            Def("fence3", "Assets/ThirdParty/UnityStore/Assets_FenceChained/prefabs/chainlink_single.prefab", Kind.Furniture, mat: D0 + "SOTDD_ChainlinkFence.mat", collider: true);
            Def("fenceWire", "Assets/ThirdParty/UnityStore/Assets_FenceChained/prefabs/chainlink_barbwire-2.prefab", Kind.Furniture, mat: D0 + "SOTDD_ChainlinkFence.mat", collider: true);
            Def("carRed", "Assets/ThirdParty/UnityStore/ARCADE - FREE Racing Car/Prefabs (With Colliders)/Free Racing Car Red Variant.prefab", Kind.Furniture);
            Def("carGray", "Assets/ThirdParty/UnityStore/ARCADE - FREE Racing Car/Prefabs (With Colliders)/Free Racing Car Gray Variant.prefab", Kind.Furniture);
            Def("carBlue", "Assets/ThirdParty/UnityStore/ARCADE - FREE Racing Car/Prefabs (With Colliders)/Free Racing Car Blue Variant.prefab", Kind.Furniture);
            // --- supplemental props modelled in Blender for this pass (front = +Z, origin conventions noted)
            foreach (string n in new[] { "VendingMachine", "MetalShelf", "MetalShelf_Stocked", "CardboardBox_M", "CardboardBox_L",
                         "NurseCot", "ParkBench", "Dumpster", "SawhorseBarrier", "Pallet", "OilDrum_Rust", "OilDrum_Blue",
                         "JerseyBarrier", "StreetLamp", "PicnicTable", "Mattress", "Bleachers", "CubbyShelf", "BikeRack",
                         "SandbagWall", "Easel", "UprightPiano", "TrafficCone", "TableLong", "ConferenceTable", "CheckoutCounter",
                         "ReceptionCounter", "Workbench", "Ladder", "WaitingBench", "BoilerTank", "GeneratorUnit" })
                Def(n, D + "SOTDD_" + n + ".prefab", Kind.Furniture);
            foreach (string n in new[] { "CardboardBox_S", "TrashBag_A", "TrashBag_B", "MusicStand", "Toolbox", "FallenCeilingTiles", "PlankPile", "BloodPool_A", "BloodPool_B" })
                Def(n, D + "SOTDD_" + n + ".prefab", Kind.Clutter);
            foreach (string n in new[] { "ElectricalPanel", "WaterFountain", "FireExtinguisher", "BulletinBoard", "Whiteboard",
                         "MedicineCabinet", "WallTV", "RoomSign", "PlankBarricade_Door", "PlankBarricade_Wide", "BasketballHoop",
                         "Broom", "ProjectorScreen", "BloodSmearWall" })
                Def(n, D + "SOTDD_" + n + ".prefab", Kind.Wall);
            foreach (string n in new[] { "CeilingLightPanel", "IndustrialLamp", "PipeRun_4m", "CeilingVent" })
                Def(n, D + "SOTDD_" + n + ".prefab", Kind.Ceiling);
            return lib;
        }
    }
    const string D0 = "Assets/SchoolOfTheDead_Detail/Materials/";

    public static PropDef Get(string key)
    {
        if (!Lib.TryGetValue(key, out var d)) throw new ArgumentException("Unknown prop key " + key);
        if (d.prefab == null) d.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(d.path);
        if (d.prefab == null) throw new ArgumentException("Missing prefab " + d.path);
        if (d.mat != null && d.material == null) d.material = AssetDatabase.LoadAssetAtPath<Material>(d.mat);
        return d;
    }

    /// <summary>URP materials for the Meshy models whose textures exist in the project but had no material.</summary>
    public static void EnsureMeshyMaterials()
    {
        const string MY = "Assets/ThirdParty/UnityStore/Meshy/";
        var defs = new (string name, string tex, string normal, string emission, float smooth)[]
        {
            ("SOTDD_Meshy_Backpack", MY + "Backpack/Meshy_AI_Weathered_Denim_Backp_0708233410_texture.png", MY + "Backpack/Meshy_AI_texture_0_normal.png", null, 0.15f),
            ("SOTDD_Meshy_ExitSign", MY + "Exit sign/Meshy_AI_Rusted_Industrial_Exi_0708233410_texture.png", MY + "Exit sign/Meshy_AI_texture_0_normal.png", MY + "Exit sign/Meshy_AI_texture_0_emission.png", 0.35f),
            ("SOTDD_Meshy_TrophyCase", MY + "Trophy case/Meshy_AI_Dusty_Trophy_Cabinet_0708233410_texture.png", MY + "Trophy case/Meshy_AI_texture_0_normal.png", null, 0.4f),
            ("SOTDD_Meshy_WetFloor", MY + "wet floor sign/Meshy_AI_Caution_Slippery_Flo_0708233410_texture.png", MY + "wet floor sign/Meshy_AI_texture_0_normal.png", null, 0.45f),
        };
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        foreach (var d in defs)
        {
            string p = D0 + d.name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { m = new Material(lit); AssetDatabase.CreateAsset(m, p); }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(d.tex));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", d.smooth);
            var nImp = AssetImporter.GetAtPath(d.normal) as TextureImporter;
            if (nImp != null && nImp.textureType != TextureImporterType.NormalMap) { nImp.textureType = TextureImporterType.NormalMap; nImp.SaveAndReimport(); }
            m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(d.normal));
            m.EnableKeyword("_NORMALMAP");
            if (d.emission != null)
            {
                m.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(d.emission));
                m.SetColor("_EmissionColor", new Color(2.5f, 0.4f, 0.3f));
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
        }
        AssetDatabase.SaveAssets();
    }

    // ------------------------------------------------------------------ rooms

    public enum Side { N = 0, E = 1, S = 2, W = 3 }

    public class Opening { public Side side; public float from, to; public float Mid => (from + to) * 0.5f; public float Width => to - from; }

    public class Room
    {
        public string id, display;
        public Transform node;
        public Rect rect;                 // interior, x = world X, y = world Z
        public float floorY, ceilY;
        public int level;                 // -1 basement, 0 ground, 1 second floor, 9 exterior
        public readonly List<Opening> openings = new();
        public readonly List<Bounds> keepouts = new();
        public readonly List<Bounds> doorKeepouts = new();
        public readonly List<Bounds> placed = new();
        public Transform group;
        public Random rng;
        public int placedCount, rejected;
        public readonly Dictionary<string, int> reasons = new();

        public float Len(Side s) => s == Side.N || s == Side.S ? rect.width : rect.height;
        public float Face(Side s) => s switch { Side.N => rect.yMax, Side.S => rect.yMin, Side.E => rect.xMax, _ => rect.xMin };
        public Vector3 Inward(Side s) => s switch { Side.N => Vector3.back, Side.S => Vector3.forward, Side.E => Vector3.left, _ => Vector3.right };
        public float AlongMin(Side s) => s == Side.N || s == Side.S ? rect.xMin : rect.yMin;
        public float AlongMax(Side s) => s == Side.N || s == Side.S ? rect.xMax : rect.yMax;
        /// <summary>World point on the floor, "inset" metres in from wall s, at coordinate "along" (X for N/S walls, Z for E/W).</summary>
        public Vector3 WallPoint(Side s, float along, float inset)
        {
            return s switch
            {
                Side.N => new Vector3(along, floorY, rect.yMax - inset),
                Side.S => new Vector3(along, floorY, rect.yMin + inset),
                Side.E => new Vector3(rect.xMax - inset, floorY, along),
                _ => new Vector3(rect.xMin + inset, floorY, along),
            };
        }
        /// <summary>Yaw (degrees) that makes a prop's front face away from wall s, into the room.</summary>
        public static float YawFacingInto(Side s) => s switch { Side.N => 180f, Side.S => 0f, Side.E => 270f, _ => 90f };
        public float OpeningWidth(Side s) => openings.Where(o => o.side == s).Sum(o => o.Width);
        public bool AlongIsOpen(Side s, float a0, float a1, float pad = 0.3f) =>
            openings.Any(o => o.side == s && a1 > o.from - pad && a0 < o.to + pad);
        public Vector3 Center => new Vector3(rect.center.x, floorY, rect.center.y);
        public float Area => rect.width * rect.height;
        public float R(float a, float b) => (float)(a + rng.NextDouble() * (b - a));
        public bool Chance(float p) => rng.NextDouble() < p;
        public T Pick<T>(IList<T> list) => list[rng.Next(list.Count)];
    }

    static readonly Dictionary<string, Room> rooms = new();
    public static IReadOnlyDictionary<string, Room> Rooms => rooms;

    static GameObject doorsRoot;
    static bool IsDoorish(Collider c)
    {
        if (c == null) return false;
        Transform t = c.transform;
        if (doorsRoot == null) doorsRoot = GameObject.Find("Generated_Doors");
        var doors = doorsRoot;
        if (doors != null && t.IsChildOf(doors.transform)) return true;
        return c.GetComponentInParent<Door>() != null;
    }

    /// <summary>Nearest raycast hit that is not one of this pass's own props.</summary>
    static bool FirstArchHit(Vector3 origin, Vector3 dir, float dist, out RaycastHit best)
    {
        best = default; float bd = float.MaxValue; bool any = false;
        foreach (var h in Physics.RaycastAll(origin, dir, dist, ~0, QueryTriggerInteraction.Ignore))
            if (!IsMine(h.collider) && h.distance < bd) { bd = h.distance; best = h; any = true; }
        return any;
    }

    static Transform mineRoot;
    static bool IsMine(Collider c)
    {
        if (mineRoot == null) { var g = GameObject.Find(RootName); if (g != null) mineRoot = g.transform; }
        return mineRoot != null && c.transform.IsChildOf(mineRoot);
    }

    /// <summary>Scans the map hierarchy and builds room descriptors (rect, heights, openings, keep-outs).</summary>
    public static string AnalyzeRooms()
    {
        rooms.Clear();
        // analyse the original architecture only: hide this pass's props while scanning
        var detail = GameObject.Find(RootName);
        if (detail != null) detail.SetActive(false);
        try { return AnalyzeRoomsInner(); }
        finally { if (detail != null) detail.SetActive(true); Physics.SyncTransforms(); }
    }

    static string AnalyzeRoomsInner()
    {
        Physics.SyncTransforms();
        var map = GameObject.Find(MapRootName).transform;
        var log = new System.Text.StringBuilder();
        foreach (Transform node in map)
        {
            Transform floor = node.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.EndsWith("_Floor") && t.GetComponent<Renderer>());
            if (floor == null) continue;
            string id = floor.name.Substring(0, floor.name.Length - "_Floor".Length);
            Bounds fb = floor.GetComponent<Renderer>().bounds;
            Transform ceil = node.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == id + "_Ceiling" && t.GetComponent<Renderer>());
            var r = new Room
            {
                id = id,
                display = node.name.StartsWith(id + "_") ? node.name.Substring(id.Length + 1) : node.name,
                node = node,
                floorY = fb.max.y,
                ceilY = ceil ? ceil.GetComponent<Renderer>().bounds.min.y : fb.max.y + 4f,
                rect = Rect.MinMaxRect(fb.min.x, fb.min.z, fb.max.x, fb.max.z),
                rng = new Random(StableHash(id)),
            };
            r.level = r.floorY < -1f ? -1 : (r.floorY > 3f ? 1 : 0);
            if (id.StartsWith("parking") || id.StartsWith("courtyard") || id.StartsWith("staff_entrance")) r.level = 9;
            FitWalls(r);
            FindOpenings(r);
            rooms[id] = r;
        }
        AddGlobalKeepouts();
        foreach (var r in rooms.Values.OrderBy(x => x.level).ThenBy(x => x.id))
            log.AppendLine($"{r.id} L{r.level} rect=({r.rect.xMin:F1},{r.rect.yMin:F1})-({r.rect.xMax:F1},{r.rect.yMax:F1}) floor={r.floorY:F2} ceil={r.ceilY:F2} " +
                           "open=[" + string.Join(" ", r.openings.Select(o => $"{o.side}:{o.from:F1}-{o.to:F1}")) + $"] keep={r.keepouts.Count}+{r.doorKeepouts.Count}");
        return log.ToString();
    }

    static int StableHash(string s) { unchecked { int h = 23; foreach (char c in s) h = h * 31 + c; return h; } }

    /// <summary>Tightens the floor rect to the actual wall faces using horizontal raycasts.</summary>
    static void FitWalls(Room r)
    {
        float y = r.floorY + 1.3f;
        Rect fr = r.rect;
        float NewFace(Side s)
        {
            var hits = new List<float>();
            bool ns = s == Side.N || s == Side.S;
            float a0 = ns ? fr.xMin : fr.yMin, a1 = ns ? fr.xMax : fr.yMax;
            for (int i = 1; i < 12; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / 12f);
                Vector3 dir = -r.Inward(s);
                float face = s switch { Side.N => fr.yMax, Side.S => fr.yMin, Side.E => fr.xMax, _ => fr.xMin };
                // start 1.2 m inside the floor edge, look outwards up to 1.6 m
                Vector3 start = ns ? new Vector3(a, y, face) : new Vector3(face, y, a);
                start -= dir * 1.2f;
                if (FirstArchHit(start, dir, 1.6f, out var hit) && !IsDoorish(hit.collider) && hit.collider.bounds.size.y > 2f)
                    hits.Add(ns ? hit.point.z : hit.point.x);
            }
            float fallback = s switch { Side.N => fr.yMax - 0.08f, Side.S => fr.yMin + 0.08f, Side.E => fr.xMax - 0.08f, _ => fr.xMin + 0.08f };
            if (hits.Count < 3) return fallback;
            hits.Sort();
            // the wall face is the innermost consistent value; use the median of the outer half
            float med = hits[hits.Count / 2];
            return med;
        }
        float n = NewFace(Side.N), sth = NewFace(Side.S), e = NewFace(Side.E), w = NewFace(Side.W);
        if (n - sth > 1f && e - w > 1f) r.rect = Rect.MinMaxRect(w, sth, e, n);
    }

    /// <summary>Samples each wall at two heights; contiguous runs with no wall (or a door leaf) are openings.</summary>
    static void FindOpenings(Room r)
    {
        foreach (Side s in Enum.GetValues(typeof(Side)))
        {
            bool ns = s == Side.N || s == Side.S;
            float a0 = r.AlongMin(s) + 0.1f, a1 = r.AlongMax(s) - 0.1f;
            Vector3 dir = -r.Inward(s);
            float runStart = float.NaN;
            const float step = 0.2f;
            for (float a = a0; a <= a1 + 0.001f; a += step)
            {
                bool open = true;
                foreach (float h in new[] { 0.5f, 1.7f })
                {
                    Vector3 p = r.WallPoint(s, a, 0.6f) + Vector3.up * h;
                    if (FirstArchHit(p, dir, 1.3f, out var hit) && !IsDoorish(hit.collider))
                    { open = false; break; }
                }
                if (open && float.IsNaN(runStart)) runStart = a;
                bool last = a + step > a1 + 0.001f;
                if ((!open || last) && !float.IsNaN(runStart))
                {
                    float end = open ? a : a - step;
                    if (end - runStart >= 0.5f) r.openings.Add(new Opening { side = s, from = runStart - step * 0.5f, to = end + step * 0.5f });
                    runStart = float.NaN;
                }
            }
        }
        // doorway clearance: 2.0 m deep, 0.5 m wider than the opening on each side, full player height
        foreach (var o in r.openings)
        {
            Vector3 a = r.WallPoint(o.side, o.from - 0.5f, 0f), b = r.WallPoint(o.side, o.to + 0.5f, 2.0f);
            var bb = new Bounds((a + b) * 0.5f, Vector3.zero);
            bb.Encapsulate(a); bb.Encapsulate(b);
            bb.min = new Vector3(bb.min.x, r.floorY, bb.min.z); bb.max = new Vector3(bb.max.x, r.floorY + 2.4f, bb.max.z);
            r.doorKeepouts.Add(bb);
        }
    }

    /// <summary>Clearance around gameplay objects: interactables, perks, teleporters, books, power-ups and zombie spawns.</summary>
    static void AddGlobalKeepouts()
    {
        var pts = new List<(Vector3 p, float rad)>();
        foreach (var c in Object.FindObjectsByType<InteractableBase>(FindObjectsSortMode.None)) pts.Add((c.transform.position, 1.8f));
        foreach (var c in Object.FindObjectsByType<PerkMachine>(FindObjectsSortMode.None)) pts.Add((c.transform.position, 2.0f));
        foreach (var c in Object.FindObjectsByType<Door>(FindObjectsSortMode.None)) pts.Add((c.transform.position, 1.6f));
        foreach (var c in Object.FindObjectsByType<Teleporter>(FindObjectsSortMode.None)) pts.Add((c.transform.position, 2.5f));
        foreach (var c in Object.FindObjectsByType<SecretBookcase>(FindObjectsSortMode.None)) pts.Add((c.transform.position, 2.0f));
        foreach (var c in Object.FindObjectsByType<BookPickup>(FindObjectsSortMode.None)) pts.Add((c.transform.position, 1.0f));
        foreach (var c in Object.FindObjectsByType<Powerup>(FindObjectsSortMode.None)) pts.Add((c.transform.position, 1.0f));
        foreach (var sp in Object.FindObjectsByType<ZombieSpawner>(FindObjectsSortMode.None))
            if (sp.spawnPoints != null) foreach (var t in sp.spawnPoints) if (t) pts.Add((t.position, 2.2f));
        foreach (var r in rooms.Values)
            foreach (var (p, rad) in pts)
                if (p.x > r.rect.xMin - rad && p.x < r.rect.xMax + rad && p.z > r.rect.yMin - rad && p.z < r.rect.yMax + rad && p.y > r.floorY - 1f && p.y < r.ceilY + 0.5f)
                    r.keepouts.Add(new Bounds(new Vector3(p.x, r.floorY + 1.3f, p.z), new Vector3(rad * 2, 2.6f, rad * 2)));
    }

    // ------------------------------------------------------------------ placement

    public static Transform GetRoot()
    {
        var root = GameObject.Find(RootName);
        if (root == null) root = new GameObject(RootName);
        return root.transform;
    }

    static string LevelGroup(int level) => level switch { -1 => "Basement", 0 => "Floor1_Main", 1 => "Floor2_Upper", _ => "Exterior" };

    public static Transform RoomGroup(Room r, bool clear)
    {
        Transform root = GetRoot();
        string lg = LevelGroup(r.level);
        Transform level = root.Find(lg) ?? new GameObject(lg).transform;
        level.SetParent(root, false);
        string gname = r.id + " (" + r.display + ")";
        Transform g = level.Find(gname);
        if (g != null && clear) { Object.DestroyImmediate(g.gameObject); g = null; }
        if (g == null) { g = new GameObject(gname).transform; g.SetParent(level, false); }
        r.group = g;
        r.placed.Clear();
        r.rng = new Random(StableHash(r.id));
        r.placedCount = r.rejected = 0;
        r.reasons.Clear();
        // keep previously placed props in this group as obstacles
        foreach (var rend in g.GetComponentsInChildren<Renderer>()) r.placed.Add(rend.bounds);
        return g;
    }

    public class Opt
    {
        public float? y;                  // absolute bottom height (default: floor)
        public float? centerY;            // absolute centre height (wall decor)
        public Side? wall;                // flush the prop's back against this wall
        public float wallGap = 0.015f;
        public Vector3 tilt;              // extra rotation (knocked-over furniture)
        public float scale = 1f;
        public bool ignorePlaced;         // allow overlapping other placed props (clutter on furniture)
        public bool ignoreKeepouts;
        public bool ignorePhysics;
        public bool ceiling;              // top flush with ceiling
        public string name;
        public Material mat;
    }

    static readonly Opt Default = new Opt();
    static Material bookMat;
    // seating is pushed aside by zombies rather than routed around: keeps aisles and desk grids from trapping the NavMesh
    static readonly HashSet<string> NavIgnoreKeys = new() { "studentDesk", "chair", "chairWood", "swivelChair", "MusicStand", "Easel", "trashCan", "wetFloor", "TrafficCone", "gymBench" };

    /// <summary>
    /// Instantiates prop "key" with its footprint centred at (pos.x, pos.z), validates it and returns it,
    /// or returns null (and destroys it) if it would clip, float, block a doorway or leave the room.
    /// </summary>
    public static GameObject Place(Room r, string key, Vector3 pos, float yaw, Opt o = null)
    {
        o ??= Default;
        PropDef d = Get(key);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(d.prefab, r.group);
        Transform t = go.transform;
        Quaternion prefabRot = d.prefab.transform.rotation;
        Vector3 prefabScale = d.prefab.transform.localScale;
        t.rotation = Quaternion.Euler(d.baseEuler) * prefabRot;
        t.localScale = prefabScale;
        if (d.fitHeight > 0 || d.fitMax > 0)
        {
            Bounds b0 = WorldBounds(go);
            float k = d.fitHeight > 0 ? d.fitHeight / Mathf.Max(0.001f, b0.size.y) : d.fitMax / Mathf.Max(0.001f, Mathf.Max(b0.size.x, b0.size.z));
            t.localScale = prefabScale * k;
        }
        t.localScale *= o.scale;
        t.rotation = Quaternion.Euler(0, yaw + d.facing, 0) * Quaternion.Euler(o.tilt) * Quaternion.Euler(d.baseEuler) * prefabRot;
        t.position = pos;
        if (d.material != null || o.mat != null) ApplyMaterial(go, o.mat ? o.mat : d.material);
        if (key == "shelfBooks")
        {
            bookMat ??= AssetDatabase.LoadAssetAtPath<Material>("Assets/Textures/SchoolOfTheDead/Materials/Generated Image June 24, 2026 - 4_26PM.mat");
            if (bookMat != null)
                foreach (var rend in go.GetComponentsInChildren<Renderer>(true))
                    if (rend.name.StartsWith("book")) rend.sharedMaterial = bookMat;
        }

        Bounds b = WorldBounds(go);
        Vector3 shift = new Vector3(pos.x - b.center.x, 0, pos.z - b.center.z);
        if (o.wall.HasValue)
        {
            Side s = o.wall.Value;
            float face = r.Face(s);
            switch (s)
            {
                case Side.N: shift.z = face - o.wallGap - b.max.z; break;
                case Side.S: shift.z = face + o.wallGap - b.min.z; break;
                case Side.E: shift.x = face - o.wallGap - b.max.x; break;
                case Side.W: shift.x = face + o.wallGap - b.min.x; break;
            }
        }
        if (o.ceiling) shift.y = r.ceilY - 0.005f - b.max.y;
        else if (o.centerY.HasValue) shift.y = o.centerY.Value - b.center.y;
        else
        {
            float baseY = o.y ?? GroundAt(r, new Vector3(pos.x, 0, pos.z));
            shift.y = baseY + 0.002f - b.min.y;
        }
        t.position += shift;
        b.center += shift;

        string why = Validate(r, go, b, d, o);
        if (why != null)
        {
            Object.DestroyImmediate(go);
            r.rejected++;
            string rk = key + ":" + (why.StartsWith("hits ") ? "hits" : why);
            r.reasons[rk] = r.reasons.TryGetValue(rk, out int rc) ? rc + 1 : 1;
            return null;
        }
        if (d.addCollider && go.GetComponentInChildren<Collider>() == null)
        {
            var bc = go.AddComponent<BoxCollider>();
            bc.center = t.InverseTransformPoint(b.center);
            Vector3 ls = t.InverseTransformVector(b.size);
            bc.size = new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z)) * 0.95f;
        }
        if (d.kind == Kind.Clutter || d.kind == Kind.Surface || d.kind == Kind.Ceiling)
            foreach (var rend in go.GetComponentsInChildren<Renderer>()) rend.shadowCastingMode = d.kind == Kind.Ceiling ? ShadowCastingMode.Off : rend.shadowCastingMode;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
        // only solid floor furniture shapes zombie navigation; clutter, wall dressing and fixtures are ignored by the bake
        if (d.kind != Kind.Furniture || NavIgnoreKeys.Contains(key) || o.tilt != Vector3.zero && b.size.y < 0.5f)
            go.AddComponent<Unity.AI.Navigation.NavMeshModifier>().ignoreFromBuild = true;
        go.name = o.name ?? key;
        r.placed.Add(b);
        r.placedCount++;
        Physics.SyncTransforms();
        return go;
    }

    public static string Reasons(string id) => rooms.TryGetValue(id, out var r)
        ? string.Join(", ", r.reasons.OrderByDescending(kv => kv.Value).Take(12).Select(kv => kv.Key + "=" + kv.Value)) : "?";

    public static Bounds WorldBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true).Where(x => !(x is ParticleSystemRenderer)).ToArray();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        foreach (var x in rs) b.Encapsulate(x.bounds);
        return b;
    }

    static void ApplyMaterial(GameObject go, Material m)
    {
        foreach (var rend in go.GetComponentsInChildren<Renderer>(true))
        {
            if (rend is ParticleSystemRenderer) continue;
            var mf = rend.GetComponent<MeshFilter>();
            int n = mf && mf.sharedMesh ? Mathf.Max(1, mf.sharedMesh.subMeshCount) : 1;
            var arr = new Material[n];
            for (int i = 0; i < n; i++) arr[i] = m;
            rend.sharedMaterials = arr;   // one slot per sub-mesh: avoids re-drawing the same mesh 3x
        }
    }

    /// <summary>Floor height under a point (raycast against non-placed colliders), defaulting to the room floor.</summary>
    public static float GroundAt(Room r, Vector3 p)
    {
        var start = new Vector3(p.x, r.floorY + 0.6f, p.z);
        var hits = Physics.RaycastAll(start, Vector3.down, 2.0f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (var h in hits)
            if (!IsMine(h.collider) && h.point.y < r.floorY + 0.3f && h.point.y > best) best = h.point.y;
        return float.IsNegativeInfinity(best) ? r.floorY : best;
    }

    /// <summary>Top surface of whatever is under a point (used for items on desks / shelves).</summary>
    public static float SurfaceAt(Room r, Vector3 p, float maxY)
    {
        var start = new Vector3(p.x, maxY, p.z);
        if (Physics.Raycast(start, Vector3.down, out var h, maxY - r.floorY + 0.1f, ~0, QueryTriggerInteraction.Ignore)) return h.point.y;
        return r.floorY;
    }

    static string Validate(Room r, GameObject go, Bounds b, PropDef d, Opt o)
    {
        const float tol = 0.06f;
        bool exterior = r.level == 9;
        if (!exterior && !o.ceiling && d.kind != Kind.Wall)
        {
            if (b.min.x < r.rect.xMin - tol || b.max.x > r.rect.xMax + tol || b.min.z < r.rect.yMin - tol || b.max.z > r.rect.yMax + tol)
                return "outside room";
        }
        if (!exterior && b.max.y > r.ceilY + 0.02f) return "above ceiling";
        if (d.kind == Kind.Wall && o.wall.HasValue)
        {
            Side s = o.wall.Value;
            bool ns = s == Side.N || s == Side.S;
            float a0 = ns ? b.min.x : b.min.z, a1 = ns ? b.max.x : b.max.z;
            if (a0 < r.AlongMin(s) - tol || a1 > r.AlongMax(s) + tol) return "off wall";
            if (r.AlongIsOpen(s, a0, a1, 0.25f)) return "over opening";
        }
        if (!o.ignoreKeepouts && !o.ceiling && b.min.y < r.floorY + 2.3f)
        {
            foreach (var k in r.keepouts)
                if (Overlap2D(k, b, 0f)) return "keepout";
            // flush wall dressing never blocks a doorway, so only floor-standing props respect door clearance
            if (d.kind != Kind.Wall)
                foreach (var k in r.doorKeepouts)
                    if (Overlap2D(k, b, 0f)) return "door keepout";
        }
        if (!o.ignorePlaced)
            foreach (var p in r.placed)
                if (Overlap3D(p, b, 0.03f)) return "overlaps placed";
        if (!o.ignorePhysics)
        {
            var own = new HashSet<Collider>(go.GetComponentsInChildren<Collider>(true));
            Vector3 half = b.extents - new Vector3(0.04f, 0.04f, 0.04f);
            half = Vector3.Max(half, new Vector3(0.01f, 0.01f, 0.01f));
            foreach (var c in Physics.OverlapBox(b.center, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            {
                if (own.Contains(c)) continue;
                Bounds cb = c.bounds;
                if (cb.max.y <= b.min.y + 0.06f) continue;   // floor / surface we stand on
                if (cb.min.y >= b.max.y - 0.06f) continue;   // ceiling
                if (o.ignorePlaced && IsMine(c)) continue;
                return "hits " + c.name;
            }
        }
        return null;
    }

    /// <summary>Debug: lines props up at y=500 with yaw 0 (front should face +Z) for visual calibration.</summary>
    public static string CalibrationLineup(string keys)
    {
        var old = GameObject.Find("__Calibration"); if (old) Object.DestroyImmediate(old);
        var holder = new GameObject("__Calibration").transform;
        var r = new Room { id = "__cal", display = "cal", floorY = 500, ceilY = 520, level = 9, rect = new Rect(-100, -100, 400, 200), rng = new Random(1), group = holder };
        float x = 0; var sb = new System.Text.StringBuilder();
        foreach (string k in keys.Split(','))
        {
            var go = Place(r, k.Trim(), new Vector3(x, 500, 0), 0, new Opt { ignorePhysics = true, ignorePlaced = true, ignoreKeepouts = true });
            if (go == null) { sb.Append(k + "=FAIL "); continue; }
            Bounds b = WorldBounds(go);
            go.transform.position += new Vector3(x - b.min.x, 0, 0);
            var mark = GameObject.CreatePrimitive(PrimitiveType.Cube); mark.transform.SetParent(holder);
            mark.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f); mark.transform.position = new Vector3(x + b.size.x / 2, 500.05f, b.max.z + 0.25f);
            sb.Append($"{k}@{x:F1}({b.size.x:F2}x{b.size.y:F2}x{b.size.z:F2}) ");
            x += b.size.x + 0.6f;
        }
        var lt = new GameObject("L").AddComponent<Light>(); lt.transform.SetParent(holder); lt.type = LightType.Directional; lt.intensity = 1.6f; lt.transform.rotation = Quaternion.Euler(35, 200, 0);
        return sb.ToString();
    }

    static bool Overlap2D(Bounds a, Bounds b, float shrink) =>
        a.min.x < b.max.x - shrink && a.max.x > b.min.x + shrink && a.min.z < b.max.z - shrink && a.max.z > b.min.z + shrink &&
        a.min.y < b.max.y && a.max.y > b.min.y;

    static bool Overlap3D(Bounds a, Bounds b, float shrink) =>
        a.min.x < b.max.x - shrink && a.max.x > b.min.x + shrink && a.min.z < b.max.z - shrink && a.max.z > b.min.z + shrink &&
        a.min.y < b.max.y - shrink && a.max.y > b.min.y + shrink;
}
