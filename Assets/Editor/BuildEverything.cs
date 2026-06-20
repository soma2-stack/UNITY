using UnityEditor;
using UnityEngine;

/// <summary>
/// Runs every "School Of The Dead" setup tool in the correct order with one click:
/// fixes pink materials, builds the player/weapons/zombie, textures + furnishes the
/// map, places doors/perks/game-loop/secret machines, then bakes the NavMesh and
/// drops the player in. Each step is an existing menu item, invoked by path so this
/// stays in sync with the individual tools.
/// </summary>
public static class BuildEverything
{
    // Order matters: assets/prefabs first, then map population, NavMesh bake LAST
    // (after all geometry/colliders exist) so spawners + navmesh are correct.
    private static readonly string[] Steps =
    {
        "Tools/School Of The Dead/Fix Pink Materials (URP)",
        "Tools/School Of The Dead/Set Up Player",
        "Tools/School Of The Dead/Set Up Weapons",
        "Tools/School Of The Dead/Set Up Zombie",
        "Tools/School Of The Dead/Apply Room Textures",
        "Tools/School Of The Dead/Fix Wall Colliders",
        "Tools/School Of The Dead/Place Buyable Doors",
        "Tools/School Of The Dead/Furnish Rooms",
        "Tools/School Of The Dead/Place Perk Machines",
        "Tools/School Of The Dead/Place Game-Loop Machines",
        "Tools/School Of The Dead/Setup Secret Pack-a-Punch",
        "Tools/School Of The Dead/Setup Gameplay (NavMesh + Spawners)",
    };

    [MenuItem("Tools/School Of The Dead/BUILD EVERYTHING", false, -1000)]
    public static void Run()
    {
        if (!EditorUtility.DisplayDialog("Build Everything",
                "This runs all School Of The Dead setup tools in order (player, weapons, zombie, " +
                "textures, props, machines, NavMesh + spawners). It edits and saves the scene.\n\nContinue?",
                "Build it", "Cancel"))
        {
            return;
        }

        int ok = 0;
        for (int i = 0; i < Steps.Length; i++)
        {
            string step = Steps[i];
            Debug.Log($"[Build Everything] ({i + 1}/{Steps.Length}) {step}");
            bool ran = EditorApplication.ExecuteMenuItem(step);
            if (ran)
            {
                ok++;
            }
            else
            {
                Debug.LogWarning($"[Build Everything] Step not found / did not run: {step} (skipping). " +
                                 "Make sure that tool compiled.");
            }
        }

        Debug.Log($"[Build Everything] Done: {ok}/{Steps.Length} steps ran. " +
                  "Open the SchoolOfTheDead scene and press Play.");
        EditorUtility.DisplayDialog("Build Everything",
            $"Finished: {ok}/{Steps.Length} steps ran.\nOpen SchoolOfTheDead and press Play.", "OK");
    }
}
