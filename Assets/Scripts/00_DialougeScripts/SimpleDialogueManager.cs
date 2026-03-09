using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

public class SimpleDialogueManager : MonoBehaviour
{
    // =========================================================
    // ACTIVE MANAGER (scene-based)
    // =========================================================
    public static SimpleDialogueManager Instance { get; private set; } // Active for current scene

    [Header("Scene Binding (NEW)")]
    [Tooltip("If TRUE, this manager auto binds to the scene it's placed in.")]
    public bool autoBindToCurrentScene = true;

    [Tooltip("Scene name this manager belongs to. If empty and autoBind is OFF, it will match ANY scene.")]
    public string sceneName = "";

    [Tooltip("If multiple managers match the active scene, higher priority wins.")]
    public int priority = 0;

    private static readonly List<SimpleDialogueManager> _loadedManagers = new List<SimpleDialogueManager>();

    private static void RecomputeActiveManager()
    {
        string active = SceneManager.GetActiveScene().name;

        SimpleDialogueManager best = null;

        for (int i = 0; i < _loadedManagers.Count; i++)
        {
            var m = _loadedManagers[i];
            if (m == null) continue;
            if (!m.isActiveAndEnabled) continue;

            bool match = string.IsNullOrWhiteSpace(m.sceneName) || m.sceneName == active;
            if (!match) continue;

            if (best == null || m.priority > best.priority)
                best = m;
        }

        Instance = best;
    }

    private void Register()
    {
        if (!_loadedManagers.Contains(this))
            _loadedManagers.Add(this);

        RecomputeActiveManager();
    }

    private void Unregister()
    {
        _loadedManagers.Remove(this);

        if (Instance == this)
            Instance = null;

        RecomputeActiveManager();
    }

    // =========================================================
    // YOUR EXISTING FIELDS (unchanged)
    // =========================================================
    [Header("UI & Settings")]
    public SimpleDialogueUI ui; // ✅ kept for inspector compatibility

    [Tooltip("Optional: explicitly assign the NORMAL dialogue UI here.")]
    public SimpleDialogueUI normalUi;

    [Tooltip("Optional: explicitly assign the CUTSCENE dialogue UI here.")]
    public SimpleDialogueUI cutsceneUi;

    public float charsPerSecond = 40f;
    public bool allowSkipTyping = true;

    [Header("Player Lock (optional)")]
    public PlayerMovement playerMovement;
    public bool lockPlayerWhileDialogue = true;

    [Header("Typing SFX (AudioSource)")]
    public AudioSource typingSource;
    public AudioClip fallbackTypingClip;

    [Range(0f, 1f)]
    public float fallbackTypingVolume = 0.6f;

    // ✅ Pitch mapping
    [Serializable]
    public class SpeakerPitchRule
    {
        public string speakerName;
        [Range(-3f, 3f)] public float pitch = 1f;
    }

    [Header("Typing SFX Pitch")]
    [Range(-3f, 3f)]
    public float defaultTypingPitch = 1f;

    public bool randomizeTypingPitch = false;

    [Range(0f, 0.3f)]
    public float typingPitchJitter = 0.05f;

    public SpeakerPitchRule[] speakerPitchRules;

    [Header("Advance Input (New Input System)")]
    public InputActionReference advanceAction;
    public InputActionReference submitAction;
    public InputActionReference clickAction;

    [Header("Input Behaviour")]
    public bool autoEnableActionsIfDisabled = true;

    [Header("Debug")]
    public bool verboseLogs = true;

    public bool IsPlaying => _playRoutine != null;

    public event Action OnDialogueEnded;

    private Coroutine _playRoutine;
    private bool _advancePressed;

    private bool _choiceSelected;
    private int _selectedChoiceIndex;

    private InputAction _advance;
    private InputAction _submit;
    private InputAction _click;

    private bool _yesNoPromptActive;
    private Action _yesCallback;
    private Action _noCallback;

    // ✅ UI override stack (cutscene temporarily pushes)
    private readonly Stack<SimpleDialogueUI> _uiOverrideStack = new Stack<SimpleDialogueUI>();

    // ✅ NEW: per-run typing speed override (prevents charsPerSecond from being permanently changed)
    private bool _overrideTypingSpeedThisRun = false;
    private float _typingSpeedOverride = -1f;

    // =========================================================
    // LIFECYCLE (scene-based, NO DontDestroyOnLoad)
    // =========================================================
    private void Awake()
    {
        if (autoBindToCurrentScene)
            sceneName = SceneManager.GetActiveScene().name;

        Register();

        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;

        RefreshUIReference();
        EnsureTypingSource();
        CacheActions();
        EnableActionsIfNeeded();
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        Unregister();
    }

