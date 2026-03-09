using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

[RequireComponent(typeof(UIPathManager))]
public class UIPathPuzzleGenerator : MonoBehaviour
{
    [Header("References")]
    public UIPathManager pathManager;

    [Tooltip("All control points used by the path manager (same order).")]
    public RectTransform[] controlPoints;  // Start, middle(s), End

    [Tooltip("Prefab with UICheckpoint + Image on it.")]
    public RectTransform checkpointPrefab;

    [Header("Checkpoint Generation")]
    public int minCheckpoints = 2;
    public int maxCheckpoints = 4;
    public float checkpointRadius = 40f;

    [Tooltip("Minimum distance in panel units between any two checkpoints.")]
    public float minCheckpointSpacing = 80f;

    [Header("Robust Generation")]
    [Tooltip("How many times to regenerate the entire puzzle if checkpoint placement fails.")]
    public int maxFullGenerateAttempts = 12;

    [Tooltip("How many attempts to pick valid checkpoint indices for a single puzzle.")]
    public int maxCheckpointPickAttempts = 40;

    [Header("Scramble")]
    public int maxScrambleAttempts = 30;
    public float minScrambleOffset = 60f;
    public float maxScrambleOffset = 180f;
    public int firstDraggableIndex = 1;
    public int lastDraggableIndex = -2;   // negative means 'len - 2'

    [Header("Win UI")]
    public GameObject winRoot;
    public Animator winAnimator;
    public string winTrigger = "Win";
    public float winHoldSeconds = 1.25f;

    [Header("Objective Update (Optional)")]
    public bool setObjectiveWinOnReturn = true;
    public string completeObjectiveIdOnWin = "";

    [Header("Win Behavior")]
    public bool lockNodesOnWin = true;

    [Header("Return Fallback (Safety)")]
    public string fallbackReturnSceneName = "";
    public bool forceUnpauseOnReturn = true;

    // ============================================================
    // START / FINISH INDICATORS (same style as your GalleryGame)
    // ============================================================

    [Header("Start Countdown Gate (NEW)")]
    public bool enableStartCountdownGate = true;
    public GameObject countdownRoot;
    public TMP_Text countdownText;
    public float countdownStepSeconds = 0.75f;
    public float countdownStartHoldSeconds = 0.35f;
    public string countdownStartText = "START";

    [Header("Finish Overlay (NEW)")]
    public GameObject finishRoot;
    public TMP_Text finishText;
    public string finishMessage = "FINISHED!";
    public float finishHoldSeconds = 0.8f;

    [Header("Countdown/Finish Ease (NEW)")]
    [Tooltip("Ease for fade/scale in/out on Countdown + Finish overlays (TEXT ONLY).")]
    public AnimationCurve countdownEase = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public float countdownScaleMin = 0.75f;
    public float countdownScaleMax = 1.15f;

    // ============================================================

    private Vector2[] solutionPositions;
    private RectTransform panelRect;
    private bool _won = false;

    // input + runner gate
    private bool inputLocked = false;
    private Coroutine _countdownCo;

    private void Awake()
    {
        if (pathManager == null)
            pathManager = GetComponent<UIPathManager>();

        panelRect = GetComponent<RectTransform>();

        if (pathManager != null && pathManager.generator == null)
            pathManager.generator = this;

        // ensure overlays start hidden
        if (countdownRoot) countdownRoot.SetActive(false);
        if (finishRoot) finishRoot.SetActive(false);
        if (winRoot) winRoot.SetActive(false);
    }

    private void OnEnable()
    {
        if (pathManager != null)
            pathManager.OnPuzzleSolved += HandlePuzzleSolved;
    }

    private void OnDisable()
    {
        if (pathManager != null)
            pathManager.OnPuzzleSolved -= HandlePuzzleSolved;
    }

    private void Start()
    {
        // IMPORTANT: first entry should NOT start runner until countdown ends.
        GenerateNewPuzzle(startRunnerImmediately: !enableStartCountdownGate);

        if (enableStartCountdownGate)
            BeginCountdownThenStartRunner(keepLoops: false);
        else
            SetInputLocked(false);
    }

    // ============================================================
    // PUBLIC GENERATION API
    // ============================================================

    public void GenerateNewPuzzleForNextLoop()
    {
        GenerateNewPuzzleForNextLoop(startRunnerImmediately: !enableStartCountdownGate);

        if (enableStartCountdownGate)
            BeginCountdownThenStartRunner(keepLoops: true);
        else
            SetInputLocked(false);
    }

