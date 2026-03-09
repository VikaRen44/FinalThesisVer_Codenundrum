using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Audio;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel; // ✅ still fine to keep (InputEventPtr type)
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using System.Collections;

public class SettingsUIController : MonoBehaviour
{
    public enum InputMode { Keyboard, Gamepad }

    [System.Serializable]
    public class GameSettingsData
    {
        [Range(0f, 1f)] public float masterVolume01 = 1f;
        public InputMode inputMode = InputMode.Keyboard;

        public GameSettingsData Copy()
        {
            return new GameSettingsData
            {
                masterVolume01 = masterVolume01,
                inputMode = inputMode
            };
        }

        public bool Equals(GameSettingsData other)
        {
            if (other == null) return false;
            return Mathf.Abs(masterVolume01 - other.masterVolume01) < 0.0001f
                   && inputMode == other.inputMode;
        }
    }

    [Header("UI Refs")]
    public GameObject settingsRoot;
    public Slider masterSlider;

    [Header("Input Toggles")]
    public Toggle gamepadToggle;
    public Toggle keyboardToggle;

    [Tooltip("Optional. If null, a ToggleGroup will be auto-created on this object.")]
    public ToggleGroup inputToggleGroup;

    [Header("Buttons")]
    public Button applyButton;
    public Button cancelButton;

    [Header("Return to Main Menu (optional)")]
    [Tooltip("Assign a button that returns to main menu. Leave null if not used in this scene.")]
    public Button returnToMenuButton;

    [Tooltip("Scene name to load when returning to menu (e.g., 01_MainMenu).")]
    public string mainMenuSceneName = "01_MainMenu";

    [Tooltip("If true, shows Return to Main Menu button (if assigned). Set FALSE in menu UI.")]
    public bool enableReturnToMainMenu = false;

    [Header("Modals")]
    public SimpleModal confirmModal;
    public SimpleModal infoModal;

    [Header("Audio")]
    public AudioMixer masterMixer;
    public string exposedParam = "MasterVolume";
    public string prefsKey = "settings_masterVolume";

    [Header("Input Lock (New Input System)")]
    public PlayerInput playerInput;
    public string keyboardSchemeName = "Keyboard&Mouse";
    public string gamepadSchemeName = "Gamepad";

    [Header("Pause")]
    public bool pauseGameWhileOpen = true;

    [Header("Cursor")]
    public bool showCursorWhileOpen = true;
    public bool hideCursorOnClose = false; // menus usually want false

    [Header("Controller Selection")]
    [Tooltip("First object to be selected when Settings opens (ex: Master slider, Apply button).")]
    public GameObject firstSelectedOnOpen;

    [Tooltip("Where controller selection returns when Settings closes (ex: Settings button on menu). Optional.")]
    public GameObject returnSelectedOnClose;

    [Tooltip("If true, keeps selection alive while Settings is open (fixes 'EventSystem went wonky').")]
    public bool keepSelectionAlive = true;

    [Header("3D Scene Hotkey (optional)")]
    [Tooltip("If true, ESC toggles the Settings UI open/close EVEN if this object is disabled.")]
    public bool enableEscToggle = false;

    [Tooltip("If true, ESC will close settings using the same logic as Cancel (confirm keep/revert).")]
    public bool escActsLikeCancel = true;

    [Header("Startup")]
    [Tooltip("If true, Settings UI will be forced hidden after scene load so it can start disabled.")]
    public bool startHiddenOnSceneLoad = true;

    // =========================================================
    // ✅ BADGES (NEW)
    // =========================================================
    [Header("Badges (NEW)")]
    [Tooltip("Optional root panel for badges (Pokemon gym badge style).")]
    public GameObject badgesRoot;

    [Tooltip("Optional parent transform containing BadgeIconUI children (grid). If null, uses badgesRoot.transform.")]
    public Transform badgesGridRoot;

    [Tooltip("If true, refreshes badge icons whenever Settings opens.")]
    public bool refreshBadgesOnOpen = true;

    private const string PREF_INPUTMODE = "settings_inputMode";

    private GameSettingsData _applied;
    private GameSettingsData _working;
    private bool _suppressToggleCallbacks;

    private bool _prevCursorVisible;
    private CursorLockMode _prevCursorLock;

    private Coroutine _focusRoutine;

