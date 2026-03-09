using UnityEngine;

public class CharacterSelectionButtons : MonoBehaviour
{
    [Header("Scene Names")]
    public string backScene = "02_CharacterCustomization";
    public string chooseScene = "04_gamehub";

    // ✅ NEW: tells NameInput to keep the typed name on the next open
    private const string PREF_KEEP_TYPED_ON_NEXT_OPEN = "NameInput_keepTypedNextOpen";

    public void Back()
    {
        PlayerPrefs.SetInt(PREF_KEEP_TYPED_ON_NEXT_OPEN, 1);
        PlayerPrefs.Save();

        if (SceneTransition.Instance != null)
            SceneTransition.Instance.LoadScene(backScene);
    }

    public void Choose()
    {
        // optional: consume the flag so it doesn't accidentally apply later
        PlayerPrefs.SetInt(PREF_KEEP_TYPED_ON_NEXT_OPEN, 0);
        PlayerPrefs.Save();

        if (SceneTransition.Instance != null)
            SceneTransition.Instance.LoadScene(chooseScene);
    }
}