using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Room-type layouts for the School of the Dead detail pass (see SchoolDetailKit for placement rules).</summary>
public static partial class SchoolDetailKit
{
    // ------------------------------------------------------------------ room frame (front = teaching wall)

    public struct Frame
    {
        public Room r; public Side front; public float W, D;
        public Side Back => Opp(front);
        public Side Right => (Side)(((int)front + 1) % 4);
        public Side Left => (Side)(((int)front + 3) % 4);
        public float YawFront => front switch { Side.N => 0f, Side.S => 180f, Side.E => 90f, _ => 270f };
        /// <summary>World floor point: s across the room (0 = left wall, W = right wall), t from back (0) to front wall (D).</summary>
        public Vector3 L(float s, float t)
        {
            Rect q = r.rect;
            return front switch
            {
                Side.N => new Vector3(q.xMin + s, r.floorY, q.yMin + t),
                Side.S => new Vector3(q.xMax - s, r.floorY, q.yMax - t),
                Side.E => new Vector3(q.xMin + t, r.floorY, q.yMax - s),
                _ => new Vector3(q.xMax - t, r.floorY, q.yMin + s),
            };
        }
    }

    static Side Opp(Side s) => (Side)(((int)s + 2) % 4);
    static float AlongOf(Side wall, Vector3 p) => wall == Side.N || wall == Side.S ? p.x : p.z;

    static Frame MakeFrame(Room r, Side front)
    {
        var f = new Frame { r = r, front = front };
        f.W = r.Len(front);
        f.D = front == Side.N || front == Side.S ? r.rect.height : r.rect.width;
        return f;
    }

    /// <summary>Teaching wall: a long-enough wall without doorways, preferably opposite the entrance.</summary>
    static Side PickFrontWall(Room r, bool preferLong = false)
    {
        Side best = Side.N; float bestScore = float.NegativeInfinity;
        foreach (Side s in Enum.GetValues(typeof(Side)))
        {
            float score = -r.OpeningWidth(s) * 10f + (r.OpeningWidth(Opp(s)) > 0 ? 4f : 0f) + (preferLong ? r.Len(s) * 0.3f : -Mathf.Abs(r.Len(s) - 9f) * 0.2f);
            if (r.Len(s) < 4f) score -= 50f;
            if (score > bestScore) { bestScore = score; best = s; }
        }
        return best;
    }

    // ------------------------------------------------------------------ generic helpers

    static GameObject WallItem(Room r, Side wall, float along, string key, float? centerY = null, float? bottomY = null, string name = null, bool allowOverOpening = false)
    {
        var o = new Opt { wall = wall, centerY = centerY, y = bottomY, name = name };
        if (allowOverOpening) o.ignoreKeepouts = true;
        if (allowOverOpening)
        {
            // temporarily hide openings on this wall (exit signs above doorways)
            var saved = r.openings.Where(x => x.side == wall).ToList();
            r.openings.RemoveAll(x => x.side == wall);
            var go = Place(r, key, r.WallPoint(wall, along, 0.4f), Room.YawFacingInto(wall), o);
            r.openings.AddRange(saved);
            return go;
        }
        return Place(r, key, r.WallPoint(wall, along, 0.4f), Room.YawFacingInto(wall), o);
    }

    static GameObject Floor(Room r, string key, Vector3 p, float yaw, Vector3 tilt = default, string name = null) =>
        Place(r, key, p, yaw, new Opt { tilt = tilt, name = name });

    /// <summary>Places a run of props flush against a wall, skipping doorways; keys cycle through the list.</summary>
    static int WallRun(Room r, Side wall, IList<string> keys, float step, float pad = 0.5f, int max = 999, float a0 = float.NaN, float a1 = float.NaN, float jitterYaw = 0f, float chanceSkip = 0f)
    {
        float from = float.IsNaN(a0) ? r.AlongMin(wall) + pad : a0;
        float to = float.IsNaN(a1) ? r.AlongMax(wall) - pad : a1;
        int n = 0, i = 0;
        for (float a = from + step * 0.5f; a <= to - step * 0.5f + 0.001f && n < max; a += step, i++)
        {
            if (chanceSkip > 0 && r.Chance(chanceSkip)) continue;
            string key = keys[i % keys.Count];
            var go = Place(r, key, r.WallPoint(wall, a, 0.5f), Room.YawFacingInto(wall) + (jitterYaw > 0 ? r.R(-jitterYaw, jitterYaw) : 0f), new Opt { wall = wall });
            if (go != null) n++;
        }
        return n;
    }

    /// <summary>Puts a small item on top of a host prop (desk, shelf, counter).</summary>
    static GameObject OnTop(Room r, GameObject host, string key, float dx, float dz, float yaw)
    {
        if (host == null) return null;
        Bounds hb = WorldBounds(host);
        var p = new Vector3(hb.center.x + dx, 0, hb.center.z + dz);
        float y = SurfaceAt(r, p, hb.max.y + 0.05f);
        if (y < hb.center.y) return null; // missed the surface
        return Place(r, key, p, yaw, new Opt { y = y, ignorePlaced = true, scale = key == "papers" ? 0.42f : 1f });
    }

    static Vector3 RandomPoint(Room r, float margin)
    {
        return new Vector3(r.R(r.rect.xMin + margin, r.rect.xMax - margin), r.floorY, r.R(r.rect.yMin + margin, r.rect.yMax - margin));
    }

    /// <summary>Floor clutter: tries "count" placements of random keys at random spots.</summary>
    static int Scatter(Room r, IList<string> keys, int count, float margin = 0.6f, bool flat = true, int tries = 3)
    {
        int n = 0;
        for (int i = 0; i < count; i++)
            for (int k = 0; k < tries; k++)
            {
                var o = new Opt { ignorePlaced = flat };
                if (Place(r, r.Pick(keys), RandomPoint(r, margin), r.R(0, 360), o) != null) { n++; break; }
            }
        return n;
    }

    static readonly string[] Posters = { "posterA", "posterB", "posterC", "posterD", "posterE" };
    static readonly string[] FloorPaper = { "papers", "papers", "notebooks", "papers" };
    static readonly string[] Lockers = { "lockerGray", "lockerGreen", "lockerRusty" };

    static void SideWallDecor(Room r, Side wall, float spacing, float centerY, IList<string> keys, float chance = 0.75f)
    {
        float a0 = r.AlongMin(wall) + 1.2f, a1 = r.AlongMax(wall) - 1.2f;
        int i = r.rng.Next(keys.Count);
        for (float a = a0; a <= a1; a += spacing, i++)
            if (r.Chance(chance)) WallItem(r, wall, a + r.R(-0.3f, 0.3f), keys[i % keys.Count], centerY: r.floorY + centerY);
    }

