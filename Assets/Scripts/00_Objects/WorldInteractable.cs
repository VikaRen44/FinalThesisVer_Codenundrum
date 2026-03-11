using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Collider))]
public class WorldInteractable : MonoBehaviour
{
    [Header("ID (save key)")]
    public string interactableId = "Interactable_001";

    [Header("Dialogue (classic)")]
    public SimpleDialogueSequenceSO firstTimeDialogue;
    public SimpleDialogueSequenceSO genericDialogue;

    [Header("First-time behaviour")]
    public bool markUsedAfterFirstInteraction = true;

    [Header("Prompt UI (optional)")]
    [Tooltip("Optional: a small world prompt object (ex: 'Press E'). Leave NONE if you don't use it.")]
    public GameObject promptObject;

    [Header("Interaction (New Input System)")]
    public InputActionReference interactAction;

    [Header("Player Detection")]
    public string playerTag = "Player";

    [Header("Optional")]
    public bool blockWhileDialoguePlaying = true;

    [Header("Debug")]
    public bool verboseLogs = true;

    // ✅ NEW: per-interactable toggle to hide the dialogue "PressE / Continue" object
    [Header("Dialogue Visual")]
    [Tooltip("If true, hides DialogueUI's PressE/Continue icon ONLY for this interactable while its dialogue/prompt is active.")]
    public bool hidePressContinueIcon = false;

    // ✅ NEW: optional objective gate
    [Header("Objective Gate (Optional)")]
    [Tooltip("If set, this interactable will only stay active while this objective is the CURRENT active objective. Leave empty for normal behavior.")]
    public string requiredActiveObjectiveId = "";

    // ---------------- CHAPTER PORTAL MODE ----------------
    [Header("CHAPTER PORTAL (optional)")]
    [Tooltip("If true, entering this trigger will prompt a Yes/No to enter a chapter scene.")]
    public bool isChapterPortal = false;

    [Tooltip("Shown as the dialogue title/speaker.")]
    public string chapterTitle = "Chapter Title";

    [Tooltip("Scene name to load when player chooses YES. Must be added in Build Settings.")]
    public string targetSceneName = "";

    [Tooltip("Message shown under the title.")]
    [TextArea(2, 5)]
    public string portalMessage = "Would you like to enter this chapter?";

    [Tooltip("If true, prompt triggers immediately on entering range (no interact press).")]
    public bool autoPromptOnEnter = true;

    [Tooltip("If true, portal prompt won't repeat until player exits trigger and re-enters.")]
    public bool requireExitToRePrompt = true;

    [Tooltip("If true, will load using SceneTransition.LoadSceneWhite() (ONE-SHOT WHITE, does not affect other loads).")]
    public bool forceWhiteFade = true;

    // ✅ NEW: Hub pose save (NO PlayerPrefs)
    [Header("HUB RETURN POSE (NO PlayerPrefs)")]
    [Tooltip("If set, and targetSceneName equals your chapter scene, we save current HUB player pose before leaving.")]
    public bool saveHubPoseBeforeLeaving = true;

    [Tooltip("Your hub scene name used by LoadCharacter hub pose system.")]
    public string hubSceneNameForPose = "04_Gamehub";

    [Tooltip("Your chapter scene name. If targetSceneName matches this, we treat it as leaving hub to chapter.")]
    public string chapterSceneNameForPose = "05_Chapter1";

    // ---------------- internals ----------------
    private bool _playerInRange;
    private bool _alreadyUsed;
    private bool _interactRoutineRunning;

    private InputAction _cachedInteract;
    private Collider _col;

    private const string PREFS_PREFIX = "WI_USED_";

    // portal internal
    private bool _portalPromptedThisEnter;

    // ✅ continue icon tracking (so we can restore safely)
    private GameObject _pressContinueGO;
    private bool _pressContinueWasActive = true;
    private Coroutine _restorePressCoroutine;

    // ✅ NEW: objective gate state tracking
    private bool _objectiveGateLastAllowed = true;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    private void Awake()
    {
        _col = GetComponent<Collider>();
        if (_col != null) _col.isTrigger = true;

        _cachedInteract = (interactAction != null) ? interactAction.action : null;

        if (promptObject != null)
            promptObject.SetActive(false);
    }

