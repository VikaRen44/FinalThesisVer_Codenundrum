using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class AssessmentNPCFeedback : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("The NPC image to swap.")]
    public Image npcImage;

    [Tooltip("Move this RectTransform for the 'reaction move'. Usually the NPC root.")]
    public RectTransform npcRoot;

    [Header("Sprites")]
    public Sprite defaultSprite;
    public Sprite correctSprite;
    public Sprite wrongSprite;

    [Header("SFX")]
    public AudioSource sfxSource;
    public AudioClip correctSfx;
    public AudioClip wrongSfx;

    [Header("Reaction Motion")]
    [Tooltip("How far the NPC moves during reaction (pixels).")]
    public Vector2 moveOffset = new Vector2(40f, 0f);

    [Tooltip("How fast the NPC moves into reaction pose.")]
    public float moveInDuration = 0.12f;

    [Tooltip("How long to hold the reaction before resetting.")]
    public float holdDuration = 0.4f;

    [Tooltip("How fast the NPC returns to default pose.")]
    public float moveOutDuration = 0.14f;

    private Vector2 _startAnchoredPos;
    private Coroutine _routine;

    private void Awake()
    {
        if (npcRoot == null) npcRoot = GetComponent<RectTransform>();
        if (npcRoot != null) _startAnchoredPos = npcRoot.anchoredPosition;

        ResetToDefaultInstant();
    }

    public void ResetToDefaultInstant()
    {
        if (npcImage != null && defaultSprite != null)
            npcImage.sprite = defaultSprite;

        if (npcRoot != null)
            npcRoot.anchoredPosition = _startAnchoredPos;
    }

    public void PlayCorrect()
    {
        PlayFeedback(correct: true);
    }

    public void PlayWrong()
    {
        PlayFeedback(correct: false);
    }

    private void PlayFeedback(bool correct)
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(FeedbackRoutine(correct));
    }

    private IEnumerator FeedbackRoutine(bool correct)
    {
        // sprite + sfx
        if (npcImage != null)
        {
            if (correct && correctSprite != null) npcImage.sprite = correctSprite;
            if (!correct && wrongSprite != null) npcImage.sprite = wrongSprite;
        }

        if (sfxSource != null)
        {
            var clip = correct ? correctSfx : wrongSfx;
            if (clip != null) sfxSource.PlayOneShot(clip);
        }

        // move in
        if (npcRoot != null)
        {
            Vector2 from = _startAnchoredPos;
            Vector2 to = _startAnchoredPos + moveOffset;

            yield return LerpAnchoredPos(from, to, moveInDuration);
        }

        // hold
        yield return new WaitForSeconds(holdDuration);

        // move out + reset sprite
        if (npcRoot != null)
        {
            Vector2 from = npcRoot.anchoredPosition;
            Vector2 to = _startAnchoredPos;

            yield return LerpAnchoredPos(from, to, moveOutDuration);
        }

        if (npcImage != null && defaultSprite != null)
            npcImage.sprite = defaultSprite;

        _routine = null;
    }

    private IEnumerator LerpAnchoredPos(Vector2 from, Vector2 to, float duration)
    {
        if (duration <= 0f)
        {
            npcRoot.anchoredPosition = to;
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(t / duration);
            npcRoot.anchoredPosition = Vector2.Lerp(from, to, a);
            yield return null;
        }
        npcRoot.anchoredPosition = to;
    }
}
