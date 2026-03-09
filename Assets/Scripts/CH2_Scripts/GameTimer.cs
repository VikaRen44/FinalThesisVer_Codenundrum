using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;

public class GameTimer : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI timeText;
    public TextMeshProUGUI completionTimeText;

    [Tooltip("All UI texts that should display the best remaining time")]
    public TextMeshProUGUI[] bestTimeTexts;

    [Header("Lose UI")]
    public CanvasGroup timeUpPanel;
    public Image loseFlashImage;
    public MonoBehaviour uiManager;

    [Header("Settings")]
    public string levelID = "IntroHeap";
    public float timeLimit = 120f;

    [Header("Warning Animation")]
    public float warningTime = 60f;
    public Color normalColor = Color.white;
    public Color warningColor = Color.red;
    public float pulseSpeed = 4f;

    [Header("Critical Warning")]
    public float criticalTime = 10f;
    public float screenPulseSpeed = 2f;
    public float screenPulseAlpha = 0.25f;

    private float remainingTime;
    private bool timerRunning = false;

    void Start()
    {

        // PlayerPrefs.DeleteKey("BestRemainingTime_" + levelID);

        UpdateBestTimeUI();

        if (timeUpPanel != null)
        {
            timeUpPanel.alpha = 0f;
            timeUpPanel.interactable = false;
            timeUpPanel.blocksRaycasts = false;
        }

        if (loseFlashImage != null)
        {
            Color c = loseFlashImage.color;
            c.a = 0;
            loseFlashImage.color = c;
        }
    }

    void Update()
    {
        if (!timerRunning) return;

        remainingTime -= Time.deltaTime;

        if (remainingTime <= 0f)
        {
            remainingTime = 0f;
            timerRunning = false;

            StartCoroutine(TimeUpSequence());
        }

        Debug.Log("Timer running: " + remainingTime);

        UpdateTimerUI();
        UpdateScreenPulse();
    }

    public void StartTimer()
    {
        remainingTime = timeLimit;
        timerRunning = true;

        UpdateTimerUI();
        UpdateBestTimeUI();
    }

    public void StopTimer()
    {
        if (!timerRunning) return;

        timerRunning = false;

        float finalRemainingTime = remainingTime;

        if (completionTimeText != null)
            completionTimeText.text = "Time Left: " + FormatTime(finalRemainingTime);

        string key = "BestRemainingTime_" + levelID;
        float bestTime = PlayerPrefs.GetFloat(key, 0f);

        // Safety check (fixes old stopwatch values)
        if (bestTime >= timeLimit)
            bestTime = 0f;

        if (finalRemainingTime > bestTime)
        {
            bestTime = finalRemainingTime;
            PlayerPrefs.SetFloat(key, bestTime);
            PlayerPrefs.Save();
        }

        foreach (var text in bestTimeTexts)
        {
            if (text != null)
                text.text = "Best Time: " + FormatTime(bestTime);
        }

        Debug.Log("Remaining: " + finalRemainingTime);
        Debug.Log("BestTime: " + bestTime);

    }

    void UpdateTimerUI()
    {
        if (timeText == null) return;

        Debug.Log("Updating timer UI");
        
        timeText.text = "Time Left: " + FormatTime(remainingTime);

        if (remainingTime <= warningTime)
        {
            float pulse = Mathf.PingPong(Time.time * pulseSpeed, 1f);
            timeText.color = Color.Lerp(normalColor, warningColor, pulse);
        }
        else
        {
            timeText.color = normalColor;
        }
    }

    void UpdateScreenPulse()
    {
        if (loseFlashImage == null) return;

        if (remainingTime <= criticalTime && remainingTime > 0)
        {
            float pulse = Mathf.PingPong(Time.time * screenPulseSpeed, 1f);

            Color c = loseFlashImage.color;
            c.a = pulse * screenPulseAlpha;
            loseFlashImage.color = c;
        }
        else if (remainingTime > criticalTime)
        {
            Color c = loseFlashImage.color;
            c.a = 0;
            loseFlashImage.color = c;
        }
    }

    IEnumerator TimeUpSequence()
    {
        if (loseFlashImage != null)
        {
            float t = 0;

            while (t < 1.5f)
            {
                t += Time.deltaTime;

                float pulse = Mathf.PingPong(Time.time * 3f, 1f);

                Color c = loseFlashImage.color;
                c.a = pulse * 0.6f;
                loseFlashImage.color = c;

                yield return null;
            }

            Color reset = loseFlashImage.color;
            reset.a = 0;
            loseFlashImage.color = reset;
        }
        else
        {
            yield return new WaitForSeconds(1.5f);
        }

        if (uiManager != null)
        {
            var method = uiManager.GetType().GetMethod("FadeInTimeUpPanel");
            if (method != null)
            StartCoroutine((IEnumerator)method.Invoke(uiManager, null));
        }
    }

    void UpdateBestTimeUI()
    {
        string key = "BestRemainingTime_" + levelID;
        float bestTime = PlayerPrefs.GetFloat(key, 0f);

        if (bestTimeTexts == null) return;

        foreach (var text in bestTimeTexts)
        {
            if (text != null)
                text.text = "Best Time: " + FormatTime(bestTime);
        }
    }

    string FormatTime(float time)
    {
        int minutes = Mathf.FloorToInt(time / 60);
        int seconds = Mathf.FloorToInt(time % 60);

        return string.Format("{0:00}:{1:00}", minutes, seconds);
    }
}