    /// <summary>A trash can just inside the room next to a doorway (outside the doorway clearance zone).</summary>
    static void TrashByDoor(Room r)
    {
        foreach (var o in r.openings.OrderBy(x => r.rng.Next()).Take(1))
        {
            Side wall = o.side;
            foreach (float a in new[] { o.to + 0.9f, o.from - 0.9f, o.to + 1.6f, o.from - 1.6f })
                if (Place(r, "trashCan", r.WallPoint(wall, a, 0.45f), r.R(0, 360)) != null) return;
        }
    }

    static void Blood(Room r, float chance)
    {
        if (!r.Chance(chance)) return;
        Place(r, r.Chance(0.5f) ? "BloodPool_A" : "BloodPool_B", RandomPoint(r, 1.0f), r.R(0, 360), new Opt { ignorePlaced = true });
        if (r.Chance(0.5f))
        {
            Side w = (Side)r.rng.Next(4);
            WallItem(r, w, r.R(r.AlongMin(w) + 1f, r.AlongMax(w) - 1f), "BloodSmearWall", bottomY: r.floorY + 0.55f);
        }
    }

    static void CeilingDamage(Room r, float chance)
    {
        if (r.Chance(chance)) Place(r, "FallenCeilingTiles", RandomPoint(r, 1.0f), r.R(0, 360), new Opt { ignorePlaced = true });
    }

