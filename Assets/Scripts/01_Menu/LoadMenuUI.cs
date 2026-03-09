using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

public class LoadMenuUI : MonoBehaviour, IProfileLoadTarget
{
    [Header("Root")]
    public GameObject root;

    [Header("Title")]
    public TMP_Text titleText;

    [Header("Slots UI")]
    public GameObject slotsRoot;
    public Transform slotsParent;
    public SaveSlotRowUI slotPrefab;

    [Tooltip("Assign CanvasGroup on ScrollView/Viewport/Content (recommended).")]
    public CanvasGroup slotsCanvasGroup;

    [Header("Scroll")]
    [Tooltip("Assign the ScrollRect that contains the save slots.")]
    public ScrollRect slotsScrollRect;

    [Tooltip("Assign the Viewport RectTransform.")]
    public RectTransform slotsViewport;

    [Tooltip("If true, auto-scrolls to keep the currently selected slot visible.")]
    public bool autoScrollToSelected = true;

    [Tooltip("Extra space from viewport edge when auto-scrolling.")]
    [Range(0f, 64f)] public float scrollPadding = 12f;

    [Header("Buttons")]
    public Button closeButton;

    [Header("Popup")]
    public ConfirmDialogUI confirmDialog;

    [Header("Settings")]
    public int maxSlots = 5;

    [Header("Selection Fix")]
    public GameObject firstSelectedOnOpen;

    [Header("Modal Robustness")]
    [Tooltip("If true, modal is opened on next frame + forced to front (fixes first-click-in-scene issue).")]
    public bool openModalNextFrame = true;

    [Tooltip("Extra safety retry if modal didn't appear (rare build timing).")]
    [Range(0, 2)] public int modalShowRetries = 1;

    [Header("Controller Navigation Robustness")]
    [Tooltip("If true, while this menu is open it will auto-repair controller focus if selection becomes null.")]
    public bool repairSelectionWhileOpen = true;

    [Tooltip("How often (seconds) to check for lost selection while open. 0.05 = ~20x/sec.")]
    [Range(0.02f, 0.25f)] public float repairCheckInterval = 0.05f;

    [Tooltip("How many frames to retry selecting a target when opening/closing modals (UI can take a frame to be ready).")]
    [Range(1, 8)] public int selectRetryFrames = 4;

    private readonly List<SaveSlotRowUI> rows = new();
    private bool _showingModal = false;
    private int _pendingSlotToLoad = -1;

    private Coroutine _selectRoutine;
    private Coroutine _showModalRoutine;
    private Coroutine _repairRoutine;
    private Coroutine _selectRetryRoutine;
    private Coroutine _scrollRoutine;

    private void Awake()
    {
        if (closeButton)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(Close);
        }

        if (root) root.SetActive(false);
        if (confirmDialog) confirmDialog.HideImmediate();

