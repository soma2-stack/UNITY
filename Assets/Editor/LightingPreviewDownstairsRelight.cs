using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-shot editor pass that redoes ONLY the downstairs lighting in
/// SchoolOfTheDead_LightingPreview.unity (never the base gameplay scene).
///
/// Tools > Lighting Preview > Redo Downstairs Lighting:
///  - DISABLES (never deletes) the old preview-only downstairs lights under LightingPreview_Only
///    (the ground-floor Cool Pool / Warm Pool / Cool Stair Fill / Emergency Red Accent lights).
///    Base-map lights, the preview moonlight fill, upstairs lights, and the basement/teleporter
///    lights are left untouched.
///  - CREATES a "Downstairs Relight (Preview)" group under LightingPreview_Only with:
///      * large dim cool ceiling spotlights centered in each downstairs room (1-3 per room by size),
///      * evenly spaced red emergency ceiling spotlights along every downstairs hallway, with
///        dim neutral/cool fills between the red pools for readability,
///      * red + cool fills in each downstairs stairwell.
///  - Saves ONLY this scene.
///
/// Positions are anchored to the existing (now disabled) preview lights' coordinates, so the new
/// pass lands exactly in the rooms/hallways the old one covered. All values are constants at the
/// top of each table for quick tuning. Tools > Lighting Preview > Revert Downstairs Relight
/// removes the new group and re-enables the old lights.
/// </summary>
public static class LightingPreviewDownstairsRelight
{
    private const string PreviewScene = "SchoolOfTheDead_LightingPreview";
    private const string PreviewRootName = "LightingPreview_Only";
    private const string GroupName = "Downstairs Relight (Preview)";

    // Old downstairs preview lights live in this world-Y band (ground floor pools + stair fills).
    private const float DownstairsMinY = 2.4f;
    private const float DownstairsMaxY = 5.1f;

    // New-light heights.
    private const float RoomLightY = 3.25f;
    private const float HallLightY = 3.3f;

    // Palette: deep blue-gray night school. Slight room-to-room variation, muted cold white mix.
    private static readonly Color CoolA = new Color(0.55f, 0.62f, 0.76f);
    private static readonly Color CoolB = new Color(0.60f, 0.66f, 0.74f);
    private static readonly Color ColdWhite = new Color(0.66f, 0.70f, 0.76f);
    private static readonly Color EmergencyRed = new Color(0.85f, 0.07f, 0.05f);
    private static readonly Color HallFill = new Color(0.50f, 0.58f, 0.72f);

    // Hallway pass tuning.
    private const float RedSpacing = 6f;      // meters between red ceiling spots (overlapping pools)
    private const float RedIntensity = 20f;   // per user spec: 15-30
    private const float RedSpotAngle = 100f;
    private const float FillIntensity = 10f;  // dim neutral fill between red pools
    private const float FillRange = 9f;
    private const float FillSpotAngle = 85f;

    // ---- Data tables (coordinates taken from the old preview lights' anchors) ---------------

    private struct RoomDef
    {
        public string name; public Vector2[] anchors; public float intensity; public float range; public Color color;
        public RoomDef(string n, float i, float r, Color c, params Vector2[] a) { name = n; intensity = i; range = r; color = c; anchors = a; }
    }

