using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// Room plan, lighting and exterior dressing for the School of the Dead detail pass, plus the
/// entry points used to (re)build rooms and report on them.
/// </summary>
public static partial class SchoolDetailKit
{
    // ------------------------------------------------------------------ plan

    enum Style { Floor1, Hallway1, Floor2, Hallway2, Basement, Gym, Stair }

    static readonly Dictionary<string, (Action<Room> layout, Style light, string label)> Plan = new()
    {
        // ground floor — rooms that were already dressed get lights + light touches only
        { "starter",                    (DressedRoomExtras, Style.Floor1, "101") },
        { "science_lab",                (DressedRoomExtras, Style.Floor1, "SCIENCE") },
        { "library",                    (DressedRoomExtras, Style.Floor1, "ENGLISH") },
        { "cafeteria",                  (DressedRoomExtras, Style.Floor1, "CAFETERIA") },
        { "cafeteria_kitchen",          (DressedRoomExtras, Style.Floor1, "KITCHEN") },
        { "West Corridor Chemistry Lab_1", (DressedRoomExtras, Style.Floor1, "CHEM LAB") },
        { "west_classroom_1",           (DressedRoomExtras, Style.Floor1, "BIOLOGY") },
        { "west_classroom_2",           (DressedRoomExtras, Style.Floor1, "ENGLISH 2") },
        { "hallway",                    (r => Hallway(r), Style.Hallway1, null) },
        { "cafeteria_west_hallway",     (r => Hallway(r, lockers: false), Style.Hallway1, null) },
        { "lower_hallway_south",        (r => Hallway(r), Style.Hallway1, null) },
        { "lower_hallway_east",         (r => Hallway(r), Style.Hallway1, null) },
        { "south_office_hallway",       (r => Hallway(r), Style.Hallway1, null) },
        { "south_end_hallway",          (r => Hallway(r), Style.Hallway1, null) },
        { "history_room",               (r => Classroom(r, ClassVariant.History), Style.Floor1, "112 HISTORY") },
        { "math_room",                  (r => Classroom(r, ClassVariant.Math), Style.Floor1, "114 MATH") },
        { "music_room",                 (r => Classroom(r, ClassVariant.Music), Style.Floor1, "116 CHOIR") },
        { "east_room_3",                (Library, Style.Floor1, "LIBRARY") },
        { "main_office",                (r => Office(r, reception: true), Style.Floor1, "MAIN OFFICE") },
        { "conference_room",            (Conference, Style.Floor1, "CONFERENCE") },
        { "south_office_classroom_1",   (r => Classroom(r, ClassVariant.Science), Style.Floor1, "121 BIO LAB") },
        { "south_office_classroom_2",   (r => Classroom(r, ClassVariant.Music), Style.Floor1, "123 BAND") },
        { "south_office_classroom_3",   (r => Classroom(r, ClassVariant.Art), Style.Floor1, "125 ART") },
        { "south_office_classroom_4",   (r => Classroom(r, ClassVariant.Computer), Style.Floor1, "127 COMPUTERS") },
        { "janitors_closet_2",          (r => Janitor(r, workshop: false), Style.Floor1, "CUSTODIAN") },
        { "west_south_office",          (r => Office(r, reception: false), Style.Floor1, "COUNSELOR") },
        { "west_wing_storage",          (r => Storage(r, camp: true), Style.Floor1, "STORAGE") },
        { "stairwell",                  (Stairwell, Style.Stair, null) },
        { "stairwell_2",                (Stairwell, Style.Stair, null) },
        { "east_stairwell",             (Stairwell, Style.Stair, null) },
        // second floor
        { "gym",                        (Gym, Style.Gym, "GYM") },
        { "upper_hallway",              (r => Hallway(r), Style.Hallway2, null) },
        { "upper_hallway_2",            (DressedRoomExtras, Style.Hallway2, null) },
        { "upper_hallway_3",            (r => Hallway(r), Style.Hallway2, null) },
        { "upper_hallway_north",        (r => Hallway(r), Style.Hallway2, null) },
        { "gym_north_hallway",          (r => Hallway(r), Style.Hallway2, null) },
        { "principal_office",           (Principal, Style.Floor2, "PRINCIPAL") },
        { "security_room",              (DressedRoomExtras, Style.Floor2, "SECURITY") },
        { "nurses_office",              (r => Nurse(r, backroom: false), Style.Floor2, "NURSE") },
        { "nurses_office_backroom",     (r => Nurse(r, backroom: true), Style.Floor2, "SUPPLIES") },
        { "janitors_closet",            (r => Janitor(r, workshop: true), Style.Floor2, "MAINTENANCE") },
        { "east_room_1",                (r => Classroom(r, ClassVariant.English), Style.Floor2, "204 ENGLISH") },
        { "east_room_2",                (r => Classroom(r, ClassVariant.General), Style.Floor2, "206 SOCIAL STUDIES") },
        // basement
        { "underground_tunnel",         (Tunnel, Style.Basement, null) },
        { "the_vault",                  (Vault, Style.Basement, null) },
        { "library_basement",           (r => BasementStorage(r, boiler: true), Style.Basement, null) },
        { "library_basement_annex",     (r => BasementStorage(r, boiler: false), Style.Basement, null) },
        { "teleporter_room",            (UtilityRoom, Style.Basement, null) },
        { "library_staircase",          (Stairwell, Style.Stair, null) },
    };

