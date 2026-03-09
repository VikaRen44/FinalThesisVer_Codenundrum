using UnityEngine;
using UnityEngine.SceneManagement;

public class LoadNextSceneButton : MonoBehaviour
{
    [Header("Next Scene")]
    [Tooltip("Set this to the scene you want to load (must be added in Build Settings).")]
    public string nextSceneName = "05_After_True Heap";

    [Header("Optional: Use your fade transition if available")]
    public bool useSceneTransitionIfAvailable = true;

    [Header("Optional: Prevent double click")]
    public bool blockDoubleClick = true;

    private bool _busy = false;

    // ✅ Wire your Button OnClick() to this
    public void GoNext()
    {
        if (blockDoubleClick && _busy) return;
        _busy = true;

        if (string.IsNullOrWhiteSpace(nextSceneName))
        {
            Debug.LogError("[LoadNextSceneButton] nextSceneName is empty.");
            _busy = false;
            return;
        }

        // Prefer your SceneTransition (fade + input block) if you use it in the project
        if (useSceneTransitionIfAvailable && SceneTransition.Instance != null)
        {
            SceneTransition.Instance.LoadScene(nextSceneName);
            return;
        }

        // Fallback to normal load
        SceneManager.LoadScene(nextSceneName, LoadSceneMode.Single);
    }

#if UNITY_EDITOR
    // Optional helper you can click in the Inspector (3 dots) by calling from context menu
    [ContextMenu("Test Load Next Scene")]
    private void TestLoad()
    {
        GoNext();
    }
#endif
}