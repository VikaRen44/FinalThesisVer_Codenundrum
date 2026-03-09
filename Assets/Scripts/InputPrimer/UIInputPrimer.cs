using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

public class UIInputPrimer : MonoBehaviour
{
    public static UIInputPrimer Instance { get; private set; }

    [Tooltip("How many frames to wait after scene load before priming UI input. 2 is usually enough.")]
    [Range(0, 10)] public int framesAfterSceneLoad = 2;

    private Coroutine _routine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(PrimeRoutine());
    }

    /// <summary>
    /// Call this when you open a menu too (extra safety).
    /// </summary>
    public void PrimeNow()
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(PrimeRoutine());
    }

    private IEnumerator PrimeRoutine()
    {
        // Wait a couple frames so EventSystem + canvases + raycasters are fully ready in build.
        for (int i = 0; i < framesAfterSceneLoad; i++)
            yield return null;

        Canvas.ForceUpdateCanvases();

        var es = EventSystem.current;
        if (es == null)
        {
            _routine = null;
            yield break;
        }

        // Clear selection (prevents first submit/click being swallowed)
        es.SetSelectedGameObject(null);

        // Wake the InputSystemUIInputModule (build-only timing issue fix)
        var uiModule = es.GetComponent<InputSystemUIInputModule>();
        if (uiModule != null)
        {
            if (uiModule.actionsAsset != null)
                uiModule.actionsAsset.Enable();

            uiModule.enabled = false;
            uiModule.enabled = true;
        }

        // One more frame helps on some devices/builds
        yield return null;

        Canvas.ForceUpdateCanvases();
        _routine = null;
    }
}