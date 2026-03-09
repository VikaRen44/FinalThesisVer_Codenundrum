using UnityEngine;
using UnityEngine.SceneManagement;

public class BranchChoice : MonoBehaviour
{
    public DialogueManager dialogueManager;

    [Header("Branch Dialogue")]
    public DialogueLine[] choiceADialogue;
    public DialogueLine[] choiceBDialogue;

    [Header("Buttons")]
    public GameObject choiceAButton;
    public GameObject choiceBButton;

    [Header("Optional Scene Loading")]
    public bool loadSceneAfterChoice = false;
    public string sceneForChoiceA;
    public string sceneForChoiceB;

    void Start()
    {
        if (StoryFlags.instance == null)
        {
            Debug.LogWarning("StoryFlags missing - test mode.");
        }
    }
    
    public void ChooseA()
    {
        choiceAButton.SetActive(false);
        choiceBButton.SetActive(false);

        // If this choice loads a scene
        if (loadSceneAfterChoice)
        {
            SceneManager.LoadScene(sceneForChoiceA);
            return;
        }

        // Otherwise play dialogue branch
        StoryFlags.instance.currentRoute = Route.Good;

        dialogueManager.enabled = true;
        dialogueManager.StartDialogue(choiceADialogue);
    }

    public void ChooseB()
    {
        choiceAButton.SetActive(false);
        choiceBButton.SetActive(false);

        // If this choice loads a scene
        if (loadSceneAfterChoice)
        {
            SceneManager.LoadScene(sceneForChoiceB);
            return;
        }

        // Otherwise play dialogue branch
        StoryFlags.instance.currentRoute = Route.Bad;

        dialogueManager.enabled = true;
        dialogueManager.StartDialogue(choiceBDialogue);
    }
}