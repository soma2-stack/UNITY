using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// GAME-OVER + RESTART loop (Call of Duty Zombies style).
///
/// Self-bootstraps in the "SchoolOfTheDead" scene (mirrors PerkManager / GameHud),
/// subscribes to <see cref="PlayerHealth.OnPlayerDied"/>, then shows a game-over
/// screen with the final round (from <see cref="RoundManager"/>) and score (from
/// <see cref="PlayerPoints"/>). Buttons: RESTART (reload the gameplay scene) and
/// MAIN MENU (load "MainMenu"). Sets Time.timeScale = 0 while shown and restores it
/// on action. IMGUI only — the shared GameHud is untouched.
/// </summary>
public class GameOverController : MonoBehaviour
{
    private const string GameplayScene = "SchoolOfTheDead";
    private const string MainMenuScene = "MainMenu";
    private static GameOverController _runtimeInstance;

    // All players in the scene (co-op aware). The game ends only when EVERY one is dead.
    private readonly List<PlayerHealth> trackedPlayers = new List<PlayerHealth>();
    private readonly HashSet<PlayerHealth> deadPlayers = new HashSet<PlayerHealth>();
    private bool showScreen;
    private int finalRound;
    private int finalScore;
    private int finalKills;
    private int bestRound;
    private int bestScore;

    private GUIStyle titleStyle;
    private GUIStyle infoStyle;
    private GUIStyle buttonStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnAnySceneLoaded;
        SceneManager.sceneLoaded += OnAnySceneLoaded;
        SpawnIfGameplayScene(SceneManager.GetActiveScene());
    }

    private static void OnAnySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SpawnIfGameplayScene(scene);
    }

    private static void SpawnIfGameplayScene(Scene scene)
    {
        if (scene.name != GameplayScene)
        {
            if (_runtimeInstance != null)
            {
                Destroy(_runtimeInstance.gameObject);
                _runtimeInstance = null;
            }
            return;
        }

        // Make sure time is running for a fresh run (in case we left it paused).
        Time.timeScale = 1f;

        // Fresh run: reset the run kill counter so the game-over screen starts at zero.
        ZombieAgent.ResetKillCount();

        if (_runtimeInstance != null || FindFirstObjectByType<GameOverController>() != null)
        {
            return;
        }

        var go = new GameObject("GameOverController (Runtime)");
        _runtimeInstance = go.AddComponent<GameOverController>();
    }

    private void OnDestroy()
    {
        Unsubscribe();
        if (_runtimeInstance == this)
        {
            _runtimeInstance = null;
        }
    }

    private void Update()
    {
        // ✅ CHECKPOINT 1 — late-spawning player tracking fixed
        // Scan EVERY frame and subscribe to any PlayerHealth we aren't already
        // tracking. The old code set a one-shot `subscribed` flag on the first
        // frame players were found, so any co-op player that spawned later was
        // never tracked and couldn't contribute to game over. Tracking is now
        // idempotent (trackedPlayers.Contains guards against double-subscribing),
        // so late joiners are always picked up.
        PlayerHealth[] players = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
        foreach (PlayerHealth ph in players)
        {
            if (ph == null || trackedPlayers.Contains(ph))
            {
                continue;
            }
            trackedPlayers.Add(ph);
            ph.OnPlayerDied += HandlePlayerDied;
        }
    }

    private void Unsubscribe()
    {
        // Unsubscribe from every tracked player so nothing leaks across scene loads.
        foreach (PlayerHealth ph in trackedPlayers)
        {
            if (ph != null)
            {
                ph.OnPlayerDied -= HandlePlayerDied;
            }
        }
        trackedPlayers.Clear();
        deadPlayers.Clear();
    }

    private void HandlePlayerDied()
    {
        if (showScreen)
        {
            return;
        }

        // Co-op: one player going down must NOT end the game. Record dead players
        // and only show GAME OVER once EVERY tracked player has finally died.
        int trackedTotal = 0;
        int aliveCount = 0;
        foreach (PlayerHealth ph in trackedPlayers)
        {
            if (ph == null)
            {
                continue;
            }
            trackedTotal++;
            if (ph.IsDead)
            {
                deadPlayers.Add(ph);
            }
            else
            {
                aliveCount++;
            }
        }

        if (trackedTotal == 0 || aliveCount > 0)
        {
            return; // at least one player is still alive
        }

        RoundManager rm = FindFirstObjectByType<RoundManager>();
        finalRound = rm != null ? rm.CurrentRound : 0;
        finalScore = PlayerPoints.Instance != null ? PlayerPoints.Instance.Points : 0;
        finalKills = ZombieAgent.TotalKillsThisRun;

        // Best round / best score persistence (PlayerPrefs). Update the local copies so
        // a new record is reflected immediately on the game-over screen.
        bestRound = PlayerPrefs.GetInt("BestRound", 0);
        bestScore = PlayerPrefs.GetInt("BestScore", 0);
        if (finalRound > bestRound)
        {
            bestRound = finalRound;
            PlayerPrefs.SetInt("BestRound", bestRound);
        }
        if (finalScore > bestScore)
        {
            bestScore = finalScore;
            PlayerPrefs.SetInt("BestScore", bestScore);
        }
        PlayerPrefs.Save();

        showScreen = true;
        Time.timeScale = 0f;

        // Free the cursor so the buttons are clickable.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Restart()
    {
        Time.timeScale = 1f;
        showScreen = false;
        ZombieAgent.ResetKillCount(); // fresh run starts at zero kills
        SceneManager.LoadScene(GameplayScene);
    }

    private void ToMainMenu()
    {
        Time.timeScale = 1f;
        showScreen = false;
        ZombieAgent.ResetKillCount(); // clear the run kill count when leaving to the menu
        SceneManager.LoadScene(MainMenuScene);
    }

    private void OnGUI()
    {
        if (!showScreen)
        {
            return;
        }

        EnsureStyles();

        // Dim the screen.
        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = prev;

        float cx = Screen.width * 0.5f;
        float y = Screen.height * 0.28f;

        DrawCentered("GAME OVER", titleStyle, cx, y, 560f, 70f);
        y += 90f;

        DrawCentered("You survived to Round " + finalRound, infoStyle, cx, y, 560f, 34f);
        y += 40f;
        DrawCentered("Final Score: " + finalScore, infoStyle, cx, y, 560f, 34f);
        y += 40f;
        DrawCentered("Zombies Killed: " + finalKills, infoStyle, cx, y, 560f, 34f);
        y += 40f;
        DrawCentered("Best Round: " + bestRound + "   Best Score: " + bestScore, infoStyle, cx, y, 560f, 34f);
        y += 70f;

        float bw = 240f;
        float bh = 56f;
        if (GUI.Button(new Rect(cx - bw - 10f, y, bw, bh), "RESTART", buttonStyle))
        {
            Restart();
        }
        if (GUI.Button(new Rect(cx + 10f, y, bw, bh), "MAIN MENU", buttonStyle))
        {
            ToMainMenu();
        }
    }

    private void DrawCentered(string text, GUIStyle style, float cx, float y, float w, float h)
    {
        GUI.Label(new Rect(cx - w * 0.5f, y, w, h), text, style);
    }

    private void EnsureStyles()
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 56,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            titleStyle.normal.textColor = new Color(0.9f, 0.2f, 0.2f);
        }
        if (infoStyle == null)
        {
            infoStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                alignment = TextAnchor.MiddleCenter,
            };
            infoStyle.normal.textColor = new Color(0.95f, 0.93f, 0.86f);
        }
        if (buttonStyle == null)
        {
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
            };
        }
    }
}
