using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MinigameLoopController : MonoBehaviour
{
    [Header("Loop Settings")]
    [Tooltip("How many wins are required before returning to the world.")]
    public int winsRequired = 1;

    [Tooltip("Used only for legacy fallback systems that key by returnTag.")]
    public string returnTag = "Mini_PathPuzzle";

    [Tooltip("If empty, uses current active scene name.")]
    public string minigameSceneNameOverride = "";

    [Header("Debug")]
    public bool verboseLogs = true;

    private bool _handledThisWin;

    // ✅ Static in-memory remaining wins per RUN (not per scene)
    private static readonly Dictionary<string, int> _remainingByRunId = new Dictionary<string, int>();

    private string ActiveMinigameSceneName =>
        string.IsNullOrEmpty(minigameSceneNameOverride)
            ? SceneManager.GetActiveScene().name
            : minigameSceneNameOverride;

    private int Required => Mathf.Max(1, winsRequired);

    private string RunKey
    {
        get
        {
            // ✅ Primary: unique per entry trigger (set by MinigameReturnContext.Capture -> BeginRun)
            if (MinigameReturnContext.hasData && !string.IsNullOrWhiteSpace(MinigameReturnContext.runId))
                return MinigameReturnContext.runId;

            // Safety fallback: still survives reloads, but if you ever enter without Capture,
            // it can collide across entry points. (Best practice: ALWAYS enter via Capture.)
            return $"LOCAL__{ActiveMinigameSceneName}__{returnTag}";
        }
    }

    private void Awake()
    {
        // Initialize or resume remaining count for this RUN
        if (!_remainingByRunId.ContainsKey(RunKey))
        {
            _remainingByRunId[RunKey] = Required;

            if (verboseLogs)
                Debug.Log($"[MinigameLoopController] Awake NEW RUN. winsRequired={Required}, remaining={_remainingByRunId[RunKey]}, scene='{ActiveMinigameSceneName}', runKey='{RunKey}', tag='{returnTag}'");
        }
        else
        {
            // Clamp in case inspector value changed mid-run
            _remainingByRunId[RunKey] = Mathf.Clamp(_remainingByRunId[RunKey], 0, Required);

            if (verboseLogs)
                Debug.Log($"[MinigameLoopController] Awake RESUME RUN. winsRequired={Required}, remaining={_remainingByRunId[RunKey]}, scene='{ActiveMinigameSceneName}', runKey='{RunKey}', tag='{returnTag}'");
        }
    }

    private void OnEnable()
    {
        // allow another win event after reload
        _handledThisWin = false;
    }

    public void NotifyWin()
    {
        if (_handledThisWin) return;
        _handledThisWin = true;

        int remaining = Mathf.Max(0, _remainingByRunId[RunKey] - 1);
        _remainingByRunId[RunKey] = remaining;

        if (verboseLogs)
            Debug.Log($"[MinigameLoopController] WIN! Remaining wins now: {remaining} (of {Required}) runKey='{RunKey}'");

        // -------------------- LOOP CONTINUES --------------------
        if (remaining > 0)
        {
            if (verboseLogs)
                Debug.Log($"[MinigameLoopController] Reloading minigame scene: '{ActiveMinigameSceneName}' (progress preserved via runKey).");

            SceneManager.LoadScene(ActiveMinigameSceneName, LoadSceneMode.Single);
            return;
        }

        // -------------------- LOOP COMPLETE: RETURN --------------------
        // ✅ Cleanup so next entry doesn't inherit anything
        _remainingByRunId.Remove(RunKey);

        // ✅ PRIMARY: use your current return pipeline
        if (MinigameReturnContext.hasData && !string.IsNullOrWhiteSpace(MinigameReturnContext.returnSceneName))
        {
            if (verboseLogs)
                Debug.Log($"[MinigameLoopController] Loop complete. Returning via MinigameReturnContext to '{MinigameReturnContext.returnSceneName}'.");

            MinigameReturnContext.ReturnToWorld();
            return;
        }

        // ✅ FALLBACK (compile-safe): reflection call if MinigameReturnData exists
        if (verboseLogs)
            Debug.LogWarning("[MinigameLoopController] No MinigameReturnContext set. Trying legacy fallback via reflection...");

        bool ok = TryInvokeStatic("MinigameReturnData", "WinAndReturn");
        if (!ok)
        {
            Debug.LogError("[MinigameLoopController] No return context available. Enter the minigame via EnterMinigame step so MinigameReturnContext is captured.");
        }
    }

    // -------------------------------------------------------
    // Reflection fallback (compile-safe)
    // -------------------------------------------------------
    private static bool TryInvokeStatic(string typeName, string methodName, params object[] args)
    {
        try
        {
            var t = FindTypeByName(typeName);
            if (t == null) return false;

            var flags = System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Static;

            var m = (args == null || args.Length == 0)
                ? t.GetMethod(methodName, flags, null, Type.EmptyTypes, null)
                : t.GetMethod(methodName, flags);

            if (m == null) return false;

            m.Invoke(null, args);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Type FindTypeByName(string typeName)
    {
        // Fast path
        var t = Type.GetType(typeName);
        if (t != null) return t;

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            try
            {
                // by full name
                var tt = assemblies[i].GetType(typeName);
                if (tt != null) return tt;

                // by short name
                var types = assemblies[i].GetTypes();
                for (int k = 0; k < types.Length; k++)
                {
                    if (types[k].Name == typeName)
                        return types[k];
                }
            }
            catch { }
        }
        return null;
    }
}
