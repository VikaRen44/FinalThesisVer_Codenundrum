using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

public class MinigameReviewRules : MonoBehaviour
{
    // =========================
    // CONTENT
    // =========================
    [Header("Review Rules Content (Drag & Drop)")]
    [Tooltip("The TutorialSequenceSO that contains your minigame review/rules pages.")]
    public TutorialSequenceSO reviewRulesTutorial;

    // =========================
    // HOTKEY (TAB)
    // =========================
    [Header("Hotkey (New Input System)")]
    [Tooltip("Optional. If assigned, this action toggles the review rules UI (recommended bind: TAB).")]
    public InputActionReference toggleAction;

    [Tooltip("Fallback hotkey if toggleAction is not assigned (Keyboard only).")]
    public bool enableTabFallback = true;

    // =========================
    // FREEZE LIKE SAVE MENU
    // =========================
    [Header("Freeze Like Save Menu (Time.timeScale)")]
    public bool freezeWorldWithTimeScale = true;

    private float _prevTimeScale = 1f;
    private bool _timeScaleCaptured = false;

    // =========================
    // OPTIONAL ACTION MAP SWAP
    // =========================
    [Header("Optional Extra Lock (Action Map Swap)")]
    public bool alsoSwapActionMap = true;

    [Tooltip("Drag your PlayerInput here. If empty, auto-finds PlayerInput in the scene.")]
    public PlayerInput playerInput;

    public string gameplayActionMapName = "Player";
    public string uiActionMapName = "UI";

    private string _prevActionMap = "";
    private bool _cachedPrevActionMap = false;

    // =========================
    // TUTORIAL UI TARGET
    // =========================
    [Header("Tutorial UI Target (Drag TutorialUI Script Here)")]
    public TutorialUI tutorialUI;

    [Header("Optional Player (for lock)")]
    public string playerTag = "Player";

    // =========================
    // STABILITY
    // =========================
    [Header("Stability / Debounce")]
    [Tooltip("Prevents double-toggle flashes (especially during map swaps / module priming).")]
    [Range(0.05f, 0.5f)]
    public float toggleDebounceSeconds = 0.18f;

    [Tooltip("Extra safety: ignore toggles for a short time after enabling this component.")]
    [Range(0f, 0.5f)]
    public float ignoreInputAfterEnableSeconds = 0.08f;

    [Tooltip("If the TutorialUI is closed by its own Close button, this script auto-detects that and restores input/time.")]
    public bool autoSyncIfTutorialClosedExternally = true;

    [Tooltip("Extra lockout applied after an external close is detected (prevents immediate flash on next TAB).")]
    [Range(0.05f, 0.6f)]
    public float lockoutAfterExternalCloseSeconds = 0.22f;

    [Header("Debug")]
    public bool debugLogs = false;

    // =========================
    // INTERNALS
    // =========================
    private bool _isOpen;
    private bool _transitioning;
    private bool _tabWasDown;

    private float _lockoutUntilUnscaled;
    private float _ignoreUntilUnscaled;

    // Cached reflection for TutorialUI.root (best-effort)
    private FieldInfo _tutorialRootField;
    private PropertyInfo _tutorialRootProp;

    private void Awake()
    {
        // Auto-find TutorialUI if not assigned
        if (tutorialUI == null)
        {
#if UNITY_2023_1_OR_NEWER
            tutorialUI = UnityEngine.Object.FindFirstObjectByType<TutorialUI>(FindObjectsInactive.Include);
#else
            tutorialUI = UnityEngine.Object.FindObjectOfType<TutorialUI>(true);
#endif
        }

        CacheTutorialUIRootMember();
    }

    private void OnEnable()
    {
        _ignoreUntilUnscaled = Time.unscaledTime + ignoreInputAfterEnableSeconds;

        if (toggleAction != null && toggleAction.action != null)
        {
            toggleAction.action.Enable();
            toggleAction.action.performed -= OnTogglePerformed;
            toggleAction.action.performed += OnTogglePerformed;
        }
    }

    private void OnDisable()
    {
        if (toggleAction != null && toggleAction.action != null)
        {
            toggleAction.action.performed -= OnTogglePerformed;
            toggleAction.action.Disable();
        }

        if (_isOpen) ForceCloseAndRestore();
        else
        {
            RestoreTimeScaleIfWePaused();
            RestoreActionMapIfWeSwapped();
        }
    }