    private void Start()
    {
        _alreadyUsed = LoadUsed();

        if (_cachedInteract != null && !_cachedInteract.enabled)
            _cachedInteract.Enable();

        ApplyObjectiveGateImmediate();

        if (verboseLogs)
        {
            Debug.Log($"[WorldInteractable] '{name}' Start() " +
                      $"portal={isChapterPortal} autoPrompt={autoPromptOnEnter} targetScene='{targetSceneName}' " +
                      $"interactAction='{(_cachedInteract != null ? _cachedInteract.name : "NULL")}' " +
                      $"requiredObjective='{requiredActiveObjectiveId}'");
        }
    }

    private void OnEnable()
    {
        _cachedInteract = (interactAction != null) ? interactAction.action : _cachedInteract;
        if (_cachedInteract != null && !_cachedInteract.enabled)
            _cachedInteract.Enable();

        ApplyObjectiveGateImmediate();
    }

    private void OnDisable()
    {
        _playerInRange = false;

        if (promptObject != null)
            promptObject.SetActive(false);

        _portalPromptedThisEnter = false;

        // ✅ safety: restore icon if we hid it
        RestorePressContinueImmediate();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        if (!IsAllowedByObjectiveGate()) return;

        _playerInRange = true;

        if (promptObject != null)
            promptObject.SetActive(true);

        if (verboseLogs)
            Debug.Log($"[WorldInteractable] '{name}': Player entered range.");

        if (isChapterPortal && autoPromptOnEnter)
            TryOpenChapterPrompt();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        _playerInRange = false;

        if (promptObject != null)
            promptObject.SetActive(false);

        if (verboseLogs)
            Debug.Log($"[WorldInteractable] '{name}': Player left range.");

        if (isChapterPortal && requireExitToRePrompt)
            _portalPromptedThisEnter = false;
    }

    private void Update()
    {
        ApplyObjectiveGateImmediate();

        if (!IsAllowedByObjectiveGate()) return;
        if (!_playerInRange) return;

        var mgr = GetDialogueManager();
        if (blockWhileDialoguePlaying && mgr != null && mgr.IsPlaying)
            return;

        if (isChapterPortal && !autoPromptOnEnter)
        {
            bool pressed = (_cachedInteract != null && _cachedInteract.enabled && _cachedInteract.WasPressedThisFrame());
            if (pressed && !_interactRoutineRunning)
                StartCoroutine(PortalPromptAfterRelease());
            return;
        }

        if (!isChapterPortal)
        {
            bool pressed = (_cachedInteract != null && _cachedInteract.enabled && _cachedInteract.WasPressedThisFrame());
            if (pressed && !_interactRoutineRunning)
                StartCoroutine(InteractAfterRelease());
        }
    }

    private IEnumerator PortalPromptAfterRelease()
    {
        _interactRoutineRunning = true;
        yield return null;

        if (_cachedInteract != null && _cachedInteract.enabled)
        {
            while (_cachedInteract.IsPressed())
                yield return null;
        }

        if (IsAllowedByObjectiveGate())
            TryOpenChapterPrompt();

        _interactRoutineRunning = false;
    }

    // ✅ NEW: safer manager getter (fixes your scene-based manager switching)
    private SimpleDialogueManager GetDialogueManager()
    {
        // Preferred: global instance (if you maintain it)
        if (SimpleDialogueManager.Instance != null)
            return SimpleDialogueManager.Instance;

        // Fallback: find one that exists in THIS scene
#if UNITY_2023_1_OR_NEWER
        var all = Object.FindObjectsByType<SimpleDialogueManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
        var all = Object.FindObjectsOfType<SimpleDialogueManager>(true);
#endif
        if (all == null || all.Length == 0) return null;

        string activeScene = SceneManager.GetActiveScene().name;

        // Choose one that is in the active scene (not DontDestroy, not disabled)
        for (int i = 0; i < all.Length; i++)
        {
            var m = all[i];
            if (m == null) continue;
            if (!m.isActiveAndEnabled) continue;

            if (m.gameObject.scene.name == activeScene)
                return m;
        }

        // Last resort: return any enabled one
        for (int i = 0; i < all.Length; i++)
        {
            var m = all[i];
            if (m != null && m.isActiveAndEnabled)
                return m;
        }

        return null;
    }

    // =========================================================
    // ✅ NEW: Objective Gate Handling
    // =========================================================
    private bool IsAllowedByObjectiveGate()
    {
        if (string.IsNullOrWhiteSpace(requiredActiveObjectiveId))
            return true;

        if (ObjectiveManager.Instance == null)
            return false;

        return ObjectiveManager.Instance.IsObjectiveActive(requiredActiveObjectiveId);
    }

