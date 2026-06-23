using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;  //added missing using statement for SceneManager

public class GameHud : MonoBehaviour
{
    public Text roundText;
    public Text killsText;
    public Text scoreText;
    public Button returnToMenuButton;

    private void Start()
    {
        // Initialize UI elements and add listeners if necessary
        if (returnToMenuButton != null)  //check for null references
            returnToMenuButton.onClick.AddListener(OnReturnToMenu);
    }

    public void UpdateUI(int currentRound, int kills, int score)
    {
        roundText.text = "Round: " + currentRound;
        killsText.text = "Kills: " + kills;
        scoreText.text = "Score: " + score;
        
        if(score > PlayerPrefs.GetInt("BestScore", 0))   //update best score
        {
            PlayerPrefs.SetInt("BestScore", score);
        }

        if (currentRound > PlayerPrefs.GetInt("BestRound", 0))
        {
            PlayerPrefs.SetInt("BestRound", currentRound);
        }
    }

    private void OnReturnToMenu()
    {
        SceneManager.LoadScene(0);   //load scene index 0, which is assumed to be the main menu
    }
}