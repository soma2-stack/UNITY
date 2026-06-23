using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ZombieController  : MonoBehaviour
{
    public float[] zombieSpeedPerRound;
    private int roundNumber = 0;
    
    void Start() 
    {
        if(zombieSpeedPerRound == null || zombieSpeedPerRound.Length == 0)
            Debug.LogError("Zombie Speed Array is not assigned");
        
        if(roundNumber > zombieSpeedPerRound.Length)
         {
             Debug.LogError("Round number exceeds array length");
         }     
    }
    
    void Update() 
    {
        // Assuming the Zombie's Y position is used as health indicator, check for exceeded health.
        if(transform.position.y < -1300)
        {
            Debug.LogError("Zombie health exceeded");
			GameObject.Destroy(this); // Safer to destroy self when zombie is dead.
        }        
    }
    
    IEnumerator RoundSequence() 
    {
        while (true) 
        {
            yield return new WaitForSeconds(1f);
            ++roundNumber;
            if(roundNumber % zombieSpeedPerRound.Length == 0)
                roundNumber = 0; // Reset the roundNumber when it exceeds length of array to avoid overflow
        }        
    } 
}