using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Sets up the secret Pack-a-Punch easter egg in the School Of The Dead scene:
/// a manager + HUD, 6 collectible book placeholders, and a sliding bookcase with a
/// blocker over the library staircase.
///
/// Everything is created under a single "Generated_SecretEgg" root (recreated each
/// run). The books and the bookcase are PLACEHOLDERS - drag them to their real
/// hiding spots / over the library_staircase entrance after running this.
/// </summary>
public static class SecretStaircaseSetup
{
    private const string ScenePath = "Assets/Scenes/SchoolOfTheDead.unity";
    private const string MaterialFolder = "Assets/Materials/SchoolOfTheDead";
    private const string RootName = "Generated_SecretEgg";

    [MenuItem("Tools/School Of The Dead/Setup Secret Pack-a-Punch")]
    public static void SetupSecretEgg()
    {
        EnsureFolder("Assets/Materials");
        EnsureFolder(MaterialFolder);

        Material bookMat = CreateSolidMaterial("Secret Book", new Color(0.55f, 0.13f, 0.10f, 1f));
        Material bookcaseMat = CreateSolidMaterial("Secret Bookcase", new Color(0.32f, 0.20f, 0.10f, 1f));

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameObject existing = GameObject.Find(RootName);
        if (existing != null)
        {
            Object.DestroyImmediate(existing);
        }
        GameObject root = new GameObject(RootName);

        // Manager + HUD holder.
        GameObject managerGo = new GameObject("SecretEggManager");
        managerGo.transform.SetParent(root.transform);
        managerGo.AddComponent<SecretBookManager>();
        managerGo.AddComponent<SecretEggHud>();

        // 6 book placeholders in a row near the origin (move each to a hiding spot).
        GameObject booksRoot = new GameObject("Books (move these around the map)");
        booksRoot.transform.SetParent(root.transform);
        for (int i = 0; i < SecretBookManager.TotalBooks; i++)
        {
            GameObject book = GameObject.CreatePrimitive(PrimitiveType.Cube);
            book.name = $"Book_{i + 1}";
            book.transform.SetParent(booksRoot.transform);
            book.transform.position = new Vector3(i * 1.5f, 1.2f, 0f);
            book.transform.localScale = new Vector3(0.35f, 0.22f, 0.28f);
            book.GetComponent<MeshRenderer>().sharedMaterial = bookMat;
            // The default BoxCollider stays for proximity; it's solid - that's fine.
            book.AddComponent<BookPickup>();
        }

        // Bookcase + blocker placeholder (move over the library_staircase entrance).
        GameObject bookcase = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bookcase.name = "SecretBookcase";
        bookcase.transform.SetParent(root.transform);
        bookcase.transform.position = new Vector3(0f, 1.1f, 4f);
        bookcase.transform.localScale = new Vector3(1.2f, 2.2f, 0.4f);
        bookcase.GetComponent<MeshRenderer>().sharedMaterial = bookcaseMat;

        // Blocker that seals the stairwell while the bookcase is closed.
        GameObject blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blocker.name = "StairwellBlocker";
        blocker.transform.SetParent(bookcase.transform);
        blocker.transform.localPosition = Vector3.zero;
        blocker.transform.localScale = Vector3.one; // matches the bookcase footprint
        Object.DestroyImmediate(blocker.GetComponent<MeshRenderer>()); // invisible blocker
        Object.DestroyImmediate(blocker.GetComponent<MeshFilter>());

        SecretBookcase bookcaseComp = bookcase.AddComponent<SecretBookcase>();
        bookcaseComp.blocker = blocker;
        bookcaseComp.openMoveOffset = new Vector3(1.4f, 0f, 0f); // slides aside by its width

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("Secret Pack-a-Punch set up under 'Generated_SecretEgg'. " +
                  "Move the 6 Book_# objects to hiding spots and place the SecretBookcase over the library_staircase entrance.");
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
