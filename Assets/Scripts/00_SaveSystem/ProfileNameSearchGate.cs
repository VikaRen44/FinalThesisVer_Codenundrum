using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class ProfileNameSearchGate : MonoBehaviour
{
    [Header("UI Roots")]
    public GameObject nameTypeRoot;

    [Header("Refs")]
    public NameInput nameInput;
    public ConfirmDialogUI confirmDialog;

    [Header("Load Target (drag LoadMenuUI OR SaveLoadMenuUI here)")]
    [Tooltip("You may accidentally assign CanvasScaler/RectTransform etc. This script will auto-resolve the correct component implementing IProfileLoadTarget on the same GameObject.")]
    public MonoBehaviour loadTargetBehaviour;

    [Header("Text")]
    public string noNamesTitle = "No Names Found";
    public string noNamesBody = "No saves exist for that name.";
    public string foundTitle = "Name Found";
    public string foundBodyFormat = "Found saves for \"{0}\".";
    public string okLabel = "OK";

    [Header("Desktop Fix (Click/Submit Carryover)")]
    [Tooltip("Wait until mouse-left is released before opening slots. This prevents the OK-click from being 'consumed' by the first slot UI.")]
    public bool waitForMouseReleaseBeforeOpeningSlots = true;

    [Tooltip("Extra: wait until keyboard/gamepad Submit is released too (prevents Enter/A being carried into the next menu).")]
    public bool waitForSubmitReleaseBeforeOpeningSlots = true;

    [Tooltip("Safety delay frames before opening slots (after releases). 1 is usually enough.")]
    [Range(0, 3)] public int framesToDelayBeforeOpeningSlots = 1;

    public bool clearSelectionBeforeOpeningSlots = true;
    public bool forceSelectFirstInteractableOnOpen = true;

    [Header("Debug")]
    public bool debugLogs = false;

    private Coroutine _proceedCo;

    public void OpenSearch()
    {
        if (nameTypeRoot) nameTypeRoot.SetActive(true);
    }

    public void BackClose()
    {
        // Leaving search should NOT change gameplay profile
        SaveSystem.ClearProfileOverride();

        if (nameTypeRoot) nameTypeRoot.SetActive(false);

        if (confirmDialog) confirmDialog.HideImmediate();

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    public void SearchProfile()
    {
        if (nameInput != null)
            nameInput.CommitForValidationOnly();

        string typed = (nameInput != null) ? nameInput.CurrentName : "";
        typed = (typed ?? "").Trim();

        if (string.IsNullOrWhiteSpace(typed))
        {
            ShowNoNamesFound();
            return;
        }

        bool exists;
        try { exists = SaveSystem.DoesProfileExist(typed); }
        catch { exists = false; }

        if (!exists)
        {
            ShowNoNamesFound();
            return;
        }

        // Use override so Load slots can browse this profile safely.
        SaveSystem.SetProfileOverride(typed);

        if (debugLogs)
            Debug.Log($"[ProfileNameSearchGate] Profile found. Override='{SaveSystem.GetEffectiveProfile()}' Active='{SaveSystem.GetActiveProfile()}'");

        string body = string.Format(foundBodyFormat, typed);

        if (confirmDialog)
        {
            // IMPORTANT: confirmDialog will HideImmediate before calling callback.
            confirmDialog.ShowOkOnly(foundTitle, body, okLabel, ProceedToSlots);
        }
        else
        {
            ProceedToSlots();
        }
    }

    private void ShowNoNamesFound()
    {
        if (confirmDialog)
            confirmDialog.ShowOkOnly(noNamesTitle, noNamesBody, okLabel, null);
    }

    private void ProceedToSlots()
    {
        if (_proceedCo != null)
        {
            StopCoroutine(_proceedCo);
            _proceedCo = null;
        }

        _proceedCo = StartCoroutine(ProceedToSlotsRoutine());
    }

    private IEnumerator ProceedToSlotsRoutine()
    {
        // Ensure search root is hidden
        if (nameTypeRoot) nameTypeRoot.SetActive(false);

        // HARD safety: ensure dialog isn't leaving raycast blockers around
        if (confirmDialog) confirmDialog.HideImmediate();

        // Clear selection now
        if (clearSelectionBeforeOpeningSlots && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        // ✅ KEY FIX: wait for the click/submit that closed the dialog to be RELEASED
        if (waitForMouseReleaseBeforeOpeningSlots)
        {
#if ENABLE_INPUT_SYSTEM
            // New Input System
            var mouse = Mouse.current;
            if (mouse != null)
            {
                while (mouse.leftButton.isPressed)
                    yield return null;
            }
#else
            // Old Input Manager
            while (Input.GetMouseButton(0))
                yield return null;
#endif
        }

        if (waitForSubmitReleaseBeforeOpeningSlots)
        {
#if ENABLE_INPUT_SYSTEM
            // If you have keyboard/gamepad, releasing Enter/A helps.
            // (We can't reliably know your exact action map here, so we check common keys/buttons.)
            var kb = Keyboard.current;
            if (kb != null)
            {
                while (kb.enterKey.isPressed || kb.numpadEnterKey.isPressed || kb.spaceKey.isPressed)
                    yield return null;
            }

            var gp = Gamepad.current;
            if (gp != null)
            {
                while (gp.buttonSouth.isPressed) // A on Xbox, X on PS layout
                    yield return null;
            }
#endif
        }

        // Delay a couple frames AFTER releases (helps EventSystem settle + layout rebuild)
        int frames = Mathf.Clamp(framesToDelayBeforeOpeningSlots, 0, 3);
        for (int i = 0; i < frames; i++)
            yield return null;

        // One end-of-frame after UI transitions
        yield return new WaitForEndOfFrame();

        var target = ResolveProfileLoadTarget(loadTargetBehaviour);
        if (target == null)
        {
            Debug.LogError("[ProfileNameSearchGate] loadTargetBehaviour is missing or does not implement IProfileLoadTarget.");
            _proceedCo = null;
            yield break;
        }

        // Open slots UI
        target.OpenLoadOnly();

        // Optional: select first interactable next frame for keyboard/gamepad stability
        if (forceSelectFirstInteractableOnOpen)
        {
            yield return null;

            var es = EventSystem.current;
            if (es != null)
            {
                var first = FindFirstInteractableSelectable(loadTargetBehaviour);
                if (first != null && first.gameObject.activeInHierarchy)
                {
                    es.SetSelectedGameObject(null);
                    es.SetSelectedGameObject(first.gameObject);
                }
            }
        }

        _proceedCo = null;
    }

    private Selectable FindFirstInteractableSelectable(MonoBehaviour mb)
    {
        if (mb == null) return null;

        var rootGO = mb.gameObject;

        var buttons = rootGO.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null && buttons[i].isActiveAndEnabled && buttons[i].interactable)
                return buttons[i];
        }

        var sels = rootGO.GetComponentsInChildren<Selectable>(true);
        for (int i = 0; i < sels.Length; i++)
        {
            if (sels[i] != null && sels[i].isActiveAndEnabled && sels[i].interactable)
                return sels[i];
        }

        return null;
    }

    private IProfileLoadTarget ResolveProfileLoadTarget(MonoBehaviour mb)
    {
        if (mb == null) return null;

        if (mb is IProfileLoadTarget direct)
            return direct;

        var components = mb.GetComponents<MonoBehaviour>();
        for (int i = 0; i < components.Length; i++)
            if (components[i] is IProfileLoadTarget t) return t;

        var parentTargets = mb.GetComponentsInParent<MonoBehaviour>(true);
        for (int i = 0; i < parentTargets.Length; i++)
            if (parentTargets[i] is IProfileLoadTarget t) return t;

        var childTargets = mb.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < childTargets.Length; i++)
            if (childTargets[i] is IProfileLoadTarget t) return t;

        return null;
    }
}