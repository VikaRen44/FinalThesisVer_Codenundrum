using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class CutsceneRunner : MonoBehaviour
{
    public static CutsceneRunner Instance { get; private set; }

    [Header("Behavior")]
    public bool blockIfAlreadyPlaying = true;
    public bool IsPlaying { get; private set; }

    [Header("Auto-Find")]
    [Tooltip("If player is not passed in, CutsceneRunner will find by this tag.")]
    public string defaultPlayerTag = "Player";

    [Header("Critical Step Behavior")]
    public bool waitForDialogueManager = true;
    public bool abortIfDialogueMissing = true;

    // NOTE: kept field names to avoid breaking your inspector,
    // but this runner will look for TutorialUI (NOT TutorialManager).
    public bool waitForTutorialManager = true;
    public bool abortIfTutorialMissing = false;
    public float managerWaitTimeout = 1.0f;

    private bool _abortSequence;

    private enum LegacyBlackFadeType { FadeIn, FadeOut }

    [Header("Fade UI (REUSE TeleportTransition)")]
    [SerializeField] private CanvasGroup fadeCanvasGroup;
    [SerializeField] private bool autoFindFadeCanvas = true;
    [SerializeField] private string fadeCanvasObjectName = "TeleportTransition";
    [SerializeField] private bool blockInputDuringFade = true;

    [Header("Fade Overlay (optional)")]
    [Tooltip("Image used by the fade canvas. We'll tint it white only for PlayCutsceneDialogue flashes.")]
    [SerializeField] private Image fadeOverlayImage;
    [SerializeField] private bool autoFindFadeOverlayImage = true;

    // =========================
    // ✅ GLOBAL PLAYER LOCK (ENTIRE SEQUENCE)
    // =========================
    [Header("Global Player Lock (entire cutscene)")]
    [Tooltip("If true, player controls are disabled for the entire cutscene, including steps without dialogue/tutorial.")]
    public bool lockPlayerForEntireSequence = true;

    [Tooltip("Optional direct reference. If null, runner will try to find PlayerMovement on resolved player.")]
    public PlayerMovement playerMovement;

    [Tooltip("Extra safety: disable CharacterController while cutscene plays (NOT recommended if it causes lifting).")]
    public bool disableCharacterControllerWhileCutscene = false;

    [Header("Cutscene Stop Safety (optional, OFF by default)")]
    [Tooltip("Optional: small freeze to kill input for a moment. Leave 0 to avoid any movement side effects.")]
    public float cutsceneStartFreezeSeconds = 0f;

    [Tooltip("Optional: snap to ground right when lock starts. Can lift player in some setups, keep OFF.")]
    public bool snapPlayerToGroundOnLock = false;

    private int _sequenceLockCount = 0;

    // legacy fields kept (not removed)
    private bool _prevCanMove = true;
    private CharacterController _playerCC;
    private bool _prevCCEnabled = true;

    private bool _lockAppliedThisSequence = false;
    private Transform _lockedPlayerTransform;

    // =========================
    // ✅ Host post-move idle enforcement
    // =========================
    [Header("Host Post-Move Safety (prevents stuck walking)")]
    [Tooltip("If true, after MoveHostPath completes we force idle immediately (and optionally for a few frames).")]
    public bool forceHostIdleAfterMove = true;

    [Tooltip("How many frames to keep forcing idle after MoveHostPath finishes (recommended 4-10).")]
    public int forceHostIdleFrames = 6;

    [Tooltip("If true, also stop/reset NavMeshAgent after MoveHostPath (prevents agent velocity from re-triggering walk).")]
    public bool stopHostNavMeshAgentAfterMove = true;

    [Tooltip("Extra bool parameters to force false during post-move idle.")]
    public string[] hostForceFalseBoolsOnIdle = new string[] { "IsWalking", "Walking", "Moving", "IsMoving", "InCutscen" };

    [Tooltip("Animator float param to force to 0 during post-move idle (usually 'Speed'). Leave empty to skip.")]
    public string hostSpeedParamName = "Speed";

    // =========================
    // ✅ RESUME SYSTEM
    // =========================
    private bool _hasPendingResume;
    private bool _resumingNow;

    private CutsceneSequenceSO _resumeSequence;
    private int _resumeStartIndex;
    private string _resumeOwnerPath;
    private string _resumePlayerTag;
    private string _resumeHostTag;

    // =========================
    // ✅ BGM FIXES
    // =========================
    [Header("BGM Fix")]
    [Tooltip("After a cutscene override ends, force the scene BGM for a few frames (safety).")]
    public int forceSceneBgmFramesAfterOverride = 10;

    [Tooltip("After returning from minigame, force scene BGM for a few frames (safety).")]
    public int forceSceneBgmFramesAfterMinigame = 10;

    private bool _cutsceneOverrideActive = false;
    private string _sceneNameToRestoreAfterCutscene = null;

    private bool _minigameSnapshotPushed = false;
    private string _sceneNameToRestoreAfterMinigame = null;

    // =========================
    // ✅ OBJECTIVE STEP WAIT (FIXES "STRIKE ONLY WORKS ONCE")
    // =========================
    [Header("Objective Step Wait (Fix)")]
    [Tooltip("If true, CutsceneRunner will WAIT (realtime) for ObjectiveManager's fade/bounce/hold animations after objective steps.\nThis prevents objective visuals from being instantly overwritten by later steps.")]
    public bool waitForObjectiveAnimations = true;

    [Tooltip("Extra buffer added to objective wait (seconds). Helps if your UI is heavy or you chain steps fast.")]
    public float objectiveAnimExtraBuffer = 0.02f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // =========================================================
    // ✅ MAIN ENTRY: locks player for ENTIRE sequence
    // =========================================================
    public IEnumerator Play(CutsceneSequenceSO sequence, GameObject owner = null, Transform host = null, Transform player = null)
    {
        if (sequence == null || sequence.steps == null || sequence.steps.Length == 0)
            yield break;

        if (blockIfAlreadyPlaying && IsPlaying)
            yield break;

        Transform resolvedPlayer = ResolvePlayer(player, defaultPlayerTag);
        BeginSequenceLock(resolvedPlayer);

        IsPlaying = true;
        _abortSequence = false;

        try
        {
            yield return RunSteps(sequence, startIndex: 0, owner, host, player);
        }
        finally
        {
            IsPlaying = false;
            EndSequenceLock();
        }
    }

    public void ResumeAfterMinigameReturn()
    {
        if (!_hasPendingResume)
        {
            Debug.LogWarning("[CutsceneRunner] ResumeAfterMinigameReturn called but no pending resume exists.");
            return;
        }

        if (_resumingNow) return;
        StartCoroutine(ResumeRoutine());
    }

    // =========================================================
    // ✅ EXTERNAL LOCK API (for AssessmentUI, menus, etc.)
    // =========================================================
    public void ExternalLockPlayer(Transform playerOverride = null, string playerTagOverride = null)
    {
        string tag = string.IsNullOrEmpty(playerTagOverride) ? defaultPlayerTag : playerTagOverride;
        Transform p = ResolvePlayer(playerOverride, tag);
        BeginSequenceLock(p);
    }

    public void ExternalUnlockPlayer()
    {
        EndSequenceLock();
    }

    private IEnumerator ResumeRoutine()
    {
        _resumingNow = true;

        yield return null;
        yield return null;
        yield return null;
        yield return new WaitForFixedUpdate();

        GameObject owner = ResolveOwnerByPath(_resumeOwnerPath);
        Transform player = ResolvePlayer(null, _resumePlayerTag);
        Transform host = ResolveHost(null, _resumeHostTag);

        // ✅ CAMERA FIX: re-lock camera to THIS scene + THIS player after return
        yield return ForceCameraFixAfterReturn(player);

        // ✅ BGM FIX: after returning from minigame, restore snapshot THEN force scene BGM
        if (_minigameSnapshotPushed && BgmManager.Instance != null)
        {
            BgmManager.Instance.PopSnapshotAndRestore(fade: false);
            _minigameSnapshotPushed = false;
        }

        if (BgmManager.Instance != null)
        {
            string sceneName = string.IsNullOrEmpty(_sceneNameToRestoreAfterMinigame)
                ? SceneManager.GetActiveScene().name
                : _sceneNameToRestoreAfterMinigame;

            StartCoroutine(ForceSceneBgmForFrames(sceneName, forceSceneBgmFramesAfterMinigame, 0.1f, 0.1f));
        }

        if (_resumeSequence == null || _resumeSequence.steps == null)
        {
            Debug.LogWarning("[CutsceneRunner] Resume failed: resume sequence missing.");
            ClearResume();
            _resumingNow = false;
            yield break;
        }

        if (_resumeStartIndex < 0 || _resumeStartIndex >= _resumeSequence.steps.Length)
        {
            Debug.LogWarning("[CutsceneRunner] Resume failed: resumeStartIndex out of range.");
            ClearResume();
            _resumingNow = false;
            yield break;
        }

        BeginSequenceLock(player);

        IsPlaying = true;
        _abortSequence = false;

        try
        {
            yield return RunSteps(_resumeSequence, _resumeStartIndex, owner, host, player);
        }
        finally
        {
            IsPlaying = false;
            EndSequenceLock();
            ClearResume();
            _resumingNow = false;
        }
    }

    // ✅ camera retarget helper (reflection-based, no hierarchy changes)
    private IEnumerator ForceCameraFixAfterReturn(Transform player)
    {
        if (player == null) yield break;

        yield return null;
        yield return new WaitForFixedUpdate();

        Scene active = SceneManager.GetActiveScene();

        const int frames = 6;
        for (int f = 0; f < frames; f++)
        {
            var all = FindObjectsOfType<MonoBehaviour>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var mb = all[i];
                if (mb == null) continue;

                if (mb.GetType().Name != "CameraFollow") continue;

                if (!mb.gameObject.scene.IsValid() || mb.gameObject.scene != active)
                    continue;

                TryInvokeVoid(mb, "ApplySceneCameraSettings");
                TryInvokeVoid(mb, "RecalculateOffset");

                if (!TryInvokeVoid(mb, "SetTarget", player, true, false))
                    TryInvokeVoid(mb, "SetTarget", player);

                TryInvokeVoid(mb, "ForceSnap");
            }

            yield return null;
        }
    }

    private void ClearResume()
    {
        _hasPendingResume = false;
        _resumeSequence = null;
        _resumeStartIndex = 0;
        _resumeOwnerPath = null;
        _resumePlayerTag = null;
        _resumeHostTag = null;
        _sceneNameToRestoreAfterMinigame = null;
    }

    // -------------------------------------------------------
    // ✅ GLOBAL LOCK IMPLEMENTATION (NO MOVEMENT SIDE EFFECTS)
    // -------------------------------------------------------
    private void BeginSequenceLock(Transform resolvedPlayer)
    {
        if (!lockPlayerForEntireSequence) return;

        _sequenceLockCount++;
        if (_sequenceLockCount > 1) return;

        _lockedPlayerTransform = resolvedPlayer;

        if (playerMovement == null && resolvedPlayer != null)
        {
            playerMovement = resolvedPlayer.GetComponent<PlayerMovement>();
            if (playerMovement == null)
                playerMovement = resolvedPlayer.GetComponentInChildren<PlayerMovement>(true);
        }

        if (playerMovement != null)
        {
            try { _prevCanMove = playerMovement.canMove; } catch { _prevCanMove = true; }

            try { playerMovement.AddLock(this); }
            catch
            {
                try { playerMovement.canMove = false; } catch { }
            }

            if (cutsceneStartFreezeSeconds > 0.0001f)
            {
                try { playerMovement.FreezeForSeconds(cutsceneStartFreezeSeconds); } catch { }
            }

            if (snapPlayerToGroundOnLock)
            {
                try { playerMovement.ForceSnapToGroundNow(); } catch { }
            }

            _lockAppliedThisSequence = true;
        }
        else
        {
            _lockAppliedThisSequence = false;
        }

        if (disableCharacterControllerWhileCutscene && resolvedPlayer != null)
        {
            _playerCC = resolvedPlayer.GetComponent<CharacterController>();
            if (_playerCC != null)
            {
                _prevCCEnabled = _playerCC.enabled;
                _playerCC.enabled = false;
            }
        }
    }

    private void EndSequenceLock()
    {
        if (!lockPlayerForEntireSequence) return;

        _sequenceLockCount = Mathf.Max(0, _sequenceLockCount - 1);
        if (_sequenceLockCount > 0) return;

        if (_playerCC != null)
        {
            _playerCC.enabled = _prevCCEnabled;
            _playerCC = null;
        }

        if (_lockAppliedThisSequence && playerMovement != null)
        {
            try { playerMovement.RemoveLock(this); }
            catch
            {
                try { playerMovement.canMove = _prevCanMove; } catch { }
            }
        }

        _lockAppliedThisSequence = false;
        _lockedPlayerTransform = null;
    }

    // -------------------------------------------------------
    // Core runner
    // -------------------------------------------------------
    private IEnumerator RunSteps(CutsceneSequenceSO sequence, int startIndex, GameObject owner, Transform host, Transform player)
    {
        for (int i = startIndex; i < sequence.steps.Length; i++)
        {
            if (_abortSequence) yield break;

            var step = sequence.steps[i];
            if (step == null) continue;

            Transform resolvedPlayer = ResolvePlayer(player, GetStringField(step, "playerTag"));
            Transform resolvedHost = ResolveHost(host, GetStringField(step, "hostTag"));

            switch (step.type)
            {
                case CutsceneStepType.PlayDialogue:
                    yield return Step_PlayDialogue(step);
                    break;

                case CutsceneStepType.PlayCutsceneDialogue:
                    yield return Step_PlayCutsceneDialogue(step);
                    break;

                case CutsceneStepType.ShowTutorial:
                    yield return Step_ShowTutorial(step, resolvedPlayer);
                    break;

                case CutsceneStepType.WaitSeconds:
                    yield return Step_WaitSeconds(step);
                    break;

                case CutsceneStepType.SetObjective:
                    yield return Step_SetObjective_Routine(step);
                    break;

                case CutsceneStepType.CompleteObjective:
                    yield return Step_CompleteObjective_Routine(step);
                    break;

                case CutsceneStepType.CompleteObjectiveAndSet:
                    yield return Step_CompleteObjectiveAndSet_Routine(step);
                    break;

                case CutsceneStepType.TurnPlayerToHost:
                    yield return Step_Turn(
                        subject: resolvedPlayer,
                        target: ResolveTurnTarget_LookAtHost(step, owner, resolvedHost),
                        turnSpeed: GetFloatField(step, "turnSpeed", 6f),
                        angleTolerance: GetFloatField(step, "angleTolerance", 3f)
                    );
                    break;

                case CutsceneStepType.TurnHostToPlayer:
                    yield return Step_Turn(
                        subject: resolvedHost,
                        target: ResolveTurnTarget_LookAtPlayer(step, owner, resolvedPlayer),
                        turnSpeed: GetFloatField(step, "turnSpeed", 6f),
                        angleTolerance: GetFloatField(step, "angleTolerance", 3f)
                    );
                    break;

                case CutsceneStepType.TeleportPlayer:
                    yield return Step_Teleport(resolvedPlayer, owner, step);
                    break;

                case CutsceneStepType.TeleportHost:
                    yield return Step_Teleport(resolvedHost, owner, step);
                    break;

                case CutsceneStepType.MoveHostPath:
                    yield return Step_MoveHostPath(resolvedHost, owner, step);
                    break;

                case CutsceneStepType.BlackTransition:
                    yield return Step_BlackTransition_UsingTeleportUI(step);
                    break;

                case CutsceneStepType.FadeBlackIn:
                    yield return Step_BlackFromLegacy_UsingTeleportUI(step, LegacyBlackFadeType.FadeIn);
                    break;

                case CutsceneStepType.FadeBlackOut:
                    yield return Step_BlackFromLegacy_UsingTeleportUI(step, LegacyBlackFadeType.FadeOut);
                    break;

                case CutsceneStepType.EnterMinigame:
                    yield return Step_EnterMinigame_AndPause(sequence, i, step, owner, resolvedPlayer, resolvedHost);
                    yield break;
            }
        }
    }

    // -------------------------------------------------------
    // Objective step routines (NEW) — do not remove existing functions
    // -------------------------------------------------------
    private IEnumerator Step_SetObjective_Routine(CutsceneStep step)
    {
        Step_SetObjective(step);
        yield return WaitForObjectiveAnimationIfAny(wasCompletion: false);
    }

    private IEnumerator Step_CompleteObjective_Routine(CutsceneStep step)
    {
        Step_CompleteObjective(step);
        yield return WaitForObjectiveAnimationIfAny(wasCompletion: true);
    }

    private IEnumerator Step_CompleteObjectiveAndSet_Routine(CutsceneStep step)
    {
        // Prefer the ObjectiveManager method designed for "complete then set"
        // (this is very likely the path that does the green flash correctly)
        string id = step != null ? step.objectiveId : null;
        if (string.IsNullOrEmpty(id)) id = GetStringField(step, "objectiveId");

        bool did = false;

        if (ObjectiveManager.Instance != null)
        {
            if (!string.IsNullOrEmpty(id))
            {
                ObjectiveManager.Instance.CompleteCurrentObjectiveAndSet(id);
                did = true;
            }
            else
            {
                // If no next id, at least complete current using the "current" path (green flash)
                ObjectiveManager.Instance.CompleteCurrentObjective();
                did = true;
            }
        }
        else
        {
            var om = FindFirstComponentByTypeName("ObjectiveManager");
            if (om != null)
            {
                if (!string.IsNullOrEmpty(id))
                    did = TryInvokeVoid(om, "CompleteCurrentObjectiveAndSet", id);
                else
                    did = TryInvokeVoid(om, "CompleteCurrentObjective");

                // If method doesn't exist, fall back to legacy chain
                if (!did)
                {
                    Step_CompleteObjective(step);
                    Step_SetObjective(step);
                    did = true;
                }
            }
        }

        if (did)
            yield return WaitForObjectiveAnimationIfAny(wasCompletion: true);
    }

    private IEnumerator WaitForObjectiveAnimationIfAny(bool wasCompletion)
    {
        if (!waitForObjectiveAnimations) yield break;

        var om = ObjectiveManager.Instance;
        if (om == null) yield break;

        // If objective animations are disabled, nothing to wait for.
        if (!om.animateObjectiveCompletion) yield break;

        // Realtime wait (safe with timescale changes)
        float wait = 0f;

        // Completion animations may have bounce + hold
        if (wasCompletion)
        {
            if (om.bounceOnCheckmark) wait += Mathf.Max(0f, om.bounceDuration);
            wait += Mathf.Max(0f, om.completedHoldSeconds);
        }

        // Both normal change + completion use fade out/in
        wait += Mathf.Max(0f, om.fadeOutDuration);
        wait += Mathf.Max(0f, om.fadeInDuration);

        wait += Mathf.Max(0f, objectiveAnimExtraBuffer);

        if (wait <= 0.0001f) yield break;
        yield return new WaitForSecondsRealtime(wait);
    }

    // -------------------------------------------------------
    // Steps
    // -------------------------------------------------------
    private IEnumerator Step_PlayDialogue(CutsceneStep step)
    {
        var dialogue = step.dialogue;
        if (dialogue == null) yield break;

        var dm = ResolveDialogueManager();
        if (dm == null)
        {
            Debug.LogWarning("[CutsceneRunner] No SimpleDialogueManager found for PlayDialogue.");
            if (abortIfDialogueMissing) _abortSequence = true;
            yield break;
        }

        if (dm.normalUi != null)
            dm.PushUIOverride(dm.normalUi);

        bool started = dm.TryStartDialogueInternal(dialogue);
        if (!started)
        {
            Debug.LogWarning("[CutsceneRunner] TryStartDialogueInternal(dialogue) failed.");
            if (abortIfDialogueMissing) _abortSequence = true;

            if (dm.normalUi != null) dm.PopUIOverride(dm.normalUi);
            yield break;
        }

        bool wait = GetBoolField(step, "waitUntilDialogueEnds", true);
        if (wait)
        {
            yield return null;
            while (dm.IsPlaying)
                yield return null;
        }

        if (dm.normalUi != null) dm.PopUIOverride(dm.normalUi);
    }

    private IEnumerator Step_PlayCutsceneDialogue(CutsceneStep step)
    {
        var dialogue = step.dialogue;
        if (dialogue == null) yield break;

        var dm = ResolveDialogueManager();
        if (dm == null)
        {
            Debug.LogWarning("[CutsceneRunner] No SimpleDialogueManager found for PlayCutsceneDialogue.");
            if (abortIfDialogueMissing) _abortSequence = true;
            yield break;
        }

        _sceneNameToRestoreAfterCutscene = SceneManager.GetActiveScene().name;

        StartCutsceneBgmOverride(step);

        bool useCutsceneUI = GetBoolField(step, "useCutsceneUI", true);
        bool whiteBefore = GetBoolField(step, "whiteFlashBefore", true);
        bool whiteAfter = GetBoolField(step, "whiteFadeAfter", false);

        float inDur = Mathf.Max(0f, GetFloatField(step, "whiteInDuration", 0.20f));
        float outDur = Mathf.Max(0f, GetFloatField(step, "whiteOutDuration", 0.20f));
        float hold = Mathf.Max(0f, GetFloatField(step, "whiteHoldSeconds", 0.03f));

        EnsureFadeCanvasGroup();
        EnsureFadeOverlayImage();

        Color prevColor = Color.black;
        bool hadPrevColor = false;
        if (fadeOverlayImage != null)
        {
            prevColor = fadeOverlayImage.color;
            hadPrevColor = true;
        }

        if (whiteBefore && fadeCanvasGroup != null)
        {
            if (fadeOverlayImage != null) fadeOverlayImage.color = Color.white;
            yield return FadeCanvasGroup(1f, inDur);
            if (hold > 0f) yield return new WaitForSecondsRealtime(hold);
            yield return FadeCanvasGroup(0f, outDur);
        }

        bool started;

        if (useCutsceneUI && dm.cutsceneUi != null)
            started = dm.TryStartDialogueWithUI(dialogue, dm.cutsceneUi);
        else
            started = dm.TryStartDialogueInternal(dialogue);

        if (!started)
        {
            Debug.LogWarning("[CutsceneRunner] PlayCutsceneDialogue: failed to start dialogue.");
            if (abortIfDialogueMissing) _abortSequence = true;

            if (fadeOverlayImage != null && hadPrevColor) fadeOverlayImage.color = prevColor;

            EndCutsceneBgmOverrideAndReturnToScene(step);
            yield break;
        }

        bool wait = GetBoolField(step, "waitUntilDialogueEnds", true);
        if (wait)
        {
            yield return null;
            while (dm.IsPlaying)
                yield return null;
        }

        if (whiteAfter && fadeCanvasGroup != null)
        {
            if (fadeOverlayImage != null) fadeOverlayImage.color = Color.white;
            yield return FadeCanvasGroup(1f, inDur);
            if (hold > 0f) yield return new WaitForSecondsRealtime(hold);
            yield return FadeCanvasGroup(0f, outDur);
        }

        if (fadeOverlayImage != null && hadPrevColor)
            fadeOverlayImage.color = prevColor;

        EndCutsceneBgmOverrideAndReturnToScene(step);
    }

    private void StartCutsceneBgmOverride(CutsceneStep step)
    {
        _cutsceneOverrideActive = false;

        if (BgmManager.Instance == null) return;
        if (step.cutsceneOverrideBgm == null) return;

        BgmManager.Instance.PushOverride();

        BgmManager.Instance.Play(
            step.cutsceneOverrideBgm,
            Mathf.Clamp01(step.cutsceneOverrideVolume),
            step.cutsceneOverrideLoop,
            step.cutsceneOverrideFadeOut,
            step.cutsceneOverrideFadeIn,
            forceRestart: true
        );

        _cutsceneOverrideActive = true;
    }

    private void EndCutsceneBgmOverrideAndReturnToScene(CutsceneStep step)
    {
        if (BgmManager.Instance == null)
            return;

        if (_cutsceneOverrideActive)
            BgmManager.Instance.PopOverride();

        string sceneName = string.IsNullOrEmpty(_sceneNameToRestoreAfterCutscene)
            ? SceneManager.GetActiveScene().name
            : _sceneNameToRestoreAfterCutscene;

        float outFade = Mathf.Max(0f, step.restorePrevBgmFadeOut);
        float inFade = Mathf.Max(0f, step.restorePrevBgmFadeIn);

        BgmManager.Instance.PlaySceneBgmNow(sceneName, outFade, inFade);

        StartCoroutine(ForceSceneBgmForFrames(sceneName, forceSceneBgmFramesAfterOverride, outFade, inFade));

        _cutsceneOverrideActive = false;
        _sceneNameToRestoreAfterCutscene = null;
    }

    private IEnumerator ForceSceneBgmForFrames(string sceneName, int frames, float fadeOut, float fadeIn)
    {
        if (BgmManager.Instance == null) yield break;
        if (string.IsNullOrEmpty(sceneName)) yield break;

        frames = Mathf.Clamp(frames, 1, 60);

        yield return null;
        yield return null;

        for (int i = 0; i < frames; i++)
        {
            BgmManager.Instance.PlaySceneBgmNow(sceneName, fadeOut, fadeIn);
            yield return null;
        }
    }

    private SimpleDialogueManager ResolveDialogueManager()
    {
        if (SimpleDialogueManager.Instance != null)
            return SimpleDialogueManager.Instance;

        MonoBehaviour mb = FindFirstComponentByTypeName("SimpleDialogueManager");
        return mb as SimpleDialogueManager;
    }

    private IEnumerator Step_ShowTutorial(CutsceneStep step, Transform player)
    {
        var tutorial = step.tutorial;
        if (tutorial == null) yield break;

        MonoBehaviour tut = FindFirstComponentByTypeName("TutorialUI");

        if (tut == null && waitForTutorialManager)
        {
            float end = Time.unscaledTime + Mathf.Max(0f, managerWaitTimeout);
            while (tut == null && Time.unscaledTime < end)
            {
                tut = FindFirstComponentByTypeName("TutorialUI");
                yield return null;
            }
        }

        if (tut == null)
        {
            Debug.LogWarning("[CutsceneRunner] No TutorialUI found for ShowTutorial.");
            if (abortIfTutorialMissing) _abortSequence = true;
            yield break;
        }

        bool started =
            TryInvokeVoid(tut, "Show", tutorial, player) ||
            TryInvokeVoid(tut, "Show", tutorial, null);

        if (!started)
        {
            Debug.LogWarning("[CutsceneRunner] TutorialUI.Show(...) failed.");
            if (abortIfTutorialMissing) _abortSequence = true;
            yield break;
        }

        bool wait = GetBoolField(step, "waitUntilTutorialClosed", true);
        if (wait)
        {
            yield return null;
            while (GetBoolProperty(tut, "IsOpen"))
                yield return null;
        }
    }

    private IEnumerator Step_WaitSeconds(CutsceneStep step)
    {
        float t = Mathf.Max(0f, step.waitSeconds);
        if (t <= 0f) yield break;
        yield return new WaitForSeconds(t);
    }

    // ✅ EXISTING FUNCTIONS KEPT (bodies adjusted ONLY for correct animation path)
    private void Step_SetObjective(CutsceneStep step)
    {
        // Legacy-safe id read
        string id = step != null ? step.objectiveId : null;
        if (string.IsNullOrEmpty(id)) id = GetStringField(step, "objectiveId");
        if (string.IsNullOrEmpty(id)) return;

        // Prefer NON-force animated setters first (this is usually what your "other updates" use)
        if (ObjectiveManager.Instance != null)
        {
            // Try common animated method names first
            if (TryInvokeVoid(ObjectiveManager.Instance, "SetObjective", id)) return;
            if (TryInvokeVoid(ObjectiveManager.Instance, "SetCurrentObjective", id)) return;
            if (TryInvokeVoid(ObjectiveManager.Instance, "SetObjectiveId", id)) return;

            // Fallback to your known method
            ObjectiveManager.Instance.ForceSetObjective(id);
            return;
        }

        var om = FindFirstComponentByTypeName("ObjectiveManager");
        if (om == null) return;

        // Reflection: try animated first
        if (TryInvokeVoid(om, "SetObjective", id)) return;
        if (TryInvokeVoid(om, "SetCurrentObjective", id)) return;
        if (TryInvokeVoid(om, "SetObjectiveId", id)) return;

        // Fallback force
        TryInvokeVoid(om, "ForceSetObjective", id);
    }

    private void Step_CompleteObjective(CutsceneStep step)
    {
        // Legacy-safe id read
        string id = step != null ? step.objectiveId : null;
        if (string.IsNullOrEmpty(id)) id = GetStringField(step, "objectiveId");

        // We want the SAME behavior as your normal updates:
        // ✅ If we're completing the CURRENT objective, call CompleteCurrentObjective()
        // because that's commonly where "turn green" + strike animation is implemented.
        if (ObjectiveManager.Instance != null)
        {
            string current = null;
            try { current = ObjectiveManager.Instance.GetCurrentObjectiveId(); } catch { }

            bool completingCurrent = string.IsNullOrEmpty(id) || (!string.IsNullOrEmpty(current) && id == current);

            if (completingCurrent)
            {
                // Prefer any animated variants first
                if (TryInvokeVoid(ObjectiveManager.Instance, "CompleteCurrentObjectiveAnimated")) return;
                if (TryInvokeVoid(ObjectiveManager.Instance, "CompleteCurrentObjective")) return;

                // Worst-case fallback (your existing direct call)
                ObjectiveManager.Instance.CompleteCurrentObjective();
                return;
            }

            // Completing a NON-current objective:
            // still try method names, but many UI systems only do green flash for "current"
            if (TryInvokeVoid(ObjectiveManager.Instance, "CompleteObjective", id)) return;
            ObjectiveManager.Instance.CompleteObjective(id);
            return;
        }

        var om = FindFirstComponentByTypeName("ObjectiveManager");
        if (om == null) return;

        // Try to get current id via reflection
        string cur = null;
        try
        {
            // Works if GetCurrentObjectiveId exists
            var t = om.GetType();
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var m = t.GetMethod("GetCurrentObjectiveId", flags);
            if (m != null) cur = m.Invoke(om, null) as string;
        }
        catch { }

        bool completingCurrent2 = string.IsNullOrEmpty(id) || (!string.IsNullOrEmpty(cur) && id == cur);

        if (completingCurrent2)
        {
            if (TryInvokeVoid(om, "CompleteCurrentObjectiveAnimated")) return;
            if (TryInvokeVoid(om, "CompleteCurrentObjective")) return;
            TryInvokeVoid(om, "CompleteCurrentObjective"); // last attempt
            return;
        }

        if (!string.IsNullOrEmpty(id))
        {
            if (TryInvokeVoid(om, "CompleteObjective", id)) return;
        }
        else
        {
            TryInvokeVoid(om, "CompleteCurrentObjective");
        }
    }


    private IEnumerator Step_EnterMinigame_AndPause(
        CutsceneSequenceSO sequence,
        int currentIndex,
        CutsceneStep step,
        GameObject owner,
        Transform player,
        Transform host
    )
    {
        string minigameScene = GetStringField(step, "minigameSceneName");
        string returnOverride = GetStringField(step, "returnSceneNameOverride");

        bool restoreOnReturn = GetBoolField(step, "restorePlayerAndHostOnReturn", true);
        bool restoreRot = GetBoolField(step, "restoreRotationOnReturn", true);

        float entryDur = GetFloatField(step, "entryTransitionDuration", 0.35f);

        if (string.IsNullOrWhiteSpace(minigameScene))
        {
            Debug.LogWarning("[CutsceneRunner] EnterMinigame: minigameSceneName is empty.");
            yield break;
        }

        string currentWorldScene = SceneManager.GetActiveScene().name;
        string returnScene = string.IsNullOrWhiteSpace(returnOverride) ? currentWorldScene : returnOverride;

        _hasPendingResume = true;
        _resumeSequence = sequence;
        _resumeStartIndex = currentIndex + 1;

        _resumeOwnerPath = (owner != null) ? GetHierarchyPath(owner.transform) : null;

        _resumePlayerTag = GetStringField(step, "playerTag");
        if (string.IsNullOrEmpty(_resumePlayerTag)) _resumePlayerTag = defaultPlayerTag;

        _resumeHostTag = GetStringField(step, "hostTag");
        if (string.IsNullOrEmpty(_resumeHostTag)) _resumeHostTag = "Host";

        _sceneNameToRestoreAfterMinigame = returnScene;
        if (BgmManager.Instance != null)
        {
            BgmManager.Instance.PushSnapshot();
            _minigameSnapshotPushed = true;
        }
        else
        {
            _minigameSnapshotPushed = false;
        }

        if (restoreOnReturn)
        {
            MinigameReturnContext.Capture(
                returnScene,
                player,
                host,
                true,
                restoreRot,
                _resumePlayerTag,
                _resumeHostTag
            );
        }
        else
        {
            MinigameReturnContext.Clear();
        }

        EnsureFadeCanvasGroup();
        if (fadeCanvasGroup != null)
            yield return FadeCanvasGroup(1f, Mathf.Max(0f, entryDur));

        AsyncOperation op = null;
        try { op = SceneManager.LoadSceneAsync(minigameScene); }
        catch (Exception e)
        {
            Debug.LogWarning("[CutsceneRunner] EnterMinigame LoadSceneAsync failed: " + e.Message);
            MinigameReturnContext.Clear();
            ClearResume();
            yield break;
        }

        while (op != null && !op.isDone)
            yield return null;

        EnsureFadeCanvasGroup();
        if (fadeCanvasGroup != null)
            yield return FadeCanvasGroup(0f, 0.15f);
    }

    private Transform ResolveTurnTarget_LookAtHost(CutsceneStep step, GameObject owner, Transform host)
    {
        if (host != null) return host;
        return GetTransformField(step, "explicitLookTarget");
    }

    private Transform ResolveTurnTarget_LookAtPlayer(CutsceneStep step, GameObject owner, Transform player)
    {
        if (player != null) return player;
        return GetTransformField(step, "explicitLookTarget");
    }

    private IEnumerator Step_Turn(Transform subject, Transform target, float turnSpeed, float angleTolerance)
    {
        if (subject == null || target == null) yield break;

        var agent = subject.GetComponent<NavMeshAgent>();
        bool hadAgent = (agent != null && agent.enabled);
        bool prevUpdateRot = false;

        if (hadAgent)
        {
            prevUpdateRot = agent.updateRotation;
            agent.updateRotation = false;
        }

        float speed = Mathf.Max(0.01f, turnSpeed);
        float tol = Mathf.Clamp(angleTolerance, 0.1f, 45f);

        while (true)
        {
            Vector3 dir = target.position - subject.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) break;

            Quaternion targetRot = Quaternion.LookRotation(dir.normalized);
            subject.rotation = Quaternion.Slerp(subject.rotation, targetRot, speed * Time.deltaTime);

            if (Quaternion.Angle(subject.rotation, targetRot) <= tol)
                break;

            yield return null;
        }

        if (hadAgent)
            agent.updateRotation = prevUpdateRot;
    }

    private IEnumerator Step_Teleport(Transform who, GameObject owner, CutsceneStep step)
    {
        if (who == null) yield break;

        Transform dest = GetTransformField(step, "teleportTarget");

        string childName = GetStringField(step, "ownerChildTargetName");
        bool includeInactive = GetBoolField(step, "includeInactiveChildren", true);

        if (dest == null && owner != null && !string.IsNullOrEmpty(childName))
            dest = FindChildByName(owner.transform, childName, includeInactive);

        if (dest == null) yield break;

        SafeSetPose(who, dest.position, dest.rotation);
        yield break;
    }

    // ✅ UPDATED: Always force IDLE immediately after the move ends.
    private IEnumerator Step_MoveHostPath(Transform host, GameObject owner, CutsceneStep step)
    {
        if (host == null || owner == null) yield break;

        var provider = owner.GetComponent<CutscenePathPoints>();
        if (provider == null || provider.points == null || provider.points.Length == 0) yield break;

        List<Vector3> pts = new List<Vector3>();
        foreach (var t in provider.points)
            if (t != null) pts.Add(t.position);

        if (pts.Count == 0) yield break;

        float speed = Mathf.Max(0.1f, GetFloatField(step, "moveSpeed", 3f));

        Component mover = host.GetComponent("HostCutsceneMove");
        if (mover == null) mover = host.GetComponent("HostCutsceneMover");

        if (mover != null)
        {
            TrySetFieldOrProperty(mover, "moveSpeed", speed);
            TrySetFieldOrProperty(mover, "MoveSpeed", speed);

            bool started = TryInvokeIEnumerator(mover, "MoveAlongPath", pts, out IEnumerator routine);
            if (started && routine != null)
                yield return StartCoroutine(routine);

            if (forceHostIdleAfterMove)
            {
                TryInvokeVoid(mover, "ForceIdleNow");
                TryInvokeVoid(mover, "ForceIdle");
                yield return ForceHostIdleForFrames(host, mover);
            }

            yield break;
        }

        var agent = host.GetComponent<NavMeshAgent>();
        if (agent != null && agent.enabled)
        {
            float prevSpeed = agent.speed;
            agent.speed = speed;

            for (int i = 0; i < pts.Count; i++)
            {
                agent.SetDestination(pts[i]);
                while (true)
                {
                    if (!agent.pathPending && agent.remainingDistance <= Mathf.Max(0.15f, agent.stoppingDistance))
                        break;
                    yield return null;
                }
            }

            agent.speed = prevSpeed;

            if (forceHostIdleAfterMove)
                yield return ForceHostIdleForFrames(host, null);

            yield break;
        }

        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 target = pts[i];
            while ((host.position - target).sqrMagnitude > 0.04f)
            {
                host.position = Vector3.MoveTowards(host.position, target, speed * Time.deltaTime);
                yield return null;
            }
        }

        if (forceHostIdleAfterMove)
            yield return ForceHostIdleForFrames(host, null);
    }

    private IEnumerator ForceHostIdleForFrames(Transform host, Component mover)
    {
        if (host == null) yield break;

        int frames = Mathf.Clamp(forceHostIdleFrames, 0, 60);
        if (frames <= 0) yield break;

        Animator anim = host.GetComponentInChildren<Animator>(true);

        int speedHash = 0;
        if (anim != null && !string.IsNullOrEmpty(hostSpeedParamName))
            speedHash = Animator.StringToHash(hostSpeedParamName);

        NavMeshAgent agent = host.GetComponent<NavMeshAgent>();

        for (int i = 0; i < frames; i++)
        {
            if (stopHostNavMeshAgentAfterMove && agent != null && agent.enabled)
            {
                try
                {
                    agent.isStopped = true;
                    agent.ResetPath();
                    agent.velocity = Vector3.zero;
                }
                catch { }
            }

            if (anim != null)
            {
                if (speedHash != 0)
                {
                    try { anim.SetFloat(speedHash, 0f); } catch { }
                }

                if (hostForceFalseBoolsOnIdle != null)
                {
                    for (int b = 0; b < hostForceFalseBoolsOnIdle.Length; b++)
                    {
                        string bn = hostForceFalseBoolsOnIdle[b];
                        if (string.IsNullOrEmpty(bn)) continue;
                        try { anim.SetBool(bn, false); } catch { }
                    }
                }

                if (mover != null)
                {
                    TryInvokeVoid(mover, "ForceIdleNow");
                    TryInvokeVoid(mover, "ForceIdle");
                }

                try { anim.Update(0f); } catch { }
            }

            yield return null;
        }
    }

    private IEnumerator Step_BlackTransition_UsingTeleportUI(CutsceneStep step)
    {
        EnsureFadeCanvasGroup();
        if (fadeCanvasGroup == null || step.blackTransition == null) yield break;

        float dur = Mathf.Max(0f, step.blackTransition.duration);
        float targetAlpha = IsFadeOut(step.blackTransition) ? 1f : 0f;
        yield return FadeCanvasGroup(targetAlpha, dur);
    }

    private IEnumerator Step_BlackFromLegacy_UsingTeleportUI(CutsceneStep step, LegacyBlackFadeType type)
    {
        EnsureFadeCanvasGroup();
        if (fadeCanvasGroup == null) yield break;

        float dur = (step.blackTransition != null)
            ? Mathf.Max(0f, step.blackTransition.duration)
            : Mathf.Max(0f, GetFloatField(step, "fadeDuration", 0.25f));

        float targetAlpha = (type == LegacyBlackFadeType.FadeOut) ? 1f : 0f;
        yield return FadeCanvasGroup(targetAlpha, dur);
    }

    private bool IsFadeOut(object blackTransitionSO)
    {
        if (blackTransitionSO == null) return true;

        var t = blackTransitionSO.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        var f = t.GetField("fadeType", flags);
        if (f != null)
        {
            try
            {
                object v = f.GetValue(blackTransitionSO);
                return v != null && v.ToString().IndexOf("Out", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { }
        }

        var p = t.GetProperty("fadeType", flags);
        if (p != null)
        {
            try
            {
                object v = p.GetValue(blackTransitionSO);
                return v != null && v.ToString().IndexOf("Out", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { }
        }

        return true;
    }

    private void EnsureFadeCanvasGroup()
    {
        if (fadeCanvasGroup != null) return;
        if (!autoFindFadeCanvas) return;

        var groups = FindObjectsOfType<CanvasGroup>(true);
        foreach (var cg in groups)
        {
            if (!cg) continue;
            if (cg.gameObject.name == fadeCanvasObjectName || cg.transform.name == fadeCanvasObjectName)
            {
                fadeCanvasGroup = cg;
                break;
            }
        }

        if (fadeCanvasGroup != null)
        {
            fadeCanvasGroup.alpha = Mathf.Clamp01(fadeCanvasGroup.alpha);
            if (Mathf.Approximately(fadeCanvasGroup.alpha, 0f))
            {
                fadeCanvasGroup.blocksRaycasts = false;
                fadeCanvasGroup.interactable = false;
            }
        }
    }

    private void EnsureFadeOverlayImage()
    {
        if (fadeOverlayImage != null) return;
        if (!autoFindFadeOverlayImage) return;

        EnsureFadeCanvasGroup();
        if (fadeCanvasGroup == null) return;

        fadeOverlayImage = fadeCanvasGroup.GetComponent<Image>();
        if (fadeOverlayImage == null)
            fadeOverlayImage = fadeCanvasGroup.GetComponentInChildren<Image>(true);
    }

    private IEnumerator FadeCanvasGroup(float targetAlpha, float duration)
    {
        if (fadeCanvasGroup == null) yield break;

        if (blockInputDuringFade)
        {
            fadeCanvasGroup.blocksRaycasts = true;
            fadeCanvasGroup.interactable = true;
        }

        float start = fadeCanvasGroup.alpha;
        float t = 0f;

        if (duration <= 0f)
        {
            fadeCanvasGroup.alpha = targetAlpha;
        }
        else
        {
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                fadeCanvasGroup.alpha = Mathf.Lerp(start, targetAlpha, t / duration);
                yield return null;
            }
            fadeCanvasGroup.alpha = targetAlpha;
        }

        if (blockInputDuringFade && Mathf.Approximately(targetAlpha, 0f))
        {
            fadeCanvasGroup.blocksRaycasts = false;
            fadeCanvasGroup.interactable = false;
        }
    }

    // -------------------------------------------------------
    // Helpers
    // -------------------------------------------------------
    private Transform ResolvePlayer(Transform passedPlayer, string stepPlayerTag)
    {
        if (passedPlayer != null) return passedPlayer;
        string tag = string.IsNullOrEmpty(stepPlayerTag) ? defaultPlayerTag : stepPlayerTag;
        return SafeFindByTag(tag);
    }

    private Transform ResolveHost(Transform passedHost, string stepHostTag)
    {
        if (passedHost != null) return passedHost;
        if (string.IsNullOrEmpty(stepHostTag)) return null;
        return SafeFindByTag(stepHostTag);
    }

    private Transform FindChildByName(Transform root, string name, bool includeInactiveChildren)
    {
        if (root == null) return null;
        if (root.name == name) return root;

        var all = root.GetComponentsInChildren<Transform>(includeInactiveChildren);
        foreach (var t in all)
            if (t != null && t.name == name)
                return t;

        return null;
    }

    private void SafeSetPose(Transform t, Vector3 position, Quaternion rotation)
    {
        if (!t) return;

        var agent = t.GetComponent<NavMeshAgent>();
        if (agent != null && agent.enabled)
        {
            agent.Warp(position);
            t.rotation = rotation;
            return;
        }

        var rb = t.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        {
            rb.MovePosition(position);
            rb.MoveRotation(rotation);
            rb.WakeUp();
            return;
        }

        var cc = t.GetComponent<CharacterController>();
        if (cc != null)
        {
            bool was = cc.enabled;
            cc.enabled = false;
            t.SetPositionAndRotation(position, rotation);
            cc.enabled = was;
            return;
        }

        t.SetPositionAndRotation(position, rotation);
    }

    private Transform SafeFindByTag(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return null;
        try
        {
            var go = GameObject.FindGameObjectWithTag(tag);
            return go != null ? go.transform : null;
        }
        catch (UnityException)
        {
            Debug.LogWarning($"[CutsceneRunner] Tag '{tag}' is not defined.");
            return null;
        }
    }

    private static string GetHierarchyPath(Transform t)
    {
        if (t == null) return null;

        var stack = new Stack<string>();
        Transform cur = t;
        while (cur != null)
        {
            stack.Push(cur.name);
            cur = cur.parent;
        }

        return string.Join("/", stack.ToArray());
    }

    private static GameObject ResolveOwnerByPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var parts = path.Split('/');
        if (parts.Length == 0) return null;

        GameObject root = GameObject.Find(parts[0]);
        if (root == null) return null;

        Transform cur = root.transform;
        for (int i = 1; i < parts.Length; i++)
        {
            var child = cur.Find(parts[i]);
            if (child == null) return root;
            cur = child;
        }

        return cur != null ? cur.gameObject : root;
    }

    // -------------------------------------------------------
    // Reflection utilities (kept)
    // -------------------------------------------------------
    private MonoBehaviour FindFirstComponentByTypeName(string typeName)
    {
        var all = FindObjectsOfType<MonoBehaviour>(true);
        foreach (var mb in all)
            if (mb != null && mb.GetType().Name == typeName)
                return mb;
        return null;
    }

    private bool TryInvokeVoid(object obj, string methodName, params object[] args)
    {
        if (obj == null) return false;
        var t = obj.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var m = FindMethodBestMatch(t, methodName, args, flags);
        if (m == null) return false;

        try { m.Invoke(obj, args); return true; }
        catch { return false; }
    }

    private bool TryInvokeIEnumerator(object obj, string methodName, object arg0, out IEnumerator routine)
    {
        routine = null;
        if (obj == null) return false;

        var t = obj.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        foreach (var m in t.GetMethods(flags))
        {
            if (m.Name != methodName) continue;
            var ps = m.GetParameters();
            if (ps.Length != 1) continue;

            try
            {
                var res = m.Invoke(obj, new object[] { arg0 });
                routine = res as IEnumerator;
                return routine != null;
            }
            catch { return false; }
        }

        return false;
    }

    private void TrySetFieldOrProperty(object obj, string name, float value)
    {
        if (obj == null) return;
        var t = obj.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        var f = t.GetField(name, flags);
        if (f != null && (f.FieldType == typeof(float) || f.FieldType == typeof(int)))
        {
            try { f.SetValue(obj, value); } catch { }
            return;
        }

        var p = t.GetProperty(name, flags);
        if (p != null && p.CanWrite && (p.PropertyType == typeof(float) || p.PropertyType == typeof(int)))
        {
            try { p.SetValue(obj, value); } catch { }
        }
    }

    private MethodInfo FindMethodBestMatch(Type t, string name, object[] args, BindingFlags flags)
    {
        var methods = t.GetMethods(flags);

        foreach (var m in methods)
        {
            if (m.Name != name) continue;

            var ps = m.GetParameters();
            if (ps.Length != (args?.Length ?? 0)) continue;

            bool ok = true;
            for (int i = 0; i < ps.Length; i++)
            {
                if (args[i] == null) continue;
                if (!ps[i].ParameterType.IsAssignableFrom(args[i].GetType()))
                {
                    ok = false;
                    break;
                }
            }

            if (ok) return m;
        }

        return null;
    }

    private bool GetBoolProperty(object obj, string propName)
    {
        if (obj == null) return false;
        var t = obj.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        var p = t.GetProperty(propName, flags);
        if (p != null && p.PropertyType == typeof(bool))
        {
            try { return (bool)p.GetValue(obj); } catch { return false; }
        }

        var f = t.GetField(propName, flags);
        if (f != null && f.FieldType == typeof(bool))
        {
            try { return (bool)f.GetValue(obj); } catch { return false; }
        }

        return false;
    }

    private string GetStringField(object obj, string fieldName)
    {
        if (obj == null) return null;
        var t = obj.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        var f = t.GetField(fieldName, flags);
        if (f != null && f.FieldType == typeof(string))
        {
            try { return (string)f.GetValue(obj); } catch { return null; }
        }

        var p = t.GetProperty(fieldName, flags);
        if (p != null && p.PropertyType == typeof(string))
        {
            try { return (string)p.GetValue(obj); } catch { return null; }
        }

        return null;
    }

    private bool GetBoolField(object obj, string fieldName, bool fallback)
    {
        if (obj == null) return fallback;
        var t = obj.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        var f = t.GetField(fieldName, flags);
        if (f != null && f.FieldType == typeof(bool))
        {
            try { return (bool)f.GetValue(obj); } catch { return fallback; }
        }

        var p = t.GetProperty(fieldName, flags);
        if (p != null && p.PropertyType == typeof(bool))
        {
            try { return (bool)p.GetValue(obj); } catch { return fallback; }
        }

        return fallback;
    }

    private float GetFloatField(object obj, string fieldName, float fallback)
    {
        if (obj == null) return fallback;
        var t = obj.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        var f = t.GetField(fieldName, flags);
        if (f != null && (f.FieldType == typeof(float) || f.FieldType == typeof(int)))
        {
            try { return Convert.ToSingle(f.GetValue(obj)); } catch { return fallback; }
        }

        var p = t.GetProperty(fieldName, flags);
        if (p != null && (p.PropertyType == typeof(float) || p.PropertyType == typeof(int)))
        {
            try { return Convert.ToSingle(p.GetValue(obj)); } catch { return fallback; }
        }

        return fallback;
    }

    private Transform GetTransformField(object obj, string fieldName)
    {
        if (obj == null) return null;
        var t = obj.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        var f = t.GetField(fieldName, flags);
        if (f != null && typeof(Transform).IsAssignableFrom(f.FieldType))
        {
            try { return (Transform)f.GetValue(obj); } catch { return null; }
        }

        var p = t.GetProperty(fieldName, flags);
        if (p != null && typeof(Transform).IsAssignableFrom(p.PropertyType))
        {
            try { return (Transform)p.GetValue(obj); } catch { return null; }
        }

        return null;
    }
}
