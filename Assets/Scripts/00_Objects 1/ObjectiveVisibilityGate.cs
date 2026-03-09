using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(210)] // after most managers
public class ObjectiveVisibilityGate : MonoBehaviour
{
    public enum GateMode
    {
        /// <summary>
        /// Target is ENABLED when current objective matches requiredObjectiveId.
        /// With optional "latch": once enabled, it can stay enabled until disableForeverWhenObjectiveActiveId.
        /// </summary>
        ActiveOnlyWhenObjective = 0,

        /// <summary>
        /// Target stays ENABLED until "disableWhenObjectiveActiveId" becomes the CURRENT objective,
        /// then disables forever (FOR THIS RUN ONLY).
        /// </summary>
        ActiveUntilObjectiveThenDisableForever = 1
    }

    [Header("Mode")]
    public GateMode mode = GateMode.ActiveOnlyWhenObjective;

    [Header("Target To Toggle (Recommended)")]
    [Tooltip(
        "What to enable/disable.\n" +
        "RECOMMENDED: assign a CHILD object (door mesh root) here.\n" +
        "Do NOT let this script disable the GameObject it lives on."
    )]
    public GameObject targetToToggle;

    [Header("Toggle Strategy (Safety)")]
    [Tooltip("If targetToToggle is THIS SAME object, SetActive(false) would kill this script. When true, we toggle Renderers/Colliders instead in that case.")]
    public bool useComponentToggleIfTargetIsSelf = true;

    [Tooltip("Used only when component toggle safety is active.")]
    public bool toggleRenderers = true;

    [Tooltip("Used only when component toggle safety is active.")]
    public bool toggleColliders = true;

    // =========================================================
    // MODE: ActiveOnlyWhenObjective
    // =========================================================
    [Header("Objective Gate (Mode: ActiveOnlyWhenObjective)")]
    [Tooltip("Target becomes ACTIVE when the current objective matches this ID.")]
    public string requiredObjectiveId;

    [Tooltip("If true, target stays disabled once objective is passed (legacy behavior).")]
    public bool disableAfterObjectivePassed = false;

    [Tooltip("When THIS objective becomes CURRENT, target disables forever (this run only).")]
    public string disableForeverWhenObjectiveActiveId;

    [Tooltip("NEW: If true, also disable forever if disableForeverWhenObjectiveActiveId is already PASSED on load.")]
    public bool alsoDisableForeverIfDisableObjectivePassed = true;

    [Tooltip(
        "If true, once the required objective becomes active at least once, the target stays ACTIVE\n" +
        "until the disableForeverWhenObjectiveActiveId becomes current (then it disables forever)."
    )]
    public bool stayActiveOnceEnabledUntilDisableForever = true;

    [Tooltip(
        "If true, and requiredObjectiveId is already PASSED on load, treat it as if it was enabled before (latch becomes true).\n" +
        "Helps keep behavior consistent after loading mid-run."
    )]
    public bool inferLatchFromRequiredObjectivePassed = true;

    // =========================================================
    // MODE: ActiveUntilObjectiveThenDisableForever
    // =========================================================
    [Header("Disable Forever Gate (Mode: ActiveUntilObjectiveThenDisableForever)")]
    [Tooltip("When THIS objective becomes the CURRENT active objective, target disables forever (this run only).")]
    public string disableWhenObjectiveActiveId;

    [Tooltip("If true, also disable forever if the objective is already passed when loading.")]
    public bool alsoDisableForeverIfObjectivePassed = true;

    // =========================================================
    // SAFETY + ROBUSTNESS
    // =========================================================
    [Header("Safety")]
    [Tooltip("Disable target while ObjectiveManager state is not ready.")]
    public bool disableUntilObjectiveLoaded = true;

    [Tooltip("Force-hide immediately in Awake/OnEnable before any evaluation (prevents objects staying active during load).")]
    public bool startHidden = true;

    [Header("Robust Load Handling")]
    [Tooltip("Re-check visibility repeatedly for a short window after enabling/scene load to catch late-applied save state.")]
    public bool aggressiveRefreshWindow = true;

