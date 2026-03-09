using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Cutscene/Cutscene Sequence")]
public class CutsceneSequenceSO : ScriptableObject
{
    public CutsceneStep[] steps;
}

public enum CutsceneStepType
{
    PlayDialogue = 0,
    WaitSeconds = 1,
    SetObjective = 2,
    CompleteObjective = 3,
    CompleteObjectiveAndSet = 4,
    ShowTutorial = 5,

    TurnPlayerToHost = 6,
    TurnHostToPlayer = 7,
    TeleportPlayer = 8,
    TeleportHost = 9,
    MoveHostPath = 10,

    // ✅ ADD LAST (legacy-friendly)
    FadeBlackIn = 11,
    FadeBlackOut = 12,

    // ✅ optional newer unified step (also last)
    BlackTransition = 13,

    // ✅ NEW: minigame entry (add last)
    EnterMinigame = 14,

    // ✅ NEW: cutscene dialogue (add LAST)
    PlayCutsceneDialogue = 15
}

[Serializable]
public class CutsceneStep
{
    public CutsceneStepType type = CutsceneStepType.PlayDialogue;

    [Header("PlayDialogue")]
    public SimpleDialogueSequenceSO dialogue;
    public bool waitUntilDialogueEnds = true;
    public bool ensureDialogueUIVisible = true;

    [Header("ShowTutorial")]
    public TutorialSequenceSO tutorial;
    public bool waitUntilTutorialClosed = true;

    [Header("WaitSeconds")]
    public float waitSeconds = 0.25f;

    [Header("Objective")]
    public string objectiveId;

    [Header("Turn")]
    public float turnSpeed = 6f;
    public float angleTolerance = 3f;
    public Transform explicitLookTarget;

    [Header("Auto-Find Tags")]
    public string playerTag = "Player";
    public string hostTag = "Host";

    [Header("Teleport")]
    public Transform teleportTarget;
    public string ownerChildTargetName = "TeleportTarget";
    public bool includeInactiveChildren = true;

    [Header("Move Host Path")]
    public float moveSpeed = 3f;

    [Tooltip("Optional: assign a parent Transform whose CHILDREN are waypoint points (P1, P2, P3...).")]
    public Transform hostPathRoot;

    [Tooltip("Fallback: if hostPathRoot is null, CutsceneRunner can look for a child under the trigger/owner with this name.")]
    public string hostPathChildName = "HostPath";

    [Tooltip("If true, only uses direct children of the path root as points (recommended).")]
    public bool hostPathOnlyDirectChildren = true;

    [Header("Black Transition (SO)")]
    public BlackTransitionSO blackTransition;

    [Header("Legacy Fade Duration (backup)")]
    public float fadeDuration = 0.25f;

    // =========================
    // ✅ NEW: Enter Minigame
    // =========================
    [Header("Enter Minigame")]
    [Tooltip("Scene name to load for the minigame (must be added to Build Settings).")]
    public string minigameSceneName;

    [Tooltip("If empty, the runner will remember the current active scene and return there.")]
    public string returnSceneNameOverride;

    [Tooltip("If true, store Player & Host transform before leaving, then restore after returning.")]
    public bool restorePlayerAndHostOnReturn = true;

    [Tooltip("If true, also restore rotation (recommended).")]
    public bool restoreRotationOnReturn = true;

    [Tooltip("Optional: show a Pokemon-like flash/transition before loading the minigame.")]
    public BlackTransitionSO entryTransition;

    [Tooltip("Fallback duration if entryTransition is null.")]
    public float entryTransitionDuration = 0.35f;

    [Tooltip("Optional: transition when returning to the world scene.")]
    public BlackTransitionSO returnTransition;

    [Tooltip("Fallback duration if returnTransition is null.")]
    public float returnTransitionDuration = 0.25f;

    // =========================
    // ✅ NEW: PlayCutsceneDialogue
    // =========================
    [Header("PlayCutsceneDialogue")]
    [Tooltip("If true, uses CutsceneRunner.cutsceneDialogueUI for this step (different UI from normal PlayDialogue).")]
    public bool useCutsceneUI = true;

    [Tooltip("If true, does a white flash/fade BEFORE the cutscene dialogue starts.")]
    public bool whiteFlashBefore = true;

    [Tooltip("If true, does a white fade OUT after the cutscene dialogue ends.")]
    public bool whiteFadeAfter = true;

    [Tooltip("Fade duration for white flash in (0.25 - 0.6 feels good).")]
    public float whiteInDuration = 0.45f;

    [Tooltip("Fade duration for white flash back out.")]
    public float whiteOutDuration = 0.30f;

    [Tooltip("Optional: small pause while fully white (prevents flicker feeling).")]
    public float whiteHoldSeconds = 0.05f;

    // =========================
    // ✅ NEW: Cutscene BGM Override (NO scene change)
    // =========================
    [Header("Cutscene BGM Override (optional)")]
    [Tooltip("If assigned, BgmManager will temporarily switch to this clip ONLY for this PlayCutsceneDialogue step.")]
    public AudioClip cutsceneOverrideBgm;

    [Range(0f, 1f)]
    public float cutsceneOverrideVolume = 1f;

    public bool cutsceneOverrideLoop = true;

    [Tooltip("Fade out current BGM, then fade in override.")]
    public float cutsceneOverrideFadeOut = 0.4f;

    public float cutsceneOverrideFadeIn = 0.4f;

    [Tooltip("When this step ends, restore previous BGM with these fades.")]
    public float restorePrevBgmFadeOut = 0.25f;

    public float restorePrevBgmFadeIn = 0.25f;
}