    public void GenerateNewPuzzle()
    {
        GenerateNewPuzzle(startRunnerImmediately: !enableStartCountdownGate);

        if (enableStartCountdownGate)
            BeginCountdownThenStartRunner(keepLoops: false);
        else
            SetInputLocked(false);
    }

    // Overloads with runner control
    private void GenerateNewPuzzleForNextLoop(bool startRunnerImmediately)
    {
        _won = false;
        if (winRoot) winRoot.SetActive(false);

        // Always unlock at start of any new puzzle
        LockAllDragPoints(false);

        InternalGeneratePuzzleRobust();

        pathManager.RebuildPath();

        // DO NOT start runner yet if we are gating with countdown
        if (startRunnerImmediately)
            StartRunner(keepLoops: true);
        else
            StopRunner(); // make sure it is not moving

        if (!lockNodesOnWin)
        {
            // still ensure draggable points are usable
            LockAllDragPoints(false);
        }
    }

    private void GenerateNewPuzzle(bool startRunnerImmediately)
    {
        _won = false;
        if (winRoot) winRoot.SetActive(false);

        // Always unlock at start of any new puzzle
        LockAllDragPoints(false);

        InternalGeneratePuzzleRobust();

        pathManager.RebuildPath();

        if (startRunnerImmediately)
            StartRunner(keepLoops: false);
        else
            StopRunner(); // make sure it is not moving

        if (!lockNodesOnWin)
        {
            LockAllDragPoints(false);
        }
    }

    // ============================================================
    // ✅ RUNNER CONTROL (fix: runner must not move during countdown)
    // ============================================================

    private void StartRunner(bool keepLoops)
    {
        if (pathManager == null) return;

        // reset runner based on the same functions you already use
        if (keepLoops) pathManager.ResetRunnerKeepLoops();
        else pathManager.ResetRunner();

        // if pathManager component was disabled by StopRunner, re-enable it
        if (!pathManager.enabled) pathManager.enabled = true;
    }

    private void StopRunner()
    {
        if (pathManager == null) return;

        // Most runner movement is driven by UIPathManager Update/coroutines.
        // Disabling it is the safest "freeze runner" without touching Time.timeScale.
        if (pathManager.enabled)
            pathManager.enabled = false;
    }

    // ============================================================
    // ✅ COUNTDOWN GATE (freeze EVERYTHING until done)
    // ============================================================

    private void BeginCountdownThenStartRunner(bool keepLoops)
    {
        // cancel old countdown if something retriggers generation
        if (_countdownCo != null)
        {
            StopCoroutine(_countdownCo);
            _countdownCo = null;
        }

        _countdownCo = StartCoroutine(CountdownThenUnlockRoutine(keepLoops));
    }

    private IEnumerator CountdownThenUnlockRoutine(bool keepLoops)
    {
        // freeze input + dragging + runner
        SetInputLocked(true);
        StopRunner();
        LockAllDragPoints(true);

        if (countdownRoot == null || countdownText == null)
        {
            // no UI assigned -> just unlock & start
            LockAllDragPoints(false);
            SetInputLocked(false);
            StartRunner(keepLoops);
            yield break;
        }

        countdownRoot.SetActive(true);
        ForceEnableTree(countdownRoot);

        // animate TEXT ONLY
        var cg = countdownText.GetComponent<CanvasGroup>();
        if (cg == null) cg = countdownText.gameObject.AddComponent<CanvasGroup>();
        var rt = countdownText.rectTransform;

        // 3,2,1
        for (int n = 3; n >= 1; n--)
        {
            countdownText.text = n.ToString();
            yield return AnimateOverlayInOut(cg, rt, countdownStepSeconds);
        }

        // START
        countdownText.text = string.IsNullOrWhiteSpace(countdownStartText) ? "START" : countdownStartText;
        yield return AnimateOverlayInOut(cg, rt, Mathf.Max(0.05f, countdownStartHoldSeconds));

        countdownRoot.SetActive(false);

        // unlock nodes + input
        LockAllDragPoints(false);
        SetInputLocked(false);

        // now start runner
        StartRunner(keepLoops);

        _countdownCo = null;
    }

    private void SetInputLocked(bool locked)
    {
        inputLocked = locked;
    }

    // ============================================================
    // ✅ FINAL WIN (after required loops)
    // ============================================================

    private void HandlePuzzleSolved()
    {
        if (_won) return;
        _won = true;
        StartCoroutine(WinThenReturnRoutine());
    }

