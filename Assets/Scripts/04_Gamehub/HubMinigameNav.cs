using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Collider))]
public class HubMinigameNavigatorUI : MonoBehaviour
{
    [Serializable]
    public class MinigameButton
    {
        [Header("UI + Scene")]
        public Button button;
        public string sceneName;

        [TextArea(2, 4)]
        public string confirmMessage = "Enter this minigame?";

        public bool forceWhiteFade = true;

        [Header("Assessment Overrides (Optional)")]
        public bool useAssessmentOverrides = false;

        public string assessmentJsonFileName = "";
        public string completeAssessmentRewardId = "";
        public string perfectAssessmentRewardId = "";
        public string chapterCompletionRewardId = "";

        [Tooltip("This is the KEY used to store/read score (ex: CH1_ASSESS, CH2_ASSESS).")]
        public string assessmentHighScoreKey = "CH1_ASSESS";

        [Header("Assessment -> Hub Spawn Override (Optional)")]
        public bool useHubSpawnOverrideOnReturn = false;
        public string hubSpawnPointNameOnReturn = "";
    }

    [Header("Prompt UI (like SavePoint / WorldInteractable)")]
    public GameObject promptObject;

    [Header("Interaction (New Input System)")]
    public InputActionReference interactAction;

    public bool blockDuringDialogue = true;
    public string playerTag = "Player";

    [Header("Root")]
    public GameObject root;

    [Header("Title")]
    public TMP_Text titleText;
    public string title = "Minigames";

    [Header("Buttons")]
    public Button closeButton;
    public GameObject firstSelectedOnOpen;

    [Header("Minigame Buttons (manual drag & drop)")]
    public List<MinigameButton> minigames = new List<MinigameButton>();

    [Header("Confirm Prompt")]
    public string confirmTitle = "A Memory Worth Saving";
    public string yesLabel = "Yes";
    public string noLabel = "No";

    [Header("Dialogue UI Override")]
    [Tooltip("Optional: assign the Dialogue UI PressE object so it stays hidden during the confirm prompt.")]
    public GameObject dialoguePressEIndicator;

    [Header("Hub Return")]
    public string hubSceneName = "04_Gamehub";
    public string hubSceneNameForPose = "04_Gamehub";
    public bool saveHubPoseBeforeLeaving = true;

    [Header("Freeze Like Save Menu (Time.timeScale)")]
    public bool freezeWorldWithTimeScale = true;

    private float _prevTimeScale = 1f;
    private bool _timeScaleCaptured = false;

    [Header("Optional Extra Lock (Action Map Swap)")]
    public bool alsoSwapActionMap = true;

    public PlayerInput playerInput;
    public string gameplayActionMapName = "Player";
    public string uiActionMapName = "UI";

    private bool _playerInRange;
    private bool _isOpen;

    private string _prevActionMap = "";
    private bool _cachedPrevActionMap = false;

    private bool _dialoguePressEWasActiveBeforePrompt = false;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    private void Awake()
    {
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;

        if (root) root.SetActive(false);
        if (promptObject) promptObject.SetActive(false);
    }

    private void Start()
    {
        WireButtons();
    }

    private void OnEnable()
    {
        if (interactAction != null && interactAction.action != null)
            interactAction.action.Enable();
    }

