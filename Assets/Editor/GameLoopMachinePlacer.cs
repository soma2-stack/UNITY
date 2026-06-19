using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Drops placeholder GAME-LOOP machines (Mystery Box, Pack-a-Punch, Power Switch,
/// a sample Wall Buy) into the School Of The Dead scene under a single, re-runnable
/// "Generated_GameLoop" root. Each is a colored cube with the matching interactable
/// component.
///
/// These are PLACEHOLDERS — after running, drag each one to where you want it in the
/// map. Re-running deletes and recreates the root.
///
/// Power-up drops, the game-over screen, and the PowerupManager all self-bootstrap
/// at runtime, so nothing needs to be placed for those.
/// </summary>
public static class GameLoopMachinePlacer
{
    private const string ScenePath = "Assets/Scenes/SchoolOfTheDead.unity";
    private const string MaterialFolder = "Assets/Materials/SchoolOfTheDead";
    private const string RootName = "Generated_GameLoop";

    [MenuItem("Tools/School Of The Dead/Place Game-Loop Machines")]
    public static void PlaceGameLoopMachines()
    {
        EnsureFolder("Assets/Materials");
        EnsureFolder(MaterialFolder);

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameObject existing = GameObject.Find(RootName);
        if (existing != null)
        {
            Object.DestroyImmediate(existing);
        }
        GameObject root = new GameObject(RootName);

        // Mystery Box (purple).
        GameObject box = CreateMachine(root, "MysteryBox", new Vector3(0f, 0.5f, 0f),
            new Vector3(1.4f, 1f, 0.9f), new Color(0.55f, 0.25f, 0.8f));
        box.AddComponent<MysteryBox>();

        // Pack-a-Punch (gold).
        GameObject pap = CreateMachine(root, "PackAPunch", new Vector3(3f, 1f, 0f),
            new Vector3(1f, 2f, 1f), new Color(0.85f, 0.65f, 0.1f));
        pap.AddComponent<PackAPunchMachine>();

        // Power Switch (orange).
        GameObject power = CreateMachine(root, "PowerSwitch", new Vector3(6f, 1f, 0f),
            new Vector3(0.6f, 1f, 0.3f), new Color(1f, 0.45f, 0.05f));
        power.AddComponent<PowerSwitch>();

        // Sample Wall Buy (steel).
        GameObject wall = CreateMachine(root, "WallBuy_Rifle", new Vector3(9f, 1f, 0f),
            new Vector3(1.2f, 0.6f, 0.15f), new Color(0.5f, 0.55f, 0.6f));
        WallBuy wb = wall.AddComponent<WallBuy>();
        wb.weaponName = "Rifle";
        wb.buyCost = 1500;
        wb.ammoCost = 500;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("Placed Mystery Box, Pack-a-Punch, Power Switch and a sample Wall Buy under '" +
                  RootName + "'. Move each to its spot in the map.");
    }

    private static GameObject CreateMachine(GameObject root, string name, Vector3 position,
        Vector3 scale, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(root.transform);
        go.transform.position = position;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = CreateSolidMaterial("GameLoop - " + name, color);
        return go;
    }

    private static Material CreateSolidMaterial(string materialName, Color color)
    {
        string materialPath = MaterialFolder + "/" + materialName + ".mat";
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
