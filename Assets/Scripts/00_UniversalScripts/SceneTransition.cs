using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SceneTransition : MonoBehaviour
{
    public static SceneTransition Instance { get; private set; }

    [Header("UI")]
    public CanvasGroup canvasGroup;
    public Image fadeImage;

    [Header("Timing")]
    public float fadeOutDuration = 0.6f;
    public float fadeInDuration = 0.6f;

    [Header("Behavior")]
    public bool blockInputDuringTransition = true;

    [Header("Freeze Gameplay During Transition (TIMING FIX)")]
    [Tooltip("If true, sets Time.timeScale=0 during scene load to prevent player falling before map/spawn finishes.")]
    public bool freezeGameplayDuringTransition = true;

    [Tooltip("How many frames to wait AFTER the new scene loads before fading in + unfreezing. (2-6 is typical)")]
    [Range(0, 12)]
    public int postLoadDelayFrames = 3;

    [Tooltip("Optional extra realtime delay AFTER frames. Usually leave 0. (Useful if you have heavy async spawners)")]
    public float postLoadDelaySecondsRealtime = 0f;

    [Header("Cursor Safety")]
    public bool enforceCursorAfterLoad = true;
    public bool cursorVisibleAfterLoad = true;
    public CursorLockMode cursorLockAfterLoad = CursorLockMode.None;

    [Header("DEBUG - FIND WHO LOADS SCENES")]
    [Tooltip("If true, every LoadScene call prints a stack trace (who called it).")]
    public bool logCallerStackTrace = true;

    [Tooltip("If true, scene loads are blocked (useful to prove who is calling LoadScene).")]
    public bool blockAllSceneLoads = false;

    bool _isTransitioning;

    // remember default fade color so white can be per-call (not global)
    private Color _defaultFadeColor = Color.black;

    // remember previous timescale so we restore correctly
    private float _prevTimeScale = 1f;
    private bool _timeWasFrozenByTransition = false;

    // ✅ NEW: manual "cover then reveal" mode
    private bool _manualCoverActive = false;
    private Color _manualOldColor = Color.black;

    // ✅ NEW: safety routine handle (prevents stacking)
    private Coroutine _postLoadSafetyRoutine;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (canvasGroup == null)
            canvasGroup = GetComponentInChildren<CanvasGroup>(true);

        if (fadeImage != null)
            _defaultFadeColor = fadeImage.color;

        SceneManager.sceneLoaded += OnSceneLoaded;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f; // start covered
            canvasGroup.blocksRaycasts = blockInputDuringTransition;
            canvasGroup.interactable = blockInputDuringTransition;
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    IEnumerator Start()
    {
        // First entry: fade in to reveal initial scene
        yield return Fade(0f, fadeInDuration);

        // ✅ NEW: safety unblock after first reveal (builds can be picky)
        PostLoadSafetyUnblock();
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (enforceCursorAfterLoad)
        {
            Cursor.visible = cursorVisibleAfterLoad;
            Cursor.lockState = cursorLockAfterLoad;
        }

        // ✅ NEW: ALWAYS run a post-load safety unblock
        // This fixes build-only "first click does nothing" caused by fade overlay catching raycasts for 1 frame.
        PostLoadSafetyUnblock();
    }

    // ===========================
    // EXISTING API (kept)
    // ===========================
    public void LoadScene(string sceneName)
    {
        InternalLoadScene(sceneName, useColorOverride: false, overrideColor: _defaultFadeColor);
    }

    public void QuitGame()
    {
        if (_isTransitioning) return;
        StartCoroutine(QuitRoutine());
    }

    // ===========================
    // EXISTING API (kept)
    // ===========================
    public void LoadSceneWhite(string sceneName)
    {
        InternalLoadScene(sceneName, useColorOverride: true, overrideColor: Color.white);
    }

    // =========================================================
    // ✅ NEW: COVER THEN REVEAL (NO SCENE LOAD)
    // =========================================================
    /// <summary>
    /// Fade OUT to a color and KEEP the overlay fully covering the screen (alpha=1).
    /// Use this to hide the previous UI before enabling a new UI (like intro cutscene UI).
    /// </summary>
    public IEnumerator FadeOutToColorAndKeep(Color color, float outDuration = 0.35f, bool freezeDuring = false)
    {
        if (_isTransitioning && !_manualCoverActive)
        {
            Debug.LogWarning("[SceneTransition] FadeOutToColorAndKeep blocked: already transitioning.");
            yield break;
        }

        // Start manual cover
        if (!_manualCoverActive)
        {
            _isTransitioning = true;
            _manualCoverActive = true;

            // store current color, override to requested color
            _manualOldColor = (_fadeImageSafe ? fadeImage.color : _defaultFadeColor);
            if (fadeImage != null) fadeImage.color = color;

            if (freezeDuring)
            {
                _prevTimeScale = Time.timeScale;
                Time.timeScale = 0f;
                _timeWasFrozenByTransition = true;
            }
        }
        else
        {
            // if already active, just enforce color
            if (fadeImage != null) fadeImage.color = color;
        }

        yield return Fade(1f, Mathf.Max(0f, outDuration)); // keep covered
        // DO NOT restore after transition here — we want to stay covered
    }

    /// <summary>
    /// Fade IN (reveal) after a manual cover. Restores old fade color and timescale.
    /// </summary>
    public IEnumerator FadeInFromKeptColor(float inDuration = 0.45f)
    {
        if (!_manualCoverActive)
        {
            Debug.LogWarning("[SceneTransition] FadeInFromKeptColor called but no manual cover is active.");
            yield break;
        }

        yield return Fade(0f, Mathf.Max(0f, inDuration));

        // Restore and end manual cover
        RestoreAfterTransition(_manualOldColor);
        _manualCoverActive = false;
        _isTransitioning = false;

        // ✅ NEW: safety unblock (manual cover can also leave 1-frame blockers)
        PostLoadSafetyUnblock();
    }

    private bool _fadeImageSafe => fadeImage != null;

    // ===========================
    // Internals
    // ===========================
    private void InternalLoadScene(string sceneName, bool useColorOverride, Color overrideColor)
    {
        if (logCallerStackTrace)
        {
            Debug.LogWarning(
                $"[SceneTransition] LoadScene('{sceneName}') CALLED.\n" +
                $"ActiveScene='{SceneManager.GetActiveScene().name}'\n" +
                $"StackTrace:\n{Environment.StackTrace}"
            );
        }

        if (blockAllSceneLoads)
        {
            Debug.LogError($"[SceneTransition] BLOCKED scene load to '{sceneName}' (blockAllSceneLoads=true).");
            return;
        }

        if (_isTransitioning)
        {
            Debug.LogWarning($"[SceneTransition] LoadScene('{sceneName}') blocked: already transitioning.");
            return;
        }

        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("[SceneTransition] LoadScene called with empty sceneName.");
            return;
        }

        StartCoroutine(LoadRoutine(sceneName, useColorOverride, overrideColor));
    }

    IEnumerator LoadRoutine(string sceneName, bool useColorOverride, Color overrideColor)
    {
        _isTransitioning = true;

        // Per-call fade color
        Color oldColor = _defaultFadeColor;
        if (fadeImage != null)
        {
            oldColor = fadeImage.color;
            fadeImage.color = useColorOverride ? overrideColor : _defaultFadeColor;
        }

        // Freeze gameplay
        if (freezeGameplayDuringTransition)
        {
            _prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            _timeWasFrozenByTransition = true;
        }

        // Fade OUT
        yield return Fade(1f, fadeOutDuration);

        // Load scene
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        if (op == null)
        {
            Debug.LogError($"[SceneTransition] LoadSceneAsync returned NULL for '{sceneName}'. Is it in Build Settings?");
            RestoreAfterTransition(oldColor);
            _isTransitioning = false;
            yield break;
        }

        while (!op.isDone)
            yield return null;

        for (int i = 0; i < postLoadDelayFrames; i++)
            yield return null;

        if (postLoadDelaySecondsRealtime > 0f)
            yield return new WaitForSecondsRealtime(postLoadDelaySecondsRealtime);

        // Fade IN
        yield return Fade(0f, fadeInDuration);

        RestoreAfterTransition(oldColor);

        _isTransitioning = false;

        // ✅ NEW: safety unblock after every transition
        PostLoadSafetyUnblock();
    }

    private void RestoreAfterTransition(Color oldFadeColor)
    {
        if (fadeImage != null)
            fadeImage.color = oldFadeColor;

        if (_timeWasFrozenByTransition)
        {
            Time.timeScale = _prevTimeScale;
            _timeWasFrozenByTransition = false;
        }

        if (blockInputDuringTransition && canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }
    }

    IEnumerator QuitRoutine()
    {
        _isTransitioning = true;

        if (freezeGameplayDuringTransition)
        {
            _prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            _timeWasFrozenByTransition = true;
        }

        yield return Fade(1f, fadeOutDuration);

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    IEnumerator Fade(float targetAlpha, float duration)
    {
        if (canvasGroup == null) yield break;

        if (blockInputDuringTransition)
        {
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
        }

        float start = canvasGroup.alpha;
        float t = 0f;

        if (duration <= 0f)
        {
            canvasGroup.alpha = targetAlpha;
        }
        else
        {
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Lerp(start, targetAlpha, t / duration);
                yield return null;
            }
            canvasGroup.alpha = targetAlpha;
        }

        if (blockInputDuringTransition && Mathf.Approximately(targetAlpha, 0f))
        {
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }
    }

    // =========================================================
    // ✅ NEW: POST-LOAD SAFETY UNBLOCK (BUILD BUG FIX)
    // - If the fade overlay is invisible but still intercepting for 1 frame,
    //   this forces it OFF after 1–2 frames.
    // - Does NOT affect manual cover mode.
    // =========================================================
    private void PostLoadSafetyUnblock()
    {
        if (_postLoadSafetyRoutine != null)
            StopCoroutine(_postLoadSafetyRoutine);

        _postLoadSafetyRoutine = StartCoroutine(PostLoadSafetyUnblockRoutine());
    }

    private IEnumerator PostLoadSafetyUnblockRoutine()
    {
        // wait 1 frame for UI/EventSystem to settle
        yield return null;

        // if we are manually keeping cover, DO NOT unblock
        if (_manualCoverActive)
        {
            _postLoadSafetyRoutine = null;
            yield break;
        }

        // if we're transitioning, don't fight it
        if (_isTransitioning)
        {
            _postLoadSafetyRoutine = null;
            yield break;
        }

        // Force overlay non-blocking
        if (canvasGroup != null)
        {
            // In builds, sometimes alpha is ~0 but raycast still blocks for a frame.
            // We force it OFF hard.
            if (canvasGroup.alpha <= 0.001f)
            {
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;
            }
        }

        // wait 1 more frame (some builds need 2)
        yield return null;

        if (_manualCoverActive || _isTransitioning)
        {
            _postLoadSafetyRoutine = null;
            yield break;
        }

        if (canvasGroup != null && canvasGroup.alpha <= 0.001f)
        {
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        _postLoadSafetyRoutine = null;
    }
}