    private void OnDisable()
    {
        if (interactAction != null && interactAction.action != null)
            interactAction.action.Disable();

        _playerInRange = false;
        if (promptObject) promptObject.SetActive(false);

        RestoreDialoguePressEIndicator();

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

    private void ForceCloseAndRestore()
    {
        _isOpen = false;

        if (root) root.SetActive(false);

        RestoreTimeScaleIfWePaused();
        RestoreActionMapIfWeSwapped();
        RestoreDialoguePressEIndicator();

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

        for (int i = 0; i < minigames.Count; i++)
        {
            int idx = i;
            var entry = minigames[idx];
            if (entry.button == null) continue;

            entry.button.onClick.RemoveAllListeners();
            entry.button.onClick.AddListener(() => TryOpenConfirm(idx));
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        _playerInRange = true;

        if (promptObject)
            promptObject.SetActive(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        _playerInRange = false;

        if (promptObject)
            promptObject.SetActive(false);
    }

    private void Update()
    {
        if (!_playerInRange) return;

        if (blockDuringDialogue && SimpleDialogueManager.Instance != null && SimpleDialogueManager.Instance.IsPlaying)
            return;

        if (interactAction == null || interactAction.action == null) return;

        if (interactAction.action.WasPressedThisFrame())
        {
            if (_isOpen) CloseMenu();
            else OpenMenu();
        }
    }

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

    private PlayerInput ResolvePlayerInput()
    {
        if (playerInput) return playerInput;

        var playerGo = GameObject.FindGameObjectWithTag(playerTag);
        if (playerGo)
        {
            playerInput = playerGo.GetComponent<PlayerInput>();
            if (playerInput) return playerInput;

            playerInput = playerGo.GetComponentInChildren<PlayerInput>(true);
            if (playerInput) return playerInput;
        }

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

    private void HideDialoguePressEIndicator()
    {
        if (dialoguePressEIndicator == null) return;

        _dialoguePressEWasActiveBeforePrompt = dialoguePressEIndicator.activeSelf;
        dialoguePressEIndicator.SetActive(false);
    }

    private void RestoreDialoguePressEIndicator()
    {
        if (dialoguePressEIndicator == null) return;

        if (_dialoguePressEWasActiveBeforePrompt)
            dialoguePressEIndicator.SetActive(true);

        _dialoguePressEWasActiveBeforePrompt = false;
    }

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
        RestoreDialoguePressEIndicator();

        if (EventSystem.current)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private IEnumerator SetSelectionNextFrame()
    {
        yield return null;

        if (EventSystem.current && firstSelectedOnOpen)
            EventSystem.current.SetSelectedGameObject(firstSelectedOnOpen);
    }

    private void TryOpenConfirm(int index)
    {
        if (index < 0 || index >= minigames.Count) return;

        var entry = minigames[index];
        if (string.IsNullOrEmpty(entry.sceneName)) return;

        var mgr = SimpleDialogueManager.Instance;
        if (mgr == null)
        {
            LoadMinigame(entry);
            return;
        }

        HideDialoguePressEIndicator();

        mgr.TryStartYesNoPrompt(
            confirmTitle,
            entry.confirmMessage,
            yesLabel,
            noLabel,
            onYes: () => LoadMinigame(entry),
            onNo: () =>
            {
                RestoreDialoguePressEIndicator();
                StartCoroutine(SetSelectionNextFrame());
            },
            useTyping: true
        );
    }

    private void LoadMinigame(MinigameButton entry)
    {
        RestoreDialoguePressEIndicator();

        HubMinigameReturnContext.SetHubReturn(hubSceneName, entry.forceWhiteFade);

        if (saveHubPoseBeforeLeaving)
        {
            var player = GameObject.FindGameObjectWithTag(playerTag);
            if (player != null)
                LoadCharacter.SaveHubPoseNow(player.transform, hubSceneNameForPose);
        }

        if (entry.useAssessmentOverrides)
        {
            AssessmentLaunchContext.Set(
                entry.assessmentJsonFileName,
                entry.completeAssessmentRewardId,
                entry.perfectAssessmentRewardId,
                entry.chapterCompletionRewardId,
                entry.useHubSpawnOverrideOnReturn,
                entry.hubSpawnPointNameOnReturn,
                entry.assessmentHighScoreKey
            );
        }

        CloseMenu();

        if (SceneTransition.Instance != null)
        {
            if (entry.forceWhiteFade)
                SceneTransition.Instance.LoadSceneWhite(entry.sceneName);
            else
                SceneTransition.Instance.LoadScene(entry.sceneName);
        }
        else
        {
            SceneManager.LoadScene(entry.sceneName);
        }
    }
}