    // =========================================================
    // ✅ STATIC ESC LISTENER (works even if object is inactive)
    // =========================================================
    private static bool s_hooked;
    private static bool s_escDown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RuntimeInit()
    {
        HookGlobalEscListener();
        ForceHideOnLoadForAllControllers();
    }

    private static void HookGlobalEscListener()
    {
        if (s_hooked) return;
        s_hooked = true;

        InputSystem.onEvent += OnGlobalInputEvent;
        SceneManager.sceneLoaded += (_, __) => ForceHideOnLoadForAllControllers();
    }

    private static void OnGlobalInputEvent(InputEventPtr eventPtr, InputDevice device)
    {
        var kb = device as Keyboard;
        if (kb == null) return;

        bool down = (kb.escapeKey != null) && kb.escapeKey.isPressed;

        // rising edge only
        if (down && !s_escDown)
        {
            ToggleAnyControllerByEsc();
        }

        s_escDown = down;
    }

    // =========================================================
    // ✅ FIX #1: Only hide Settings controllers in the ACTIVE scene
    // =========================================================
    private static void ForceHideOnLoadForAllControllers()
    {
        Scene active = SceneManager.GetActiveScene();

        var all = Resources.FindObjectsOfTypeAll<SettingsUIController>();
        if (all == null) return;

        for (int i = 0; i < all.Length; i++)
        {
            var c = all[i];
            if (c == null) continue;
            if (!c.startHiddenOnSceneLoad) continue;

            if (!c.gameObject.scene.IsValid()) continue;
            if (c.gameObject.scene != active) continue;

            if (c.settingsRoot != null)
            {
                if (c.settingsRoot.activeSelf)
                    c.settingsRoot.SetActive(false);
            }
            else
            {
                if (c.gameObject.activeSelf)
                    c.gameObject.SetActive(false);
            }

            // ✅ EXTRA SAFETY: if any controller masked bindings previously, clear it on scene load
            if (c.playerInput != null && c.playerInput.actions != null)
                c.playerInput.actions.bindingMask = null;
        }
    }

    // =========================================================
    // ✅ FIX #2: ESC should only toggle a Settings controller in the ACTIVE scene
    // =========================================================
    private static void ToggleAnyControllerByEsc()
    {
        var all = Resources.FindObjectsOfTypeAll<SettingsUIController>();
        if (all == null || all.Length == 0) return;

        Scene active = SceneManager.GetActiveScene();

        SettingsUIController target = null;
        for (int i = 0; i < all.Length; i++)
        {
            var c = all[i];
            if (c == null) continue;
            if (!c.enableEscToggle) continue;

            if (!c.gameObject.scene.IsValid()) continue;
            if (c.gameObject.scene != active) continue;

            target = c;
            break;
        }

        if (target == null) return;

        if (target.confirmModal != null && target.confirmModal.IsOpen()) return;
        if (target.infoModal != null && target.infoModal.IsOpen()) return;

        bool isOpen = (target.settingsRoot != null)
            ? target.settingsRoot.activeInHierarchy
            : target.gameObject.activeInHierarchy;

        if (!isOpen)
        {
            target.OpenSettingsUI();
            return;
        }

        if (target.escActsLikeCancel)
            target.OnCancelClicked();
        else
            target.CloseSettingsUI();
    }

    // =========================================================
    // INSTANCE LIFECYCLE
    // =========================================================
    private void Awake()
    {
        if (applyButton) applyButton.onClick.AddListener(OnApplyClicked);
        if (cancelButton) cancelButton.onClick.AddListener(OnCancelClicked);

        if (masterSlider) masterSlider.onValueChanged.AddListener(OnMasterChanged);

        EnsureToggleGroup();

        if (keyboardToggle) keyboardToggle.onValueChanged.AddListener(OnKeyboardToggleChanged);
        if (gamepadToggle) gamepadToggle.onValueChanged.AddListener(OnGamepadToggleChanged);

        if (playerInput != null)
            playerInput.neverAutoSwitchControlSchemes = true;

        if (returnToMenuButton)
            returnToMenuButton.onClick.AddListener(OnReturnToMenuClicked);

        RefreshReturnToMenuVisibility();
    }

