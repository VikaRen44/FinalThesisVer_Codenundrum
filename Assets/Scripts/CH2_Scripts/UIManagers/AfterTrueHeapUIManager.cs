using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class AfterTrueHeapManager : MonoBehaviour
{
    public DialogueManager dialogueManager;

    [Header("First Choice")]
    public GameObject choiceAButton;
    public GameObject choiceBButton;

    [Header("Proceed Panel")]
    public GameObject proceedPanel;

    public GameObject continueButton;

    private bool firstChoicesShown = false;
    private bool waitingForProceed = false;

    void Start()
    {
        // Hide UI at start
        if (choiceAButton != null) choiceAButton.SetActive(false);
        if (choiceBButton != null) choiceBButton.SetActive(false);

        if (proceedPanel != null)
            proceedPanel.SetActive(false);

        // Intro reflection dialogue
        DialogueLine[] reflectionLines =
        {
            new DialogueLine("Mouse", "Mouse_Neutral",
                "She looks locked in right now."),

            new DialogueLine("Mouse", "Mouse_Sad",
                "She must be feeling very nervous on the sudden role that was pitched to her.")
        };

        if (dialogueManager != null)
            dialogueManager.StartDialogue(reflectionLines);
    }

    void Update()
    {
        if (dialogueManager == null) return;

        string currentText = dialogueManager.dialogueText.text;

        // Trigger first choices after the last intro line
        if (!firstChoicesShown && currentText.Contains("She must be feeling very nervous on the sudden role that was pitched to her."))
        {
            firstChoicesShown = true;

            if (continueButton != null)
                continueButton.SetActive(false);

            // IMPORTANT: freeze dialogue input while choices are shown
            dialogueManager.enabled = false;

            if (choiceAButton != null) choiceAButton.SetActive(true);
            if (choiceBButton != null) choiceBButton.SetActive(true);

            return;
        }

        // Detect the final dialogue line from either branch
        if (!waitingForProceed)
        {
            if (currentText.Contains("It'll lift some of the nervousness she has right now.") ||
                currentText.Contains("Let's go organize the props next!"))
            {
                waitingForProceed = true;
            }
        }

        // Only allow E to show proceed panel once branch dialogue is active again
        if (waitingForProceed && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
        {
            ShowProceedPanel();
        }
    }

    void ShowProceedPanel()
    {
        if (proceedPanel != null)
            proceedPanel.SetActive(true);

        waitingForProceed = false;
    }

    // Call this from Choice A button
    public void OnChoiceA()
    {
        if (choiceAButton != null) choiceAButton.SetActive(false);
        if (choiceBButton != null) choiceBButton.SetActive(false);

        // Re-enable dialogue so the branch dialogue can continue
        if (dialogueManager != null)
            dialogueManager.enabled = true;

        DialogueLine[] branchLines =
        {
            new DialogueLine("Mouse", "Mouse_Happy",
                "Maybe telling her she’s doing well will help."),
            new DialogueLine("Mouse", "Mouse_Neutral",
                "It'll lift some of the nervousness she has right now.")
        };

        dialogueManager.StartDialogue(branchLines);
    }

    // Call this from Choice B button
    public void OnChoiceB()
    {
        if (choiceAButton != null) choiceAButton.SetActive(false);
        if (choiceBButton != null) choiceBButton.SetActive(false);

        // Re-enable dialogue so the branch dialogue can continue
        if (dialogueManager != null)
            dialogueManager.enabled = true;

        DialogueLine[] branchLines =
        {
            new DialogueLine("Mouse", "Mouse_Neutral",
                "Maybe helping with the next task is better."),
            new DialogueLine("Mouse", "Mouse_Happy",
                "Let's go organize the props next!")
        };

        dialogueManager.StartDialogue(branchLines);
    }

    // Proceed button → QuickSort scene
    public void GoToQuickSort()
    {
        SceneManager.LoadScene("06_QuickSort");
    }

    // Replay button → reload current scene
    public void ReplayScene()
    {
        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.name);
    }
}