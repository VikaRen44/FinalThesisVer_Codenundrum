using UnityEngine;
using TMPro;
using System.Collections;

public class EndingSummaryDisplay : MonoBehaviour
{
    public TextMeshProUGUI minHeapText;
    public TextMeshProUGUI maxHeapText;
    public TextMeshProUGUI quickSortText;

    [Header("Animation")]
    public float revealDelay = 0.6f;
    public float countDuration = 1.2f;

    [Header("Proceed Button")]
    public ProceedToAssessment2Button proceedButton;

    void OnEnable()
    {
        if (proceedButton != null)
            proceedButton.gameObject.SetActive(false);

        StartCoroutine(RevealResults());
    }

    IEnumerator RevealResults()
    {
        minHeapText.text = "";
        maxHeapText.text = "";
        quickSortText.text = "";

        yield return new WaitForSeconds(0.5f);

        float minHeap = PlayerPrefs.GetFloat("BestRemainingTime_IntroHeap", 0f);
        yield return StartCoroutine(AnimateTime(minHeapText, "Organize Boxes", minHeap));

        yield return new WaitForSeconds(revealDelay);

        float maxHeap = PlayerPrefs.GetFloat("BestRemainingTime_TrueHeap", 0f);
        yield return StartCoroutine(AnimateTime(maxHeapText, "Organize Scripts", maxHeap));

        yield return new WaitForSeconds(revealDelay);

        float quickSort = PlayerPrefs.GetFloat("BestRemainingTime_QuickSort", 0f);
        yield return StartCoroutine(AnimateTime(quickSortText, "Organize Props", quickSort));

        yield return new WaitForSeconds(0.8f);

        if (proceedButton != null)
            proceedButton.gameObject.SetActive(true);
    }

    IEnumerator AnimateTime(TextMeshProUGUI text, string label, float targetTime)
    {
        float elapsed = 0f;

        while (elapsed < countDuration)
        {
            elapsed += Time.deltaTime;

            float current = Mathf.Lerp(0, targetTime, elapsed / countDuration);

            text.text = label + ":    " + FormatTime(current);

            yield return null;
        }

        text.text = label + ":     " + FormatTime(targetTime);
    }

    string FormatTime(float time)
    {
        int minutes = Mathf.FloorToInt(time / 60);
        int seconds = Mathf.FloorToInt(time % 60);

        return string.Format("{0:00}:{1:00}", minutes, seconds);
    }
}