    /// <summary>Room-number plaque with a 3D TextMeshPro label, beside a doorway on the corridor side.</summary>
    static void DoorSign(Room corridor, Opening o, string text)
    {
        foreach (float a in new[] { o.to + 0.45f, o.from - 0.45f })
        {
            var go = WallItem(corridor, o.side, a, "RoomSign", centerY: corridor.floorY + 1.62f, name: "RoomSign_" + text);
            if (go == null) continue;
            var lbl = new GameObject("Label");
            lbl.transform.SetParent(go.transform, false);
            var tmp = lbl.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = 0.9f;
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.3f; tmp.fontSizeMax = 1.0f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.92f, 0.9f, 0.82f);
            tmp.rectTransform.sizeDelta = new Vector2(0.3f, 0.1f);
            // plaque front is local +Z; TMP faces -Z, so turn it around and sit it just proud of the face
            Bounds b = WorldBounds(go);
            Vector3 inward = corridor.Inward(o.side);
            lbl.transform.position = new Vector3(b.center.x, b.center.y + 0.015f, b.center.z) + inward * (Mathf.Abs(Vector3.Dot(b.extents, inward)) + 0.003f);
            lbl.transform.rotation = Quaternion.LookRotation(-inward, Vector3.up);
            lbl.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return;
        }
    }

    // ------------------------------------------------------------------ classrooms

    public enum ClassVariant { General, History, Math, English, Science, Computer, Art, Music }

    static void Classroom(Room r, ClassVariant v)
    {
        Side F = PickFrontWall(r);
        var f = MakeFrame(r, F);
        float yawF = f.YawFront;

        // teaching wall
        string boardKey = v == ClassVariant.History || r.Chance(0.4f) ? "chalkboard" : "Whiteboard";
        GameObject board = null;
        foreach (float s in new[] { 0.5f, 0.42f, 0.58f, 0.35f, 0.65f })
        {
            board = WallItem(r, F, AlongOf(F, f.L(f.W * s, f.D)), boardKey, centerY: r.floorY + 1.55f, name: "TeachingBoard");
            if (board != null) break;
        }
        if (v == ClassVariant.Math && f.W > 9f) WallItem(r, F, AlongOf(F, f.L(f.W * 0.85f, f.D)), "Whiteboard", centerY: r.floorY + 1.55f);
        if (f.W > 8.5f && v != ClassVariant.Math) WallItem(r, F, AlongOf(F, f.L(f.W * 0.86f, f.D)), "ProjectorScreen", bottomY: r.floorY + 1.5f);
        WallItem(r, F, AlongOf(F, f.L(f.W * 0.14f, f.D)), "clock", centerY: r.floorY + 3.0f);
        WallItem(r, F, AlongOf(F, f.L(f.W * 0.93f, f.D)), "speaker", centerY: r.floorY + 3.3f);

        // teacher's desk & chair
        float sDesk = f.W * (r.Chance(0.5f) ? 0.27f : 0.73f);
        GameObject desk = null;
        if (v != ClassVariant.Music)
        {
            desk = Floor(r, "teacherDesk", f.L(sDesk, f.D - 1.75f), yawF + 180f + r.R(-4, 4), name: "TeacherDesk");
            if (desk == null) desk = Floor(r, "teacherDesk", f.L(f.W - sDesk, f.D - 1.75f), yawF + 180f, name: "TeacherDesk");
            if (desk != null)
            {
                Bounds db = WorldBounds(desk);
                Vector3 behind = new Vector3(db.center.x, r.floorY, db.center.z) + DirOf(yawF) * 0.95f;
                Floor(r, "swivelChair", behind, yawF + 180f + r.R(-40, 40));
                OnTop(r, desk, r.Chance(0.5f) ? "monitor" : "laptop", 0.25f, 0f, yawF + 180f);
                OnTop(r, desk, "book", -0.45f, 0.1f, r.R(0, 360));
                OnTop(r, desk, "papers", 0f, 0.05f, r.R(0, 360));
                if (v == ClassVariant.History || v == ClassVariant.Science) OnTop(r, desk, "globe", -0.2f, -0.1f, r.R(0, 360));
            }
        }

        // student seating
        switch (v)
        {
            case ClassVariant.Science: Rows(r, f, "labBench", 3.1f, 3.0f, 3.6f, 0.06f, 0.05f, stool: true, maxRows: 4, maxCols: 5); break;
            case ClassVariant.Computer: ComputerRows(r, f); break;
            case ClassVariant.Art: ArtTables(r, f); break;
            case ClassVariant.Music: MusicArcs(r, f); break;
            default: Rows(r, f, "studentDesk", 2.0f, 2.35f, 3.4f, 0.1f, 0.1f, maxRows: 5); break;
        }

        // back wall storage
        var backKeys = v switch
        {
            ClassVariant.English => new[] { "shelfBooks", "shelfBooks", "CubbyShelf", "shelfBooks" },
            ClassVariant.Science => new[] { "MetalShelf_Stocked", "filingCabinet", "shelfWood", "MetalShelf" },
            ClassVariant.Art => new[] { "CubbyShelf", "MetalShelf_Stocked", "CubbyShelf", "Easel" },
            ClassVariant.Music => new[] { "shelfWood", "CubbyShelf", "UprightPiano", "shelfWood" },
            ClassVariant.Computer => new[] { "filingCabinet", "MetalShelf_Stocked", "filingCabinet" },
            _ => new[] { "shelfBooks", "CubbyShelf", "filingCabinet", "shelfWood" },
        };
        WallRun(r, f.Back, backKeys, 2.4f, pad: 0.6f, max: Mathf.Clamp((int)(f.W / 3f), 2, 5));

        // side walls: notice boards, posters, the odd cabinet
        SideWallDecor(r, f.Left, 2.6f, 1.6f, new[] { "BulletinBoard", "posterA", "posterC" });
        SideWallDecor(r, f.Right, 2.6f, 1.6f, new[] { "posterB", "BulletinBoard", "posterD" });
        if (r.Chance(0.6f)) WallRun(r, f.Right, new[] { "filingCabinet" }, 1.0f, max: 1, a0: AlongOf(f.Right, f.L(f.W, f.D * 0.25f)) - 0.5f, a1: AlongOf(f.Right, f.L(f.W, f.D * 0.25f)) + 0.5f);

        TrashByDoor(r);

        // abandonment & outbreak storytelling
        Scatter(r, FloorPaper, Mathf.Clamp((int)(r.Area / 20f), 3, 10));
        Scatter(r, new[] { "backpack" }, r.rng.Next(2, 6), flat: false);
        Scatter(r, new[] { "book" }, r.rng.Next(1, 4), flat: false);
        if (r.Chance(0.3f)) DeskBarricadeCorner(r, f);
        if (r.Area > 230f) SecondaryZone(r, f, v);
        Blood(r, 0.35f);
        CeilingDamage(r, 0.35f);
    }

    /// <summary>Oversized rooms: the teaching zone stays a realistic size; the leftover back area gets its own purpose.</summary>
    static void SecondaryZone(Room r, Frame f, ClassVariant v)
    {
        string[] keys = v switch
        {
            ClassVariant.Music => new[] { "UprightPiano", "CardboardBox_L", "MusicStand", "chair", "CubbyShelf", "CardboardBox_M" },
            ClassVariant.Science => new[] { "TableLong", "MetalShelf_Stocked", "CardboardBox_M", "rollingCart", "filingCabinet" },
            ClassVariant.Computer => new[] { "TableLong", "CardboardBox_L", "rollingCart", "CardboardBox_M", "filingCabinet" },
            ClassVariant.Art => new[] { "Easel", "MetalShelf_Stocked", "TableLong", "CardboardBox_M", "Easel" },
            _ => new[] { "CubbyShelf", "CardboardBox_M", "TableLong", "shelfWood", "CardboardBox_L" },
        };
        float tMax = Mathf.Max(3f, f.D * 0.38f);
        int n = 0;
        for (int i = 0; i < 40 && n < Mathf.Clamp((int)(f.W / 2f), 4, 10); i++)
        {
            string k = r.Pick(keys);
            Vector3 p = f.L(r.R(1.4f, f.W - 1.4f), r.R(1.4f, tMax));
            bool aligned = k == "TableLong" || k.StartsWith("Cubby") || k.StartsWith("shelf") || k.StartsWith("Metal");
            float yaw = aligned ? f.YawFront + (r.Chance(0.5f) ? 0 : 90) : r.R(0, 360);
            var go = Floor(r, k, p, yaw);
            if (go == null) continue;
            n++;
            if (k == "TableLong") OnTop(r, go, r.Chance(0.5f) ? "monitor" : "papers", 0, 0, r.R(0, 360));
        }
        if (v == ClassVariant.Computer)
            for (int i = 0; i < 4; i++) Floor(r, "monitor", f.L(r.R(1.5f, f.W - 1.5f), r.R(1f, tMax)), r.R(0, 360), new Vector3(r.Chance(0.5f) ? 90 : -90, 0, 0));
        if (r.Area > 380f && r.Chance(0.6f))
        {
            Vector3 c = f.L(r.Chance(0.5f) ? 2.2f : f.W - 2.2f, 1.8f);
            Floor(r, "Mattress", c, f.YawFront + 90f, name: "SurvivorMattress");
            Floor(r, "backpack", c + DirOf(f.YawFront) * 1.3f, r.R(0, 360));
            Floor(r, "CardboardBox_S", c + DirOf(f.YawFront + 90f) * 1.4f, r.R(0, 360));
        }
    }

    static Vector3 DirOf(float yaw) => new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0, Mathf.Cos(yaw * Mathf.Deg2Rad));

    /// <summary>Grid of seats facing the front wall, with gaps, shoves and knocked-over pieces.</summary>
    static void Rows(Room r, Frame f, string key, float colStep, float rowStep, float frontGap, float pMissing, float pKnocked, bool stool = false, int maxRows = 6, int maxCols = 7)
    {
        int cols = Mathf.Clamp(Mathf.FloorToInt((f.W - 3.2f) / colStep) + 1, 1, maxCols);
        float s0 = (f.W - (cols - 1) * colStep) * 0.5f;
        int row = 0;
        for (float t = f.D - frontGap; t >= 1.8f && row < maxRows; t -= rowStep, row++)
            for (int c = 0; c < cols; c++)
            {
                if (r.Chance(pMissing)) continue;
                float s = s0 + c * colStep + r.R(-0.05f, 0.05f);
                float tt = t + r.R(-0.06f, 0.06f);
                float yaw = f.YawFront + r.R(-6, 6);
                Vector3 tilt = default;
                if (r.Chance(pKnocked)) { tilt = r.Chance(0.5f) ? new Vector3(0, 0, r.Chance(0.5f) ? 90 : -90) : new Vector3(r.Chance(0.5f) ? 90 : -90, 0, 0); yaw = r.R(0, 360); }
                else if (r.Chance(0.14f)) { s += r.R(-0.2f, 0.2f); tt += r.R(-0.15f, 0.15f); yaw += r.R(-30, 30); }
                var go = Floor(r, key, f.L(s, tt), yaw, tilt, name: key + "_R" + row + "C" + c);
                if (go != null && stool && tilt == default)
                    Floor(r, "chair", f.L(s + r.R(-0.3f, 0.3f), tt - 0.85f), f.YawFront + r.R(-25, 25));
                if (go != null && key == "studentDesk" && tilt == default && r.Chance(0.25f))
                    OnTop(r, go, r.Chance(0.6f) ? "book" : "papers", 0f, 0f, r.R(0, 360));
            }
    }

    static void ComputerRows(Room r, Frame f)
    {
        int row = 0;
        float zone = Mathf.Min(f.W - 3.2f, 13f), s0 = (f.W - zone) * 0.5f;
        for (float t = f.D - 3.0f; t >= 1.6f && row < 5; t -= 2.6f, row++)
            for (float s = s0 + 1.0f; s <= s0 + zone - 0.6f; s += 2.2f)
            {
                if (r.Chance(0.08f)) continue;
                var table = Floor(r, "TableLong", f.L(s, t), f.YawFront + 180f + r.R(-2, 2), name: "ComputerTable_R" + row);
                if (table == null) continue;
                Vector3 along = DirOf(f.YawFront + 90f);
                OnTop(r, table, "monitor", -0.42f * along.x, -0.42f * along.z, f.YawFront + 180f);
                Vector3 fwd = DirOf(f.YawFront);
                OnTop(r, table, "keyboard", -0.42f * along.x - 0.22f * fwd.x, -0.42f * along.z - 0.22f * fwd.z, f.YawFront + r.R(-8, 8));
                if (r.Chance(0.6f)) OnTop(r, table, "monitor", 0.42f * along.x, 0.42f * along.z, f.YawFront + 180f + r.R(-10, 10));
                var seat = f.L(s + r.R(-0.3f, 0.3f), t - 0.75f);
                if (r.Chance(0.85f)) Floor(r, r.Chance(0.5f) ? "swivelChair" : "chair", seat, f.YawFront + r.R(-30, 30), r.Chance(0.12f) ? new Vector3(0, 0, 90) : default);
            }
    }

    static void ArtTables(Room r, Frame f)
    {
        for (float t = f.D - 3.2f; t >= 2.0f; t -= 3.0f)
            for (float s = 2.2f; s <= f.W - 2.2f; s += 3.4f)
            {
                var table = Floor(r, "TableLong", f.L(s, t), f.YawFront + 90f + r.R(-8, 8));
                if (table == null) continue;
                OnTop(r, table, "papers", 0f, 0f, r.R(0, 360));
                if (r.Chance(0.6f)) OnTop(r, table, "cupStack", 0.5f, 0.1f, 0);
                for (int k = 0; k < 4; k++)
                {
                    Vector3 off = new Vector3(r.R(-1.1f, 1.1f), 0, r.R(-1.1f, 1.1f));
                    Floor(r, "chairWood", f.L(s, t) + off, r.R(0, 360), r.Chance(0.1f) ? new Vector3(90, 0, 0) : default);
                }
            }
        for (int i = 0; i < 4; i++) Floor(r, "Easel", f.L(r.R(1.2f, f.W - 1.2f), r.R(f.D * 0.5f, f.D - 2.5f)), f.YawFront + r.R(-40, 40));
    }

    static void MusicArcs(Room r, Frame f)
    {
        Vector3 focus = f.L(f.W * 0.5f, f.D - 1.2f);
        Floor(r, "MusicStand", focus + DirOf(f.YawFront + 180f) * 0.6f, f.YawFront + 180f, name: "ConductorStand");
        int ring = 0;
        for (float rad = 2.6f; rad < Mathf.Min(f.D - 1.5f, f.W * 0.62f); rad += 1.6f, ring++)
        {
            int seats = Mathf.Clamp((int)(rad * 2.1f), 4, 12);
            for (int i = 0; i < seats; i++)
            {
                float a = Mathf.Lerp(-62f, 62f, seats == 1 ? 0.5f : i / (float)(seats - 1));
                float yaw = f.YawFront + 180f + a;          // direction from the conductor outwards
                Vector3 p = focus + DirOf(yaw) * rad;
                if (r.Chance(0.1f)) continue;
                var chair = Floor(r, "chair", p, yaw + 180f + r.R(-8, 8), r.Chance(0.07f) ? new Vector3(90, 0, 0) : default);
                if (chair != null && r.Chance(0.55f)) Floor(r, "MusicStand", p + DirOf(yaw + 180f) * 0.6f, yaw + r.R(-15, 15), r.Chance(0.1f) ? new Vector3(0, 0, 90) : default);
            }
        }
    }

    /// <summary>Outbreak barricade: desks and chairs shoved into a back corner, some on their sides.</summary>
    static void DeskBarricadeCorner(Room r, Frame f)
    {
        float s = r.Chance(0.5f) ? 1.0f : f.W - 1.0f;
        for (int i = 0; i < 6; i++)
        {
            Vector3 p = f.L(s + r.R(-0.8f, 0.8f), r.R(0.8f, 2.4f));
            Vector3 tilt = r.Chance(0.5f) ? new Vector3(r.Chance(0.5f) ? 90 : -90, 0, 0) : new Vector3(0, 0, r.R(-20, 20));
            Floor(r, r.Chance(0.6f) ? "studentDesk" : "chair", p, r.R(0, 360), tilt);
        }
    }

    // ------------------------------------------------------------------ other interiors

    static void Library(Room r)
    {
        Side F = PickFrontWall(r, preferLong: true);
        var f = MakeFrame(r, F);
        // stacks: rows of book shelves across the back half
        for (float t = 1.6f; t <= f.D * 0.48f; t += 3.0f)
            for (float s = 1.6f; s <= f.W - 1.6f; s += 2.0f)
                if (Mathf.Abs(s - f.W * 0.5f) > 1.3f) Floor(r, "shelfBooks", f.L(s, t), f.YawFront + (r.Chance(0.5f) ? 0 : 180), r.Chance(0.06f) ? new Vector3(0, 0, 90) : default);
        WallRun(r, f.Left, new[] { "shelfBooks" }, 2.0f, pad: 0.4f);
        WallRun(r, f.Right, new[] { "shelfBooks" }, 2.0f, pad: 0.4f);
        // reading tables in the front half
        for (float t = f.D * 0.58f; t <= f.D - 2.0f; t += 2.6f)
            for (float s = 2.0f; s <= f.W - 2.0f; s += 3.0f)
            {
                var table = Floor(r, "TableLong", f.L(s, t), f.YawFront + 90f + r.R(-5, 5));
                if (table == null) continue;
                OnTop(r, table, "book", r.R(-0.5f, 0.5f), 0f, r.R(0, 360));
                if (r.Chance(0.5f)) OnTop(r, table, "laptop", 0.3f, 0f, r.R(0, 360));
                for (int k = 0; k < 4; k++) Floor(r, "chairWood", f.L(s + (k < 2 ? -0.9f : 0.9f), t + (k % 2 == 0 ? -0.75f : 0.75f)), f.YawFront + (k % 2 == 0 ? 0 : 180) + r.R(-20, 20), r.Chance(0.12f) ? new Vector3(90, 0, 0) : default);
            }
        // circulation desk near the entrance
        var door = r.openings.FirstOrDefault();
        if (door != null)
        {
            Vector3 p = r.WallPoint(door.side, door.to + 2.8f, 2.6f);
            var desk = Floor(r, "CheckoutCounter", p, Room.YawFacingInto(door.side) + 90f, name: "CirculationDesk")
                       ?? Floor(r, "CheckoutCounter", r.WallPoint(door.side, door.from - 2.8f, 2.6f), Room.YawFacingInto(door.side) - 90f, name: "CirculationDesk");
            OnTop(r, desk, "monitor", 0.6f, 0f, Room.YawFacingInto(door.side));
            OnTop(r, desk, "book", -0.6f, 0f, r.R(0, 360));
        }
        WallItem(r, F, AlongOf(F, f.L(f.W * 0.5f, f.D)), "clock", centerY: r.floorY + 3.0f);
        SideWallDecor(r, F, 2.8f, 1.6f, Posters, 0.6f);
        Scatter(r, new[] { "book", "book", "notebooks", "papers" }, (int)(r.Area / 10f), flat: false);
        TrashByDoor(r);
        Blood(r, 0.3f);
        CeilingDamage(r, 0.4f);
    }

    static void OfficeDesks(Room r, Frame f, int count, float tFrom, float tTo)
    {
        int placed = 0;
        for (float t = tFrom; t <= tTo && placed < count; t += 2.6f)
            for (float s = 1.8f; s <= f.W - 1.8f && placed < count; s += 3.0f)
            {
                float yaw = f.YawFront + (r.Chance(0.5f) ? 0f : 180f) + r.R(-6, 6);
                var desk = Floor(r, "teacherDesk", f.L(s, t), yaw);
                if (desk == null) continue;
                placed++;
                Bounds db = WorldBounds(desk);
                Floor(r, "swivelChair", new Vector3(db.center.x, r.floorY, db.center.z) - DirOf(yaw) * 0.95f, yaw + r.R(-50, 50), r.Chance(0.1f) ? new Vector3(0, 0, 90) : default);
                OnTop(r, desk, "monitor", 0.3f, 0f, yaw + 180f + r.R(-10, 10));
                OnTop(r, desk, "papers", -0.2f, 0f, r.R(0, 360));
                if (r.Chance(0.4f)) OnTop(r, desk, "cupStack", -0.5f, 0.1f, 0);
            }
    }

    static void Office(Room r, bool reception)
    {
        Side F = PickFrontWall(r);
        var f = MakeFrame(r, F);
        if (reception && r.openings.Count > 0)
        {
            var door = r.openings.OrderByDescending(o => o.Width).First();
            Vector3 p = r.WallPoint(door.side, door.Mid, 3.4f);
            Floor(r, "ReceptionCounter", p, Room.YawFacingInto(door.side) + 180f, name: "ReceptionCounter");
            WallRun(r, door.side, new[] { "WaitingBench" }, 2.3f, max: 2, a0: door.to + 0.8f, a1: door.to + 5.6f);
        }
        OfficeDesks(r, f, reception ? 4 : 2, 2.2f, f.D - 2.0f);
        WallRun(r, f.Back, new[] { "filingCabinet", "filingCabinet", "shelfWood", "filingCabinet" }, 1.1f, pad: 0.5f, max: 6, chanceSkip: 0.2f);
        WallRun(r, f.Left, new[] { "filingCabinet", "MetalShelf_Stocked" }, 1.6f, pad: 1.0f, max: 3, chanceSkip: 0.3f);
        WallItem(r, F, AlongOf(F, f.L(f.W * 0.5f, f.D)), "BulletinBoard", centerY: r.floorY + 1.6f);
        WallItem(r, F, AlongOf(F, f.L(f.W * 0.2f, f.D)), "clock", centerY: r.floorY + 2.9f);
        SideWallDecor(r, f.Right, 3.0f, 1.6f, Posters, 0.6f);
        if (r.Chance(0.7f)) WallItem(r, f.Right, AlongOf(f.Right, f.L(f.W, f.D * 0.7f)), "WaterFountain");
        if (r.Area > 200f)
        {
            WallRun(r, f.Right, new[] { "filingCabinet", "filingCabinet", "MetalShelf_Stocked" }, 1.2f, pad: 1.0f, max: 5, chanceSkip: 0.25f);
            for (float s = 2.0f; s <= f.W - 2.0f; s += 3.2f) Floor(r, r.Chance(0.6f) ? "filingCabinet" : "CubbyShelf", f.L(s, 1.2f), f.YawFront + r.R(-5, 5));
            Floor(r, "rollingCart", f.L(f.W * 0.5f, f.D * 0.45f), r.R(0, 360));
        }
        TrashByDoor(r);
        Scatter(r, FloorPaper, (int)(r.Area / 18f));
        Scatter(r, new[] { "CardboardBox_S", "CardboardBox_M" }, 3, flat: false);
        Blood(r, 0.3f);
        CeilingDamage(r, 0.25f);
    }

    static void Principal(Room r)
    {
        Side F = PickFrontWall(r);
        var f = MakeFrame(r, F);
        var desk = Floor(r, "teacherDesk", f.L(f.W * 0.5f, f.D - 2.4f), f.YawFront + 180f, name: "PrincipalDesk");
        if (desk != null)
        {
            Floor(r, "swivelChair", f.L(f.W * 0.5f, f.D - 1.5f), f.YawFront + 180f);
            Floor(r, "chairWood", f.L(f.W * 0.5f - 0.7f, f.D - 3.6f), f.YawFront + r.R(-15, 15));
            Floor(r, "chairWood", f.L(f.W * 0.5f + 0.7f, f.D - 3.6f), f.YawFront + r.R(-15, 15), r.Chance(0.4f) ? new Vector3(90, 0, 0) : default);
            OnTop(r, desk, "monitor", 0.35f, 0f, f.YawFront + 180f);
            OnTop(r, desk, "globe", -0.45f, 0f, 0);
            OnTop(r, desk, "papers", 0f, 0f, r.R(0, 360));
        }
        WallRun(r, F, new[] { "shelfBooks", "trophyCase", "shelfBooks" }, 2.4f, pad: 1.0f, max: 4);
        Floor(r, "ConferenceTable", f.L(f.W * 0.5f, f.D * 0.38f), f.YawFront + 90f, name: "MeetingTable");
        for (int i = 0; i < 6; i++)
            Floor(r, "chairWood", f.L(f.W * 0.5f + (i < 3 ? -1.05f : 1.05f), f.D * 0.38f + (i % 3 - 1) * 1.2f), f.YawFront + (i < 3 ? 90 : 270) + r.R(-15, 15), r.Chance(0.15f) ? new Vector3(0, 0, 90) : default);
        Floor(r, "sofa", f.L(1.4f, f.D * 0.7f), f.YawFront + 90f);
        WallRun(r, f.Right, new[] { "filingCabinet" }, 1.0f, pad: 1.0f, max: 4);
        SideWallDecor(r, f.Left, 3.2f, 1.7f, Posters, 0.7f);
        WallItem(r, f.Back, AlongOf(f.Back, f.L(f.W * 0.25f, 0)), "clock", centerY: r.floorY + 2.9f);
        WallItem(r, f.Back, AlongOf(f.Back, f.L(f.W * 0.75f, 0)), "WallTV", centerY: r.floorY + 2.2f);
        // the suite is huge: secretaries' desks and a waiting area fill the entry half
        OfficeDesks(r, f, 4, 2.0f, f.D * 0.22f);
        WallRun(r, f.Left, new[] { "WaitingBench", "WaitingBench" }, 2.4f, pad: 1.2f, max: 3, a0: AlongOf(f.Left, f.L(0, f.D * 0.05f)), a1: AlongOf(f.Left, f.L(0, f.D * 0.45f)));
        for (float s = f.W * 0.62f; s <= f.W - 1.4f; s += 2.6f) Floor(r, "filingCabinet", f.L(s, f.D * 0.62f), f.YawFront + (r.Chance(0.5f) ? 0 : 180));
        WallRun(r, f.Back, new[] { "filingCabinet", "shelfWood", "filingCabinet", "MetalShelf_Stocked" }, 1.6f, pad: 1.0f, max: 6, chanceSkip: 0.25f);
        Scatter(r, FloorPaper, (int)(r.Area / 14f));
        Scatter(r, new[] { "CardboardBox_M", "CardboardBox_S", "chairWood" }, 5, flat: false);
        Blood(r, 0.5f);
        CeilingDamage(r, 0.3f);
        TrashByDoor(r);
    }

    static void Conference(Room r)
    {
        Side F = PickFrontWall(r);
        var f = MakeFrame(r, F);
        Vector3 c = r.Center;
        float yaw = r.rect.width > r.rect.height ? 90f : 0f;
        foreach (float off in r.rect.width > 14f || r.rect.height > 14f ? new[] { -4.3f, 0f, 4.3f } : new[] { 0f })
        {
            Vector3 p = c + (yaw == 90f ? new Vector3(off, 0, 0) : new Vector3(0, 0, off));
            if (Floor(r, "ConferenceTable", p, yaw, name: "ConferenceTable") == null) continue;
            for (int i = 0; i < 6; i++)
            {
                float along = (i % 3 - 1) * 1.25f;
                float side = i < 3 ? -1.05f : 1.05f;
                Vector3 q = p + (yaw == 90f ? new Vector3(along, 0, side) : new Vector3(side, 0, along));
                Floor(r, r.Chance(0.6f) ? "swivelChair" : "chairWood", q, (yaw == 90f ? (i < 3 ? 0 : 180) : (i < 3 ? 90 : 270)) + r.R(-25, 25), r.Chance(0.1f) ? new Vector3(0, 0, 90) : default);
            }
        }
        WallItem(r, F, AlongOf(F, f.L(f.W * 0.5f, f.D)), "ProjectorScreen", bottomY: r.floorY + 1.4f);
        WallItem(r, F, AlongOf(F, f.L(f.W * 0.15f, f.D)), "Whiteboard", centerY: r.floorY + 1.5f);
        SideWallDecor(r, f.Left, 3.0f, 1.7f, Posters, 0.6f);
        WallRun(r, f.Back, new[] { "filingCabinet", "shelfWood" }, 1.5f, max: 3, chanceSkip: 0.3f);
        Scatter(r, FloorPaper, (int)(r.Area / 12f));
        Blood(r, 0.25f);
        TrashByDoor(r);
    }

    static void Nurse(Room r, bool backroom)
    {
        Side F = PickFrontWall(r);
        var f = MakeFrame(r, F);
        if (!backroom)
        {
            // cots along the back and side walls, with a desk near the door
            WallRun(r, f.Back, new[] { "NurseCot" }, 2.3f, pad: 0.6f, max: 4, jitterYaw: 4f);
            WallRun(r, f.Left, new[] { "NurseCot" }, 2.3f, pad: 1.5f, max: 2);
            OfficeDesks(r, f, 1, f.D - 2.6f, f.D - 2.0f);
            WallItem(r, F, AlongOf(F, f.L(f.W * 0.3f, f.D)), "MedicineCabinet", bottomY: r.floorY + 1.38f);
            WallItem(r, F, AlongOf(F, f.L(f.W * 0.75f, f.D)), "BulletinBoard", centerY: r.floorY + 1.6f);
            WallRun(r, f.Right, new[] { "filingCabinet", "MetalShelf_Stocked" }, 1.6f, max: 2);
            Scatter(r, new[] { "papers", "CardboardBox_S", "CardboardBox_M" }, 6, flat: false);
            Blood(r, 0.8f);
        }
        else
        {
            WallRun(r, f.Back, new[] { "MetalShelf_Stocked" }, 2.0f, pad: 0.4f);
            WallRun(r, f.Left, new[] { "MetalShelf_Stocked", "MedicineCabinet" }, 2.0f, pad: 0.4f, max: 3);
            WallRun(r, f.Right, new[] { "NurseCot" }, 2.3f, max: 1);
            WallRun(r, f.Right, new[] { "MetalShelf_Stocked", "filingCabinet" }, 1.9f, pad: 0.4f, max: 3);
            WallRun(r, F, new[] { "filingCabinet", "MetalShelf_Stocked", "filingCabinet" }, 1.2f, pad: 0.4f, max: 4);
            Scatter(r, new[] { "CardboardBox_S", "CardboardBox_M", "CardboardBox_L", "crate" }, 7, flat: false);
            // a survivor tried to hole up here
            Floor(r, "Mattress", f.L(f.W * 0.5f, f.D * 0.45f), r.R(0, 360), name: "SurvivorMattress");
            Scatter(r, new[] { "backpack", "papers" }, 3, flat: false);
            Blood(r, 0.6f);
        }
        TrashByDoor(r);
        CeilingDamage(r, 0.3f);
    }

    static void Janitor(Room r, bool workshop)
    {
        Side F = PickFrontWall(r);
        var f = MakeFrame(r, F);
        WallRun(r, f.Back, new[] { "MetalShelf_Stocked", "MetalShelf", "MetalShelf_Stocked" }, 2.0f, pad: 0.4f);
        WallRun(r, f.Left, new[] { "mopBucket", "Ladder", "MetalShelf_Stocked" }, 1.9f, pad: 0.6f, max: 4);
        if (workshop) Floor(r, "Workbench", f.L(f.W * 0.5f, f.D - 0.7f), f.YawFront + 180f, name: "Workbench");
        else WallItem(r, F, AlongOf(F, f.L(f.W * 0.5f, f.D)), "ElectricalPanel", bottomY: r.floorY + 0.25f);
        WallItem(r, f.Right, AlongOf(f.Right, f.L(f.W, f.D * 0.5f)), "ElectricalPanel", bottomY: r.floorY + 0.25f);
        WallItem(r, f.Right, AlongOf(f.Right, f.L(f.W, f.D * 0.25f)), "Broom");
        Scatter(r, new[] { "wetFloor", "CardboardBox_M", "CardboardBox_L", "OilDrum_Blue", "TrashBag_A", "Toolbox", "rollingCart" }, workshop ? 10 : 6, flat: false);
        if (workshop) Floor(r, "Pallet", f.L(f.W * 0.75f, f.D * 0.4f), r.R(0, 30));
        if (r.Area > 100f)
        {
            // big maintenance room: shelving islands, a second bench and stock on pallets
            for (float s = 2.6f; s <= f.W - 2.6f; s += 3.4f) Floor(r, "MetalShelf_Stocked", f.L(s, f.D * 0.45f), f.YawFront + 90f);
            Floor(r, "Workbench", f.L(f.W * 0.3f, f.D * 0.72f), f.YawFront + r.R(-5, 5));
            Floor(r, "GeneratorUnit", f.L(f.W - 1.4f, 1.3f), f.YawFront);
            Scatter(r, new[] { "CardboardBox_L", "crate", "OilDrum_Rust", "Pallet", "Toolbox", "mopBucket", "PlankPile" }, 10, flat: false);
        }
        Blood(r, 0.2f);
        CeilingDamage(r, 0.3f);
    }

    static void Storage(Room r, bool camp)
    {
        Side F = PickFrontWall(r, preferLong: true);
        var f = MakeFrame(r, F);
        WallRun(r, F, new[] { "MetalShelf_Stocked", "MetalShelf", "MetalShelf_Stocked" }, 2.0f, pad: 0.4f);
        WallRun(r, f.Back, new[] { "MetalShelf_Stocked", "MetalShelf_Stocked", "MetalShelf" }, 2.0f, pad: 0.4f);
        for (float s = 2.4f; s <= f.W - 2.4f; s += 3.2f)
            Floor(r, "MetalShelf_Stocked", f.L(s, f.D * 0.5f), f.YawFront + 90f);
        Scatter(r, new[] { "crate", "CardboardBox_L", "CardboardBox_M", "Pallet", "OilDrum_Rust", "PlankPile" }, (int)(r.Area / 14f), flat: false);
        if (camp)
        {
            Vector3 p = f.L(f.W * 0.2f, f.D * 0.25f);
            Floor(r, "Mattress", p, f.YawFront + 90f, name: "SurvivorMattress");
            Floor(r, "backpack", p + DirOf(f.YawFront) * 1.2f, r.R(0, 360));
            Floor(r, "CardboardBox_M", p + DirOf(f.YawFront + 90f) * 1.4f, r.R(0, 360));
            Floor(r, "TrashBag_A", p + DirOf(f.YawFront - 90f) * 1.3f, r.R(0, 360));
            Floor(r, "petrol", p + DirOf(f.YawFront + 45f) * 1.6f, r.R(0, 360));
        }
        Blood(r, 0.3f);
        CeilingDamage(r, 0.3f);
    }

    static void Gym(Room r)
    {
        // the gym was already dressed by hand (weights, machines, benches line every wall): add hoops where free
        bool longX = r.rect.width >= r.rect.height;
        foreach (Side s in longX ? new[] { Side.E, Side.W } : new[] { Side.N, Side.S })
        {
            float mid = (r.AlongMin(s) + r.AlongMax(s)) * 0.5f;
            foreach (float off in new[] { 0f, 3f, -3f, 5.5f, -5.5f })
                if (WallItem(r, s, mid + off, "BasketballHoop", bottomY: r.floorY + 2.85f) != null) break;
        }
        DressedRoomExtras(r);
        Scatter(r, new[] { "kettlebell", "backpack", "papers" }, 4, flat: false);
    }

    static void Hallway(Room r, bool lockers = true)
    {
        bool longX = r.rect.width >= r.rect.height;
        float width = longX ? r.rect.height : r.rect.width;
        Side a = longX ? Side.N : Side.E, b = longX ? Side.S : Side.W;
        if (lockers && width >= 3.4f)
        {
            foreach (Side s in width >= 4.6f ? new[] { a, b } : new[] { r.OpeningWidth(a) <= r.OpeningWidth(b) ? a : b })
            {
                // banks of lockers with breaks for notice boards, fountains and extinguishers
                float from = r.AlongMin(s) + 0.6f, to = r.AlongMax(s) - 0.6f;
                float cur = from + r.R(0, 1.5f);
                while (cur < to - 1f)
                {
                    int bank = r.rng.Next(4, 9);
                    string lk = r.Pick(Lockers);
                    float bankEnd = Mathf.Min(to, cur + bank * 0.52f);
                    WallRun(r, s, new[] { lk }, 0.52f, a0: cur, a1: bankEnd, chanceSkip: 0.04f);
                    cur = bankEnd + 0.4f;
                    float gap = r.R(1.6f, 3.2f);
                    string feature = r.Pick(new[] { "BulletinBoard", "FireExtinguisher", "WaterFountain", "posterA", "posterC", "BulletinBoard" });
                    if (feature == "WaterFountain") WallItem(r, s, cur + gap * 0.5f, feature);
                    else if (feature == "FireExtinguisher") WallItem(r, s, cur + gap * 0.5f, feature, bottomY: r.floorY + 1.0f);
                    else WallItem(r, s, cur + gap * 0.5f, feature, centerY: r.floorY + 1.6f);
                    cur += gap;
                }
            }
        }
        else
        {
            foreach (Side s in new[] { a, b }) SideWallDecor(r, s, 3.2f, 1.6f, new[] { "BulletinBoard", "posterB", "posterD" }, 0.6f);
        }
        // trash cans, the odd knocked-over locker or bench, lots of paper
        Scatter(r, new[] { "trashCan" }, Mathf.Max(1, (int)(r.Area / 70f)), margin: 0.5f, flat: false);
        Scatter(r, FloorPaper, Mathf.Clamp((int)(r.Area / 16f), 3, 16));
        Scatter(r, new[] { "backpack", "book", "wetFloor", "CardboardBox_S" }, Mathf.Clamp((int)(r.Area / 30f), 2, 8), flat: false);
        Blood(r, 0.45f);
        CeilingDamage(r, 0.4f);
    }

    static void Stairwell(Room r)
    {
        foreach (var o in r.openings.ToList())
            WallItem(r, o.side, o.Mid, "exitSign", centerY: r.floorY + 3.35f, allowOverOpening: true);
        Scatter(r, new[] { "papers" }, 2);
    }

    // ------------------------------------------------------------------ basement

    static void Tunnel(Room r)
    {
        bool longZ = r.rect.height > r.rect.width;
        // overhead pipe runs down the whole length
        float len = longZ ? r.rect.height : r.rect.width;
        for (float u = 2f; u < len - 1.5f; u += 4.05f)
        {
            Vector3 p = longZ ? new Vector3(r.rect.xMax - 1.0f, 0, r.rect.yMin + u) : new Vector3(r.rect.xMin + u, 0, r.rect.yMax - 1.0f);
            Place(r, "PipeRun_4m", p, longZ ? 90f : 0f, new Opt { ceiling = true, ignoreKeepouts = true });
        }
        Side wallA = longZ ? Side.E : Side.N, wallB = longZ ? Side.W : Side.S;
        for (float a = r.AlongMin(wallA) + 3f; a < r.AlongMax(wallA) - 2f; a += r.R(6f, 9f))
            WallItem(r, wallA, a, "ElectricalPanel", bottomY: r.floorY + 0.25f);
        Scatter(r, new[] { "crate", "OilDrum_Rust", "CardboardBox_L", "PlankPile", "TrashBag_A" }, (int)(len / 4f), margin: 0.5f, flat: false);
        Blood(r, 0.6f);
    }

    static void Vault(Room r)
    {
        Side F = PickFrontWall(r, preferLong: true);
        var f = MakeFrame(r, F);
        WallRun(r, F, new[] { "MetalShelf_Stocked" }, 2.0f, pad: 0.4f);
        WallRun(r, f.Left, new[] { "MetalShelf_Stocked", "OilDrum_Blue", "OilDrum_Blue", "MetalShelf_Stocked" }, 1.4f, pad: 0.5f);
        WallRun(r, f.Back, new[] { "NurseCot", "NurseCot" }, 2.3f, pad: 0.6f, max: 5);
        Floor(r, "GeneratorUnit", f.L(f.W - 1.6f, f.D - 1.4f), f.YawFront + 180f, name: "ShelterGenerator");
        WallItem(r, f.Right, AlongOf(f.Right, f.L(f.W, f.D * 0.6f)), "ElectricalPanel", bottomY: r.floorY + 0.25f);
        for (int i = 0; i < 3; i++) Floor(r, "SandbagWall", f.L(r.R(3f, f.W - 3f), r.R(3f, f.D - 3f)), r.R(0, 180));
        Scatter(r, new[] { "crate", "CardboardBox_L", "CardboardBox_M", "Mattress", "petrol", "TrashBag_B", "backpack" }, (int)(r.Area / 16f), flat: false);
        Blood(r, 0.5f);
    }

    static void BasementStorage(Room r, bool boiler)
    {
        Side F = PickFrontWall(r, preferLong: true);
        var f = MakeFrame(r, F);
        WallRun(r, F, new[] { "MetalShelf_Stocked", "MetalShelf", "filingCabinet", "MetalShelf_Stocked" }, 1.9f, pad: 0.4f);
        WallRun(r, f.Back, new[] { "MetalShelf", "MetalShelf_Stocked" }, 2.0f, pad: 0.4f, chanceSkip: 0.25f);
        for (float s = 2.6f; s <= f.W - 2.6f; s += 4.0f)
            for (float t = 3.0f; t <= f.D - 3.0f; t += 4.5f)
                Floor(r, "MetalShelf_Stocked", f.L(s, t), f.YawFront + 90f);
        if (boiler)
        {
            Floor(r, "BoilerTank", f.L(1.2f, f.D - 1.2f), f.YawFront + 180f, name: "Boiler");
            Floor(r, "BoilerTank", f.L(2.4f, f.D - 1.2f), f.YawFront + 180f, name: "Boiler");
        }
        for (float u = 2f; u < (f.W > f.D ? f.W : f.D) - 2f; u += 4.05f)
        {
            bool alongX = r.rect.width > r.rect.height;
            Vector3 p = alongX ? new Vector3(r.rect.xMin + u, 0, r.rect.yMax - 1.1f) : new Vector3(r.rect.xMax - 1.1f, 0, r.rect.yMin + u);
            Place(r, "PipeRun_4m", p, alongX ? 0f : 90f, new Opt { ceiling = true, ignoreKeepouts = true });
        }
        Scatter(r, new[] { "crate", "CardboardBox_L", "CardboardBox_M", "Pallet", "OilDrum_Rust", "PlankPile", "book" }, (int)(r.Area / 14f), flat: false);
        Blood(r, 0.4f);
    }

    static void UtilityRoom(Room r)
    {
        Side F = PickFrontWall(r, preferLong: true);
        var f = MakeFrame(r, F);
        // boilers and the generator line the teaching... err, service wall
        for (float s = 1.5f; s <= f.W - 1.5f; s += 2.6f)
            if (r.Chance(0.75f)) Floor(r, r.Chance(0.7f) ? "BoilerTank" : "GeneratorUnit", f.L(s, f.D - 1.1f), f.YawFront + 180f);
        for (float a = r.AlongMin(f.Left) + 1.5f; a < r.AlongMax(f.Left) - 1.5f; a += 2.2f)
            WallItem(r, f.Left, a, "ElectricalPanel", bottomY: r.floorY + 0.25f);
        WallRun(r, f.Back, new[] { "MetalShelf_Stocked", "Workbench", "MetalShelf" }, 2.4f, pad: 0.6f);
        for (float u = 2f; u < f.W - 2f; u += 4.05f)
        {
            Vector3 p = f.L(u, f.D * 0.5f);
            Place(r, "PipeRun_4m", p, f.YawFront + 90f, new Opt { ceiling = true, ignoreKeepouts = true });
            Place(r, "PipeRun_4m", f.L(u, f.D - 2.6f), f.YawFront + 90f, new Opt { ceiling = true, ignoreKeepouts = true });
        }
        Scatter(r, new[] { "crate", "OilDrum_Rust", "OilDrum_Blue", "CardboardBox_L", "Toolbox", "Pallet", "Ladder" }, (int)(r.Area / 18f), flat: false);
        Blood(r, 0.5f);
    }
}
