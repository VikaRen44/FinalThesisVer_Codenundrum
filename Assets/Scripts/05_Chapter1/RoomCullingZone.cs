using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[DefaultExecutionOrder(-1000)]
[RequireComponent(typeof(Collider))]
public class RoomCullingZone : MonoBehaviour
{
    [Tooltip("Objects in this room that should be culled (disabled until player enters or is close enough).")]
    public GameObject[] roomObjects;

    [Header("Stability")]
    [Tooltip("Prevents rapid on/off spam when the player quickly crosses room triggers.")]
    public float minSwitchInterval = 0.12f;

    [Tooltip("Wait 1 frame before applying activation during normal trigger-based activation.")]
    public bool delayActivationByOneFrame = false;

    [Header("Startup / Build Safety")]
    [Tooltip("Continuously re-check player position for a few startup frames.")]
    public bool recheckPlayerOnStart = true;

    [Range(1, 30)]
    [Tooltip("How many rendered frames after Start to re-check zone ownership.")]
    public int startRecheckFrames = 10;

    [Range(1, 20)]
    [Tooltip("How many fixed steps after Start to re-check zone ownership.")]
    public int startRecheckFixedSteps = 8;

    [Tooltip("If no active zone exists, keep trying to resolve one every FixedUpdate.")]
    public bool keepResolvingUntilActive = true;

    [Tooltip("Extra tolerance when checking if a point is inside this zone.")]
    public float insideTolerance = 0.08f;

    [Header("Preload Safety")]
    [Tooltip("Activates this room slightly BEFORE the player fully enters it. Helps prevent falling before floor spawns.")]
    public bool usePreloadRange = true;

    [Tooltip("Extra world-space distance around the zone used for early activation.")]
    public float preloadDistance = 2.5f;

    [Tooltip("If player is near this zone during startup, this room can be force-activated immediately.")]
    public bool allowStartupPreloadActivation = true;

    [Header("Exit Safety")]
    [Tooltip("Delay before deactivating after exit. Helps with teleports / trigger handoff.")]
    public float exitDeactivationDelay = 0.08f;

    [Tooltip("If true, the current active room will stay alive unless another valid zone takes over.")]
    public bool neverDropToNoRoomDuringHandoff = true;

    [Header("Player Lookup")]
    [Tooltip("Tag used to find the player.")]
    public string playerTag = "Player";

    [Header("Debug")]
    public bool verboseLogs = false;

    private static readonly List<RoomCullingZone> allZones = new();
    private static RoomCullingZone activeZone;
    private static float lastSwitchTime;

    private Collider _col;
    private Coroutine _pendingActivate;
    private Coroutine _pendingExitCheck;

    // =========================================================
    // PUBLIC STATIC HELPERS
    // =========================================================

