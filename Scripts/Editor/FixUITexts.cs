using UnityEngine;
using UnityEditor;
using TMPro;

[InitializeOnLoad]
public class FixUITexts
{
    static FixUITexts()
    {
        EditorApplication.delayCall += FixUI;
    }

    static void FixUI()
    {
        // Find the RoundTimer script
        RoundTimer timer = Object.FindAnyObjectByType<RoundTimer>();
        if (timer != null)
        {
            Transform timerTextObj = timer.transform.Find("RoundTimerText");
            if (timerTextObj == null && timer.transform.parent != null)
            {
                timerTextObj = timer.transform.parent.Find("RoundTimerText");
            }
            
            if (timerTextObj != null)
            {
                // Remove legacy Text if it exists
                UnityEngine.UI.Text legacyText = timerTextObj.GetComponent<UnityEngine.UI.Text>();
                if (legacyText != null) Object.DestroyImmediate(legacyText);

                // Destroy existing TMP if it's corrupted from manual YAML edits
                TextMeshProUGUI existingTmp = timerTextObj.GetComponent<TextMeshProUGUI>();
                if (existingTmp != null) Object.DestroyImmediate(existingTmp);

                // Add fresh TMP
                TextMeshProUGUI tmp = timerTextObj.gameObject.AddComponent<TextMeshProUGUI>();

                tmp.text = "03:00";
                tmp.fontSize = 48;
                tmp.alignment = TextAlignmentOptions.TopLeft;
                
                // Assign reference
                timer.timerText = tmp;
                EditorUtility.SetDirty(timer);
                EditorUtility.SetDirty(timerTextObj.gameObject);
            }
        }

        // Find the LeaderboardUI script
        LeaderboardUI leaderboard = Object.FindAnyObjectByType<LeaderboardUI>();
        if (leaderboard != null)
        {
            Transform lbTextObj = leaderboard.transform.Find("LeaderboardText");
            if (lbTextObj == null && leaderboard.transform.parent != null)
            {
                lbTextObj = leaderboard.transform.parent.Find("LeaderboardText");
            }

            if (lbTextObj != null)
            {
                // Remove legacy Text
                UnityEngine.UI.Text legacyText = lbTextObj.GetComponent<UnityEngine.UI.Text>();
                if (legacyText != null) Object.DestroyImmediate(legacyText);

                // Destroy existing corrupted TMP
                TextMeshProUGUI existingTmp = lbTextObj.GetComponent<TextMeshProUGUI>();
                if (existingTmp != null) Object.DestroyImmediate(existingTmp);

                // Add fresh TMP
                TextMeshProUGUI tmp = lbTextObj.gameObject.AddComponent<TextMeshProUGUI>();

                tmp.text = "Top 5";
                tmp.fontSize = 24;
                tmp.alignment = TextAlignmentOptions.TopRight;
                
                RectTransform rt = lbTextObj.GetComponent<RectTransform>();
                if (rt != null) rt.sizeDelta = new Vector2(600, 400);

                leaderboard.leaderboardText = tmp;
                EditorUtility.SetDirty(leaderboard);
                EditorUtility.SetDirty(lbTextObj.gameObject);
            }
        }
        
        Debug.Log("[Auto-Fix] Successfully fixed Timer and Leaderboard UI components!");
    }
}
