using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class TutorialUI : MonoBehaviour
{
    [Header("Root")]
    public GameObject root; // assign TutorialUI root panel (can be this.gameObject)

    [Header("Page UI")]
    public Image pageImage;
    public TMP_Text pageLabel; // optional "1/3"

    [Header("Buttons")]
    public Button nextButton;
    public Button prevButton;
    public Button closeButton;

    [Header("Controller Selection (optional)")]
    public bool forceSelectCloseOnOpen = true;
    public Selectable firstSelectedOnOpen; // if empty, uses closeButton then nextButton then prevButton

    [Header("Player Lock (optional)")]
    public PlayerMovement playerMovement;
    public bool lockPlayerWhileOpen = true;

    [Header("Button Visibility (non-loop)")]
    [Tooltip("Alpha when a nav button is NOT available (0 = fully invisible).")]
    [Range(0f, 1f)] public float hiddenNavAlpha = 0f;

    [Tooltip("Alpha when a nav button IS available.")]
    [Range(0f, 1f)] public float visibleNavAlpha = 1f;

    [Header("Close Button Rule")]
    [Tooltip("If true, Close button only shows on the LAST page (when not looping).")]
    public bool closeOnlyOnLastPage = true;

    // =========================================================
    // ✅ NEW: TAB HOTKEY (TOGGLE)
    // =========================================================
    [Header("Hotkey (NEW)")]
    [Tooltip("If true, pressing Tab will toggle this Tutorial UI.")]
    public bool enableTabToggle = true;

    [Tooltip("Optional: if Tab is pressed and no tutorial is open yet, it will open this default tutorial.")]
    public TutorialSequenceSO defaultTutorialOnTab;

    [Tooltip("If assigned, Tab toggle uses this input action instead of raw Keyboard tab.")]
    public InputActionReference toggleAction;

    [Tooltip("Fallback to Keyboard Tab if toggleAction is not assigned.")]
    public bool useKeyboardTabFallback = true;

    public bool IsOpen { get; private set; }

    private TutorialSequenceSO _current;
    private int _index;

    // CanvasGroups for fading buttons (auto-added if missing)
    private CanvasGroup _nextCG;
    private CanvasGroup _prevCG;
    private CanvasGroup _closeCG;

    // hotkey state
    private bool _tabWasDown;

    private void Awake()
    {
        if (root == null) root = gameObject;

        if (nextButton) nextButton.onClick.AddListener(Next);
        if (prevButton) prevButton.onClick.AddListener(Prev);
        if (closeButton) closeButton.onClick.AddListener(Close);

        CacheOrAddCanvasGroups();

        // start closed
        root.SetActive(false);
        IsOpen = false;
    }

    private void OnEnable()
    {
        if (toggleAction != null && toggleAction.action != null)
            toggleAction.action.Enable();
    }

    private void OnDisable()
    {
        if (toggleAction != null && toggleAction.action != null)
            toggleAction.action.Disable();
    }

    private void Update()
    {
        // ✅ NEW: Tab toggle handler (does NOT affect existing tutorial logic)
        if (enableTabToggle)
            HandleTabToggle();
    }

    private void HandleTabToggle()
    {
        // Preferred: Input Action
        if (toggleAction != null && toggleAction.action != null)
        {
            if (toggleAction.action.WasPressedThisFrame())
                ToggleFromHotkey();
            return;
        }

        // Fallback: raw keyboard Tab
        if (!useKeyboardTabFallback) return;

        var kb = Keyboard.current;
        if (kb == null) return;

        bool down = kb.tabKey.isPressed;
        if (down && !_tabWasDown)
            ToggleFromHotkey();

        _tabWasDown = down;
    }

    private void ToggleFromHotkey()
    {
        // If already open, close it.
        if (IsOpen)
        {
            Close();
            return;
        }

        // If closed, open something sensible.
        // If you set a default tutorial, open it.
        if (defaultTutorialOnTab != null)
        {
            Show(defaultTutorialOnTab);
            return;
        }

        // If no default is set, just open the root (won't show anything unless _current exists).
        // This avoids null tutorial errors.
        if (root != null)
        {
            root.SetActive(true);
            IsOpen = true;

            // lock movement consistent with Show()
            if (lockPlayerWhileOpen && playerMovement != null)
                playerMovement.canMove = false;

            ForceSelectionOnOpen();
        }
    }

    private void CacheOrAddCanvasGroups()
    {
        if (nextButton) _nextCG = GetOrAddCanvasGroup(nextButton.gameObject);
        if (prevButton) _prevCG = GetOrAddCanvasGroup(prevButton.gameObject);
        if (closeButton) _closeCG = GetOrAddCanvasGroup(closeButton.gameObject);
    }

    private CanvasGroup GetOrAddCanvasGroup(GameObject go)
    {
        if (go == null) return null;
        var cg = go.GetComponent<CanvasGroup>();
        if (cg == null) cg = go.AddComponent<CanvasGroup>();
        return cg;
    }

    // ✅ IMPORTANT: overload so CutsceneRunner can call Show(seq) via reflection
    public void Show(TutorialSequenceSO seq)
    {
        Show(seq, null);
    }

    // ✅ Keep your original signature (DO NOT REMOVE)
    public void Show(TutorialSequenceSO seq, Transform player = null)
    {
        if (seq == null || seq.pages == null || seq.pages.Length == 0)
        {
            Debug.LogWarning("[TutorialUI] Show called but sequence is empty.");
            return;
        }

        _current = seq;
        _index = 0;

        // find PlayerMovement if not assigned
        if (playerMovement == null)
        {
            if (player != null) playerMovement = player.GetComponent<PlayerMovement>();
            if (playerMovement == null)
            {
                var pm = FindObjectOfType<PlayerMovement>(true);
                if (pm != null) playerMovement = pm;
            }
        }

        if (lockPlayerWhileOpen && playerMovement != null)
            playerMovement.canMove = false;

        root.SetActive(true);
        IsOpen = true;

        Refresh();
        ForceSelectionOnOpen();
    }

    public void Close()
    {
        if (!IsOpen) return;

        root.SetActive(false);
        IsOpen = false;

        if (lockPlayerWhileOpen && playerMovement != null)
            playerMovement.canMove = true;

        _current = null;
        _index = 0;

        // optional: clear sprite so you don’t see last frame if object gets re-enabled weirdly
        if (pageImage) pageImage.sprite = null;
        if (pageLabel) pageLabel.text = "";
    }

    public void Next()
    {
        if (_current == null || _current.pages == null || _current.pages.Length == 0) return;

        if (_current.loop)
        {
            _index = (_index + 1) % _current.pages.Length;
        }
        else
        {
            _index = Mathf.Clamp(_index + 1, 0, _current.pages.Length - 1);
        }

        Refresh();
        ForceSelectionOnOpen(); // keep selection stable when pressing buttons via controller
    }

    public void Prev()
    {
        if (_current == null || _current.pages == null || _current.pages.Length == 0) return;

        if (_current.loop)
        {
            _index = (_index - 1);
            if (_index < 0) _index = _current.pages.Length - 1;
        }
        else
        {
            _index = Mathf.Clamp(_index - 1, 0, _current.pages.Length - 1);
        }

        Refresh();
        ForceSelectionOnOpen();
    }

    private void Refresh()
    {
        if (_current == null || _current.pages == null || _current.pages.Length == 0)
        {
            Debug.LogWarning("[TutorialUI] Refresh called but current sequence is invalid.");
            return;
        }

        _index = Mathf.Clamp(_index, 0, _current.pages.Length - 1);

        // image
        if (pageImage != null)
        {
            var sprite = _current.pages[_index];
            pageImage.sprite = sprite;
            pageImage.enabled = (sprite != null);
        }
        else
        {
            Debug.LogWarning("[TutorialUI] pageImage is not assigned.");
        }

        // label
        if (pageLabel != null)
            pageLabel.text = $"{_index + 1}/{_current.pages.Length}";

        bool isLastPage = (_index == _current.pages.Length - 1);

        // buttons visibility + interactable
        if (_current.loop)
        {
            SetNavButtonState(prevButton, _prevCG, true);
            SetNavButtonState(nextButton, _nextCG, true);

            // In loop mode, Close is always visible (unless you want different behavior)
            SetCloseButtonState(true);
        }
        else
        {
            bool canPrev = _index > 0;
            bool canNext = _index < _current.pages.Length - 1;

            SetNavButtonState(prevButton, _prevCG, canPrev);
            SetNavButtonState(nextButton, _nextCG, canNext);

            // ✅ Close only appears on last page (if enabled)
            bool showClose = !closeOnlyOnLastPage || isLastPage;
            SetCloseButtonState(showClose);

            // Optional: if close is hidden and currently selected, move selection somewhere valid
            if (!showClose && EventSystem.current != null &&
                closeButton != null && EventSystem.current.currentSelectedGameObject == closeButton.gameObject)
            {
                ForceSelectionOnOpen();
            }
        }
    }

    private void SetNavButtonState(Button btn, CanvasGroup cg, bool available)
    {
        if (btn == null) return;

        btn.interactable = available;

        if (cg != null)
        {
            cg.alpha = available ? visibleNavAlpha : hiddenNavAlpha;
            cg.interactable = available;
            cg.blocksRaycasts = available;
        }
        else
        {
            btn.gameObject.SetActive(available); // last resort
        }

        if (!available && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == btn.gameObject)
        {
            ForceSelectionOnOpen();
        }
    }

    private void SetCloseButtonState(bool visible)
    {
        if (closeButton == null) return;

        if (_closeCG != null)
        {
            _closeCG.alpha = visible ? 1f : 0f;
            _closeCG.interactable = visible;
            _closeCG.blocksRaycasts = visible;
        }
        else
        {
            closeButton.gameObject.SetActive(visible);
        }

        closeButton.interactable = visible;
    }

    private void ForceSelectionOnOpen()
    {
        if (!forceSelectCloseOnOpen) return;
        if (EventSystem.current == null) return;
        if (!IsOpen) return;

        // If close is hidden, don't pick it as first selection
        bool closeVisible = true;
        if (closeButton != null && _closeCG != null)
            closeVisible = _closeCG.alpha > 0.001f;
        else if (closeButton != null)
            closeVisible = closeButton.gameObject.activeInHierarchy;

        Selectable s = firstSelectedOnOpen;

        if (s == null)
        {
            if (closeButton && closeVisible) s = closeButton;
            else if (nextButton) s = nextButton;
            else if (prevButton) s = prevButton;
        }

        if (s != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(s.gameObject);
        }
    }
}