using UnityEngine;

public class TestRoundStarter : MonoBehaviour
{
    public ZombieSpawner spawner;

    void Start()
    {
        if (spawner != null)
            spawner.BeginRound(6, 150, 3.5f);
    }
}