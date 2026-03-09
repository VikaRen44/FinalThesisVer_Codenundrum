using UnityEngine;

public class FaithPostMinHeapDialogueManager : MonoBehaviour
{
    public DialogueManager dialogueManager;

    [Header("First Choice")]
    public GameObject choiceAButton;
    public GameObject choiceBButton;

    [Header("Second Choice (Minigame Select)")]
    public GameObject scriptGameButton;
    public GameObject propsGameButton;

    public GameObject continueButton;

    private bool firstChoicesShown = false;
    private bool secondChoicesShown = false;

    void Start()
    {
        // Hide everything at the start
        choiceAButton.SetActive(false);
        choiceBButton.SetActive(false);
        scriptGameButton.SetActive(false);
        propsGameButton.SetActive(false);

        DialogueLine[] afterLines =
        {
            new DialogueLine("Mouse", "Mouse_Neutral", "Hey, Faith! What's next?"),
            new DialogueLine("Faith", "Surprised_Faith", "You are done already?")
        };

        dialogueManager.StartDialogue(afterLines);
    }

    void Update()
    {
        if (dialogueManager == null) return;

        string currentText = dialogueManager.dialogueText.text;

        // FIRST CHOICE
        if (!firstChoicesShown && currentText.Contains("You are done already?"))
        {
            firstChoicesShown = true;

            continueButton.SetActive(false);
            dialogueManager.enabled = false;

            choiceAButton.SetActive(true);
            choiceBButton.SetActive(true);
        }

        // SECOND CHOICE (minigame select)
        if (!secondChoicesShown && currentText.Contains("Which one would you like to do next?"))
        {
            secondChoicesShown = true;

            continueButton.SetActive(false);
            dialogueManager.enabled = false;

            scriptGameButton.SetActive(true);
            propsGameButton.SetActive(true);
        }
    }
}