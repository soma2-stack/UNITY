using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-shot editor pass that redoes ONLY the upstairs lighting in
/// SchoolOfTheDead_LightingPreview.unity (never the base gameplay scene). Companion to
/// LightingPreviewDownstairsRelight — same rules, second floor.
///
/// Tools > Lighting Preview > Redo Upstairs Lighting:
///  - DISABLES (never deletes) the old upstairs preview lights under LightingPreview_Only
///    (the floor-2 Cool Pool / Warm Pool lights, world-Y 7.0-8.5). Base-map lights, the preview
///    moonlight, downstairs, and basement lights are untouched.
///  - CREATES an "Upstairs Relight (Preview)" group with:
///      * large dim cool ceiling spotlights per upstairs room (1-3 by size, per-room variation),
///      * evenly spaced red emergency ceiling spotlights along every upper hallway with dim
///        neutral/cool fills between the red pools,
///      * red + cool fills at each stairwell's upper landing.
///  - Saves ONLY this scene. Tools > Lighting Preview > Revert Upstairs Relight undoes it.
///
/// Positions are anchored to the old preview lights' exact coordinates.
/// </summary>
public static class LightingPreviewUpstairsRelight
{
    private const string PreviewScene = "SchoolOfTheDead_LightingPreview";
    private const string PreviewRootName = "LightingPreview_Only";
    private const string GroupName = "Upstairs Relight (Preview)";

    // Old upstairs preview lights live in this world-Y band (floor-2 pools at 7.4-7.6).
    private const float UpstairsMinY = 7.0f;
    private const float UpstairsMaxY = 8.5f;

    // New-light heights (old floor-2 pools sat at 7.6).
    private const float RoomLightY = 7.65f;
    private const float HallLightY = 7.65f;
    private const float StairLandingY = 7.7f;

    // Palette: deep blue-gray night school; slight per-room variation, muted cold white mix.
    private static readonly Color CoolA = new Color(0.55f, 0.62f, 0.76f);
    private static readonly Color CoolB = new Color(0.60f, 0.66f, 0.74f);
    private static readonly Color ColdWhite = new Color(0.66f, 0.70f, 0.76f);
    private static readonly Color EmergencyRed = new Color(0.85f, 0.07f, 0.05f);
    private static readonly Color HallFill = new Color(0.50f, 0.58f, 0.72f);

