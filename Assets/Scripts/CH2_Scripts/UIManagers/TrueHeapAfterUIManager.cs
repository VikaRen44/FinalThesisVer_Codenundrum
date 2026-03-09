using UnityEngine;
using UnityEngine.SceneManagement;

public class TrueHeapAfterUIManager : MonoBehaviour
{
    public DialogueManager dialogueManager;

    private bool sceneLoading = false;

    void Start()
    {
        DialogueLine[] afterLines = {

            new DialogueLine("Faith", "Faith_cropped",
                "That was impressive."),

            new DialogueLine("Mouse", "Excited_MOUSE",
                "Told you the highest page number belongs at the top!"),

            new DialogueLine("Faith", "Faith_cropped",
                "So that's how a max-heap works."),

            new DialogueLine("Faith", "Faith_cropped",
                "I think I'm starting to understand.")
        };

        dialogueManager.StartDialogue(afterLines);
    }

    void Update()
    {
        if (!sceneLoading && dialogueManager != null && !dialogueManager.IsDialogueActive())
        {
            sceneLoading = true;
            SceneManager.LoadScene("06_QuickSort");
        }
    }
}