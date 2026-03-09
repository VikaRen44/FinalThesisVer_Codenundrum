using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class EnemyFaintEffectUI : MonoBehaviour
{
    [Header("Target")]
    public RectTransform target;
    public CanvasGroup canvasGroup;

    // ================= SFX =================
    [Header("Faint SFX")]
    public AudioSource audioSource;      // optional, auto-add if missing
    public AudioClip faintSfx;
    [Range(0f, 1f)] public float sfxVolume = 1f;
    public bool randomizePitch = true;
    public Vector2 pitchRange = new Vector2(0.95f, 1.08f);

    [Header("Shake")]
    public float shakeDuration = 0.22f;
    public float shakeMagnitude = 10f;

    [Header("Fall")]
    public float fallDistance = 90f;
    public float fallDuration = 0.25f;

    [Header("Fade")]
    public float fadeDuration = 0.18f;

    private Vector2 _startPos;

    private void Awake()
    {
        if (target == null)
            target = GetComponent<RectTransform>();

        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.playOnAwake = false;

        if (target != null)
            _startPos = target.anchoredPosition;
    }

    public IEnumerator PlayFaint()
    {
        if (target == null) yield break;

        // Reset position/alpha
        target.anchoredPosition = _startPos;
        if (canvasGroup != null) canvasGroup.alpha = 1f;

        // ✅ PLAY SFX immediately when faint begins
        PlayFaintSFX();

        // ---------- SHAKE ----------
        float t = 0f;
        while (t < shakeDuration)
        {
            t += Time.unscaledDeltaTime;

            float x = Random.Range(-shakeMagnitude, shakeMagnitude);
            float y = Random.Range(-shakeMagnitude * 0.35f, shakeMagnitude * 0.35f);

            target.anchoredPosition = _startPos + new Vector2(x, y);
            yield return null;
        }
        target.anchoredPosition = _startPos;

        // ---------- FALL ----------
        Vector2 from = _startPos;
        Vector2 to = _startPos + Vector2.down * fallDistance;

        t = 0f;
        while (t < fallDuration)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / fallDuration);
            float eased = u * u;

            target.anchoredPosition = Vector2.LerpUnclamped(from, to, eased);
            yield return null;
        }

        target.anchoredPosition = to;

        // ---------- FADE ----------
        if (canvasGroup != null)
        {
            t = 0f;
            float startA = canvasGroup.alpha;

            while (t < fadeDuration)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / fadeDuration);

                canvasGroup.alpha = Mathf.Lerp(startA, 0f, u);
                yield return null;
            }

            canvasGroup.alpha = 0f;
        }
    }

    private void PlayFaintSFX()
    {
        if (audioSource == null || faintSfx == null) return;

        if (randomizePitch)
            audioSource.pitch = Random.Range(pitchRange.x, pitchRange.y);
        else
            audioSource.pitch = 1f;

        audioSource.PlayOneShot(faintSfx, sfxVolume);
    }
}