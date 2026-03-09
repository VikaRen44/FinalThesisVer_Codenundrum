using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[DefaultExecutionOrder(-500)]
[RequireComponent(typeof(Collider))]
public class RoomCullingZone : MonoBehaviour
{
    [Tooltip("Objects in this room that should be culled (disabled until player enters).")]
    public GameObject[] roomObjects;

    [Header("Stability")]
    [Tooltip("Prevents rapid on/off spam when the player quickly crosses room triggers.")]
    public float minSwitchInterval = 0.12f;

    [Tooltip("Wait 1 frame before applying activation during normal trigger-based activation.")]
    public bool delayActivationByOneFrame = true;

    [Header("Start / Teleport Safety")]
    [Tooltip("Continuously re-check player position for a few startup frames.")]
    public bool recheckPlayerOnStart = true;

    [Range(1, 20)]
    [Tooltip("How many rendered frames after Start to re-check zone ownership.")]
    public int startRecheckFrames = 6;

    [Range(1, 20)]
    [Tooltip("How many fixed steps after Start to re-check zone ownership.")]
    public int startRecheckFixedSteps = 4;

    [Tooltip("If no active zone exists, keep trying to resolve one every FixedUpdate.")]
    public bool keepResolvingUntilActive = true;

    [Tooltip("Extra tolerance when checking if a point is inside this zone.")]
    public float insideTolerance = 0.08f;

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

    /// <summary>
    /// Activates the first zone that contains the given point.
    /// Uses robust containment instead of plain bounds.Contains only.
    /// </summary>
    public static bool ActivateZoneContainingPoint(Vector3 worldPoint)
    {
        if (allZones == null || allZones.Count == 0)
            return false;

        Physics.SyncTransforms();

        RoomCullingZone best = FindZoneContainingPoint(worldPoint);
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

    private static RoomCullingZone FindZoneContainingPoint(Vector3 point)
    {
        RoomCullingZone fallbackByBounds = null;

        for (int i = 0; i < allZones.Count; i++)
        {
            var z = allZones[i];
            if (!z) continue;
            if (!z.isActiveAndEnabled) continue;
            if (z._col == null) continue;

            // Exact-ish test first
            if (z.IsPointInsideZone(point))
                return z;

            // Bounds fallback
            if (fallbackByBounds == null && z._col.bounds.Contains(point))
                fallbackByBounds = z;
        }

        return fallbackByBounds;
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

        // Start disabled. A robust startup resolver will turn on the correct room.
        SetRoomObjectsActive(false);
    }

    private void OnEnable()
    {
        if (!allZones.Contains(this))
            allZones.Add(this);
    }

    private void Start()
    {
        // Try immediately in case player already exists and has already been teleported.
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

        // On slower devices, physics may step before normal trigger events settle.
        // So if there is no active zone yet, aggressively resolve by player position.
        if (activeZone == null)
        {
            ResolveNowFromPlayerPosition();
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

        // Teleport-safe:
        // if the player is already inside but this zone didn't get activated yet,
        // request activation again.
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

    private bool ResolveNowFromPlayerPosition()
    {
        var player = GameObject.FindGameObjectWithTag(playerTag);
        if (player == null)
            return false;

        Physics.SyncTransforms();

        Vector3 playerPoint = player.transform.position;

        // Best case: if THIS zone contains the player, activate immediately.
        if (_col != null && IsPointInsideZone(playerPoint))
        {
            if (verboseLogs)
                Debug.Log($"[RoomCullingZone] ResolveNow: player is inside '{name}', forcing activation.");

            ActivateThisZoneNow_Force();
            return true;
        }

        // Otherwise ask the whole zone set to resolve ownership.
        bool activated = ActivateZoneContainingPoint(playerPoint);

        if (verboseLogs && activated)
            Debug.Log($"[RoomCullingZone] ResolveNow: another zone was activated for player position.");

        return activated;
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

        // If this zone is no longer the active one, nothing to do.
        if (activeZone != this)
            yield break;

        Vector3 playerPoint = player.position;

        // If the player is actually still inside, keep room active.
        if (IsPointInsideZone(playerPoint))
            yield break;

        // Try to hand off to the zone that currently contains the player.
        RoomCullingZone newZone = FindZoneContainingPoint(playerPoint);
        if (newZone != null && newZone != this)
        {
            if (verboseLogs)
                Debug.Log($"[RoomCullingZone] Exit handoff: '{name}' -> '{newZone.name}'.");

            newZone.ActivateThisZoneNow_Force();
            yield break;
        }

        // Safety: do not drop to no room during handoff unless explicitly allowed.
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

    // Force activation immediately, ignoring minSwitchInterval + delayed activation.
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

        // Fast broad phase
        Bounds b = _col.bounds;
        b.Expand(insideTolerance * 2f);
        if (!b.Contains(point))
            return false;

        // More reliable than plain bounds.Contains for many collider shapes:
        // if ClosestPoint returns the same point, the point is inside or on the surface.
        Vector3 closest = _col.ClosestPoint(point);
        float sqr = (closest - point).sqrMagnitude;
        return sqr <= insideTolerance * insideTolerance;
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
}