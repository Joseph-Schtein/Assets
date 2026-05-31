using UnityEngine;
using TMPro; // Uses TextMeshPro for UI

public class ScoreManager : MonoBehaviour
{
    [Header("UI Text Components")]
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI multiplierText;

    private float currentScore = 0f;
    private float riskMultiplier = 1f;
    private PlayerEnergy energy;

    void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            energy = playerObj.GetComponent<PlayerEnergy>();
        }
    }

    void Update()
    {
        if (energy == null) return;

        // Read the true score: Kills are worth 3 points, deaths subtract 1
        int trueScore = (energy.killCount * 3) - energy.deathCount;

        // Update display text strings
        scoreText.text = "SCORE: " + trueScore.ToString();

        // Display current charge percentage
        int chargePercent = Mathf.RoundToInt((energy.currentEnergy / energy.maxEnergy) * 100f);
        multiplierText.text = $"CHARGE: {chargePercent}%";
    }
}