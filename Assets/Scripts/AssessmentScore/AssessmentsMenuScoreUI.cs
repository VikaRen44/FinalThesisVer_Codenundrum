using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class AssessmentsMenuScoreUI : MonoBehaviour
{
    [Header("TMP Texts")]
    public TMP_Text ch1ScoreText;
    public TMP_Text ch2ScoreText;

    [Header("Keys (MUST MATCH what AssessmentUIRoot saves under)")]
    public string ch1Key = "CH1_ASSESS";
    public string ch2Key = "CH2_ASSESS";

    [Header("Display")]
    public string prefix = "High Score:";
    public string missingValueText = "--";

    private void OnEnable()
    {
        // ✅ ensure manager exists (prevents null on hub)
        AssessmentScoreManager.EnsureInstance();

        // ✅ refresh now + again next frame (wait for save import)
        Refresh();
        StartCoroutine(RefreshNextFrame());

        // ✅ listen for changes (ImportSaveStateJson / RecordScore)
        if (AssessmentScoreManager.Instance != null)
            AssessmentScoreManager.Instance.OnScoresChanged += HandleScoresChanged;

        // ✅ also refresh after scene load (important for hub load timing)
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        if (AssessmentScoreManager.Instance != null)
            AssessmentScoreManager.Instance.OnScoresChanged -= HandleScoresChanged;

        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void HandleScoresChanged()
    {
        Refresh();
    }

    private void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        // delay one frame so any SaveGameManager import has time to run
        StartCoroutine(RefreshNextFrame());
    }

    private IEnumerator RefreshNextFrame()
    {
        yield return null;
        Refresh();
    }

    public void Refresh()
    {
        SetOne(ch1ScoreText, ch1Key);
        SetOne(ch2ScoreText, ch2Key);
    }

    private void SetOne(TMP_Text t, string key)
    {
        if (t == null) return;

        AssessmentScoreManager.EnsureInstance();
        var mgr = AssessmentScoreManager.Instance;

        string p = string.IsNullOrWhiteSpace(prefix) ? "" : prefix.Trim();

        if (mgr == null || string.IsNullOrWhiteSpace(key))
        {
            t.text = $"{p} {missingValueText}".Trim();
            return;
        }

        if (mgr.TryGetBest(key, out int best, out int total) && best > 0)
        {
            if (total > 0) t.text = $"{p} {best}/{total}".Trim();
            else t.text = $"{p} {best}".Trim();
        }
        else
        {
            t.text = $"{p} {missingValueText}".Trim();
        }
    }
}