    private void Update()
    {
        // ✅ Auto-sync if the tutorial was closed via its own UI close button
        if (autoSyncIfTutorialClosedExternally && _isOpen && !_transitioning)
        {
            if (!IsTutorialUIVisible())
            {
                if (debugLogs) Debug.Log("[MinigameReviewRules] Detected external TutorialUI close -> restoring state");
                HandleExternalCloseRestore();
            }
        }

        // If using InputActionReference, we don't poll
        if (toggleAction != null && toggleAction.action != null)
            return;

        if (!enableTabFallback) return;

        var kb = Keyboard.current;
        if (kb == null) return;

        bool down = kb.tabKey.isPressed;
        if (down && !_tabWasDown)
            RequestToggle();

        _tabWasDown = down;
    }

    private void OnTogglePerformed(InputAction.CallbackContext ctx)
    {
        RequestToggle();
    }

    private void RequestToggle()
    {
        if (Time.unscaledTime < _ignoreUntilUnscaled) return;
        if (Time.unscaledTime < _lockoutUntilUnscaled) return;
        if (_transitioning) return;

        _lockoutUntilUnscaled = Time.unscaledTime + toggleDebounceSeconds;

        if (_isOpen) StartCoroutine(CoCloseStable());
        else StartCoroutine(CoOpenStable());
    }

    // =========================
    // STABLE OPEN/CLOSE
    // =========================
    private IEnumerator CoOpenStable()
    {
        _transitioning = true;

        if (reviewRulesTutorial == null)
        {
            Debug.LogWarning("[MinigameReviewRules] No reviewRulesTutorial assigned.");
            _transitioning = false;
            yield break;
        }

        if (tutorialUI == null)
        {
            Debug.LogError("[MinigameReviewRules] tutorialUI is not assigned and could not be auto-found.");
            _transitioning = false;
            yield break;
        }

        PrimeEventSystemAndUIModule();
        yield return new WaitForEndOfFrame();

        PauseTimeScaleLikeSaveMenu();
        SwapToUIMap();
        yield return new WaitForEndOfFrame();

        Transform player = null;
        var pgo = GameObject.FindGameObjectWithTag(playerTag);
        if (pgo != null) player = pgo.transform;

        if (debugLogs) Debug.Log("[MinigameReviewRules] OPEN review rules (TAB)");

        _isOpen = true;
        tutorialUI.Show(reviewRulesTutorial, player);

        if (EventSystem.current)
            EventSystem.current.SetSelectedGameObject(null);

        _transitioning = false;
    }

    private IEnumerator CoCloseStable()
    {
        _transitioning = true;

        PrimeEventSystemAndUIModule();
        yield return new WaitForEndOfFrame();

        if (debugLogs) Debug.Log("[MinigameReviewRules] CLOSE review rules (TAB)");

        _isOpen = false;

        TryCloseTutorialUI();
        RestoreTimeScaleIfWePaused();
        RestoreActionMapIfWeSwapped();

        if (EventSystem.current)
            EventSystem.current.SetSelectedGameObject(null);

        _transitioning = false;
    }

    private void ForceCloseAndRestore()
    {
        _isOpen = false;
        _transitioning = false;

        TryCloseTutorialUI();
        RestoreTimeScaleIfWePaused();
        RestoreActionMapIfWeSwapped();

        if (EventSystem.current)
            EventSystem.current.SetSelectedGameObject(null);
    }

    // =========================
    // EXTERNAL CLOSE SYNC
    // =========================
    private void HandleExternalCloseRestore()
    {
        // If TutorialUI closed itself, our locks may still be active -> restore them now.
        _isOpen = false;

        RestoreTimeScaleIfWePaused();
        RestoreActionMapIfWeSwapped();

        if (EventSystem.current)
            EventSystem.current.SetSelectedGameObject(null);

        // Add a short lockout so the next TAB press doesn't “fight” with UI close input
        _lockoutUntilUnscaled = Time.unscaledTime + lockoutAfterExternalCloseSeconds;
    }

