#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SchoolOfTheDeadPropPlacementValidator
{
    private const string ScenePath = "Assets/Scenes/SchoolOfTheDead.unity";
    private const string ResultsPath = "Assets/Editor/SchoolOfTheDeadPropValidationResults.md";
    private const float PlayerRadius = 0.3f;
    private const float DoorClearance = 0.9f;
    private const float StairClearance = 0.35f;
    private const float FloorTolerance = 0.08f;
    private const float FloatingTolerance = 0.12f;
    private const float WallNearDistance = 0.06f;
    private const float WallClipDepth = 0.03f;

    private enum Severity
    {
        Error,
        Warning,
        Review,
        Info
    }

    private sealed class Finding
    {
        public Severity Severity;
        public string Category;
        public string ObjectPath;
        public string Message;
        public Vector3 Position;
    }

    private sealed class RoomRecord
    {
        public string Id;
        public MeshRenderer Floor;
        public readonly List<MeshRenderer> Walls = new List<MeshRenderer>();
        public readonly List<MeshRenderer> Transoms = new List<MeshRenderer>();
    }

    private sealed class PropRecord
    {
        public Transform Root;
        public string RoomId;
        public Bounds Bounds;       // Enabled non-trigger colliders, falling back to renderers.
        public Bounds VisualBounds; // All enabled renderers, falling back to physical bounds.
        public bool UsesColliderBounds;
        public string Path;
    }

    [MenuItem("Tools/School Of The Dead/Validate Prop Placement")]
    public static void ValidatePropPlacement()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedForValidation = !scene.IsValid() || !scene.isLoaded;

        try
        {
            if (openedForValidation)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            }

            List<Finding> findings = new List<Finding>();
            List<MeshRenderer> renderers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true))
                .ToList();
            Dictionary<string, RoomRecord> rooms = BuildRooms(renderers);
            List<PropRecord> props = CollectProps(scene, rooms, findings);
            Door[] doors = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Door>(true))
                .ToArray();
            List<Bounds> stairBounds = CollectStairBounds(renderers);
            List<KeyValuePair<string, Bounds>> walkingPaths = BuildWalkingPaths(rooms);

            ValidateDoorClearance(props, doors, rooms, findings);
            ValidateStairs(props, stairBounds, findings);
            ValidateWalkingPaths(props, walkingPaths, findings);
            ValidateFloorsWallsAndScale(props, rooms, findings);
            ValidateFurnitureFacing(props, findings);
            ValidateKitchenCounters(props, rooms, findings);

            WriteReport(scene, rooms, props, doors, stairBounds, walkingPaths, findings);
            AssetDatabase.ImportAsset(ResultsPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"[SchoolOfTheDeadPropPlacementValidator] Inspected {props.Count} props and wrote {findings.Count} findings to {ResultsPath}.");

            if (!Application.isBatchMode)
            {
                int errors = findings.Count(finding => finding.Severity == Severity.Error);
                int warnings = findings.Count(finding => finding.Severity == Severity.Warning);
                EditorUtility.DisplayDialog(
                    "School of the Dead Prop Validation",
                    $"Checked {props.Count} props.\nErrors: {errors}\nWarnings: {warnings}\n\nResults: {ResultsPath}",
                    "OK");
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("Prop Validation Failed", exception.Message, "OK");
            }
            throw;
        }
        finally
        {
            if (openedForValidation && scene.IsValid() && scene.isLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }

    private static Dictionary<string, RoomRecord> BuildRooms(List<MeshRenderer> renderers)
    {
        Dictionary<string, RoomRecord> rooms = renderers
            .Where(renderer => renderer.name.EndsWith("_Floor", StringComparison.OrdinalIgnoreCase))
            .GroupBy(renderer => renderer.name.Substring(0, renderer.name.Length - "_Floor".Length), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new RoomRecord { Id = group.Key, Floor = group.First() },
                StringComparer.OrdinalIgnoreCase);

        foreach (RoomRecord room in rooms.Values)
        {
            string prefix = room.Id + "_";
            room.Walls.AddRange(renderers.Where(renderer =>
                renderer.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                renderer.name.IndexOf("_Seg", StringComparison.OrdinalIgnoreCase) >= 0 &&
                renderer.name.IndexOf("Transom", StringComparison.OrdinalIgnoreCase) < 0 &&
                renderer.name.IndexOf("Floor", StringComparison.OrdinalIgnoreCase) < 0 &&
                renderer.name.IndexOf("Ceiling", StringComparison.OrdinalIgnoreCase) < 0 &&
                renderer.name.IndexOf("AntiZFight", StringComparison.OrdinalIgnoreCase) < 0));
            room.Transoms.AddRange(renderers.Where(renderer =>
                renderer.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                renderer.name.IndexOf("Transom", StringComparison.OrdinalIgnoreCase) >= 0 &&
                renderer.name.IndexOf("AntiZFight", StringComparison.OrdinalIgnoreCase) < 0));
        }

        return rooms;
    }

    private static List<PropRecord> CollectProps(Scene scene, Dictionary<string, RoomRecord> rooms, List<Finding> findings)
    {
        List<PropRecord> props = new List<PropRecord>();
        GameObject roomPropsRoot = FindRoot(scene, "Generated_RoomProps");
        if (roomPropsRoot != null)
        {
            for (int roomIndex = 0; roomIndex < roomPropsRoot.transform.childCount; roomIndex++)
            {
                Transform room = roomPropsRoot.transform.GetChild(roomIndex);
                for (int propIndex = 0; propIndex < room.childCount; propIndex++)
                {
                    AddProp(props, room.GetChild(propIndex), room.name, rooms, findings);
                }
            }
        }

        foreach (string rootName in new[] { "Generated_Props", "Generated_SecretEgg" })
        {
            GameObject root = FindRoot(scene, rootName);
            if (root == null)
            {
                continue;
            }

            for (int index = 0; index < root.transform.childCount; index++)
            {
                Transform child = root.transform.GetChild(index);
                if (child.name.StartsWith("Books (", StringComparison.OrdinalIgnoreCase))
                {
                    for (int nested = 0; nested < child.childCount; nested++)
                    {
                        AddProp(props, child.GetChild(nested), null, rooms, findings);
                    }
                }
                else if (HasRenderableOrCollider(child))
                {
                    AddProp(props, child, null, rooms, findings);
                }
            }
        }

        return props;
    }

    private static void AddProp(
        List<PropRecord> props,
        Transform root,
        string roomId,
        Dictionary<string, RoomRecord> rooms,
        List<Finding> findings)
    {
        if (!TryGetPropBounds(root, out Bounds bounds, out Bounds visualBounds, out bool usesColliderBounds))
        {
            findings.Add(NewFinding(Severity.Info, "Unbounded props", root, "No enabled collider or renderer was found; spatial checks were skipped."));
            return;
        }

        if (string.IsNullOrWhiteSpace(roomId) || !rooms.ContainsKey(roomId))
        {
            roomId = FindContainingRoom(bounds.center, rooms);
        }

        props.Add(new PropRecord
        {
            Root = root,
            RoomId = roomId,
            Bounds = bounds,
            VisualBounds = visualBounds,
            UsesColliderBounds = usesColliderBounds,
            Path = HierarchyPath(root)
        });
    }

    private static void ValidateDoorClearance(
        List<PropRecord> props,
        Door[] doors,
        Dictionary<string, RoomRecord> rooms,
        List<Finding> findings)
    {
        foreach (PropRecord prop in props)
        {
            List<Door> intersectingDoors = doors.Where(door =>
            {
                Collider collider = door.GetComponent<Collider>();
                Bounds doorBounds = collider != null
                    ? collider.bounds
                    : new Bounds(door.transform.position, new Vector3(1f, 2.2f, 0.2f));
                Bounds clearance = ExpandedHorizontal(doorBounds, DoorClearance);
                clearance.Expand(new Vector3(0f, 0.2f, 0f));
                return prop.Bounds.Intersects(clearance);
            }).ToList();

            if (intersectingDoors.Count > 0)
            {
                string doorLabels = string.Join(", ", intersectingDoors.Select(door => $"`{door.doorId}`").Distinct());
                findings.Add(NewFinding(
                    Severity.Error,
                    "Buyable door clearance",
                    prop,
                    $"Intersects the {DoorClearance:0.0} m player-clearance zone for {doorLabels}."));
            }
        }

        foreach (RoomRecord room in rooms.Values)
        {
            foreach (MeshRenderer transom in room.Transoms)
            {
                float floorY = room.Floor.bounds.max.y;
                Bounds doorway = new Bounds(
                    new Vector3(transom.bounds.center.x, floorY + 1.1f, transom.bounds.center.z),
                    new Vector3(Mathf.Max(1.2f, transom.bounds.size.x), 2.2f, Mathf.Max(1.2f, transom.bounds.size.z)));
                doorway = ExpandedHorizontal(doorway, PlayerRadius);

                foreach (PropRecord prop in props.Where(prop => string.Equals(prop.RoomId, room.Id, StringComparison.OrdinalIgnoreCase) && prop.Bounds.Intersects(doorway)))
                {
                    findings.Add(NewFinding(
                        Severity.Warning,
                        "Doorway blocking",
                        prop,
                        $"Intersects the player-sized doorway zone derived from `{transom.name}`."));
                }
            }
        }
    }

    private static void ValidateStairs(List<PropRecord> props, List<Bounds> stairs, List<Finding> findings)
    {
        foreach (PropRecord prop in props)
        {
            if (stairs.Any(stair => prop.Bounds.Intersects(ExpandedHorizontal(stair, StairClearance))))
            {
                findings.Add(NewFinding(
                    Severity.Error,
                    "Stair blocking",
                    prop,
                    $"Intersects a stair/step/landing clearance zone expanded by {StairClearance:0.00} m."));
            }
        }
    }

    private static void ValidateWalkingPaths(
        List<PropRecord> props,
        List<KeyValuePair<string, Bounds>> walkingPaths,
        List<Finding> findings)
    {
        foreach (KeyValuePair<string, Bounds> path in walkingPaths)
        {
            foreach (PropRecord prop in props.Where(prop => prop.Bounds.Intersects(path.Value)))
            {
                findings.Add(NewFinding(
                    Severity.Warning,
                    "Main walking path",
                    prop,
                    $"Overlaps the conservative 1.8 m center lane derived from `{path.Key}_Floor`. Confirm with NavMesh and player-capsule testing."));
            }
        }
    }

    private static void ValidateFloorsWallsAndScale(
        List<PropRecord> props,
        Dictionary<string, RoomRecord> rooms,
        List<Finding> findings)
    {
        foreach (PropRecord prop in props)
        {
            Vector3 scale = prop.Root.lossyScale;
            float minScale = Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            float maxDimension = Mathf.Max(prop.VisualBounds.size.x, prop.VisualBounds.size.y, prop.VisualBounds.size.z);
            bool unexpectedlyLarge = maxDimension > 15f && !IsWallBacked(prop.Root.name);
            if (scale.x <= 0f || scale.y <= 0f || scale.z <= 0f || minScale < 0.02f || maxScale > 8f || unexpectedlyLarge)
            {
                findings.Add(NewFinding(
                    Severity.Warning,
                    "Unrealistic scale",
                    prop,
                    $"World scale is {Format(scale)} and visual bounds size is {Format(prop.VisualBounds.size)}; verify against room dimensions."));
            }

            if (string.IsNullOrWhiteSpace(prop.RoomId) || !rooms.TryGetValue(prop.RoomId, out RoomRecord room))
            {
                findings.Add(NewFinding(Severity.Review, "Room association", prop, "Could not associate this prop with a room floor; floor and wall checks were skipped."));
                continue;
            }

            bool elevatedByDesign = IsElevatedByDesign(prop.Root.name);
            float floorDelta = prop.VisualBounds.min.y - room.Floor.bounds.max.y;
            if (!elevatedByDesign && floorDelta > FloatingTolerance)
            {
                findings.Add(NewFinding(Severity.Warning, "Floating prop", prop, $"Lowest physical/rendered bound is {floorDelta:0.00} m above `{room.Floor.name}`."));
            }
            else if (floorDelta < -FloorTolerance)
            {
                findings.Add(NewFinding(Severity.Warning, "Sunken prop", prop, $"Extends {-floorDelta:0.00} m below `{room.Floor.name}`."));
            }

            foreach (MeshRenderer wall in room.Walls)
            {
                if (prop.VisualBounds.Intersects(wall.bounds))
                {
                    Vector3 overlap = IntersectionSize(prop.VisualBounds, wall.bounds);
                    float horizontalDepth = Mathf.Min(overlap.x, overlap.z);
                    Severity severity = IsWallBacked(prop.Root.name) ? Severity.Review : Severity.Warning;
                    if (horizontalDepth >= WallClipDepth)
                    {
                        findings.Add(NewFinding(
                            severity,
                            "Wall clipping",
                            prop,
                            $"Overlaps `{wall.name}` by approximately {horizontalDepth:0.000} m horizontally. Wall-backed props may be intentional."));
                        break;
                    }
                }
                else
                {
                    float gap = HorizontalBoundsGap(prop.VisualBounds, wall.bounds);
                    if (gap <= WallNearDistance && !IsWallBacked(prop.Root.name))
                    {
                        findings.Add(NewFinding(
                            Severity.Review,
                            "Wall proximity",
                            prop,
                            $"Sits {gap:0.000} m from `{wall.name}`; confirm usable clearance and no visual contact."));
                        break;
                    }
                }
            }
        }
    }

    private static void ValidateFurnitureFacing(List<PropRecord> props, List<Finding> findings)
    {
        foreach (IGrouping<string, PropRecord> roomGroup in props
                     .Where(prop => !string.IsNullOrWhiteSpace(prop.RoomId))
                     .GroupBy(prop => prop.RoomId, StringComparer.OrdinalIgnoreCase))
        {
            PropRecord whiteboard = roomGroup.FirstOrDefault(prop => prop.Root.name.IndexOf("Whiteboard", StringComparison.OrdinalIgnoreCase) >= 0);
            if (whiteboard == null)
            {
                continue;
            }

            foreach (PropRecord prop in roomGroup.Where(IsStudentDeskOrChair))
            {
                Vector3 expected = whiteboard.VisualBounds.center - prop.VisualBounds.center;
                expected.y = 0f;
                Vector3 forward = prop.Root.forward;
                forward.y = 0f;
                if (expected.sqrMagnitude < 0.01f || forward.sqrMagnitude < 0.01f)
                {
                    continue;
                }

                float dot = Vector3.Dot(forward.normalized, expected.normalized);
                if (dot < 0.5f)
                {
                    findings.Add(NewFinding(
                        Severity.Warning,
                        "Desk/chair facing",
                        prop,
                        $"Forward direction is not facing the room whiteboard (alignment dot {dot:0.00}; expected at least 0.50)."));
                }
            }
        }
    }

    private static void ValidateKitchenCounters(
        List<PropRecord> props,
        Dictionary<string, RoomRecord> rooms,
        List<Finding> findings)
    {
        foreach (PropRecord prop in props.Where(prop =>
                     prop.Root.name.IndexOf("Counter", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     prop.Root.name.IndexOf("Cabinet", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            if (string.IsNullOrWhiteSpace(prop.RoomId) ||
                prop.RoomId.IndexOf("cafeteria", StringComparison.OrdinalIgnoreCase) < 0 ||
                !rooms.TryGetValue(prop.RoomId, out RoomRecord room))
            {
                continue;
            }

            foreach (MeshRenderer wall in room.Walls)
            {
                if (!prop.VisualBounds.Intersects(wall.bounds))
                {
                    continue;
                }

                Vector3 overlap = IntersectionSize(prop.VisualBounds, wall.bounds);
                float horizontalDepth = Mathf.Min(overlap.x, overlap.z);
                if (horizontalDepth >= WallClipDepth)
                {
                    findings.Add(NewFinding(
                        Severity.Warning,
                        "Kitchen/cafeteria counter clearance",
                        prop,
                        $"Counter/cabinet penetrates `{wall.name}` by about {horizontalDepth:0.000} m; verify the usable front aisle after correction."));
                    break;
                }
            }
        }
    }

    private static void WriteReport(
        Scene scene,
        Dictionary<string, RoomRecord> rooms,
        List<PropRecord> props,
        Door[] doors,
        List<Bounds> stairs,
        List<KeyValuePair<string, Bounds>> paths,
        List<Finding> findings)
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine("# School of the Dead Prop Validation Results");
        report.AppendLine();
        report.AppendLine($"Generated by `Tools/School Of The Dead/Validate Prop Placement` at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC.");
        report.AppendLine("The validator is read-only: it does not move props, save scenes, rebake navigation, or change gameplay systems.");
        report.AppendLine();
        report.AppendLine("## Summary");
        report.AppendLine();
        report.AppendLine($"- Scene: `{scene.path}`");
        report.AppendLine($"- Rooms with floor renderers: {rooms.Count}");
        report.AppendLine($"- Props checked: {props.Count}");
        report.AppendLine($"- Buyable doors checked: {doors.Length}");
        report.AppendLine($"- Stair/step/landing bounds checked: {stairs.Count}");
        report.AppendLine($"- Derived main walking lanes checked: {paths.Count}");
        foreach (Severity severity in Enum.GetValues(typeof(Severity)))
        {
            report.AppendLine($"- {severity}: {findings.Count(finding => finding.Severity == severity)}");
        }
        report.AppendLine();

        report.AppendLine("## Thresholds and Limits");
        report.AppendLine();
        report.AppendLine($"- Player radius: {PlayerRadius:0.00} m; buyable-door side clearance: {DoorClearance:0.00} m.");
        report.AppendLine($"- Floating threshold: {FloatingTolerance:0.00} m; sunken threshold: {FloorTolerance:0.00} m.");
        report.AppendLine($"- Wall-near review distance: {WallNearDistance:0.00} m; wall clipping threshold: {WallClipDepth:0.00} m.");
        report.AppendLine("- Main paths are conservative 1.8 m center lanes derived from hallway, alley, and tunnel floor bounds; they are not a NavMesh substitute.");
        report.AppendLine("- Whiteboards, monitors, posters, boards, and utility pipes are treated as intentionally elevated.");
        report.AppendLine("- Wall-backed shelves/counters/cabinets produce review findings where contact may be intentional.");
        report.AppendLine();

        string[] categories =
        {
            "Buyable door clearance",
            "Doorway blocking",
            "Stair blocking",
            "Main walking path",
            "Wall clipping",
            "Wall proximity",
            "Floating prop",
            "Sunken prop",
            "Unrealistic scale",
            "Desk/chair facing",
            "Kitchen/cafeteria counter clearance",
            "Room association",
            "Unbounded props"
        };

        foreach (string category in categories)
        {
            report.AppendLine("## " + category);
            report.AppendLine();
            List<Finding> categoryFindings = findings
                .Where(finding => finding.Category == category)
                .OrderBy(finding => finding.Severity)
                .ThenBy(finding => finding.ObjectPath)
                .ToList();
            if (categoryFindings.Count == 0)
            {
                report.AppendLine("- No candidates detected.");
            }
            else
            {
                foreach (Finding finding in categoryFindings.Take(100))
                {
                    report.AppendLine($"- **{finding.Severity}** `{finding.ObjectPath}` at {Format(finding.Position)}: {finding.Message}");
                }
                if (categoryFindings.Count > 100)
                {
                    report.AppendLine($"- {categoryFindings.Count - 100} additional candidate(s) omitted; refine the scene or validator threshold before expanding output.");
                }
            }
            report.AppendLine();
        }

        report.AppendLine("## Manual Unity Review Required");
        report.AppendLine();
        report.AppendLine("- Confirm errors and warnings in Scene view before moving anything; axis-aligned renderer/collider bounds can over-report rotated or decorative geometry.");
        report.AppendLine("- Bake and inspect the NavMesh to prove main-path and stair traversal.");
        report.AppendLine("- Walk through every buyable door with the 0.3 m-radius player capsule.");
        report.AppendLine("- Check classroom sightlines from the player camera; furniture facing uses transform forward and whiteboard position only.");
        report.AppendLine("- Review counter front aisles in cafeteria/kitchen rooms even when no wall penetration is detected.");

        File.WriteAllText(ResultsPath, report.ToString());
    }

    private static List<Bounds> CollectStairBounds(List<MeshRenderer> renderers)
    {
        return renderers
            .Where(renderer =>
            {
                string name = renderer.name.ToLowerInvariant();
                bool stairPart = name.Contains("step") || name.Contains("stairs") || name.Contains("landing");
                bool geometry = !name.Contains("floor") && !name.Contains("transom") && !name.Contains("_seg");
                return stairPart && geometry;
            })
            .Select(renderer => renderer.bounds)
            .ToList();
    }

    private static List<KeyValuePair<string, Bounds>> BuildWalkingPaths(Dictionary<string, RoomRecord> rooms)
    {
        List<KeyValuePair<string, Bounds>> paths = new List<KeyValuePair<string, Bounds>>();
        foreach (RoomRecord room in rooms.Values.Where(room => IsCirculationRoom(room.Id)))
        {
            Bounds floor = room.Floor.bounds;
            const float laneWidth = 1.8f;
            Vector3 size = floor.size.x >= floor.size.z
                ? new Vector3(floor.size.x, 2f, Mathf.Min(laneWidth, floor.size.z))
                : new Vector3(Mathf.Min(laneWidth, floor.size.x), 2f, floor.size.z);
            Bounds lane = new Bounds(new Vector3(floor.center.x, floor.max.y + 1f, floor.center.z), size);
            paths.Add(new KeyValuePair<string, Bounds>(room.Id, lane));
        }
        return paths;
    }

    private static bool IsCirculationRoom(string roomId)
    {
        string value = roomId.ToLowerInvariant();
        return value.Contains("hallway") || value.Contains("alley") || value.Contains("tunnel");
    }

    private static bool IsStudentDeskOrChair(PropRecord prop)
    {
        string name = prop.Root.name;
        bool deskOrChair = name.StartsWith("Desk_", StringComparison.OrdinalIgnoreCase) ||
                           name.StartsWith("Chair_", StringComparison.OrdinalIgnoreCase);
        return deskOrChair && name.IndexOf("Teacher", StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static bool IsElevatedByDesign(string name)
    {
        string value = name.ToLowerInvariant();
        return value.Contains("whiteboard") || value.Contains("monitor") || value.Contains("poster") ||
               value.Contains("pipe") || value.Contains("wallboard") || value.Contains("screen");
    }

    private static bool IsWallBacked(string name)
    {
        string value = name.ToLowerInvariant();
        return IsElevatedByDesign(name) || value.Contains("shelf") || value.Contains("locker") ||
               value.Contains("cabinet") || value.Contains("counter") || value.Contains("bookcase");
    }

    private static GameObject FindRoot(Scene scene, string name)
    {
        return scene.GetRootGameObjects().FirstOrDefault(root => root.name == name);
    }

    private static bool HasRenderableOrCollider(Transform root)
    {
        return root.GetComponentInChildren<Renderer>(true) != null || root.GetComponentInChildren<Collider>(true) != null;
    }

    private static bool TryGetPropBounds(
        Transform root,
        out Bounds bounds,
        out Bounds visualBounds,
        out bool usesColliderBounds)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer.enabled)
            .ToArray();
        bool hasVisualBounds = renderers.Length > 0;
        visualBounds = hasVisualBounds ? renderers[0].bounds : default;
        foreach (Renderer renderer in renderers.Skip(1))
        {
            visualBounds.Encapsulate(renderer.bounds);
        }

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true)
            .Where(collider => collider.enabled && !collider.isTrigger)
            .ToArray();
        if (colliders.Length > 0)
        {
            bounds = colliders[0].bounds;
            foreach (Collider collider in colliders.Skip(1))
            {
                bounds.Encapsulate(collider.bounds);
            }
            usesColliderBounds = true;
            if (!hasVisualBounds)
            {
                visualBounds = bounds;
            }
            return true;
        }

        if (hasVisualBounds)
        {
            bounds = visualBounds;
            usesColliderBounds = false;
            return true;
        }

        bounds = default;
        visualBounds = default;
        usesColliderBounds = false;
        return false;
    }

    private static string FindContainingRoom(Vector3 point, Dictionary<string, RoomRecord> rooms)
    {
        return rooms.Values
            .Where(room => point.x >= room.Floor.bounds.min.x && point.x <= room.Floor.bounds.max.x &&
                           point.z >= room.Floor.bounds.min.z && point.z <= room.Floor.bounds.max.z)
            .OrderBy(room => Mathf.Abs(point.y - room.Floor.bounds.max.y))
            .Select(room => room.Id)
            .FirstOrDefault();
    }

    private static Bounds ExpandedHorizontal(Bounds bounds, float padding)
    {
        bounds.Expand(new Vector3(padding * 2f, 0f, padding * 2f));
        return bounds;
    }

    private static Vector3 IntersectionSize(Bounds first, Bounds second)
    {
        return new Vector3(
            Mathf.Max(0f, Mathf.Min(first.max.x, second.max.x) - Mathf.Max(first.min.x, second.min.x)),
            Mathf.Max(0f, Mathf.Min(first.max.y, second.max.y) - Mathf.Max(first.min.y, second.min.y)),
            Mathf.Max(0f, Mathf.Min(first.max.z, second.max.z) - Mathf.Max(first.min.z, second.min.z)));
    }

    private static float HorizontalBoundsGap(Bounds first, Bounds second)
    {
        float xGap = Mathf.Max(0f, Mathf.Max(first.min.x - second.max.x, second.min.x - first.max.x));
        float zGap = Mathf.Max(0f, Mathf.Max(first.min.z - second.max.z, second.min.z - first.max.z));
        return Mathf.Sqrt(xGap * xGap + zGap * zGap);
    }

    private static Finding NewFinding(Severity severity, string category, Transform target, string message)
    {
        return new Finding
        {
            Severity = severity,
            Category = category,
            ObjectPath = HierarchyPath(target),
            Message = message,
            Position = target.position
        };
    }

    private static Finding NewFinding(Severity severity, string category, PropRecord prop, string message)
    {
        return new Finding
        {
            Severity = severity,
            Category = category,
            ObjectPath = prop.Path,
            Message = message,
            Position = prop.Bounds.center
        };
    }

    private static string HierarchyPath(Transform transform)
    {
        List<string> parts = new List<string>();
        while (transform != null)
        {
            parts.Add(transform.name);
            transform = transform.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static string Format(Vector3 value)
    {
        return $"({value.x:0.00}, {value.y:0.00}, {value.z:0.00})";
    }
}
#endif
