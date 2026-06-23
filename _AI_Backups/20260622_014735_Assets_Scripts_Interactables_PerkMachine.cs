using UnityEngine;

public class PerkMachine : MonoBehaviour
{
    public AudioClip purchaseSound;
    public GameObject powerRequiredPanel;

    private AudioSource audioSource;
    private PowerManager powerManager;
    private PlayerHealth playerHealth;
    private GameUI gameUI;

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        powerManager = FindObjectOfType<PowerManager>();
        playerHealth = GetComponent<PlayerHealth>();
        gameUI = FindObjectOfType<GameUI>();

        if (audioSource == null)
        {
            Debug.LogError("AudioSource not found on the Perk Machine GameObject.");
        }

        if (powerManager == null)
        {
            Debug.LogError("PowerManager not found in the scene.");
        }

        if (playerHealth == null)
        {
            Debug.LogError("PlayerHealth not found on the Perk Machine GameObject.");
        }

        if (gameUI == null)
        {
            Debug.LogError("GameUI not found in the scene.");
        }
    }

    public void PurchasePerk(string perkName, int cost)
    {
        if (!powerManager.IsPowerOn())
        {
            ShowMessage("Power required");
            return;
        }

        if (playerHealth.GetHealth() <= 0)
        {
            if (perkName != "Juggernog")
            {
                ResetPerks();
            }
            return;
        }

        if (gameUI.GetCurrentPoints() < cost)
        {
            ShowMessage("Not enough points");
            return;
        }

        gameUI.SpendPoints(cost);
        audioSource.PlayOneShot(purchaseSound);

        switch (perkName)
        {
            case "Mystery Box":
                // Implement Mystery Box logic here
                break;
            case "Wall Buy":
                // Implement Wall Buy logic here
                break;
            case "Pack-a-Punch":
                // Implement Pack-a-Punch logic here
                break;
            default:
                ShowMessage("Invalid perk");
                break;
        }
    }

    private void ShowMessage(string message)
    {
        Debug.Log(message);
        // Implement UI message display here
    }

    private void ResetPerks()
    {
        // Implement Perk reset logic here
    }
}