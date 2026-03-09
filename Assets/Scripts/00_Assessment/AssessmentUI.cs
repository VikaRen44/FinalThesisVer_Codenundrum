using UnityEngine;

public class AssessmentUI : MonoBehaviour
{
    [Header("UI")]
    public GameObject root;            // Keep this as AssessmentUIRoot GameObject (your current setup)
    public AssessmentUIRoot uiRoot;    // The script on AssessmentUIRoot

    [Header("Music")]
    public AssessmentMusicOverride musicOverride;

    // ✅ Optional: Assign your AssessmentsMenuScoreUI (or any script) here to refresh after closing.
    // Leave null if you don't need it.
    [Header("Optional: Menu Score Refresh")]
    public MonoBehaviour menuScoreUi; // should be AssessmentsMenuScoreUI
    public string refreshMethodName = "Refresh";

    public void OpenAssessment()
    {
        if (root != null)
            root.SetActive(true);

        if (musicOverride != null)
            musicOverride.StartAssessmentMusic();

        if (uiRoot != null)
            uiRoot.BeginAssessmentFromTrigger();
    }

    // Optional manual close (if you add a close button)
    public void CloseAssessmentManually()
    {
        if (uiRoot != null)
            uiRoot.CloseAndRestore();

        StopAssessmentMusicOnly();

        if (root != null)
            root.SetActive(false);

        // ✅ If assigned, refresh score display (safe reflection)
        TryInvoke(menuScoreUi, refreshMethodName);
    }

    // ✅ used by AssessmentUIRoot on finish (safe, no disabling here)
    public void StopAssessmentMusicOnly()
    {
        if (musicOverride != null)
            musicOverride.StopAssessmentMusic();

        // ✅ If assigned, refresh score display (safe reflection)
        TryInvoke(menuScoreUi, refreshMethodName);
    }

    private void TryInvoke(object target, string method)
    {
        if (target == null) return;
        if (string.IsNullOrWhiteSpace(method)) return;

        try
        {
            var t = target.GetType();
            var m = t.GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (m == null) return;
            if (m.GetParameters().Length != 0) return;
            m.Invoke(target, null);
        }
        catch { }
    }
}