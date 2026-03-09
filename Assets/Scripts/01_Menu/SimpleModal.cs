using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using UnityEngine.EventSystems;

public class SimpleModal : MonoBehaviour
{
    [Header("Root")]
    [Tooltip("The panel GameObject that gets enabled/disabled.")]
    public GameObject root;

    [Header("Text")]
    public TMP_Text messageText;

    [Header("Buttons")]
    public Button yesButton;   // Confirm
    public Button noButton;    // Confirm
    public Button okButton;    // Info

    [Header("Focus")]
    [Tooltip("Optional override. If null, defaults to Yes.")]
    public Selectable firstConfirmSelectable;

    [Tooltip("Optional override. If null, defaults to OK.")]
    public Selectable firstInfoSelectable;

    [Header("Trap Selection While Open")]
    [Tooltip("Keeps controller selection inside the modal while it is open.")]
    public bool trapSelection = true;

    [Tooltip("How many frames we retry focusing if Unity clears selection.")]
    public int focusRetryFrames = 6;

    // callbacks
    private Action _onYes;
    private Action _onNo;
    private Action _onOk;

    // selection restore
    private GameObject _previousSelected;

    // focusing
    private Coroutine _focusRoutine;
    private Selectable[] _modalSelectables;
    private Selectable _preferredSelectable;

    public bool IsOpen() => root != null && root.activeInHierarchy;

    private void Awake()
    {
        Hide();
        CacheSelectables();

        if (yesButton)
            yesButton.onClick.AddListener(() =>
            {
                Hide();
                _onYes?.Invoke();
            });

        if (noButton)
            noButton.onClick.AddListener(() =>
            {
                Hide();
                _onNo?.Invoke();
            });

        if (okButton)
            okButton.onClick.AddListener(() =>
            {
                Hide();
                _onOk?.Invoke();
            });
    }

    private void Update()
    {
        if (!trapSelection) return;
        if (root == null || !root.activeInHierarchy) return;

        var es = EventSystem.current;
        if (es == null) return;

        var current = es.currentSelectedGameObject;

        // ✅ IMPORTANT:
        // Do NOT fight navigation between YES/NO.
        // Only repair selection if it's null or escaped the modal.
        if (current == null || !IsSelectableInsideModal(current))
        {
            TryFocusPreferredSelectable();
        }
    }

    // ===== PUBLIC API =====

    public void ShowConfirm(string message, Action onYes, Action onNo)
    {
        if (messageText) messageText.text = message;

        _onYes = onYes;
        _onNo = onNo;
        _onOk = null;

        SetButtons(confirm: true);
        Show();

        // ✅ Ensure YES <-> NO navigation works reliably for controller
        ConfigureConfirmNavigation();

        FocusModal(confirm: true);
    }

    public void ShowInfo(string message, Action onOk)
    {
        if (messageText) messageText.text = message;

        _onYes = null;
        _onNo = null;
        _onOk = onOk;

        SetButtons(confirm: false);
        Show();

        FocusModal(confirm: false);
    }

    public void Hide()
    {
        StopFocusRoutine();

        if (root)
            root.SetActive(false);

        RestorePreviousSelection();
    }

    // ===== INTERNAL =====

    private void Show()
    {
        if (root)
            root.SetActive(true);

        CacheSelectables();
    }

    private void SetButtons(bool confirm)
    {
        if (yesButton) yesButton.gameObject.SetActive(confirm);
        if (noButton) noButton.gameObject.SetActive(confirm);
        if (okButton) okButton.gameObject.SetActive(!confirm);
    }

    private void CacheSelectables()
    {
        if (root == null)
            _modalSelectables = GetComponentsInChildren<Selectable>(true);
        else
            _modalSelectables = root.GetComponentsInChildren<Selectable>(true);
    }

    private bool IsSelectableInsideModal(GameObject go)
    {
        if (go == null) return false;
        if (_modalSelectables == null || _modalSelectables.Length == 0) CacheSelectables();

        for (int i = 0; i < _modalSelectables.Length; i++)
        {
            var s = _modalSelectables[i];
            if (s == null) continue;
            if (s.gameObject == go) return true;
        }
        return false;
    }

