using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CharacterCustomizationButtons : MonoBehaviour
{
    [Header("Scene Names")]
    public string mainMenuScene = "01_MainMenu";
    public string characterSelectionScene = "03_CharacterSelection";

    [Header("Refs")]
    public NameInput nameInput;
    public ConfirmDialogUI confirmDialog;

    [Header("Duplicate Popup")]
    public string duplicateTitle = "Name Already Exists";
    public string duplicateBody = "That name is already taken. Please choose a different name.";
    public string duplicateOkLabel = "OK";

    private bool _busy;

    public void BackToMenu()
    {
        if (_busy) return;

        if (SceneTransition.Instance != null)
            SceneTransition.Instance.LoadScene(mainMenuScene);
        else
            SceneManager.LoadScene(mainMenuScene);
    }

    public void Done()
    {
        if (_busy) return;
        StartCoroutine(DoneRoutine());
    }

    private IEnumerator DoneRoutine()
    {
        _busy = true;

        if (nameInput == null)
        {
            _busy = false;
            yield break;
        }

        // Commit text -> but DO NOT create profile yet
        bool prevCreate = nameInput.setActiveProfileOnPersist;
        nameInput.setActiveProfileOnPersist = false;
        nameInput.Done();
        nameInput.setActiveProfileOnPersist = prevCreate;

        yield return new WaitForEndOfFrame();

        string raw = nameInput.CurrentName;
        if (string.IsNullOrWhiteSpace(raw))
            raw = nameInput.defaultName;

        // ✅ Only block if profile exists AND has any slots
        if (SaveSystem.ProfileHasAnySlots(raw))
        {
            if (confirmDialog != null)
                confirmDialog.ShowOkOnly(duplicateTitle, duplicateBody, duplicateOkLabel, null);

            _busy = false;
            yield break;
        }

        // ✅ Create profile container using SaveSystem ONLY
        SaveSystem.SetActiveProfile(raw);
        SaveSystem.EnsureProfileFolderExists(raw);

        // Also persist the playerName so main menu doesn’t fall back to Charlie
        if (SaveGameManager.Instance != null)
            SaveGameManager.Instance.SetPlayerName(raw);
        else
        {
            PlayerPrefs.SetString("playerName", raw);
            PlayerPrefs.Save();
        }

        _busy = false;

        if (SceneTransition.Instance != null)
            SceneTransition.Instance.LoadScene(characterSelectionScene);
        else
            SceneManager.LoadScene(characterSelectionScene);
    }
}
