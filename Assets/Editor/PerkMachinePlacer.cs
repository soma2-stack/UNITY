using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Places one placeholder PERK MACHINE per perk into the School Of The Dead scene
/// under a single "Generated_Perks" root (recreated each run). Each machine is a
/// colored cube with a <see cref="PerkMachine"/> component and the classic price.
///
/// These are PLACEHOLDERS - after running, drag each machine to where you want it
/// in the map. Re-runnable.
/// </summary>
public static class PerkMachinePlacer
{
    private const string ScenePath = "Assets/Scenes/SchoolOfTheDead.unity";
    private const string MaterialFolder = "Assets/Materials/SchoolOfTheDead";
    private const string RootName = "Generated_Perks";

    [MenuItem("Tools/School Of The Dead/Place Perk Machines")]
    public static void PlacePerkMachines()
    {
        EnsureFolder("Assets/Materials");
        EnsureFolder(MaterialFolder);

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameObject existing = GameObject.Find(RootName);
        if (existing != null)
        {
            UnityEngine.Object.DestroyImmediate(existing);
        }
        GameObject root = new GameObject(RootName);

        PerkType[] perks = (PerkType[])Enum.GetValues(typeof(PerkType));
        int created = 0;

        for (int i = 0; i < perks.Length; i++)
        {
            PerkType perk = perks[i];

            GameObject machine = GameObject.CreatePrimitive(PrimitiveType.Cube);
            machine.name = $"PerkMachine_{perk}";
            machine.transform.SetParent(root.transform);
            machine.transform.position = new Vector3(i * 2f, 1f, 0f);
            machine.transform.localScale = new Vector3(0.9f, 2f, 0.9f);

            Material mat = CreateSolidMaterial($"Perk - {perk}", PerkManager.PerkColor(perk));
            machine.GetComponent<MeshRenderer>().sharedMaterial = mat;

            // Keep the default BoxCollider (solid, blocks/marks the machine).
            PerkMachine pm = machine.AddComponent<PerkMachine>();
            pm.perk = perk;
            pm.cost = PerkManager.DefaultCost(perk);

            created++;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Placed {created} perk machines under '{RootName}'. Move each machine to its spot in the map.");
    }

    private static Material CreateSolidMaterial(string materialName, Color color)
    {
        string materialPath = $"{MaterialFolder}/{materialName}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            material = new Material(shader) { name = materialName };
            AssetDatabase.CreateAsset(material, materialPath);
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string parentFolder = System.IO.Path.GetDirectoryName(folderPath)?.Replace("\\", "/");
        string folderName = System.IO.Path.GetFileName(folderPath);

        if (!string.IsNullOrEmpty(parentFolder) && !AssetDatabase.IsValidFolder(parentFolder))
        {
            EnsureFolder(parentFolder);
        }

        AssetDatabase.CreateFolder(parentFolder, folderName);
    }
}
