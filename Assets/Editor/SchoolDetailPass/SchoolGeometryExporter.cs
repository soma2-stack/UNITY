using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Exports the School of the Dead scene geometry to Wavefront OBJ so it can be used as a
/// detailing reference in Blender. Coordinates are written in "Blender space":
///   blender.x = unity.x, blender.y = unity.z, blender.z = unity.y
/// (triangle winding is flipped to compensate for the handedness change), so a Blender
/// location (x, y, z) maps straight back to Unity (x, z, y). Import in Blender with
/// forward_axis='Y', up_axis='Z'. Read-only with respect to the scene.
/// </summary>
public static class SchoolGeometryExporter
{
    public static string ExportRoots(string outputPath, string[] rootNames, bool skipProps)
    {
        var roots = new List<Transform>();
        foreach (string n in rootNames)
        {
            GameObject go = GameObject.Find(n);
            if (go != null) roots.Add(go.transform);
        }
        return Export(outputPath, roots, skipProps);
    }

    /// <summary>Exports every renderer under the given roots; one OBJ object per renderer, grouped by top-level child.</summary>
    public static string Export(string outputPath, List<Transform> roots, bool skipProps)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(1 << 20);
        var mtl = new StringBuilder();
        var writtenMats = new HashSet<string>();
        string mtlName = Path.GetFileNameWithoutExtension(outputPath) + ".mtl";
        sb.AppendLine("# School of the Dead geometry export (Blender space: x=Ux, y=Uz, z=Uy)");
        sb.AppendLine("mtllib " + mtlName);

        int vOffset = 1, vtOffset = 1, objects = 0, tris = 0;
        foreach (Transform root in roots)
        {
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(false))
            {
                Mesh mesh = mf.sharedMesh;
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                if (mesh == null || mr == null || !mr.enabled) continue;
                if (!mesh.isReadable && !Application.isEditor) continue;
                if (skipProps && PrefabUtility.IsPartOfPrefabInstance(mf.gameObject)) continue;

                Vector3[] verts = mesh.vertices;
                Vector2[] uvs = mesh.uv;
                if (verts == null || verts.Length == 0) continue;

                string group = GroupName(root, mf.transform);
                sb.Append("o ").Append(Sanitize(group + "__" + mf.name + "_" + mf.GetInstanceID())).AppendLine();
                Matrix4x4 m = mf.transform.localToWorldMatrix;
                foreach (Vector3 v in verts)
                {
                    Vector3 w = m.MultiplyPoint3x4(v);
                    sb.Append("v ").Append(w.x.ToString("F4", inv)).Append(' ')
                      .Append(w.z.ToString("F4", inv)).Append(' ')
                      .Append(w.y.ToString("F4", inv)).AppendLine();
                }
                bool hasUv = uvs != null && uvs.Length == verts.Length;
                if (hasUv)
                    foreach (Vector2 uv in uvs)
                        sb.Append("vt ").Append(uv.x.ToString("F4", inv)).Append(' ').Append(uv.y.ToString("F4", inv)).AppendLine();

                Material[] mats = mr.sharedMaterials;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    Material mat = s < mats.Length ? mats[s] : null;
                    string matName = Sanitize(mat != null ? mat.name : "None");
                    if (writtenMats.Add(matName)) WriteMaterial(mtl, matName, mat);
                    sb.Append("usemtl ").Append(matName).AppendLine();
                    if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                    int[] idx = mesh.GetTriangles(s);
                    for (int i = 0; i + 2 < idx.Length; i += 3)
                    {
                        // Swap two indices: Unity is left-handed, Blender right-handed.
                        int a = idx[i] + vOffset, b = idx[i + 2] + vOffset, c = idx[i + 1] + vOffset;
                        int ta = idx[i] + vtOffset, tb = idx[i + 2] + vtOffset, tc = idx[i + 1] + vtOffset;
                        if (hasUv) sb.Append("f ").Append(a).Append('/').Append(ta).Append(' ').Append(b).Append('/').Append(tb).Append(' ').Append(c).Append('/').Append(tc).AppendLine();
                        else sb.Append("f ").Append(a).Append(' ').Append(b).Append(' ').Append(c).AppendLine();
                        tris++;
                    }
                }
                vOffset += verts.Length;
                if (hasUv) vtOffset += verts.Length;
                objects++;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        File.WriteAllText(outputPath, sb.ToString());
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(outputPath), mtlName), mtl.ToString());
        return "objects=" + objects + " tris=" + tris + " -> " + outputPath;
    }

    static string GroupName(Transform root, Transform t)
    {
        Transform top = t;
        while (top.parent != null && top.parent != root) top = top.parent;
        return top == root ? root.name : root.name + "__" + top.name;
    }

    static void WriteMaterial(StringBuilder mtl, string name, Material mat)
    {
        Color c = Color.gray;
        if (mat != null)
        {
            if (mat.HasProperty("_BaseColor")) c = mat.GetColor("_BaseColor");
            else if (mat.HasProperty("_Color")) c = mat.GetColor("_Color");
        }
        var inv = CultureInfo.InvariantCulture;
        mtl.AppendLine("newmtl " + name);
        mtl.AppendLine("Kd " + c.r.ToString("F3", inv) + " " + c.g.ToString("F3", inv) + " " + c.b.ToString("F3", inv));
        Texture tex = null;
        if (mat != null)
        {
            if (mat.HasProperty("_BaseMap")) tex = mat.GetTexture("_BaseMap");
            if (tex == null && mat.HasProperty("_MainTex")) tex = mat.GetTexture("_MainTex");
        }
        if (tex != null)
        {
            string p = AssetDatabase.GetAssetPath(tex);
            if (!string.IsNullOrEmpty(p)) mtl.AppendLine("map_Kd " + Path.GetFullPath(p).Replace('\\', '/'));
        }
        mtl.AppendLine();
    }

    static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s) sb.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_');
        return sb.ToString();
    }
}
