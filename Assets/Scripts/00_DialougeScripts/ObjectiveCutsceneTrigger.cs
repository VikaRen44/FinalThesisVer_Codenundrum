using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ObjectiveCutsceneTrigger : MonoBehaviour
{
    [Header("Cutscene")]
    public CutsceneSequenceSO cutscene;

    [Header("Optional Host (NPC)")]
    [Tooltip("Assign if you want steps to reference this host. (CutsceneRunner will still also support tag-based finding.)")]
    public Transform host; // kept so your inspector doesn't break

    [Header("Objective Gate")]
    [Tooltip("Cutscene only plays when current objective matches this ID. Empty = always.")]
    public string requiredObjectiveId;

    [Header("Behavior")]
    public bool oneShot = true;

    [Tooltip("If true, disables only THIS collider after play (not the whole gameObject).")]
    public bool disableColliderAfterPlay = true;

    [Header("Load Safety (IMPORTANT)")]
    [Tooltip("If true, this trigger will NOT fire while SaveGameManager is loading/applying a snapshot.")]
    public bool blockWhileLoading = true;

    [Tooltip("Extra frames to wait after entering trigger before checking objective (prevents load race condition).")]
    public int waitFramesBeforeCheck = 2;

    [Tooltip("If true, and the required objective is already passed, never play.")]
    public bool blockIfObjectiveAlreadyPassed = true;

    [Header("Player")]
    public string playerTag = "Player";

    [Header("Debug")]
    public bool verboseLogs = true;

    private bool _playedThisSession;
    private bool _isTryingToPlay;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        if (cutscene == null) return;

        TryStart(other.transform);
    }

    // Safety: if player loads INSIDE collider, OnTriggerEnter may not fire.
    private void OnTriggerStay(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        if (cutscene == null) return;
        if (oneShot && _playedThisSession) return;
        if (_isTryingToPlay) return;

        // avoid spamming: only start if currently eligible
        if (!ObjectiveMatchesFast()) return;

        TryStart(other.transform);
    }

    private void TryStart(Transform player)
    {
        if (oneShot && _playedThisSession) return;
        if (_isTryingToPlay) return;

        // prevent starting while another cutscene is playing
        if (CutsceneRunner.Instance != null && CutsceneRunner.Instance.IsPlaying)
            return;

        _isTryingToPlay = true;
        StartCoroutine(TryPlayRoutine(player));
    }

    private IEnumerator TryPlayRoutine(Transform player)
    {
        // Block while save/load applying
        if (blockWhileLoading && SaveGameManager.Instance != null && SaveGameManager.Instance.IsLoadInProgress)
        {
            if (verboseLogs) Debug.Log($"[ObjectiveCutsceneTrigger] '{name}' blocked: Save load in progress.");
            _isTryingToPlay = false;
            yield break;
        }

        // Wait a couple frames so ObjectiveManager.ImportSaveStateJson has applied
        int frames = Mathf.Max(0, waitFramesBeforeCheck);
        for (int i = 0; i < frames; i++)
            yield return null;

        // If loading starts during those frames, still block
        if (blockWhileLoading && SaveGameManager.Instance != null && SaveGameManager.Instance.IsLoadInProgress)
        {
            if (verboseLogs) Debug.Log($"[ObjectiveCutsceneTrigger] '{name}' blocked: Save load became active.");
            _isTryingToPlay = false;
            yield break;
        }

        // If ObjectiveManager hasn't finished loading state, don't trust it yet
        if (ObjectiveManager.Instance != null && !ObjectiveManager.Instance.HasLoadedState)
        {
            if (verboseLogs) Debug.Log($"[ObjectiveCutsceneTrigger] '{name}' blocked: ObjectiveManager state not ready yet.");
            _isTryingToPlay = false;
            yield break;
        }

        if (!ObjectiveMatches())
        {
            if (verboseLogs)
                Debug.Log($"[ObjectiveCutsceneTrigger] '{name}' blocked: objective mismatch (current='{ObjectiveManager.Instance?.GetCurrentObjectiveId()}').");

            _isTryingToPlay = false;
            yield break;
        }

        if (blockIfObjectiveAlreadyPassed && ObjectiveManager.Instance != null && !string.IsNullOrEmpty(requiredObjectiveId))
        {
            if (ObjectiveManager.Instance.IsObjectivePassed(requiredObjectiveId))
            {
                if (verboseLogs) Debug.Log($"[ObjectiveCutsceneTrigger] '{name}' blocked: objective already passed ('{requiredObjectiveId}').");
                _isTryingToPlay = false;
                yield break;
            }
        }

        if (verboseLogs) Debug.Log($"[ObjectiveCutsceneTrigger] '{name}' START cutscene '{cutscene.name}'");

        _playedThisSession = true;

        if (CutsceneRunner.Instance != null)
        {
            yield return CutsceneRunner.Instance.Play(cutscene, owner: gameObject, host: host, player: player);
        }
        else
        {
            Debug.LogError("[ObjectiveCutsceneTrigger] No CutsceneRunner.Instance found in the scene.");
        }

        if (disableColliderAfterPlay)
        {
            var col = GetComponent<Collider>();
            if (col) col.enabled = false;
        }

        _isTryingToPlay = false;
    }

    // fast check for OnTriggerStay to avoid excessive coroutines
    private bool ObjectiveMatchesFast()
    {
        if (ObjectiveManager.Instance == null) return true;
        if (string.IsNullOrEmpty(requiredObjectiveId)) return true;
        if (!ObjectiveManager.Instance.HasLoadedState) return false;
        return ObjectiveManager.Instance.GetCurrentObjectiveId() == requiredObjectiveId;
    }

    private bool ObjectiveMatches()
    {
        if (ObjectiveManager.Instance == null) return true;
        if (string.IsNullOrEmpty(requiredObjectiveId)) return true;
        return ObjectiveManager.Instance.GetCurrentObjectiveId() == requiredObjectiveId;
    }
}

