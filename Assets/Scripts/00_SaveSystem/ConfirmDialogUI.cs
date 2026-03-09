using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ConfirmDialogUI : MonoBehaviour
{
    [Header("Root")]
    [Tooltip("Optional: Assign the dialog panel root GameObject here. If null, this script will use its own GameObject and try to auto-find a panel root.")]
    public GameObject root;

    [Header("Text")]
    public TMP_Text titleText;
    public TMP_Text bodyText;

    [Header("Buttons (3-button prefab)")]
    public Button yesButton;
    public TMP_Text yesButtonText;

    public Button okButton;
    public TMP_Text okButtonText;

    public Button noButton;
    public TMP_Text noButtonText;

    [Header("Wiring Mode")]
    [Tooltip("ON = You wire button OnClick in Inspector to PressYesFromButton/PressOkFromButton/PressNoFromButton.\nOFF = Script overwrites listeners at runtime.")]
    public bool useInspectorOnClick = true;

    [Header("Optional: Load OK Targets (Menu vs 3D Scenes)")]
    public LoadMenuUI loadMenuUI;
    public SaveLoadMenuUI saveLoadMenuUI;

    [Header("Optional: Generic Profile Load Target (Preferred)")]
    public MonoBehaviour profileLoadTargetBehaviour;

    [Header("Focus / Sorting (Build & Controller Reliability)")]
    [Tooltip("Optional: what to select when dialog opens (if button exists this is ignored).")]
    public GameObject firstSelectedOverride;

    [Tooltip("If true, forces the dialog canvas to render on top of other UI.")]
    public bool forceTopMostCanvas = true;

    [Tooltip("Sorting order used when forceTopMostCanvas is enabled.")]
    public int topMostSortingOrder = 9999;

    [Header("Desktop Safety")]
    [Tooltip("Opens the load menu one frame later after closing this dialog. Fixes the classic 'first click does nothing' / double-click issue.")]
    public bool deferOpenLoadTargetNextFrame = true;

    [Tooltip("Stronger than next-frame: waits EndOfFrame. Usually not needed on desktop.")]
    public bool deferOpenLoadTargetEndOfFrame = false;

    [Tooltip("Extra safety: after closing, temporarily disables navigation/click for 1 frame so the 'submit' that closed this dialog can't immediately trigger slot UI.")]
    public bool blockOneFrameAfterClose = true;

    // callbacks
    private Action _onYes;
    private Action _onOk;
    private Action _onNo;

    // special ok behavior
    private bool _allowSpecialOkOpenLoadTarget = false;

    // focus routine
    private Coroutine _focusCo;

    // deferred open routine
    private Coroutine _deferOpenCo;

    // cached components
    private CanvasGroup _rootCanvasGroup;
    private Canvas _rootCanvas;

    private void Awake()
    {
        AutoResolveRootIfMissing();
        CacheRootComponents();

        if (!useInspectorOnClick)
            WireButtonsRuntime();

        HideImmediate();
    }

    private void OnEnable()
    {
        AutoResolveRootIfMissing();
        CacheRootComponents();

        if (!useInspectorOnClick)
            WireButtonsRuntime();
    }

    private void OnDisable()
    {
        StopFocusRoutine();
        StopDeferOpenRoutine();
    }

    private void AutoResolveRootIfMissing()
    {
        if (root != null) return;

        Transform t = transform.Find("Root");
        if (t == null) t = transform.Find("DialogRoot");
        if (t == null) t = transform.Find("Panel");
        if (t != null)
        {
            root = t.gameObject;
            return;
        }

        var cg = GetComponentInChildren<CanvasGroup>(true);
        if (cg != null && cg.gameObject != gameObject)
        {
            root = cg.gameObject;
            return;
        }

        root = gameObject;
    }

    private void CacheRootComponents()
    {
        var r = (root != null) ? root : gameObject;
        if (r == null) return;

        _rootCanvasGroup = r.GetComponent<CanvasGroup>();
        _rootCanvas = r.GetComponent<Canvas>();

        if (_rootCanvasGroup == null) _rootCanvasGroup = r.GetComponentInChildren<CanvasGroup>(true);
        if (_rootCanvas == null) _rootCanvas = r.GetComponentInChildren<Canvas>(true);
    }

    private void WireButtonsRuntime()
    {
        if (yesButton)
        {
            yesButton.onClick.RemoveAllListeners();
            yesButton.onClick.AddListener(PressYesFromButton);
        }

        if (okButton)
        {
            okButton.onClick.RemoveAllListeners();
            okButton.onClick.AddListener(PressOkFromButton);
        }

        if (noButton)
        {
            noButton.onClick.RemoveAllListeners();
            noButton.onClick.AddListener(PressNoFromButton);
        }
    }

    // =========================================================
    // Buttons
    // =========================================================
    public void PressYesFromButton()
    {
        var cb = _onYes;
        HideImmediate();
        cb?.Invoke();
    }

    public void PressOkFromButton()
    {
        var cb = _onOk;
        HideImmediate();
        cb?.Invoke();
    }

    public void PressNoFromButton()
    {
        var cb = _onNo;
        HideImmediate();
        cb?.Invoke();
    }

    // ✅ SPECIAL OK (opens load UI)
    public void PressOkForProfileLoadTargetFromButton()
    {
        // If this OK dialog was created via ShowOkOnly with a callback, respect it.
        if (_onOk != null)
        {
            PressOkFromButton();
            return;
        }

        if (!_allowSpecialOkOpenLoadTarget)
        {
            HideImmediate();
            return;
        }

        // Close dialog first
        HideImmediate();

        // Clear selection so the next UI doesn't inherit focus
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        var resolved = ResolveProfileLoadTarget(profileLoadTargetBehaviour);

        StopDeferOpenRoutine();
        _deferOpenCo = StartCoroutine(OpenLoadTargetDeferred(resolved));
    }

    // Backwards compatible
    public void PressOkForLoadMenuUIFromButton() => PressOkForProfileLoadTargetFromButton();

    private IEnumerator OpenLoadTargetDeferred(IProfileLoadTarget resolved)
    {
        // ✅ Key fix: let the click/submit that closed this dialog finish processing first.
        if (deferOpenLoadTargetEndOfFrame)
            yield return new WaitForEndOfFrame();
        else if (deferOpenLoadTargetNextFrame)
            yield return null;

        // Optional: block interactions one extra frame (prevents carryover submit)
        if (blockOneFrameAfterClose)
        {
            // Clear selection again right before open
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);

            // wait one more frame so new UI doesn't immediately receive the same submit/click
            yield return null;
        }

        // If you have your own primer, it can still help, but it’s not the main fix.
        if (UIInputPrimer.Instance != null)
            UIInputPrimer.Instance.PrimeNow();

        if (resolved != null)
        {
            resolved.OpenLoadOnly();
            _deferOpenCo = null;
            yield break;
        }

        if (loadMenuUI != null)
        {
            loadMenuUI.OpenLoadOnly();
            _deferOpenCo = null;
            yield break;
        }

        if (saveLoadMenuUI != null)
        {
            saveLoadMenuUI.OpenLoadOnly();
            _deferOpenCo = null;
            yield break;
        }

        Debug.LogWarning("[ConfirmDialogUI] Special OK requested, but no load target is assigned and no _onOk callback exists.");
        _deferOpenCo = null;
    }

    private void StopDeferOpenRoutine()
    {
        if (_deferOpenCo != null)
        {
            StopCoroutine(_deferOpenCo);
            _deferOpenCo = null;
        }
    }

    private IProfileLoadTarget ResolveProfileLoadTarget(MonoBehaviour mb)
    {
        if (mb == null) return null;

        if (mb is IProfileLoadTarget direct)
            return direct;

        var comps = mb.GetComponents<MonoBehaviour>();
        for (int i = 0; i < comps.Length; i++)
            if (comps[i] is IProfileLoadTarget t) return t;

        var parents = mb.GetComponentsInParent<MonoBehaviour>(true);
        for (int i = 0; i < parents.Length; i++)
            if (parents[i] is IProfileLoadTarget t) return t;

        var children = mb.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < children.Length; i++)
            if (children[i] is IProfileLoadTarget t) return t;

        return null;
    }

    // =========================================================
    // Show/Hide
    // =========================================================
    public void Hide() => HideImmediate();

    public void HideImmediate()
    {
        StopFocusRoutine();
        StopDeferOpenRoutine();

        _onYes = null;
        _onOk = null;
        _onNo = null;

        _allowSpecialOkOpenLoadTarget = false;

        SetYesVisible(true);
        SetOkVisible(true);
        SetNoVisible(true);

        SetRaycastBlocking(false);
        ShowRoot(false);

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    public void ShowConfirmCancel(string title, string body, string yesText, string noText, Action onYes, Action onNo)
    {
        _onYes = onYes;
        _onNo = onNo;
        _onOk = null;

        _allowSpecialOkOpenLoadTarget = false;

        if (titleText) titleText.text = title ?? "";
        if (bodyText) bodyText.text = body ?? "";

        if (yesButtonText) yesButtonText.text = string.IsNullOrWhiteSpace(yesText) ? "Yes" : yesText;
        if (noButtonText) noButtonText.text = string.IsNullOrWhiteSpace(noText) ? "No" : noText;

        SetYesVisible(true);
        SetNoVisible(true);
        SetOkVisible(false);

        ShowRoot(true);
        EnsureDialogReadyNow();
        SetRaycastBlocking(true);

        var focus = (yesButton && yesButton.gameObject.activeInHierarchy) ? yesButton.gameObject :
                    (noButton && noButton.gameObject.activeInHierarchy) ? noButton.gameObject :
                    firstSelectedOverride;

        FocusNextFrame(focus);
    }

    public void ShowOkOnly(string title, string body, string okText, Action onOk)
    {
        _onOk = onOk;
        _onYes = null;
        _onNo = null;

        _allowSpecialOkOpenLoadTarget = false;

        if (titleText) titleText.text = title ?? "";
        if (bodyText) bodyText.text = body ?? "";

        if (okButtonText) okButtonText.text = string.IsNullOrWhiteSpace(okText) ? "OK" : okText;

        SetYesVisible(false);
        SetNoVisible(false);
        SetOkVisible(true);

        ShowRoot(true);
        EnsureDialogReadyNow();
        SetRaycastBlocking(true);

        var focus = (okButton && okButton.gameObject.activeInHierarchy) ? okButton.gameObject : firstSelectedOverride;
        FocusNextFrame(focus);
    }

    public void ShowOkOnly_OpenLoadTargetOnOk(string title, string body, string okText)
    {
        _onOk = null;
        _onYes = null;
        _onNo = null;

        _allowSpecialOkOpenLoadTarget = true;

        if (titleText) titleText.text = title ?? "";
        if (bodyText) bodyText.text = body ?? "";

        if (okButtonText) okButtonText.text = string.IsNullOrWhiteSpace(okText) ? "OK" : okText;

        SetYesVisible(false);
        SetNoVisible(false);
        SetOkVisible(true);

        ShowRoot(true);
        EnsureDialogReadyNow();
        SetRaycastBlocking(true);

        var focus = (okButton && okButton.gameObject.activeInHierarchy) ? okButton.gameObject : firstSelectedOverride;
        FocusNextFrame(focus);
    }

    public void SetYesVisible(bool visible) { if (yesButton) yesButton.gameObject.SetActive(visible); }
    public void SetOkVisible(bool visible) { if (okButton) okButton.gameObject.SetActive(visible); }
    public void SetNoVisible(bool visible) { if (noButton) noButton.gameObject.SetActive(visible); }

    private void ShowRoot(bool show)
    {
        AutoResolveRootIfMissing();

        if (root != null) root.SetActive(show);
        else gameObject.SetActive(show);
    }

    private void EnsureDialogReadyNow()
    {
        AutoResolveRootIfMissing();
        CacheRootComponents();

        Canvas.ForceUpdateCanvases();

        var rt = (root != null) ? root.GetComponent<RectTransform>() : GetComponent<RectTransform>();
        if (rt) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);

        if (forceTopMostCanvas && _rootCanvas != null)
        {
            _rootCanvas.overrideSorting = true;
            _rootCanvas.sortingOrder = topMostSortingOrder;
        }

        if (_rootCanvasGroup != null)
        {
            _rootCanvasGroup.alpha = 1f;
            _rootCanvasGroup.interactable = true;
            _rootCanvasGroup.blocksRaycasts = true;
        }
    }

    private void FocusNextFrame(GameObject go)
    {
        StopFocusRoutine();
        if (!isActiveAndEnabled) return;
        _focusCo = StartCoroutine(FocusNextFrameRoutine(go));
    }

    private IEnumerator FocusNextFrameRoutine(GameObject go)
    {
        yield return null;

        if (!isActiveAndEnabled) yield break;
        if (go == null || !go.activeInHierarchy) yield break;

        var es = EventSystem.current;
        if (es == null) yield break;

        es.SetSelectedGameObject(null);
        es.SetSelectedGameObject(go);
        _focusCo = null;
    }

    private void StopFocusRoutine()
    {
        if (_focusCo != null)
        {
            StopCoroutine(_focusCo);
            _focusCo = null;
        }
    }

    private void SetRaycastBlocking(bool on)
    {
        AutoResolveRootIfMissing();
        GameObject r = (root != null) ? root : gameObject;
        if (r == null) return;

        var cgs = r.GetComponentsInChildren<CanvasGroup>(true);
        for (int i = 0; i < cgs.Length; i++)
        {
            cgs[i].blocksRaycasts = on;
            cgs[i].interactable = on;
            if (on) cgs[i].alpha = 1f;
        }
    }
}