    [Min(0f)]
    [Tooltip("How many realtime seconds to keep re-checking after enable/scene load.")]
    public float aggressiveWindowSecondsRealtime = 0.75f;

    [Min(0)]
    [Tooltip("Also cap by frames (whichever ends first). 0 = no frame cap.")]
    public int aggressiveWindowMaxFrames = 45;

    [Range(1, 10)]
    [Tooltip("Stop early once the objective id has stayed the same for this many checks.")]
    public int stableChecksToStop = 3;

    [Tooltip("Refresh again whenever a new scene is loaded (recommended if you load saves from main menu/hub).")]
    public bool refreshOnSceneLoaded = true;

    [Tooltip("Optional fallback polling even after the aggressive window (OFF by default).")]
    public bool enableSlowPolling = false;

    [Min(0.05f)]
    public float slowPollIntervalSecondsRealtime = 0.25f;

    // =========================================================
    // DEBUG
    // =========================================================
    [Header("Debug")]
    public bool verboseLogs = false;

    // Runtime-only permanent disable flag
    private bool _permanentlyDisabledThisRun;

    // Latch flag (for ActiveOnlyWhenObjective)
    private bool _wasEverEnabledByRequiredObjective;

    // cached components for self-toggle
    private Renderer[] _cachedRenderers;
    private Collider[] _cachedColliders;

    // routines
    private Coroutine _refreshRoutine;
    private Coroutine _slowPollRoutine;

    // stability tracking
    private string _lastSeenObjectiveId;
    private int _stableCount;

    // subscription
    private bool _subscribed;

    private void Reset()
    {
        targetToToggle = gameObject;
    }

    private void Awake()
    {
        if (targetToToggle == null)
            targetToToggle = gameObject;

        CacheSelfToggleComponents();

        if (startHidden)
            SetTargetActiveSafe(false);
    }

    private void OnEnable()
    {
        if (refreshOnSceneLoaded)
            SceneManager.sceneLoaded += HandleSceneLoaded;

        ResetRunFlagsForSafety();

        TrySubscribe();

        // Always evaluate once
        Evaluate();

        // Aggressive refresh to catch late-applied save objective/passed flags
        StartAggressiveRefresh();

        // Optional slow polling fallback
        StartSlowPollingIfNeeded();
    }

    private void OnDisable()
    {
        if (refreshOnSceneLoaded)
            SceneManager.sceneLoaded -= HandleSceneLoaded;

        Unsubscribe();
        StopAllRoutines();
    }

    private void OnDestroy()
    {
        if (refreshOnSceneLoaded)
            SceneManager.sceneLoaded -= HandleSceneLoaded;

        Unsubscribe();
        StopAllRoutines();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // When loading from main menu/hub, objects can spawn active before objective state is applied.
        // Force safe state and run refresh window again.
        ResetRunFlagsForSafety();
        Evaluate();
        StartAggressiveRefresh();
    }

    private void ResetRunFlagsForSafety()
    {
        _lastSeenObjectiveId = null;
        _stableCount = 0;

        // IMPORTANT: Do NOT reset _permanentlyDisabledThisRun unless you intentionally want it reset on scene load.
        // This is "this run only", so scene changes are still part of the same run; keep it.

        // Latch should not be blindly reset if we're loading mid-run. We'll infer from passed objective if enabled.
        // But if you want latch reset on every enable, uncomment:
        // _wasEverEnabledByRequiredObjective = false;

        if (startHidden)
            SetTargetActiveSafe(false);
    }

    private void CacheSelfToggleComponents()
    {
        if (!useComponentToggleIfTargetIsSelf) return;

        // Only needed when target == self, but caching is cheap and avoids repeated GetComponents calls.
        if (toggleRenderers)
            _cachedRenderers = GetComponentsInChildren<Renderer>(true);

        if (toggleColliders)
            _cachedColliders = GetComponentsInChildren<Collider>(true);
    }

    private void StopAllRoutines()
    {
        if (_refreshRoutine != null)
        {
            StopCoroutine(_refreshRoutine);
            _refreshRoutine = null;
        }

        if (_slowPollRoutine != null)
        {
            StopCoroutine(_slowPollRoutine);
            _slowPollRoutine = null;
        }
    }