    private bool IsTutorialUIVisible()
    {
        if (tutorialUI == null) return false;

        // Best case: TutorialUI has a public field/property named "root" that is a GameObject
        GameObject rootObj = null;

        try
        {
            if (_tutorialRootField != null && _tutorialRootField.FieldType == typeof(GameObject))
                rootObj = _tutorialRootField.GetValue(tutorialUI) as GameObject;

            if (rootObj == null && _tutorialRootProp != null && _tutorialRootProp.PropertyType == typeof(GameObject))
                rootObj = _tutorialRootProp.GetValue(tutorialUI, null) as GameObject;
        }
        catch { /* ignore */ }

        if (rootObj != null)
            return rootObj.activeInHierarchy;

        // Fallback: use the TutorialUI component's GameObject visibility
        return tutorialUI.gameObject.activeInHierarchy;
    }

    private void CacheTutorialUIRootMember()
    {
        if (tutorialUI == null) return;

        var t = tutorialUI.GetType();
        // common naming: "root"
        _tutorialRootField = t.GetField("root", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        _tutorialRootProp = t.GetProperty("root", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }

    // =========================
    // EVENTSYSTEM / UI MODULE PRIME (BUILD FIX)
    // =========================
    private void PrimeEventSystemAndUIModule()
    {
        var es = EventSystem.current;
        if (es == null) return;

        es.sendNavigationEvents = true;

        var uiModule = es.GetComponent<InputSystemUIInputModule>();
        if (uiModule != null)
        {
            bool wasEnabled = uiModule.enabled;
            uiModule.enabled = false;
            uiModule.enabled = true;
            uiModule.enabled = wasEnabled ? true : uiModule.enabled;
        }

        es.SetSelectedGameObject(null);
    }

    // =========================
    // FREEZE / RESTORE
    // =========================
    private void PauseTimeScaleLikeSaveMenu()
    {
        if (!freezeWorldWithTimeScale) return;

        if (!_timeScaleCaptured)
        {
            _prevTimeScale = Time.timeScale;
            _timeScaleCaptured = true;
        }

        Time.timeScale = 0f;
    }

    private void RestoreTimeScaleIfWePaused()
    {
        if (!freezeWorldWithTimeScale) return;

        if (_timeScaleCaptured)
        {
            Time.timeScale = _prevTimeScale;
            _timeScaleCaptured = false;
        }
    }

    // =========================
    // ACTION MAP SWAP
    // =========================
    private PlayerInput ResolvePlayerInput()
    {
        if (playerInput) return playerInput;

#if UNITY_2023_1_OR_NEWER
        playerInput = UnityEngine.Object.FindFirstObjectByType<PlayerInput>(FindObjectsInactive.Include);
#else
        playerInput = UnityEngine.Object.FindObjectOfType<PlayerInput>(true);
#endif
        return playerInput;
    }

    private void SwapToUIMap()
    {
        if (!alsoSwapActionMap) return;

        var pi = ResolvePlayerInput();
        if (!pi) return;

        if (!_cachedPrevActionMap)
        {
            _prevActionMap = pi.currentActionMap != null ? pi.currentActionMap.name : "";
            _cachedPrevActionMap = true;
        }

        if (!string.IsNullOrWhiteSpace(uiActionMapName))
        {
            try { pi.SwitchCurrentActionMap(uiActionMapName); } catch { }
        }
    }

    private void RestoreActionMapIfWeSwapped()
    {
        if (!alsoSwapActionMap) return;

        var pi = ResolvePlayerInput();
        if (!pi) return;

        if (_cachedPrevActionMap)
        {
            string restore = !string.IsNullOrWhiteSpace(_prevActionMap) ? _prevActionMap : gameplayActionMapName;
            if (!string.IsNullOrWhiteSpace(restore))
            {
                try { pi.SwitchCurrentActionMap(restore); } catch { }
            }

            _cachedPrevActionMap = false;
            _prevActionMap = "";
        }
    }

    // =========================
    // CLOSE TUTORIAL UI (best-effort)
    // =========================
    private void TryCloseTutorialUI()
    {
        if (tutorialUI == null) return;

        tutorialUI.SendMessage("Hide", SendMessageOptions.DontRequireReceiver);
        tutorialUI.SendMessage("Close", SendMessageOptions.DontRequireReceiver);
    }
}