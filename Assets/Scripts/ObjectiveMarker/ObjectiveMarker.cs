using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(200)] // run AFTER most managers initialize
public class ObjectiveMarkerToggle : MonoBehaviour
{
    [Header("Target Objective")]
    [Tooltip("Marker shows only when this objective id is active.")]
    public string objectiveId;

    [Header("What to Toggle (pick one)")]
    [Tooltip("Drag ANY GameObject here (3D object, sprite object, UI root, etc). If empty, defaults to this.gameObject.")]
    public GameObject targetObject;

    [Tooltip("Optional: toggle a Renderer (SpriteRenderer / MeshRenderer). If set, this takes priority over targetObject.")]
    public Renderer targetRenderer;

    [Tooltip("If true, marker is visible when objectiveId is active.")]
    public bool showWhenActive = true;

    [Header("Startup")]
    [Tooltip("If true, we force-hide immediately in Awake before any refresh, to prevent 1-frame flicker on scene load.")]
    public bool startHidden = true;

    [Tooltip("If true, will also disable all child Renderers (helps if your marker has multiple renderers).")]
    public bool includeChildRenderers = true;

    [Tooltip("If true, the script will re-check visibility after the scene has finished loading (fixes stale active markers).")]
    public bool refreshOnSceneLoaded = true;

    [Tooltip("Extra safety: refresh again next frame after scene load (handles managers that update objective late).")]
    public bool refreshOneFrameLater = true;

    [Tooltip("Extra safety: refresh after a small realtime delay (handles async init / fade transitions).")]
    public bool refreshAfterDelay = false;

    [Min(0f)]
    public float refreshDelaySecondsRealtime = 0.05f;

    [Header("Load Safety (New)")]
    [Tooltip("Aggressively re-check CurrentObjectiveId for a short window after enable/scene load.\nThis fixes cases where save applies objective AFTER your first refresh without firing events.")]
    public bool aggressiveRefreshWindow = true;

    [Tooltip("Max realtime seconds to keep re-checking objective after load/enable.")]
    [Min(0f)]
    public float aggressiveWindowSecondsRealtime = 0.75f;

    [Tooltip("Also cap by frames (whichever ends first). 0 = no frame cap.")]
    [Min(0)]
    public int aggressiveWindowMaxFrames = 45;

    [Tooltip("How many consecutive checks with the SAME objective id before we consider it 'stable' and stop early.")]
    [Range(1, 10)]
    public int stableChecksToStop = 3;

    [Tooltip("If ON, we will NOT show the marker until we either (A) receive an OnObjectiveChanged event, or (B) we detect the objective id changed at least once during the refresh window.\nThis prevents markers for the chapter's default first objective from flashing ON during save load.")]
    public bool requireObjectiveChangeBeforeShowing = true;

    [Header("Debug")]
    public bool debugLogs = false;

    private bool _subscribed;
    private Renderer[] _childRenderers;
    private Coroutine _refreshRoutine;

    // Load-safety tracking
    private bool _sawObjectiveChange;
    private bool _receivedObjectiveEvent;
    private string _lastSeenObjectiveId;
    private int _stableCount;

    private void Awake()
    {
        if (targetObject == null) targetObject = gameObject;

        if (targetRenderer == null)
            targetRenderer = GetComponent<Renderer>();

        if (includeChildRenderers)
            _childRenderers = GetComponentsInChildren<Renderer>(true);

        if (startHidden)
            ApplyVisible(false);
    }

    private void OnEnable()
    {
        if (refreshOnSceneLoaded)
            SceneManager.sceneLoaded += HandleSceneLoaded;

        ResetLoadSafetyState();

        TrySubscribe();

        // Always do an initial safe refresh (but it may be “untrusted” during load)
        SafeRefreshNow();

        // Start aggressive refresh window to catch late-applied save objective
        StartAggressiveRefreshIfNeeded();
    }

    private void Start()
    {
        // In case ObjectiveManager appears after this object
        TrySubscribe();
        SafeRefreshNow();
        StartAggressiveRefreshIfNeeded();
    }

    private void OnDisable()
    {
        if (refreshOnSceneLoaded)
            SceneManager.sceneLoaded -= HandleSceneLoaded;

        Unsubscribe();
        StopRefreshRoutine();
    }

    private void OnDestroy()
    {
        if (refreshOnSceneLoaded)
            SceneManager.sceneLoaded -= HandleSceneLoaded;

        Unsubscribe();
        StopRefreshRoutine();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ResetLoadSafetyState();

        SafeRefreshNow();

        if (refreshOneFrameLater || refreshAfterDelay || aggressiveRefreshWindow)
        {
            StopRefreshRoutine();
            _refreshRoutine = StartCoroutine(RefreshAfterSceneLoadRoutine());
        }
    }