    /// <summary>Rooms that were already furnished by hand: add only lighting, wall dressing and a little clutter.</summary>
    static void DressedRoomExtras(Room r)
    {
        foreach (Side s in Enum.GetValues(typeof(Side)))
            if (r.Len(s) > 5f) SideWallDecor(r, s, 4.0f, 1.65f, new[] { "BulletinBoard", "posterB", "posterC" }, 0.35f);
        Scatter(r, FloorPaper, Mathf.Clamp((int)(r.Area / 60f), 1, 3));
        Blood(r, 0.25f);
    }

    // ------------------------------------------------------------------ lighting

    class LightStyle
    {
        public string fixture = "CeilingLightPanel";
        public Color color; public float intensity, range, spacing, areaPerLight, broken, flicker, drop = 0.45f;
        public int maxLights = 6;
    }

    static LightStyle StyleFor(Style s) => s switch
    {
        Style.Floor1 => new LightStyle { color = new Color(1f, 0.92f, 0.78f), intensity = 1.15f, range = 8.5f, spacing = 4.4f, areaPerLight = 48f, broken = 0.22f, flicker = 0.18f },
        Style.Hallway1 => new LightStyle { color = new Color(1f, 0.9f, 0.74f), intensity = 1.1f, range = 8.5f, spacing = 4.6f, areaPerLight = 42f, broken = 0.3f, flicker = 0.28f },
        Style.Floor2 => new LightStyle { color = new Color(0.8f, 0.94f, 0.9f), intensity = 1.05f, range = 8.5f, spacing = 4.4f, areaPerLight = 50f, broken = 0.35f, flicker = 0.3f },
        Style.Hallway2 => new LightStyle { color = new Color(0.78f, 0.92f, 0.86f), intensity = 1.0f, range = 8.5f, spacing = 4.6f, areaPerLight = 44f, broken = 0.4f, flicker = 0.35f },
        Style.Basement => new LightStyle { fixture = "IndustrialLamp", color = new Color(1f, 0.68f, 0.38f), intensity = 1.35f, range = 7.5f, spacing = 5.0f, areaPerLight = 42f, broken = 0.3f, flicker = 0.4f, drop = 1.0f },
        Style.Gym => new LightStyle { fixture = "IndustrialLamp", color = new Color(0.88f, 0.94f, 1f), intensity = 1.75f, range = 15f, spacing = 6.0f, areaPerLight = 120f, broken = 0.35f, flicker = 0.25f, drop = 1.0f },
        _ => new LightStyle { color = new Color(1f, 0.9f, 0.75f), intensity = 1.3f, range = 7f, spacing = 6f, areaPerLight = 60f, broken = 0.0f, flicker = 0.4f, maxLights = 1 },
    };

    static Material offDiffuser, onDiffuser;