    private static readonly RoomDef[] Rooms =
    {
        // Large starter room: 3 lights, slightly brighter so box/wall-buys/doors read clearly.
        new RoomDef("Starter Classroom", 22f, 13f, new Color(0.62f, 0.68f, 0.78f),
            new Vector2(-6.8f, -7.7f), new Vector2(0f, -7.7f), new Vector2(6.8f, -7.7f)),
        // Cafeteria (wide): 3 lights.
        new RoomDef("Cafeteria Wing", 20f, 14f, CoolB,
            new Vector2(-33.8f, 5.1f), new Vector2(-27f, 5.1f), new Vector2(-20.2f, 5.1f)),
        new RoomDef("Cafeteria Kitchen", 18f, 12f, CoolA, new Vector2(-26.9f, -10f), new Vector2(-18.4f, -10f)),
        // Library (long room): 3 lights.
        new RoomDef("Library", 18f, 13f, CoolB,
            new Vector2(63.4f, -32.6f), new Vector2(63.4f, -24.3f), new Vector2(63.4f, -16f)),
        new RoomDef("English Classroom", 18f, 12f, CoolA, new Vector2(9f, 11.8f), new Vector2(9f, 22f)),
        new RoomDef("History Room", 18f, 12.5f, ColdWhite, new Vector2(20.2f, -26f), new Vector2(20.2f, -12.6f)),
        new RoomDef("Math Room", 18f, 12f, CoolA, new Vector2(35.5f, -22.7f), new Vector2(45.2f, -22.7f)),
        new RoomDef("Music Room", 24f, 12f, CoolB, new Vector2(38.2f, -35.9f)),
        new RoomDef("Main Office", 18f, 12f, ColdWhite, new Vector2(23.4f, -49.2f), new Vector2(34.6f, -49.2f)),
        new RoomDef("Conference Room", 18f, 12.5f, CoolA, new Vector2(62.2f, 5.7f), new Vector2(75.8f, 5.7f)),
        new RoomDef("South Office Biology Lab", 18f, 13f, CoolB, new Vector2(-70.6f, -30.5f), new Vector2(-56.2f, -30.5f)),
        new RoomDef("South Office Music Room", 18f, 12f, CoolA, new Vector2(-40.6f, -36.6f), new Vector2(-40.6f, -24.4f)),
        new RoomDef("South Office Art Studio", 18f, 13f, ColdWhite, new Vector2(-25.1f, -30.5f), new Vector2(-11.3f, -30.5f)),
        new RoomDef("South Office Computer Lab", 18f, 12f, CoolA, new Vector2(4.2f, -36.6f), new Vector2(4.2f, -24.4f)),
        new RoomDef("West Wing Biology Room", 24f, 12f, CoolB, new Vector2(-70.8f, 13.1f)),
        new RoomDef("West Wing English Room", 24f, 12f, CoolA, new Vector2(-57f, 13.1f)),
        new RoomDef("West South Office", 18f, 12f, ColdWhite, new Vector2(-94.3f, -30.5f), new Vector2(-85.3f, -30.5f)),
    };

    private struct HallDef
    {
        public string name; public Vector2 from; public Vector2 to; public float redRange;
        public HallDef(string n, Vector2 f, Vector2 t, float r) { name = n; from = f; to = t; redRange = r; }
    }

    // From/to = old anchor extents padded ~1.5m so the whole route is covered.
    private static readonly HallDef[] Hallways =
    {
        new HallDef("Main Hallway",              new Vector2(-9.5f, 5.1f),   new Vector2(11.5f, 5.1f),   11f),
        new HallDef("Lower East Wing Corridor",  new Vector2(53f, -30.7f),   new Vector2(53f, 1.4f),     12f),
        new HallDef("South Wing Hallway",        new Vector2(29f, -35.3f),   new Vector2(29f, -13.2f),   11f),
        new HallDef("West Cafeteria Corridor",   new Vector2(-74.9f, 5.1f),  new Vector2(-47.1f, 5.1f),  11f),
        new HallDef("South End Hallway",         new Vector2(-79f, -37.8f),  new Vector2(-79f, -7f),     11f),
        new HallDef("Office South Hallway",      new Vector2(-56.8f, -44.1f),new Vector2(-0.4f, -44.1f), 11f),
        // Parking-lot / outside-facing covered corridor (staff entrance alleys).
        new HallDef("Staff Entrance Alley South (Parking Lot)", new Vector2(23.1f, -63.7f), new Vector2(40.5f, -63.7f), 11f),
        new HallDef("Staff Entrance Alley North (Parking Lot)", new Vector2(46.8f, -58.1f), new Vector2(46.8f, -37.1f), 11f),
        new HallDef("West Corridor Chemistry Lab", new Vector2(-64.1f, -4.4f), new Vector2(-49.8f, -4.4f), 11f),
    };

    private struct StairDef
    {
        public string name; public Vector3 pos; public float redRange;
        public StairDef(string n, Vector3 p, float r) { name = n; pos = p; redRange = r; }
    }

