using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CameraFollow : MonoBehaviour
{
    [Header("Auto Target")]
    public bool autoFindTarget = true;
    public string playerTag = "Player";
    public float refindInterval = 0.25f;
    private float _nextRefindTimeUnscaled = 0f;

    [Header("Target")]
    [SerializeField] private Transform target;

    [Header("Centering / Focus Point (IMPORTANT for isometric)")]
    public Vector3 focusOffset = new Vector3(0f, 1.0f, 0f);

    [Header("Offset")]
    public Vector3 offset;
    public bool autoComputeOffsetIfZero = true;

    [Header("Isometric Helper (optional)")]
    public bool useDefaultIsometricOffsetIfOffsetZero = false;
    public Vector3 defaultIsometricOffset = new Vector3(-8f, 10f, -8f);

    [Header("Follow Smoothing")]
    public bool smoothFollow = true;
    public float smoothTime = 6f;

    [Header("Vertical Damp (stairs help)")]
    public bool dampVertical = true;
    public float verticalDampTime = 4f;

    [Header("Snap Settings")]
    public float snapDistance = 3f;

    [Header("Spawn/Scene Load Stabilizer (recommended)")]
    public bool snapAfterSettle = true;
    public int settleFixedFrames = 3;
    public bool waitForGroundedBeforeSnap = true;
    public float groundedCheckDistance = 3.0f;
    public LayerMask groundMask = ~0;
    public bool waitForLowVelocityBeforeSnap = true;
    public float settledVelocity = 0.15f;

    [Header("Anti-Clipping / Collision Avoidance (RECOMMENDED)")]
    public bool avoidClipping = true;
    public LayerMask clipMask = ~0;
    public float clipSphereRadius = 0.35f;
    public float clipPadding = 0.10f;
    public float minDistanceFromTarget = 1.0f;

    // ===========================
    // ✅ CAMERA SETTINGS LOCK
    // ===========================
    [Header("Camera Settings Lock (Fixes GameHub → Chapter skew)")]
    public bool enforceSceneCameraSettings = true;
    public bool enforceRotation = true;
    public bool enforceProjection = true;
    public int enforceFramesAfterEnable = 2;

    private Camera _cam;
    private Quaternion _sceneRotation;
    private bool _sceneIsOrtho;
    private float _sceneOrthoSize;
    private float _sceneFov;

    // internal state
    private Vector3 _velXZ = Vector3.zero;
    private float _velY = 0f;
    private bool _forceSnapThisFrame = false;

    private Vector3 _sceneStartCamPos;
    private bool _didComputeOffsetOnce = false;

    private bool _offsetWasAutoComputed = false;
    private int _lastSceneHandle = -1;

    private Coroutine _settleRoutine;
    private Coroutine _delayedAcquireRoutine;
    private Coroutine _enforceRoutine;

    // ===========================
    // ✅ JITTER FIX: SMOOTH THE FOCUS POINT ITSELF
    // ===========================
    [Header("Jitter Fix: Smooth Focus Point (Recommended)")]
    [Tooltip("Smooths the target focus point separately (fixes jitter when CharacterController snaps/settles).")]
    public bool smoothFocusPoint = true;

    [Tooltip("How quickly the focus point catches up on XZ (seconds). Smaller = smoother/less jitter.")]
    public float focusSmoothTimeXZ = 0.10f;

    [Tooltip("How quickly the focus point catches up on Y (seconds). Smaller = smoother/less jitter.")]
    public float focusSmoothTimeY = 0.14f;

    [Tooltip("Optional cap to prevent sudden huge jumps (0 = no cap).")]
    public float maxFocusSpeed = 0f;

    // ===========================
    // ✅ JITTER FIX: USE SmoothDamp FOR CAMERA POS
    // ===========================
    [Header("Jitter Fix: Camera Position Integrator (Recommended)")]
    [Tooltip("Use SmoothDamp for camera position (more stable if the scene hitches during load).")]
    public bool useSmoothDampForCamera = true;

    [Tooltip("If true, camera smoothing uses unscaled delta time (ignores timeScale spikes/pauses).")]
    public bool useUnscaledDeltaTimeForCamera = true;

    [Tooltip("Caps how fast the camera can move (0 = unlimited). Helps prevent overshoot on spawn).")]
    public float maxCameraSpeed = 0f;

    private Vector3 _camSmoothVel;

    // ===========================
    // ✅ Spawn Window (No snap while things still load)
    // ===========================
    [Header("Spawn Window (No snap while things still load)")]
    public float initialNoSnapSeconds = 0.60f;
    public float initialSnapDistanceMultiplier = 6.0f;
    public bool convertSpawnSnapsToSoftSnap = true;
    public float softSnapDuration = 0.25f;
    private float _spawnPhaseEndUnscaled = 0f;

    // ===========================
    // ✅ LOAD/SAVE JITTER FIX (NEW): Teleport detection + stabilize mode
    // ===========================
    [Header("Load/Save Stabilization (NEW)")]
    [Tooltip("Detects big position jumps (teleport) during load/save and stabilizes the camera before snapping.")]
    public bool stabilizeOnTeleportJump = true;

    [Tooltip("If focus moves more than this in 1 frame, treat it like a teleport.")]
    public float teleportJumpDistance = 2.5f;

    [Tooltip("How many frames to wait before checking stability (lets spawns/teleports finish).")]
    public int postTeleportIgnoreFrames = 2;

    [Tooltip("How many consecutive frames focus must be 'stable' before we soft-snap.")]
    public int stableFramesRequired = 4;

    [Tooltip("Focus movement below this per frame counts as stable.")]
    public float stableDeltaThreshold = 0.02f;

    [Tooltip("Maximum time (seconds) to wait for stability before giving up and snapping anyway.")]
    public float stabilizeTimeoutSeconds = 1.0f;

    [Tooltip("If true, while stabilizing we keep camera position but update scene camera settings if enabled.")]
    public bool keepCameraStillWhileStabilizing = true;

    private bool _stabilizing = false;
    private int _stabilizeIgnoreLeft = 0;
    private int _stableFrameCount = 0;
    private float _stabilizeTimeoutAtUnscaled = -1f;
    private Vector3 _lastRawFocusForTeleport = Vector3.zero;
    private bool _hasLastRawFocusForTeleport = false;

    // soft snap state
    private bool _softSnapping = false;
    private float _softSnapT0 = 0f;
    private float _softSnapT1 = 0f;
    private Vector3 _softFrom;
    private Vector3 _softTo;

    // focus smoothing state
    private Vector3 _smoothedFocus;
    private Vector3 _focusVel;
    private bool _hasFocusInit = false;

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        CaptureSceneDefaultsForThisScene(forceResetAutoOffset: true);
    }

    private void OnEnable()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;

        _forceSnapThisFrame = true;
        _spawnPhaseEndUnscaled = Time.unscaledTime + Mathf.Max(0f, initialNoSnapSeconds);

        // ✅ always begin stabilize window on enable (covers scene enter)
        BeginStabilizeWindow();

        if (enforceSceneCameraSettings)
            StartEnforceRoutine();

        if (_delayedAcquireRoutine != null) StopCoroutine(_delayedAcquireRoutine);
        _delayedAcquireRoutine = StartCoroutine(DelayedAcquire());
    }

    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    private void Start()
    {
        if (enforceSceneCameraSettings)
            StartEnforceRoutine();

        TryAutoFindTarget(force: true);

        if (_delayedAcquireRoutine != null) StopCoroutine(_delayedAcquireRoutine);
        _delayedAcquireRoutine = StartCoroutine(DelayedAcquire());

        if (target != null && autoComputeOffsetIfZero && offset == Vector3.zero && !_didComputeOffsetOnce)
        {
            if (useDefaultIsometricOffsetIfOffsetZero)
                ForceDefaultOffsetNow();
            else
                offset = _sceneStartCamPos - GetRawFocusPoint();

            _didComputeOffsetOnce = true;
            _offsetWasAutoComputed = true;
        }

        if (useDefaultIsometricOffsetIfOffsetZero && offset == Vector3.zero && !_didComputeOffsetOnce)
        {
            ForceDefaultOffsetNow();
            _didComputeOffsetOnce = true;
            _offsetWasAutoComputed = true;
        }

        if (target != null) ResetSmoothedFocusToRaw();
        _camSmoothVel = Vector3.zero;
    }

    private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
    {
        CaptureSceneDefaultsForThisScene(forceResetAutoOffset: true);

        _spawnPhaseEndUnscaled = Time.unscaledTime + Mathf.Max(0f, initialNoSnapSeconds);

        // ✅ stabilize on scene switch too
        BeginStabilizeWindow();

        TryAutoFindTarget(force: true);
        StartSettleSnapRoutine();
    }

    private void CaptureSceneDefaultsForThisScene(bool forceResetAutoOffset)
    {
        Scene s = SceneManager.GetActiveScene();
        _lastSceneHandle = s.handle;

        if (_cam == null) _cam = GetComponent<Camera>();

        _sceneRotation = transform.rotation;

        if (_cam != null)
        {
            _sceneIsOrtho = _cam.orthographic;
            _sceneOrthoSize = _cam.orthographicSize;
            _sceneFov = _cam.fieldOfView;
        }

        _sceneStartCamPos = transform.position;

        if (forceResetAutoOffset && _offsetWasAutoComputed)
        {
            offset = Vector3.zero;
            _didComputeOffsetOnce = false;
        }

        _forceSnapThisFrame = true;

        _softSnapping = false;
        _hasFocusInit = false;
        _focusVel = Vector3.zero;
        _camSmoothVel = Vector3.zero;

        _hasLastRawFocusForTeleport = false;
    }

    private IEnumerator DelayedAcquire()
    {
        yield return null;

        if (enforceSceneCameraSettings)
            ApplySceneCameraSettings();

        TryAutoFindTarget(force: true);

        if (target != null)
        {
            ResetSmoothedFocusToRaw();
            _camSmoothVel = Vector3.zero;

            // ✅ entering target after delay: stabilize again (covers save-load teleports)
            BeginStabilizeWindow();

            StartSettleSnapRoutine();
        }
    }

    private void LateUpdate()
    {
        int currentHandle = SceneManager.GetActiveScene().handle;
        if (currentHandle != _lastSceneHandle)
            CaptureSceneDefaultsForThisScene(forceResetAutoOffset: true);

        TryAutoFindTarget(force: false);
        if (!target) return;

        if (autoComputeOffsetIfZero && offset == Vector3.zero && !_didComputeOffsetOnce)
        {
            if (useDefaultIsometricOffsetIfOffsetZero)
                ForceDefaultOffsetNow();
            else
                offset = _sceneStartCamPos - GetRawFocusPoint();

            _didComputeOffsetOnce = true;
            _offsetWasAutoComputed = true;
            _forceSnapThisFrame = true;
        }

        // Soft snap in progress
        if (_softSnapping)
        {
            float t = (_softSnapT1 <= _softSnapT0) ? 1f : Mathf.InverseLerp(_softSnapT0, _softSnapT1, Time.unscaledTime);
            float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            transform.position = Vector3.Lerp(_softFrom, _softTo, eased);

            if (t >= 1f)
            {
                _softSnapping = false;
                _velXZ = Vector3.zero;
                _velY = 0f;
                _forceSnapThisFrame = false;

                ResetSmoothedFocusToRaw();
                _camSmoothVel = Vector3.zero;
            }
            return;
        }

        float dt = useUnscaledDeltaTimeForCamera ? Time.unscaledDeltaTime : Time.deltaTime;
        if (dt <= 0f) dt = 0.0001f;

        // raw focus
        Vector3 rawFocus = GetRawFocusPoint();

        // ===========================
        // ✅ TELEPORT DETECTION (NEW)
        // ===========================
        if (stabilizeOnTeleportJump)
        {
            if (_hasLastRawFocusForTeleport)
            {
                float jump = (rawFocus - _lastRawFocusForTeleport).magnitude;
                if (jump >= Mathf.Max(0.01f, teleportJumpDistance))
                {
                    // Target teleported / got corrected during load
                    BeginStabilizeWindow();
                    ResetSmoothedFocusToRaw();
                    _camSmoothVel = Vector3.zero;
                }
            }

            _lastRawFocusForTeleport = rawFocus;
            _hasLastRawFocusForTeleport = true;
        }

        // focus smoothing
        Vector3 focusPoint = rawFocus;
        if (smoothFocusPoint)
        {
            if (!_hasFocusInit)
            {
                _smoothedFocus = rawFocus;
                _focusVel = Vector3.zero;
                _hasFocusInit = true;
            }
            else
            {
                float smXZ = Mathf.Max(0.0001f, focusSmoothTimeXZ);
                float smY = Mathf.Max(0.0001f, focusSmoothTimeY);
                float maxSpd = (maxFocusSpeed > 0f) ? maxFocusSpeed : Mathf.Infinity;

                Vector3 current = _smoothedFocus;
                current.x = Mathf.SmoothDamp(current.x, rawFocus.x, ref _focusVel.x, smXZ, maxSpd, dt);
                current.z = Mathf.SmoothDamp(current.z, rawFocus.z, ref _focusVel.z, smXZ, maxSpd, dt);
                current.y = Mathf.SmoothDamp(current.y, rawFocus.y, ref _focusVel.y, smY, maxSpd, dt);

                _smoothedFocus = current;
            }

            focusPoint = _smoothedFocus;
        }

        Vector3 desired = focusPoint + offset;
        if (avoidClipping)
            desired = ApplyClipAvoidance(focusPoint, desired);

        // ===========================
        // ✅ STABILIZE MODE (NEW)
        // ===========================
        if (_stabilizing)
        {
            if (enforceSceneCameraSettings)
                ApplySceneCameraSettings();

            // ignore a few frames first
            if (_stabilizeIgnoreLeft > 0)
            {
                _stabilizeIgnoreLeft--;
                if (keepCameraStillWhileStabilizing) return;
            }
            else
            {
                // check stability using raw focus (not smoothed)
                // stable if raw focus barely changes frame-to-frame
                float d = (_hasLastRawFocusForTeleport ? (rawFocus - _lastRawFocusForTeleport).magnitude : 999f);
                // NOTE: _lastRawFocusForTeleport was updated above, so we need a separate delta sample:
                // We'll approximate stability by comparing smoothedFocus to rawFocus as a proxy (still works well).
                float proxyDelta = (focusPoint - rawFocus).magnitude;

                if (proxyDelta <= Mathf.Max(0.0001f, stableDeltaThreshold))
                    _stableFrameCount++;
                else
                    _stableFrameCount = 0;

                bool timedOut = (Time.unscaledTime >= _stabilizeTimeoutAtUnscaled);

                if (_stableFrameCount >= Mathf.Max(1, stableFramesRequired) || timedOut)
                {
                    _stabilizing = false;
                    _stableFrameCount = 0;

                    // after stable: do ONE clean soft snap (no jitter)
                    StartSoftSnap(desired, softSnapDuration);
                    return;
                }

                if (keepCameraStillWhileStabilizing) return;
            }
        }

        // spawn window snap restriction
        float distSqr = (transform.position - desired).sqrMagnitude;
        bool inSpawnWindow = (Time.unscaledTime < _spawnPhaseEndUnscaled);

        float snapMult = Mathf.Max(1f, initialSnapDistanceMultiplier);
        float allowedSnapDist = snapDistance * snapMult;
        float allowedSnapDistSqr = allowedSnapDist * allowedSnapDist;

        bool snapRequested =
            _forceSnapThisFrame ||
            !smoothFollow ||
            distSqr > snapDistance * snapDistance;

        if (inSpawnWindow && smoothFollow)
            snapRequested = _forceSnapThisFrame || (!smoothFollow) || (distSqr > allowedSnapDistSqr);

        if (snapRequested && inSpawnWindow && smoothFollow && convertSpawnSnapsToSoftSnap && softSnapDuration > 0.0001f)
        {
            StartSoftSnap(desired, softSnapDuration);
            return;
        }

        if (snapRequested)
        {
            transform.position = desired;
            _forceSnapThisFrame = false;
            _velXZ = Vector3.zero;
            _velY = 0f;

            ResetSmoothedFocusToRaw();
            _camSmoothVel = Vector3.zero;
            return;
        }

        if (!smoothFollow)
        {
            transform.position = desired;
            return;
        }

        // SmoothDamp camera (stable with dt spikes)
        if (useSmoothDampForCamera)
        {
            float stXZ = Mathf.Max(0.0001f, 1f / Mathf.Max(0.0001f, smoothTime));
            float stY = dampVertical ? Mathf.Max(0.0001f, 1f / Mathf.Max(0.0001f, verticalDampTime)) : stXZ;

            float dampXZ = stXZ;
            float dampY = stY;

            float maxSpd = (maxCameraSpeed > 0f) ? maxCameraSpeed : Mathf.Infinity;

            Vector3 cur = transform.position;
            cur.x = Mathf.SmoothDamp(cur.x, desired.x, ref _camSmoothVel.x, dampXZ, maxSpd, dt);
            cur.z = Mathf.SmoothDamp(cur.z, desired.z, ref _camSmoothVel.z, dampXZ, maxSpd, dt);
            cur.y = Mathf.SmoothDamp(cur.y, desired.y, ref _camSmoothVel.y, dampY, maxSpd, dt);

            transform.position = cur;
        }
        else
        {
            Vector3 current = transform.position;

            float tXZ = 1f - Mathf.Exp(-Mathf.Max(0f, smoothTime) * dt);
            float tY = dampVertical
                ? 1f - Mathf.Exp(-Mathf.Max(0f, verticalDampTime) * dt)
                : tXZ;

            current.x = Mathf.Lerp(current.x, desired.x, tXZ);
            current.z = Mathf.Lerp(current.z, desired.z, tXZ);
            current.y = Mathf.Lerp(current.y, desired.y, tY);

            transform.position = current;
        }
    }

    // RAW focus point (CharacterController friendly)
    private Vector3 GetRawFocusPoint()
    {
        if (!target) return transform.position;
        return target.position + focusOffset;
    }

    private void ResetSmoothedFocusToRaw()
    {
        if (!target) return;
        _smoothedFocus = GetRawFocusPoint();
        _focusVel = Vector3.zero;
        _hasFocusInit = true;
    }

    // ✅ NEW: begin a stabilization window (used on enable/scene change/teleport detection)
    private void BeginStabilizeWindow()
    {
        _stabilizing = true;
        _stabilizeIgnoreLeft = Mathf.Max(0, postTeleportIgnoreFrames);
        _stableFrameCount = 0;
        _stabilizeTimeoutAtUnscaled = Time.unscaledTime + Mathf.Max(0.05f, stabilizeTimeoutSeconds);
    }

    private Vector3 ApplyClipAvoidance(Vector3 from, Vector3 desired)
    {
        Vector3 toDesired = desired - from;
        float dist = toDesired.magnitude;
        if (dist <= 0.0001f) return desired;

        Vector3 dir = toDesired / dist;

        if (Physics.SphereCast(from, clipSphereRadius, dir, out RaycastHit hit, dist, clipMask, QueryTriggerInteraction.Ignore))
        {
            float safeDist = Mathf.Max(minDistanceFromTarget, hit.distance - clipPadding);
            Vector3 adjusted = from + dir * safeDist;

            if (Physics.CheckSphere(adjusted, clipSphereRadius, clipMask, QueryTriggerInteraction.Ignore))
            {
                adjusted = from + dir * Mathf.Max(minDistanceFromTarget, safeDist - (clipSphereRadius + clipPadding));
            }

            return adjusted;
        }

        return desired;
    }

    private void TryAutoFindTarget(bool force)
    {
        if (!autoFindTarget) return;

        if (!force && Time.unscaledTime < _nextRefindTimeUnscaled) return;
        _nextRefindTimeUnscaled = Time.unscaledTime + Mathf.Max(0.05f, refindInterval);

        GameObject playerObj = GameObject.FindGameObjectWithTag(playerTag);
        if (playerObj == null) return;

        bool mustRetarget =
            target == null ||
            !target.gameObject.activeInHierarchy ||
            target.gameObject != playerObj;

        if (!mustRetarget) return;

        if (autoComputeOffsetIfZero && offset == Vector3.zero && !_didComputeOffsetOnce)
        {
            ForceDefaultOffsetNow();
            _didComputeOffsetOnce = true;
            _offsetWasAutoComputed = true;
        }

        SetTarget(playerObj.transform, snap: true, recalcOffset: false);

        ResetSmoothedFocusToRaw();
        _camSmoothVel = Vector3.zero;

        // ✅ retargeting usually happens during load => stabilize again
        BeginStabilizeWindow();

        ForceSnap();
    }

    private void StartSettleSnapRoutine()
    {
        if (Time.timeScale == 0f)
        {
            ForceSnap();
            return;
        }

        if (!snapAfterSettle) { ForceSnap(); return; }

        if (_settleRoutine != null)
            StopCoroutine(_settleRoutine);

        _settleRoutine = StartCoroutine(SnapWhenSettled());
    }

    private IEnumerator SnapWhenSettled()
    {
        int frames = Mathf.Max(0, settleFixedFrames);
        for (int i = 0; i < frames; i++)
            yield return null;

        if (!target) yield break;

        if (enforceSceneCameraSettings)
            ApplySceneCameraSettings();

        // ✅ Instead of hard snap, stabilize + soft snap (prevents load jitter)
        BeginStabilizeWindow();
    }

    public void RecalculateOffset()
    {
        if (target != null)
        {
            offset = transform.position - GetRawFocusPoint();
            _didComputeOffsetOnce = true;
            _offsetWasAutoComputed = true;
        }
    }

    public void ForceSnap()
    {
        _forceSnapThisFrame = true;
    }

    public void SetTarget(Transform newTarget, bool snap = true, bool recalcOffset = false)
    {
        target = newTarget;

        if (recalcOffset)
            RecalculateOffset();

        if (snap)
            ForceSnap();
    }

    private void ForceDefaultOffsetNow()
    {
        offset = defaultIsometricOffset;
        _didComputeOffsetOnce = true;
        _offsetWasAutoComputed = true;
    }

    public void ForceAcquirePlayerAndSnap(bool recalcOffsetIfZero = true)
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag(playerTag);
        if (playerObj == null) return;

        if (autoComputeOffsetIfZero && offset == Vector3.zero && !_didComputeOffsetOnce)
        {
            ForceDefaultOffsetNow();
        }

        SetTarget(playerObj.transform, snap: true, recalcOffset: false);

        ResetSmoothedFocusToRaw();
        _camSmoothVel = Vector3.zero;

        // ✅ acquire during load => stabilize, then soft snap
        BeginStabilizeWindow();

        ForceSnap();
    }

    private void StartSoftSnap(Vector3 desired, float duration)
    {
        _softSnapping = true;
        _softFrom = transform.position;
        _softTo = desired;

        float d = Mathf.Max(0.01f, duration);
        _softSnapT0 = Time.unscaledTime;
        _softSnapT1 = _softSnapT0 + d;

        _forceSnapThisFrame = false;
        _velXZ = Vector3.zero;
        _velY = 0f;
    }

    private void StartEnforceRoutine()
    {
        if (_enforceRoutine != null) StopCoroutine(_enforceRoutine);
        _enforceRoutine = StartCoroutine(EnforceForAFewFrames());
    }

    private IEnumerator EnforceForAFewFrames()
    {
        int frames = Mathf.Max(0, enforceFramesAfterEnable);

        ApplySceneCameraSettings();
        yield return null;

        for (int i = 0; i < frames; i++)
        {
            ApplySceneCameraSettings();
            yield return null;
        }
    }

    public void ApplySceneCameraSettings()
    {
        if (!enforceSceneCameraSettings) return;

        if (enforceRotation)
            transform.rotation = _sceneRotation;

        if (enforceProjection && _cam != null)
        {
            _cam.orthographic = _sceneIsOrtho;

            if (_sceneIsOrtho)
                _cam.orthographicSize = _sceneOrthoSize;
            else
                _cam.fieldOfView = _sceneFov;
        }
    }

    private void OnValidate()
    {
        if (smoothTime < 0f) smoothTime = 0f;
        if (verticalDampTime < 0f) verticalDampTime = 0f;
        if (snapDistance < 0f) snapDistance = 0f;
        if (refindInterval < 0.05f) refindInterval = 0.05f;
        if (clipSphereRadius < 0.01f) clipSphereRadius = 0.01f;
        if (minDistanceFromTarget < 0f) minDistanceFromTarget = 0f;
        if (clipPadding < 0f) clipPadding = 0f;
        if (settleFixedFrames < 0) settleFixedFrames = 0;
        if (groundedCheckDistance < 0.1f) groundedCheckDistance = 0.1f;
        if (settledVelocity < 0f) settledVelocity = 0f;
        if (enforceFramesAfterEnable < 0) enforceFramesAfterEnable = 0;

        if (initialNoSnapSeconds < 0f) initialNoSnapSeconds = 0f;
        if (initialSnapDistanceMultiplier < 1f) initialSnapDistanceMultiplier = 1f;
        if (softSnapDuration < 0f) softSnapDuration = 0f;

        if (focusSmoothTimeXZ < 0.0001f) focusSmoothTimeXZ = 0.0001f;
        if (focusSmoothTimeY < 0.0001f) focusSmoothTimeY = 0.0001f;
        if (maxFocusSpeed < 0f) maxFocusSpeed = 0f;

        if (maxCameraSpeed < 0f) maxCameraSpeed = 0f;

        if (teleportJumpDistance < 0.01f) teleportJumpDistance = 0.01f;
        if (postTeleportIgnoreFrames < 0) postTeleportIgnoreFrames = 0;
        if (stableFramesRequired < 1) stableFramesRequired = 1;
        if (stableDeltaThreshold < 0.0001f) stableDeltaThreshold = 0.0001f;
        if (stabilizeTimeoutSeconds < 0.05f) stabilizeTimeoutSeconds = 0.05f;
    }
}