    private void ApplyObjectiveGateImmediate()
    {
        bool allowed = IsAllowedByObjectiveGate();

        if (_objectiveGateLastAllowed == allowed && gameObject.activeSelf == allowed)
        {
            if (promptObject != null && !allowed && promptObject.activeSelf)
                promptObject.SetActive(false);
            return;
        }

        _objectiveGateLastAllowed = allowed;

        if (!allowed)
        {
            _playerInRange = false;

            if (promptObject != null)
                promptObject.SetActive(false);

            RestorePressContinueImmediate();

            if (verboseLogs)
                Debug.Log($"[WorldInteractable] '{name}': Disabled by objective gate. Required active objective = '{requiredActiveObjectiveId}'");

            if (gameObject.activeSelf)
                gameObject.SetActive(false);
        }
        else
        {
            if (verboseLogs && !gameObject.activeSelf)
                Debug.Log($"[WorldInteractable] '{name}': Enabled by objective gate. Active objective matched '{requiredActiveObjectiveId}'");

            if (!gameObject.activeSelf)
                gameObject.SetActive(true);
        }
    }

    // =========================================================
    // ✅ Press Continue Icon Handling (per interactable)
    // =========================================================
    private void CachePressContinueGO(SimpleDialogueManager mgr)
    {
        _pressContinueGO = null;

        if (mgr == null || mgr.ui == null) return;

        // Your hierarchy (from screenshot) is:
        // DialogueUI
        //  └ DialogueRoot
        //      └ DialoguePanel
        //          └ PressE
        //
        // We'll try a couple safe paths:
        Transform t = null;

        t = mgr.ui.transform.Find("DialogueRoot/DialoguePanel/PressE");
        if (t == null) t = mgr.ui.transform.Find("DialoguePanel/PressE");
        if (t == null) t = mgr.ui.transform.Find("PressE");

        if (t != null)
        {
            _pressContinueGO = t.gameObject;
            _pressContinueWasActive = _pressContinueGO.activeSelf;
        }
        else if (verboseLogs)
        {
            Debug.LogWarning("[WorldInteractable] Could not find PressE object under mgr.ui. Check hierarchy/path.");
        }
    }

    private void HidePressContinueIfNeeded(SimpleDialogueManager mgr)
    {
        if (!hidePressContinueIcon) return;

        CachePressContinueGO(mgr);

        if (_pressContinueGO != null)
            _pressContinueGO.SetActive(false);

        // Start restore watcher
        if (_restorePressCoroutine != null) StopCoroutine(_restorePressCoroutine);
        _restorePressCoroutine = StartCoroutine(RestorePressContinueAfterDialogue());
    }

    private IEnumerator RestorePressContinueAfterDialogue()
    {
        var mgr = GetDialogueManager();
        if (mgr == null) yield break;

        // wait until dialogue finishes
        while (mgr.IsPlaying)
            yield return null;

        RestorePressContinueImmediate();
    }

    private void RestorePressContinueImmediate()
    {
        if (_restorePressCoroutine != null)
        {
            StopCoroutine(_restorePressCoroutine);
            _restorePressCoroutine = null;
        }

        if (_pressContinueGO != null)
        {
            // restore to its previous state
            _pressContinueGO.SetActive(_pressContinueWasActive);
        }

        _pressContinueGO = null;
        _pressContinueWasActive = true;
    }

    // =========================================================

