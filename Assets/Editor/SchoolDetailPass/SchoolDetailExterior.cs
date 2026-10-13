using System.Collections.Generic;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Exterior dressing for the School of the Dead detail pass (parking lot, courtyards, staff alleys).</summary>
public static partial class SchoolDetailKit
{
    /// <summary>Keeps building entrances clear from the outside: a 3.5 m deep box in front of every doorway that opens onto this exterior area.</summary>
    static void AddExteriorDoorKeepouts(Room ext)
    {
        foreach (var room in rooms.Values)
        {
            if (room == ext || room.level == 9) continue;
            foreach (var o in room.openings)
            {
                Vector3 outside = room.WallPoint(o.side, o.Mid, -1.0f);
                if (!ext.rect.Contains(new Vector2(outside.x, outside.z)) || Mathf.Abs(room.floorY - ext.floorY) > 1.5f) continue;
                Vector3 a = room.WallPoint(o.side, o.from - 0.8f, -0.2f), b = room.WallPoint(o.side, o.to + 0.8f, -3.7f);
                var bb = new Bounds((a + b) * 0.5f, Vector3.zero); bb.Encapsulate(a); bb.Encapsulate(b);
                bb.min = new Vector3(bb.min.x, ext.floorY, bb.min.z); bb.max = new Vector3(bb.max.x, ext.floorY + 2.6f, bb.max.z);
                ext.doorKeepouts.Add(bb);
            }
        }
    }

    static Light StreetLight(Room r, GameObject lamp, bool flicker)
    {
        Bounds b = WorldBounds(lamp);
        // the lamp head overhangs toward local +Z (front); find the highest point on that side
        Vector3 head = lamp.transform.position + lamp.transform.forward * 1.3f;
        var lgo = new GameObject("Light");
        lgo.transform.SetParent(lamp.transform, true);
        lgo.transform.position = new Vector3(head.x, b.max.y - 0.5f, head.z);
        var l = lgo.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(1f, 0.64f, 0.32f); l.intensity = 2.6f; l.range = 17f;
        l.shadows = LightShadows.None; l.lightmapBakeType = LightmapBakeType.Realtime;
        if (flicker) { var fl = lgo.AddComponent<SOTDFlickerLight>(); fl.severity = 0.6f; fl.burstInterval = 5f; }
        return l;
    }

    static Room MakeExteriorRoom(string id, string display, Rect rect)
    {
        var r = new Room { id = id, display = display, rect = rect, floorY = 0f, ceilY = 40f, level = 9, rng = new System.Random(StableHash(id)) };
        rooms[id] = r;
        return r;
    }

