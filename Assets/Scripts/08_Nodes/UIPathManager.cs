using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class UIPathManager : MonoBehaviour
{
    [Header("Control Points in order (A → … → B)")]
    public RectTransform[] controlPoints;

    [Header("Sampling")]
    public int samplesPerSegment = 10;

    [Header("Runner")]
    public RectTransform runnerBall;
    public float runnerSpeed = 300f; // pixels / second

    [Header("Checkpoints")]
    public UICheckpoint[] checkpoints;

    [Header("Checkpoint Sprites (by order)")]
    public Sprite[] inactiveNumberSprites;
    public Sprite[] passedNumberSprites;
    public bool oneBasedSprites = true;

    [Header("Path Line (optional)")]
    public UIPathLineGraphic pathLine;

    // ---------------------------
    // ✅ LOOP REQUIREMENTS + UI INDICATOR
    // ---------------------------
    [Header("Win Condition: Required Loops")]
    [Tooltip("How many SUCCESSFUL laps are required to fully complete.")]
    public int loopsToComplete = 1;

    [Tooltip("Optional UI indicator text like: 'Loops: 1 / 3'")]
    public TMP_Text loopIndicatorText;

    [Tooltip("Label prefix for the loop indicator.")]
    public string loopIndicatorPrefix = "Loops";

    [Header("Loop Behavior")]
    [Tooltip("If true: after each successful lap (that isn't final), generate a NEW random puzzle.")]
    public bool randomizePuzzleEachSuccessfulLap = true;

    [Tooltip("Optional: assign your UIPathPuzzleGenerator for loop regeneration.")]
    public UIPathPuzzleGenerator generator;

    // ✅ Final solved event (Generator uses this)
    public event Action OnPuzzleSolved;

    // ---------------------------
    // ✅ SFX
    // ---------------------------
    [Header("SFX")]
    [Tooltip("AudioSource used for checkpoint SFX. If left empty, one will be auto-created.")]
    public AudioSource sfxSource;

    [Tooltip("Plays when the runner hits the correct checkpoint in order.")]
    public AudioClip correctCheckpointSfx;

    [Tooltip("Plays when the runner hits a wrong checkpoint and the lap resets.")]
    public AudioClip wrongCheckpointSfx;

    [Range(0f, 1f)] public float sfxVolume = 1f;

    [Tooltip("Prevents spam if multiple checkpoints are detected in the same frame.")]
    public float sfxCooldownSeconds = 0.05f;

    // ---------------------------
    // ✅ NODE SELECTION VISUALS
    // ---------------------------
    [Header("Selected Node Visuals")]
    [Tooltip("Enable highlighted color + breathing animation on selected node.")]
    public bool enableNodeSelectionVisuals = true;

    [Tooltip("Color used for the currently selected node.")]
    public Color selectedNodeColor = new Color(1f, 0.9f, 0.25f, 1f);

    [Tooltip("Optional different color while move mode is ON.")]
    public Color selectedNodeMoveModeColor = new Color(0.4f, 1f, 0.7f, 1f);

    [Tooltip("How strong the breathing scale effect is.")]
    public float selectedNodePulseScale = 1.12f;

    [Tooltip("How fast the breathing animation plays.")]
    public float selectedNodePulseSpeed = 3f;

    [Tooltip("If true, selected node scales up and down.")]
    public bool animateSelectedNodeScale = true;

    [Tooltip("If true, selected node is tinted with the selected color.")]
    public bool tintSelectedNode = true;

    // ---------------------------
    // ✅ CONTROLLER / KEYBOARD SUPPORT
    // ---------------------------
    [Header("Node Selection / Controller Support")]
    [Tooltip("Enables gamepad/keyboard-style node selection and movement.")]
    public bool enableNodeControllerSupport = true;

    [Tooltip("2D navigation input. Used to switch selected node OR move the selected node while in move mode.")]
    public InputActionReference navigateAction;

    [Tooltip("Press to confirm/select the currently highlighted candidate node. If nothing is selected yet, selects a default node.")]
    public InputActionReference selectAction;

    [Tooltip("Press to enter/exit move mode for the currently selected node.")]
    public InputActionReference toggleMoveAction;

    [Tooltip("Optional cancel/back action to exit move mode or clear selection.")]
    public InputActionReference cancelAction;

    [Tooltip("Movement speed of the selected node while in move mode.")]
    public float selectedNodeMoveSpeed = 500f;

    [Tooltip("Stick deadzone before navigation / movement starts.")]
    public float navigationDeadzone = 0.45f;

    [Tooltip("Delay between directional selection hops.")]
    public float navigationRepeatDelay = 0.18f;

    [Tooltip("If true, automatically select the first selectable node at Start.")]
    public bool autoSelectFirstNodeOnStart = false;

    [Tooltip("If false, the first and last control points (A/B) cannot be moved.")]
    public bool allowMovingEndpoints = false;

    [Tooltip("If false, endpoints are also skipped during selection navigation.")]
    public bool allowSelectingEndpoints = true;

    // ---------------------------
    // Internal path data
    // ---------------------------
    private readonly List<Vector2> sampledPoints = new List<Vector2>();
    private readonly List<float> cumulativeLen = new List<float>();
    private float totalLen = 0f;

    private float progressDist = 0f;

    private int nextCheckpointIndex;
    private bool puzzleCompleted;
    private bool allCheckpointsHitThisLap;

    private int _loopsCompleted = 0;
    private bool _isFailResetting = false;

    private Vector2[] _lastControlPositions;
    [Tooltip("How sensitive the path-change detector is (smaller = more sensitive).")]
    public float controlPointChangeEpsilon = 0.25f;

    private float _nextSfxTime = 0f;

    public IReadOnlyList<Vector2> SampledPoints => sampledPoints;

    // ---------------------------
    // ✅ Selection internals
    // ---------------------------
    private int _selectedControlIndex = -1;
    private int _candidateControlIndex = -1;
    private bool _selectedNodeMoveMode = false;
    private float _nextNavigationTime = 0f;

    private Vector3[] _originalNodeScales;
    private Graphic[] _nodeGraphics;
    private Color[] _originalNodeColors;

    private void Awake()
    {
        // ✅ Ensure we have an AudioSource for SFX
        if (sfxSource == null)
        {
            sfxSource = GetComponent<AudioSource>();
            if (sfxSource == null)
                sfxSource = gameObject.AddComponent<AudioSource>();

            sfxSource.playOnAwake = false;
            sfxSource.loop = false;
            sfxSource.spatialBlend = 0f; // UI sound
        }
    }

    private void OnEnable()
    {
        EnableNodeInputActions(true);
    }

    private void OnDisable()
    {
        EnableNodeInputActions(false);
        RestoreAllNodeVisuals();
    }

    private void Start()
    {
        CacheControlPointPositions();
        CacheNodeVisualState();

        RebuildPath();

        if (pathLine != null)
            pathLine.SetPoints(sampledPoints);

        ApplyCheckpointSprites();
        ResetRunner();
        UpdateLoopIndicator();

        if (enableNodeControllerSupport && autoSelectFirstNodeOnStart)
        {
            int first = GetDefaultSelectableIndex();
            if (first >= 0)
            {
                _candidateControlIndex = first;
                SelectControlPoint(first);
            }
        }
        else
        {
            int first = GetDefaultSelectableIndex();
            _candidateControlIndex = first;
            RefreshSelectedNodeVisuals();
        }
    }

    private void CacheControlPointPositions()
    {
        if (controlPoints == null)
        {
            _lastControlPositions = null;
            return;
        }

        _lastControlPositions = new Vector2[controlPoints.Length];
        for (int i = 0; i < controlPoints.Length; i++)
            _lastControlPositions[i] = controlPoints[i] ? controlPoints[i].anchoredPosition : Vector2.zero;
    }

    private bool ControlPointsChanged()
    {
        if (controlPoints == null || _lastControlPositions == null || _lastControlPositions.Length != controlPoints.Length)
            return true;

        float epsSqr = controlPointChangeEpsilon * controlPointChangeEpsilon;

        for (int i = 0; i < controlPoints.Length; i++)
        {
            var rt = controlPoints[i];
            if (!rt) continue;

            Vector2 now = rt.anchoredPosition;
            Vector2 old = _lastControlPositions[i];
            if ((now - old).sqrMagnitude > epsSqr)
                return true;
        }

        return false;
    }

    private void UpdateLastControlPositions()
    {
        if (controlPoints == null || _lastControlPositions == null) return;

        for (int i = 0; i < controlPoints.Length; i++)
        {
            var rt = controlPoints[i];
            if (!rt) continue;
            _lastControlPositions[i] = rt.anchoredPosition;
        }
    }

    public void RebuildPath()
    {
        sampledPoints.Clear();
        cumulativeLen.Clear();
        totalLen = 0f;

        if (controlPoints == null || controlPoints.Length < 2)
            return;

        for (int i = 0; i < controlPoints.Length - 1; i++)
        {
            Vector2 a = controlPoints[i].anchoredPosition;
            Vector2 b = controlPoints[i + 1].anchoredPosition;

            for (int s = 0; s < samplesPerSegment; s++)
            {
                float t = s / (float)samplesPerSegment;
                Vector2 p = Vector2.Lerp(a, b, t);
                sampledPoints.Add(p);
            }
        }

        sampledPoints.Add(controlPoints[controlPoints.Length - 1].anchoredPosition);

        if (sampledPoints.Count > 0)
        {
            cumulativeLen.Add(0f);
            for (int i = 1; i < sampledPoints.Count; i++)
            {
                totalLen += Vector2.Distance(sampledPoints[i - 1], sampledPoints[i]);
                cumulativeLen.Add(totalLen);
            }
        }

        if (pathLine != null)
            pathLine.SetPoints(sampledPoints);
    }

    private void Update()
    {
        if (sampledPoints.Count == 0 || runnerBall == null)
            return;

        HandleNodeControllerInput();
        UpdateSelectedNodeVisualsRealtime();

        if (ControlPointsChanged())
        {
            Vector2 oldPos = runnerBall.anchoredPosition;

            RebuildPath();
            UpdateLastControlPositions();

            SnapRunnerToPath(oldPos);

            if (pathLine != null)
                pathLine.SetPoints(sampledPoints);
        }

        if (puzzleCompleted)
        {
            runnerBall.anchoredPosition = sampledPoints[sampledPoints.Count - 1];
            return;
        }

        if (_isFailResetting)
            return;

        progressDist += runnerSpeed * Time.deltaTime;

        if (progressDist >= totalLen)
        {
            progressDist = totalLen;
            runnerBall.anchoredPosition = sampledPoints[sampledPoints.Count - 1];

            if (allCheckpointsHitThisLap && nextCheckpointIndex >= (checkpoints?.Length ?? 0))
            {
                HandleSuccessfulLap();
                return;
            }
            else
            {
                FailCurrentLap("End reached without all checkpoints");
                return;
            }
        }

        runnerBall.anchoredPosition = EvaluatePointAtDistance(progressDist);

        CheckCheckpoints();
    }

    // -------------------------------------------------------------
    // ✅ INPUT ACTION HELPERS
    // -------------------------------------------------------------
    private void EnableNodeInputActions(bool enable)
    {
        if (navigateAction != null && navigateAction.action != null)
        {
            if (enable) navigateAction.action.Enable();
            else navigateAction.action.Disable();
        }

        if (selectAction != null && selectAction.action != null)
        {
            if (enable) selectAction.action.Enable();
            else selectAction.action.Disable();
        }

        if (toggleMoveAction != null && toggleMoveAction.action != null)
        {
            if (enable) toggleMoveAction.action.Enable();
            else toggleMoveAction.action.Disable();
        }

        if (cancelAction != null && cancelAction.action != null)
        {
            if (enable) cancelAction.action.Enable();
            else cancelAction.action.Disable();
        }
    }

    // -------------------------------------------------------------
    // ✅ NODE SELECTION + MOVEMENT
    // -------------------------------------------------------------
    private void HandleNodeControllerInput()
    {
        if (!enableNodeControllerSupport || controlPoints == null || controlPoints.Length == 0)
            return;

        Vector2 nav = Vector2.zero;
        if (navigateAction != null && navigateAction.action != null)
            nav = navigateAction.action.ReadValue<Vector2>();

        // SELECT ACTION
        if (selectAction != null && selectAction.action != null && selectAction.action.WasPressedThisFrame())
        {
            if (_selectedControlIndex < 0)
            {
                int first = _candidateControlIndex >= 0 ? _candidateControlIndex : GetDefaultSelectableIndex();
                if (first >= 0)
                    SelectControlPoint(first);
            }
            else if (_candidateControlIndex >= 0)
            {
                SelectControlPoint(_candidateControlIndex);
            }
        }

        // TOGGLE MOVE ACTION
        if (toggleMoveAction != null && toggleMoveAction.action != null && toggleMoveAction.action.WasPressedThisFrame())
        {
            if (_selectedControlIndex >= 0)
                _selectedNodeMoveMode = !_selectedNodeMoveMode;
        }

        // CANCEL ACTION
        if (cancelAction != null && cancelAction.action != null && cancelAction.action.WasPressedThisFrame())
        {
            if (_selectedNodeMoveMode)
            {
                _selectedNodeMoveMode = false;
            }
            else
            {
                DeselectControlPoint();
            }
        }

        if (nav.magnitude < navigationDeadzone)
            return;

        // MOVE MODE = move selected node
        if (_selectedNodeMoveMode)
        {
            TryMoveSelectedNode(nav);
            return;
        }

        // NORMAL MODE = move candidate/highlight between nodes using Navigate
        if (Time.unscaledTime >= _nextNavigationTime)
        {
            int startIndex;

            if (_candidateControlIndex >= 0)
                startIndex = _candidateControlIndex;
            else if (_selectedControlIndex >= 0)
                startIndex = _selectedControlIndex;
            else
                startIndex = GetDefaultSelectableIndex();

            int next = FindNextControlPointInDirection(startIndex, nav.normalized);
            if (next >= 0)
                _candidateControlIndex = next;

            _nextNavigationTime = Time.unscaledTime + Mathf.Max(0.01f, navigationRepeatDelay);
        }
    }

    private void TryMoveSelectedNode(Vector2 nav)
    {
        if (_selectedControlIndex < 0 || _selectedControlIndex >= controlPoints.Length)
            return;

        if (!allowMovingEndpoints)
        {
            if (_selectedControlIndex == 0 || _selectedControlIndex == controlPoints.Length - 1)
                return;
        }

        RectTransform target = controlPoints[_selectedControlIndex];
        if (target == null)
            return;

        target.anchoredPosition += nav * selectedNodeMoveSpeed * Time.unscaledDeltaTime;
    }

    private int FindNextControlPointInDirection(int currentIndex, Vector2 dir)
    {
        if (controlPoints == null || controlPoints.Length == 0)
            return -1;

        if (currentIndex < 0 || currentIndex >= controlPoints.Length || controlPoints[currentIndex] == null)
            currentIndex = GetDefaultSelectableIndex();

        if (currentIndex < 0)
            return -1;

        RectTransform current = controlPoints[currentIndex];
        Vector2 origin = current.anchoredPosition;

        int bestIndex = -1;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < controlPoints.Length; i++)
        {
            if (i == currentIndex) continue;
            if (controlPoints[i] == null) continue;
            if (!IsSelectableIndex(i)) continue;

            Vector2 to = controlPoints[i].anchoredPosition - origin;
            float dist = to.magnitude;
            if (dist <= 0.001f) continue;

            Vector2 toNorm = to / dist;
            float alignment = Vector2.Dot(dir, toNorm);

            if (alignment <= 0.2f) continue;

            float score = alignment * 1000f - dist;
            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        return bestIndex >= 0 ? bestIndex : currentIndex;
    }

    private bool IsSelectableIndex(int index)
    {
        if (controlPoints == null || index < 0 || index >= controlPoints.Length)
            return false;

        if (controlPoints[index] == null)
            return false;

        if (!allowSelectingEndpoints)
        {
            if (index == 0 || index == controlPoints.Length - 1)
                return false;
        }

        return true;
    }

    private int GetDefaultSelectableIndex()
    {
        if (controlPoints == null || controlPoints.Length == 0)
            return -1;

        for (int i = 0; i < controlPoints.Length; i++)
        {
            if (IsSelectableIndex(i))
                return i;
        }

        return -1;
    }

    public void SelectControlPoint(int index)
    {
        if (controlPoints == null || controlPoints.Length == 0) return;
        if (index < 0 || index >= controlPoints.Length) return;
        if (!IsSelectableIndex(index)) return;

        _selectedControlIndex = index;
        _candidateControlIndex = index;
        RefreshSelectedNodeVisuals();
    }

    public void DeselectControlPoint()
    {
        _selectedControlIndex = -1;
        _selectedNodeMoveMode = false;

        if (_candidateControlIndex < 0 || !IsSelectableIndex(_candidateControlIndex))
            _candidateControlIndex = GetDefaultSelectableIndex();

        RefreshSelectedNodeVisuals();
    }

    public int GetSelectedControlIndex()
    {
        return _selectedControlIndex;
    }

    public bool IsSelectedNodeInMoveMode()
    {
        return _selectedNodeMoveMode;
    }

    // -------------------------------------------------------------
    // ✅ NODE VISUAL HELPERS
    // -------------------------------------------------------------
    private void CacheNodeVisualState()
    {
        if (controlPoints == null)
        {
            _originalNodeScales = null;
            _nodeGraphics = null;
            _originalNodeColors = null;
            return;
        }

        _originalNodeScales = new Vector3[controlPoints.Length];
        _nodeGraphics = new Graphic[controlPoints.Length];
        _originalNodeColors = new Color[controlPoints.Length];

        for (int i = 0; i < controlPoints.Length; i++)
        {
            RectTransform rt = controlPoints[i];
            if (rt == null) continue;

            _originalNodeScales[i] = rt.localScale;

            Graphic g = rt.GetComponent<Graphic>();
            if (g == null)
                g = rt.GetComponentInChildren<Graphic>(true);

            _nodeGraphics[i] = g;
            _originalNodeColors[i] = g != null ? g.color : Color.white;
        }
    }

    private void RefreshSelectedNodeVisuals()
    {
        if (!enableNodeSelectionVisuals || controlPoints == null)
            return;

        if (_originalNodeScales == null || _originalNodeScales.Length != controlPoints.Length)
            CacheNodeVisualState();

        for (int i = 0; i < controlPoints.Length; i++)
        {
            RectTransform rt = controlPoints[i];
            if (rt == null) continue;

            if (_originalNodeScales != null && i < _originalNodeScales.Length)
                rt.localScale = _originalNodeScales[i];

            if (_nodeGraphics != null && i < _nodeGraphics.Length && _nodeGraphics[i] != null)
            {
                if (_originalNodeColors != null && i < _originalNodeColors.Length)
                    _nodeGraphics[i].color = _originalNodeColors[i];
            }
        }
    }

    private void UpdateSelectedNodeVisualsRealtime()
    {
        if (!enableNodeSelectionVisuals || controlPoints == null)
            return;

        if (_originalNodeScales == null || _originalNodeScales.Length != controlPoints.Length)
            CacheNodeVisualState();

        for (int i = 0; i < controlPoints.Length; i++)
        {
            RectTransform rt = controlPoints[i];
            if (rt == null) continue;

            bool isSelected = i == _selectedControlIndex;
            bool isCandidate = !isSelected && !_selectedNodeMoveMode && i == _candidateControlIndex;

            if (!isSelected && !isCandidate)
            {
                if (_originalNodeScales != null && i < _originalNodeScales.Length)
                    rt.localScale = _originalNodeScales[i];

                if (_nodeGraphics != null && i < _nodeGraphics.Length && _nodeGraphics[i] != null)
                {
                    if (_originalNodeColors != null && i < _originalNodeColors.Length)
                        _nodeGraphics[i].color = _originalNodeColors[i];
                }

                continue;
            }

            float pulse = 1f;
            if (animateSelectedNodeScale && _originalNodeScales != null && i < _originalNodeScales.Length)
            {
                float pulseStrength = isSelected ? (selectedNodePulseScale - 1f) : ((selectedNodePulseScale - 1f) * 0.5f);
                pulse = 1f + (Mathf.Sin(Time.unscaledTime * selectedNodePulseSpeed) * 0.5f + 0.5f) * pulseStrength;
                rt.localScale = _originalNodeScales[i] * pulse;
            }

            if (tintSelectedNode && _nodeGraphics != null && i < _nodeGraphics.Length && _nodeGraphics[i] != null)
            {
                if (isSelected)
                {
                    _nodeGraphics[i].color = _selectedNodeMoveMode ? selectedNodeMoveModeColor : selectedNodeColor;
                }
                else if (isCandidate)
                {
                    _nodeGraphics[i].color = Color.Lerp(selectedNodeColor, _originalNodeColors[i], 0.45f);
                }
            }
        }
    }

    private void RestoreAllNodeVisuals()
    {
        if (controlPoints == null || _originalNodeScales == null)
            return;

        for (int i = 0; i < controlPoints.Length; i++)
        {
            RectTransform rt = controlPoints[i];
            if (rt == null) continue;

            if (i < _originalNodeScales.Length)
                rt.localScale = _originalNodeScales[i];

            if (_nodeGraphics != null && i < _nodeGraphics.Length && _nodeGraphics[i] != null &&
                _originalNodeColors != null && i < _originalNodeColors.Length)
            {
                _nodeGraphics[i].color = _originalNodeColors[i];
            }
        }
    }

    // -------------------------------------------------------------
    // ✅ SFX helpers
    // -------------------------------------------------------------
    private void PlaySfx(AudioClip clip)
    {
        if (clip == null || sfxSource == null) return;

        if (Time.unscaledTime < _nextSfxTime) return;
        _nextSfxTime = Time.unscaledTime + Mathf.Max(0f, sfxCooldownSeconds);

        sfxSource.PlayOneShot(clip, Mathf.Clamp01(sfxVolume));
    }

    // -------------------------------------------------------------
    // ✅ Always stick to the path
    // -------------------------------------------------------------
    private void SnapRunnerToPath(Vector2 posInPanelSpace)
    {
        if (sampledPoints.Count < 2)
            return;

        float bestDistSqr = float.MaxValue;
        float bestAlong = 0f;
        Vector2 bestPoint = sampledPoints[0];

        for (int i = 0; i < sampledPoints.Count - 1; i++)
        {
            Vector2 a = sampledPoints[i];
            Vector2 b = sampledPoints[i + 1];

            Vector2 ab = b - a;
            float abLenSqr = ab.sqrMagnitude;
            if (abLenSqr < 0.000001f)
                continue;

            float t = Vector2.Dot(posInPanelSpace - a, ab) / abLenSqr;
            t = Mathf.Clamp01(t);
            Vector2 proj = a + ab * t;

            float dSqr = (proj - posInPanelSpace).sqrMagnitude;
            if (dSqr < bestDistSqr)
            {
                bestDistSqr = dSqr;
                bestPoint = proj;

                float segStartAlong = cumulativeLen[i];
                float segLen = Vector2.Distance(a, b);
                bestAlong = segStartAlong + (segLen * t);
            }
        }

        progressDist = Mathf.Clamp(bestAlong, 0f, totalLen);
        runnerBall.anchoredPosition = bestPoint;
    }

    private Vector2 EvaluatePointAtDistance(float dist)
    {
        dist = Mathf.Clamp(dist, 0f, totalLen);

        int seg = 0;
        for (int i = 0; i < cumulativeLen.Count - 1; i++)
        {
            if (cumulativeLen[i + 1] >= dist)
            {
                seg = i;
                break;
            }
        }

        Vector2 a = sampledPoints[seg];
        Vector2 b = sampledPoints[seg + 1];
        float start = cumulativeLen[seg];
        float end = cumulativeLen[seg + 1];
        float len = Mathf.Max(0.000001f, end - start);
        float t = (dist - start) / len;

        return Vector2.Lerp(a, b, t);
    }

    // -------------------------------------------------------------
    // ✅ SUCCESSFUL LAP HANDLING
    // -------------------------------------------------------------
    private void HandleSuccessfulLap()
    {
        _loopsCompleted++;
        UpdateLoopIndicator();

        int req = Mathf.Max(1, loopsToComplete);

        var loop = FindObjectOfType<MinigameLoopController>();
        if (loop != null)
            loop.NotifyWin();

        if (_loopsCompleted >= req)
        {
            puzzleCompleted = true;
            progressDist = totalLen;
            runnerBall.anchoredPosition = sampledPoints[sampledPoints.Count - 1];

            OnPuzzleSolved?.Invoke();
            return;
        }

        if (randomizePuzzleEachSuccessfulLap)
        {
            if (generator == null)
                generator = FindObjectOfType<UIPathPuzzleGenerator>();

            if (generator != null)
            {
                generator.GenerateNewPuzzleForNextLoop();
                return;
            }
        }

        progressDist = 0f;
        runnerBall.anchoredPosition = sampledPoints[0];
        ResetCheckpointsOnly();
    }

    public void ApplyCheckpointSprites()
    {
        if (checkpoints == null) return;

        for (int i = 0; i < checkpoints.Length; i++)
        {
            var cp = checkpoints[i];
            if (cp == null) continue;

            if (cp.orderIndex != i)
                cp.orderIndex = i;

            cp.AssignSpritesByOrder(inactiveNumberSprites, passedNumberSprites, oneBasedSprites);

            if (cp.reached) cp.OnCorrectHit();
            else cp.ResetVisual();
        }
    }

    // -------------------------------------------------------------
    // ✅ CHECKPOINT LOGIC + SFX
    // -------------------------------------------------------------
    private void CheckCheckpoints()
    {
        if (_isFailResetting) return;
        if (checkpoints == null || checkpoints.Length == 0) return;

        Vector2 ballPos = runnerBall.anchoredPosition;

        for (int i = 0; i < checkpoints.Length; i++)
        {
            UICheckpoint cp = checkpoints[i];
            if (cp == null || cp.reached) continue;

            float dist = Vector2.Distance(ballPos, cp.Rect.anchoredPosition);
            if (dist > cp.radius) continue;

            if (cp.orderIndex == nextCheckpointIndex)
            {
                cp.reached = true;
                nextCheckpointIndex++;
                cp.OnCorrectHit();

                PlaySfx(correctCheckpointSfx);

                if (nextCheckpointIndex >= checkpoints.Length)
                    allCheckpointsHitThisLap = true;
            }
            else
            {
                PlaySfx(wrongCheckpointSfx);
                FailCurrentLap($"Wrong checkpoint order. Hit={cp.orderIndex}, expected={nextCheckpointIndex}");
                return;
            }
        }
    }

    private void FailCurrentLap(string reason)
    {
        if (_isFailResetting) return;
        _isFailResetting = true;

        ResetRunnerKeepLoops();

        _isFailResetting = false;
    }

    private void ResetCheckpointsOnly()
    {
        nextCheckpointIndex = 0;
        allCheckpointsHitThisLap = false;

        if (checkpoints == null) return;

        foreach (var cp in checkpoints)
        {
            if (cp == null) continue;
            cp.reached = false;
            cp.ResetVisual();
        }

        ApplyCheckpointSprites();
    }

    public void ResetRunnerKeepLoops()
    {
        puzzleCompleted = false;
        allCheckpointsHitThisLap = false;

        progressDist = 0f;

        if (sampledPoints.Count > 0 && runnerBall != null)
            runnerBall.anchoredPosition = sampledPoints[0];

        ResetCheckpointsOnly();
    }

    public void ResetRunner()
    {
        puzzleCompleted = false;
        allCheckpointsHitThisLap = false;

        _loopsCompleted = 0;
        UpdateLoopIndicator();

        progressDist = 0f;

        if (sampledPoints.Count > 0 && runnerBall != null)
            runnerBall.anchoredPosition = sampledPoints[0];

        ResetCheckpointsOnly();
    }

    private void UpdateLoopIndicator()
    {
        if (loopIndicatorText == null) return;

        int req = Mathf.Max(1, loopsToComplete);
        int done = Mathf.Clamp(_loopsCompleted, 0, req);

        loopIndicatorText.text = $"{loopIndicatorPrefix}: {done} / {req}";
    }
}