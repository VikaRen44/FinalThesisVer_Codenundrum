using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class BattleDialogueUI : MonoBehaviour
{
    [Header("UI Root (whole dialogue panel)")]
    public GameObject uiRoot;
    public bool hideUIWhenIdle = false;

    [Header("UI Refs")]
    public TMP_Text bodyText;
    public GameObject continueIcon;

    [Header("Typing")]
    public float charsPerSecond = 40f;
    public bool allowSkipToEnd = true;
    public bool advanceRequiresNewClick = true;
    public float inputCooldown = 0.08f;

    // internal
    private readonly List<string> _lines = new();
    private int _index = 0;
    private bool _typing = false;
    private bool _waitingForAdvance = false;
    private float _cooldownTimer = 0f;
    private Coroutine _typingRoutine;

    // Optional: to enforce "new click" when advanceRequiresNewClick is true
    private bool _armedForAdvance = false;

    void Awake()
    {
        if (uiRoot == null) uiRoot = gameObject;

        // AUTO FIND REFS if not assigned
        if (bodyText == null)
        {
            bodyText = GetComponentInChildren<TMP_Text>(true);
        }

        if (continueIcon == null)
        {
            // look for child named ContinueIcon
            var t = transform.Find("ContinueIcon");
            if (t != null) continueIcon = t.gameObject;
        }

        if (bodyText == null)
            Debug.LogError("[BattleDialogueUI] BodyText TMP_Text not found/assigned.");

        if (continueIcon == null)
            Debug.LogWarning("[BattleDialogueUI] ContinueIcon not found/assigned.");

        if (!hideUIWhenIdle && uiRoot != null) uiRoot.SetActive(true);

        ClearText();
        SetContinue(false);
    }

    void Update()
    {
        if (_cooldownTimer > 0) _cooldownTimer -= Time.deltaTime;

        if (!_waitingForAdvance) return;
        if (_cooldownTimer > 0) return;

        // If we require a "new click", we wait until the player has released everything once.
        if (advanceRequiresNewClick && !_armedForAdvance)
        {
            if (!IsAnyAdvanceHeld())
                _armedForAdvance = true;

            return;
        }

        if (!AdvancePressedThisFrame()) return;

        _cooldownTimer = inputCooldown;
        AdvanceLine();
    }

    // Plays ScriptableObject sequence
    public void PlaySequence(BattleDialogueSequence seq)
    {
        if (seq == null || seq.lines == null || seq.lines.Count == 0)
        {
            ShowRuntimeLine("...");
            return;
        }

        _lines.Clear();
        _lines.AddRange(seq.lines);
        _index = 0;

        StartLine(_lines[_index]);
    }

    // Plays dynamic line
    public void ShowRuntimeLine(string line)
    {
        _lines.Clear();
        _lines.Add(line);
        _index = 0;

        StartLine(_lines[_index]);
    }

    public void ShowRuntimeLines(params string[] lines)
    {
        _lines.Clear();
        _lines.AddRange(lines);
        _index = 0;

        StartLine(_lines[_index]);
    }

    public void ClearText()
    {
        if (_typingRoutine != null) StopCoroutine(_typingRoutine);
        _typingRoutine = null;

        _typing = false;
        _waitingForAdvance = false;
        _armedForAdvance = false;

        if (bodyText != null) bodyText.text = "";
        SetContinue(false);

        if (hideUIWhenIdle && uiRoot != null)
            uiRoot.SetActive(false);
    }

    void StartLine(string line)
    {
        if (uiRoot != null) uiRoot.SetActive(true);

        if (bodyText == null)
        {
            Debug.LogError("[BattleDialogueUI] Can't show line — bodyText is null.");
            return;
        }

        if (_typingRoutine != null) StopCoroutine(_typingRoutine);
        _typingRoutine = StartCoroutine(TypeLine(line));
    }

    IEnumerator TypeLine(string line)
    {
        _typing = true;
        _waitingForAdvance = false;
        _armedForAdvance = false;
        SetContinue(false);

        bodyText.text = "";

        float t = 0;
        int charIndex = 0;

        while (charIndex < line.Length)
        {
            if (allowSkipToEnd && AdvancePressedThisFrame())
            {
                bodyText.text = line;
                break;
            }

            t += Time.deltaTime * charsPerSecond;
            charIndex = Mathf.FloorToInt(t);
            charIndex = Mathf.Clamp(charIndex, 0, line.Length);

            bodyText.text = line.Substring(0, charIndex);
            yield return null;
        }

        bodyText.text = line;
        _typing = false;

        _waitingForAdvance = true;
        SetContinue(true);

        if (advanceRequiresNewClick)
        {
            // Ensure player releases keys before next advance is allowed
            _armedForAdvance = false;
        }

        _cooldownTimer = inputCooldown;
    }

    void AdvanceLine()
    {
        if (_typing) return;

        _index++;
        if (_index >= _lines.Count)
        {
            _waitingForAdvance = false;
            _armedForAdvance = false;
            SetContinue(false);

            if (hideUIWhenIdle && uiRoot != null)
                uiRoot.SetActive(false);

            return;
        }

        StartLine(_lines[_index]);
    }

    void SetContinue(bool on)
    {
        if (continueIcon != null)
            continueIcon.SetActive(on);
    }

    // =========================================================
    // Input System helpers (NEW input system only)
    // =========================================================

    private bool AdvancePressedThisFrame()
    {
        // Mouse left click
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;

        // Keyboard keys
        if (Keyboard.current != null)
        {
            if (Keyboard.current.zKey.wasPressedThisFrame) return true;
            if (Keyboard.current.spaceKey.wasPressedThisFrame) return true;
            if (Keyboard.current.enterKey.wasPressedThisFrame) return true;
            if (Keyboard.current.numpadEnterKey.wasPressedThisFrame) return true;
        }

        // Gamepad "Submit" equivalent (A / Cross)
        if (Gamepad.current != null)
        {
            if (Gamepad.current.buttonSouth.wasPressedThisFrame) return true;
            if (Gamepad.current.startButton.wasPressedThisFrame) return true; // optional
        }

        return false;
    }

    private bool IsAnyAdvanceHeld()
    {
        // Mouse held
        if (Mouse.current != null && Mouse.current.leftButton.isPressed) return true;

        // Keyboard held
        if (Keyboard.current != null)
        {
            if (Keyboard.current.zKey.isPressed) return true;
            if (Keyboard.current.spaceKey.isPressed) return true;
            if (Keyboard.current.enterKey.isPressed) return true;
            if (Keyboard.current.numpadEnterKey.isPressed) return true;
        }

        // Gamepad held
        if (Gamepad.current != null)
        {
            if (Gamepad.current.buttonSouth.isPressed) return true;
            if (Gamepad.current.startButton.isPressed) return true;
        }

        return false;
    }
}
