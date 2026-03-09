// ProceedToAssessment2Button.cs
using UnityEngine;
using UnityEngine.SceneManagement;

public class ProceedToAssessment2Button : MonoBehaviour
{
    [Header("Load Target")]
    [Tooltip("Scene name that contains your AssessmentUIRoot.")]
    public string assessmentSceneName = "09_Assessment";

    [Header("Assessment 2 Data (StreamingAssets file name)")]
    [Tooltip("IMPORTANT: This MUST be a real file inside Assets/StreamingAssets/ and MUST include .json (or AssessmentUIRoot will auto-add it).")]
    public string assessment2JsonFileName = "Assessment2_questions.json";

    [Header("Assessment 2 Rewards")]
    public string completeAssessment2RewardId = "CompleteAssessment2";
    public string perfectAssessment2RewardId = "PerfectAssessment2";

    [Header("Chapter 2 Completion Reward (after assessment)")]
    public string chapter2CompletionRewardId = "CH2_COMPLETE";

    [Header("Hub Spawn Override On Return")]
    public bool useHubSpawnOverrideOnReturn = true;
    public string hubSpawnPointNameOnReturn = "Assessment_Return";

    [Header("High Score Key")]
    public string highScoreKey = "Assessment2";

    [Header("Optional: Transition")]
    public bool useSceneTransitionIfAvailable = true;

    public void Proceed()
    {
        AssessmentLaunchContext.Set(
            assessment2JsonFileName,
            completeAssessment2RewardId,
            perfectAssessment2RewardId,
            chapter2CompletionRewardId,
            useHubSpawnOverrideOnReturn,
            hubSpawnPointNameOnReturn,
            highScoreKey
        );

        if (useSceneTransitionIfAvailable && SceneTransition.Instance != null)
        {
            SceneTransition.Instance.LoadScene(assessmentSceneName);
        }
        else
        {
            SceneManager.LoadScene(assessmentSceneName, LoadSceneMode.Single);
        }
    }
}