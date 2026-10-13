using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

/// <summary>
/// Editor tool that wires the laptop "security feed" video onto the laptop screen in the
/// DeathCinematic scene. Done from the Editor API (never by hand-editing scene YAML) so the
/// VideoPlayer, dedicated screen material, and renderer assignment serialize correctly.
///
/// The video plays on awake, loops, needs no input, and outputs no audio, so nothing extra has
/// to run at play time — the VideoPlayer drives itself.
///
/// Screen target resolution order:
///   1) the GameObject you have SELECTED in the Hierarchy (if it has a Renderer) — the reliable
///      path when the auto-search can't name the screen,
///   2) auto-search: the "a laptop" instance's child renderer whose name looks like a screen,
///   3) if a laptop has exactly one renderer, that renderer,
///   otherwise it reports what it found and asks you to select the screen mesh and re-run.
/// </summary>
public static class DeathCinematicLaptopVideo
{
    private const string ScenePath = "Assets/Scenes/DeathCinematic.unity";
    private const string VideoPath = "Assets/Videos/Laptop video.mp4";
    private const string ScreenMaterialPath = "Assets/Videos/LaptopScreen.mat";

    // Case-insensitive hints for locating the laptop and its screen sub-mesh by name.
    private static readonly string[] LaptopHints = { "laptop" };
    private static readonly string[] ScreenHints =
        { "screen", "display", "monitor", "glass", "panel", "lcd", "cctv", "feed" };

    [MenuItem("Tools/Death Cinematic/Setup Laptop Video")]
    public static void SetupLaptopVideo()
    {
        // Load the video clip first — nothing else matters if it's missing.
        VideoClip clip = AssetDatabase.LoadAssetAtPath<VideoClip>(VideoPath);
        if (clip == null)
        {
            EditorUtility.DisplayDialog("Setup Laptop Video",
                "Could not load the video clip at:\n" + VideoPath +
                "\n\nMake sure the file exists and has imported as a VideoClip.", "OK");
            return;
        }

        // Make sure we're operating on the DeathCinematic scene.
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }
            if (!File.Exists(ScenePath))
            {
                EditorUtility.DisplayDialog("Setup Laptop Video", "Missing scene:\n" + ScenePath, "OK");
                return;
            }
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        Renderer screen = ResolveScreenRenderer(scene, out string report);
        if (screen == null)
        {
            EditorUtility.DisplayDialog("Setup Laptop Video",
                "Couldn't identify the laptop screen automatically.\n\n" + report +
                "\n\nSelect the laptop SCREEN mesh in the Hierarchy and run this again.", "OK");
            return;
        }

        // Dedicated unlit material so the feed reads as a lit screen and no shared/other
        // material is touched.
        Material screenMat = GetOrCreateScreenMaterial(out string baseMapProperty);
        AssignMaterialSlot(screen, screenMat);

        // Add/reuse the VideoPlayer on the screen object and configure it.
        VideoPlayer vp = screen.GetComponent<VideoPlayer>();
        if (vp == null)
        {
            vp = Undo.AddComponent<VideoPlayer>(screen.gameObject);
        }
        vp.source = VideoSource.VideoClip;
        vp.clip = clip;
        vp.playOnAwake = true;
        vp.isLooping = true;
        vp.waitForFirstFrame = true;
        vp.skipOnDrop = true;
        vp.playbackSpeed = 1f;
        // Render straight onto the screen renderer's material texture (no RenderTexture asset).
        vp.renderMode = VideoRenderMode.MaterialOverride;
        vp.targetMaterialRenderer = screen;
        vp.targetMaterialProperty = baseMapProperty;
        // No audio: a silent security feed unless a laptop audio source is intentionally added.
        vp.audioOutputMode = VideoAudioOutputMode.None;
        vp.controlledAudioTrackCount = 0;

