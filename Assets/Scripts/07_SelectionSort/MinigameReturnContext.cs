using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class MinigameReturnContext
{
    public static bool hasData { get; private set; }
    public static string returnSceneName { get; private set; }
    public static string runId { get; private set; }
    public static string capturedSceneName { get; private set; }

    private static bool _restoreOnlyIfReturningToCapturedScene = true;

    private static bool _restorePlayerHost;
    private static bool _restoreRotation;

    private static string _playerTag = "Player";
    private static string _hostTag = "Host";

    private static Vector3 _playerPos;
    private static Quaternion _playerRot;

    private static Vector3 _hostPos;
    private static Quaternion _hostRot;

    private static string _fadeCanvasObjectName = "TeleportTransition";
    private static float _returnFadeOut = 0.25f;
    private static float _returnFadeIn = 0.15f;

    private static MonoBehaviour _runner;
    private static bool _isReturning;

    private const int kExtraFramesAfterLoad = 3;
    private const float kFindTimeoutSeconds = 3f;

    public static void BeginRun()
    {
        runId = Guid.NewGuid().ToString("N");
    }

    public static void ClearRun()
    {
        runId = null;
    }

    public static void SetRestoreOnlyIfReturningToCapturedScene(bool enabled)
    {
        _restoreOnlyIfReturningToCapturedScene = enabled;
    }

    public static void Capture(
        string returnSceneNameArg,
        Transform player,
        Transform host,
        bool restorePlayerHost,
        bool restoreRotation,
        string playerTag,
        string hostTag
    )
    {
        if (string.IsNullOrWhiteSpace(returnSceneNameArg))
        {
            Debug.LogWarning("[MinigameReturnContext] Capture called with empty returnSceneName.");
            hasData = false;
            returnSceneName = null;
            capturedSceneName = null;
            runId = null;
            return;
        }

        BeginRun();

        capturedSceneName = SceneManager.GetActiveScene().name;

        returnSceneName = returnSceneNameArg;
        hasData = true;

        _restorePlayerHost = restorePlayerHost;
        _restoreRotation = restoreRotation;

        _playerTag = string.IsNullOrWhiteSpace(playerTag) ? "Player" : playerTag;
        _hostTag = string.IsNullOrWhiteSpace(hostTag) ? "Host" : hostTag;

        if (player != null)
        {
            _playerPos = player.position;
            _playerRot = player.rotation;
        }

        if (host != null)
        {
            _hostPos = host.position;
            _hostRot = host.rotation;
        }

        EnsureRunner();

        Debug.Log(
            $"[MinigameReturnContext] CAPTURED. capturedScene='{capturedSceneName}', returnScene='{returnSceneName}', runId='{runId}', " +
            $"playerTag='{_playerTag}', hostTag='{_hostTag}', restore={_restorePlayerHost}, rot={_restoreRotation}, " +
            $"restoreOnlyIfSameScene={_restoreOnlyIfReturningToCapturedScene}"
        );
    }

    public static void Clear()
    {
        hasData = false;
        returnSceneName = null;
        capturedSceneName = null;

        _restorePlayerHost = false;
        _restoreRotation = false;
        _isReturning = false;

        ClearRun();
    }

    public static void ReturnToWorld()
    {
        if (!hasData || string.IsNullOrWhiteSpace(returnSceneName))
        {
            Debug.LogWarning("[MinigameReturnContext] ReturnToWorld called but no context is set.");
            return;
        }

        if (_isReturning) return;
        EnsureRunner();
        _runner.StartCoroutine(ReturnRoutine());
    }

    private static IEnumerator ReturnRoutine()
    {
        _isReturning = true;

        var cg = FindFadeCanvasGroup();
        if (cg != null)
            yield return FadeCanvasGroup(cg, 1f, Mathf.Max(0f, _returnFadeOut));

        string targetScene = returnSceneName;

        AsyncOperation op = null;
        try { op = SceneManager.LoadSceneAsync(targetScene); }
        catch (Exception e)
        {
            Debug.LogWarning("[MinigameReturnContext] LoadSceneAsync failed: " + e.Message);
            _isReturning = false;
            yield break;
        }

        while (op != null && !op.isDone)
            yield return null;

        for (int i = 0; i < kExtraFramesAfterLoad; i++)
            yield return null;

        yield return new WaitForFixedUpdate();

        if (ObjectiveManager.Instance != null)
        {
            ObjectiveManager.Instance.ForceRefreshForActiveScene(playSfx: false);
        }

        string loadedSceneName = SceneManager.GetActiveScene().name;

        bool sameScene = !string.IsNullOrWhiteSpace(capturedSceneName) &&
                         string.Equals(loadedSceneName, capturedSceneName, StringComparison.Ordinal);

        bool allowRestore =
            _restorePlayerHost &&
            (!_restoreOnlyIfReturningToCapturedScene || sameScene);

        if (_restorePlayerHost && !allowRestore)
        {
            Debug.LogWarning(
                $"[MinigameReturnContext] Skipping pose restore because scene changed. " +
                $"capturedScene='{capturedSceneName}', loadedScene='{loadedSceneName}'."
            );
        }

        if (allowRestore)
        {
            Transform player = null;
            Transform host = null;

            float t = 0f;
            while (t < kFindTimeoutSeconds)
            {
                player = SafeFindByTag(_playerTag);
                host = SafeFindByTag(_hostTag);
                if (player != null) break;

                t += Time.unscaledDeltaTime;
                yield return null;
            }

            if (player != null)
            {
                var pm = player.GetComponent<PlayerMovement>();
                if (pm != null) pm.FreezeForSeconds(0.35f);

                SafeSetPose(player, _playerPos, _restoreRotation ? _playerRot : player.rotation);

                if (pm != null) pm.ForceSnapToGroundNow();
                else SnapTransformToGround_Fallback(player);

                // ✅ IMPORTANT: snap camera AFTER player is restored
                ForceCameraSnapTo(player);
            }

            if (host != null)
                SafeSetPose(host, _hostPos, _restoreRotation ? _hostRot : host.rotation);
        }

        yield return null;

        cg = FindFadeCanvasGroup();
        if (cg != null)
            yield return FadeCanvasGroup(cg, 0f, Mathf.Max(0f, _returnFadeIn));

        if (CutsceneRunner.Instance != null)
            CutsceneRunner.Instance.ResumeAfterMinigameReturn();

        Clear();
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

    private static void EnsureRunner()
    {
        if (_runner != null) return;

        var go = new GameObject("[MinigameReturnContextRunner]");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _runner = go.AddComponent<Runner>();
    }

    private class Runner : MonoBehaviour { }

    private static Transform SafeFindByTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        try
        {
            var go = GameObject.FindGameObjectWithTag(tag);
            return go != null ? go.transform : null;
        }
        catch (UnityException)
        {
            Debug.LogWarning($"[MinigameReturnContext] Tag '{tag}' is not defined.");
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
            float targetY;

            if (cc != null)
            {
                targetY = hit.point.y + clearance - cc.center.y + (cc.height * 0.5f);
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

    private static CanvasGroup FindFadeCanvasGroup()
    {
        var groups = UnityEngine.Object.FindObjectsOfType<CanvasGroup>(true);
        foreach (var cg in groups)
        {
            if (!cg) continue;
            if (cg.gameObject.name == _fadeCanvasObjectName || cg.transform.name == _fadeCanvasObjectName)
                return cg;
        }
        return null;
    }

    private static IEnumerator FadeCanvasGroup(CanvasGroup cg, float targetAlpha, float duration)
    {
        if (cg == null) yield break;

        cg.blocksRaycasts = true;
        cg.interactable = true;

        float start = cg.alpha;
        float t = 0f;

        if (duration <= 0f)
        {
            cg.alpha = targetAlpha;
        }
        else
        {
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                cg.alpha = Mathf.Lerp(start, targetAlpha, t / duration);
                yield return null;
            }
            cg.alpha = targetAlpha;
        }

        if (Mathf.Approximately(targetAlpha, 0f))
        {
            cg.blocksRaycasts = false;
            cg.interactable = false;
        }
    }
}
