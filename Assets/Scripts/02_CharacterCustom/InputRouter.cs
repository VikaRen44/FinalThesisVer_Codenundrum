using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class InputModeUIRouter : MonoBehaviour
{
    public enum Mode { Mouse, KeyboardGamepad }

    [Header("Mode")]
    public Mode mode = Mode.KeyboardGamepad;

    [Header("Selection Roots")]
    [Tooltip("Uppercase grid (used to pick the first key).")]
    public RectTransform upperCaseGrid;

    [Tooltip("Upper + Lower grids for key lookup when typing.")]
    public RectTransform[] keyGrids;

    [Header("Remember Hover")]
    public bool rememberLastMouseHovered = true;
    public bool ensureSelectionOnKBGamepad = true;

    [Header("Input Actions (UI Map)")]
    [Tooltip("We hook this to backspace behavior as well (UI module may also use it).")]
    public InputActionReference cancel;

    [Header("Name Input (typing + backspace)")]
    public NameInput nameInput;

    [Header("Typing")]
    [Tooltip("If true, typing will snap selection + retrigger selection animation even if same key.")]
    public bool forceReselectForAnimation = true;

    [Tooltip("If true, keyboard typing will also show pressed visuals (without double-typing).")]
    public bool typingShowsPressedVisual = true;

    [Header("Cursor")]
    [Tooltip("For UI, keep this OFF. Locking can interfere with UI navigation in some setups.")]
    public bool lockCursorInKBGamepadMode = false;

    [Header("Debug")]
    public bool logTyping = false;
    public bool logSelection = false;
    public bool logKeyMapCount = false;

    private readonly Dictionary<char, KeyButton> _keyMap = new();
    private GameObject _lastMouseHovered;
    private GameObject _lastSelected;

    // ✅ CapsLock must be tracked as a TOGGLE (not isPressed)
    private bool _capsLockOn = false;

    // ─────────────────────────────────────────────────────────────
    // Unity lifecycle
    // ─────────────────────────────────────────────────────────────
    void OnEnable()
    {
        BuildKeyMap();
        if (logKeyMapCount) Debug.Log($"[Router] KeyMap count = {_keyMap.Count}");

        if (Keyboard.current != null)
            Keyboard.current.onTextInput += OnTextInput;

        if (cancel != null)
            cancel.action.performed += OnCancelPerformed;

        StartCoroutine(EnsureSelectionSoon());
    }

    void OnDisable()
    {
        if (Keyboard.current != null)
            Keyboard.current.onTextInput -= OnTextInput;

        if (cancel != null)
            cancel.action.performed -= OnCancelPerformed;
    }

    void Update()
    {
        UpdateCapsLockToggle();
        DetectDeviceUseAndSwitch();
        TrackLastSelected();
    }

    // ─────────────────────────────────────────────────────────────
    // CapsLock toggle handling (press once => stays on)
    // ─────────────────────────────────────────────────────────────
    void UpdateCapsLockToggle()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.capsLockKey.wasPressedThisFrame)
        {
            _capsLockOn = !_capsLockOn;
            if (logTyping) Debug.Log($"[Router] CapsLock toggled: {_capsLockOn}");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Public API (used by LetterGenerator)
    // ─────────────────────────────────────────────────────────────
    public void BuildKeyMap()
    {
        _keyMap.Clear();
        if (keyGrids == null) return;

        foreach (RectTransform g in keyGrids)
        {
            if (!g) continue;

            KeyButton[] keys = g.GetComponentsInChildren<KeyButton>(true);
            foreach (KeyButton kb in keys)
            {
                if (kb == null) continue;

                char c = kb.keyChar;

                // Store both cases so lookup is robust
                AddMap(char.ToUpperInvariant(c), kb);
                AddMap(char.ToLowerInvariant(c), kb);
            }
        }

        if (logKeyMapCount) Debug.Log($"[Router] KeyMap rebuilt = {_keyMap.Count}");

        if (ensureSelectionOnKBGamepad && mode == Mode.KeyboardGamepad)
            EnsureSelectionNow();
    }

    // ✅ This was missing in your file -> caused the red underline
    void AddMap(char c, KeyButton kb)
    {
        if (kb == null) return;

        // Keep first mapping; don’t overwrite
        if (_keyMap.ContainsKey(c)) return;

        _keyMap.Add(c, kb);
    }

    public void ForceSelectFirstKey()
    {
        if (!ensureSelectionOnKBGamepad) return;
        if (EventSystem.current == null) return;

        // Don't override existing selection
        if (EventSystem.current.currentSelectedGameObject != null) return;

        GameObject first = FindFirstKey();
        if (first != null)
        {
            EventSystem.current.SetSelectedGameObject(first);
            if (logSelection) Debug.Log($"[Router] ForceSelectFirstKey -> {first.name}");
        }
        else
        {
            Debug.LogWarning("[Router] ForceSelectFirstKey: upperCaseGrid has no interactable Button child.");
        }
    }

    public void NotifyMouseHover(GameObject hovered)
    {
        if (!rememberLastMouseHovered) return;
        _lastMouseHovered = hovered;
    }

    // ─────────────────────────────────────────────────────────────
    // Selection helpers
    // ─────────────────────────────────────────────────────────────
    IEnumerator EnsureSelectionSoon()
    {
        yield return null;
        EnsureSelectionNow();
    }

    void EnsureSelectionNow()
    {
        if (!ensureSelectionOnKBGamepad) return;
        if (EventSystem.current == null) return;
        if (EventSystem.current.currentSelectedGameObject != null) return;

        GameObject target = null;

        if (rememberLastMouseHovered && _lastMouseHovered != null) target = _lastMouseHovered;
        else if (_lastSelected != null) target = _lastSelected;
        else target = FindFirstKey();

        if (target != null)
        {
            EventSystem.current.SetSelectedGameObject(target);
            if (logSelection) Debug.Log($"[Router] EnsureSelectionNow -> {target.name}");
        }
        else
        {
            Debug.LogWarning("[Router] EnsureSelectionNow: No target found. Assign upperCaseGrid and ensure it has Button children.");
        }
    }

    GameObject FindFirstKey()
    {
        if (!upperCaseGrid) return null;

        Button[] buttons = upperCaseGrid.GetComponentsInChildren<Button>(true);
        foreach (var b in buttons)
        {
            if (b != null && b.gameObject.activeInHierarchy && b.interactable)
                return b.gameObject;
        }
        return null;
    }

    void SelectForHoverAnimation(GameObject target)
    {
        if (EventSystem.current == null || target == null) return;

        if (forceReselectForAnimation)
            EventSystem.current.SetSelectedGameObject(null);

        EventSystem.current.SetSelectedGameObject(target);

        if (logSelection)
        {
            var cur = EventSystem.current.currentSelectedGameObject;
            Debug.Log($"[Router] Selected: {(cur ? cur.name : "NULL")}");
        }
    }

    void TrackLastSelected()
    {
        if (EventSystem.current == null) return;
        GameObject sel = EventSystem.current.currentSelectedGameObject;
        if (sel != null) _lastSelected = sel;
    }

    // ─────────────────────────────────────────────────────────────
    // Mode switching
    // ─────────────────────────────────────────────────────────────
    void DetectDeviceUseAndSwitch()
    {
        // Mouse activity -> mouse mode
        if (Mouse.current != null)
        {
            if (Mouse.current.delta.ReadValue().sqrMagnitude > 0.01f ||
                Mouse.current.leftButton.wasPressedThisFrame ||
                Mouse.current.rightButton.wasPressedThisFrame ||
                Mouse.current.scroll.ReadValue().sqrMagnitude > 0.01f)
            {
                SetMode(Mode.Mouse);
                return;
            }
        }

        bool kbUsed = Keyboard.current != null &&
                      (Keyboard.current.anyKey.wasPressedThisFrame || Keyboard.current.anyKey.isPressed);

        bool padUsed = Gamepad.current != null &&
                       (Gamepad.current.dpad.ReadValue().sqrMagnitude > 0.01f ||
                        Gamepad.current.leftStick.ReadValue().sqrMagnitude > 0.2f ||
                        Gamepad.current.buttonSouth.wasPressedThisFrame ||
                        Gamepad.current.buttonEast.wasPressedThisFrame);

        if (kbUsed || padUsed)
            SetMode(Mode.KeyboardGamepad);
    }

    void SetMode(Mode newMode)
    {
        if (mode == newMode) return;
        mode = newMode;

        if (mode == Mode.Mouse)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
        else
        {
            Cursor.visible = false;
            Cursor.lockState = lockCursorInKBGamepadMode ? CursorLockMode.Locked : CursorLockMode.None;
            EnsureSelectionNow();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Typing (CapsLock toggle + Shift)
    // ─────────────────────────────────────────────────────────────
    void OnTextInput(char c)
    {
        if (mode != Mode.KeyboardGamepad) return;
        if (!char.IsLetter(c)) return;
        if (nameInput == null) return;
        if (Keyboard.current == null) return;

        bool shift = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;

        // ✅ XOR behavior: CapsLock toggles base, Shift flips it
        bool upper = _capsLockOn ^ shift;

        char finalChar = upper ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c);

        if (logTyping)
            Debug.Log($"[Router] onTextInput '{c}' -> '{finalChar}' (caps={_capsLockOn}, shift={shift})");

        // UI keys are A-Z (uppercase), so highlight by uppercase key
        char highlightKey = char.ToUpperInvariant(finalChar);

        if (!TryGetKeyButtonForChar(highlightKey, out var keyButton) || keyButton == null)
            return;

        // highlight/select
        SelectForHoverAnimation(keyButton.gameObject);

        // type correct case directly (prevents your button onClick forcing uppercase)
        nameInput.AddLetter(finalChar);

        // pressed visual only (no click => no double typing)
        if (typingShowsPressedVisual)
            SimulatePressedVisualOnly(keyButton);
    }

    void OnCancelPerformed(InputAction.CallbackContext ctx)
    {
        if (mode != Mode.KeyboardGamepad) return;
        if (nameInput == null) return;

        nameInput.Backspace();
        EnsureSelectionNow();
    }

    bool TryGetKeyButtonForChar(char c, out KeyButton kb)
    {
        kb = null;

        if (_keyMap.TryGetValue(c, out kb) && kb != null)
            return true;

        if (logTyping)
            Debug.LogWarning($"[Router] No KeyButton mapped for '{c}'. Check keyGrids + KeyButton.keyChar assignments.");

        return false;
    }

    // Pressed visual only (no click)
    void SimulatePressedVisualOnly(KeyButton kb)
    {
        if (kb == null) return;

        Button b = kb.button != null ? kb.button : kb.GetComponent<Button>();
        if (b == null || !b.interactable) return;

        if (EventSystem.current == null) return;

        var ped = new PointerEventData(EventSystem.current)
        {
            pointerId = -1,
            button = PointerEventData.InputButton.Left
        };

        ExecuteEvents.Execute(b.gameObject, ped, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(b.gameObject, ped, ExecuteEvents.pointerUpHandler);
    }
}
