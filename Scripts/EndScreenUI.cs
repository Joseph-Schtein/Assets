using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using TMPro;

public class EndScreenUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("The parent GameObject that contains the End Screen UI.")]
    public GameObject endScreenPanel;
    [Tooltip("The TextMeshPro component to display the top 5 leaderboard.")]
    public TextMeshProUGUI leaderboardText;

    public Button replay;
    public Button mainMenu;

    void Start()
    {
        // Ensure the end screen is hidden when the game starts
        if (endScreenPanel != null)
        {
            endScreenPanel.SetActive(false);
            replay.gameObject.SetActive(false);
            mainMenu.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Activates the End Screen panel and populates the leaderboard.
    /// </summary>
    public void ShowEndScreen()
    {
        if (endScreenPanel != null)
        {
            UpdateLeaderboard();
            endScreenPanel.SetActive(true);
            replay.gameObject.SetActive(true);
            mainMenu.gameObject.SetActive(true);

        }
    }

    /// <summary>
    /// Gathers all players, sorts them, and displays the top 5.
    /// </summary>
    private void UpdateLeaderboard()
    {
        if (leaderboardText == null) return;

        PlayerEnergy[] allPlayers = FindObjectsByType<PlayerEnergy>(FindObjectsSortMode.None);
        List<PlayerEnergy> players = new List<PlayerEnergy>(allPlayers);

        // Sort players using the CompareScore logic (Most Kills, Least Deaths)
        players.Sort((a, b) => a.CompareScore(b));

        string lbText = "<color=white><b>MATCH OVER - TOP 5</b></color>\n";
        lbText += "<color=white>Rank | Name | Score | Charge</color>\n";
        lbText += "<color=white>--------------------------------------------------</color>\n";

        // Display up to 5 players
        int count = Mathf.Min(5, players.Count);
        for (int i = 0; i < count; i++)
        {
            PlayerEnergy p = players[i];

            // Format the color of the player's name
            string hexColor = ColorUtility.ToHtmlStringRGB(p.fullEnergyColor);
            int score = (p.killCount * 3) - p.deathCount;
            int chargePercent = Mathf.RoundToInt((p.currentEnergy / p.maxEnergy) * 100f);

            lbText += $"<color=white>{i + 1}. </color><color=#{hexColor}>{p.gameObject.name}</color> <color=white>- Score: {score} | Charge: {chargePercent}%</color>\n";
        }

        leaderboardText.text = lbText;
    }

    /// <summary>
    /// Called by the Replay button to restart the match.
    /// </summary>
    public void ReplayGame()
    {
        // Make sure time scale is back to normal before reloading
        Time.timeScale = 1f;
        SceneManager.LoadScene("SampleScene");
    }

    /// <summary>
    /// Called by the Main Menu button to return to the main menu.
    /// </summary>
    public void ReturnToMainMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("MainMenu");
    }
}
