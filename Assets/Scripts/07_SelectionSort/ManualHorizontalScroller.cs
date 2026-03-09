using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class ManualHorizontalScroller : MonoBehaviour
{
    [Header("Refs")]
    public RectTransform viewport;   // GalleryArea
    public RectTransform content;    // PortraitGrid

    [Header("Buttons (optional)")]
    public Button leftButton;
    public Button rightButton;

    [Header("Open Behavior")]
    public bool snapToStartOnEnable = true;

    [Header("Paging")]
    public float pageWidthOverride = 0f;
    public float pageWidthMultiplier = 1f;
    public float pageExtraOffset = 0f;

    [Header("Motion")]
    public float smooth = 18f;
    public float settleThreshold = 0.1f;

    [Header("Clamp")]
    public float edgePadding = 0f;

    [Header("Controller / Keyboard Support")]
    public bool enableInputPaging = true;

    [Tooltip("Usually UI/Navigate or another Vector2 action.")]
    public InputActionReference navigateAction;

    [Tooltip("Optional submit action if you want to press a button while focusing left/right arrows.")]
    public InputActionReference submitAction;

    [Tooltip("Stick / dpad deadzone before paging.")]
    public float navigationDeadzone = 0.5f;

    [Tooltip("Delay between horizontal page steps.")]
    public float navigationRepeatDelay = 0.2f;

    [Tooltip("If true, left/right on the navigate action pages the scroller.")]
    public bool useNavigateForPaging = true;

    [Header("Debug")]
    public bool verboseLogs = true;

    private Vector2 _target;
    private bool _ready;
    private float _nextNavigateTime;

    void Reset()
    {
        viewport = GetComponent<RectTransform>();
    }

    void Awake()
    {
        if (!viewport) viewport = GetComponent<RectTransform>();

        if (leftButton) leftButton.onClick.AddListener(ScrollLeft);
        if (rightButton) rightButton.onClick.AddListener(ScrollRight);

        if (content) _target = content.anchoredPosition;
    }

    void OnEnable()
    {
        _ready = false;
        _nextNavigateTime = 0f;

        EnableActions(true);

        StopAllCoroutines();
        StartCoroutine(InitAfterLayout());
    }

    void OnDisable()
    {
        EnableActions(false);
    }

    private void EnableActions(bool enable)
    {
        if (navigateAction != null && navigateAction.action != null)
        {
            if (enable) navigateAction.action.Enable();
            else navigateAction.action.Disable();
        }

        if (submitAction != null && submitAction.action != null)
        {
            if (enable) submitAction.action.Enable();
            else submitAction.action.Disable();
        }
    }

    IEnumerator InitAfterLayout()
    {
        // Wait for UI + layout groups + instantiation to finish
        yield return null;
        yield return new WaitForEndOfFrame();

        ForceLayoutNow();

        if (!viewport || !content)
        {
            if (verboseLogs) Debug.LogWarning("[Scroller] Missing viewport/content refs. Assign them on GalleryArea.", this);
            yield break;
        }

        DiagnoseIfBadLayout("InitAfterLayout");

        _target = content.anchoredPosition;

        if (snapToStartOnEnable)
            JumpToStartImmediate();
        else
            ClampTargetToBounds();

        ApplyImmediate();
        UpdateButtons();

        _ready = true;
    }

    void Update()
    {
        if (!enableInputPaging || !_ready) return;

        HandleInputPaging();
    }

    void LateUpdate()
    {
        if (!viewport || !content) return;

        if (content.rect.width <= 0.01f)
        {
            DiagnoseIfBadLayout("LateUpdate");
            return;
        }

        content.anchoredPosition = Vector2.Lerp(
            content.anchoredPosition,
            _target,
            Time.unscaledDeltaTime * Mathf.Max(0.01f, smooth)
        );

        if (Vector2.Distance(content.anchoredPosition, _target) <= settleThreshold)
            content.anchoredPosition = _target;
    }

    private void HandleInputPaging()
    {
        Vector2 nav = Vector2.zero;
        if (navigateAction != null && navigateAction.action != null)
            nav = navigateAction.action.ReadValue<Vector2>();

        if (useNavigateForPaging && Time.unscaledTime >= _nextNavigateTime)
        {
            if (nav.x <= -navigationDeadzone)
            {
                ScrollLeft();
                _nextNavigateTime = Time.unscaledTime + Mathf.Max(0.01f, navigationRepeatDelay);
            }
            else if (nav.x >= navigationDeadzone)
            {
                ScrollRight();
                _nextNavigateTime = Time.unscaledTime + Mathf.Max(0.01f, navigationRepeatDelay);
            }
        }

        if (submitAction != null && submitAction.action != null && submitAction.action.WasPressedThisFrame())
        {
            // Optional convenience:
            // If one of the arrow buttons is selected in EventSystem, pressing Submit will still invoke it naturally.
            // This method is here only to keep input enabled; no extra action needed.
        }
    }

    public void ScrollLeft() => ScrollPages(-1);
    public void ScrollRight() => ScrollPages(+1);

    public void ScrollPages(int dir)
    {
        if (!viewport || !content)
        {
            if (verboseLogs) Debug.LogWarning("[Scroller] Button clicked but viewport/content is null. Assign refs on GalleryArea.", this);
            return;
        }

        ForceLayoutNow();
        DiagnoseIfBadLayout("ScrollPages");

        float viewportW = viewport.rect.width;
        float contentW = content.rect.width;

        if (verboseLogs)
            Debug.Log($"[Scroller] Click dir={dir} | viewportW={viewportW:F1} contentW={contentW:F1} targetX={_target.x:F1}", this);

        if (contentW <= viewportW + 0.01f)
        {
            if (verboseLogs)
                Debug.LogWarning("[Scroller] Content width <= viewport width. PortraitGrid is NOT expanding. " +
                                 "Fix: PortraitGrid needs a HorizontalLayoutGroup + ContentSizeFitter(H=Preferred) OR a LayoutElement width. " +
                                 "Also make sure PortraitGrid anchors are NOT stretch.", this);

            UpdateButtons();
            return;
        }

        float step = GetPageStep();
        float deltaX = -step * dir;

        _target += new Vector2(deltaX, 0f);

        ClampTargetToBounds();
        ApplyImmediateOneFrame();
        UpdateButtons();

        _ready = true;
    }

    public void ScrollToStart()
    {
        JumpToStartImmediate();
        ApplyImmediate();
        UpdateButtons();
    }

    public void ScrollToEnd()
    {
        if (!viewport || !content) return;

        ForceLayoutNow();
        GetClampRange(out float minX, out float maxX);
        _target = new Vector2(minX, content.anchoredPosition.y);

        ApplyImmediate();
        UpdateButtons();
    }

    public void ScrollToApproxIndex(int index, int itemsPerPage)
    {
        if (!viewport || !content) return;
        if (itemsPerPage <= 0) return;

        ForceLayoutNow();

        int page = Mathf.Max(0, index / itemsPerPage);
        float step = GetPageStep();

        _target = new Vector2(-(page * step), _target.y);
        ClampTargetToBounds();
        ApplyImmediateOneFrame();
        UpdateButtons();
    }

    float GetPageStep()
    {
        float baseWidth = (pageWidthOverride > 0f) ? pageWidthOverride : viewport.rect.width;
        float mult = Mathf.Max(0.01f, pageWidthMultiplier);
        return baseWidth * mult + pageExtraOffset;
    }

    public void ForceLayoutNow()
    {
        Canvas.ForceUpdateCanvases();

        if (content)
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);

        Canvas.ForceUpdateCanvases();
    }

    public void JumpToStartImmediate()
    {
        if (!viewport || !content) return;

        ForceLayoutNow();
        DiagnoseIfBadLayout("JumpToStartImmediate");

        GetClampRange(out float minX, out float maxX);
        _target = new Vector2(maxX, content.anchoredPosition.y);
    }

    void ClampTargetToBounds()
    {
        GetClampRange(out float minX, out float maxX);
        _target = new Vector2(Mathf.Clamp(_target.x, minX, maxX), _target.y);

        if (verboseLogs)
            Debug.Log($"[Scroller] Clamp range: minX={minX:F1} maxX={maxX:F1} => targetX={_target.x:F1}", this);
    }

    void GetClampRange(out float minX, out float maxX)
    {
        float viewportW = viewport.rect.width;
        float contentW = content.rect.width;

        if (contentW <= viewportW + 0.01f)
        {
            minX = maxX = edgePadding;
            return;
        }

        maxX = edgePadding;
        minX = -(contentW - viewportW) - edgePadding;
    }

    void ApplyImmediate()
    {
        if (!content) return;
        content.anchoredPosition = _target;
    }

    void ApplyImmediateOneFrame()
    {
        if (!content) return;
        content.anchoredPosition = _target;
    }

    void UpdateButtons()
    {
        if (!leftButton && !rightButton) return;
        if (!viewport || !content) return;

        GetClampRange(out float minX, out float maxX);

        bool canGoLeft = _target.x < maxX - 0.01f;
        bool canGoRight = _target.x > minX + 0.01f;

        if (leftButton) leftButton.interactable = canGoLeft;
        if (rightButton) rightButton.interactable = canGoRight;
    }

    private void DiagnoseIfBadLayout(string caller)
    {
        if (!verboseLogs || !viewport || !content) return;

        float vw = viewport.rect.width;
        float cw = content.rect.width;

        if (vw <= 0.01f)
        {
            Debug.LogWarning($"[Scroller][{caller}] Viewport width is ~0. This usually means the parent panel is scaled to 0 or disabled, " +
                             $"or anchors are wrong. viewport='{viewport.name}'", this);
        }

        if (cw <= 0.01f)
        {
            Debug.LogWarning($"[Scroller][{caller}] Content width is ~0. This means PortraitGrid is collapsing. " +
                             $"Fix PortraitGrid: add HorizontalLayoutGroup + ContentSizeFitter(H=Preferred), " +
                             $"set child cards to LayoutElement preferred width, and avoid stretch anchors on PortraitGrid. content='{content.name}'", this);
        }
    }
}