    public static string BuildExterior()
    {
        AnalyzeRooms();
        sceneRenderers = null;
        var log = new List<string>();

        // ---------------- parking lot & front of school
        Room lot = MakeExteriorRoom("parking_lot", "Parking Lot", Rect.MinMaxRect(-75f, -70.7f, 17f, -47.6f));
        RoomGroup(lot, clear: true);
        AddExteriorDoorKeepouts(lot);
        AddExistingObstacles(lot);
        AddGlobalKeepoutsFor(lot);

        // street lamps down the far side of the lot (a few dead, one flickering)
        int lit = 0;
        for (float x = -70f; x <= 14f; x += 21f)
        {
            var lamp = Floor(lot, "StreetLamp", new Vector3(x, 0, -69.9f), 0f, name: "StreetLamp");
            if (lamp == null) continue;
            if (lot.Chance(0.75f)) { StreetLight(lot, lamp, lot.Chance(0.3f)); lit++; }
        }
        // evacuation checkpoint in front of both entrances: barriers funnel toward the doors, which stay clear
        foreach (float doorX in new[] { -52.6f, -16.5f })
        {
            Floor(lot, "JerseyBarrier", new Vector3(doorX - 4.6f, 0, -52.6f), 90f + lot.R(-6, 6));
            Floor(lot, "JerseyBarrier", new Vector3(doorX + 4.6f, 0, -52.6f), 90f + lot.R(-6, 6));
            Floor(lot, "SawhorseBarrier", new Vector3(doorX - 3.2f, 0, -55.6f), lot.R(-10, 10));
            Floor(lot, "SawhorseBarrier", new Vector3(doorX + 3.4f, 0, -55.9f), lot.R(-10, 10), new Vector3(0, 0, lot.Chance(0.5f) ? 90 : 0));
            Floor(lot, "SandbagWall", new Vector3(doorX - 6.8f, 0, -50.2f), 90f);
            Floor(lot, "SandbagWall", new Vector3(doorX + 6.8f, 0, -50.2f), 90f);
            for (int i = 0; i < 5; i++)
                Floor(lot, "TrafficCone", new Vector3(doorX + lot.R(-6f, 6f), 0, lot.R(-58f, -54f)), lot.R(0, 360), lot.Chance(0.3f) ? new Vector3(90, 0, 0) : default);
        }
        // abandoned vehicles left at odd angles in the aisle
        Floor(lot, "carRed", new Vector3(-31f, 0, -59f), 28f, name: "AbandonedCar");
        Floor(lot, "carGray", new Vector3(6f, 0, -60f), -64f, name: "AbandonedCar");
        Floor(lot, "carBlue", new Vector3(-47f, 0, -59.5f), 104f, name: "AbandonedCar");
        // furniture along the building front
        foreach (float x in new[] { -70f, -42f, -30f, -6f, 8f })
            Floor(lot, lot.Chance(0.5f) ? "ParkBench" : "trashCan", new Vector3(x, 0, -48.6f), 180f + lot.R(-4, 4));
        Floor(lot, "BikeRack", new Vector3(-36f, 0, -49f), 0f);
        Floor(lot, "Dumpster", new Vector3(12.5f, 0, -49.4f), 180f);
                // chain-link fence along the lot's existing south boundary wall (it already blocks movement; this just shows why)
        for (float x = -68.5f; x <= 12f; x += 13.85f)
            Place(lot, "fenceLong", new Vector3(x, 0, -70.55f), 0f, new Opt { ignorePhysics = true, ignorePlaced = true, name = "BoundaryFence" });
        // debris of a panicked evacuation
        Scatter(lot, new[] { "papers", "papers", "TrashBag_A", "TrashBag_B", "backpack", "CardboardBox_M", "TrafficCone", "PlankPile" }, 40, margin: 1f, flat: false);
        Scatter(lot, new[] { "BloodPool_A", "BloodPool_B" }, 4);
        log.Add($"parking_lot: props={lot.placedCount} rejected={lot.rejected} lights={lit}");

        // ---------------- courtyards
        foreach (string id in new[] { "courtyard", "courtyard_east", "courtyard_west" })
        {
            if (!rooms.TryGetValue(id, out Room c)) continue;
            RoomGroup(c, clear: true);
            AddExteriorDoorKeepouts(c);
            AddExistingObstacles(c);
            if (id == "courtyard")
            {
                Vector3 fountain = new Vector3(-32.2f, 0, 27.1f);
                for (int i = 0; i < 4; i++)
                {
                    float a = i * 90f + 45f;
                    Floor(c, "ParkBench", fountain + DirOf(a) * 5.2f, a + 180f + c.R(-6, 6));
                }
                Floor(c, "PicnicTable", new Vector3(-37f, 0, 20f), c.R(-10, 10));
                Floor(c, "PicnicTable", new Vector3(-27f, 0, 38f), 90f + c.R(-10, 10), c.Chance(0.4f) ? new Vector3(0, 0, 90) : default);
            }
            else
            {
                Floor(c, "PicnicTable", c.Center + new Vector3(0, 0, c.R(-6f, 6f)), c.R(0, 30));
                Floor(c, "ParkBench", c.Center + new Vector3(c.R(-2.5f, 2.5f), 0, 8f), c.R(0, 360));
            }
            Scatter(c, new[] { "trashCan", "papers", "backpack", "TrashBag_A", "bush5", "CardboardBox_S" }, 8, margin: 1.2f, flat: false);
            log.Add($"{id}: props={c.placedCount} rejected={c.rejected}");
        }

        // ---------------- staff entrance alleys: service clutter
        foreach (string id in new[] { "staff_entrance_alley", "staff_entrance_alley_north" })
        {
            if (!rooms.TryGetValue(id, out Room a)) continue;
            RoomGroup(a, clear: true);
            AddExteriorDoorKeepouts(a);
            AddExistingObstacles(a);
            bool longX = a.rect.width > a.rect.height;
            Side wall = longX ? Side.S : Side.E;
            WallRun(a, wall, new[] { "Dumpster", "Pallet", "OilDrum_Rust", "Dumpster", "crate", "OilDrum_Blue" }, 2.6f, pad: 1.0f, max: 8, chanceSkip: 0.2f);
            Scatter(a, new[] { "TrashBag_A", "TrashBag_B", "CardboardBox_L", "PlankPile", "papers", "TrafficCone" }, 12, margin: 0.8f, flat: false);
            var lamp = Floor(a, "StreetLamp", new Vector3(a.rect.center.x, 0, a.rect.center.y), longX ? 0f : 90f, name: "AlleyLamp");
            if (lamp != null) StreetLight(a, lamp, true);
            log.Add($"{id}: props={a.placedCount} rejected={a.rejected}");
        }
        EditorSceneManager.MarkSceneDirty(lot.group.gameObject.scene);
        return string.Join("\n", log);
    }

    static void AddGlobalKeepoutsFor(Room r)
    {
        // reuse the interactable / spawn keep-outs computed for interior rooms that fall inside this exterior area
        foreach (var other in rooms.Values)
            if (other != r) foreach (var k in other.keepouts)
                if (r.rect.Contains(new Vector2(k.center.x, k.center.z)) && !r.keepouts.Contains(k)) r.keepouts.Add(k);
    }
}