    private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
    {
        RecomputeActiveManager();

        // Only active manager refreshes
        if (Instance == this)
        {
            RefreshUIReference();
            CacheActions();
            EnableActionsIfNeeded();
        }
    }

    private void OnEnable()
    {
        CacheActions();
        EnableActionsIfNeeded();
        Register();
    }

    private void OnDisable()
    {
        Unregister();
    }

    // =========================================================
    // SAFE STATIC ENTRYPOINTS (use these from other scripts)
    // =========================================================
    public static bool TryStartDialogueOnActive(SimpleDialogueSequenceSO sequence)
    {
        if (Instance == null) return false;
        return Instance.TryStartDialogueInternal(sequence);
    }

    public static bool TryStartYesNoPromptOnActive(
        string title, string message, string yesText, string noText,
        Action onYes, Action onNo, bool useTyping = true)
    {
        if (Instance == null) return false;
        return Instance.TryStartYesNoPrompt(title, message, yesText, noText, onYes, onNo, useTyping);
    }

    // =========================================================
    // UI OVERRIDES
    // =========================================================
    public void PushUIOverride(SimpleDialogueUI overrideUi)
    {
        if (overrideUi == null) return;
        _uiOverrideStack.Push(overrideUi);
        ui = overrideUi; // keep legacy field in sync
    }

    public void PopUIOverride(SimpleDialogueUI expectedTop = null)
    {
        if (_uiOverrideStack.Count == 0)
        {
            RefreshUIReference();
            return;
        }

        if (expectedTop != null && _uiOverrideStack.Peek() != expectedTop)
        {
            var temp = new Stack<SimpleDialogueUI>();
            while (_uiOverrideStack.Count > 0 && _uiOverrideStack.Peek() != expectedTop)
                temp.Push(_uiOverrideStack.Pop());
            if (_uiOverrideStack.Count > 0) _uiOverrideStack.Pop();
        }
        else
        {
            _uiOverrideStack.Pop();
        }

        if (_uiOverrideStack.Count > 0)
            ui = _uiOverrideStack.Peek();
        else
            RefreshUIReference();
    }

    private void CacheActions()
    {
        _advance = (advanceAction != null) ? advanceAction.action : null;
        _submit = (submitAction != null) ? submitAction.action : null;
        _click = (clickAction != null) ? clickAction.action : null;
    }

    private void EnableActionsIfNeeded()
    {
        if (!autoEnableActionsIfDisabled) return;

        if (_advance != null && !_advance.enabled) _advance.Enable();
        if (_submit != null && !_submit.enabled) _submit.Enable();
        if (_click != null && !_click.enabled) _click.Enable();
    }

    private void EnsureTypingSource()
    {
        if (typingSource != null)
        {
            typingSource.playOnAwake = false;
            typingSource.loop = true;
            typingSource.spatialBlend = 0f;
            return;
        }

        typingSource = GetComponent<AudioSource>();
        if (typingSource == null)
        {
            typingSource = gameObject.AddComponent<AudioSource>();
        }

        typingSource.playOnAwake = false;
        typingSource.loop = true;
        typingSource.spatialBlend = 0f;
    }

    private void RefreshUIReference()
    {
        if (_uiOverrideStack.Count > 0)
        {
            ui = _uiOverrideStack.Peek();
            return;
        }

        if (normalUi == null)
            normalUi = FindUIByRole(isCutscene: false);

        if (cutsceneUi == null)
            cutsceneUi = FindUIByRole(isCutscene: true);

        if (ui == null || ui.isCutsceneUI)
            ui = normalUi != null ? normalUi : ui;

        if (ui == null)
            ui = FindAnyUI();
    }

    private SimpleDialogueUI FindUIByRole(bool isCutscene)
    {
#if UNITY_2023_1_OR_NEWER
        var all = UnityEngine.Object.FindObjectsByType<SimpleDialogueUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        var all = UnityEngine.Object.FindObjectsOfType<SimpleDialogueUI>(true);
#endif
        foreach (var u in all)
        {
            if (u != null && u.isCutsceneUI == isCutscene)
                return u;
        }
        return null;
    }

    private SimpleDialogueUI FindAnyUI()
    {
#if UNITY_2023_1_OR_NEWER
        return UnityEngine.Object.FindFirstObjectByType<SimpleDialogueUI>(FindObjectsInactive.Include);
#else
        return UnityEngine.Object.FindObjectOfType<SimpleDialogueUI>(true);
#endif
    }

    private void Update()
    {
        // ✅ Only active manager reads inputs
        if (Instance != this) return;
        if (!IsPlaying) return;

        if ((_advance != null && _advance.enabled && _advance.WasPressedThisFrame()) ||
            (_submit != null && _submit.enabled && _submit.WasPressedThisFrame()) ||
            (_click != null && _click.enabled && _click.WasPressedThisFrame()))
        {
            _advancePressed = true;
        }
    }

