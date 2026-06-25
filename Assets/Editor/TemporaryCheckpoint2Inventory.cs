#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class TemporaryCheckpoint2Inventory
{
    private static readonly string[] TargetRooms =
    {
        "starter", "math_room", "music_room", "history_room", "east_room_1", "east_room_2", "east_room_3",
        "south_office_classroom_1", "south_office_classroom_2", "south_office_classroom_3", "south_office_classroom_4",
        "west_cafeteria_classroom_1", "west_classroom_1", "west_classroom_2",
        "main_office", "principal_office", "west_south_office", "security_room"
    };

    static TemporaryCheckpoint2Inventory()
    {
        if (Application.isBatchMode)
        {
            EditorApplication.delayCall += Run;
        }
    }

    private static void Run()
    {
        try
        {
            Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/SchoolOfTheDead.unity", OpenSceneMode.Single);
            Transform generated = scene.GetRootGameObjects().First(root => root.name == "Generated_RoomProps").transform;
            using (StreamWriter writer = new StreamWriter("Temp/Checkpoint2Inventory.txt"))
            {
                foreach (string roomId in TargetRooms)
                {
                    Transform room = generated.Find(roomId);
                    writer.WriteLine($"[{roomId}] count={(room == null ? -1 : room.childCount)}");
                    if (room == null) continue;
                    for (int index = 0; index < room.childCount; index++)
                    {
                        Transform prop = room.GetChild(index);
                        Renderer[] renderers = prop.GetComponentsInChildren<Renderer>(true);
                        Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(prop.position, Vector3.zero);
                        foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                        writer.WriteLine($"  {prop.name}|pos=({prop.position.x:0.00},{prop.position.y:0.00},{prop.position.z:0.00})|yaw={prop.eulerAngles.y:0.0}|size=({bounds.size.x:0.00},{bounds.size.y:0.00},{bounds.size.z:0.00})");
                    }
                }
            }
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }
}
#endif
