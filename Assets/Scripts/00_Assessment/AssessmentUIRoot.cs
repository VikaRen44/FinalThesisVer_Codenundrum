// AssessmentUIRoot.cs
// ✅ FULL EDITED + HARDENED VERSION + ✅ GREEN/RED CHOICE COLOR FEEDBACK FIX (STABLE)
// - Keeps your existing behavior + features
// - ✅ High score is PER-SAVE (SaveGameManager -> SaveData.assessmentScores)
// - ✅ Optional legacy GLOBAL PlayerPrefs highscores (toggle)
// - ✅ AssessmentLaunchContext overrides (json/rewards/highScoreKey/spawn override)
// - ✅ Fix: does NOT mutate q.correctIndex permanently when shuffling choices
// - ✅ Fix: validates activeHighScoreKey against supportedHighScoreKeys (prevents wrong key)
// - ✅ Fix: handles timer/flow safely, prevents double-finish, stops stray coroutines
// - ✅ Adds small safety guards for missing UI refs
//
// ✅ ADDED (for hub score view):
// - Records score to AssessmentScoreManager (so hub score UI can read it immediately)
// - Optional safe reflection Refresh() call on your hub/menu score UI script
//
// ✅ ADDED (your request):
// - After clicking a choice: clicked button turns GREEN if correct, RED if wrong
// - Fixes Disabled/Highlighted tint overriding the feedback color by LOCKING ColorBlock states
//
// ✅ FIXED (your new bug):
// - The “stuck on first choices / can’t click / never moves on” was caused by an exception:
//   ClearButtons() iterated _btnBackup with foreach while RestoreButtonVisual() removed from the same dictionary.
//   That throws “Collection was modified” -> coroutine breaks -> UI gets stuck.
// - Now ClearButtons restores visuals using a SAFE copy of keys, THEN clears the dictionary.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class AssessmentUIRoot : MonoBehaviour
{
    [Header("JSON")]
    [Tooltip("File name inside Assets/StreamingAssets/ (example: Assessment_questions.json)")]
    public string jsonFileName = "Assessment_questions.json";
    public bool shuffleQuestions = true;
    public bool shuffleChoices = true;

    [Header("UI - Question Board")]
    public TMP_Text questionText;
    public TMP_Text questionNumberText;
    public TMP_Text scoreText;

    [Header("UI - Choices")]
    public Transform choicesParent;
    public ChoiceButtonUI choiceButtonPrefab;

    [Header("Timer (Top Right Circle)")]
    public Image timerCircleImage;
    public float timePerQuestion = 10f;

    [Header("NPC Feedback")]
    public AssessmentNPCFeedback npcFeedback;

    [Header("Flow")]
    public float nextDelay = 0.55f;

    [Header("Auto Close After Finish")]
    public bool autoCloseOnFinish = true;

    [Tooltip("How long to show FINAL SCORE before returning.")]
    public float autoCloseDelay = 1.5f;

    [Tooltip("If true, this script will DISABLE this GameObject after finish (optional).")]
    public bool disableThisGameObjectOnFinish = false;

    [Tooltip("Optional override: if assigned, THIS object will be disabled instead of (this.gameObject).")]
    public GameObject disableTargetOverride;

    [Tooltip("If true, UI uses unscaled time (good if you pause the game).")]
    public bool useUnscaledTime = true;

    [Header("Choice Feedback (Green/Red)")]
    [Tooltip("If ON, clicked choice turns green if correct, red if wrong.")]
    public bool enableChoiceColorFeedback = true;

    [Tooltip("If ON, choices dim while showing feedback.")]
    public bool dimOtherChoicesWhileFeedback = true;

    [Range(0f, 1f)]
    public float dimAlpha = 0.35f;

    [Tooltip("Color used when a choice is correct.")]
    public Color correctChoiceColor = new Color(0.25f, 0.9f, 0.35f, 1f);

    [Tooltip("Color used when a choice is wrong.")]
    public Color wrongChoiceColor = new Color(0.95f, 0.25f, 0.25f, 1f);

    [Tooltip("If ON, when time runs out, the correct answer is shown in green (optional).")]
    public bool showCorrectOnTimeout = false;

    [Header("Player Lock While Assessing (via CutsceneRunner)")]
    public bool blockPlayerWhileActive = true;

    [Tooltip("Optional override. If null/empty, Assessment uses CutsceneRunner's defaultPlayerTag.")]
    public string playerTagOverride = "Player";

    [Tooltip("Optional direct player transform. If null, CutsceneRunner will find by tag.")]
    public Transform playerOverride;

    [Header("Optional Wrapper (music stop etc.)")]
    public AssessmentUI assessmentUI;

    [Header("Win State (Modern Return)")]
    public GameObject winRoot;
    public Animator winAnimator;
    public string winTrigger = "Win";
    public float winHoldSeconds = 1.25f;

    [Header("Return Fallback (Safety)")]
    public string fallbackReturnSceneName = "";
    public bool forceUnpauseOnReturn = true;

    [Header("Objective Update On Return (Hub)")]
    public bool forceHubObjectiveOnReturn = false;
    public string hubObjectiveIdOnReturn = "04_objective";
    public bool playObjectiveSfxOnApply = false;

    [Header("Hub Spawn Override (Story Return Only)")]
    public string hubSpawnPointNameOnReturn = "HubSpawnPoint";
    public string hubSceneName = "04_Gamehub";
    public string playerTagForHubSpawn = "Player";

    [Header("Rewards (Chapter Completion / Badges)")]
    public bool unlockRewardsOnFinish = true;
    public string chapterCompletionRewardId = RewardIDs.Chapter1Complete;
    public List<string> extraRewardIdsToUnlock = new List<string>();
    public bool requireMinScoreToUnlock = false;
    public int minScoreToUnlock = 0;

    [Header("Rewards (Assessment Badges - NEW)")]
    public bool awardCompletionBadgeFirstTime = true;
    public string assessmentCompleteRewardId = "CompleteAssessment1";
    public bool awardPerfectScoreBadge = true;
    public string perfectScoreRewardId = "PerfectAssessment1";

    [Header("High Score (NEW)")]
    [Tooltip("List of allowed high score keys this shared scene can support (ex: CH1_ASSESS, CH2_ASSESS).")]
    public List<string> supportedHighScoreKeys = new List<string>() { "CH1_ASSESS", "CH2_ASSESS" };

    [Tooltip("Runtime key used for saving. Usually overridden by AssessmentLaunchContext.")]
    public string activeHighScoreKey = "CH1_ASSESS";

    [Header("High Score Storage (Fix Persisting Across Saves)")]
    [Tooltip("If true, saves high score PER SAVE (SaveGameManager -> SaveData.assessmentScores). Recommended ON.")]
    public bool saveHighScoreIntoSaveFile = true;

    [Tooltip("If true, ALSO writes legacy GLOBAL PlayerPrefs high scores. Usually keep OFF to prevent leaking across saves.")]
    public bool alsoWriteLegacyPlayerPrefsHighScore = false;

    [Header("Hub Score UI Refresh (Optional)")]
    [Tooltip("Assign your hub/menu score UI component (ex: AssessmentsMenuScoreUI) if you want it to refresh immediately.")]
    public UnityEngine.Object menuScoreUi;

    [Tooltip("Method name to call on menuScoreUi (default: Refresh).")]
    public string menuScoreUiRefreshMethodName = "Refresh";

    [Tooltip("If true, calls Refresh() after saving score (finish/close).")]
    public bool refreshMenuScoreUiOnFinish = true;

    [Header("Start Countdown Gate (NEW)")]
    public bool enableStartCountdownGate = true;
    public GameObject countdownRoot;
    public TMP_Text countdownText;
    public float countdownStepSeconds = 0.75f;
    public float countdownStartHoldSeconds = 0.35f;
    public string countdownStartText = "START";

    [Header("Finish Overlay (NEW)")]
    public GameObject finishRoot;
    public TMP_Text finishText;
    public string finishMessage = "FINISHED!";
    public float finishHoldSeconds = 0.8f;

    [Header("Countdown/Finish Ease (NEW)")]
    public AnimationCurve countdownEase = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public float countdownScaleMin = 0.75f;
    public float countdownScaleMax = 1.15f;

    [Header("Countdown/Finish Text Size (NEW)")]
    public float countdownFontSizeMultiplier = 3.0f;
    public float finishFontSizeMultiplier = 2.5f;
    public bool forceCenterAlignForCountdown = true;

    [Header("Controller / Keyboard Support")]
    [Tooltip("Enable manual controller navigation across answer choices.")]
    public bool enableControllerSupport = true;

    [Tooltip("Usually UI/Navigate.")]
    public InputActionReference navigateAction;

    [Tooltip("Usually UI/Submit.")]
    public InputActionReference submitAction;

    [Tooltip("Optional UI/Cancel.")]
    public InputActionReference cancelAction;

    [Tooltip("Deadzone used for manual navigation.")]
    public float navigationDeadzone = 0.45f;

    [Tooltip("Repeat delay for controller navigation.")]
    public float navigationRepeatDelay = 0.18f;

    [Tooltip("Auto-select the first valid choice whenever a question appears.")]
    public bool autoSelectFirstChoiceOnQuestionShow = true;

    [Tooltip("If true, reselect a choice after countdown unlock.")]
    public bool autoReselectAfterCountdown = true;

    [Tooltip("If true, cancel clears current selected EventSystem object.")]
    public bool allowCancelToClearSelection = false;

    private bool _launchWantsHubSpawnOverride = false;

    private static bool _pendingHubSpawn = false;
    private static string _pendingHubSceneName = "";
    private static string _pendingPlayerTag = "Player";
    private static string _pendingSpawnObjectName = "";
    private static bool _spawnHookInstalled = false;

    private static void InstallSpawnHookIfNeeded()
    {
        if (_spawnHookInstalled) return;
        SceneManager.sceneLoaded += ApplyPendingHubSpawn_OnSceneLoaded;
        _spawnHookInstalled = true;
    }

    private static void ArmPendingHubSpawnByName(string hubSceneName, string playerTag, string spawnObjectName)
    {
        if (string.IsNullOrWhiteSpace(spawnObjectName)) return;

        _pendingHubSpawn = true;
        _pendingHubSceneName = hubSceneName ?? "";
        _pendingPlayerTag = string.IsNullOrEmpty(playerTag) ? "Player" : playerTag;
        _pendingSpawnObjectName = spawnObjectName;

        InstallSpawnHookIfNeeded();
    }

    private static void ApplyPendingHubSpawn_OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (!_pendingHubSpawn) return;
        if (string.IsNullOrEmpty(_pendingHubSceneName)) return;

        if (!string.Equals(s.name, _pendingHubSceneName, StringComparison.OrdinalIgnoreCase))
            return;

        var playerGO = GameObject.FindGameObjectWithTag(_pendingPlayerTag);
        if (playerGO == null)
        {
            Debug.LogWarning($"[AssessmentUIRoot] Pending Hub Spawn: Player with tag '{_pendingPlayerTag}' not found in scene '{s.name}'.");
            return;
        }

        GameObject spawnGO = GameObject.Find(_pendingSpawnObjectName);
        if (spawnGO == null)
        {
            Debug.LogWarning($"[AssessmentUIRoot] Pending Hub Spawn: Spawn object '{_pendingSpawnObjectName}' not found in scene '{s.name}'.");
            return;
        }

        playerGO.transform.SetPositionAndRotation(spawnGO.transform.position, spawnGO.transform.rotation);

        _pendingHubSpawn = false;
        _pendingHubSceneName = "";
        _pendingSpawnObjectName = "";
    }

    private AssessmentQuestionList _data;
    private List<AssessmentQuestion> _questions;
    private int _index;
    private int _score;
    private bool _running;
    private bool _finished;

    private readonly List<ChoiceButtonUI> _spawnedButtons = new List<ChoiceButtonUI>();
    private Coroutine _timerRoutine;
    private Coroutine _flowRoutine;
    private Coroutine _autoCloseRoutine;
    private bool inputLocked = false;

    private int _currentCorrectIndex = -1;

    private readonly Dictionary<ChoiceButtonUI, Color> _originalChoiceColors = new Dictionary<ChoiceButtonUI, Color>();

    private struct ButtonVisualBackup
    {
        public Selectable.Transition transition;
        public ColorBlock colors;
        public Graphic targetGraphic;
    }

    private readonly Dictionary<Button, ButtonVisualBackup> _btnBackup = new Dictionary<Button, ButtonVisualBackup>();

    private float _nextNavigateTime = 0f;

    public event Action OnAssessmentClosed;

    private void Awake()
    {
        InstallSpawnHookIfNeeded();
    }

    private void Start()
    {
        if (AssessmentLaunchContext.TryConsume(out var launch))
        {
            if (!string.IsNullOrWhiteSpace(launch.jsonFileName))
                jsonFileName = launch.jsonFileName;

            if (!string.IsNullOrWhiteSpace(launch.completionRewardId))
                assessmentCompleteRewardId = launch.completionRewardId;

            if (!string.IsNullOrWhiteSpace(launch.perfectRewardId))
                perfectScoreRewardId = launch.perfectRewardId;

            if (!string.IsNullOrWhiteSpace(launch.chapterCompletionRewardId))
                chapterCompletionRewardId = launch.chapterCompletionRewardId;

            _launchWantsHubSpawnOverride = launch.useHubSpawnOverrideOnReturn;
            if (launch.useHubSpawnOverrideOnReturn && !string.IsNullOrWhiteSpace(launch.hubSpawnPointNameOnReturn))
                hubSpawnPointNameOnReturn = launch.hubSpawnPointNameOnReturn;

            if (!string.IsNullOrWhiteSpace(launch.highScoreKey))
                activeHighScoreKey = launch.highScoreKey;
        }

        activeHighScoreKey = ValidateOrFallbackHighScoreKey(activeHighScoreKey);
        BeginAssessmentFromTrigger();
    }

    private void OnEnable()
    {
        if (winRoot) winRoot.SetActive(false);
        if (countdownRoot) countdownRoot.SetActive(false);
        if (finishRoot) finishRoot.SetActive(false);

        EnableInputActions(true);
    }

    private void OnDisable()
    {
        if (_running)
            EndAssessmentInternal(restorePlayer: true);

        StopAllRoutines();
        EnableInputActions(false);
    }

    private void Update()
    {
        HandleControllerInput();
    }

    private void EnableInputActions(bool enable)
    {
        if (navigateAction != null && navigateAction.action != null)
        {
            if (enable) navigateAction.action.Enable();
            else navigateAction.action.Disable();
        }

        if (submitAction != null && submitAction.action != null)
        {
            if (enable) submitAction.action.Enable();
            else navigateAction.action.Disable();
        }

        if (cancelAction != null && cancelAction.action != null)
        {
            if (enable) cancelAction.action.Enable();
            else cancelAction.action.Disable();
        }
    }

    private void HandleControllerInput()
    {
        if (!enableControllerSupport) return;
        if (!_running || _finished || inputLocked) return;
        if (_spawnedButtons == null || _spawnedButtons.Count == 0) return;

        if (submitAction != null && submitAction.action != null && submitAction.action.WasPressedThisFrame())
        {
            var current = GetCurrentlySelectedChoice();
            if (current != null && current.IsInteractable())
            {
                current.Submit();
                return;
            }

            SelectFirstAvailableChoiceIfNeeded(force: true);
        }

        if (cancelAction != null && cancelAction.action != null && cancelAction.action.WasPressedThisFrame())
        {
            if (allowCancelToClearSelection && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }

        Vector2 nav = Vector2.zero;
        if (navigateAction != null && navigateAction.action != null)
            nav = navigateAction.action.ReadValue<Vector2>();

        if (nav.magnitude < navigationDeadzone)
            return;

        if (Time.unscaledTime < _nextNavigateTime)
            return;

        int currentIndex = GetCurrentSelectedChoiceIndex();
        if (currentIndex < 0)
        {
            SelectFirstAvailableChoiceIfNeeded(force: true);
            _nextNavigateTime = Time.unscaledTime + Mathf.Max(0.01f, navigationRepeatDelay);
            return;
        }

        int dir = 0;
        if (Mathf.Abs(nav.y) >= Mathf.Abs(nav.x))
            dir = nav.y < 0f ? 1 : -1;
        else
            dir = nav.x > 0f ? 1 : -1;

        int nextIndex = FindNextInteractableChoiceIndex(currentIndex, dir);
        if (nextIndex >= 0 && nextIndex < _spawnedButtons.Count)
            SelectChoice(nextIndex);

        _nextNavigateTime = Time.unscaledTime + Mathf.Max(0.01f, navigationRepeatDelay);
    }

    private int GetCurrentSelectedChoiceIndex()
    {
        var current = GetCurrentlySelectedChoice();
        if (current == null) return -1;

        for (int i = 0; i < _spawnedButtons.Count; i++)
        {
            if (_spawnedButtons[i] == current)
                return i;
        }

        return -1;
    }

    private ChoiceButtonUI GetCurrentlySelectedChoice()
    {
        if (EventSystem.current == null) return null;

        GameObject go = EventSystem.current.currentSelectedGameObject;
        if (go == null) return null;

        ChoiceButtonUI choice = go.GetComponent<ChoiceButtonUI>();
        if (choice != null) return choice;

        return go.GetComponentInParent<ChoiceButtonUI>();
    }

    private int FindNextInteractableChoiceIndex(int startIndex, int dir)
    {
        if (_spawnedButtons == null || _spawnedButtons.Count == 0)
            return -1;

        int count = _spawnedButtons.Count;
        int idx = Mathf.Clamp(startIndex, 0, count - 1);

        for (int step = 1; step <= count; step++)
        {
            int next = idx + (dir * step);
            if (next < 0 || next >= count)
                continue;

            var choice = _spawnedButtons[next];
            if (choice != null && choice.IsInteractable())
                return next;
        }

        return idx;
    }

    private void SelectChoice(int index)
    {
        if (index < 0 || index >= _spawnedButtons.Count) return;

        var choice = _spawnedButtons[index];
        if (choice == null || !choice.IsInteractable()) return;

        choice.FocusNow();
    }

    private void SelectFirstAvailableChoiceIfNeeded(bool force = false)
    {
        if (_spawnedButtons == null || _spawnedButtons.Count == 0) return;

        if (!force)
        {
            var current = GetCurrentlySelectedChoice();
            if (current != null && current.IsInteractable())
                return;
        }

        for (int i = 0; i < _spawnedButtons.Count; i++)
        {
            if (_spawnedButtons[i] != null && _spawnedButtons[i].IsInteractable())
            {
                SelectChoice(i);
                return;
            }
        }
    }

    public void BeginAssessmentFromTrigger()
    {
        if (_running) return;

        if (blockPlayerWhileActive)
            LockPlayer();

        BeginAssessment();
    }

    public void BeginAssessment()
    {
        if (_running) return;

        _running = true;
        _finished = false;
        _score = 0;
        _index = 0;
        _currentCorrectIndex = -1;
        _nextNavigateTime = 0f;

        if (npcFeedback != null) npcFeedback.ResetToDefaultInstant();

        if (winRoot) winRoot.SetActive(false);
        if (countdownRoot) countdownRoot.SetActive(false);
        if (finishRoot) finishRoot.SetActive(false);

        StopAllRoutines();
        _flowRoutine = StartCoroutine(LoadThenStart());
    }

    public void CloseAndRestore()
    {
        if (_running)
            EndAssessmentInternal(restorePlayer: true);
        else
            RestorePlayerControl();

        TryRefreshMenuScoreUi();

        OnAssessmentClosed?.Invoke();
    }

    private void EndAssessmentInternal(bool restorePlayer)
    {
        _running = false;

        StopAllRoutines();
        SetAllButtonsInteractable(false);

        if (timerCircleImage != null) timerCircleImage.fillAmount = 1f;
        if (npcFeedback != null) npcFeedback.ResetToDefaultInstant();

        if (restorePlayer)
            RestorePlayerControl();
    }

    private void LockPlayer()
    {
        if (CutsceneRunner.Instance != null)
        {
            CutsceneRunner.Instance.ExternalLockPlayer(playerOverride, playerTagOverride);
            return;
        }

        Debug.LogWarning("[AssessmentUIRoot] CutsceneRunner.Instance missing. Player will not be locked.");
    }

    private void RestorePlayerControl()
    {
        if (!blockPlayerWhileActive) return;

        if (CutsceneRunner.Instance != null)
        {
            CutsceneRunner.Instance.ExternalUnlockPlayer();
            return;
        }
    }

    private void StopAllRoutines()
    {
        if (_timerRoutine != null) StopCoroutine(_timerRoutine);
        if (_flowRoutine != null) StopCoroutine(_flowRoutine);
        if (_autoCloseRoutine != null) StopCoroutine(_autoCloseRoutine);

        _timerRoutine = null;
        _flowRoutine = null;
        _autoCloseRoutine = null;
    }

    private IEnumerator LoadThenStart()
    {
        yield return LoadJsonFromStreamingAssets();
        if (!_running) yield break;

        if (_data == null || _data.questions == null || _data.questions.Count == 0)
        {
            if (questionText) questionText.text = "No questions found.";
            if (questionNumberText) questionNumberText.text = "";
            if (scoreText) scoreText.text = "";
            ClearButtons();

            _running = false;
            RestorePlayerControl();

            if (autoCloseOnFinish)
                _autoCloseRoutine = StartCoroutine(WinThenReturnRoutine());

            yield break;
        }

        _questions = new List<AssessmentQuestion>(_data.questions);
        if (shuffleQuestions) Shuffle(_questions);

        _index = 0;
        _score = 0;
        _currentCorrectIndex = -1;

        if (enableStartCountdownGate) SetInputLocked(true);
        else SetInputLocked(false);

        ShowQuestion(_index);

        if (enableStartCountdownGate)
            yield return StartCoroutine(StartCountdownGateRoutine());
    }

    private void ShowQuestion(int i)
    {
        if (!_running || _finished) return;

        if (_questions == null || i < 0 || i >= _questions.Count)
        {
            FinishAssessment();
            return;
        }

        var q = _questions[i];
        if (q == null)
        {
            _index++;
            ShowQuestion(_index);
            return;
        }

        if (q.choices == null) q.choices = new List<string>();

        if (questionText) questionText.text = string.IsNullOrEmpty(q.question) ? "(Missing question)" : q.question;
        if (questionNumberText) questionNumberText.text = $"Question {i + 1} / {_questions.Count}";
        UpdateScoreText();

        BuildChoiceButtonsForQuestion(q);

        if (_timerRoutine != null) StopCoroutine(_timerRoutine);
        _timerRoutine = null;

        if (!inputLocked)
        {
            _timerRoutine = StartCoroutine(TimerRoutine());

            if (autoSelectFirstChoiceOnQuestionShow)
                StartCoroutine(SelectFirstChoiceNextFrame());
        }
        else
        {
            if (timerCircleImage != null) timerCircleImage.fillAmount = 1f;
            SetAllButtonsInteractable(false);
        }
    }

    private IEnumerator SelectFirstChoiceNextFrame()
    {
        yield return null;
        SelectFirstAvailableChoiceIfNeeded(force: true);
    }

    private void BuildChoiceButtonsForQuestion(AssessmentQuestion q)
    {
        ClearButtons();
        _currentCorrectIndex = -1;

        if (choicesParent == null || choiceButtonPrefab == null)
        {
            Debug.LogWarning("[AssessmentUIRoot] Missing choicesParent or choiceButtonPrefab.");
            return;
        }

        List<string> choices = new List<string>(q.choices ?? new List<string>());
        int originalCorrect = Mathf.Clamp(q.correctIndex, 0, Mathf.Max(0, choices.Count - 1));

        if (shuffleChoices && choices.Count > 1)
        {
            List<int> order = new List<int>(choices.Count);
            for (int k = 0; k < choices.Count; k++) order.Add(k);
            Shuffle(order);

            List<string> shuffled = new List<string>(choices.Count);
            int newCorrect = 0;

            for (int k = 0; k < order.Count; k++)
            {
                int oldIndex = order[k];
                shuffled.Add(choices[oldIndex]);
                if (oldIndex == originalCorrect) newCorrect = k;
            }

            choices = shuffled;
            _currentCorrectIndex = newCorrect;
        }
        else
        {
            _currentCorrectIndex = originalCorrect;
        }

        for (int c = 0; c < choices.Count; c++)
        {
            var btn = Instantiate(choiceButtonPrefab, choicesParent);
            _spawnedButtons.Add(btn);

            string label = string.IsNullOrEmpty(choices[c]) ? "(Empty)" : choices[c];
            int choiceIndex = c;

            btn.Setup(label, choiceIndex, HandleChoiceClicked);

            _originalChoiceColors[btn] = GetChoiceVisualColor(btn);

            btn.SetInteractable(!inputLocked);
        }
    }

    private void HandleChoiceClicked(int choiceIndex)
    {
        if (!_running || _finished || inputLocked) return;

        if (_timerRoutine != null) StopCoroutine(_timerRoutine);
        _timerRoutine = null;

        SetAllButtonsInteractable(false);

        int safeCorrectIndex = Mathf.Clamp(_currentCorrectIndex, 0, Mathf.Max(0, _spawnedButtons.Count - 1));
        int safeClicked = Mathf.Clamp(choiceIndex, 0, Mathf.Max(0, _spawnedButtons.Count - 1));
        bool correct = (safeClicked == safeCorrectIndex);

        if (enableChoiceColorFeedback)
        {
            ApplyChoiceFeedback(
                clickedIndex: safeClicked,
                correctIndex: safeCorrectIndex,
                clickedWasCorrect: correct,
                dimOthers: dimOtherChoicesWhileFeedback
            );
        }

        if (correct)
        {
            _score++;
            if (npcFeedback != null) npcFeedback.PlayCorrect();
        }
        else
        {
            if (npcFeedback != null) npcFeedback.PlayWrong();
        }

        UpdateScoreText();

        if (_flowRoutine != null) StopCoroutine(_flowRoutine);
        _flowRoutine = StartCoroutine(NextQuestionAfterDelay());
    }

    private IEnumerator NextQuestionAfterDelay()
    {
        yield return Wait(nextDelay);

        if (!_running || _finished) yield break;

        _index++;
        ShowQuestion(_index);
    }

    private IEnumerator TimerRoutine()
    {
        float t = 0f;
        float dur = Mathf.Max(0.01f, timePerQuestion);

        if (timerCircleImage != null)
            timerCircleImage.fillAmount = 1f;

        while (t < dur && _running && !_finished && !inputLocked)
        {
            t += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

            float a = Mathf.Clamp01(1f - (t / dur));
            if (timerCircleImage != null)
                timerCircleImage.fillAmount = a;

            yield return null;
        }

        if (!_running || _finished || inputLocked) yield break;

        SetAllButtonsInteractable(false);
        if (npcFeedback != null) npcFeedback.PlayWrong();

        if (enableChoiceColorFeedback && showCorrectOnTimeout)
        {
            int safeCorrectIndex = Mathf.Clamp(_currentCorrectIndex, 0, Mathf.Max(0, _spawnedButtons.Count - 1));
            HighlightCorrectOnly(safeCorrectIndex, dimOtherChoicesWhileFeedback);
        }

        if (_flowRoutine != null) StopCoroutine(_flowRoutine);
        _flowRoutine = StartCoroutine(NextQuestionAfterDelay());
    }

    private void FinishAssessment()
    {
        if (_finished) return;

        _finished = true;
        _running = false;

        StopAllRoutines();
        SetAllButtonsInteractable(false);
        ClearButtons();

        if (timerCircleImage != null) timerCircleImage.fillAmount = 1f;

        int total = _questions != null ? _questions.Count : 0;

        if (questionText) questionText.text = "Assessment Complete!";
        if (questionNumberText) questionNumberText.text = "";
        if (scoreText) scoreText.text = $"Score: {_score} / {total}";

        if (npcFeedback != null) npcFeedback.ResetToDefaultInstant();

        SaveHighScoreIfBetter();
        TryRefreshMenuScoreUi();

        if (autoCloseOnFinish)
            _autoCloseRoutine = StartCoroutine(WinThenReturnRoutine());
    }

    private IEnumerator WinThenReturnRoutine()
    {
        SetInputLocked(true);

        if (winRoot)
        {
            winRoot.SetActive(true);
            ForceEnableTree(winRoot);
        }

        if (winAnimator && !string.IsNullOrEmpty(winTrigger))
        {
            try { winAnimator.ResetTrigger(winTrigger); } catch { }
            try { winAnimator.SetTrigger(winTrigger); } catch { }
        }

        yield return new WaitForSecondsRealtime(Mathf.Max(0f, winHoldSeconds > 0 ? winHoldSeconds : autoCloseDelay));

        if (finishRoot != null && finishText != null)
            yield return StartCoroutine(FinishOverlayRoutine());

        TryUnlockFinishRewards();
        RestorePlayerControl();

        if (assessmentUI != null)
            assessmentUI.StopAssessmentMusicOnly();

        if (disableThisGameObjectOnFinish)
        {
            var t = (disableTargetOverride != null) ? disableTargetOverride : gameObject;
            if (t != null) t.SetActive(false);
        }

        TryRefreshMenuScoreUi();

        OnAssessmentClosed?.Invoke();

        if (forceUnpauseOnReturn)
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        if (forceHubObjectiveOnReturn && !string.IsNullOrEmpty(hubObjectiveIdOnReturn))
        {
            if (ObjectiveManager.Instance != null)
            {
                ObjectiveManager.Instance.ForceSetHubObjective(
                    hubObjectiveIdOnReturn,
                    playSfx: playObjectiveSfxOnApply,
                    applyOnNextHubLoadToo: true
                );
            }
            else
            {
                Debug.LogWarning("[AssessmentUIRoot] ObjectiveManager.Instance is null. Hub objective cannot be forced.");
            }
        }

        string targetReturnScene = null;

        if (HubMinigameReturnContext.hasData && !string.IsNullOrWhiteSpace(HubMinigameReturnContext.returnSceneName))
            targetReturnScene = HubMinigameReturnContext.returnSceneName;
        else if (MinigameReturnContext.hasData && !string.IsNullOrWhiteSpace(MinigameReturnContext.returnSceneName))
            targetReturnScene = MinigameReturnContext.returnSceneName;

        bool isReturningToHub =
            !string.IsNullOrWhiteSpace(targetReturnScene) &&
            !string.IsNullOrWhiteSpace(hubSceneName) &&
            string.Equals(targetReturnScene, hubSceneName, StringComparison.OrdinalIgnoreCase);

        if (isReturningToHub && _launchWantsHubSpawnOverride && !string.IsNullOrWhiteSpace(hubSpawnPointNameOnReturn))
        {
            ArmPendingHubSpawnByName(hubSceneName, playerTagForHubSpawn, hubSpawnPointNameOnReturn);
        }

        if (HubMinigameReturnContext.hasData && !string.IsNullOrWhiteSpace(HubMinigameReturnContext.returnSceneName))
        {
            HubMinigameReturnContext.ReturnToWorld();
            yield break;
        }

        if (MinigameReturnContext.hasData && !string.IsNullOrWhiteSpace(MinigameReturnContext.returnSceneName))
        {
            MinigameReturnContext.ReturnToWorld();
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(fallbackReturnSceneName))
        {
            SceneManager.LoadScene(fallbackReturnSceneName, LoadSceneMode.Single);
            yield break;
        }

        Debug.LogError("[AssessmentUIRoot] RETURN FAILED: No HubMinigameReturnContext, no MinigameReturnContext, AND no fallbackReturnSceneName set.");
    }

    private void TryUnlockFinishRewards()
    {
        if (!unlockRewardsOnFinish) return;
        if (requireMinScoreToUnlock && _score < minScoreToUnlock) return;

        if (RewardStateManager.Instance == null)
        {
            Debug.LogWarning("[AssessmentUIRoot] RewardStateManager.Instance is null. Rewards cannot be unlocked.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(chapterCompletionRewardId))
            RewardStateManager.Instance.MarkRewardPending(chapterCompletionRewardId);

        if (extraRewardIdsToUnlock != null)
        {
            for (int i = 0; i < extraRewardIdsToUnlock.Count; i++)
            {
                var id = extraRewardIdsToUnlock[i];
                if (string.IsNullOrWhiteSpace(id)) continue;
                RewardStateManager.Instance.MarkRewardPending(id);
            }
        }

        if (awardCompletionBadgeFirstTime && !string.IsNullOrWhiteSpace(assessmentCompleteRewardId))
        {
            bool already =
                RewardStateManager.Instance.IsRewardUnlocked(assessmentCompleteRewardId) ||
                RewardStateManager.Instance.IsRewardPending(assessmentCompleteRewardId);

            if (!already)
                RewardStateManager.Instance.MarkRewardPending(assessmentCompleteRewardId);
        }

        if (awardPerfectScoreBadge && !string.IsNullOrWhiteSpace(perfectScoreRewardId))
        {
            int total = _questions != null ? _questions.Count : 0;
            bool isPerfect = (total > 0 && _score >= total);

            if (isPerfect)
            {
                bool already =
                    RewardStateManager.Instance.IsRewardUnlocked(perfectScoreRewardId) ||
                    RewardStateManager.Instance.IsRewardPending(perfectScoreRewardId);

                if (!already)
                    RewardStateManager.Instance.MarkRewardPending(perfectScoreRewardId);
            }
        }
    }

    private void SetInputLocked(bool locked)
    {
        inputLocked = locked;
        SetAllButtonsInteractable(!inputLocked);

        if (!inputLocked && _running && !_finished && _timerRoutine == null)
        {
            _timerRoutine = StartCoroutine(TimerRoutine());

            if (autoReselectAfterCountdown)
                StartCoroutine(SelectFirstChoiceNextFrame());
        }
    }

    private IEnumerator StartCountdownGateRoutine()
    {
        SetInputLocked(true);

        if (countdownRoot == null || countdownText == null)
        {
            SetInputLocked(false);
            yield break;
        }

        countdownRoot.SetActive(true);
        ForceEnableTree(countdownRoot);

        var textGO = countdownText.gameObject;

        var cg = textGO.GetComponent<CanvasGroup>();
        if (cg == null) cg = textGO.AddComponent<CanvasGroup>();

        var rt = countdownText.rectTransform;

        float originalFontSize = countdownText.fontSize;
        bool originalAutoSize = countdownText.enableAutoSizing;
        TextAlignmentOptions originalAlign = countdownText.alignment;

        if (forceCenterAlignForCountdown)
            countdownText.alignment = TextAlignmentOptions.Center;

        countdownText.enableAutoSizing = false;

        float mult = Mathf.Max(0.01f, countdownFontSizeMultiplier);
        countdownText.fontSize = originalFontSize * mult;

        Vector3 baseScale = (rt != null) ? rt.localScale : Vector3.one;

        for (int n = 3; n >= 1; n--)
        {
            countdownText.text = n.ToString();
            yield return AnimateOverlayInOut(cg, rt, baseScale, countdownStepSeconds);
        }

        countdownText.text = string.IsNullOrWhiteSpace(countdownStartText) ? "START" : countdownStartText;
        yield return AnimateOverlayInOut(cg, rt, baseScale, Mathf.Max(0.05f, countdownStartHoldSeconds));

        countdownText.fontSize = originalFontSize;
        countdownText.enableAutoSizing = originalAutoSize;
        countdownText.alignment = originalAlign;

        countdownRoot.SetActive(false);
        SetInputLocked(false);
    }

    private IEnumerator FinishOverlayRoutine()
    {
        if (finishRoot == null || finishText == null) yield break;

        finishRoot.SetActive(true);
        ForceEnableTree(finishRoot);

        var textGO = finishText.gameObject;

        var cg = textGO.GetComponent<CanvasGroup>();
        if (cg == null) cg = textGO.AddComponent<CanvasGroup>();

        var rt = finishText.rectTransform;

        float originalFontSize = finishText.fontSize;
        bool originalAutoSize = finishText.enableAutoSizing;
        TextAlignmentOptions originalAlign = finishText.alignment;

        if (forceCenterAlignForCountdown)
            finishText.alignment = TextAlignmentOptions.Center;

        finishText.enableAutoSizing = false;

        float mult = Mathf.Max(0.01f, finishFontSizeMultiplier);
        finishText.fontSize = originalFontSize * mult;

        Vector3 baseScale = (rt != null) ? rt.localScale : Vector3.one;

        finishText.text = string.IsNullOrWhiteSpace(finishMessage) ? "FINISHED!" : finishMessage;

        yield return AnimateOverlayInOut(cg, rt, baseScale, Mathf.Max(0.05f, finishHoldSeconds));

        finishText.fontSize = originalFontSize;
        finishText.enableAutoSizing = originalAutoSize;
        finishText.alignment = originalAlign;

        finishRoot.SetActive(false);
    }

    private IEnumerator AnimateOverlayInOut(CanvasGroup cg, RectTransform rt, Vector3 baseScale, float holdSeconds)
    {
        float inDur = 0.18f;
        float outDur = 0.18f;

        yield return AnimateOverlay(cg, rt, baseScale, 0f, 1f, countdownScaleMin, countdownScaleMax, inDur);
        yield return new WaitForSeconds(Mathf.Max(0.01f, holdSeconds));
        yield return AnimateOverlay(cg, rt, baseScale, 1f, 0f, countdownScaleMax, countdownScaleMin, outDur);
    }

    private IEnumerator AnimateOverlay(CanvasGroup cg, RectTransform rt, Vector3 baseScale, float a0, float a1, float s0, float s1, float dur)
    {
        if (cg != null) cg.alpha = a0;
        if (rt != null) rt.localScale = baseScale * s0;

        float t = 0f;
        dur = Mathf.Max(0.01f, dur);

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);
            float e = countdownEase != null ? countdownEase.Evaluate(u) : u;

            if (cg != null) cg.alpha = Mathf.LerpUnclamped(a0, a1, e);
            if (rt != null) rt.localScale = baseScale * Mathf.LerpUnclamped(s0, s1, e);

            yield return null;
        }

        if (cg != null) cg.alpha = a1;
        if (rt != null) rt.localScale = baseScale * s1;
    }

    private Button FindUnityButton(ChoiceButtonUI btn)
    {
        if (btn == null) return null;
        var b = btn.GetComponent<Button>();
        if (b != null) return b;
        return btn.GetComponentInChildren<Button>(true);
    }

    private void BackupButtonVisual(Button b)
    {
        if (b == null) return;
        if (_btnBackup.ContainsKey(b)) return;

        _btnBackup[b] = new ButtonVisualBackup
        {
            transition = b.transition,
            colors = b.colors,
            targetGraphic = b.targetGraphic
        };
    }

    private void RestoreButtonVisual(Button b)
    {
        if (b == null) return;
        if (!_btnBackup.TryGetValue(b, out var bak)) return;

        b.transition = bak.transition;
        b.colors = bak.colors;

        if (bak.targetGraphic != null)
            b.targetGraphic = bak.targetGraphic;

        _btnBackup.Remove(b);
    }

    private void LockButtonToColor(Button b, Color c)
    {
        if (b == null) return;

        BackupButtonVisual(b);

        var cb = b.colors;
        cb.normalColor = c;
        cb.highlightedColor = c;
        cb.pressedColor = c;
        cb.selectedColor = c;
        cb.disabledColor = c;
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0f;
        b.colors = cb;

        if (b.targetGraphic != null)
            b.targetGraphic.color = c;
    }

    private Color GetChoiceVisualColor(ChoiceButtonUI btn)
    {
        if (btn == null) return Color.white;

        var unityBtn = FindUnityButton(btn);
        if (unityBtn != null && unityBtn.targetGraphic != null)
            return unityBtn.targetGraphic.color;

        var img = btn.GetComponentInChildren<Image>(true);
        if (img != null) return img.color;

        var tmp = btn.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null) return tmp.color;

        return Color.white;
    }

    private void SetChoiceVisualColor(ChoiceButtonUI btn, Color c, bool forceAlphaOne)
    {
        if (btn == null) return;

        if (forceAlphaOne) c.a = 1f;

        var imgs = btn.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < imgs.Length; i++)
        {
            if (imgs[i] == null) continue;
            imgs[i].color = c;
        }

        var unityBtn = FindUnityButton(btn);
        if (unityBtn != null && unityBtn.targetGraphic != null)
            unityBtn.targetGraphic.color = c;
    }

    private void ApplyChoiceFeedback(int clickedIndex, int correctIndex, bool clickedWasCorrect, bool dimOthers)
    {
        for (int i = 0; i < _spawnedButtons.Count; i++)
        {
            var b = _spawnedButtons[i];
            if (b == null) continue;

            var unityBtn = FindUnityButton(b);

            if (i == clickedIndex)
            {
                Color col = clickedWasCorrect ? correctChoiceColor : wrongChoiceColor;
                SetChoiceVisualColor(b, col, forceAlphaOne: true);

                if (unityBtn != null) LockButtonToColor(unityBtn, col);
            }
            else
            {
                if (dimOthers)
                {
                    var baseCol = GetChoiceVisualColor(b);
                    baseCol.a = Mathf.Clamp01(dimAlpha);
                    SetChoiceVisualColor(b, baseCol, forceAlphaOne: false);

                    if (unityBtn != null)
                    {
                        var lockCol = baseCol;
                        lockCol.a = 1f;
                        LockButtonToColor(unityBtn, lockCol);
                    }
                }
            }
        }
    }

    private void HighlightCorrectOnly(int correctIndex, bool dimOthers)
    {
        for (int i = 0; i < _spawnedButtons.Count; i++)
        {
            var b = _spawnedButtons[i];
            if (b == null) continue;

            var unityBtn = FindUnityButton(b);

            if (i == correctIndex)
            {
                SetChoiceVisualColor(b, correctChoiceColor, forceAlphaOne: true);
                if (unityBtn != null) LockButtonToColor(unityBtn, correctChoiceColor);
            }
            else
            {
                if (dimOthers)
                {
                    var baseCol = GetChoiceVisualColor(b);
                    baseCol.a = Mathf.Clamp01(dimAlpha);
                    SetChoiceVisualColor(b, baseCol, forceAlphaOne: false);

                    if (unityBtn != null)
                    {
                        var lockCol = baseCol;
                        lockCol.a = 1f;
                        LockButtonToColor(unityBtn, lockCol);
                    }
                }
            }
        }
    }

    private void SaveHighScoreIfBetter()
    {
        string key = ValidateOrFallbackHighScoreKey(activeHighScoreKey);
        if (string.IsNullOrWhiteSpace(key)) return;

        int total = (_questions != null) ? _questions.Count : 0;

        if (AssessmentScoreManager.Instance != null)
        {
            AssessmentScoreManager.Instance.RecordScore(key, _score, total);
        }

        if (saveHighScoreIntoSaveFile && SaveGameManager.Instance != null)
        {
            TryRecordAssessmentScoreSafe(
                manager: SaveGameManager.Instance,
                assessmentId: key,
                score: _score,
                totalQuestions: total,
                patchIntoSlotJson: true
            );
        }

        if (alsoWriteLegacyPlayerPrefsHighScore)
        {
            string prefKey = $"ASSESS_HIGHSCORE_{key}";
            int best = PlayerPrefs.GetInt(prefKey, -1);

            if (_score > best)
            {
                PlayerPrefs.SetInt(prefKey, _score);
                PlayerPrefs.Save();
                Debug.Log($"[AssessmentUIRoot] (Legacy) High score updated: {key} = {_score}");
            }
        }
    }

    private bool TryRecordAssessmentScoreSafe(object manager, string assessmentId, int score, int totalQuestions, bool patchIntoSlotJson)
    {
        if (manager == null) return false;

        var t = manager.GetType();

        string[] methodNames =
        {
            "RecordAssessmentScore",
            "SaveAssessmentScore",
            "SetAssessmentScore",
            "RecordAssessScore"
        };

        foreach (var name in methodNames)
        {
            var mi = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi == null) continue;

            var ps = mi.GetParameters();

            try
            {
                if (ps.Length == 4 &&
                    ps[0].ParameterType == typeof(string) &&
                    ps[1].ParameterType == typeof(int) &&
                    ps[2].ParameterType == typeof(int) &&
                    ps[3].ParameterType == typeof(bool))
                {
                    mi.Invoke(manager, new object[] { assessmentId, score, totalQuestions, patchIntoSlotJson });
                    return true;
                }

                if (ps.Length == 3 &&
                    ps[0].ParameterType == typeof(string) &&
                    ps[1].ParameterType == typeof(int) &&
                    ps[2].ParameterType == typeof(int))
                {
                    mi.Invoke(manager, new object[] { assessmentId, score, totalQuestions });
                    return true;
                }

                if (ps.Length == 4 &&
                    ps[0].ParameterType == typeof(string) &&
                    ps[1].ParameterType == typeof(int) &&
                    ps[2].ParameterType == typeof(int) &&
                    ps[3].ParameterType == typeof(int))
                {
                    mi.Invoke(manager, new object[] { assessmentId, score, totalQuestions, score });
                    return true;
                }
            }
            catch
            {
            }
        }

        Debug.LogWarning($"[AssessmentUIRoot] Could not call SaveGameManager.RecordAssessmentScore (no compatible method found).");
        return false;
    }

    private string ValidateOrFallbackHighScoreKey(string key)
    {
        if (supportedHighScoreKeys == null || supportedHighScoreKeys.Count == 0)
            return string.IsNullOrWhiteSpace(key) ? "" : key.Trim();

        if (string.IsNullOrWhiteSpace(key))
            return supportedHighScoreKeys[0];

        key = key.Trim();

        for (int i = 0; i < supportedHighScoreKeys.Count; i++)
        {
            if (string.Equals(supportedHighScoreKeys[i], key, StringComparison.OrdinalIgnoreCase))
                return supportedHighScoreKeys[i];
        }

        Debug.LogWarning($"[AssessmentUIRoot] activeHighScoreKey '{key}' is not in supportedHighScoreKeys. Falling back to '{supportedHighScoreKeys[0]}'.");
        return supportedHighScoreKeys[0];
    }

    private void UpdateScoreText()
    {
        if (scoreText != null)
            scoreText.text = $"Score: {_score}";
    }

    private void ClearButtons()
    {
        if (_btnBackup.Count > 0)
        {
            var keys = new List<Button>(_btnBackup.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                var b = keys[i];
                if (b != null) RestoreButtonVisual(b);
            }
            _btnBackup.Clear();
        }

        for (int i = 0; i < _spawnedButtons.Count; i++)
        {
            if (_spawnedButtons[i] != null)
                Destroy(_spawnedButtons[i].gameObject);
        }

        _spawnedButtons.Clear();
        _originalChoiceColors.Clear();

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void SetAllButtonsInteractable(bool value)
    {
        if (inputLocked) value = false;

        for (int i = 0; i < _spawnedButtons.Count; i++)
        {
            if (_spawnedButtons[i] != null)
                _spawnedButtons[i].SetInteractable(value);
        }
    }

    private string NormalizeJsonFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        name = name.Trim();

        if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            name += ".json";

        return name;
    }

    private IEnumerator LoadJsonFromStreamingAssets()
    {
        _data = null;

        jsonFileName = NormalizeJsonFileName(jsonFileName);
        string path = Path.Combine(Application.streamingAssetsPath, jsonFileName);

#if UNITY_ANDROID && !UNITY_EDITOR
        using (UnityWebRequest req = UnityWebRequest.Get(path))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[AssessmentUIRoot] Failed to load JSON.\nFile: {jsonFileName}\nPath: {path}\nError: {req.error}");
                yield break;
            }

            string json = req.downloadHandler.text;
            _data = JsonUtility.FromJson<AssessmentQuestionList>(json);

            if (_data == null || _data.questions == null)
                Debug.LogError($"[AssessmentUIRoot] JSON parsed but data/questions is null.\nFile: {jsonFileName}\nPath: {path}\n(Check root format: {{ \"questions\": [ ... ] }})");
        }