    private void StartAggressiveRefresh()
    {
        if (!aggressiveRefreshWindow) return;

        if (_refreshRoutine != null)
            StopCoroutine(_refreshRoutine);

        _refreshRoutine = StartCoroutine(AggressiveRefreshRoutine());
    }

    private IEnumerator AggressiveRefreshRoutine()
    {
        float start = Time.realtimeSinceStartup;
        int frames = 0;

        while (true)
        {
            Evaluate();

            // stop early if objective id seems stable and OM is ready
            var om = ObjectiveManager.Instance;
            if (om != null && om.HasLoadedState)
            {
                string cur = om.GetCurrentObjectiveId() ?? "";
                TrackStability(cur);

                if (_stableCount >= stableChecksToStop)
                    break;
            }

            bool timeUp = (aggressiveWindowSecondsRealtime > 0f) &&
                          (Time.realtimeSinceStartup - start >= aggressiveWindowSecondsRealtime);

            bool framesUp = (aggressiveWindowMaxFrames > 0) &&
                            (frames >= aggressiveWindowMaxFrames);

            if (timeUp || framesUp)
                break;

            frames++;
            yield return null;
        }

        if (verboseLogs)
            Debug.Log($"[ObjectiveVisibilityGate] Aggressive refresh done ({name}). stableCount={_stableCount}");

        _refreshRoutine = null;
    }

    private void TrackStability(string current)
    {
        if (_lastSeenObjectiveId == null)
        {
            _lastSeenObjectiveId = current;
            _stableCount = 1;
            return;
        }

        if (!string.Equals(_lastSeenObjectiveId, current, StringComparison.Ordinal))
        {
            _lastSeenObjectiveId = current;
            _stableCount = 1;
        }
        else
        {
            _stableCount++;
        }
    }

    private void StartSlowPollingIfNeeded()
    {
        if (!enableSlowPolling) return;

        if (_slowPollRoutine != null)
            StopCoroutine(_slowPollRoutine);

        _slowPollRoutine = StartCoroutine(SlowPollRoutine());
    }