    private void OnEnable()
    {
        CacheCursorState();
        ApplyOpenState();

        _working = LoadFromPrefs();
        _applied = _working.Copy();

        if (_working.inputMode == InputMode.Gamepad && !IsGamepadConnected())
            _working.inputMode = InputMode.Keyboard;

        RefreshUI();
        ApplyPreview(_working);
        UpdateApplyInteractable();

        RefreshReturnToMenuVisibility();

        // ✅ NEW: Refresh badge icons whenever settings opens
        RefreshBadgesUI();

        StartFocusOnOpen();
    }

    private void OnDisable()
    {
        RestoreCloseState();
        StopFocusRoutine();
    }

    private void Update()
    {
        if (!keepSelectionAlive) return;

        bool open = (settingsRoot != null) ? settingsRoot.activeInHierarchy : gameObject.activeInHierarchy;
        if (!open) return;

        var es = EventSystem.current;
        if (es == null) return;

        if (es.currentSelectedGameObject == null)
        {
            if (confirmModal != null && confirmModal.IsOpen()) return;
            if (infoModal != null && infoModal.IsOpen()) return;

            ForceSelectFirst();
        }
    }

    // =========================================================
    // ✅ PUBLIC OPEN
    // =========================================================
    public void OpenSettingsUI()
    {
        if (settingsRoot != null)
        {
            if (!settingsRoot.activeInHierarchy)
                settingsRoot.SetActive(true);
        }
        else
        {
            if (!gameObject.activeInHierarchy)
                gameObject.SetActive(true);
        }
        // OnEnable handles the rest.
    }

    private void StartFocusOnOpen()
    {
        StopFocusRoutine();
        _focusRoutine = StartCoroutine(FocusNextFrame());
    }

