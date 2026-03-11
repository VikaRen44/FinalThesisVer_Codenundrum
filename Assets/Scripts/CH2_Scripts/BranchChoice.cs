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
            Debug.LogWarning("[BranchChoice] StoryFlags missing - creating fallback instance.");
            StoryFlags.EnsureExists();
        }

        if (dialogueManager == null)
        {
            Debug.LogWarning("[BranchChoice] DialogueManager is missing.");
        }
    }

    public void ChooseA()
    {
        if (loadSceneAfterChoice)
        {
            if (string.IsNullOrWhiteSpace(sceneForChoiceA))
            {
                Debug.LogWarning("[BranchChoice] sceneForChoiceA is empty.");
                return;
            }

            SceneManager.LoadScene(sceneForChoiceA);
            return;
        }

        if (StoryFlags.instance == null)
        {
            StoryFlags.EnsureExists();
        }

        if (StoryFlags.instance == null)
        {
            Debug.LogError("[BranchChoice] Cannot choose A because StoryFlags.instance is null.");
            return;
        }

        if (dialogueManager == null)
        {
            Debug.LogError("[BranchChoice] Cannot choose A because dialogueManager is null.");
            return;
        }

        StoryFlags.instance.currentRoute = Route.Good;

        if (choiceAButton != null) choiceAButton.SetActive(false);
        if (choiceBButton != null) choiceBButton.SetActive(false);

        dialogueManager.enabled = true;
        dialogueManager.StartDialogue(choiceADialogue);
    }

    public void ChooseB()
    {
        if (loadSceneAfterChoice)
        {
            if (string.IsNullOrWhiteSpace(sceneForChoiceB))
            {
                Debug.LogWarning("[BranchChoice] sceneForChoiceB is empty.");
                return;
            }

            SceneManager.LoadScene(sceneForChoiceB);
            return;
        }

        if (StoryFlags.instance == null)
        {
            StoryFlags.EnsureExists();
        }

        if (StoryFlags.instance == null)
        {
            Debug.LogError("[BranchChoice] Cannot choose B because StoryFlags.instance is null.");
            return;
        }

        if (dialogueManager == null)
        {
            Debug.LogError("[BranchChoice] Cannot choose B because dialogueManager is null.");
            return;
        }

        StoryFlags.instance.currentRoute = Route.Bad;

        if (choiceAButton != null) choiceAButton.SetActive(false);
        if (choiceBButton != null) choiceBButton.SetActive(false);

        dialogueManager.enabled = true;
        dialogueManager.StartDialogue(choiceBDialogue);
    }
}