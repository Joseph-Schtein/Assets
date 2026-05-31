using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using TMPro;

public class MatchSettingsUI : MonoBehaviour
{
    [Header("UI Elements")]
    public TMP_Dropdown timeDropdown;
    public Slider aiCountSlider;
    public Text aiCountText;
    public TMP_InputField playerNameInput;

    void Start()
    {
        int savedTimeIndex = PlayerPrefs.GetInt("RoundDurationIndex", 0);
        int savedAICount = PlayerPrefs.GetInt("AIPlayerCount", 10);
        string savedPlayerName = PlayerPrefs.GetString("PlayerName", "Player");

        if (playerNameInput != null)
        {
            playerNameInput.text = savedPlayerName;
        }

        if (timeDropdown != null)
        {
            timeDropdown.ClearOptions();
            List<string> options = new List<string> { "1 Minute", "3 Minutes", "5 Minutes" };
            timeDropdown.AddOptions(options);
            timeDropdown.value = savedTimeIndex;
            timeDropdown.RefreshShownValue();
        }

        if (aiCountSlider != null)
        {
            aiCountSlider.minValue = 5;
            aiCountSlider.maxValue = 15;
            aiCountSlider.wholeNumbers = true;
            aiCountSlider.value = Mathf.Clamp(savedAICount, 5, 15);
            UpdateAIText(aiCountSlider.value);

            // Listen for slider changes
            aiCountSlider.onValueChanged.AddListener(UpdateAIText);
        }
    }

    public void UpdateAIText(float value)
    {
        if (aiCountText != null)
        {
            aiCountText.text = "AI Opponents: " + Mathf.RoundToInt(value).ToString();
        }
    }

    public void StartMatch()
    {
        // Save Player Name
        if (playerNameInput != null && !string.IsNullOrWhiteSpace(playerNameInput.text))
        {
            PlayerPrefs.SetString("PlayerName", playerNameInput.text);
        }

        // Save Time Dropdown (Index 0: 60s, 1: 180s, 2: 300s)
        if (timeDropdown != null)
        {
            float[] times = { 60f, 180f, 300f };
            int selectedIndex = timeDropdown.value;
            PlayerPrefs.SetFloat("RoundDuration", times[selectedIndex]);
            PlayerPrefs.SetInt("RoundDurationIndex", selectedIndex);
        }

        // Save AI Count
        if (aiCountSlider != null)
        {
            PlayerPrefs.SetInt("AIPlayerCount", Mathf.RoundToInt(aiCountSlider.value));
        }

        PlayerPrefs.Save();

        // Load the actual game scene
        SceneManager.LoadScene("SampleScene");
    }

    public void ReturnToMainMenu()
    {
        SceneManager.LoadScene("MainMenu");
    }
}