    public static RoomCullingZone ActiveZone => activeZone;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        allZones.Clear();
        activeZone = null;
        lastSwitchTime = -999f;
    }

    /// <summary>
    /// Activates the best zone for the given point.
    /// Priority:
    /// 1. Exact inside zone
    /// 2. Bounds fallback
    /// 3. Preload range fallback
    /// </summary>
    public static bool ActivateZoneContainingPoint(Vector3 worldPoint)
    {
        if (allZones == null || allZones.Count == 0)
            return false;

        Physics.SyncTransforms();

        RoomCullingZone best = FindBestZoneForPoint(worldPoint);
        if (best == null)
            return false;

        best.ActivateThisZoneNow_Force();
        return true;
    }

    public static bool ActivateZoneContainingPlayer(Transform player)
    {
        if (player == null) return false;
        return ActivateZoneContainingPoint(player.position);
    }

    private static RoomCullingZone FindBestZoneForPoint(Vector3 point)
    {
        RoomCullingZone boundsFallback = null;
        RoomCullingZone preloadFallback = null;

        for (int i = 0; i < allZones.Count; i++)
        {
            var z = allZones[i];
            if (!z) continue;
            if (!z.isActiveAndEnabled) continue;
            if (z._col == null) continue;

            // Best case: exact-ish inside test
            if (z.IsPointInsideZone(point))
                return z;

            // Next fallback: raw bounds check
            if (boundsFallback == null && z._col.bounds.Contains(point))
                boundsFallback = z;

            // Final fallback: preload range
            if (preloadFallback == null && z.IsPointWithinPreloadRange(point))
                preloadFallback = z;
        }

        if (boundsFallback != null) return boundsFallback;
        return preloadFallback;
    }

    // =========================================================
    // UNITY LIFECYCLE
    // =========================================================

    private void Awake()
    {
        _col = GetComponent<Collider>();
        if (_col) _col.isTrigger = true;

        if (!allZones.Contains(this))
            allZones.Add(this);

        // Start disabled by default
        SetRoomObjectsActive(false);

        // EARLY BOOTSTRAP:
        // Try immediately in Awake so the room can exist before physics gets a chance
        // to make the player fall in builds.
        TryImmediateStartupActivation();
    }

    private void OnEnable()
    {
        if (!allZones.Contains(this))
            allZones.Add(this);

        // Also retry here in case enable timing differs in build.
        TryImmediateStartupActivation();
    }

    private void Start()
    {
        ResolveNowFromPlayerPosition();

        if (recheckPlayerOnStart)
        {
            StartCoroutine(RecheckPlayerAcrossStartupFrames());
            StartCoroutine(RecheckPlayerAcrossFixedSteps());
        }
    }

    private void FixedUpdate()
    {
        if (!keepResolvingUntilActive)
            return;

        var player = FindPlayerTransform();
        if (player == null)
            return;

        // If there is no active zone yet, aggressively resolve.
        if (activeZone == null)
        {
            ResolveNowFromPlayerPosition();
            return;
        }

        // Extra safety:
        // If this zone is not active yet but the player is already inside/near it,
        // activate now before physics keeps advancing.
        if (activeZone != this)
        {
            Vector3 playerPoint = player.position;

            if (IsPointInsideZone(playerPoint) || IsPointWithinPreloadRange(playerPoint))
            {
                if (verboseLogs)
                    Debug.Log($"[RoomCullingZone] FixedUpdate preload activation for '{name}'.");

                ActivateThisZoneNow_Force();
            }
        }
    }

    private void OnDestroy()
    {
        allZones.Remove(this);

        if (activeZone == this)
            activeZone = null;
    }

    // =========================================================
    // TRIGGERS
    // =========================================================

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        RequestActivate();
    }

    private void OnTriggerStay(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        if (activeZone != this)
            RequestActivate();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        if (_pendingExitCheck != null)
            StopCoroutine(_pendingExitCheck);

        _pendingExitCheck = StartCoroutine(DeferExitResolution(other.transform));
    }

    // =========================================================
    // STARTUP / RESOLUTION
    // =========================================================

    private IEnumerator RecheckPlayerAcrossStartupFrames()
    {
        for (int i = 0; i < startRecheckFrames; i++)
        {
            yield return null;
            ResolveNowFromPlayerPosition();
        }
    }

    private IEnumerator RecheckPlayerAcrossFixedSteps()
    {
        for (int i = 0; i < startRecheckFixedSteps; i++)
        {
            yield return new WaitForFixedUpdate();
            ResolveNowFromPlayerPosition();
        }
    }

    private void TryImmediateStartupActivation()
    {
        var player = FindPlayerTransform();
        if (player == null || _col == null)
            return;

        Physics.SyncTransforms();

        Vector3 p = player.position;

        // Exact inside = always activate immediately
        if (IsPointInsideZone(p))
        {
            if (verboseLogs)
                Debug.Log($"[RoomCullingZone] Awake/OnEnable immediate activation: player inside '{name}'.");

            ActivateThisZoneNow_Force();
            return;
        }

        // Near enough = preload this room early
        if (allowStartupPreloadActivation && IsPointWithinPreloadRange(p))
        {
            if (verboseLogs)
                Debug.Log($"[RoomCullingZone] Awake/OnEnable preload activation: player near '{name}'.");

            ActivateThisZoneNow_Force();
        }
    }

    private bool ResolveNowFromPlayerPosition()
    {
        var player = FindPlayerTransform();
        if (player == null)
            return false;

        Physics.SyncTransforms();

        Vector3 playerPoint = player.position;

        // Best case: player is already inside THIS zone
        if (_col != null && IsPointInsideZone(playerPoint))
        {
            if (verboseLogs)
                Debug.Log($"[RoomCullingZone] ResolveNow: player is inside '{name}', forcing activation.");

            ActivateThisZoneNow_Force();
            return true;
        }

        // Preload case: player is very near THIS zone
        if (_col != null && usePreloadRange && IsPointWithinPreloadRange(playerPoint))
        {
            if (verboseLogs)
                Debug.Log($"[RoomCullingZone] ResolveNow: player is near '{name}', preload-forcing activation.");

            ActivateThisZoneNow_Force();
            return true;
        }

        // Otherwise ask all zones to determine best ownership
        bool activated = ActivateZoneContainingPoint(playerPoint);

        if (verboseLogs && activated)
            Debug.Log($"[RoomCullingZone] ResolveNow: a zone was activated for player position.");

        return activated;
    }

    private Transform FindPlayerTransform()
    {
        GameObject player = GameObject.FindGameObjectWithTag(playerTag);
        return player != null ? player.transform : null;
    }

    // =========================================================
    // EXIT HANDLING
    // =========================================================

    private IEnumerator DeferExitResolution(Transform player)
    {
        if (exitDeactivationDelay > 0f)
            yield return new WaitForSecondsRealtime(exitDeactivationDelay);
        else
            yield return null;

        Physics.SyncTransforms();

        if (player == null)
            yield break;

        if (activeZone != this)
            yield break;

        Vector3 playerPoint = player.position;

        // Still inside exact zone
        if (IsPointInsideZone(playerPoint))
            yield break;

        // Still near zone, keep it alive to avoid sudden floor loss
        if (usePreloadRange && IsPointWithinPreloadRange(playerPoint))
            yield break;

        RoomCullingZone newZone = FindBestZoneForPoint(playerPoint);
        if (newZone != null && newZone != this)
        {
            if (verboseLogs)
                Debug.Log($"[RoomCullingZone] Exit handoff: '{name}' -> '{newZone.name}'.");

            newZone.ActivateThisZoneNow_Force();
            yield break;
        }

        if (neverDropToNoRoomDuringHandoff)
        {
            if (verboseLogs)
                Debug.Log($"[RoomCullingZone] Exit ignored for '{name}' because no replacement zone was found.");
            yield break;
        }

        if (verboseLogs)
            Debug.Log($"[RoomCullingZone] Player left active zone '{name}'. Deactivating.");

        SetRoomObjectsActive(false);
        activeZone = null;
    }

    // =========================================================
    // ACTIVATION
    // =========================================================

    private void RequestActivate()
    {
        if (activeZone == this)
            return;

        if (_pendingExitCheck != null)
        {
            StopCoroutine(_pendingExitCheck);
            _pendingExitCheck = null;
        }

        float elapsed = Time.unscaledTime - lastSwitchTime;
        if (elapsed < minSwitchInterval)
        {
            if (_pendingActivate != null)
                StopCoroutine(_pendingActivate);

            _pendingActivate = StartCoroutine(ActivateAfterDelay(minSwitchInterval - elapsed));
            return;
        }

        if (_pendingActivate != null)
            StopCoroutine(_pendingActivate);

        if (delayActivationByOneFrame)
            _pendingActivate = StartCoroutine(ActivateNextFrame());
        else
            ActivateThisZoneNow();
    }

    private IEnumerator ActivateNextFrame()
    {
        yield return null;
        ActivateThisZoneNow();
    }

    private IEnumerator ActivateAfterDelay(float delay)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, delay));

        if (delayActivationByOneFrame)
            yield return null;

        ActivateThisZoneNow();
    }

    private void ActivateThisZoneNow()
    {
        lastSwitchTime = Time.unscaledTime;
        activeZone = this;

        for (int i = 0; i < allZones.Count; i++)
        {
            var zone = allZones[i];
            if (!zone) continue;

            if (zone != this)
                zone.SetRoomObjectsActive(false);
        }

        SetRoomObjectsActive(true);

        if (verboseLogs)
            Debug.Log($"[RoomCullingZone] Activated room '{name}'.");
    }

    private void ActivateThisZoneNow_Force()
    {
        if (_pendingActivate != null)
        {
            StopCoroutine(_pendingActivate);
            _pendingActivate = null;
        }

        if (_pendingExitCheck != null)
        {
            StopCoroutine(_pendingExitCheck);
            _pendingExitCheck = null;
        }

        lastSwitchTime = Time.unscaledTime;
        activeZone = this;

        for (int i = 0; i < allZones.Count; i++)
        {
            var zone = allZones[i];
            if (!zone) continue;

            if (zone != this)
                zone.SetRoomObjectsActive(false);
        }

        SetRoomObjectsActive(true);

        if (verboseLogs)
            Debug.Log($"[RoomCullingZone] (FORCE) Activated room '{name}'.");
    }

    // =========================================================
    // INTERNAL HELPERS
    // =========================================================

    private bool IsPointInsideZone(Vector3 point)
    {
        if (_col == null)
            return false;

        Bounds b = _col.bounds;
        b.Expand(insideTolerance * 2f);
        if (!b.Contains(point))
            return false;

        Vector3 closest = _col.ClosestPoint(point);
        float sqr = (closest - point).sqrMagnitude;
        return sqr <= insideTolerance * insideTolerance;
    }

    private bool IsPointWithinPreloadRange(Vector3 point)
    {
        if (_col == null || !usePreloadRange)
            return false;

        Bounds b = _col.bounds;
        b.Expand(preloadDistance * 2f);
        return b.Contains(point);
    }

    private void SetRoomObjectsActive(bool active)
    {
        if (roomObjects == null) return;

        for (int i = 0; i < roomObjects.Length; i++)
        {
            var obj = roomObjects[i];
            if (obj) obj.SetActive(active);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!usePreloadRange) return;

        Collider c = GetComponent<Collider>();
        if (c == null) return;

        Gizmos.color = new Color(0f, 1f, 1f, 0.2f);
        Bounds b = c.bounds;
        b.Expand(preloadDistance * 2f);
        Gizmos.DrawCube(b.center, b.size);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(b.center, b.size);
    }
#endif
}