using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class MainMenuUI : MonoBehaviour
{
    public Button playGameButton;
    public Button instructionButton;
    public Button exitInstructionButton;

    public GameObject instructionPanel; // Changed Panel to GameObject

    // Function to show Options and hide others
    public void CloseInstruction()
    {
        if (instructionButton != null) instructionButton.gameObject.SetActive(true);
        if (exitInstructionButton != null) exitInstructionButton.gameObject.SetActive(false);
        if (playGameButton != null) playGameButton.gameObject.SetActive(true);
        if (instructionPanel != null) instructionPanel.SetActive(false);
    }

    // Function to show Credits and hide others
    public void OpenInstruction()
    {
        if (playGameButton != null) playGameButton.gameObject.SetActive(false);
        if (instructionButton != null) instructionButton.gameObject.SetActive(false);
        if (exitInstructionButton != null) exitInstructionButton.gameObject.SetActive(true);
        if (instructionPanel != null) instructionPanel.SetActive(true);
    }

    // Function to start the actual game
    public void PlayGame()
    {
        SceneManager.LoadScene("MatchSettingsScene");
    }
}