    // =========================================================
    // YOUR PUBLIC API (kept)
    // =========================================================
    public bool TryStartDialogue(SimpleDialogueSequenceSO sequence)
    {
        // redirect to active manager if called on wrong one
        if (Instance != null && Instance != this)
            return Instance.TryStartDialogueInternal(sequence);

        return TryStartDialogueInternal(sequence);
    }

    public bool TryStartDialogueInternal(SimpleDialogueSequenceSO sequence)
    {
        if (sequence == null) return false;

        if (ui == null)
        {
            RefreshUIReference();
            if (ui == null)
            {
                Debug.LogError("[SimpleDialogueManager] Missing SimpleDialogueUI reference.");
                return false;
            }
        }

        if (IsPlaying) return false;

        _playRoutine = StartCoroutine(PlayRoutine(sequence));
        return true;
    }

    public bool TryStartDialogueWithUI(SimpleDialogueSequenceSO sequence, SimpleDialogueUI overrideUi)
    {
        if (Instance != null && Instance != this)
            return Instance.TryStartDialogueWithUI(sequence, overrideUi);

        if (sequence == null) return false;
        if (IsPlaying) return false;

        if (overrideUi != null)
            PushUIOverride(overrideUi);

        bool started = TryStartDialogueInternal(sequence);

        if (!started && overrideUi != null)
            PopUIOverride(overrideUi);

        return started;
    }

    public bool TryStartYesNoPrompt(
        string title,
        string message,
        string yesText,
        string noText,
        Action onYes,
        Action onNo,
        bool useTyping = true
    )
    {
        if (Instance != null && Instance != this)
            return Instance.TryStartYesNoPrompt(title, message, yesText, noText, onYes, onNo, useTyping);

        if (IsPlaying) return false;

        if (ui == null)
        {
            RefreshUIReference();
            if (ui == null)
            {
                Debug.LogError("[SimpleDialogueManager] Missing SimpleDialogueUI reference (Yes/No prompt).");
                return false;
            }
        }

        _yesNoPromptActive = true;
        _yesCallback = onYes;
        _noCallback = onNo;

        // ✅ IMPORTANT: do NOT permanently mutate charsPerSecond
        _overrideTypingSpeedThisRun = !useTyping;
        _typingSpeedOverride = useTyping ? -1f : 0f;

        var seq = ScriptableObject.CreateInstance<SimpleDialogueSequenceSO>();
        seq.defaultTypingSfx = null;
        seq.defaultTypingSfxVolume = 1f;

        var line = new SimpleDialogueLine();
        line.speakerName = title;
        line.text = message;

        line.choices = new SimpleDialogueChoice[2];
        line.choices[0] = new SimpleDialogueChoice { text = yesText, nextLineIndex = -1 };
        line.choices[1] = new SimpleDialogueChoice { text = noText, nextLineIndex = -1 };

        seq.lines = new SimpleDialogueLine[1];
        seq.lines[0] = line;

        _playRoutine = StartCoroutine(PlayRoutine(seq));
        return true;
    }

    // =========================================================
    // Playback
    // =========================================================
    private IEnumerator PlayRoutine(SimpleDialogueSequenceSO sequence)
    {
        ui.Show();

        if (lockPlayerWhileDialogue && playerMovement != null)
            playerMovement.canMove = false;

        for (int lineIndex = 0; lineIndex < sequence.lines.Length; lineIndex++)
        {
            var line = sequence.lines[lineIndex];
            if (line == null) continue;

            ui.SetSpeakerName(line.speakerName);
            ui.SetPortrait(line.portrait, line.clearPortraitIfNone);
            ui.PlayEmotion(line.emotion);

            ui.SetContinueIconVisible(false);

            yield return TypeLine(sequence, lineIndex, line, line.text ?? string.Empty);

            bool hasChoices = (line.choices != null && line.choices.Length > 0);
            ui.SetContinueIconVisible(!hasChoices);

            if (hasChoices)
            {
                _choiceSelected = false;
                _selectedChoiceIndex = -1;

                ui.ShowChoices(line.choices, idx =>
                {
                    _selectedChoiceIndex = idx;
                    _choiceSelected = true;
                });

                while (!_choiceSelected)
                    yield return null;

                ui.ClearChoices();

                if (_yesNoPromptActive)
                {
                    if (_selectedChoiceIndex == 0) SafeInvoke(_yesCallback);
                    else SafeInvoke(_noCallback);
                    break;
                }

                int next = line.choices[_selectedChoiceIndex].nextLineIndex;
                if (next < 0) break;
                lineIndex = next - 1;
            }
            else
            {
                yield return WaitForAdvance();
            }
        }

        EndDialogue();
    }