    private IEnumerator WinThenReturnRoutine()
    {
        // hard stop runner + input immediately
        SetInputLocked(true);
        StopRunner();

        if (lockNodesOnWin)
            LockAllDragPoints(true);

        if (winRoot)
        {
            winRoot.SetActive(true);
            ForceEnableTree(winRoot);
        }

        if (winAnimator && !string.IsNullOrEmpty(winTrigger))
        {
            try { winAnimator.ResetTrigger(winTrigger); } catch { }
            try { winAnimator.SetTrigger(winTrigger); } catch { }
        }

        if (setObjectiveWinOnReturn && ObjectiveManager.Instance != null)
        {
            if (!string.IsNullOrWhiteSpace(completeObjectiveIdOnWin))
                ObjectiveManager.Instance.CompleteObjective(completeObjectiveIdOnWin);
            else
                ObjectiveManager.Instance.CompleteCurrentObjective();
        }

        yield return new WaitForSecondsRealtime(Mathf.Max(0f, winHoldSeconds));

        // finish overlay before returning
        if (finishRoot != null && finishText != null)
            yield return StartCoroutine(FinishOverlayRoutine());

        if (forceUnpauseOnReturn)
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        // ✅ NEW: HUB MENU RETURN HAS PRIORITY (does not affect story flow)
        if (HubMinigameReturnContext.hasData && !string.IsNullOrWhiteSpace(HubMinigameReturnContext.returnSceneName))
        {
            HubMinigameReturnContext.ReturnToWorld();
            yield break;
        }

        // ✅ EXISTING: Story progression return
        if (MinigameReturnContext.hasData && !string.IsNullOrWhiteSpace(MinigameReturnContext.returnSceneName))
        {
            MinigameReturnContext.ReturnToWorld();
            yield break;
        }

        // ✅ SAFETY: fallback
        if (!string.IsNullOrWhiteSpace(fallbackReturnSceneName))
        {
            SceneManager.LoadScene(fallbackReturnSceneName, LoadSceneMode.Single);
            yield break;
        }

        Debug.LogError("[UIPathPuzzleGenerator] RETURN FAILED: No HubMinigameReturnContext, no MinigameReturnContext, and no fallbackReturnSceneName set.");
    }

    // ============================================================
    // Finish overlay (text-only ease)
    // ============================================================

    private IEnumerator FinishOverlayRoutine()
    {
        if (finishRoot == null || finishText == null) yield break;

        finishRoot.SetActive(true);
        ForceEnableTree(finishRoot);

        var cg = finishText.GetComponent<CanvasGroup>();
        if (cg == null) cg = finishText.gameObject.AddComponent<CanvasGroup>();
        var rt = finishText.rectTransform;

        finishText.text = string.IsNullOrWhiteSpace(finishMessage) ? "FINISHED!" : finishMessage;

        yield return AnimateOverlayInOut(cg, rt, Mathf.Max(0.05f, finishHoldSeconds));

        finishRoot.SetActive(false);
    }

    // ============================================================
    // ✅ ANIMATION PORTION (UPDATED ONLY)
    // - Uses TEXT’s real baseScale (not Vector3.one)
    // - Adds a small “settle” after the pop so it feels alive
    // - Unscaled time, same as your GalleryGame feel
    // ============================================================

    private IEnumerator AnimateOverlayInOut(CanvasGroup cg, RectTransform rt, float holdSeconds)
    {
        // cache the REAL scale of the text object
        Vector3 baseScale = (rt != null) ? rt.localScale : Vector3.one;

        float inDur = 0.20f;
        float settleDur = 0.08f;
        float outDur = 0.18f;

        yield return AnimateOverlay(cg, rt, baseScale, 0f, 1f, countdownScaleMin, countdownScaleMax, inDur);
        yield return AnimateOverlay(cg, rt, baseScale, 1f, 1f, countdownScaleMax, 1f, settleDur);

        yield return new WaitForSecondsRealtime(Mathf.Max(0.01f, holdSeconds));

        yield return AnimateOverlay(cg, rt, baseScale, 1f, 0f, 1f, countdownScaleMin, outDur);

        if (rt != null) rt.localScale = baseScale;
        if (cg != null) cg.alpha = 0f;
    }

    private IEnumerator AnimateOverlay(CanvasGroup cg, RectTransform rt, Vector3 baseScale, float a0, float a1, float s0, float s1, float dur)
    {
        if (cg != null) cg.alpha = a0;
        if (rt != null) rt.localScale = baseScale * s0;

        float t = 0f;
        dur = Mathf.Max(0.01f, dur);

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);
            float e = (countdownEase != null) ? countdownEase.Evaluate(u) : u;