    private void TryOpenChapterPrompt()
    {
        if (!isChapterPortal) return;
        if (!IsAllowedByObjectiveGate()) return;

        if (requireExitToRePrompt && _portalPromptedThisEnter)
        {
            if (verboseLogs)
                Debug.Log($"[WorldInteractable] '{name}': Portal prompt blocked (already prompted this enter).");
            return;
        }

        var mgr = GetDialogueManager();
        if (mgr == null)
        {
            Debug.LogWarning($"[WorldInteractable] '{name}': No SimpleDialogueManager found in active scene.");
            return;
        }

        if (string.IsNullOrEmpty(targetSceneName))
        {
            Debug.LogWarning($"[WorldInteractable] '{name}': targetSceneName is EMPTY.");
            return;
        }

        _portalPromptedThisEnter = true;

        if (verboseLogs)
            Debug.Log($"[WorldInteractable] '{name}': Opening chapter prompt for scene '{targetSceneName}'.");

        bool started = mgr.TryStartYesNoPrompt(
            chapterTitle,
            portalMessage,
            "Yes",
            "No",
            onYes: () =>
            {
                Debug.Log($"[WorldInteractable] YES clicked for portal '{name}'. Loading scene='{targetSceneName}'");

                Time.timeScale = 1f;
                Physics.SyncTransforms();

                // ✅ Save HUB pose in-memory BEFORE leaving to chapter scene
                if (saveHubPoseBeforeLeaving &&
                    !string.IsNullOrEmpty(chapterSceneNameForPose) &&
                    string.Equals(targetSceneName, chapterSceneNameForPose, System.StringComparison.Ordinal))
                {
                    var playerGo = GameObject.FindGameObjectWithTag(playerTag);
                    if (playerGo != null)
                    {
                        LoadCharacter.SaveHubPoseNow(playerGo.transform, hubSceneNameForPose);
                        if (verboseLogs) Debug.Log("[WorldInteractable] ✅ Saved hub pose before leaving to chapter.");
                    }
                    else
                    {
                        Debug.LogWarning("[WorldInteractable] Could not find Player to save hub pose.");
                    }
                }

                if (SceneTransition.Instance != null)
                {
                    if (forceWhiteFade)
                    {
                        Debug.Log("[WorldInteractable] Using SceneTransition.Instance.LoadSceneWhite()");
                        SceneTransition.Instance.LoadSceneWhite(targetSceneName);
                    }
                    else
                    {
                        Debug.Log("[WorldInteractable] Using SceneTransition.Instance.LoadScene()");
                        SceneTransition.Instance.LoadScene(targetSceneName);
                    }
                }
                else
                {
                    Debug.LogWarning("[WorldInteractable] SceneTransition.Instance is NULL. Using SceneManager.LoadScene fallback.");
                    SceneManager.LoadScene(targetSceneName);
                }
            },
            onNo: () =>
            {
                Debug.Log($"[WorldInteractable] NO clicked for portal '{name}'.");

                if (!requireExitToRePrompt)
                    _portalPromptedThisEnter = false;
            },
            useTyping: true
        );

        if (verboseLogs)
            Debug.Log($"[WorldInteractable] '{name}': TryStartYesNoPrompt => {started}");

        // ✅ hide icon for this prompt only (if dialogue started)
        if (started)
            HidePressContinueIfNeeded(mgr);
    }

    private IEnumerator InteractAfterRelease()
    {
        _interactRoutineRunning = true;
        yield return null;

        if (_cachedInteract != null && _cachedInteract.enabled)
        {
            while (_cachedInteract.IsPressed())
                yield return null;
        }

        if (IsAllowedByObjectiveGate())
            InteractClassic();

        _interactRoutineRunning = false;
    }

    private void InteractClassic()
    {
        if (!IsAllowedByObjectiveGate()) return;

        var mgr = GetDialogueManager();
        if (mgr == null)
        {
            Debug.LogWarning("[WorldInteractable] No SimpleDialogueManager found in active scene.");
            return;
        }

        SimpleDialogueSequenceSO seqToPlay = null;

        if (_alreadyUsed)
            seqToPlay = (genericDialogue != null) ? genericDialogue : firstTimeDialogue;
        else
            seqToPlay = (firstTimeDialogue != null) ? firstTimeDialogue : genericDialogue;

        if (seqToPlay == null)
        {
            Debug.LogWarning($"[WorldInteractable] '{name}': No dialogue assigned.");
            return;
        }

        bool started = mgr.TryStartDialogue(seqToPlay);
        if (verboseLogs)
            Debug.Log($"[WorldInteractable] '{name}': TryStartDialogue('{seqToPlay.name}') => {started}");

        if (!started) return;

        // ✅ hide icon for this dialogue only
        HidePressContinueIfNeeded(mgr);

        if (!_alreadyUsed && markUsedAfterFirstInteraction)
        {
            _alreadyUsed = true;
            SaveUsed(true);
        }
    }

    private bool LoadUsed()
    {
        if (string.IsNullOrEmpty(interactableId)) return false;
        return PlayerPrefs.GetInt(PREFS_PREFIX + interactableId, 0) == 1;
    }

    private void SaveUsed(bool used)
    {
        if (string.IsNullOrEmpty(interactableId)) return;
        PlayerPrefs.SetInt(PREFS_PREFIX + interactableId, used ? 1 : 0);
        PlayerPrefs.Save();
    }
}