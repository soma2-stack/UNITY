#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SchoolOfTheDeadSafePropAutoFix
{
    private const string ScenePath = "Assets/Scenes/SchoolOfTheDead.unity";
    private const float DoorClearance = 0.9f;
    private const float FloorSnapMinimum = 0.10f;
    private const float FloorSnapMaximum = 0.75f;
    private const float MaximumDoorMove = 1.25f;
    private const float MaximumWallMove = 0.15f;
    private const float WallClipMaximum = 0.08f;
    private const float SafetyMargin = 0.08f;

    private enum FixKind
    {
        FloorSnap,
        DoorClearance,
        WallClearance,
        ExtremeScale,
        DeskFacing
    }

    private sealed class RoomRecord
    {
        public string Id;
        public MeshRenderer Floor;
        public readonly List<MeshRenderer> Walls = new List<MeshRenderer>();
    }

    private sealed class PropRecord
    {
        public Transform Root;
        public RoomRecord Room;
        public Bounds PhysicalBounds;
        public Bounds VisualBounds;
    }

    private sealed class FixProposal
    {
        public FixKind Kind;
        public PropRecord Prop;
        public Vector3? Position;
        public Quaternion? Rotation;
        public Vector3? LocalScale;
        public string Reason;
    }

    [MenuItem("Tools/School Of The Dead/Safe Auto Fix Props")]
    public static void SafeAutoFixProps()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedForFix = !scene.IsValid() || !scene.isLoaded;

        try
        {
            if (openedForFix)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            }

            List<MeshRenderer> renderers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true))
                .ToList();
            Dictionary<string, RoomRecord> rooms = BuildRooms(renderers);
            List<PropRecord> props = CollectGeneratedRoomProps(scene, rooms);
            List<Bounds> doorZones = CollectDoorZones(scene);
            List<FixProposal> proposals = BuildProposals(props, doorZones);

            if (proposals.Count == 0)
            {
                Debug.Log("[SchoolOfTheDeadSafePropAutoFix] No clearly safe fixes were found. No scene changes were made.");
                if (!Application.isBatchMode)
                {
                    EditorUtility.DisplayDialog("Safe Auto Fix Props", "No clearly safe fixes were found. The scene was not changed.", "OK");
                }
                return;
            }

            string preview = BuildPreview(proposals);
            if (Application.isBatchMode || !EditorUtility.DisplayDialog(
                    "Safe Auto Fix Props",
                    preview + "\n\nOnly Generated_RoomProps objects will change. The operation supports Undo and reruns validation afterward.",
                    "Apply Safe Fixes",
                    "Cancel"))
            {
                Debug.Log("[SchoolOfTheDeadSafePropAutoFix] Cancelled before applying changes.\n" + preview);
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Safe Auto Fix School Props");
            foreach (FixProposal proposal in proposals)
            {
                Undo.RecordObject(proposal.Prop.Root, "Safe Auto Fix School Prop");
                if (proposal.Position.HasValue)
                {
                    proposal.Prop.Root.position = proposal.Position.Value;
                }
                if (proposal.Rotation.HasValue)
                {
                    proposal.Prop.Root.rotation = proposal.Rotation.Value;
                }
                if (proposal.LocalScale.HasValue)
                {
                    proposal.Prop.Root.localScale = proposal.LocalScale.Value;
                }
                EditorUtility.SetDirty(proposal.Prop.Root);
                Debug.Log($"[SchoolOfTheDeadSafePropAutoFix] {proposal.Kind}: {HierarchyPath(proposal.Prop.Root)} - {proposal.Reason}");
            }
            Undo.CollapseUndoOperations(undoGroup);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new InvalidOperationException("Unity could not save the gameplay scene after safe prop fixes.");
            }

            SchoolOfTheDeadPropPlacementValidator.ValidatePropPlacement();
            Debug.Log($"[SchoolOfTheDeadSafePropAutoFix] Applied {proposals.Count} safe fix(es) and refreshed validation results.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("Safe Auto Fix Failed", exception.Message, "OK");
            }
            throw;
        }
        finally
        {
            if (openedForFix && scene.IsValid() && scene.isLoaded)
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
        }
        return rooms;
    }

    private static List<PropRecord> CollectGeneratedRoomProps(Scene scene, Dictionary<string, RoomRecord> rooms)
    {
        GameObject generatedRoot = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Generated_RoomProps");
        List<PropRecord> props = new List<PropRecord>();
        if (generatedRoot == null)
        {
            return props;
        }

        for (int roomIndex = 0; roomIndex < generatedRoot.transform.childCount; roomIndex++)
        {
            Transform roomTransform = generatedRoot.transform.GetChild(roomIndex);
            if (!rooms.TryGetValue(roomTransform.name, out RoomRecord room))
            {
                continue;
            }

            for (int propIndex = 0; propIndex < roomTransform.childCount; propIndex++)
            {
                Transform prop = roomTransform.GetChild(propIndex);
                if (!TryGetBounds(prop, out Bounds physical, out Bounds visual))
                {
                    continue;
                }
                props.Add(new PropRecord
                {
                    Root = prop,
                    Room = room,
                    PhysicalBounds = physical,
                    VisualBounds = visual
                });
            }
        }
        return props;
    }

    private static List<Bounds> CollectDoorZones(Scene scene)
    {
        List<Bounds> zones = new List<Bounds>();
        foreach (Door door in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Door>(true)))
        {
            Collider collider = door.GetComponent<Collider>();
            Bounds bounds = collider != null
                ? collider.bounds
                : new Bounds(door.transform.position, new Vector3(1f, 2.2f, 0.2f));
            bounds.Expand(new Vector3(DoorClearance * 2f, 0.2f, DoorClearance * 2f));
            zones.Add(bounds);
        }
        return MergeNearDuplicateDoorZones(zones);
    }

    private static List<Bounds> MergeNearDuplicateDoorZones(List<Bounds> zones)
    {
        List<Bounds> merged = new List<Bounds>();
        foreach (Bounds zone in zones)
        {
            int existing = merged.FindIndex(candidate =>
                Mathf.Abs(candidate.center.x - zone.center.x) < 0.2f &&
                Mathf.Abs(candidate.center.z - zone.center.z) < 0.2f);
            if (existing < 0)
            {
                merged.Add(zone);
            }
            else
            {
                Bounds combined = merged[existing];
                combined.Encapsulate(zone);
                merged[existing] = combined;
            }
        }
        return merged;
    }

    private static List<FixProposal> BuildProposals(List<PropRecord> props, List<Bounds> doorZones)
    {
        List<FixProposal> proposals = new List<FixProposal>();
        HashSet<Transform> translated = new HashSet<Transform>();

        foreach (PropRecord prop in props)
        {
            List<Bounds> blockingDoors = doorZones.Where(zone => zone.Intersects(prop.PhysicalBounds)).ToList();
            if (blockingDoors.Count == 0)
            {
                continue;
            }

            if (TryFindDoorClearanceMove(prop, blockingDoors, doorZones, props, out Vector3 target))
            {
                Vector3 delta = target - prop.Root.position;
                proposals.Add(new FixProposal
                {
                    Kind = FixKind.DoorClearance,
                    Prop = prop,
                    Position = target,
                    Reason = $"Moved {Vector3.Distance(prop.Root.position, target):0.00} m out of a buyable-door clearance zone."
                });
                translated.Add(prop.Root);
                prop.PhysicalBounds = MoveBounds(prop.PhysicalBounds, delta);
                prop.VisualBounds = MoveBounds(prop.VisualBounds, delta);
            }
        }

        foreach (PropRecord prop in props.Where(prop => !translated.Contains(prop.Root) && IsFloorStanding(prop.Root.name)))
        {
            float rootDelta = prop.Root.position.y - prop.Room.Floor.bounds.max.y;
            if (rootDelta >= FloorSnapMinimum && rootDelta <= FloorSnapMaximum)
            {
                Vector3 target = prop.Root.position - Vector3.up * rootDelta;
                Bounds moved = MoveBounds(prop.PhysicalBounds, target - prop.Root.position);
                if (IsInsideRoom(moved, prop.Room) && !IntersectsAnyDoor(moved, doorZones))
                {
                    proposals.Add(new FixProposal
                    {
                        Kind = FixKind.FloorSnap,
                        Prop = prop,
                        Position = target,
                        Reason = $"Snapped root down {rootDelta:0.00} m to `{prop.Room.Floor.name}`."
                    });
                    translated.Add(prop.Root);
                    prop.PhysicalBounds = MoveBounds(prop.PhysicalBounds, target - prop.Root.position);
                    prop.VisualBounds = MoveBounds(prop.VisualBounds, target - prop.Root.position);
                }
            }
        }

        foreach (PropRecord prop in props.Where(prop => !translated.Contains(prop.Root) && !IsWallBacked(prop.Root.name)))
        {
            if (TryFindSmallWallMove(prop, doorZones, props, out Vector3 target))
            {
                proposals.Add(new FixProposal
                {
                    Kind = FixKind.WallClearance,
                    Prop = prop,
                    Position = target,
                    Reason = $"Moved {Vector3.Distance(prop.Root.position, target):0.00} m away from a shallow wall penetration."
                });
                translated.Add(prop.Root);
                prop.PhysicalBounds = MoveBounds(prop.PhysicalBounds, target - prop.Root.position);
                prop.VisualBounds = MoveBounds(prop.VisualBounds, target - prop.Root.position);
            }
        }

        foreach (PropRecord prop in props)
        {
            if (TryCorrectSingleExtremeScaleAxis(prop.Root.localScale, out Vector3 corrected))
            {
                proposals.Add(new FixProposal
                {
                    Kind = FixKind.ExtremeScale,
                    Prop = prop,
                    LocalScale = corrected,
                    Reason = $"Corrected clearly extreme local scale {Format(prop.Root.localScale)} to {Format(corrected)}."
                });
            }
        }

        foreach (IGrouping<RoomRecord, PropRecord> roomGroup in props.GroupBy(prop => prop.Room))
        {
            List<PropRecord> whiteboards = roomGroup
                .Where(prop => prop.Root.name.IndexOf("Whiteboard", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            if (whiteboards.Count != 1)
            {
                continue;
            }

            Vector3 expected = whiteboards[0].VisualBounds.center - roomGroup.Key.Floor.bounds.center;
            expected.y = 0f;
            if (expected.sqrMagnitude < 0.25f)
            {
                continue;
            }
            expected.Normalize();

            foreach (PropRecord desk in roomGroup.Where(prop =>
                         prop.Root.name.StartsWith("Desk_", StringComparison.OrdinalIgnoreCase) &&
                         prop.Root.name.IndexOf("Teacher", StringComparison.OrdinalIgnoreCase) < 0))
            {
                Vector3 current = desk.Root.forward;
                current.y = 0f;
                Quaternion targetRotation = Quaternion.LookRotation(expected, Vector3.up);
                if (current.sqrMagnitude > 0.01f &&
                    Vector3.Dot(current.normalized, expected) < 0.5f &&
                    RotationRemainsSafe(desk, targetRotation, doorZones, props))
                {
                    proposals.Add(new FixProposal
                    {
                        Kind = FixKind.DeskFacing,
                        Prop = desk,
                        Rotation = targetRotation,
                        Reason = $"Rotated toward the room's single whiteboard marker `{whiteboards[0].Root.name}`."
                    });
                }
            }
        }

        return proposals;
    }

    private static bool TryFindDoorClearanceMove(
        PropRecord prop,
        List<Bounds> blockingDoors,
        List<Bounds> allDoors,
        List<PropRecord> allProps,
        out Vector3 target)
    {
        List<Vector3> candidates = new List<Vector3>();
        foreach (Vector3 direction in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
        {
            float distance = 0f;
            foreach (Bounds door in blockingDoors)
            {
                float required;
                if (direction == Vector3.right) required = door.max.x - prop.PhysicalBounds.min.x + SafetyMargin;
                else if (direction == Vector3.left) required = prop.PhysicalBounds.max.x - door.min.x + SafetyMargin;
                else if (direction == Vector3.forward) required = door.max.z - prop.PhysicalBounds.min.z + SafetyMargin;
                else required = prop.PhysicalBounds.max.z - door.min.z + SafetyMargin;
                distance = Mathf.Max(distance, required);
            }

            if (distance > 0f && distance <= MaximumDoorMove)
            {
                candidates.Add(direction * distance);
            }
        }

        Vector3 roomCenter = prop.Room.Floor.bounds.center;
        foreach (Vector3 delta in candidates
                     .OrderBy(delta => delta.magnitude)
                     .ThenByDescending(delta => Vector3.Dot(delta.normalized, (roomCenter - prop.PhysicalBounds.center).normalized)))
        {
            Bounds moved = MoveBounds(prop.PhysicalBounds, delta);
            Bounds movedVisual = MoveBounds(prop.VisualBounds, delta);
            if (IsInsideRoom(movedVisual, prop.Room) &&
                !IntersectsAnyDoor(moved, allDoors) &&
                !IntersectsWalls(movedVisual, prop.Room.Walls) &&
                !IntersectsOtherProps(moved, prop, allProps))
            {
                target = prop.Root.position + delta;
                return true;
            }
        }

        target = default;
        return false;
    }

    private static bool TryFindSmallWallMove(
        PropRecord prop,
        List<Bounds> doorZones,
        List<PropRecord> allProps,
        out Vector3 target)
    {
        foreach (MeshRenderer wall in prop.Room.Walls)
        {
            if (!prop.VisualBounds.Intersects(wall.bounds))
            {
                continue;
            }

            Vector3 overlap = IntersectionSize(prop.VisualBounds, wall.bounds);
            float depth = Mathf.Min(overlap.x, overlap.z);
            if (depth <= 0f || depth > WallClipMaximum)
            {
                continue;
            }

            Vector3 towardInterior = prop.Room.Floor.bounds.center - wall.bounds.center;
            towardInterior.y = 0f;
            Vector3 direction = wall.bounds.size.x <= wall.bounds.size.z
                ? new Vector3(Mathf.Sign(towardInterior.x), 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(towardInterior.z));
            if (direction.sqrMagnitude < 0.5f)
            {
                continue;
            }

            Vector3 delta = direction * Mathf.Min(MaximumWallMove, depth + 0.03f);
            Bounds moved = MoveBounds(prop.PhysicalBounds, delta);
            Bounds movedVisual = MoveBounds(prop.VisualBounds, delta);
            if (IsInsideRoom(movedVisual, prop.Room) &&
                !IntersectsAnyDoor(moved, doorZones) &&
                !IntersectsWalls(movedVisual, prop.Room.Walls) &&
                !IntersectsOtherProps(moved, prop, allProps))
            {
                target = prop.Root.position + delta;
                return true;
            }
        }

        target = default;
        return false;
    }

    private static bool TryCorrectSingleExtremeScaleAxis(Vector3 scale, out Vector3 corrected)
    {
        float[] values = { Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z) };
        int extremeCount = values.Count(value => value > 20f);
        if (extremeCount != 1)
        {
            corrected = scale;
            return false;
        }

        List<float> normal = values.Where(value => value >= 0.1f && value <= 10f).OrderBy(value => value).ToList();
        if (normal.Count != 2 || normal[1] / normal[0] > 4f)
        {
            corrected = scale;
            return false;
        }

        float replacement = (normal[0] + normal[1]) * 0.5f;
        corrected = scale;
        if (values[0] > 20f) corrected.x = replacement;
        else if (values[1] > 20f) corrected.y = replacement;
        else corrected.z = replacement;
        return true;
    }

    private static bool IsFloorStanding(string name)
    {
        string value = name.ToLowerInvariant();
        if (IsWallBacked(name))
        {
            return false;
        }
        return value.Contains("desk") || value.Contains("chair") || value.Contains("table") ||
               value.Contains("bench") || value.Contains("planter") || value.Contains("crate") ||
               value.Contains("barrel") || value.Contains("cart") || value.Contains("generator") ||
               value.Contains("bed") || value.Contains("trash") || value.Contains("stool");
    }

    private static bool IsWallBacked(string name)
    {
        string value = name.ToLowerInvariant();
        return value.Contains("whiteboard") || value.Contains("monitor") || value.Contains("poster") ||
               value.Contains("pipe") || value.Contains("screen") || value.Contains("shelf") ||
               value.Contains("locker") || value.Contains("cabinet") || value.Contains("counter") ||
               value.Contains("bookcase");
    }

    private static bool TryGetBounds(Transform root, out Bounds physical, out Bounds visual)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true).Where(renderer => renderer.enabled).ToArray();
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true)
            .Where(collider => collider.enabled && !collider.isTrigger)
            .ToArray();

        if (renderers.Length == 0 && colliders.Length == 0)
        {
            physical = default;
            visual = default;
            return false;
        }

        visual = renderers.Length > 0 ? renderers[0].bounds : colliders[0].bounds;
        foreach (Renderer renderer in renderers.Skip(1)) visual.Encapsulate(renderer.bounds);
        physical = colliders.Length > 0 ? colliders[0].bounds : visual;
        foreach (Collider collider in colliders.Skip(1)) physical.Encapsulate(collider.bounds);
        return true;
    }

    private static bool IsInsideRoom(Bounds bounds, RoomRecord room)
    {
        Bounds floor = room.Floor.bounds;
        return bounds.min.x >= floor.min.x + SafetyMargin && bounds.max.x <= floor.max.x - SafetyMargin &&
               bounds.min.z >= floor.min.z + SafetyMargin && bounds.max.z <= floor.max.z - SafetyMargin;
    }

    private static bool IntersectsAnyDoor(Bounds bounds, List<Bounds> doors)
    {
        return doors.Any(door => door.Intersects(bounds));
    }

    private static bool IntersectsWalls(Bounds bounds, List<MeshRenderer> walls)
    {
        return walls.Any(wall => wall.bounds.Intersects(bounds));
    }

    private static bool IntersectsOtherProps(Bounds bounds, PropRecord self, List<PropRecord> props)
    {
        Bounds contracted = bounds;
        contracted.Expand(-0.02f);
        return props.Any(other => other != self && other.Room == self.Room && contracted.Intersects(other.PhysicalBounds));
    }

    private static bool RotationRemainsSafe(
        PropRecord prop,
        Quaternion targetRotation,
        List<Bounds> doorZones,
        List<PropRecord> props)
    {
        float angle = Quaternion.Angle(prop.Root.rotation, targetRotation);
        Bounds rotatedPhysical = prop.PhysicalBounds;
        Bounds rotatedVisual = prop.VisualBounds;
        if (Mathf.Abs(angle - 90f) < 10f)
        {
            rotatedPhysical.size = new Vector3(prop.PhysicalBounds.size.z, prop.PhysicalBounds.size.y, prop.PhysicalBounds.size.x);
            rotatedVisual.size = new Vector3(prop.VisualBounds.size.z, prop.VisualBounds.size.y, prop.VisualBounds.size.x);
        }

        return IsInsideRoom(rotatedVisual, prop.Room) &&
               !IntersectsAnyDoor(rotatedPhysical, doorZones) &&
               !IntersectsWalls(rotatedVisual, prop.Room.Walls) &&
               !IntersectsOtherProps(rotatedPhysical, prop, props);
    }

    private static Bounds MoveBounds(Bounds bounds, Vector3 delta)
    {
        bounds.center += delta;
        return bounds;
    }

    private static Vector3 IntersectionSize(Bounds first, Bounds second)
    {
        return new Vector3(
            Mathf.Max(0f, Mathf.Min(first.max.x, second.max.x) - Mathf.Max(first.min.x, second.min.x)),
            Mathf.Max(0f, Mathf.Min(first.max.y, second.max.y) - Mathf.Max(first.min.y, second.min.y)),
            Mathf.Max(0f, Mathf.Min(first.max.z, second.max.z) - Mathf.Max(first.min.z, second.min.z)));
    }

    private static string BuildPreview(List<FixProposal> proposals)
    {
        StringBuilder preview = new StringBuilder();
        preview.AppendLine($"Found {proposals.Count} clearly safe candidate fix(es):");
        foreach (FixKind kind in Enum.GetValues(typeof(FixKind)))
        {
            int count = proposals.Count(proposal => proposal.Kind == kind);
            if (count > 0)
            {
                preview.AppendLine($"- {kind}: {count}");
            }
        }
        return preview.ToString();
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
        return $"({value.x:0.###}, {value.y:0.###}, {value.z:0.###})";
    }
}
#endif