    private static readonly StairDef[] Stairwells =
    {
        new StairDef("Stairwell Access",     new Vector3(20.2f, 4.2f, 5.2f),    8f),
        new StairDef("Secondary Stairwell",  new Vector3(29.1f, 4.4f, -2.5f),   9f),
        new StairDef("East Wing Staircase",  new Vector3(61.8f, 4.4f, -4.3f),   10f),
        new StairDef("Secret Stairwell",     new Vector3(62.2f, -0.4f, -13.4f), 7f),
    };

    // ---- Menu entries -------------------------------------------------------------------------

    [MenuItem("Tools/Lighting Preview/Redo Downstairs Lighting")]
    private static void Apply()
    {
        if (!TryGetContext(out Scene scene, out Transform root))
        {
            return;
        }

        Undo.SetCurrentGroupName("Redo Downstairs Lighting");
        int undoGroup = Undo.GetCurrentGroup();

        // Idempotent: remove a previous run's group before rebuilding.
        Transform existing = root.Find(GroupName);
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing.gameObject);
        }

        // 1) Disable (never delete) the old downstairs preview lights.
        List<string> disabled = DisableOldDownstairsLights(root, true);

        // 2) Build the replacement pass.
        var report = new StringBuilder();
        Transform group = NewChild(root, GroupName).transform;
        Transform roomsGroup = NewChild(group, "Rooms").transform;
        Transform hallsGroup = NewChild(group, "Emergency Hallways").transform;
        Transform stairsGroup = NewChild(group, "Stairwells").transform;

        int roomCount = 0, redCount = 0, fillCount = 0;

        foreach (RoomDef room in Rooms)
        {
            Transform holder = NewChild(roomsGroup, room.name).transform;
            for (int i = 0; i < room.anchors.Length; i++)
            {
                Vector2 a = room.anchors[i];
                MakeSpot(holder, room.name + " - Night Spot " + (i + 1),
                    new Vector3(a.x, RoomLightY, a.y), room.color, room.intensity, room.range, 120f);
                roomCount++;
            }
            report.AppendLine("  [Room] " + room.name + ": " + room.anchors.Length + " ceiling spot(s)");
        }

        foreach (HallDef hall in Hallways)
        {
            Transform holder = NewChild(hallsGroup, hall.name).transform;
            Vector3 from = new Vector3(hall.from.x, HallLightY, hall.from.y);
            Vector3 to = new Vector3(hall.to.x, HallLightY, hall.to.y);
            float length = Vector3.Distance(from, to);
            int segments = Mathf.Max(1, Mathf.CeilToInt(length / RedSpacing));
            int reds = segments + 1;

            var redPositions = new Vector3[reds];
            for (int i = 0; i < reds; i++)
            {
                redPositions[i] = Vector3.Lerp(from, to, reds == 1 ? 0.5f : (float)i / (reds - 1));
                MakeSpot(holder, hall.name + " - Red Emergency " + (i + 1),
                    redPositions[i], EmergencyRed, RedIntensity, hall.redRange, RedSpotAngle);
                redCount++;
            }

            // Dim neutral/cool fill on every other midpoint so the route stays readable
            // between red pools without washing the red mood out.
            int fills = 0;
            for (int i = 0; i + 1 < reds; i += 2)
            {
                Vector3 mid = (redPositions[i] + redPositions[i + 1]) * 0.5f;
                MakeSpot(holder, hall.name + " - Cool Fill " + (++fills),
                    mid, HallFill, FillIntensity, FillRange, FillSpotAngle);
                fillCount++;
            }
            report.AppendLine("  [Hallway] " + hall.name + ": " + reds + " red emergency + " + fills + " cool fill");
        }

        foreach (StairDef stair in Stairwells)
        {
            Transform holder = NewChild(stairsGroup, stair.name).transform;
            MakePoint(holder, stair.name + " - Red Emergency",
                stair.pos + new Vector3(0.8f, 0.3f, 0f), EmergencyRed, 16f, stair.redRange);
            MakePoint(holder, stair.name + " - Cool Fill",
                stair.pos + new Vector3(-0.8f, 0f, 0f), HallFill, 8f, stair.redRange * 0.85f);
            redCount++; fillCount++;
            report.AppendLine("  [Stairwell] " + stair.name + ": 1 red emergency + 1 cool fill");
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);

        Debug.Log("[DownstairsRelight] DONE. Disabled " + disabled.Count + " old downstairs preview light(s); created "
            + roomCount + " room spot(s), " + redCount + " red emergency light(s), " + fillCount
            + " cool fill(s) under " + PreviewRootName + "/" + GroupName + ". Scene saved: " + saved + "\n"
            + "Disabled old lights:\n    " + string.Join("\n    ", disabled) + "\nCreated:\n" + report);
    }

    [MenuItem("Tools/Lighting Preview/Revert Downstairs Relight")]
    private static void Revert()
    {
        if (!TryGetContext(out Scene scene, out Transform root))
        {
            return;
        }

        Undo.SetCurrentGroupName("Revert Downstairs Relight");
        Transform group = root.Find(GroupName);
        if (group != null)
        {
            Undo.DestroyObjectImmediate(group.gameObject);
        }
        List<string> reEnabled = DisableOldDownstairsLights(root, false);
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        Debug.Log("[DownstairsRelight] Reverted: removed the relight group and re-enabled "
            + reEnabled.Count + " old light(s). Scene saved: " + saved);
    }

    [MenuItem("Tools/Lighting Preview/Redo Downstairs Lighting", true)]
    [MenuItem("Tools/Lighting Preview/Revert Downstairs Relight", true)]
    private static bool ValidateScene()
    {
        return SceneManager.GetActiveScene().name == PreviewScene;
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private static bool TryGetContext(out Scene scene, out Transform root)
    {
        scene = SceneManager.GetActiveScene();
        root = null;
        if (scene.name != PreviewScene)
        {
            EditorUtility.DisplayDialog("Downstairs Relight",
                "Open '" + PreviewScene + "' first. This tool only touches that scene.", "OK");
            return false;
        }
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            if (go.name == PreviewRootName)
            {
                root = go.transform;
                return true;
            }
        }
        EditorUtility.DisplayDialog("Downstairs Relight",
            "Couldn't find the '" + PreviewRootName + "' root in this scene.", "OK");
        return false;
    }

    // Old downstairs preview lights = every non-directional Light under LightingPreview_Only in
    // the ground-floor Y band (Cool/Warm Pools, stair fills, red accents), plus the Secret
    // Stairwell fill below it. Excludes upstairs (y>=7.4), basement/teleporter (y<=-1), the
    // preview moonlight (directional), everything outside the preview root, and our own group.
    private static List<string> DisableOldDownstairsLights(Transform root, bool disable)
    {
        var touched = new List<string>();
        foreach (Light light in root.GetComponentsInChildren<Light>(true))
        {
            if (light.type == LightType.Directional)
            {
                continue;
            }
            Transform t = light.transform;
            if (IsUnder(t, root.Find(GroupName)))
            {
                continue;
            }
            float y = t.position.y;
            bool secretStairFill = light.gameObject.name.StartsWith("library_staircase_");
            if (!secretStairFill && (y < DownstairsMinY || y > DownstairsMaxY))
            {
                continue;
            }
            if (light.enabled != !disable)
            {
                continue; // already in the requested state
            }
            Undo.RecordObject(light, disable ? "Disable old downstairs light" : "Re-enable old downstairs light");
            light.enabled = !disable;
            EditorUtility.SetDirty(light);
            touched.Add(light.gameObject.name);
        }
        touched.Sort();
        return touched;
    }

    private static bool IsUnder(Transform t, Transform maybeParent)
    {
        return maybeParent != null && t.IsChildOf(maybeParent);
    }

    private static GameObject NewChild(Transform parent, string name)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent, false);
        return go;
    }

    private static Light MakeSpot(Transform parent, string name, Vector3 worldPos, Color color,
        float intensity, float range, float spotAngle)
    {
        GameObject go = NewChild(parent, name);
        go.transform.position = worldPos;
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // ceiling spot aiming straight down
        Light l = go.AddComponent<Light>();
        l.type = LightType.Spot;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.spotAngle = spotAngle;
        l.shadows = LightShadows.None; // preview pass: keep it cheap, ~100 realtime lights
        return l;
    }

    private static Light MakePoint(Transform parent, string name, Vector3 worldPos, Color color,
        float intensity, float range)
    {
        GameObject go = NewChild(parent, name);
        go.transform.position = worldPos;
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        return l;
    }
}
