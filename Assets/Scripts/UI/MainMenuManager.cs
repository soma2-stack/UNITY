using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuManager : MonoBehaviour
{
    [Header("Scene Configuration")]
    [SerializeField] private string gameplaySceneName = "SchoolOfTheDead";

    // This runs when the player clicks the 'Play' button
    public void PlayGame()
    {
        // Make sure the scene name matches your actual gameplay scene exactly
        if (!string.IsNullOrEmpty(gameplaySceneName))
        {
            SceneManager.LoadScene(gameplaySceneName);
        }
        else
        {
            Debug.LogError("Gameplay scene name is empty! Please set it in the inspector.");
        }
    }

    // --- ONLINE CO-OP (multiplayer) ---
    // PlayGame() above is the SINGLE-PLAYER path only. These open/close the real
    // multiplayer lobby overlay (built by MainMenuUI / MultiplayerMenuController) and
    // deliberately never load a gameplay scene here, so ONLINE CO-OP can never fall
    // through to the solo scene.

    public void OpenMultiplayerMenu()
    {
        Debug.Log("ONLINE CO-OP opened");

        var menu = FindFirstObjectByType<MainMenuUI>();
        if (menu != null)
        {
            menu.ShowMultiplayerMenu();
        }
        else
        {
            Debug.LogWarning("[MainMenuManager] MainMenuUI not found in the scene; cannot open the multiplayer menu.");
        }
    }

    public void CloseMultiplayerMenu()
    {
        Debug.Log("Returned to main menu from ONLINE CO-OP");

        var menu = FindFirstObjectByType<MainMenuUI>();
        if (menu != null)
        {
            menu.HideMultiplayerMenu();
        }
    }

    // This runs when the player clicks the 'Quit' button
    public void QuitGame()
    {
        Debug.Log("Exiting the game...");

        // If playing inside the Unity Editor, stop Play Mode
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        
        // If playing the actual final built game, close the application window
        #else
        Application.Quit();
        #endif
    }
}