    // Hallway pass tuning (matches the downstairs pass): local red accents, not solid red.
    private const float RedSpacing = 6f;
    private const float RedIntensity = 20f;   // spec: 15-30
    private const float RedSpotAngle = 100f;
    private const float FillIntensity = 10f;
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
        new RoomDef("East Wing Classroom A", 18f, 12f, CoolA, new Vector2(83f, 23.2f), new Vector2(83f, 33.5f)),
        new RoomDef("East Wing Classroom B", 18f, 12.5f, CoolB, new Vector2(81.8f, -11f), new Vector2(81.8f, 2f)),
        // Long gym upper area: 3 lights, muted cold white so the big space reads clearly.
        new RoomDef("Gymnasium (Floor 2)", 20f, 14f, ColdWhite,
            new Vector2(41.6f, 12.3f), new Vector2(50.6f, 12.3f), new Vector2(59.6f, 12.3f)),
        new RoomDef("Nurse's Office", 24f, 12f, CoolA, new Vector2(58.6f, 51.1f)),
        new RoomDef("Nurse's Office Backroom", 24f, 11f, CoolB, new Vector2(58.6f, 60.3f)),
        new RoomDef("Principal's Office", 18f, 12.5f, ColdWhite, new Vector2(22.7f, 57.1f), new Vector2(35.3f, 57.1f)),
        new RoomDef("Master Security & Breaker Room", 24f, 12f, CoolB, new Vector2(18.2f, 26.9f)),
    };

    private struct HallDef
    {
        public string name; public Vector2 from; public Vector2 to; public float redRange;
        public HallDef(string n, Vector2 f, Vector2 t, float r) { name = n; from = f; to = t; redRange = r; }
    }

    // From/to = old anchor extents padded ~1.5m. The stairwell-landing segment (old single
    // "2nd Floor Hallway" pool at z 5.0) and "2nd floor hallway 2" (z 16.2-37.6) form one
    // continuous x=29 corridor, covered as a single route.
    private static readonly HallDef[] Hallways =
    {
        new HallDef("2nd Floor Hallway (full run)",  new Vector2(29f, 3.5f),   new Vector2(29f, 39.1f),  11f),
        new HallDef("East Wing Corridor (Upper)",    new Vector2(70.6f, -4.8f),new Vector2(70.6f, 29.6f),12f),
        new HallDef("2nd Floor North Connector",     new Vector2(39.7f, 41.8f),new Vector2(67.6f, 41.8f),11f),
        new HallDef("Gym North Connector",           new Vector2(50.6f, 26.3f),new Vector2(50.6f, 38.4f),11f),
    };

    private struct StairDef
    {
        public string name; public Vector3 pos; public float redRange;
        public StairDef(string n, Vector3 p, float r) { name = n; pos = p; redRange = r; }
    }

    // Upper landings of the stairwells that reach floor 2 (x/z from the stairwell shafts;
    // the Secret Stairwell only goes down to the basement, so it is not part of this pass).
    private static readonly StairDef[] StairLandings =
    {
        new StairDef("Stairwell Access (Upper Landing)",    new Vector3(20.2f, StairLandingY, 5.2f),  8f),
        new StairDef("Secondary Stairwell (Upper Landing)", new Vector3(29.1f, StairLandingY, -2.5f), 9f),
        new StairDef("East Wing Staircase (Upper Landing)", new Vector3(61.8f, StairLandingY, -4.3f), 10f),
    };

    // ---- Menu entries -------------------------------------------------------------------------

    [MenuItem("Tools/Lighting Preview/Redo Upstairs Lighting")]
    private static void Apply()
    {
        if (!TryGetContext(out Scene scene, out Transform root))
        {
            return;
        }

        Undo.SetCurrentGroupName("Redo Upstairs Lighting");
        int undoGroup = Undo.GetCurrentGroup();

        Transform existing = root.Find(GroupName);
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing.gameObject);
        }

        List<string> disabled = SetOldUpstairsLights(root, true);

        var report = new StringBuilder();
        Transform group = NewChild(root, GroupName).transform;
        Transform roomsGroup = NewChild(group, "Rooms").transform;
        Transform hallsGroup = NewChild(group, "Emergency Hallways").transform;
        Transform stairsGroup = NewChild(group, "Stairwell Landings").transform;

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

        foreach (StairDef stair in StairLandings)
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

        Debug.Log("[UpstairsRelight] DONE. Disabled " + disabled.Count + " old upstairs preview light(s); created "
            + roomCount + " room spot(s), " + redCount + " red emergency light(s), " + fillCount
            + " cool fill(s) under " + PreviewRootName + "/" + GroupName + ". Scene saved: " + saved + "\n"
            + "Disabled old lights:\n    " + string.Join("\n    ", disabled) + "\nCreated:\n" + report);
    }

    [MenuItem("Tools/Lighting Preview/Revert Upstairs Relight")]
    private static void Revert()
    {
        if (!TryGetContext(out Scene scene, out Transform root))
        {
            return;
        }

        Undo.SetCurrentGroupName("Revert Upstairs Relight");
        Transform group = root.Find(GroupName);
        if (group != null)
        {
            Undo.DestroyObjectImmediate(group.gameObject);
        }
        List<string> reEnabled = SetOldUpstairsLights(root, false);
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        Debug.Log("[UpstairsRelight] Reverted: removed the relight group and re-enabled "
            + reEnabled.Count + " old light(s). Scene saved: " + saved);
    }

    [MenuItem("Tools/Lighting Preview/Redo Upstairs Lighting", true)]
    [MenuItem("Tools/Lighting Preview/Revert Upstairs Relight", true)]
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
            EditorUtility.DisplayDialog("Upstairs Relight",
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
        EditorUtility.DisplayDialog("Upstairs Relight",
            "Couldn't find the '" + PreviewRootName + "' root in this scene.", "OK");
        return false;
    }

    // Old upstairs preview lights = every non-directional Light under LightingPreview_Only in the
    // floor-2 Y band (7.0-8.5). Excludes downstairs/basement, the preview moonlight (directional),
    // everything outside the preview root, and BOTH relight groups' own new lights.
    private static List<string> SetOldUpstairsLights(Transform root, bool disable)
    {
        var touched = new List<string>();
        Transform ownGroup = root.Find(GroupName);
        Transform downstairsGroup = root.Find("Downstairs Relight (Preview)");
        foreach (Light light in root.GetComponentsInChildren<Light>(true))
        {
            if (light.type == LightType.Directional)
            {
                continue;
            }
            Transform t = light.transform;
            if (IsUnder(t, ownGroup) || IsUnder(t, downstairsGroup))
            {
                continue;
            }
            float y = t.position.y;
            if (y < UpstairsMinY || y > UpstairsMaxY)
            {
                continue;
            }
            if (light.enabled != !disable)
            {
                continue; // already in the requested state
            }
            Undo.RecordObject(light, disable ? "Disable old upstairs light" : "Re-enable old upstairs light");
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
        l.shadows = LightShadows.None; // preview pass: keep it cheap
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