            if (cg != null) cg.alpha = Mathf.LerpUnclamped(a0, a1, e);
            if (rt != null) rt.localScale = baseScale * Mathf.LerpUnclamped(s0, s1, e);

            yield return null;
        }

        if (cg != null) cg.alpha = a1;
        if (rt != null) rt.localScale = baseScale * s1;
    }

    // ============================================================
    // Helpers (your original logic)
    // ============================================================

    private void LockAllDragPoints(bool locked)
    {
        if (controlPoints == null) return;

        for (int i = 0; i < controlPoints.Length; i++)
        {
            var rt = controlPoints[i];
            if (!rt) continue;

            var drag = rt.GetComponent<UIDragPoint>();
            if (drag != null)
                drag.lockPosition = locked;
        }
    }

    private static void ForceEnableTree(GameObject root)
    {
        if (!root) return;

        if (!root.activeSelf) root.SetActive(true);

        var t = root.transform;
        for (int i = 0; i < t.childCount; i++)
        {
            var child = t.GetChild(i).gameObject;
            if (!child.activeSelf) child.SetActive(true);
            ForceEnableTree(child);
        }

        var cgs = root.GetComponentsInChildren<CanvasGroup>(true);
        foreach (var cg in cgs)
            cg.alpha = Mathf.Clamp01(cg.alpha <= 0f ? 1f : cg.alpha);

        var canvases = root.GetComponentsInChildren<Canvas>(true);
        foreach (var c in canvases) c.enabled = true;

        var images = root.GetComponentsInChildren<UnityEngine.UI.Image>(true);
        foreach (var img in images) img.enabled = true;

        var tmps = root.GetComponentsInChildren<TMPro.TMP_Text>(true);
        foreach (var tmp in tmps) tmp.enabled = true;
    }

    private void InternalGeneratePuzzleRobust()
    {
        if (controlPoints == null || controlPoints.Length < 2)
        {
            Debug.LogWarning("UIPathPuzzleGenerator: controlPoints not set.");
            return;
        }

        int lastIndex = (lastDraggableIndex < 0)
            ? controlPoints.Length - 2
            : Mathf.Min(lastDraggableIndex, controlPoints.Length - 2);

        firstDraggableIndex = Mathf.Clamp(firstDraggableIndex, 1, controlPoints.Length - 2);
        lastIndex = Mathf.Clamp(lastIndex, firstDraggableIndex, controlPoints.Length - 2);

        int maxReasonable = Mathf.Clamp((lastIndex - firstDraggableIndex + 2), 2, 6);
        int minCP = Mathf.Clamp(minCheckpoints, 1, 10);
        int maxCP = Mathf.Clamp(maxCheckpoints, minCP, 10);
        maxCP = Mathf.Min(maxCP, maxReasonable);

        for (int attempt = 0; attempt < Mathf.Max(1, maxFullGenerateAttempts); attempt++)
        {
            int checkpointCount = UnityEngine.Random.Range(minCP, maxCP + 1);

            RandomizeMiddleNodesSolution(firstDraggableIndex, lastIndex);

            pathManager.RebuildPath();

            var checkpoints = BuildCheckpointsAlongCurrentPathRobust(checkpointCount);

            if (checkpoints == null || checkpoints.Length == 0)
                continue;

            pathManager.checkpoints = checkpoints;
            pathManager.ApplyCheckpointSprites();

            solutionPositions = new Vector2[controlPoints.Length];
            for (int i = 0; i < controlPoints.Length; i++)
                solutionPositions[i] = controlPoints[i].anchoredPosition;

            ScrambleMiddleNodesAwayFromSolution(firstDraggableIndex, lastIndex, checkpoints);

            pathManager.RebuildPath();

            return;
        }

        Debug.LogError("[UIPathPuzzleGenerator] Failed to generate a safe puzzle after many attempts. Consider reducing minCheckpointSpacing or checkpointCount range.");
    }

    private void RandomizeMiddleNodesSolution(int firstIndex, int lastIndex)
    {
        Vector2 halfSize = panelRect.rect.size * 0.5f;
        Vector2 min = -halfSize + Vector2.one * 40f;
        Vector2 max = halfSize - Vector2.one * 40f;

        for (int i = firstIndex; i <= lastIndex; i++)
        {
            float x = UnityEngine.Random.Range(min.x, max.x);
            float y = UnityEngine.Random.Range(min.y, max.y);
            controlPoints[i].anchoredPosition = new Vector2(x, y);
        }
    }

    private UICheckpoint[] BuildCheckpointsAlongCurrentPathRobust(int checkpointCount)
    {
        if (pathManager.checkpoints != null)
        {
            foreach (var cp in pathManager.checkpoints)
            {
                if (cp != null)
                    Destroy(cp.gameObject);
            }
        }

        List<Vector2> pts = new List<Vector2>(pathManager.SampledPoints);
        if (pts.Count < 5)
            return Array.Empty<UICheckpoint>();

        int minIndex = 1;
        int maxIndex = pts.Count - 2;

        int usable = (maxIndex - minIndex + 1);
        if (usable <= checkpointCount)
            checkpointCount = Mathf.Max(1, usable - 1);

        for (int attempt = 0; attempt < Mathf.Max(1, maxCheckpointPickAttempts); attempt++)
        {
            int[] indices = PickOrderedIndices(minIndex, maxIndex, checkpointCount);

            if (!ValidateIndexSpacingByDistance(pts, indices, minCheckpointSpacing))
                continue;

            UICheckpoint[] cps = new UICheckpoint[checkpointCount];
            for (int i = 0; i < checkpointCount; i++)
            {
                Vector2 pos = pts[indices[i]];

                RectTransform cpRT = Instantiate(checkpointPrefab, panelRect);
                cpRT.anchoredPosition = pos;

                UICheckpoint cp = cpRT.GetComponent<UICheckpoint>();
                cp.orderIndex = i;
                cp.radius = checkpointRadius;
                cps[i] = cp;
            }

            return cps;
        }

        return null;
    }

    private int[] PickOrderedIndices(int minIndex, int maxIndex, int count)
    {
        int[] indices = new int[count];

        int span = (maxIndex - minIndex + 1);
        float step = span / (float)(count + 1);

        int prev = minIndex - 1;

        for (int i = 0; i < count; i++)
        {
            float center = minIndex + step * (i + 1);

            int jitterRange = Mathf.Max(1, Mathf.RoundToInt(step * 0.35f));
            int pick = Mathf.RoundToInt(center) + UnityEngine.Random.Range(-jitterRange, jitterRange + 1);

            pick = Mathf.Clamp(pick, minIndex, maxIndex);

            if (pick <= prev)
                pick = Mathf.Min(maxIndex, prev + 1);

            indices[i] = pick;
            prev = pick;
        }

        for (int i = count - 2; i >= 0; i--)
        {
            if (indices[i] >= indices[i + 1])
                indices[i] = Mathf.Max(minIndex, indices[i + 1] - 1);
        }

        return indices;
    }

    private bool ValidateIndexSpacingByDistance(List<Vector2> pts, int[] indices, float minSpacing)
    {
        float minSqr = minSpacing * minSpacing;

        for (int i = 0; i < indices.Length; i++)
        {
            Vector2 a = pts[indices[i]];
            for (int j = i + 1; j < indices.Length; j++)
            {
                Vector2 b = pts[indices[j]];
                if ((a - b).sqrMagnitude < minSqr)
                    return false;
            }
        }
        return true;
    }

    private void ScrambleMiddleNodesAwayFromSolution(int firstIndex, int lastIndex, UICheckpoint[] cps)
    {
        Vector2 halfSize = panelRect.rect.size * 0.5f;
        Vector2 min = -halfSize + Vector2.one * 40f;
        Vector2 max = halfSize - Vector2.one * 40f;

        int attempts = 0;

        while (attempts < maxScrambleAttempts)
        {
            attempts++;

            for (int i = firstIndex; i <= lastIndex; i++)
            {
                Vector2 sol = solutionPositions[i];

                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float dist = UnityEngine.Random.Range(minScrambleOffset, maxScrambleOffset);
                Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;

                Vector2 p = sol + offset;
                p.x = Mathf.Clamp(p.x, min.x, max.x);
                p.y = Mathf.Clamp(p.y, min.y, max.y);

                controlPoints[i].anchoredPosition = p;
            }

            pathManager.RebuildPath();

            if (!PathCurrentlyHitsAllCheckpointsInOrder(cps))
                break;
        }
    }

    private bool PathCurrentlyHitsAllCheckpointsInOrder(UICheckpoint[] cps)
    {
        if (cps == null || cps.Length == 0)
            return false;

        var pts = pathManager.SampledPoints;
        int next = 0;

        for (int i = 0; i < pts.Count; i++)
        {
            Vector2 p = pts[i];
            UICheckpoint cp = cps[next];

            float d = Vector2.Distance(p, cp.Rect.anchoredPosition);
            if (d <= cp.radius)
            {
                next++;
                if (next >= cps.Length)
                    return true;
            }
        }
        return false;
    }
}
