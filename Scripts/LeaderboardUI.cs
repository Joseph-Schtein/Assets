using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

using TMPro;

public class LeaderboardUI : MonoBehaviour
{
    public TextMeshProUGUI leaderboardText;
    public float updateInterval = 0.5f;
    private float timer;

    void Start()
    {
        timer = updateInterval;
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer <= 0)
        {
            UpdateLeaderboard();
            timer = updateInterval;
        }
    }

    void UpdateLeaderboard()
    {
        if (leaderboardText == null) return;

        PlayerEnergy[] allPlayers = FindObjectsByType<PlayerEnergy>(FindObjectsSortMode.None);
        List<PlayerEnergy> players = new List<PlayerEnergy>(allPlayers);

        // Sort players using the CompareScore logic (Most Kills, Least Deaths) - Descending order
        players.Sort((a, b) => a.CompareScore(b));

        string lbText = "<color=white><b>TOP 5 SURVIVORS</b></color>\n";
        lbText += "<color=white>Rank | Name | Score | Charge</color>\n";
        lbText += "<color=white>----------------------------------------</color>\n";

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
}
