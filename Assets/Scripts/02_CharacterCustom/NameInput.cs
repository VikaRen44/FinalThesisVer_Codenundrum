using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class NameInput : MonoBehaviour
{
    private const string PREF_PLAYER_NAME = "playerName";

    // ✅ NEW: one-time flag set by CharacterSelectionButtons.Back()
    private const string PREF_KEEP_TYPED_ON_NEXT_OPEN = "NameInput_keepTypedNextOpen";

    [Header("UI")]
    public TMP_Text nameDisplay;

    [Header("Buttons (for hotkeys)")]
    public Button doneButton;
    public Button backButton;

    [Header("Config")]
    public int maxLength = 12;
    public string defaultName = "Charlie";

    [Header("Behavior")]
    public bool alwaysResetToDefaultOnOpen = true;
    public bool persistWhileTyping = false;

    [Tooltip("If TRUE, Persist will also call SaveSystem.SetActiveProfile(name).\n" +
             "Turn OFF in Name-Creation scene to avoid creating folders before duplicate check.")]
    public bool setActiveProfileOnPersist = true;

    // ✅ NEW: prevents Main Menu Search NameInput from hijacking the active profile
    [Header("Profile / SaveGameManager Sync (NEW)")]
    [Tooltip("If TRUE, Awake() will push the current name into SaveGameManager.SetPlayerName().\n" +
             "TURN THIS OFF for Search Name UI in Main Menu.")]
    public bool pushToSaveGameManagerOnAwake = true;

    [Tooltip("If TRUE, PersistNameToPrefsAndManager will call SaveGameManager.SetPlayerName().\n" +
             "TURN THIS OFF for Search Name UI to avoid switching profiles while searching.")]
    public bool pushToSaveGameManagerOnPersist = true;

    [Header("Hotkeys")]
    public bool enableEnterForDone = true;
    public bool enableEscForBack = true;
    public bool hotkeysWorkEvenWhenTyping = true;

    public string CurrentName => _playerName;

    public event Action<string> OnNameChanged;
    public event Action<string> OnDonePressed;

    private string _playerName = "";
    private bool _hasCommitted = false;

    private bool _enterWasDown;
    private bool _escWasDown;

    private void Awake()
    {
        // ✅ If we came BACK from Character Selection, keep the typed name ONCE
        bool keepTypedThisOpen = PlayerPrefs.GetInt(PREF_KEEP_TYPED_ON_NEXT_OPEN, 0) == 1;
        if (keepTypedThisOpen)
        {
            _playerName = PlayerPrefs.GetString(PREF_PLAYER_NAME, defaultName);
            _hasCommitted = false;

            // consume flag so next normal open (from menu) resets to default again
            PlayerPrefs.SetInt(PREF_KEEP_TYPED_ON_NEXT_OPEN, 0);
            PlayerPrefs.Save();
        }
        else
        {
            if (alwaysResetToDefaultOnOpen)
            {
                _playerName = defaultName;
                _hasCommitted = false;
            }
            else
            {
                _playerName = PlayerPrefs.GetString(PREF_PLAYER_NAME, defaultName);
            }
        }

        NormalizeName();
        RefreshUI();

        // ✅ only push if this NameInput is meant to be authoritative
        if (pushToSaveGameManagerOnAwake && SaveGameManager.Instance != null)
            SaveGameManager.Instance.SetPlayerName(_playerName);

        Debug.Log($"[NameInput] Awake -> CurrentName='{_playerName}' keepTypedThisOpen={keepTypedThisOpen} resetOnOpen={alwaysResetToDefaultOnOpen}");
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (!hotkeysWorkEvenWhenTyping && IsAnyUIInputFocused())
            return;

        if (enableEnterForDone)
        {
            bool down = kb.enterKey.isPressed || kb.numpadEnterKey.isPressed;
            if (down && !_enterWasDown)
                TriggerDoneHotkey();
            _enterWasDown = down;
        }

        if (enableEscForBack)
        {
            bool down = kb.escapeKey.isPressed;
            if (down && !_escWasDown)
                TriggerBackHotkey();
            _escWasDown = down;
        }
    }

    private bool IsAnyUIInputFocused() => false;

    private void TriggerDoneHotkey()
    {
        if (doneButton != null && doneButton.interactable)
        {
            doneButton.onClick.Invoke();
            return;
        }

        Done();
    }

    private void TriggerBackHotkey()
    {
        if (backButton != null && backButton.interactable)
        {
            backButton.onClick.Invoke();
            return;
        }

        Debug.LogWarning("[NameInput] Esc pressed but backButton is not assigned.");
    }

    public void AddLetter(char c)
    {
        if (_playerName.Length >= maxLength) return;

        _playerName += c;
        NormalizeName();
        RefreshUI();

        if (persistWhileTyping)
            PersistNameToPrefsAndManager();

        OnNameChanged?.Invoke(_playerName);
    }

    public void Backspace()
    {
        if (_playerName.Length == 0) return;

        _playerName = _playerName.Substring(0, _playerName.Length - 1);
        NormalizeName();
        RefreshUI();

        if (persistWhileTyping)
            PersistNameToPrefsAndManager();

        OnNameChanged?.Invoke(_playerName);
    }

    public void Clear()
    {
        _playerName = "";
        NormalizeName();
        RefreshUI();

        if (persistWhileTyping)
            PersistNameToPrefsAndManager();

        OnNameChanged?.Invoke(_playerName);
    }

    public string GetSanitizedNameForChecks()
    {
        string n = _playerName;

        if (string.IsNullOrWhiteSpace(n))
            n = defaultName;

        n = n.Trim();

        if (n.Length > maxLength)
            n = n.Substring(0, maxLength);

        if (string.IsNullOrWhiteSpace(n))
            n = defaultName;

        return n;
    }

    public void CommitForValidationOnly()
    {
        _playerName = GetSanitizedNameForChecks();
        NormalizeName();
        RefreshUI();

        Debug.Log($"[NameInput] CommitForValidationOnly -> '{_playerName}'");
    }

    public void Done()
    {
        NormalizeName();

        if (string.IsNullOrWhiteSpace(_playerName))
            _playerName = defaultName;

        if (_playerName.Length > maxLength)
            _playerName = _playerName.Substring(0, maxLength);

        PersistNameToPrefsAndManager();
        _hasCommitted = true;

        Debug.Log($"[NameInput] Done -> COMMITTED '{_playerName}'");
        OnDonePressed?.Invoke(_playerName);
    }

    public void DoneThenInvoke() => Done();

    private void PersistNameToPrefsAndManager()
    {
        string finalName = string.IsNullOrWhiteSpace(_playerName) ? defaultName : _playerName;

        PlayerPrefs.SetString(PREF_PLAYER_NAME, finalName);
        PlayerPrefs.Save();

        if (setActiveProfileOnPersist)
            SaveSystem.SetActiveProfile(finalName);

        if (pushToSaveGameManagerOnPersist && SaveGameManager.Instance != null)
            SaveGameManager.Instance.SetPlayerName(finalName);

        Debug.Log($"[NameInput] Persist -> '{finalName}' | setActiveProfileOnPersist={setActiveProfileOnPersist}, pushToSGMOnPersist={pushToSaveGameManagerOnPersist}");
    }

    private void NormalizeName()
    {
        if (_playerName == null) _playerName = "";
        _playerName = _playerName.Trim();

        if (_playerName.Length > maxLength)
            _playerName = _playerName.Substring(0, maxLength);
    }

    private void RefreshUI()
    {
        if (nameDisplay != null)
            nameDisplay.text = _playerName;
    }

    private void OnDisable()
    {
        if (persistWhileTyping && !_hasCommitted)
            Debug.Log("[NameInput] OnDisable -> leaving without Done().");
    }
}