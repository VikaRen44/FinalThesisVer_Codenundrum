using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class ChapterFlash : MonoBehaviour
{
    public float fadeInTime = 1f;
    public float holdTime = 1.5f;
    public float fadeOutTime = 1f;

    public string nextSceneName; // Set this in Inspector

    private CanvasGroup canvasGroup;

    void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    void Start()
    {
        StartCoroutine(PlayFlash());
    }

    IEnumerator PlayFlash()
    {
        // Fade In
        yield return StartCoroutine(Fade(0f, 1f, fadeInTime));

        // Hold
        yield return new WaitForSeconds(holdTime);

        // Fade Out
        yield return StartCoroutine(Fade(1f, 0f, fadeOutTime));

        // Load Next Scene
        SceneManager.LoadScene(nextSceneName);
    }

    IEnumerator Fade(float startAlpha, float endAlpha, float duration)
    {
        float time = 0f;

        while (time < duration)
        {
            time += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, time / duration);
            yield return null;
        }

        canvasGroup.alpha = endAlpha;
    }
}