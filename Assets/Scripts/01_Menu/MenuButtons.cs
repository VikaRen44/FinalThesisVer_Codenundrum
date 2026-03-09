using UnityEngine;

public class MenuButtons : MonoBehaviour
{
    [Header("Scene Names")]
    public string characterCustomizationScene = "02_CharacterCustomization";

    [Header("Settings UI")]
    [Tooltip("Root GameObject of the Settings UI")]
    public GameObject settingsUI;

    [Header("Load Game UI")]
    [Tooltip("Root GameObject of the Load Game UI")]
    public GameObject loadGameUI;

    [Header("Optional UI Scripts (recommended)")]
    [Tooltip("Assign if your Load menu uses the LoadMenuUI script (Load-only).")]
    public LoadMenuUI loadMenuScript;

    [Tooltip("Assign if your Settings UI uses a controller script with OpenMenu().")]
    public SaveLoadMenuUI saveLoadMenuScript;

    [Header("New Game Reset")]
    [Tooltip("If true, clears the introPlayed flag so the intro can play again in a fresh run.")]
    public bool resetIntroPlayedOnNewGame = false;

    private const string PREF_INTRO_PLAYED = "introPlayed";

    // ✅ NEW: same key used by NameInput/CharacterSelectionButtons
    private const string PREF_KEEP_TYPED_ON_NEXT_OPEN = "NameInput_keepTypedNextOpen";

    public void NewGame()
    {
        // ✅ Make sure entering name scene from menu resets to default (Charlie)
        PlayerPrefs.SetInt(PREF_KEEP_TYPED_ON_NEXT_OPEN, 0);
        PlayerPrefs.Save();

        if (ObjectiveManager.Instance != null)
        {
            ObjectiveManager.Instance.ResetForNewGame("default");
        }

        if (resetIntroPlayedOnNewGame)
        {
            PlayerPrefs.DeleteKey(PREF_INTRO_PLAYED);
            PlayerPrefs.Save();
        }

        if (SceneTransition.Instance != null)
            SceneTransition.Instance.LoadScene(characterCustomizationScene);
        else
            Debug.LogWarning("MenuButtons: SceneTransition.Instance is missing.");
    }

    public void LoadGame()
    {
        if (loadMenuScript != null)
        {
            loadMenuScript.Open();
            return;
        }

        if (saveLoadMenuScript != null)
        {
            saveLoadMenuScript.OpenLoadOnly();
            return;
        }

        if (loadGameUI == null)
        {
            Debug.LogWarning("MenuButtons: Load Game UI is not assigned.");
            return;
        }

        loadGameUI.SetActive(true);
        Time.timeScale = 0f;
    }

    public void OpenSettings()
    {
        if (saveLoadMenuScript != null)
        {
            saveLoadMenuScript.OpenMenu();
            return;
        }

        if (settingsUI == null)
        {
            Debug.LogWarning("MenuButtons: Settings UI is not assigned.");
            return;
        }

        settingsUI.SetActive(true);
        Time.timeScale = 0f;
    }

    public void Quit()
    {
        if (SceneTransition.Instance != null)
            SceneTransition.Instance.QuitGame();
        else
            Application.Quit();
    }
}