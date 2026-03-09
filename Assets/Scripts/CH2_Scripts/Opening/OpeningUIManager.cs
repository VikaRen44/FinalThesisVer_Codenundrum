using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class OpeningUIManager : MonoBehaviour
{
    public DialogueManager dialogueManager;

    [Header("MiniGame Prompt")]
    public GameObject minigamePromptPanel;
    public Button replayButton;
    public Button playButton;

    private bool promptActive = false;

    void Start()
    {
        // Ensure prompt panel is hidden at start
        if (minigamePromptPanel != null)
            minigamePromptPanel.SetActive(false);

        // Hook buttons
        if (replayButton != null)
            replayButton.onClick.AddListener(ReplayScene);

        if (playButton != null)
            playButton.onClick.AddListener(PlayMinigame);

        DialogueLine[] openingLines = {

            new DialogueLine(
                "Mouse", 
                "Mouse_Excited",
                "Woahhh.. This looks entirely different from the Lizzy's world. It's all... bright and pastel.",
                "bg_hallway"
            ),

            new DialogueLine(
                "Mouse", 
                "Mouse_Neutral",
                "In Lizzy's world we organized memories and gave them to Lizzy with selection sort.",
                "bg_hallway"
            ),

            new DialogueLine(
                "Mouse",
                "Mouse_Excited",
                "What could be our next encounter in this world? Let's go roam around!"
            ),

            new DialogueLine(
                "???",
                "Faith_sil",
                "So this is it.",
                "bg_frontdoor"
            ),

            new DialogueLine(
                "",
                "",
                "You look around for the source of the voice.",
                "bg_frontdoor"
            ),

            new DialogueLine(
                "???",
                "Faith_sil",
                "My own place. A new start.",
                "bg_frontdoor"
            ),

            new DialogueLine(
                "Mouse",
                "Mouse_Surprised",
                "Who is that?",
                "bg_frontdoor"
            ),

            new DialogueLine(
                "???",
                "Faith_sil",
                "I almost didn’t take the key.",
                "bg_frontdoor"
            ),

            new DialogueLine(
                "Mouse",
                "Mouse_Neutral",
                "What key?",
                "bg_frontdoor"
            ),

            new DialogueLine(
                "???",
                "Faith_sil",
                "!?",
                "bg_frontdoor"
            ),

            new DialogueLine(
                "???",
                "Faith_sil",
                "And you guys are?",
                "bg_frontdoor"
            ),

            new DialogueLine(
                "",
                "",
                "You and Mouse introduced yourselves, and that you came from a different world too."
            ),

            new DialogueLine(
                "???",
                "Faith_sil",
                "Heh. As if!"
            ),

            new DialogueLine(
                "Mouse",
                "Mouse_Sad",
                "You don't believe us? We're telling the truth! We just came from a whole gloomy world and this robot girl-"
            ),

            new DialogueLine(
                "???",
                "Faith_sil",
                "No need to dig yourself in too deep, fellas. I was not born yesterday."
            ),

            new DialogueLine(
                "Faith",
                "Faith_cropped",
                "I'm Faith."
            ),

            new DialogueLine(
                "Faith",
                "Confused_Faith",
                "You guys are the helpers right? The one my agency hired to help organize my stuff."
            ),

            new DialogueLine(
                "",
                "",
                "You and Mouse look at each other."
            ),

            new DialogueLine(
                "Mouse",
                "Mouse_Neutral",
                "This girl don't seem to care about who we are."
            ),

            new DialogueLine(
                "Faith",
                "Angry_Faith",
                "I do not have time to know about everyone's backstories here. I am already quite overwhelmed moving into a new place."
            ),

            new DialogueLine(
                "Faith",
                "Surprised_Faith",
                "Plus, that play that my agent just thrown me into."
            ),

            new DialogueLine(
                "Faith",
                "Angry_Faith",
                "If it weren't such a good look on me to take the role of Beth, I would not have taken the bait."
            ),

            new DialogueLine(
                "Faith",
                "Faith_cropped",
                "Now, I have to learn my script fast and adjust into this place. Hectic!"
            ),

            new DialogueLine(
                "Mouse",
                "Mouse_Surprised",
                "You're an actress??"
            ),

            new DialogueLine(
                "Faith",
                "Confused_Faith",
                "Surprised? I have yet to get my break into the forefront of the industry..."
            ),

            new DialogueLine(
                "Faith",
                "Faith_cropped",
                "In no way would I let this chance slip away!"
            ),

            new DialogueLine(
                "",
                "",
                "Awed by Faith's determination, you stood in silence."
            ),

            new DialogueLine(
                "Faith",
                "Surprised_Faith",
                "What are you guys standing there for?"
            ),

            new DialogueLine(
                "Mouse",
                "Mouse_Neutral",
                "I guess we have no choice but to go along with her, pal."
            ),

            new DialogueLine(
                "",
                "",
                "You nod"
            ),

            new DialogueLine(
                "Mouse",
                "Mouse_Neutral",
                "Let's help her and get out of here."
            ),

            new DialogueLine(
                "Mouse",
                "Mouse_Neutral",
                "Same as Lizzy's world."
            ),

            new DialogueLine(
                "Faith",
                "Surprised_Faith",
                "Chop chop!"
            ),

            new DialogueLine(
                "Mouse",
                "Mouse_Surprised",
                "ALRIGHT! ALRIGHT!"
            ),

            new DialogueLine(
                "Mouse",
                "Mouse_Sad",
                "GEEZ!"
            ),

            new DialogueLine(
                "Faith",
                "Faith_cropped",
                "Let's start with the boxes at the living room.",
                "bg_cornerlivingroom"
            )
        };

        dialogueManager.StartDialogue(openingLines);

        StartCoroutine(WaitForDialogueEnd());
    }

    System.Collections.IEnumerator WaitForDialogueEnd()
    {
        while (dialogueManager.IsDialogueActive())
            yield return null;

        ShowMinigamePrompt();
    }

    void ShowMinigamePrompt()
    {
        if (minigamePromptPanel != null)
            minigamePromptPanel.SetActive(true);

        promptActive = true;
    }

    void Update()
    {
        if (!promptActive)
            return;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.rKey.wasPressedThisFrame)
                ReplayScene();

            if (Keyboard.current.eKey.wasPressedThisFrame)
                PlayMinigame();
        }
    }

    void ReplayScene()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    void PlayMinigame()
    {
        SceneManager.LoadScene("03_Intro_Game");
    }
}