        EditorUtility.SetDirty(screen);
        EditorUtility.SetDirty(vp);
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log("[DeathCinematicLaptopVideo] Wired '" + VideoPath + "' onto renderer '" +
            GetPath(screen.transform) + "' using material '" + ScreenMaterialPath +
            "' (property " + baseMapProperty + "). Scene saved: " + saved + ".");
        EditorUtility.DisplayDialog("Setup Laptop Video",
            "Laptop video wired up.\n\nScreen object: " + GetPath(screen.transform) +
            "\nMaterial: " + ScreenMaterialPath +
            "\n\nPress Play — the screen loops the feed with no audio.", "OK");
    }

    // Find the renderer that should show the video.
    private static Renderer ResolveScreenRenderer(Scene scene, out string report)
    {
        var sb = new StringBuilder();

        // 1) Explicit selection wins.
        GameObject sel = Selection.activeGameObject;
        if (sel != null && sel.scene == scene)
        {
            Renderer selRenderer = sel.GetComponent<Renderer>();
            if (selRenderer == null)
            {
                selRenderer = sel.GetComponentInChildren<Renderer>();
            }
            if (selRenderer != null)
            {
                report = "Using selected object: " + GetPath(selRenderer.transform);
                return selRenderer;
            }
            sb.AppendLine("Selected object '" + sel.name + "' has no Renderer.");
        }

        // 2) Find a laptop root, then a screen-named child renderer.
        List<Transform> laptops = FindByHints(scene, LaptopHints);
        if (laptops.Count == 0)
        {
            sb.AppendLine("No GameObject with 'laptop' in its name was found in the scene.");
            report = sb.ToString();
            return null;
        }

        foreach (Transform laptop in laptops)
        {
            Renderer[] renderers = laptop.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                if (NameMatches(r.gameObject.name, ScreenHints))
                {
                    report = "Auto-found screen '" + GetPath(r.transform) + "' under '" + laptop.name + "'.";
                    return r;
                }
            }

            // 3) A laptop with a single renderer: that's the screen.
            if (renderers.Length == 1)
            {
                report = "Using the laptop's only renderer '" + GetPath(renderers[0].transform) + "'.";
                return renderers[0];
            }

            sb.AppendLine("Laptop '" + laptop.name + "' has " + renderers.Length +
                " renderers but none named like a screen:");
            foreach (Renderer r in renderers)
            {
                sb.AppendLine("  - " + r.gameObject.name);
            }
        }

        report = sb.ToString();
        return null;
    }

    private static Material GetOrCreateScreenMaterial(out string baseMapProperty)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(ScreenMaterialPath);

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        baseMapProperty = "_BaseMap";
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Texture");
            baseMapProperty = "_MainTex";
        }

        if (existing != null)
        {
            if (shader != null && existing.shader != shader)
            {
                existing.shader = shader;
                EditorUtility.SetDirty(existing);
            }
            // Keep property choice consistent with whatever shader the material actually has.
            baseMapProperty = existing.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
            return existing;
        }

        Directory.CreateDirectory("Assets/Videos");
        Material mat = new Material(shader != null ? shader : Shader.Find("Standard"));
        mat.name = "LaptopScreen";
        AssetDatabase.CreateAsset(mat, ScreenMaterialPath);
        AssetDatabase.ImportAsset(ScreenMaterialPath);
        baseMapProperty = mat.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
        return mat;
    }

    // Put the screen material on the renderer's first slot, preserving any extra slots so we
    // never disturb other sub-materials of the mesh.
    private static void AssignMaterialSlot(Renderer renderer, Material mat)
    {
        Undo.RecordObject(renderer, "Assign Laptop Screen Material");
        Material[] mats = renderer.sharedMaterials;
        if (mats == null || mats.Length == 0)
        {
            renderer.sharedMaterial = mat;
        }
        else
        {
            mats[0] = mat;
            renderer.sharedMaterials = mats;
        }
    }

    private static List<Transform> FindByHints(Scene scene, string[] hints)
    {
        var found = new List<Transform>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (NameMatches(t.gameObject.name, hints))
                {
                    found.Add(t);
                }
            }
        }
        return found;
    }

    private static bool NameMatches(string name, string[] hints)
    {
        string n = name.ToLowerInvariant();
        foreach (string h in hints)
        {
            if (n.Contains(h))
            {
                return true;
            }
        }
        return false;
    }

    private static string GetPath(Transform t)
    {
        var sb = new StringBuilder(t.name);
        Transform p = t.parent;
        while (p != null)
        {
            sb.Insert(0, p.name + "/");
            p = p.parent;
        }
        return sb.ToString();
    }
}
