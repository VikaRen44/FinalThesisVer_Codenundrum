using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

// ✅ Input System namespaces (these are what your screenshot is missing)
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

public class EventSystemFixer : MonoBehaviour
{
    private static EventSystemFixer s_instance;

    [Header("Optional Rebind")]
    [Tooltip("If you use Input System UI Module, assign your UI Actions Asset here (same one used by the module).")]
    public InputActionAsset uiActions;

    private EventSystem _es;
    private InputSystemUIInputModule _uiModule;

    private void Awake()
    {
        _es = GetComponent<EventSystem>();
        _uiModule = GetComponent<InputSystemUIInputModule>();

        // ✅ HARD FIX: Only ONE EventSystem survives
        if (s_instance != null && s_instance != this)
        {
            Destroy(gameObject);
            return;
        }

        s_instance = this;
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDestroy()
    {
        if (s_instance == this)
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            s_instance = null;
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (_es == null) _es = GetComponent<EventSystem>();
        if (_uiModule == null) _uiModule = GetComponent<InputSystemUIInputModule>();

        // ✅ Clear stale selection (prevents first click doing nothing)
        if (_es != null)
            _es.SetSelectedGameObject(null);

        // ✅ Optional: rebind UI actions asset (prevents module stuck in build)
        if (_uiModule != null && uiActions != null)
            _uiModule.actionsAsset = uiActions;

        // ✅ Refresh module internal state
        if (_uiModule != null)
        {
            _uiModule.enabled = false;
            _uiModule.enabled = true;
        }
    }
}
