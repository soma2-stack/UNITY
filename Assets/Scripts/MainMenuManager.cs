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