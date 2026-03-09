using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class FaithIntroDialogueManager : MonoBehaviour
{
    public DialogueManager dialogueManager;

    void Start()
    {
        DialogueLine[] introLines = {

            new DialogueLine("Mouse", "Neautral_MOUSE", "There are a lot of them..."),
            new DialogueLine("Mouse", "Neautral_MOUSE", "If we try to move everything at once, it'll just get messier."),
            new DialogueLine("Faith", "Faith_cropped", "Obviously. So what do you suggest?"),
            new DialogueLine("Mouse", "Neautral_MOUSE", "We pick one to start with."),
            new DialogueLine("Faith", "Faith_cropped", "Based on what?"),
            new DialogueLine("Mouse", "Neautral_MOUSE", "Weight. Priority. Something measurable."),
            new DialogueLine("Mouse", "Neautral_MOUSE", "Find the lightest box."),
            new DialogueLine("Mouse", "Neautral_MOUSE", "Place it somewhere stable."),
            new DialogueLine("Mouse", "Neautral_MOUSE", "Then make sure nothing heavier rests on top of it."),
            new DialogueLine("Faith", "Faith_cropped", "So... lighter boxes above heavier ones."),
            new DialogueLine("Mouse", "Excited_MOUSE", "Exactly! If the smallest stays on top, nothing collapses."),
            new DialogueLine("", "", "You begin comparing the boxes...")
        };

        dialogueManager.StartDialogue(introLines);
        StartCoroutine(WaitForEnd());
    }

    IEnumerator WaitForEnd()
    {
        while (dialogueManager.IsDialogueActive())
            yield return null;

        SceneManager.LoadScene("03_Faith_MinHeapGame");
    }
}