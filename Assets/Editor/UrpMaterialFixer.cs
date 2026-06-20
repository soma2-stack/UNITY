using UnityEditor;
using UnityEngine;

/// <summary>
/// Converts "pink" materials - i.e. materials still bound to the built-in /
/// legacy render pipeline shaders - over to the URP "Universal Render
/// Pipeline/Lit" shader so they render correctly under URP instead of showing
/// the magenta error color in Play mode.
///
/// This is exactly what the Low-Poly Western Starter Pack character materials
/// (Assets/tt-3d/Low-PolyWesternStarterPack/) need: they ship with the legacy
/// Standard shader but already have their textures assigned via _MainTex, so we
/// just carry the texture/color over to the URP property names.
///
/// The conversion is null-safe and re-runnable: materials that are already on a
/// "Universal Render Pipeline/..." shader are simply skipped.
/// </summary>
public static class UrpMaterialFixer
{
    private const string UrpLitShaderName = "Universal Render Pipeline/Lit";
    private const string UrpShaderPrefix = "Universal Render Pipeline/";

    /// <summary>
    /// Western pack scope (default). Scans every .mat under Assets/tt-3d.
    /// </summary>
    [MenuItem("Tools/School Of The Dead/Fix Pink Materials (URP)")]
    public static void FixWesternMaterials()
    {
        FixMaterials(new[] { "Assets/tt-3d" });
    }

    /// <summary>
    /// Whole-project scope. Scans every .mat under Assets so any other pink
    /// import can be fixed too.
    /// </summary>
    [MenuItem("Tools/School Of The Dead/Fix Pink Materials (Whole Project)")]
    public static void FixAllMaterials()
    {
        FixMaterials(new[] { "Assets" });
    }

    private static void FixMaterials(string[] folders)
    {
        Shader urpLit = Shader.Find(UrpLitShaderName);
        if (urpLit == null)
        {
            Debug.LogError(
                "UrpMaterialFixer: Could not find the \"" + UrpLitShaderName +
                "\" shader. The Universal Render Pipeline does not appear to be " +
                "installed/active, so nothing was converted.");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Material", folders);

        int converted = 0;
        int skipped = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                continue;
            }

            if (!NeedsConversion(material))
            {
                skipped++;
                continue;
            }

            ConvertToUrpLit(material, urpLit);
            EditorUtility.SetDirty(material);
            converted++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "Converted " + converted + " materials to URP/Lit, skipped " +
            skipped + " already-URP.");
    }

    /// <summary>
    /// A material needs conversion when it is NOT already on a URP shader: its
    /// shader is null, or is a built-in / legacy shader. We treat anything whose
    /// shader name does not start with "Universal Render Pipeline/" as
    /// convertible, with explicit checks for the common built-in families and
    /// the all-zero built-in shader guid.
    /// </summary>
    private static bool NeedsConversion(Material material)
    {
        Shader shader = material.shader;
        if (shader == null)
        {
            return true;
        }

        // Already a URP shader - leave it alone.
        if (shader.name.StartsWith(UrpShaderPrefix))
        {
            return false;
        }

        // Explicit built-in / legacy families.
        if (shader.name.StartsWith("Standard") ||
            shader.name.StartsWith("Legacy Shaders/") ||
            shader.name.StartsWith("Mobile/"))
        {
            return true;
        }

        // Built-in resource shaders live at "Resources/..." (no real asset path)
        // and carry the all-zero built-in guid.
        string shaderPath = AssetDatabase.GetAssetPath(shader);
        if (string.IsNullOrEmpty(shaderPath) ||
            shaderPath.StartsWith("Resources/") ||
            shaderPath == "Library/unity default resources" ||
            shaderPath == "Resources/unity_builtin_extra")
        {
            return true;
        }

        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                shader, out string shaderGuid, out long _) &&
            shaderGuid == "0000000000000000f000000000000000")
        {
            return true;
        }

        // Any other non-URP shader: convert so it stops rendering pink under URP.
        return true;
    }

    private static void ConvertToUrpLit(Material material, Shader urpLit)
    {
        // Capture source values BEFORE swapping shaders, because changing the
        // shader can reset/remap the property block.
        Texture baseTexture = material.GetTexture("_MainTex");
        if (baseTexture == null)
        {
            baseTexture = material.mainTexture;
        }
        if (baseTexture == null && material.HasProperty("_BaseMap"))
        {
            baseTexture = material.GetTexture("_BaseMap");
        }

        Color baseColor = Color.white;
        if (material.HasProperty("_Color"))
        {
            baseColor = material.GetColor("_Color");
        }
        else if (material.HasProperty("_BaseColor"))
        {
            baseColor = material.GetColor("_BaseColor");
        }

        Texture normalMap = material.HasProperty("_BumpMap")
            ? material.GetTexture("_BumpMap")
            : null;
        Texture metallicGlossMap = material.HasProperty("_MetallicGlossMap")
            ? material.GetTexture("_MetallicGlossMap")
            : null;

        // Swap to URP/Lit.
        material.shader = urpLit;

        // Re-apply the captured values onto the URP property names.
        if (baseTexture != null)
        {
            material.SetTexture("_BaseMap", baseTexture);
            material.mainTexture = baseTexture;
        }

        material.SetColor("_BaseColor", baseColor);

        if (normalMap != null)
        {
            material.SetTexture("_BumpMap", normalMap);
            material.EnableKeyword("_NORMALMAP");
        }

        if (metallicGlossMap != null)
        {
            material.SetTexture("_MetallicGlossMap", metallicGlossMap);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
    }
}
