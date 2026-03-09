using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class SaveLoadMenuUI : MonoBehaviour, IProfileLoadTarget
{
    public enum Mode { None, Save, Load }

    [Header("Root")]
    public GameObject root;

    [Header("Titles")]
    public TMP_Text mainTitleText;
    public TMP_Text chooserTitleText;
    public string chooseActionTitle = "Please choose an action";

    [Header("Slots UI")]
    public GameObject slotsRoot;
    public Transform slotsParent;
    public SaveSlotRowUI slotPrefab;

    [Tooltip("Assign CanvasGroup on ScrollView/Viewport/Content (recommended).")]
    public CanvasGroup slotsCanvasGroup;

    [Header("Action Chooser UI")]
    public GameObject actionChooserRoot;

    [Header("Buttons")]
    public Button saveButton;
    public Button loadButton;
    public Button closeButtonOverlay;
    public Button closeButtonMain;

    [Header("Confirm Popup")]
    public ConfirmDialogUI confirmDialog;

    [Header("Settings")]
    public int maxSlots = 5;

    [Header("Save Success Text")]
    public string savedSuccessfullyBody = "Saved successfully!";
    public string overwriteSuccessfullyBody = "Overwrite successful!";

    [Header("Profile Gate (Load Name Search)")]
    public ProfileNameSearchGate profileGate;

    [Header("Modal Robustness")]
    public bool openModalNextFrame = true;
    [Range(0, 2)] public int modalShowRetries = 1;

    [Header("Controller Selection")]
    [Tooltip("Optional first selected object when chooser opens. If null, Save button is preferred.")]
    public GameObject firstSelectedOnOpen;

    [Tooltip("Optional selection to restore after closing this menu.")]
    public GameObject returnSelectedOnClose;

    [Tooltip("If true, restores controller selection if it gets lost while this menu is open.")]
    public bool keepSelectionAlive = true;

    private readonly List<SaveSlotRowUI> rows = new();
    private Mode mode = Mode.None;

    private bool _modalBusy;
    private bool _pendingWasOverwrite;

    private Coroutine _showModalRoutine;
    private Coroutine _focusRoutine;

    private void Awake()
    {
        RewireButtons();

        if (root) root.SetActive(false);
        if (confirmDialog) confirmDialog.HideImmediate();

        ShowChooser(true);
        SetSlotsEnabled(false);
    }

    private void OnEnable()
    {
        RewireButtons();
    }

    private void OnDisable()
    {
        StopShowModalRoutineSafe();
        StopFocusRoutine();
    }

    private void Update()
    {
        if (!keepSelectionAlive) return;
        if (root == null || !root.activeInHierarchy) return;

        var es = EventSystem.current;
        if (es == null) return;

        if (IsConfirmShowing()) return;

        if (es.currentSelectedGameObject == null)
        {
            ForceSelectFirst();
        }
    }

    private void RewireButtons()
    {
        if (saveButton)
        {
            saveButton.onClick.RemoveAllListeners();
            saveButton.onClick.AddListener(() => SetMode(Mode.Save));
        }

        if (loadButton)
        {
            loadButton.onClick.RemoveAllListeners();
            loadButton.onClick.AddListener(() =>
            {
                if (profileGate != null) profileGate.OpenSearch();
                else SetMode(Mode.Load);
            });
        }

        if (closeButtonOverlay)
        {
            closeButtonOverlay.onClick.RemoveAllListeners();
            closeButtonOverlay.onClick.AddListener(CloseMenu);
        }

        if (closeButtonMain)
        {
            closeButtonMain.onClick.RemoveAllListeners();
            closeButtonMain.onClick.AddListener(CloseMenu);
        }
    }

    public void OpenMenu()
    {
        SaveSystem.ClearProfileOverride();

        mode = Mode.None;
        _modalBusy = false;

        if (root) root.SetActive(true);

        ShowChooser(true);
        SetSlotsEnabled(false);

        if (chooserTitleText)
        {
            chooserTitleText.text = chooseActionTitle;
            chooserTitleText.gameObject.SetActive(true);
        }

        if (mainTitleText) mainTitleText.gameObject.SetActive(false);

        if (saveButton) saveButton.gameObject.SetActive(true);
        if (loadButton) loadButton.gameObject.SetActive(true);

        Time.timeScale = 0f;

        BuildRows();
        Canvas.ForceUpdateCanvases();
        WireCurrentNavigation();
        StartFocusOnOpen();
    }

    public void CloseMenu()
    {
        _modalBusy = false;

        StopShowModalRoutineSafe();
        StopFocusRoutine();

        if (confirmDialog) confirmDialog.HideImmediate();
        if (root) root.SetActive(false);

        if (Time.timeScale == 0f) Time.timeScale = 1f;

        mode = Mode.None;
        SaveSystem.GetSelectedCharacterIndexForSave = null;

        SaveSystem.ClearProfileOverride();

        var es = EventSystem.current;
        if (es != null)
        {
            GameObject target = returnSelectedOnClose;
            if (target != null && target.activeInHierarchy)
            {
                es.SetSelectedGameObject(null);
                es.SetSelectedGameObject(target);
            }
            else
            {
                es.SetSelectedGameObject(null);
            }
        }
    }

    public void SetMode(Mode m)
    {
        mode = m;
        _modalBusy = false;

        if (mode == Mode.Save)
            SaveSystem.ClearProfileOverride();

        ShowChooser(false);
        SetSlotsEnabled(true);

        if (chooserTitleText) chooserTitleText.gameObject.SetActive(false);

        if (mainTitleText)
        {
            mainTitleText.gameObject.SetActive(true);
            mainTitleText.text = (mode == Mode.Save) ? "Save Game" : (mode == Mode.Load) ? "Load Game" : "";
        }

        BuildRows();
        Canvas.ForceUpdateCanvases();
        WireCurrentNavigation();
        StartFocusOnOpen();
    }

    private void ShowChooser(bool show)
    {
        if (actionChooserRoot) actionChooserRoot.SetActive(show);
    }

    private void SetSlotsEnabled(bool enabled)
    {
        if (slotsRoot) slotsRoot.SetActive(true);

        if (slotsCanvasGroup)
        {
            slotsCanvasGroup.interactable = enabled;
            slotsCanvasGroup.blocksRaycasts = enabled;
        }
    }

    private void DisableSlots()
    {
        if (slotsCanvasGroup)
        {
            slotsCanvasGroup.interactable = false;
            slotsCanvasGroup.blocksRaycasts = false;
        }
    }

    private void EnableSlots()
    {
        if (slotsCanvasGroup)
        {
            slotsCanvasGroup.interactable = true;
            slotsCanvasGroup.blocksRaycasts = true;
        }
    }

    private void BuildRows()
    {
        foreach (var r in rows) if (r) Destroy(r.gameObject);
        rows.Clear();

        if (!slotPrefab || !slotsParent) return;

        for (int slot = 1; slot <= maxSlots; slot++)
        {
            SaveData data = SaveSystem.Load(slot);
            bool hasSave = data != null;

            bool clickable = (mode == Mode.Save) || (mode == Mode.Load && hasSave);
            if (mode == Mode.None) clickable = false;

            var row = Instantiate(slotPrefab, slotsParent);
            rows.Add(row);

            row.Bind(slot, data, clickable);

            int captured = slot;
            row.SetOnClick(() => OnSlotClicked(captured));
        }

        Canvas.ForceUpdateCanvases();
    }

    private void OnSlotClicked(int slot)
    {
        if (mode == Mode.None) return;
        if (_modalBusy) return;

        if (confirmDialog) confirmDialog.HideImmediate();
        EnableSlots();

        SaveData data = SaveSystem.Load(slot);
        bool hasSave = data != null;

        if (mode == Mode.Save) HandleSave(slot, hasSave);
        else HandleLoad(slot, hasSave);
    }

    private void HandleSave(int slot, bool hasSave)
    {
        SaveSystem.ClearProfileOverride();

        if (!hasSave)
        {
            _pendingWasOverwrite = false;
            DoSave(slot);
            return;
        }

        _modalBusy = true;
        DisableSlots();

        ShowModalRobust(
            () =>
            {
                confirmDialog.ShowConfirmCancel(
                    "Overwrite Save?",
                    $"Overwrite Slot {slot}?",
                    "Overwrite",
                    "Cancel",
                    () =>
                    {
                        _pendingWasOverwrite = true;
                        _modalBusy = false;
                        EnableSlots();
                        DoSave(slot);
                    },
                    () =>
                    {
                        _modalBusy = false;
                        EnableSlots();
                        WireCurrentNavigation();
                        StartFocusOnOpen();
                    }
                );
            },
            onFail: () =>
            {
                _modalBusy = false;
                EnableSlots();
                WireCurrentNavigation();
                StartFocusOnOpen();
            }
        );
    }

    private void DoSave(int slot)
    {
        if (!SaveGameManager.Instance) return;
        StartCoroutine(CaptureAndSave(slot));
    }

    private IEnumerator CaptureAndSave(int slot)
    {
        yield return new WaitForEndOfFrame();

        Texture2D thumb = new Texture2D(256, 144, TextureFormat.RGB24, false);
        thumb.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        thumb.Apply();

        SaveGameManager.Instance.SaveToSlot(slot, thumb);

        Destroy(thumb);

        BuildRows();

        _modalBusy = true;
        DisableSlots();

        string body = _pendingWasOverwrite ? overwriteSuccessfullyBody : savedSuccessfullyBody;

        ShowModalRobust(
            () =>
            {
                confirmDialog.ShowOkOnly(
                    "Saved!",
                    string.IsNullOrWhiteSpace(body) ? "Saved successfully!" : body,
                    "OK",
                    () =>
                    {
                        _modalBusy = false;
                        EnableSlots();
                        WireCurrentNavigation();
                        StartFocusOnOpen();
                    }
                );
            },
            onFail: () =>
            {
                _modalBusy = false;
                EnableSlots();
                WireCurrentNavigation();
                StartFocusOnOpen();
            }
        );
    }

    private void HandleLoad(int slot, bool hasSave)
    {
        if (!hasSave) return;

        _modalBusy = true;
        DisableSlots();

        ShowModalRobust(
            () =>
            {
                confirmDialog.ShowConfirmCancel(
                    "Load Game?",
                    $"Load Slot {slot}?",
                    "Load",
                    "Cancel",
                    () =>
                    {
                        _modalBusy = false;
                        EnableSlots();
                        DoLoad(slot);
                    },
                    () =>
                    {
                        _modalBusy = false;
                        EnableSlots();
                        WireCurrentNavigation();
                        StartFocusOnOpen();
                    }
                );
            },
            onFail: () =>
            {
                _modalBusy = false;
                EnableSlots();
                WireCurrentNavigation();
                StartFocusOnOpen();
            }
        );
    }

    private void DoLoad(int slot)
    {
        SaveSystem.CommitOverrideAsActiveProfile();
        SaveSystem.CachePendingLoadSelection(slot);

        CloseMenu();

        if (SaveGameManager.Instance != null && SaveGameManager.Instance.useQueuedLoadGate)
            SaveGameManager.Instance.QueueLoadFromSlot(slot);
        else
            SaveGameManager.Instance?.LoadFromSlot(slot);
    }

    public void OpenLoadOnly()
    {
        mode = Mode.Load;
        _modalBusy = false;

        if (root) root.SetActive(true);

        ShowChooser(false);
        SetSlotsEnabled(true);

        if (chooserTitleText) chooserTitleText.gameObject.SetActive(false);

        if (mainTitleText)
        {
            mainTitleText.gameObject.SetActive(true);
            mainTitleText.text = "Load Game";
        }

        if (saveButton) saveButton.gameObject.SetActive(false);
        if (loadButton) loadButton.gameObject.SetActive(false);

        Time.timeScale = 0f;
        BuildRows();
        Canvas.ForceUpdateCanvases();
        WireCurrentNavigation();
        StartFocusOnOpen();
    }

    private void ShowModalRobust(Action showAction, Action onFail)
    {
        if (confirmDialog == null)
        {
            showAction?.Invoke();
            return;
        }

        StopShowModalRoutineSafe();
        _showModalRoutine = StartCoroutine(ShowModalRoutine(showAction, onFail));
    }

    private IEnumerator ShowModalRoutine(Action showAction, Action onFail)
    {
        if (openModalNextFrame)
            yield return null;

        Canvas.ForceUpdateCanvases();

        for (int attempt = 0; attempt <= modalShowRetries; attempt++)
        {
            BringDialogToFront(confirmDialog);

            showAction?.Invoke();

            yield return null;

            if (IsConfirmShowing())
            {
                _showModalRoutine = null;
                yield break;
            }
        }

        Debug.LogWarning("[SaveLoadMenuUI] Modal did not appear. Reverting slot interaction to avoid soft-lock.");
        onFail?.Invoke();

        _showModalRoutine = null;
    }

    private bool IsConfirmShowing()
    {
        if (confirmDialog == null) return false;
        if (confirmDialog.root != null) return confirmDialog.root.activeInHierarchy;
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

    private void StartFocusOnOpen()
    {
        StopFocusRoutine();
        _focusRoutine = StartCoroutine(FocusNextFrame());
    }

    private IEnumerator FocusNextFrame()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        WireCurrentNavigation();
        ForceSelectFirst();
        _focusRoutine = null;
    }

    private void StopFocusRoutine()
    {
        if (_focusRoutine != null)
        {
            StopCoroutine(_focusRoutine);
            _focusRoutine = null;
        }
    }

    private void WireCurrentNavigation()
    {
        if (mode == Mode.None)
            WireChooserNavigation();
        else
            WireSlotNavigation();
    }

    private void WireChooserNavigation()
    {
        if (saveButton != null)
        {
            Navigation nav = saveButton.navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnUp = null;
            nav.selectOnDown = closeButtonMain;
            nav.selectOnLeft = null;
            nav.selectOnRight = loadButton;
            saveButton.navigation = nav;
        }

        if (loadButton != null)
        {
            Navigation nav = loadButton.navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnUp = null;
            nav.selectOnDown = closeButtonMain;
            nav.selectOnLeft = saveButton;
            nav.selectOnRight = null;
            loadButton.navigation = nav;
        }

        if (closeButtonMain != null)
        {
            Navigation nav = closeButtonMain.navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnUp = saveButton != null && saveButton.gameObject.activeInHierarchy && saveButton.interactable ? saveButton : null;
            nav.selectOnDown = null;
            nav.selectOnLeft = null;
            nav.selectOnRight = null;
            closeButtonMain.navigation = nav;
        }

        if (closeButtonOverlay != null)
        {
            Navigation nav = closeButtonOverlay.navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnUp = saveButton != null && saveButton.gameObject.activeInHierarchy && saveButton.interactable ? saveButton : null;
            nav.selectOnDown = null;
            nav.selectOnLeft = null;
            nav.selectOnRight = null;
            closeButtonOverlay.navigation = nav;
        }
    }

    private void WireSlotNavigation()
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
            Button down = (i < slotButtons.Count - 1) ? slotButtons[i + 1] : closeButtonMain;

            Navigation nav = current.navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnUp = up;
            nav.selectOnDown = down;
            nav.selectOnLeft = null;
            nav.selectOnRight = null;
            current.navigation = nav;
        }

        if (closeButtonMain != null)
        {
            Navigation closeNav = closeButtonMain.navigation;
            closeNav.mode = Navigation.Mode.Explicit;
            closeNav.selectOnUp = slotButtons.Count > 0 ? slotButtons[slotButtons.Count - 1] : null;
            closeNav.selectOnDown = null;
            closeNav.selectOnLeft = null;
            closeNav.selectOnRight = null;
            closeButtonMain.navigation = closeNav;
        }

        if (closeButtonOverlay != null)
        {
            Navigation closeNav = closeButtonOverlay.navigation;
            closeNav.mode = Navigation.Mode.Explicit;
            closeNav.selectOnUp = slotButtons.Count > 0 ? slotButtons[slotButtons.Count - 1] : null;
            closeNav.selectOnDown = null;
            closeNav.selectOnLeft = null;
            closeNav.selectOnRight = null;
            closeButtonOverlay.navigation = closeNav;
        }
    }

    private void ForceSelectFirst()
    {
        var es = EventSystem.current;
        if (es == null) return;

        GameObject target = null;

        if (mode == Mode.None)
        {
            if (firstSelectedOnOpen != null && firstSelectedOnOpen.activeInHierarchy)
            {
                Selectable sel = firstSelectedOnOpen.GetComponent<Selectable>();
                if (sel == null || sel.IsInteractable())
                    target = firstSelectedOnOpen;
            }

            if (target == null && saveButton != null && saveButton.gameObject.activeInHierarchy && saveButton.interactable)
                target = saveButton.gameObject;

            if (target == null && loadButton != null && loadButton.gameObject.activeInHierarchy && loadButton.interactable)
                target = loadButton.gameObject;

            if (target == null && closeButtonMain != null && closeButtonMain.gameObject.activeInHierarchy && closeButtonMain.interactable)
                target = closeButtonMain.gameObject;
        }
        else
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] == null) continue;

                Button b = rows[i].GetButton();
                if (b == null) continue;
                if (!b.gameObject.activeInHierarchy) continue;
                if (!b.interactable) continue;

                target = b.gameObject;
                break;
            }

            if (target == null && closeButtonMain != null && closeButtonMain.gameObject.activeInHierarchy && closeButtonMain.interactable)
                target = closeButtonMain.gameObject;

            if (target == null && closeButtonOverlay != null && closeButtonOverlay.gameObject.activeInHierarchy && closeButtonOverlay.interactable)
                target = closeButtonOverlay.gameObject;
        }

        if (target == null) return;

        es.SetSelectedGameObject(null);
        es.SetSelectedGameObject(target);
    }
}