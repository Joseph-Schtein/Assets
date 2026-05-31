using UnityEngine;
using UnityEngine.UI;
using System;
using TMPro;

public class RoundTimer : MonoBehaviour
{
    [Header("UI Reference")]
    [Tooltip("Assign the canvas Text that shows the round timer.")]
    public TextMeshProUGUI timerText;

    [Tooltip("Reference to the End Screen UI script.")]
    public EndScreenUI endScreenUI;

    [Tooltip("Reference to the in-game Leaderboard UI GameObject to hide when the round ends.")]
    public GameObject inGameLeaderboard;

    [Header("Settings")]
    public float defaultRoundTime = 180f; // Default 3 minutes if not set in menu

    private float timeRemaining;
    private bool roundEnded = false;

    void Start()
    {
        // Read the round duration from PlayerPrefs.
        // Your Main Menu settings UI can save it using:
        // PlayerPrefs.SetFloat("RoundDuration", timeInSeconds);
        timeRemaining = PlayerPrefs.GetFloat("RoundDuration", defaultRoundTime);
    }

    void Update()
    {
        // Timer disabled for ML Training
        if (roundEnded) return;

        timeRemaining -= Time.deltaTime;

        if (timeRemaining <= 0)
        {
            timeRemaining = 0;
            EndRound();
        }

        UpdateTimerUI();
    }

    void UpdateTimerUI()
    {
        if (timerText == null) return;

        // Format as MM:SS (e.g., 03:00)
        TimeSpan time = TimeSpan.FromSeconds(timeRemaining);
        timerText.text = time.ToString(@"mm\:ss");
    }

    void EndRound()
    {
        roundEnded = true;
        Debug.Log("<color=cyan><b>ROUND OVER! Time is up.</b></color>");

        // Pause the game when time runs out
        Time.timeScale = 0f;

        // Hide the in-game UI
        if (timerText != null) timerText.gameObject.SetActive(false);
        if (inGameLeaderboard != null) inGameLeaderboard.SetActive(false);

        // Show the End Game UI Panel
        if (endScreenUI != null)
        {
            endScreenUI.ShowEndScreen();
        }
        else
        {
            Debug.LogWarning("EndScreenUI reference is not assigned in RoundTimer!");
        }
    }
}