    private IEnumerator SlowPollRoutine()
    {
        while (true)
        {
            Evaluate();
            yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, slowPollIntervalSecondsRealtime));
        }
    }

    private void TrySubscribe()
    {
        if (_subscribed) return;
        if (ObjectiveManager.Instance == null) return;

        ObjectiveManager.Instance.OnObjectiveChanged += HandleObjectiveChanged;
        _subscribed = true;

        if (verboseLogs)
            Debug.Log($"[ObjectiveVisibilityGate] Subscribed ({name})");
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;

        if (ObjectiveManager.Instance != null)
            ObjectiveManager.Instance.OnObjectiveChanged -= HandleObjectiveChanged;

        _subscribed = false;
    }

    private void HandleObjectiveChanged(string currentObjectiveId)
    {
        // Event-driven evaluation (most reliable)
        Evaluate();
    }

    private void Evaluate()
    {
        // Already disabled forever this run
        if (_permanentlyDisabledThisRun)
        {
            SetTargetActiveSafe(false);
            return;
        }

        var om = ObjectiveManager.Instance;

        // Objective system not ready
        if (om == null || !om.HasLoadedState)
        {
            // Safety: NEVER leave target active while state is unknown (this is the bug you described).
            if (disableUntilObjectiveLoaded)
                SetTargetActiveSafe(false);

            // Keep trying to subscribe if OM appears later
            TrySubscribe();
            return;
        }

        // Now OM is ready
        string current = om.GetCurrentObjectiveId() ?? "";

        // =====================================================
        // MODE: ActiveUntilObjectiveThenDisableForever
        // =====================================================
        if (mode == GateMode.ActiveUntilObjectiveThenDisableForever)
        {
            if (!string.IsNullOrEmpty(disableWhenObjectiveActiveId))
            {
                if (alsoDisableForeverIfObjectivePassed && om.IsObjectivePassed(disableWhenObjectiveActiveId))
                {
                    DisableForever($"objective already passed: {disableWhenObjectiveActiveId}");
                    return;
                }

                if (string.Equals(current, disableWhenObjectiveActiveId, StringComparison.Ordinal))
                {
                    DisableForever($"objective became active: {disableWhenObjectiveActiveId}");
                    return;
                }
            }

            SetTargetActiveSafe(true);
            return;
        }

        // =====================================================
        // MODE: ActiveOnlyWhenObjective (with latch)
        // =====================================================

        // If disable-forever objective is already passed on load, disable immediately (fixes your save-load bug)
        if (!string.IsNullOrEmpty(disableForeverWhenObjectiveActiveId) && alsoDisableForeverIfDisableObjectivePassed)
        {
            if (om.IsObjectivePassed(disableForeverWhenObjectiveActiveId))
            {
                DisableForever($"disable-forever objective already passed: {disableForeverWhenObjectiveActiveId}");
                return;
            }
        }

        // Disable forever if that objective becomes CURRENT
        if (!string.IsNullOrEmpty(disableForeverWhenObjectiveActiveId))
        {
            if (string.Equals(current, disableForeverWhenObjectiveActiveId, StringComparison.Ordinal))
            {
                DisableForever($"disable-forever objective became active: {disableForeverWhenObjectiveActiveId}");
                return;
            }
        }

        // No requirement → always visible (but still respects disable-forever logic above)
        if (string.IsNullOrEmpty(requiredObjectiveId))
        {
            SetTargetActiveSafe(true);
            return;
        }

        // Optional: infer latch from "required objective passed" (helps on load)
        if (inferLatchFromRequiredObjectivePassed && om.IsObjectivePassed(requiredObjectiveId))
            _wasEverEnabledByRequiredObjective = true;

        // Legacy behavior: permanently disable if required objective already passed
        if (disableAfterObjectivePassed && om.IsObjectivePassed(requiredObjectiveId))
        {
            SetTargetActiveSafe(false);
            return;
        }

        bool matchesRequired = string.Equals(current, requiredObjectiveId, StringComparison.Ordinal);

        if (matchesRequired)
        {
            _wasEverEnabledByRequiredObjective = true;
            SetTargetActiveSafe(true);
            return;
        }

        if (stayActiveOnceEnabledUntilDisableForever && _wasEverEnabledByRequiredObjective)
        {
            SetTargetActiveSafe(true);
            return;
        }

        SetTargetActiveSafe(false);
    }

    private void DisableForever(string reason)
    {
        _permanentlyDisabledThisRun = true;
        SetTargetActiveSafe(false);

        if (verboseLogs)
            Debug.Log($"[ObjectiveVisibilityGate] '{name}' DISABLED FOREVER (this run) – {reason}");
    }

    private void SetTargetActiveSafe(bool shouldBeActive)
    {
        if (targetToToggle == null) return;

        // SAFETY: if target is the same object as this script, SetActive(false) kills the script.
        // In that case, optionally toggle renderers/colliders instead.
        if (useComponentToggleIfTargetIsSelf && targetToToggle == gameObject)
        {
            if (toggleRenderers && _cachedRenderers != null)
            {
                for (int i = 0; i < _cachedRenderers.Length; i++)
                {
                    if (_cachedRenderers[i] == null) continue;
                    _cachedRenderers[i].enabled = shouldBeActive;
                }
            }

            if (toggleColliders && _cachedColliders != null)
            {
                for (int i = 0; i < _cachedColliders.Length; i++)
                {
                    if (_cachedColliders[i] == null) continue;
                    _cachedColliders[i].enabled = shouldBeActive;
                }
            }

            if (verboseLogs)
                Debug.Log($"[ObjectiveVisibilityGate] '{name}' (self-target) visible={shouldBeActive}");

            return;
        }

        if (targetToToggle.activeSelf == shouldBeActive)
            return;

        targetToToggle.SetActive(shouldBeActive);

        if (verboseLogs)
            Debug.Log($"[ObjectiveVisibilityGate] '{name}' target='{targetToToggle.name}' active={shouldBeActive}");
    }
}