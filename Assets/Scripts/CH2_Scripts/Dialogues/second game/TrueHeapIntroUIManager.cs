using UnityEngine;
using UnityEngine.SceneManagement;

public class TrueHeapIntroUIManager : MonoBehaviour
{
    public DialogueManager dialogueManager;

    private bool sceneLoading = false;

    void Start()
    {
        DialogueLine[] introLines = {

            new DialogueLine("Mouse", "Neautral_MOUSE",
                "So what's next? Time to grab one of those boxes and sift through them?"),

            new DialogueLine("Faith", "Faith_cropped",
                "Nah. Don't worry about that."),

            new DialogueLine("Faith", "Faith_cropped",
                "I want you to help me with sorting through my scripts."),

            new DialogueLine("Mouse", "Excited_MOUSE",
                "We'll go the max-heap way!"),

            new DialogueLine("Faith", "Faith_cropped",
                "Show me.")
        };

        dialogueManager.StartDialogue(introLines);
    }

    void Update()
    {
        if (!sceneLoading && dialogueManager != null && !dialogueManager.IsDialogueActive())
        {
            sceneLoading = true;
            SceneManager.LoadScene("04_TrueHeap_Game");
        }
    }
}