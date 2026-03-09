// HubMinigameReturnContext.cs
// Stores return target when entering minigames from the HUB menu.
// Priority return (Hub) without breaking story MinigameReturnContext flow.

using UnityEngine;
using UnityEngine.SceneManagement;

public static class HubMinigameReturnContext
{
    public static bool hasData { get; private set; }
    public static string returnSceneName { get; private set; } = "";
    public static bool forceWhiteFade { get; private set; } = true;

    /// <summary>
    /// Call this BEFORE loading a minigame from the hub menu.
    /// Example: HubMinigameReturnContext.SetHubReturn("04_Gamehub", true);
    /// </summary>
    public static void SetHubReturn(string hubSceneName, bool useWhiteFade)
    {
        returnSceneName = hubSceneName;
        forceWhiteFade = useWhiteFade;
        hasData = !string.IsNullOrWhiteSpace(returnSceneName);
    }

    public static void Clear()
    {
        hasData = false;
        returnSceneName = "";
        forceWhiteFade = true;
    }

    /// <summary>
    /// Returns to the stored hub scene. Clears itself after firing.
    /// </summary>
    public static void ReturnToWorld()
    {
        if (!hasData || string.IsNullOrWhiteSpace(returnSceneName))
        {
            Debug.LogWarning("[HubMinigameReturnContext] ReturnToWorld called but no data set.");
            return;
        }

        string scene = returnSceneName;
        bool white = forceWhiteFade;

        // Clear first so it doesn't re-trigger accidentally
        Clear();

        Time.timeScale = 1f;
        AudioListener.pause = false;

        if (SceneTransition.Instance != null)
        {
            if (white) SceneTransition.Instance.LoadSceneWhite(scene);
            else SceneTransition.Instance.LoadScene(scene);
        }
        else
        {
            SceneManager.LoadScene(scene, LoadSceneMode.Single);
        }
    }
}