    private IEnumerator FocusNextFrame()
    {
        yield return null;
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

    private void ForceSelectFirst()
    {
        var es = EventSystem.current;
        if (es == null) return;

        GameObject target = firstSelectedOnOpen;

        if (target == null)
        {
            if (masterSlider != null) target = masterSlider.gameObject;
            else if (applyButton != null) target = applyButton.gameObject;
            else if (cancelButton != null) target = cancelButton.gameObject;
            else if (returnToMenuButton != null && returnToMenuButton.gameObject.activeInHierarchy)
                target = returnToMenuButton.gameObject;
        }

        if (target == null || !target.activeInHierarchy) return;

        es.SetSelectedGameObject(null);
        es.SetSelectedGameObject(target);
    }

    private void CacheCursorState()
    {
        _prevCursorVisible = Cursor.visible;
        _prevCursorLock = Cursor.lockState;
    }

    private void ApplyOpenState()
    {
        if (pauseGameWhileOpen)
            Time.timeScale = 0f;

        if (showCursorWhileOpen)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
    }

    private void RestoreCloseState()
    {
        Time.timeScale = 1f;

        if (hideCursorOnClose)
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
        else
        {
            Cursor.visible = _prevCursorVisible;
            Cursor.lockState = _prevCursorLock;
        }
    }

    private void OnMasterChanged(float v)
    {
        _working.masterVolume01 = Mathf.Clamp01(v);
        ApplyMasterVolumeToMixer(_working.masterVolume01);
        UpdateApplyInteractable();
    }

    private void OnKeyboardToggleChanged(bool on)
    {
        if (_suppressToggleCallbacks) return;
        if (!on) return;

        _working.inputMode = InputMode.Keyboard;
        ApplyInputMode(_working.inputMode);
        UpdateApplyInteractable();
    }

    private void OnGamepadToggleChanged(bool on)
    {
        if (_suppressToggleCallbacks) return;
        if (!on) return;

        if (!IsGamepadConnected())
        {
            ForceToggleToKeyboard();

            var m = SafeInfoModal();
            if (m != null)
                m.ShowInfo("No gamepad detected. Input will remain Keyboard.", onOk: null);

            return;
        }

        _working.inputMode = InputMode.Gamepad;
        ApplyInputMode(_working.inputMode);
        UpdateApplyInteractable();
    }

    private void OnApplyClicked()
    {
        if (_working.Equals(_applied))
        {
            CloseSettingsUI();
            return;
        }

        if (_working.inputMode == InputMode.Gamepad && !IsGamepadConnected())
        {
            ForceToggleToKeyboard();

            var m = SafeInfoModal();
            if (m != null)
                m.ShowInfo("No gamepad detected. Input will remain Keyboard.", onOk: null);

            return;
        }

        var confirm = SafeConfirmModal();
        if (confirm == null) return;

        confirm.ShowConfirm(
            "Apply changes?",
            onYes: () =>
            {
                ApplyAndSave(_working);

                var info = SafeInfoModal();
                if (info == null) return;

                info.ShowInfo("Changes applied.", onOk: () =>
                {
                    _applied = _working.Copy();
                    UpdateApplyInteractable();
                    CloseSettingsUI();
                });
            },
            onNo: () => { }
        );
    }

    private void OnCancelClicked()
    {
        if (_working.Equals(_applied))
        {
            CloseSettingsUI();
            return;
        }

        var confirm = SafeConfirmModal();
        if (confirm == null) return;

        confirm.ShowConfirm(
            "Keep changes?",
            onYes: () =>
            {
                if (_working.inputMode == InputMode.Gamepad && !IsGamepadConnected())
                {
                    ForceToggleToKeyboard();

                    var m = SafeInfoModal();
                    if (m != null)
                        m.ShowInfo("No gamepad detected. Input will remain Keyboard.", onOk: null);

                    return;
                }

                ApplyAndSave(_working);

                var info = SafeInfoModal();
                if (info == null) return;

                info.ShowInfo("Changes applied.", onOk: () =>
                {
                    _applied = _working.Copy();
                    UpdateApplyInteractable();
                    CloseSettingsUI();
                });
            },
            onNo: () =>
            {
                _working = _applied.Copy();

                if (_working.inputMode == InputMode.Gamepad && !IsGamepadConnected())
                    _working.inputMode = InputMode.Keyboard;

                RefreshUI();
                ApplyPreview(_working);
                UpdateApplyInteractable();

                StartFocusOnOpen();
            }
        );
    }

    private void RefreshReturnToMenuVisibility()
    {
        if (returnToMenuButton == null) return;
        returnToMenuButton.gameObject.SetActive(enableReturnToMainMenu);
    }

    private void OnReturnToMenuClicked()
    {
        if (!enableReturnToMainMenu) return;

        var confirm = SafeConfirmModal();
        if (confirm == null) return;

        bool hasChanges = (_working != null && _applied != null && !_working.Equals(_applied));
        string msg = hasChanges
            ? "Return to Main Menu?\nUnsaved changes will be lost."
            : "Return to Main Menu?";

        confirm.ShowConfirm(
            msg,
            onYes: () =>
            {
                // ✅ SAFETY: make sure menu loads unpaused, and clear any action binding masks.
                Time.timeScale = 1f;
                AudioListener.pause = false;

                if (playerInput != null && playerInput.actions != null)
                    playerInput.actions.bindingMask = null;

                if (settingsRoot) settingsRoot.SetActive(false);
                else gameObject.SetActive(false);

                SceneManager.LoadScene(mainMenuSceneName);
            },
            onNo: () => { }
        );
    }

    private void EnsureToggleGroup()
    {
        if (inputToggleGroup == null)
        {
            inputToggleGroup = GetComponent<ToggleGroup>();
            if (inputToggleGroup == null)
                inputToggleGroup = gameObject.AddComponent<ToggleGroup>();
        }

        inputToggleGroup.allowSwitchOff = false;

        if (keyboardToggle) keyboardToggle.group = inputToggleGroup;
        if (gamepadToggle) gamepadToggle.group = inputToggleGroup;
    }

    private bool IsGamepadConnected() => Gamepad.current != null;

    private void ForceToggleToKeyboard()
    {
        _suppressToggleCallbacks = true;

        _working.inputMode = InputMode.Keyboard;

        if (keyboardToggle) keyboardToggle.SetIsOnWithoutNotify(true);
        if (gamepadToggle) gamepadToggle.SetIsOnWithoutNotify(false);

        _suppressToggleCallbacks = false;

        ApplyInputMode(InputMode.Keyboard);
        UpdateApplyInteractable();
    }

    private void RefreshUI()
    {
        _suppressToggleCallbacks = true;

        if (masterSlider) masterSlider.SetValueWithoutNotify(_working.masterVolume01);

        bool wantGamepad = (_working.inputMode == InputMode.Gamepad) && IsGamepadConnected();
        bool wantKeyboard = !wantGamepad;

        if (keyboardToggle) keyboardToggle.SetIsOnWithoutNotify(wantKeyboard);
        if (gamepadToggle) gamepadToggle.SetIsOnWithoutNotify(wantGamepad);

        _suppressToggleCallbacks = false;
    }

    private void UpdateApplyInteractable()
    {
        if (applyButton)
            applyButton.interactable = !_working.Equals(_applied);
    }

    private void ApplyPreview(GameSettingsData data)
    {
        ApplyMasterVolumeToMixer(data.masterVolume01);
        ApplyInputMode(data.inputMode);
    }

    private void ApplyAndSave(GameSettingsData data)
    {
        ApplyPreview(data);

        PlayerPrefs.SetFloat(prefsKey, data.masterVolume01);
        PlayerPrefs.SetString(PREF_INPUTMODE, data.inputMode == InputMode.Gamepad ? "Gamepad" : "Keyboard");
        PlayerPrefs.Save();
    }

    private GameSettingsData LoadFromPrefs()
    {
        var d = new GameSettingsData();
        d.masterVolume01 = Mathf.Clamp01(PlayerPrefs.GetFloat(prefsKey, 1f));
        string mode = PlayerPrefs.GetString(PREF_INPUTMODE, "Keyboard");
        d.inputMode = (mode == "Gamepad") ? InputMode.Gamepad : InputMode.Keyboard;
        return d;
    }

    private void ApplyMasterVolumeToMixer(float linear01)
    {
        if (masterMixer == null) return;

        linear01 = Mathf.Clamp01(linear01);
        float dB = (linear01 <= 0.0001f) ? -80f : Mathf.Log10(linear01) * 20f;
        dB = Mathf.Clamp(dB, -80f, 0f);

        masterMixer.SetFloat(exposedParam, dB);
    }

    private void ApplyInputMode(InputMode mode)
    {
        if (playerInput == null) return;
        if (playerInput.actions == null) return;

        playerInput.neverAutoSwitchControlSchemes = true;

        // ✅ CRITICAL FIX:
        // Do NOT set bindingMask on the whole asset.
        // If your EventSystem/InputSystemUIInputModule shares this same actions asset instance,
        // bindingMask can BREAK UI Submit/Click in other menus (especially after scene changes).
        playerInput.actions.bindingMask = null;

        if (mode == InputMode.Keyboard)
        {
            // Just switch scheme; don't mask bindings.
            playerInput.SwitchCurrentControlScheme(keyboardSchemeName, Keyboard.current, Mouse.current);
        }
        else
        {
            if (!IsGamepadConnected())
            {
                ApplyInputMode(InputMode.Keyboard);
                return;
            }

            playerInput.SwitchCurrentControlScheme(gamepadSchemeName, Gamepad.current);
        }
    }

    private void CloseSettingsUI()
    {
        if (settingsRoot)
            settingsRoot.SetActive(false);
        else
            gameObject.SetActive(false);

        RestoreCloseState();

        var es = EventSystem.current;
        if (es != null)
        {
            GameObject target = returnSelectedOnClose;
            if (target != null && target.activeInHierarchy)
            {
                es.SetSelectedGameObject(null);
                es.SetSelectedGameObject(target);
            }
        }
    }

    private SimpleModal SafeConfirmModal()
    {
        if (confirmModal == null)
            Debug.LogError("SettingsUIController: Confirm Modal is not assigned.");
        return confirmModal;
    }

    private SimpleModal SafeInfoModal()
    {
        if (infoModal == null)
            Debug.LogError("SettingsUIController: Info Modal is not assigned.");
        return infoModal;
    }

    // =========================================================
    // ✅ BADGES HELPERS (NEW)
    // =========================================================
    private void RefreshBadgesUI()
    {
        if (!refreshBadgesOnOpen) return;

        Transform root = null;

        if (badgesGridRoot != null)
            root = badgesGridRoot;
        else if (badgesRoot != null)
            root = badgesRoot.transform;

        if (root == null) return;

        // BadgeIconUI is a tiny script that swaps sprites based on RewardStateManager status.
        var icons = root.GetComponentsInChildren<BadgeIconUI>(true);
        if (icons == null) return;

        for (int i = 0; i < icons.Length; i++)
        {
            if (icons[i] != null)
                icons[i].Refresh();
        }
    }
}