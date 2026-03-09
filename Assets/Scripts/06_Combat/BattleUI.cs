using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BattleUI : MonoBehaviour
{
    [Header("Main Menu")]
    public GameObject mainMenuRoot;
    public Button attackButton;
    public Button actButton;
    public Button itemButton;

    [Header("Act Menu")]
    public GameObject actMenuRoot;
    public Transform actListParent; // not used yet, but keep for later

    [Header("Dialogue")]
    public GameObject dialogueRoot;
    public TMP_Text dialogueText; // OPTIONAL: only used if you want fallback text here

    [Header("End Screen")]
    public GameObject endRoot;
    public TMP_Text endText;

    private BattleController bc;

    // Called by BattleController
    public void Bind(BattleController controller)
    {
        bc = controller;

        // Hook up menu buttons once
        if (attackButton != null)
            attackButton.onClick.AddListener(() => bc.OnPlayerChooseAttack());

        if (actButton != null)
            actButton.onClick.AddListener(() => bc.OpenActMenu());

        if (itemButton != null)
            itemButton.onClick.AddListener(() => bc.OpenItemMenu());

        InitializeUI();
    }

    // Hide / show correct panels at game start
    public void InitializeUI()
    {
        if (mainMenuRoot) mainMenuRoot.SetActive(true);
        if (actMenuRoot) actMenuRoot.SetActive(false);
        if (endRoot) endRoot.SetActive(false);

        // Dialogue root should stay ON (your BattleDialogueUI handles text)
        if (dialogueRoot) dialogueRoot.SetActive(true);
    }

    // Called when player gets control
    public void ShowMainMenu()
    {
        if (mainMenuRoot) mainMenuRoot.SetActive(true);
        if (actMenuRoot) actMenuRoot.SetActive(false);
        if (endRoot) endRoot.SetActive(false);

        if (dialogueRoot) dialogueRoot.SetActive(true);
    }

    public void HideMenus()
    {
        if (mainMenuRoot) mainMenuRoot.SetActive(false);
        if (actMenuRoot) actMenuRoot.SetActive(false);
    }

    public void ShowActMenu()
    {
        HideMenus();
        if (actMenuRoot) actMenuRoot.SetActive(true);
    }

    public void ShowEndScreen(bool won)
    {
        HideMenus();
        if (mainMenuRoot) mainMenuRoot.SetActive(false);
        if (actMenuRoot) actMenuRoot.SetActive(false);

        if (endRoot) endRoot.SetActive(true);
        if (endText) endText.text = won ? "YOU WON!" : "YOU LOST!";
    }

    // Optional fallback dialogue (only if you want UI text here too)
    public void ShowDialogue(string line)
    {
        if (dialogueRoot) dialogueRoot.SetActive(true);
        if (dialogueText) dialogueText.text = line;
    }
}