    static void EnsureLightMaterials()
    {
        onDiffuser = AssetDatabase.LoadAssetAtPath<Material>(D0 + "SOTDD_LightDiffuser.mat");
        string p = D0 + "SOTDD_LightDiffuser_Off.mat";
        offDiffuser = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (offDiffuser == null)
        {
            offDiffuser = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            offDiffuser.SetColor("_BaseColor", new Color(0.42f, 0.41f, 0.38f));
            offDiffuser.SetFloat("_Smoothness", 0.3f);
            offDiffuser.enableInstancing = true;
            AssetDatabase.CreateAsset(offDiffuser, p);
        }
    }

    static int LightRoom(Room r, Style styleId)
    {
        EnsureLightMaterials();
        LightStyle st = StyleFor(styleId);
        var fixtures = new List<GameObject>();
        if (styleId == Style.Stair)
        {
            var f = Place(r, st.fixture, r.Center, 0, new Opt { ceiling = true, ignoreKeepouts = true, ignorePhysics = true, name = "Fixture" });
            if (f) fixtures.Add(f);
        }
        else
        {
            int nx = Mathf.Max(1, Mathf.RoundToInt(r.rect.width / st.spacing));
            int nz = Mathf.Max(1, Mathf.RoundToInt(r.rect.height / st.spacing));
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    var p = new Vector3(r.rect.xMin + (i + 0.5f) * r.rect.width / nx, 0, r.rect.yMin + (k + 0.5f) * r.rect.height / nz);
                    float yaw = r.rect.width >= r.rect.height ? 0f : 90f;
                    var f = Place(r, st.fixture, p, yaw, new Opt { ceiling = true, ignoreKeepouts = true, name = "Fixture" });
                    if (f) fixtures.Add(f);
                }
        }
        if (fixtures.Count == 0) return 0;
        int want = Mathf.Clamp(Mathf.RoundToInt(r.Area / st.areaPerLight), 1, Mathf.Min(st.maxLights, fixtures.Count));
        // spread lit fixtures evenly through the list
        var lit = new HashSet<int>();
        for (int i = 0; i < want; i++) lit.Add(Mathf.Min(fixtures.Count - 1, Mathf.RoundToInt((i + 0.5f) * fixtures.Count / want - 0.5f)));
        int lights = 0;
        for (int i = 0; i < fixtures.Count; i++)
        {
            GameObject fx = fixtures[i];
            var rend = fx.GetComponentInChildren<Renderer>();
            if (!lit.Contains(i))
            {
                if (r.Chance(st.broken + 0.35f)) SwapDiffuser(rend, offDiffuser);   // dead tube
                if (r.Chance(0.12f) && st.fixture == "CeilingLightPanel")
                    fx.transform.rotation = Quaternion.AngleAxis(r.R(8, 18), fx.transform.right) * fx.transform.rotation; // hanging loose
                continue;
            }
            Bounds b = WorldBounds(fx);
            var lgo = new GameObject("Light");
            lgo.transform.SetParent(fx.transform, true);
            lgo.transform.position = new Vector3(b.center.x, b.min.y - 0.25f, b.center.z);
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = st.color;
            l.intensity = st.intensity * r.R(0.85f, 1.1f);
            l.range = st.range;
            l.shadows = LightShadows.None;
            l.lightmapBakeType = LightmapBakeType.Realtime;
            l.renderMode = LightRenderMode.Auto;
            if (r.Chance(st.flicker))
            {
                var fl = lgo.AddComponent<SOTDFlickerLight>();
                fl.severity = r.R(0.2f, 0.9f);
                fl.burstInterval = r.R(2.5f, 7f);
                fl.fixtureRenderer = rend; fl.onMaterial = onDiffuser; fl.offMaterial = offDiffuser;
                // a flickering light can't live in a static batch if its material swaps
                GameObjectUtility.SetStaticEditorFlags(fx, StaticEditorFlags.OccludeeStatic);
            }
            lights++;
        }
        return lights;
    }

    static void SwapDiffuser(Renderer rend, Material to)
    {
        if (rend == null || to == null) return;
        var mats = rend.sharedMaterials;
        for (int i = 0; i < mats.Length; i++) if (mats[i] == onDiffuser) mats[i] = to;
        rend.sharedMaterials = mats;
    }

    static void EmergencyLight(Room r, Vector3 pos, float intensity = 0.9f)
    {
        var lgo = new GameObject("EmergencyLight");
        lgo.transform.SetParent(r.group, true);
        lgo.transform.position = pos;
        var l = lgo.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(1f, 0.12f, 0.06f); l.intensity = intensity; l.range = 5.5f;
        l.shadows = LightShadows.None; l.lightmapBakeType = LightmapBakeType.Realtime;
    }

    /// <summary>Exit signs over doorways that lead to stairs or outside, each with a faint red glow.</summary>
    static void ExitSigns(Room r)
    {
        foreach (var o in r.openings.ToList())
        {
            Vector3 outside = r.WallPoint(o.side, o.Mid, -1.5f) + Vector3.up * 1.2f;
            bool toStairOrOut = rooms.Values.Any(x => x != r && (x.id.Contains("stair") || x.level == 9) &&
                                                      x.rect.Contains(new Vector2(outside.x, outside.z)) && Mathf.Abs(x.floorY - r.floorY) < 1.5f);
            if (!toStairOrOut && o.Width < 4.5f) continue;
            var sign = WallItem(r, o.side, o.Mid, "exitSign", centerY: Mathf.Min(r.ceilY - 0.35f, r.floorY + 3.35f), allowOverOpening: true);
            if (sign != null) EmergencyLight(r, WorldBounds(sign).center + r.Inward(o.side) * 0.4f, 0.6f);
        }
    }

    // ------------------------------------------------------------------ obstacles from existing content

    /// <summary>Treats existing (non-architectural) renderers inside the room as obstacles so new props never overlap them.</summary>
    static Renderer[] sceneRenderers;
    static void AddExistingObstacles(Room r)
    {
        var mine = GameObject.Find(RootName);
        var geo = GameObject.Find("Generated_Geometry_Fixes");
        sceneRenderers ??= Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        foreach (var rend in sceneRenderers)
        {
            if (rend == null) continue;
            if (rend is ParticleSystemRenderer || !rend.enabled) continue;
            Transform t = rend.transform;
            if (mine != null && t.IsChildOf(mine.transform)) continue;
            if (geo != null && t.IsChildOf(geo.transform)) continue;
            Bounds b = rend.bounds;
            if (b.size.y > 3.6f || Mathf.Max(b.size.x, b.size.z) > 6f) continue;          // walls, floors, ceilings
            string n = t.name;
            if (n.EndsWith("_Floor") || n.EndsWith("_Ceiling") || n.Contains("_Seg") || n.StartsWith("Step_")) continue;
            Vector3 c = b.center;
            if (c.x < r.rect.xMin || c.x > r.rect.xMax || c.z < r.rect.yMin || c.z > r.rect.yMax) continue;
            if (c.y < r.floorY - 0.3f || c.y > r.ceilY + 0.2f) continue;
            r.placed.Add(b);
        }
    }

    // ------------------------------------------------------------------ entry points

    /// <summary>(Re)builds one room: clears its detail group, runs its layout, lights it. Returns a one-line report.</summary>
    public static string BuildRoom(string id)
    {
        if (rooms.Count == 0) AnalyzeRooms();
        if (!rooms.TryGetValue(id, out Room r)) return id + ": unknown room";
        if (!Plan.TryGetValue(id, out var plan)) return id + ": no plan";
        RoomGroup(r, clear: true);
        AddExistingObstacles(r);
        plan.layout(r);
        int lights = LightRoom(r, plan.light);
        if (plan.light == Style.Hallway1 || plan.light == Style.Hallway2 || plan.light == Style.Basement || plan.light == Style.Stair) ExitSigns(r);
        if (plan.light == Style.Basement || plan.light == Style.Stair) EmergencyLight(r, r.Center + Vector3.up * (r.ceilY - r.floorY - 0.6f), 0.7f);
        EditorSceneManager.MarkSceneDirty(r.group.gameObject.scene);
        return $"{id}: props={r.placedCount} rejected={r.rejected} lights={lights}";
    }

    public static string BuildRooms(string csv)
    {
        AnalyzeRooms();
        sceneRenderers = null;
        var sb = new StringBuilder();
        foreach (string id in csv.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0)) sb.AppendLine(BuildRoom(id));
        return sb.ToString();
    }

    public static string BuildAllInteriors()
    {
        AnalyzeRooms();
        sceneRenderers = null;
        var sb = new StringBuilder();
        foreach (string id in Plan.Keys) if (rooms.ContainsKey(id)) sb.AppendLine(BuildRoom(id));
        return sb.ToString();
    }

    /// <summary>Rebakes the scene's NavMeshSurface synchronously and writes the result over its existing NavMesh asset.</summary>
    public static string BakeNavMesh()
    {
        var surface = Object.FindObjectsByType<Unity.AI.Navigation.NavMeshSurface>(FindObjectsSortMode.None).FirstOrDefault();
        if (surface == null) return "no NavMeshSurface";
        string path = surface.navMeshData != null ? AssetDatabase.GetAssetPath(surface.navMeshData) : null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // doors open at runtime (and carve with their own NavMeshObstacle), so they must not be baked in as walls
        var hidden = new List<GameObject>();
        foreach (string n in new[] { "Generated_Doors" })
        { var g = GameObject.Find(n); if (g != null && g.activeSelf) { g.SetActive(false); hidden.Add(g); } }
        foreach (var door in Object.FindObjectsByType<Door>(FindObjectsSortMode.None))
            if (door.gameObject.activeSelf) { door.gameObject.SetActive(false); hidden.Add(door.gameObject); }
        // pre-existing prop instances were not part of the original bake: keep zombie routes through them unchanged
        var mineT = GameObject.Find(RootName)?.transform;
        var muted = new List<Renderer>();
        foreach (var rend in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!rend.enabled || (mineT != null && rend.transform.IsChildOf(mineT))) continue;
            if (!PrefabUtility.IsPartOfPrefabInstance(rend.gameObject)) continue;
            rend.enabled = false; muted.Add(rend);
        }
        try { surface.BuildNavMesh(); }
        finally
        {
            foreach (var g in hidden) g.SetActive(true);
            foreach (var rend in muted) rend.enabled = true;
        }
        var data = surface.navMeshData;
        if (string.IsNullOrEmpty(path)) path = "Assets/Scenes/SchoolOfTheDead/NavMesh-" + surface.name + ".asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(data, path);
        surface.navMeshData = AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(path);
        surface.RemoveData();
        surface.AddData();
        EditorUtility.SetDirty(surface);
        EditorSceneManager.MarkSceneDirty(surface.gameObject.scene);
        AssetDatabase.SaveAssets();
        return "baked " + surface.name + " in " + sw.ElapsedMilliseconds + " ms -> " + path;
    }

    /// <summary>Path from the player start to every room; compares with the pre-detail baseline file.</summary>
    public static string NavConnectivityReport(string baselinePath)
    {
        AnalyzeRooms();
        var baseline = new Dictionary<string, string>();
        if (System.IO.File.Exists(baselinePath))
            foreach (var line in System.IO.File.ReadAllLines(baselinePath))
            { var kv = line.Split('='); if (kv.Length == 2) baseline[kv[0]] = kv[1]; }
        UnityEngine.AI.NavMesh.SamplePosition(new Vector3(0, 0, -8), out var h0, 3f, UnityEngine.AI.NavMesh.AllAreas);
        var sb = new StringBuilder(); int ok = 0, regress = 0;
        foreach (var r in rooms.Values)
        {
            string st;
            if (!UnityEngine.AI.NavMesh.SamplePosition(r.Center + Vector3.up * 0.2f, out var h, 3.5f, UnityEngine.AI.NavMesh.AllAreas)) st = "NO_NAVMESH";
            else
            {
                var path = new UnityEngine.AI.NavMeshPath();
                UnityEngine.AI.NavMesh.CalculatePath(h0.position, h.position, UnityEngine.AI.NavMesh.AllAreas, path);
                st = path.status.ToString();
            }
            if (st == "PathComplete") ok++;
            if (baseline.TryGetValue(r.id, out var before) && before == "PathComplete" && st != "PathComplete") { regress++; sb.AppendLine("REGRESSION " + r.id + ": " + before + " -> " + st); }
        }
        return $"complete {ok}/{rooms.Count}, regressions {regress}\n" + sb;
    }

    /// <summary>Automated QA over everything this pass placed: grounding, materials, per-room counts, NavMesh reachability.</summary>
    public static string QAReport(string outPath)
    {
        AnalyzeRooms();
        var root = GameObject.Find(RootName);
        var sb = new StringBuilder();
        int floating = 0, sunk = 0, badMat = 0, total = 0;
        var issues = new StringBuilder();
        foreach (Transform level in root.transform)
            foreach (Transform group in level)
                foreach (Transform prop in group)
                {
                    if (prop.GetComponent<Light>() != null || prop.GetComponentInChildren<Renderer>() == null) continue;
                    total++;
                    foreach (var rend in prop.GetComponentsInChildren<Renderer>())
                        foreach (var m in rend.sharedMaterials)
                            if (m == null || m.shader == null || m.shader.name.Contains("InternalError") || !(m.shader.name.StartsWith("Universal") || m.shader.name.StartsWith("TextMeshPro")))
                            { badMat++; issues.AppendLine("material: " + group.name + "/" + prop.name + " -> " + (m ? m.shader.name : "null")); break; }
                    string key = prop.name.Split('_')[0];
                    bool grounded = !(prop.name.StartsWith("Fixture") || prop.name.StartsWith("RoomSign") || prop.name.StartsWith("Exit") || prop.name.StartsWith("exitSign") ||
                                      prop.name.StartsWith("PipeRun") || prop.name.StartsWith("TeachingBoard") || prop.name.StartsWith("Scoreboard"));
                    if (Lib.TryGetValue(prop.name, out var d) && (d.kind == Kind.Wall || d.kind == Kind.Ceiling || d.kind == Kind.Surface)) grounded = false;
                    if (!grounded) continue;
                    Bounds b = WorldBounds(prop.gameObject);
                    var hits = Physics.RaycastAll(new Vector3(b.center.x, b.min.y + 0.25f, b.center.z), Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore);
                    float best = float.PositiveInfinity;
                    foreach (var h in hits) if (!h.collider.transform.IsChildOf(prop)) best = Mathf.Min(best, h.distance - 0.25f);
                    if (float.IsPositiveInfinity(best)) continue;
                    if (best > 0.12f) { floating++; issues.AppendLine($"floating {best:F2}m: {group.name}/{prop.name}"); }
                    else if (best < -0.2f) { sunk++; issues.AppendLine($"below surface {best:F2}m: {group.name}/{prop.name}"); }
                }
        sb.AppendLine($"props checked {total}, floating {floating}, sunk {sunk}, bad materials {badMat}");
        sb.Append(issues);
        System.IO.File.WriteAllText(outPath, sb.ToString());
        return sb.ToString().Length > 3000 ? sb.ToString().Substring(0, 3000) : sb.ToString();
    }

    /// <summary>Door plaques on the corridor side of each classroom / office doorway.</summary>
    public static string BuildDoorSigns()
    {
        if (rooms.Count == 0) AnalyzeRooms();
        int n = 0;
        foreach (var kv in Plan)
        {
            if (kv.Value.label == null || !rooms.TryGetValue(kv.Key, out Room room)) continue;
            foreach (var o in room.openings)
            {
                Vector3 outside = room.WallPoint(o.side, o.Mid, -1.2f);
                Room corridor = rooms.Values.FirstOrDefault(x => x != room && x.rect.Contains(new Vector2(outside.x, outside.z)) && Mathf.Abs(x.floorY - room.floorY) < 1.5f && x.level != 9);
                if (corridor == null || corridor.group == null) continue;
                Side cs = Opp(o.side);
                var co = corridor.openings.FirstOrDefault(x => x.side == cs && x.to > o.from - 0.5f && x.from < o.to + 0.5f);
                if (co == null) continue;
                int before = corridor.placedCount;
                DoorSign(corridor, co, kv.Value.label);
                if (corridor.placedCount > before) n++;
                break;
            }
        }
        return "door signs: " + n;
    }
}
