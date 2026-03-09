using System;

public static class AssessmentLaunchContext
{
    public struct Launch
    {
        public string jsonFileName;
        public string completionRewardId;
        public string perfectRewardId;
        public string chapterCompletionRewardId;

        public bool useHubSpawnOverrideOnReturn;
        public string hubSpawnPointNameOnReturn;

        public string highScoreKey;
    }

    private static bool _has;
    private static Launch _launch;

    public static void Set(
        string jsonFileName,
        string completionRewardId,
        string perfectRewardId,
        string chapterCompletionRewardId,
        bool useHubSpawnOverrideOnReturn,
        string hubSpawnPointNameOnReturn,
        string highScoreKey
    )
    {
        _has = true;
        _launch = new Launch
        {
            jsonFileName = jsonFileName ?? "",
            completionRewardId = completionRewardId ?? "",
            perfectRewardId = perfectRewardId ?? "",
            chapterCompletionRewardId = chapterCompletionRewardId ?? "",
            useHubSpawnOverrideOnReturn = useHubSpawnOverrideOnReturn,
            hubSpawnPointNameOnReturn = hubSpawnPointNameOnReturn ?? "",
            highScoreKey = highScoreKey ?? ""
        };
    }

    public static bool TryConsume(out Launch launch)
    {
        launch = default;

        if (!_has) return false;

        launch = _launch;
        _has = false;
        _launch = default;
        return true;
    }
}