using UnityEngine;

public class QSUIManager_AfterQuickSort : MonoBehaviour
{
    public DialogueManager dialogueManager;

    [Header("Choices To Show")]
    public GameObject choiceAButton;
    public GameObject choiceBButton;

    [Header("Optional UI")]
    public GameObject continueButton;
    public GameObject proceedPanel;

    private bool choicesShown = false;

    void Awake()
    {
        if (choiceAButton != null) choiceAButton.SetActive(false);
        if (choiceBButton != null) choiceBButton.SetActive(false);
        if (proceedPanel != null) proceedPanel.SetActive(false);
    }

    void Start()
    {
        // Hide buttons at start
        if (choiceAButton != null) choiceAButton.SetActive(false);
        if (choiceBButton != null) choiceBButton.SetActive(false);
        if (proceedPanel != null) proceedPanel.SetActive(false);

        // Show continue only while displaying the intro line
        if (continueButton != null) continueButton.SetActive(true);

        DialogueLine[] introLines =
        {
            new DialogueLine("Mouse", "Mouse_Neutral", "What do we do next?")
        };

        if (dialogueManager != null)
        {
            dialogueManager.enabled = true;
            dialogueManager.StartDialogue(introLines);
        }
        else
        {
            Debug.LogError("QSUIManager_AfterQuickSort: DialogueManager is not assigned.");
        }
    }

    void Update()
    {
        if (choicesShown) return;
        if (dialogueManager == null || dialogueManager.dialogueText == null) return;

        string currentText = dialogueManager.dialogueText.text;

        // As soon as the single line is visible, lock dialogue and show choices
        if (currentText.Contains("What do we do next?"))
        {
            choicesShown = true;

            if (continueButton != null)
                continueButton.SetActive(false);

            // Freeze dialogue so pressing E does nothing
            dialogueManager.enabled = false;

            if (choiceAButton != null)
                choiceAButton.SetActive(true);

            if (choiceBButton != null)
                choiceBButton.SetActive(true);

            Debug.Log("QSUIManager_AfterQuickSort: Intro line shown. Choices are now visible.");
        }
    }
}