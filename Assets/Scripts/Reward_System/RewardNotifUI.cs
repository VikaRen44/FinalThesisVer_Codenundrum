using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RewardNotifUI : MonoBehaviour
{
    [Header("UI Refs")]
    [Tooltip("The moving panel RectTransform (usually BG root). If null, will use this RectTransform.")]
    public RectTransform panel;

    [Tooltip("Title line (ex: Achievement Unlocked!)")]
    public TMP_Text titleText;

    [Tooltip("Achievement name line")]
    public TMP_Text achievementNameText;

    [Tooltip("Badge icon image")]
    public Image badgeImage;

    [Header("Text")]
    public string defaultTitle = "Achievement Unlocked!";

    [Header("Animation")]
    public Vector2 hiddenAnchoredPos = new Vector2(-900f, 0f);
    public Vector2 shownAnchoredPos = new Vector2(0f, 0f);
    [Min(0.01f)] public float slideInSeconds = 0.25f;
    [Min(0.01f)] public float holdSeconds = 1.4f;
    [Min(0.01f)] public float slideOutSeconds = 0.25f;

    [Header("Optional Fade")]
    [Tooltip("Optional. If null, fading will be skipped even if fadeWithSlide is true.")]
    public CanvasGroup canvasGroup;

    public bool fadeWithSlide = true;

    [Header("SFX")]
    public AudioClip unlockedSfx;
    [Range(0f, 1f)] public float sfxVolume = 1f;

    private Coroutine _co;

    private void Awake()
    {
        // Auto-resolve panel if missing
        if (panel == null)
            panel = GetComponent<RectTransform>();

        // Auto-resolve CanvasGroup if missing (optional)
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        // Ensure starting state is hidden
        HideImmediate();
    }

    private void OnDisable()
    {
        // Avoid orphan coroutines if this object gets disabled externally
        if (_co != null)
        {
            StopCoroutine(_co);
            _co = null;
        }
    }

    public void HideImmediate()
    {
        if (_co != null)
        {
            StopCoroutine(_co);
            _co = null;
        }

        if (panel != null)
            panel.anchoredPosition = hiddenAnchoredPos;

        if (canvasGroup != null)
        {
            // If fading is enabled, hide alpha; if not, keep visible alpha
            canvasGroup.alpha = (fadeWithSlide ? 0f : 1f);
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        // Disable whole notification object
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Existing API (kept): show using current holdSeconds.
    /// </summary>
    public void Show(string achievementName, Sprite icon)
    {
        if (_co != null)
        {
            StopCoroutine(_co);
            _co = null;
        }

        gameObject.SetActive(true);
        _co = StartCoroutine(ShowRoutine(achievementName, icon, holdSeconds));
    }

    /// <summary>
    /// ✅ NEW: Queue can override how long it stays on-screen before sliding out.
    /// Keeps your existing Show() intact.
    /// </summary>
    public void ShowForSeconds(string achievementName, Sprite icon, float holdOverrideSeconds)
    {
        if (_co != null)
        {
            StopCoroutine(_co);
            _co = null;
        }

        gameObject.SetActive(true);
        _co = StartCoroutine(ShowRoutine(achievementName, icon, Mathf.Max(0.01f, holdOverrideSeconds)));
    }

    private IEnumerator ShowRoutine(string achievementName, Sprite icon, float holdTime)
    {
        // Safety: if panel is still missing, we can't slide. We'll still show text and then hide.
        bool canSlide = (panel != null);

        if (titleText) titleText.text = defaultTitle;
        if (achievementNameText) achievementNameText.text = string.IsNullOrWhiteSpace(achievementName) ? "Achievement" : achievementName;

        if (badgeImage)
        {
            if (icon != null) badgeImage.sprite = icon;
            badgeImage.enabled = (badgeImage.sprite != null);
        }

        // play sfx
        if (unlockedSfx != null && UISfxManager.Instance != null)
            UISfxManager.Instance.PlayClip(unlockedSfx, sfxVolume);

        // UI should not block clicks
        if (canvasGroup != null)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        // start hidden
        if (canSlide)
            panel.anchoredPosition = hiddenAnchoredPos;

        if (canvasGroup != null && fadeWithSlide)
            canvasGroup.alpha = 0f;

        // slide in (or just fade in instantly if no panel)
        if (canSlide)
        {
            yield return Slide(panel, canvasGroup,
                hiddenAnchoredPos, shownAnchoredPos,
                fadeWithSlide ? 0f : 1f, 1f,
                slideInSeconds);
        }
        else
        {
            // If no panel, at least force visible alpha
            if (canvasGroup != null && fadeWithSlide)
                canvasGroup.alpha = 1f;
            yield return null;
        }

        // hold
        yield return new WaitForSecondsRealtime(Mathf.Max(0.01f, holdTime));

        // slide out (or just fade out instantly)
        if (canSlide)
        {
            yield return Slide(panel, canvasGroup,
                shownAnchoredPos, hiddenAnchoredPos,
                1f, fadeWithSlide ? 0f : 1f,
                slideOutSeconds);
        }
        else
        {
            if (canvasGroup != null && fadeWithSlide)
                canvasGroup.alpha = 0f;
            yield return null;
        }

        gameObject.SetActive(false);
        _co = null;
    }

    private IEnumerator Slide(RectTransform rt, CanvasGroup cg,
        Vector2 a, Vector2 b,
        float alphaA, float alphaB,
        float seconds)
    {
        float dur = Mathf.Max(0.01f, seconds);
        float t = 0f;

        // If fading is requested but no CanvasGroup exists, just skip fade safely
        bool doFade = fadeWithSlide && (cg != null);

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);

            if (rt != null) rt.anchoredPosition = Vector2.LerpUnclamped(a, b, u);
            if (doFade) cg.alpha = Mathf.LerpUnclamped(alphaA, alphaB, u);

            yield return null;
        }

        if (rt != null) rt.anchoredPosition = b;
        if (doFade) cg.alpha = alphaB;
    }
}