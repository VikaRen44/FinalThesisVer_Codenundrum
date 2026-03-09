using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

public class TutorialListUI : MonoBehaviour
{
    [Serializable]
    public class TutorialButton
    {
        [Header("UI")]
        public Button button;

        [Tooltip("Optional label to set on the button (TMP child). Leave empty to not touch text.")]
        public string buttonLabel = "";

        [Header("Tutorial Content (Drag & Drop)")]
        [Tooltip("Drag the TutorialSequenceSO you want to open for this button.")]
        public TutorialSequenceSO tutorial;

        [Header("Optional: Close list when opened")]
        public bool closeListWhenOpened = false;
    }

    // =========================
    // ROOT
    // =========================
    [Header("Root")]
    public GameObject root;

    [Header("Title")]
    public TMP_Text titleText;
    public string title = "TUTORIAL LIST";

    [Header("Buttons")]
    public Button closeButton;
    public GameObject firstSelectedOnOpen;

    [Header("Tutorial Buttons (manual drag & drop)")]
    public List<TutorialButton> tutorials = new List<TutorialButton>();

    // =========================
    // OPEN/CLOSE HOTKEY
    // =========================
    [Header("Hotkey (New Input System)")]
    [Tooltip("Optional. If assigned, this action toggles the list.")]
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

    // =========================
    // ✅ TUTORIAL UI TARGET (STRONG-TYPED)
    // =========================
    [Header("Tutorial UI Target (Drag TutorialUI Script Here)")]
    public TutorialUI tutorialUI;

    // =========================
    // OPTIONAL: PLAYER (if you want to pass it into TutorialUI.Show(seq, player))
    // =========================
    [Header("Optional Player (for lock)")]
    public string playerTag = "Player";

    // =========================
    // INTERNALS
    // =========================
    private bool _isOpen;
    private string _prevActionMap = "";
    private bool _cachedPrevActionMap = false;
    private bool _tabWasDown;

    private void Awake()
    {
        if (root) root.SetActive(false);
    }

    private void Start()
    {
        WireButtons();
        ApplyButtonLabels();

        // Optional auto-find if not assigned
        if (tutorialUI == null)
        {
#if UNITY_2023_1_OR_NEWER
            tutorialUI = UnityEngine.Object.FindFirstObjectByType<TutorialUI>(FindObjectsInactive.Include);
#else
            tutorialUI = UnityEngine.Object.FindObjectOfType<TutorialUI>(true);
#endif
        }
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

        if (_isOpen)
        {
            ForceCloseAndRestore();
        }
        else
        {
            RestoreTimeScaleIfWePaused();
            RestoreActionMapIfWeSwapped();
        }
    }

    private void Update()
    {
        if (toggleAction != null && toggleAction.action != null)
        {
            if (toggleAction.action.WasPressedThisFrame())
            {
                if (_isOpen) CloseMenu();
                else OpenMenu();
            }
            return;
        }

        if (!enableTabFallback) return;

        var kb = Keyboard.current;
        if (kb == null) return;

        bool down = kb.tabKey.isPressed;
        if (down && !_tabWasDown)
        {
            if (_isOpen) CloseMenu();
            else OpenMenu();
        }
        _tabWasDown = down;
    }

    private void ForceCloseAndRestore()
    {
        _isOpen = false;

        if (root) root.SetActive(false);

        RestoreTimeScaleIfWePaused();
        RestoreActionMapIfWeSwapped();

        if (EventSystem.current)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void WireButtons()
    {
        if (closeButton)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(CloseMenu);
        }

        for (int i = 0; i < tutorials.Count; i++)
        {
            int idx = i;
            var entry = tutorials[idx];
            if (entry.button == null) continue;

            entry.button.onClick.RemoveAllListeners();
            entry.button.onClick.AddListener(() => OpenTutorial(idx));
        }
    }

    private void ApplyButtonLabels()
    {
        for (int i = 0; i < tutorials.Count; i++)
        {
            var entry = tutorials[i];
            if (entry.button == null) continue;
            if (string.IsNullOrWhiteSpace(entry.buttonLabel)) continue;

            var tmp = entry.button.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
                tmp.text = entry.buttonLabel;
        }
    }

    // =========================
    // SAVE MENU FREEZE / RESTORE
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
    // OPTIONAL ACTION MAP SWAP
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
    // MENU OPEN/CLOSE
    // =========================
    public void OpenMenu()
    {
        if (!root) return;
        if (_isOpen) return;

        if (titleText) titleText.text = title;

        root.SetActive(true);
        _isOpen = true;

        PauseTimeScaleLikeSaveMenu();
        SwapToUIMap();

        StartCoroutine(SetSelectionNextFrame());
    }

    public void CloseMenu()
    {
        if (!root) return;
        if (!_isOpen) return;

        root.SetActive(false);
        _isOpen = false;

        RestoreTimeScaleIfWePaused();
        RestoreActionMapIfWeSwapped();

        if (EventSystem.current)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private IEnumerator SetSelectionNextFrame()
    {
        yield return null;

        if (EventSystem.current && firstSelectedOnOpen)
            EventSystem.current.SetSelectedGameObject(firstSelectedOnOpen);
    }

    // =========================
    // OPEN TUTORIAL
    // =========================
    private void OpenTutorial(int index)
    {
        if (index < 0 || index >= tutorials.Count) return;

        var entry = tutorials[index];
        if (entry == null || entry.tutorial == null)
        {
            Debug.LogWarning("[TutorialListUI] Tutorial entry missing tutorial SO.");
            return;
        }

        if (tutorialUI == null)
        {
            Debug.LogError("[TutorialListUI] tutorialUI is not assigned and could not be auto-found.");
            return;
        }

        // ✅ call your actual method name: Show(...)
        Transform player = null;
        var pgo = GameObject.FindGameObjectWithTag(playerTag);
        if (pgo != null) player = pgo.transform;

        tutorialUI.Show(entry.tutorial, player);

        if (entry.closeListWhenOpened)
            CloseMenu();
        else
            StartCoroutine(SetSelectionNextFrame());
    }
}