    private IEnumerator RefreshAfterSceneLoadRoutine()
    {
        if (refreshOneFrameLater)
            yield return null;

        if (refreshAfterDelay)
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, refreshDelaySecondsRealtime));

        SafeRefreshNow();

        // After the normal refreshes, do the aggressive window to catch late save apply
        StartAggressiveRefreshIfNeeded();

        _refreshRoutine = null;
    }

    private void StartAggressiveRefreshIfNeeded()
    {
        if (!aggressiveRefreshWindow) return;

        // If a routine is already running, don't stack
        if (_refreshRoutine != null) return;

        _refreshRoutine = StartCoroutine(AggressiveRefreshWindowRoutine());
    }

    private IEnumerator AggressiveRefreshWindowRoutine()
    {
        float start = Time.realtimeSinceStartup;
        int frames = 0;

        // During this window, we will keep checking even if no event fires.
        // We also track if the objective changed at least once.
        while (true)
        {
            TrySubscribe();

            string current = (ObjectiveManager.Instance != null)
                ? ObjectiveManager.Instance.CurrentObjectiveId
                : null;

            TrackObjectiveStability(current);

            // Visibility update uses the load-safety gating
            UpdateVisibilityWithLoadSafety(current);

            // Stop conditions
            bool timeUp = (aggressiveWindowSecondsRealtime > 0f) &&
                          (Time.realtimeSinceStartup - start >= aggressiveWindowSecondsRealtime);

            bool framesUp = (aggressiveWindowMaxFrames > 0) &&
                            (frames >= aggressiveWindowMaxFrames);

            bool stableEnough = (_stableCount >= stableChecksToStop);

            if (timeUp || framesUp || stableEnough)
                break;

            frames++;
            yield return null; // next frame
        }

        _refreshRoutine = null;

        if (debugLogs)
            Debug.Log($"[ObjectiveMarkerToggle] Aggressive window done ({name}). sawChange={_sawObjectiveChange}, gotEvent={_receivedObjectiveEvent}, stableCount={_stableCount}");
    }

    private void StopRefreshRoutine()
    {
        if (_refreshRoutine != null)
        {
            StopCoroutine(_refreshRoutine);
            _refreshRoutine = null;
        }
    }

    private void ResetLoadSafetyState()
    {
        _sawObjectiveChange = false;
        _receivedObjectiveEvent = false;
        _lastSeenObjectiveId = null;
        _stableCount = 0;

        // Safety: keep hidden until we know the right state
        if (startHidden)
            ApplyVisible(false);
    }

    private void TrackObjectiveStability(string currentObjectiveId)
    {
        string cur = currentObjectiveId ?? "";

        if (_lastSeenObjectiveId == null)
        {
            _lastSeenObjectiveId = cur;
            _stableCount = 1;
            return;
        }

        if (!string.Equals(_lastSeenObjectiveId, cur, StringComparison.Ordinal))
        {
            _sawObjectiveChange = true;
            _lastSeenObjectiveId = cur;
            _stableCount = 1;

            if (debugLogs)
                Debug.Log($"[ObjectiveMarkerToggle] Objective changed (polled) ({name}) -> '{cur}'");
        }
        else
        {
            _stableCount++;
        }
    }

    private void TrySubscribe()
    {
        if (_subscribed) return;
        if (ObjectiveManager.Instance == null) return;

        ObjectiveManager.Instance.OnObjectiveChanged += HandleObjectiveChanged;
        _subscribed = true;

        if (debugLogs)
            Debug.Log($"[ObjectiveMarkerToggle] Subscribed ({name})");
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;

        if (ObjectiveManager.Instance != null)
            ObjectiveManager.Instance.OnObjectiveChanged -= HandleObjectiveChanged;

        _subscribed = false;

        if (debugLogs)
            Debug.Log($"[ObjectiveMarkerToggle] Unsubscribed ({name})");
    }

    private void HandleObjectiveChanged(string currentObjectiveId)
    {
        _receivedObjectiveEvent = true;

        // Event is the most trustworthy signal that load/apply is done
        UpdateVisibilityWithLoadSafety(currentObjectiveId);
    }

    /// <summary>
    /// Safe refresh that handles ObjectiveManager not ready yet.
    /// Also re-attempts subscription.
    /// </summary>
    public void SafeRefreshNow()
    {
        TrySubscribe();

        if (ObjectiveManager.Instance == null)
        {
            ApplyVisible(false);

            if (debugLogs)
                Debug.Log($"[ObjectiveMarkerToggle] ObjectiveManager missing -> hide ({name})");

            return;
        }

        UpdateVisibilityWithLoadSafety(ObjectiveManager.Instance.CurrentObjectiveId);
    }

    private void UpdateVisibilityWithLoadSafety(string currentObjectiveId)
    {
        // If we can't confirm, hide.
        if (ObjectiveManager.Instance == null)
        {
            ApplyVisible(false);
            return;
        }

        bool isActive = string.Equals(currentObjectiveId ?? "", objectiveId ?? "", StringComparison.Ordinal);
        bool shouldBeVisible = showWhenActive ? isActive : !isActive;

        // ✅ Load gating: prevent “default first objective” markers from showing during load
        if (requireObjectiveChangeBeforeShowing)
        {
            // Until we see a real signal that the objective has “settled”, keep hidden.
            // We consider it “trusted” if:
            // - We received an objective-changed event, OR
            // - We observed the objective id change at least once during the aggressive window, OR
            // - We reached stability checks (meaning the value is likely final now)
            bool trusted =
                _receivedObjectiveEvent ||
                _sawObjectiveChange ||
                (_stableCount >= stableChecksToStop);

            if (!trusted)
            {
                if (debugLogs)
                    Debug.Log($"[ObjectiveMarkerToggle] Untrusted objective during load -> force hide ({name}) cur='{currentObjectiveId}'");
                ApplyVisible(false);
                return;
            }
        }

        if (debugLogs)
            Debug.Log($"[ObjectiveMarkerToggle] {name} objective='{objectiveId}' current='{currentObjectiveId}' -> visible={shouldBeVisible}");

        ApplyVisible(shouldBeVisible);
    }

    private void ApplyVisible(bool visible)
    {
        // Priority 1: renderer toggle
        if (targetRenderer != null)
        {
            targetRenderer.enabled = visible;

            if (includeChildRenderers && _childRenderers != null)
            {
                for (int i = 0; i < _childRenderers.Length; i++)
                {
                    if (_childRenderers[i] == null) continue;
                    _childRenderers[i].enabled = visible;
                }
            }
            return;
        }

        // Priority 2: whole GameObject
        if (targetObject != null)
            targetObject.SetActive(visible);
    }
}