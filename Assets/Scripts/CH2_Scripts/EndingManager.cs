using UnityEngine;
using System.Collections;

public class EndingManager : MonoBehaviour
{
    public DialogueManager dialogueManager;

    [Header("Proceed To Assessment 2")]
    public ProceedToAssessment2Button proceedToAssessment2Button;


    [Header("Ending Transition")]
    public CanvasGroup fadePanel;
    public GameObject endingSummaryPanel;
    public float fadeDuration = 2.5f;

    void Start()
    {
        if (StoryFlags.instance == null)
        {
            Debug.LogError("StoryFlags missing!");
            return;
        }

        if (dialogueManager == null)
        {
            Debug.LogError("DialogueManager missing!");
            return;
        }

        if (StoryFlags.instance.currentRoute == Route.Good)
        {
            PlayGoodEnding();
        }
        else
        {
            PlayBadEnding();
        }

        if (fadePanel != null)
            fadePanel.alpha = 0f;

        if (endingSummaryPanel != null)
            endingSummaryPanel.SetActive(false);

    }

    void PlayGoodEnding()
    {
        DialogueLine[] goodEnding =
        {
            new DialogueLine("Faith", "Sad_Faith", "Whew..."),
            new DialogueLine("Faith", "Faith_cropped", "Thank you for helping with all of this today."),

            new DialogueLine("Mouse", "Mouse_Neutral", "You look less stressed."),

            new DialogueLine("Faith", "Sad_Faith", "I think I am."),

            new DialogueLine("Faith", "Faith_cropped",
            "When my agent told me about this role... I thought this was finally it."),

            new DialogueLine("Faith", "Faith_cropped",
            "But the place feels like home now."),

            new DialogueLine("Faith", "Surprised_Faith",
            "And the script doesn't feel so scary anymore."),

            new DialogueLine("Mouse", "Mouse_Excited", "Go get that role."),

            new DialogueLine("Faith", "Faith_cropped", "I will."),

            new DialogueLine("", "", "A few weeks later..."),

            new DialogueLine("", "",
            "News headline: 'Breakout Actress Faith Williams Steals the Show in Upcoming Film.'","GoodEnding"),

            new DialogueLine("", "", " \"LEAP OF FAITH!\" ", "GoodEnding")
        };

        dialogueManager.StartDialogue(goodEnding, OnEndingDialogueFinished);
    }

    void PlayBadEnding()
    {
        DialogueLine[] badEnding =
        {
            new DialogueLine("Faith", "Sad_Faith", "Thanks for helping today."),

            new DialogueLine("Mouse", "Mouse_Neutral", "You don't sound too happy."),

            new DialogueLine("Faith", "Sad_Faith", "I just... I don't think I'm ready."),

            new DialogueLine("Mouse", "Mouse_Neutral", "Ready for what?"),

            new DialogueLine("Faith", "Faith_Sad", "The audition."),

            new DialogueLine("Faith", "Faith_Sad",
            "Everything happened too fast. Moving here. Memorizing lines."),

            new DialogueLine("Faith", "Faith_cropped",
            "Maybe I grabbed the opportunity too quickly."),

            new DialogueLine("", "", "A week later...","BadEnding"),

            new DialogueLine("", "",
            "","BadEnding"),

            new DialogueLine("Faith", "Faith_Sad", "...I knew it.", "BadEnding")
        };

        dialogueManager.StartDialogue(badEnding, OnEndingDialogueFinished);
    }

    void OnEndingDialogueFinished()
    {
        if (proceedToAssessment2Button != null)
        {
            StartCoroutine(EndingSequence());
        }
        else
        {
            Debug.LogWarning("Cant start coroutine endingsequence()");
        }
    }

    IEnumerator EndingSequence()
    {

        dialogueManager.gameObject.SetActive(false);

        // Fade to black
        yield return StartCoroutine(FadeToBlack());

        // Show summary panel
        if (endingSummaryPanel != null)
            endingSummaryPanel.SetActive(true);

        // Enable proceed button AFTER summary appears
        if (proceedToAssessment2Button != null)
            proceedToAssessment2Button.gameObject.SetActive(true);
    }

    IEnumerator FadeToBlack()
    {
        if (fadePanel == null)
            yield break;

        float t = 0f;

        while (t < fadeDuration)
        {
            t += Time.deltaTime;

            fadePanel.alpha = Mathf.Lerp(0f, 1f, t / fadeDuration);

            yield return null;
        }

        fadePanel.alpha = 1f;
    }


}