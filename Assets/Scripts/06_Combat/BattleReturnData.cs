using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class BattleReturnData
{
    // ---------------------------------------------------------
    // Simple "battle return" snapshot (player + host)
    // ---------------------------------------------------------
    public static Vector3 lastPlayerPos;
    public static Quaternion lastPlayerRot;
    public static bool hasReturnPosition;

    public static Vector3 lastHostPos;
    public static Quaternion lastHostRot;
    public static bool hasHostReturnTransform;

    public static int hostInstanceId;

    // ---------------------------------------------------------
    // Other battle-return fields
    // ---------------------------------------------------------
    public static string worldSceneName;
    public static bool shouldReturnToWorld;

    public static int damageGoal;

    public static bool comingFromBattle;

    public static bool pendingObjectiveApply;
    public static string returnTag;

    public static SimpleDialogueSequenceSO postBattleCutscene;
    public static string currentBattleId;
    public static string objectiveToForceAfterReturn;

    // runner-based return
    private static MonoBehaviour _runner;
    private static bool _isReturning;

    private const int kExtraFramesAfterLoad = 3;
    private const float kFindTimeoutSeconds = 3f;

    public static void ClearReturnOnly()
    {
        hasReturnPosition = false;
        hasHostReturnTransform = false;
        hostInstanceId = 0;

        comingFromBattle = false;
        postBattleCutscene = null;
        objectiveToForceAfterReturn = null;

        worldSceneName = null;
        shouldReturnToWorld = false;
        damageGoal = 0;
        currentBattleId = null;

        // NOTE: do NOT clear pendingObjectiveApply here
        // NOTE: do NOT clear returnTag here
    }

    public static void ClearReturnObjectiveRuleOnly()
    {
        pendingObjectiveApply = false;
        returnTag = null;
        objectiveToForceAfterReturn = null;
        postBattleCutscene = null;
        currentBattleId = null;
    }

    // ---------------------------------------------------------
    // General Scene Return Stack (works for any scenes)
    // ---------------------------------------------------------
    [Serializable]
    private struct TransformSnap
    {
        public string id;
        public Vector3 pos;
        public Quaternion rot;
    }

    [Serializable]
    private class ReturnFrame
    {
        public string fromScene;
        public string toScene;

        public Vector3 playerPos;
        public Quaternion playerRot;

        public List<TransformSnap> objectSnaps = new List<TransformSnap>();
    }

    private static readonly Stack<ReturnFrame> _stack = new Stack<ReturnFrame>();

    public static void PushReturnFrame(string targetSceneName, Transform player, bool captureAllSaveIdTransforms = true)
    {
        var from = SceneManager.GetActiveScene().name;

        if (player == null)
        {
            var pgo = GameObject.FindGameObjectWithTag("Player");
            if (pgo != null) player = pgo.transform;
        }

        var frame = new ReturnFrame
        {
            fromScene = from,
            toScene = targetSceneName,
            playerPos = player ? player.position : Vector3.zero,
            playerRot = player ? player.rotation : Quaternion.identity
        };

        if (captureAllSaveIdTransforms)
        {
            var all = UnityEngine.Object.FindObjectsByType<SaveID>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var sid in all)
            {
                if (sid == null || string.IsNullOrEmpty(sid.ID)) continue;

                var t = sid.transform;

                frame.objectSnaps.Add(new TransformSnap
                {
                    id = sid.ID,
                    pos = t.position,
                    rot = t.rotation
                });
            }
        }

        _stack.Push(frame);

        lastPlayerPos = frame.playerPos;
        lastPlayerRot = frame.playerRot;
        hasReturnPosition = (player != null);

        Debug.Log($"[BattleReturnData] PushReturnFrame: from='{from}' -> to='{targetSceneName}', objs={frame.objectSnaps.Count}");
    }

    public static bool HasPendingReturn() => _stack.Count > 0;

    public static bool TryConsumeReturnForCurrentScene(
        out Vector3 playerPos,
        out Quaternion playerRot,
        out List<(string id, Vector3 pos, Quaternion rot)> objs)
    {
        playerPos = default;
        playerRot = default;
        objs = null;

        if (_stack.Count == 0) return false;

        var cur = SceneManager.GetActiveScene().name;
        var top = _stack.Peek();

        // expects the frame to be "fromScene == current world scene"
        if (top.fromScene != cur) return false;

        _stack.Pop();

        playerPos = top.playerPos;
        playerRot = top.playerRot;

        objs = new List<(string id, Vector3 pos, Quaternion rot)>(top.objectSnaps.Count);
        foreach (var s in top.objectSnaps)
            objs.Add((s.id, s.pos, s.rot));

        hasReturnPosition = true;
        lastPlayerPos = playerPos;
        lastPlayerRot = playerRot;

        Debug.Log($"[BattleReturnData] Consumed return frame for scene '{cur}', restore objs={objs.Count}");
        return true;
    }

    // =========================================================
    // ReturnToWorld (loads + restores automatically)
    // =========================================================
    public static void ReturnToWorld(string worldScene)
    {
        if (string.IsNullOrWhiteSpace(worldScene))
        {
            Debug.LogWarning("[BattleReturnData] ReturnToWorld called with empty worldScene.");
            return;
        }

        if (_isReturning) return;

        EnsureRunner();
        _runner.StartCoroutine(ReturnRoutine(worldScene));
    }

    private static IEnumerator ReturnRoutine(string worldScene)
    {
        _isReturning = true;

        AsyncOperation op = null;
        try { op = SceneManager.LoadSceneAsync(worldScene); }
        catch (Exception e)
        {
            Debug.LogWarning("[BattleReturnData] LoadSceneAsync failed: " + e.Message);
            _isReturning = false;
            yield break;
        }

        while (op != null && !op.isDone)
            yield return null;

        for (int i = 0; i < kExtraFramesAfterLoad; i++)
            yield return null;

        yield return new WaitForFixedUpdate();

        if (TryConsumeReturnForCurrentScene(out Vector3 playerPos, out Quaternion playerRot,
                                           out List<(string id, Vector3 pos, Quaternion rot)> objs))
        {
            if (objs != null && objs.Count > 0)
                RestoreSaveIdObjects(objs);

            Transform player = null;
            float t = 0f;
            while (t < kFindTimeoutSeconds)
            {
                player = SafeFindByTag("Player");
                if (player != null) break;
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            if (player != null)
            {
                var pm = player.GetComponent<PlayerMovement>();
                if (pm != null) pm.FreezeForSeconds(0.35f);

                SafeSetPose(player, playerPos, playerRot);

                if (pm != null) pm.ForceSnapToGroundNow();
                else SnapTransformToGround_Fallback(player);

                // ✅ IMPORTANT: snap camera AFTER player is restored
                ForceCameraSnapTo(player);
            }
        }
        else
        {
            Debug.LogWarning("[BattleReturnData] Returned to world but no return frame matched this scene (did you PushReturnFrame before combat?).");
        }

        _isReturning = false;
    }

    private static void ForceCameraSnapTo(Transform player)
    {
        try
        {
            var cam = Camera.main;
            if (cam == null) return;

            var follow = cam.GetComponent<CameraFollow>();
            if (follow == null) return;

            // Keep your isometric offset stable; don't recalc offset here.
            follow.SetTarget(player, snap: true, recalcOffset: false);
            follow.ForceSnap();
        }
        catch { }
    }

    private static void RestoreSaveIdObjects(List<(string id, Vector3 pos, Quaternion rot)> objs)
    {
        var all = UnityEngine.Object.FindObjectsByType<SaveID>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (all == null || all.Length == 0) return;

        var map = new Dictionary<string, SaveID>(all.Length);
        foreach (var sid in all)
        {
            if (sid == null || string.IsNullOrEmpty(sid.ID)) continue;
            if (!map.ContainsKey(sid.ID)) map.Add(sid.ID, sid);
        }

        foreach (var s in objs)
        {
            if (!map.TryGetValue(s.id, out var sid) || sid == null) continue;
            SafeSetPose(sid.transform, s.pos, s.rot);
        }
    }

    private static void EnsureRunner()
    {
        if (_runner != null) return;

        var go = new GameObject("[BattleReturnDataRunner]");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _runner = go.AddComponent<Runner>();
    }

    private class Runner : MonoBehaviour { }

    private static Transform SafeFindByTag(string tag)
    {
        try
        {
            var go = GameObject.FindGameObjectWithTag(tag);
            return go != null ? go.transform : null;
        }
        catch (UnityException)
        {
            Debug.LogWarning($"[BattleReturnData] Tag '{tag}' is not defined.");
            return null;
        }
    }

    private static void SafeSetPose(Transform t, Vector3 position, Quaternion rotation)
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

    private static void SnapTransformToGround_Fallback(Transform t)
    {
        if (!t) return;

        var cc = t.GetComponent<CharacterController>();
        LayerMask mask = ~0;

        float extra = 2.5f;
        float clearance = 0.05f;

        float castUp = (cc != null) ? Mathf.Max(0.5f, (cc.height * 0.5f) + extra) : 3f;
        Vector3 origin = t.position + Vector3.up * castUp;
        float maxDist = castUp + 10f;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxDist, mask, QueryTriggerInteraction.Ignore))
        {
            if (cc != null)
            {
                float targetY = hit.point.y + clearance - cc.center.y + (cc.height * 0.5f);
                bool was = cc.enabled;
                cc.enabled = false;
                var p = t.position; p.y = targetY; t.position = p;
                cc.enabled = was;
            }
            else
            {
                var p = t.position; p.y = hit.point.y + clearance; t.position = p;
            }
        }
    }
}