#else
        if (!File.Exists(path))
        {
            Debug.LogError($"[AssessmentUIRoot] JSON not found.\nFile: {jsonFileName}\nPath: {path}\nMake sure it's in Assets/StreamingAssets/ and the name matches exactly (case-sensitive on Android).");
            yield break;
        }

        string json = File.ReadAllText(path);
        _data = JsonUtility.FromJson<AssessmentQuestionList>(json);

        if (_data == null || _data.questions == null)
            Debug.LogError($"[AssessmentUIRoot] JSON parsed but data/questions is null.\nFile: {jsonFileName}\nPath: {path}\n(Check root format: {{ \"questions\": [ ... ] }})");

        yield return null;
#endif
    }

    private IEnumerator Wait(float seconds)
    {
        if (seconds <= 0f) yield break;
        if (useUnscaledTime) yield return new WaitForSecondsRealtime(seconds);
        else yield return new WaitForSeconds(seconds);
    }

    private static void Shuffle<T>(IList<T> list)
    {
        if (list == null || list.Count <= 1) return;

        for (int i = 0; i < list.Count; i++)
        {
            int r = UnityEngine.Random.Range(i, list.Count);
            (list[i], list[r]) = (list[r], list[i]);
        }
    }

    private static void ForceEnableTree(GameObject root)
    {
        if (!root) return;

        if (!root.activeSelf) root.SetActive(true);

        var t = root.transform;
        for (int i = 0; i < t.childCount; i++)
        {
            var child = t.GetChild(i).gameObject;
            if (!child.activeSelf) child.SetActive(true);
            ForceEnableTree(child);
        }

        var cgs = root.GetComponentsInChildren<CanvasGroup>(true);
        foreach (var cg in cgs)
        {
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;
        }

        var canvases = root.GetComponentsInChildren<Canvas>(true);
        foreach (var c in canvases) c.enabled = true;

        var images = root.GetComponentsInChildren<Image>(true);
        foreach (var img in images) img.enabled = true;

        var tmps = root.GetComponentsInChildren<TMP_Text>(true);
        foreach (var tmp in tmps) tmp.enabled = true;
    }

    private void TryRefreshMenuScoreUi()
    {
        if (!refreshMenuScoreUiOnFinish) return;
        if (menuScoreUi == null) return;

        string method = string.IsNullOrWhiteSpace(menuScoreUiRefreshMethodName) ? "Refresh" : menuScoreUiRefreshMethodName.Trim();

        try
        {
            var t = menuScoreUi.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var m = t.GetMethod(method, flags);
            if (m == null) return;
            if (m.GetParameters().Length != 0) return;
            m.Invoke(menuScoreUi, null);
        }
        catch
        {
        }
    }

    public void ForceFinishNow()
    {
        if (_finished) return;
        FinishAssessment();
    }
}