    private void FocusModal(bool confirm)
    {
        var es = EventSystem.current;
        if (es == null)
        {
            Debug.LogWarning("SimpleModal: No EventSystem in scene.");
            return;
        }

        _previousSelected = es.currentSelectedGameObject;

        // Pick preferred selectable
        if (confirm)
        {
            _preferredSelectable = firstConfirmSelectable;

            if (_preferredSelectable == null && yesButton) _preferredSelectable = yesButton;
            if (_preferredSelectable == null && noButton) _preferredSelectable = noButton;
        }
        else
        {
            _preferredSelectable = firstInfoSelectable;

            if (_preferredSelectable == null && okButton) _preferredSelectable = okButton;
        }

        // If assigned selectable is inactive/non-interactable, fallback to any valid one
        _preferredSelectable = PickFirstValidSelectable(_preferredSelectable);

        StopFocusRoutine();
        _focusRoutine = StartCoroutine(FocusAfterEnableRoutine());
    }

    private void StopFocusRoutine()
    {
        if (_focusRoutine != null)
        {
            StopCoroutine(_focusRoutine);
            _focusRoutine = null;
        }
    }

    // ✅ Key fix: wait EndOfFrame + retry focusing a few frames
    private IEnumerator FocusAfterEnableRoutine()
    {
        // Wait one frame AND end of frame so Unity fully enables buttons/layout
        yield return null;
        yield return new WaitForEndOfFrame();

        for (int i = 0; i < Mathf.Max(1, focusRetryFrames); i++)
        {
            if (TryFocusPreferredSelectable())
                break;

            yield return null;
        }

        _focusRoutine = null;
    }

    private bool TryFocusPreferredSelectable()
    {
        var es = EventSystem.current;
        if (es == null) return false;
        if (root == null || !root.activeInHierarchy) return false;

        _preferredSelectable = PickFirstValidSelectable(_preferredSelectable);
        if (_preferredSelectable == null) return false;

        // Clear selection first (important)
        es.SetSelectedGameObject(null);

        // Set selected GO
        es.SetSelectedGameObject(_preferredSelectable.gameObject);

        // Force-select on the Selectable too (important)
        _preferredSelectable.Select();

        // If Unity still didn't accept it, return false so routine retries
        return es.currentSelectedGameObject == _preferredSelectable.gameObject;
    }

    private Selectable PickFirstValidSelectable(Selectable preferred)
    {
        if (preferred != null &&
            preferred.gameObject.activeInHierarchy &&
            preferred.IsInteractable())
            return preferred;

        if (_modalSelectables == null || _modalSelectables.Length == 0) CacheSelectables();

        for (int i = 0; i < _modalSelectables.Length; i++)
        {
            var s = _modalSelectables[i];
            if (s == null) continue;
            if (!s.gameObject.activeInHierarchy) continue;
            if (!s.IsInteractable()) continue;
            return s;
        }

        return null;
    }

    private void RestorePreviousSelection()
    {
        var es = EventSystem.current;
        if (es == null) return;

        es.SetSelectedGameObject(null);

        if (_previousSelected != null && _previousSelected.activeInHierarchy)
            es.SetSelectedGameObject(_previousSelected);
    }

    // ✅ Ensures controller navigation works between YES and NO
    private void ConfigureConfirmNavigation()
    {
        if (yesButton == null || noButton == null) return;
        if (!yesButton.gameObject.activeInHierarchy || !noButton.gameObject.activeInHierarchy) return;

        // Yes -> Right goes to No
        var yesNav = yesButton.navigation;
        yesNav.mode = Navigation.Mode.Explicit;
        yesNav.selectOnRight = noButton;
        yesNav.selectOnLeft = null;
        yesButton.navigation = yesNav;

        // No -> Left goes to Yes
        var noNav = noButton.navigation;
        noNav.mode = Navigation.Mode.Explicit;
        noNav.selectOnLeft = yesButton;
        noNav.selectOnRight = null;
        noButton.navigation = noNav;

        // Optional: Up/Down can also link between them if you want vertical nav too.
        // Uncomment if your layout is vertical:
        // yesNav.selectOnDown = noButton;
        // noNav.selectOnUp = yesButton;
        // yesButton.navigation = yesNav;
        // noButton.navigation = noNav;
    }
}