        BindConfirmDialogTarget();
    }

    private void OnEnable()
    {
        BindConfirmDialogTarget();
    }

    private void OnDisable()
    {
        StopSelectRoutineSafe();
        StopShowModalRoutineSafe();
        StopRepairRoutineSafe();
        StopSelectRetryRoutineSafe();
        StopScrollRoutineSafe();
    }

    private void Update()
    {
        if (!repairSelectionWhileOpen) return;
        if (root == null || !root.activeInHierarchy) return;
        if (_showingModal) return;

        var es = EventSystem.current;
        if (es == null) return;

        if (es.currentSelectedGameObject == null)
        {
            ForceSelectNow();
        }
        else
        {
            TryScrollSelectedIntoView(es.currentSelectedGameObject);
        }
    }

    public void BindConfirmDialogTarget()
    {
        if (confirmDialog == null) return;
        if (confirmDialog.loadMenuUI == null)
            confirmDialog.loadMenuUI = this;
    }

    public void OpenLoadOnly() => Open();

    public void Open()
    {
        BindConfirmDialogTarget();

        if (root) root.SetActive(true);
        if (titleText) titleText.text = "Load Game";

        Time.timeScale = 0f;

        _showingModal = false;
        _pendingSlotToLoad = -1;

        BuildRows();
        SetSlotsInteractable(true);

        WireSlotNavigationAndFocus();
        ForceSelectNow();
        StartSelectNextFrameSafe();
        StartRepairRoutineSafe();
    }

    public void Close()
    {
        StopSelectRoutineSafe();
        StopShowModalRoutineSafe();
        StopRepairRoutineSafe();
        StopSelectRetryRoutineSafe();
        StopScrollRoutineSafe();

        _showingModal = false;
        _pendingSlotToLoad = -1;

        if (confirmDialog) confirmDialog.HideImmediate();
        if (root) root.SetActive(false);

        Time.timeScale = 1f;

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void BuildRows()
    {
        foreach (var r in rows) if (r) Destroy(r.gameObject);
        rows.Clear();

        if (!slotPrefab || !slotsParent) return;

        for (int slot = 1; slot <= maxSlots; slot++)
        {
            SaveData data = SaveSystem.Load(slot);

            var row = Instantiate(slotPrefab, slotsParent);
            rows.Add(row);

            bool clickable = (data != null);
            row.Bind(slot, data, clickable);

            int captured = slot;
            row.SetOnClick(() => OnSlotClicked(captured));
        }

        Canvas.ForceUpdateCanvases();
        WireSlotNavigationAndFocus();
    }

    private void OnSlotClicked(int slot)
    {
        if (_showingModal) return;

        SaveData data = SaveSystem.Load(slot);
        if (data == null) return;

        ShowConfirmLoadModal(slot);
    }

    private void ShowConfirmLoadModal(int slot)
    {
        _showingModal = true;
        _pendingSlotToLoad = slot;

        SetSlotsInteractable(false);

        if (openModalNextFrame)
        {
            StopShowModalRoutineSafe();
            _showModalRoutine = StartCoroutine(ShowConfirmLoadModalRoutine(slot));
        }
        else
        {
            ShowConfirmNow(slot, 0);
        }
    }

    private IEnumerator ShowConfirmLoadModalRoutine(int slot)
    {
        yield return null;
        Canvas.ForceUpdateCanvases();

        for (int attempt = 0; attempt <= modalShowRetries; attempt++)
        {
            ShowConfirmNow(slot, attempt);

            yield return null;

            if (IsConfirmShowing())
            {
                _showModalRoutine = null;
                yield break;
            }
        }

        Debug.LogWarning("[LoadMenuUI] Confirm dialog did not appear (first open timing). Re-enabling slots.");
        _showingModal = false;
        _pendingSlotToLoad = -1;

        SetSlotsInteractable(true);
        WireSlotNavigationAndFocus();
        ForceSelectNow();
        StartSelectNextFrameSafe();

        _showModalRoutine = null;
    }

    private void ShowConfirmNow(int slot, int attempt)
    {
        if (confirmDialog == null)
        {
            Debug.LogWarning("[LoadMenuUI] confirmDialog missing. Loading directly.");
            _showingModal = false;
            DoLoad(slot);
            return;
        }

        BringDialogToFront(confirmDialog);

        confirmDialog.ShowConfirmCancel(
            "Confirm",
            $"Are you sure you want to load Slot {slot}?",
            "Yes",
            "No",
            onYes: () => ShowLoadedModal(slot),
            onNo: () => HideModalImmediate()
        );

        if (attempt > 0)
            Debug.Log($"[LoadMenuUI] Retried showing confirm dialog (attempt {attempt}).");
    }

    private void ShowLoadedModal(int slot)
    {
        _showingModal = true;

        if (confirmDialog)
        {
            BringDialogToFront(confirmDialog);

            confirmDialog.ShowOkOnly(
                "Loaded!",
                $"SaveSlot{slot} loaded!",
                "OK",
                () =>
                {
                    HideModalImmediate();
                    DoLoad(slot);
                }
            );
        }
        else
        {
            HideModalImmediate();
            DoLoad(slot);
        }
    }

    private void HideModalImmediate()
    {
        _showingModal = false;
        _pendingSlotToLoad = -1;

        if (confirmDialog) confirmDialog.HideImmediate();

        SetSlotsInteractable(true);
        WireSlotNavigationAndFocus();
        ForceSelectNow();
        StartSelectNextFrameSafe();
    }

    private void SetSlotsInteractable(bool on)
    {
        if (slotsRoot != null)
            slotsRoot.SetActive(true);

        if (slotsCanvasGroup)
        {
            slotsCanvasGroup.interactable = on;
            slotsCanvasGroup.blocksRaycasts = on;
        }
    }

    private void DoLoad(int slot)
    {
        SaveSystem.CommitOverrideAsActiveProfile();
        SaveSystem.CachePendingLoadSelection(slot);

        Close();

        if (SaveGameManager.Instance != null && SaveGameManager.Instance.useQueuedLoadGate)
            SaveGameManager.Instance.QueueLoadFromSlot(slot);
        else
            SaveGameManager.Instance?.LoadFromSlot(slot);
    }

    private bool IsConfirmShowing()
    {
        if (confirmDialog == null) return false;

        if (confirmDialog.root != null)
            return confirmDialog.root.activeInHierarchy;

        return confirmDialog.gameObject.activeInHierarchy;
    }

    private void BringDialogToFront(ConfirmDialogUI dlg)
    {
        if (dlg == null) return;

        var t = (dlg.root != null) ? dlg.root.transform : dlg.transform;
        t.SetAsLastSibling();
    }

    private void StopShowModalRoutineSafe()
    {
        if (_showModalRoutine != null)
        {
            StopCoroutine(_showModalRoutine);
            _showModalRoutine = null;
        }
    }

    private void WireSlotNavigationAndFocus()
    {
        List<Button> slotButtons = new List<Button>();

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i] == null) continue;

            Button b = rows[i].GetButton();
            if (b == null) continue;
            if (!b.gameObject.activeInHierarchy) continue;
            if (!b.interactable) continue;

            slotButtons.Add(b);
        }

        for (int i = 0; i < slotButtons.Count; i++)
        {
            Button current = slotButtons[i];
            Button up = (i > 0) ? slotButtons[i - 1] : null;
            Button down = (i < slotButtons.Count - 1) ? slotButtons[i + 1] : closeButton;

            Navigation nav = current.navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnUp = up;
            nav.selectOnDown = down;
            nav.selectOnLeft = null;
            nav.selectOnRight = null;
            current.navigation = nav;
        }

        if (closeButton != null)
        {
            Navigation closeNav = closeButton.navigation;
            closeNav.mode = Navigation.Mode.Explicit;
            closeNav.selectOnUp = slotButtons.Count > 0 ? slotButtons[slotButtons.Count - 1] : null;
            closeNav.selectOnDown = null;
            closeNav.selectOnLeft = null;
            closeNav.selectOnRight = null;
            closeButton.navigation = closeNav;
        }

        if (root != null && root.activeInHierarchy)
        {
            var es = EventSystem.current;
            if (es != null && !_showingModal)
            {
                GameObject target = null;

                if (firstSelectedOnOpen != null && firstSelectedOnOpen.activeInHierarchy)
                {
                    Selectable forcedSel = firstSelectedOnOpen.GetComponent<Selectable>();
                    if (forcedSel == null || forcedSel.IsInteractable())
                        target = firstSelectedOnOpen;
                }

                if (target == null && slotButtons.Count > 0)
                    target = slotButtons[0].gameObject;

                if (target == null && closeButton != null && closeButton.gameObject.activeInHierarchy && closeButton.interactable)
                    target = closeButton.gameObject;

                if (target != null)
                {
                    es.SetSelectedGameObject(null);
                    es.SetSelectedGameObject(target);
                    TryScrollSelectedIntoView(target);
                }
            }
        }
    }

    private void ForceSelectNow()
    {
        Canvas.ForceUpdateCanvases();

        var es = EventSystem.current;
        if (es == null) return;
        if (_showingModal) return;

        WireSlotNavigationAndFocus();

        GameObject target = PickBestSelectable();
        if (target == null) return;

        es.SetSelectedGameObject(null);
        es.SetSelectedGameObject(target);

        TryScrollSelectedIntoView(target);
        StartSelectRetryRoutine(target);
    }

    private GameObject PickBestSelectable()
    {
        if (firstSelectedOnOpen != null &&
            firstSelectedOnOpen.activeInHierarchy)
        {
            Selectable forcedSel = firstSelectedOnOpen.GetComponent<Selectable>();
            if (forcedSel == null || forcedSel.IsInteractable())
                return firstSelectedOnOpen;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i] == null) continue;

            Button b = rows[i].GetButton();
            if (b == null) continue;
            if (!b.gameObject.activeInHierarchy) continue;
            if (!b.interactable) continue;

            return b.gameObject;
        }

        if (closeButton &&
            closeButton.gameObject.activeInHierarchy &&
            closeButton.interactable)
        {
            return closeButton.gameObject;
        }

        return null;
    }

    private void StartSelectNextFrameSafe()
    {
        StopSelectRoutineSafe();
        if (!isActiveAndEnabled) return;
        _selectRoutine = StartCoroutine(SelectNextFrameRoutine());
    }

    private void StopSelectRoutineSafe()
    {
        if (_selectRoutine == null) return;
        StopCoroutine(_selectRoutine);
        _selectRoutine = null;
    }

    private IEnumerator SelectNextFrameRoutine()
    {
        yield return null;
        ForceSelectNow();
        _selectRoutine = null;
    }

    private void StartSelectRetryRoutine(GameObject target)
    {
        StopSelectRetryRoutineSafe();

        if (!isActiveAndEnabled) return;
        _selectRetryRoutine = StartCoroutine(SelectRetryRoutine(target));
    }

    private void StopSelectRetryRoutineSafe()
    {
        if (_selectRetryRoutine != null)
        {
            StopCoroutine(_selectRetryRoutine);
            _selectRetryRoutine = null;
        }
    }

    private IEnumerator SelectRetryRoutine(GameObject target)
    {
        if (target == null)
        {
            _selectRetryRoutine = null;
            yield break;
        }

        var es = EventSystem.current;
        if (es == null)
        {
            _selectRetryRoutine = null;
            yield break;
        }

        for (int i = 0; i < selectRetryFrames; i++)
        {
            if (!isActiveAndEnabled) break;
            if (_showingModal) break;

            if (es.currentSelectedGameObject != target)
            {
                if (target.activeInHierarchy)
                {
                    Selectable sel = target.GetComponent<Selectable>();
                    if (sel == null || sel.IsInteractable())
                    {
                        es.SetSelectedGameObject(null);
                        es.SetSelectedGameObject(target);
                    }
                }
            }

            TryScrollSelectedIntoView(target);
            yield return null;
        }

        _selectRetryRoutine = null;
    }

    // =========================================================
    // ✅ Robust auto-scroll selected slot into view
    // =========================================================
    private void TryScrollSelectedIntoView(GameObject selected)
    {
        if (!autoScrollToSelected) return;
        if (selected == null) return;
        if (_showingModal) return;
        if (slotsScrollRect == null) return;
        if (slotsParent == null) return;

        // Don't scroll for the close button
        if (closeButton != null && selected == closeButton.gameObject) return;

        RectTransform selectedRect = selected.GetComponent<RectTransform>();
        RectTransform contentRect = slotsParent as RectTransform;
        RectTransform viewportRect = slotsViewport != null ? slotsViewport : slotsScrollRect.viewport;

        if (selectedRect == null || contentRect == null || viewportRect == null) return;

        StopScrollRoutineSafe();
        _scrollRoutine = StartCoroutine(ScrollSelectedIntoViewRoutine(selectedRect, contentRect, viewportRect));
    }

    private void StopScrollRoutineSafe()
    {
        if (_scrollRoutine != null)
        {
            StopCoroutine(_scrollRoutine);
            _scrollRoutine = null;
        }
    }

    private IEnumerator ScrollSelectedIntoViewRoutine(RectTransform selectedRect, RectTransform contentRect, RectTransform viewportRect)
    {
        yield return null;
        Canvas.ForceUpdateCanvases();

        // Convert selected center into viewport local space
        Vector3 selectedWorldCenter = selectedRect.TransformPoint(selectedRect.rect.center);
        Vector3 selectedWorldTop = selectedRect.TransformPoint(new Vector3(0f, selectedRect.rect.yMax, 0f));
        Vector3 selectedWorldBottom = selectedRect.TransformPoint(new Vector3(0f, selectedRect.rect.yMin, 0f));

        Vector3 selectedLocalCenter = viewportRect.InverseTransformPoint(selectedWorldCenter);
        Vector3 selectedLocalTop = viewportRect.InverseTransformPoint(selectedWorldTop);
        Vector3 selectedLocalBottom = viewportRect.InverseTransformPoint(selectedWorldBottom);

        float viewportTop = viewportRect.rect.yMax - scrollPadding;
        float viewportBottom = viewportRect.rect.yMin + scrollPadding;

        float delta = 0f;

        if (selectedLocalTop.y > viewportTop)
        {
            delta = selectedLocalTop.y - viewportTop;
        }
        else if (selectedLocalBottom.y < viewportBottom)
        {
            delta = selectedLocalBottom.y - viewportBottom;
        }

        if (Mathf.Abs(delta) > 0.01f)
        {
            Vector2 anchored = contentRect.anchoredPosition;
            anchored.y -= delta;

            float maxY = Mathf.Max(0f, contentRect.rect.height - viewportRect.rect.height);
            anchored.y = Mathf.Clamp(anchored.y, 0f, maxY);

            contentRect.anchoredPosition = anchored;
            Canvas.ForceUpdateCanvases();
        }

        _scrollRoutine = null;
    }

    private void StartRepairRoutineSafe()
    {
        if (!repairSelectionWhileOpen) return;
        StopRepairRoutineSafe();
        if (!isActiveAndEnabled) return;
        _repairRoutine = StartCoroutine(RepairSelectionRoutine());
    }

    private void StopRepairRoutineSafe()
    {
        if (_repairRoutine != null)
        {
            StopCoroutine(_repairRoutine);
            _repairRoutine = null;
        }
    }

    private IEnumerator RepairSelectionRoutine()
    {
        var wait = new WaitForSecondsRealtime(repairCheckInterval);

        while (isActiveAndEnabled && root != null && root.activeInHierarchy)
        {
            if (!_showingModal)
            {
                var es = EventSystem.current;
                if (es != null)
                {
                    if (es.currentSelectedGameObject == null)
                    {
                        ForceSelectNow();
                    }
                    else
                    {
                        TryScrollSelectedIntoView(es.currentSelectedGameObject);
                    }
                }
            }

            yield return wait;
        }

        _repairRoutine = null;
    }
}