    private void SafeInvoke(Action a)
    {
        try { a?.Invoke(); }
        catch (Exception e) { Debug.LogError("[SimpleDialogueManager] Callback exception:\n" + e); }
    }

    private IEnumerator TypeLine(SimpleDialogueSequenceSO sequence, int lineIndex, SimpleDialogueLine line, string fullText)
    {
        ui.SetBodyText("");

        // ✅ Determine typing speed for THIS run (prevents global mutation bugs)
        float effectiveCps = (_overrideTypingSpeedThisRun && _typingSpeedOverride >= 0f)
            ? _typingSpeedOverride
            : charsPerSecond;

        // ✅ Resolve typing SFX for this line
        AudioClip clip = null;
        float vol = 1f;

        try
        {
            // Your existing helper
            sequence.GetTypingSfxForLineSafe(lineIndex, out clip, out vol);
        }
        catch (Exception e)
        {
            if (verboseLogs)
                Debug.LogWarning("[SimpleDialogueManager] GetTypingSfxForLineSafe failed, using fallback.\n" + e);
            clip = null;
            vol = 1f;
        }

        // ✅ Fallback if sequence/line didn't provide one
        if (clip == null && fallbackTypingClip != null)
        {
            clip = fallbackTypingClip;
            vol = fallbackTypingVolume;
        }

        float pitch = GetTypingPitchForSpeaker(line != null ? line.speakerName : null);

        // Start SFX only if we have a clip
        if (clip != null)
            StartTypingSfx(clip, vol, pitch);
        else
            StopTypingSfx();

        // Instant text mode
        if (effectiveCps <= 0f)
        {
            ui.SetBodyText(fullText);
            StopTypingSfx();
            yield break;
        }

        float delay = 1f / Mathf.Max(1f, effectiveCps);
        string cur = "";

        for (int i = 0; i < fullText.Length; i++)
        {
            if (allowSkipTyping && _advancePressed)
            {
                _advancePressed = false;
                ui.SetBodyText(fullText);
                StopTypingSfx();
                yield break;
            }

            cur += fullText[i];
            ui.SetBodyText(cur);
            yield return new WaitForSecondsRealtime(delay);
        }

        StopTypingSfx();
    }

    private float GetTypingPitchForSpeaker(string speakerName)
    {
        float basePitch = defaultTypingPitch;

        if (!string.IsNullOrWhiteSpace(speakerName) && speakerPitchRules != null)
        {
            for (int i = 0; i < speakerPitchRules.Length; i++)
            {
                var r = speakerPitchRules[i];
                if (r == null) continue;
                if (string.IsNullOrWhiteSpace(r.speakerName)) continue;

                if (string.Equals(r.speakerName.Trim(), speakerName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    basePitch = r.pitch;
                    break;
                }
            }
        }

        if (randomizeTypingPitch && typingPitchJitter > 0f)
            basePitch += UnityEngine.Random.Range(-typingPitchJitter, typingPitchJitter);

        return Mathf.Clamp(basePitch, -3f, 3f);
    }

    private void StartTypingSfx(AudioClip clip, float volume01, float pitch)
    {
        EnsureTypingSource();

        if (clip == null)
        {
            StopTypingSfx();
            return;
        }

        typingSource.Stop();
        typingSource.clip = clip;
        typingSource.volume = Mathf.Clamp01(volume01);
        typingSource.pitch = pitch;
        typingSource.loop = true;
        typingSource.Play();
    }

    private void StopTypingSfx()
    {
        if (typingSource == null) return;
        if (typingSource.isPlaying) typingSource.Stop();
        typingSource.clip = null;
        typingSource.pitch = 1f;
    }

    private IEnumerator WaitForAdvance()
    {
        _advancePressed = false;
        yield return null;

        while (!_advancePressed)
            yield return null;

        _advancePressed = false;
        StopTypingSfx();
    }

    private void EndDialogue()
    {
        StopTypingSfx();

        if (lockPlayerWhileDialogue && playerMovement != null)
            playerMovement.canMove = true;

        ui.Hide();

        _playRoutine = null;
        _advancePressed = false;
        _choiceSelected = false;
        _selectedChoiceIndex = -1;

        _yesNoPromptActive = false;
        _yesCallback = null;
        _noCallback = null;

        // ✅ reset per-run typing override
        _overrideTypingSpeedThisRun = false;
        _typingSpeedOverride = -1f;

        OnDialogueEnded?.Invoke();

        if (_uiOverrideStack.Count > 0)
            PopUIOverride(_uiOverrideStack.Peek());
    }


}
