using UnityEditor;
using UnityEngine;

public static class MuzzleFlashMaterialSetup
{
    private const string TexturePath = "Assets/Textures/Effects/MuzzleFlash_Generated_Transparent.png";
    private const string MaterialPath = "Assets/MuzzleFlashParticle.mat";
    private static readonly string[] SharedFlashPrefabs =
    {
        "Assets/Prefabs/MuzzleFlash.prefab",
        "Assets/Prefabs/MuzzleFlash 1.prefab",
    };

    [MenuItem("Tools/School of the Dead/Muzzle Flashes/Apply Generated Texture")]
    public static void ApplyGeneratedTexture()
    {
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (texture == null || material == null)
        {
            Debug.LogError("[MuzzleFlashSetup] Missing generated texture or shared material. No assets were changed.");
            return;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
        {
            Debug.LogError("[MuzzleFlashSetup] URP Particles/Unlit shader was not found. No assets were changed.");
            return;
        }

        ConfigureMaterial(material, texture, shader);
        int prefabCount = ConfigureSharedFlashPrefabs(material);
        int controllerCount = AssignRuntimeFallbackMaterial(material);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[MuzzleFlashSetup] Updated shared material, " + prefabCount +
                  " muzzle-flash prefab(s), and " + controllerCount + " WeaponController prefab(s).");
    }

    private static void ConfigureMaterial(Material material, Texture2D texture, Shader shader)
    {
        material.shader = shader;
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 2f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        EditorUtility.SetDirty(material);
    }

    private static int ConfigureSharedFlashPrefabs(Material material)
    {
        int changed = 0;
        foreach (string prefabPath in SharedFlashPrefabs)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                continue;
            }

            foreach (ParticleSystemRenderer renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                renderer.sharedMaterial = material;
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            changed++;
        }
        return changed;
    }

    private static int AssignRuntimeFallbackMaterial(Material material)
    {
        int changed = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                continue;
            }

            WeaponController[] controllers = root.GetComponentsInChildren<WeaponController>(true);
            bool dirty = false;
            foreach (WeaponController controller in controllers)
            {
                if (controller.muzzleFlashFallbackMaterial == material)
                {
                    continue;
                }

                controller.muzzleFlashFallbackMaterial = material;
                dirty = true;
            }

            if (dirty)
            {
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                changed++;
            }
            PrefabUtility.UnloadPrefabContents(root);
        }
        return changed;
    }
}
