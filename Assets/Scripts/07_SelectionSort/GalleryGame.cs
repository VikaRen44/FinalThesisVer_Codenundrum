using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

public class GalleryGame : MonoBehaviour
{
    [Header("Data")]
    public PortraitSet set;

    [Header("Scene Refs")]
    public Transform portraitGridParent;

    public PortraitCard portraitCardPrefab;

    [Tooltip("Preferred: drag the PortraitCard PREFAB GameObject here (not a scene instance).")]
    public GameObject portraitCardPrefabGO;

    [Header("UI")]
    [Tooltip("This MUST show the Sort Key line (never 'Pass 1').")]
    public TMP_Text passMarkerText;

    public TMP_Text hudText;
    public TMP_Text hintText;

    public TMP_Text boxAText;
    public TMP_Text boxBText;

    public Button chooseButton;
    public Button switchButton;

    [Header("Win UI")]
    [Tooltip("Root GameObject for your Win panel (disabled by default).")]
    public GameObject winRoot;

    [Tooltip("Animator on the Win UI. Optional.")]
    public Animator winAnimator;

    [Tooltip("Trigger name for the win animation.")]
    public string winTrigger = "Win";

    [Tooltip("How long to keep the win UI before returning.")]
    public float winHoldSeconds = 1.25f;

    [Header("Objective Update (Optional)")]
    [Tooltip("If ON: call ObjectiveManager on win before returning.")]
    public bool setObjectiveWinOnReturn = true;

    [Tooltip("If filled: ObjectiveManager.CompleteObjective(id). If empty: CompleteCurrentObjective().")]
    public string completeObjectiveIdOnWin = "";

    [Header("Options")]
    public bool showHud = false;

    [Header("Runtime Randomization")]
    public bool randomizeTimeEachPlay = true;
    public bool randomizeSizeEachPlay = true;
    public Vector2 fileSizeValueRange = new Vector2(1, 999);
    public bool randomizeUnitEachPlay = true;

    [Header("Runtime Sort Key Randomization")]
    [Tooltip("If ON: the Sort Key (Smallest/Largest/Early/Late) will be randomized each time this minigame starts.\nThis does NOT modify your PortraitSet asset; it uses a runtime-only key.")]
    public bool randomizeSortKeyEachPlay = false;

    [Tooltip("If OFF: the randomizer won't pick SIZE sort keys (Smallest/Largest).")]
    public bool allowSizeSortKeys = true;

    [Tooltip("If OFF: the randomizer won't pick TIME sort keys (Early/Late).")]
    public bool allowTimeSortKeys = true;

    [Header("Swap Animation")]
    public float swapDuration = 0.25f;
    public AnimationCurve swapCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

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
    [Tooltip("Ease for fade/scale in/out on Countdown + Finish overlays.")]
    public AnimationCurve countdownEase = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public float countdownScaleMin = 0.75f;
    public float countdownScaleMax = 1.15f;

    [Header("Countdown/Finish Text Size (NEW)")]
    [Tooltip("If > 1, countdown text becomes bigger during 3-2-1-START (then restored).")]
    public float countdownFontSizeMultiplier = 3.0f;

    [Tooltip("If > 1, finish text becomes bigger during FINISHED! (then restored).")]
    public float finishFontSizeMultiplier = 2.5f;

    [Tooltip("If ON, forces center alignment for countdown/finish text while showing.")]
    public bool forceCenterAlignForCountdown = true;

    [Header("Controller / Keyboard Support")]
    public bool enableControllerSupport = true;

    [Tooltip("Usually UI/Navigate or a gameplay Vector2 action.")]
    public InputActionReference navigateAction;

    [Tooltip("Submit / South button / Enter for Choose.")]
    public InputActionReference chooseAction;

    [Tooltip("Another action for Switch / confirm swap.")]
    public InputActionReference switchAction;

    [Tooltip("Optional cancel action. Clears Box B first, then clears cursor.")]
    public InputActionReference cancelAction;

    [Tooltip("Delay between left/right card navigation steps.")]
    public float navigationRepeatDelay = 0.16f;

    [Tooltip("Deadzone for horizontal navigation.")]
    public float navigationDeadzone = 0.45f;

    [Tooltip("If true, auto-select the current front/pass card when the game starts.")]
    public bool autoSelectFrontCardOnStart = true;

    [Tooltip("Optional gallery scroller for paging when selection moves.")]
    public ManualHorizontalScroller galleryScroller;

    [Tooltip("Approximate visible card count per page when syncing scroller.")]
    public int cardsPerPage = 5;

    public event Action OnFinished;

    private readonly List<PortraitCard> cards = new();
    private readonly List<PortraitData> order = new();

    private class RuntimeStats
    {
        public double bytes;
        public string sizeLabel;
        public int timeSeconds;
        public string timeLabel;
    }

    private readonly Dictionary<PortraitData, RuntimeStats> stats = new();

    private int passIndex = 0;
    private int? cursorIndex = null;
    private int? pickB = null;
    private bool finished = false;
    private bool isSwapping = false;
    private bool inputLocked = false;

    private SortKey _runtimeSortKey;

    private float _nextNavTime = 0f;

    PortraitCard ResolvePrefab()
    {
        if (portraitCardPrefabGO != null)
        {
            var pc = portraitCardPrefabGO.GetComponent<PortraitCard>();
            if (pc == null)
            {
                Debug.LogError("[GalleryGame] portraitCardPrefabGO has no PortraitCard component. Drag the correct prefab root.", this);
                return null;
            }
            return pc;
        }

        if (portraitCardPrefab != null)
            return portraitCardPrefab;

        Debug.LogError("[GalleryGame] No PortraitCard prefab assigned. Assign portraitCardPrefabGO (preferred) or portraitCardPrefab.", this);
        return null;
    }

    static void ForceEnableTree(GameObject root)
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

        var images = root.GetComponentsInChildren<Image>(true);
        foreach (var img in images) img.enabled = true;

        var tmps = root.GetComponentsInChildren<TMP_Text>(true);
        foreach (var tmp in tmps) tmp.enabled = true;
    }

    void OnEnable()
    {
        EnableActions(true);
    }

    void OnDisable()
    {
        EnableActions(false);
    }

    void Start()
    {
        if (chooseButton) chooseButton.onClick.AddListener(OnChoose);
        if (switchButton) switchButton.onClick.AddListener(OnSwitch);

        if (hudText) hudText.gameObject.SetActive(showHud);

        if (winRoot) winRoot.SetActive(false);
        if (countdownRoot) countdownRoot.SetActive(false);
        if (finishRoot) finishRoot.SetActive(false);

        InitRound();

        if (enableStartCountdownGate)
            StartCoroutine(StartCountdownGateRoutine());
        else
            SetInputLocked(false);
    }

    private void EnableActions(bool enable)
    {
        if (navigateAction != null && navigateAction.action != null)
        {
            if (enable) navigateAction.action.Enable();
            else navigateAction.action.Disable();
        }

        if (chooseAction != null && chooseAction.action != null)
        {
            if (enable) chooseAction.action.Enable();
            else chooseAction.action.Disable();
        }

        if (switchAction != null && switchAction.action != null)
        {
            if (enable) switchAction.action.Enable();
            else switchAction.action.Disable();
        }

        if (cancelAction != null && cancelAction.action != null)
        {
            if (enable) cancelAction.action.Enable();
            else cancelAction.action.Disable();
        }
    }

    void Update()
    {
        HandleControllerInput();
    }

    void InitRound()
    {
        finished = false;
        isSwapping = false;

        if (set == null || set.portraits == null)
        {
            Debug.LogError("[GalleryGame] PortraitSet is missing or empty.", this);
            return;
        }

        _runtimeSortKey = ResolveRuntimeSortKey();

        var prefab = ResolvePrefab();
        if (prefab == null) return;

        order.Clear();
        order.AddRange(set.portraits);
        Shuffle(order);

        stats.Clear();
        for (int i = 0; i < order.Count; i++)
        {
            var p = order[i];
            stats[p] = BuildRuntimeStats(p);
        }

        if (portraitGridParent == null)
        {
            Debug.LogError("[GalleryGame] portraitGridParent is NULL, cannot spawn cards.", this);
            return;
        }

        foreach (Transform c in portraitGridParent) Destroy(c.gameObject);
        cards.Clear();

        for (int i = 0; i < order.Count; i++)
        {
            int idx = i;
            var p = order[i];
            var s = stats[p];

            var card = Instantiate(prefab, portraitGridParent);
            ForceEnableTree(card.gameObject);

            if (card.visual != null && !card.visual.gameObject.activeSelf)
                card.visual.gameObject.SetActive(true);

            card.Bind(p, idx, () => OnCardClicked(idx), s.sizeLabel, s.timeLabel);
            cards.Add(card);
        }

        passIndex = 0;
        cursorIndex = null;
        pickB = null;

        if (hintText) hintText.text = "Select a card, then press Choose.";

        RefreshUI();
        UpdateFrontHighlights();
        UpdateAllHighlights();
        UpdateLocks();

        AutoAdvanceWhileFrontCorrect(showHint: false);
        AutoCompleteIfSingleLeft();

        if (enableControllerSupport && autoSelectFrontCardOnStart)
        {
            int startIndex = Mathf.Clamp(passIndex, 0, Mathf.Max(0, cards.Count - 1));
            if (startIndex < cards.Count)
                OnCardClicked(startIndex);

            SyncScrollerToCursor();
        }
    }

    private void HandleControllerInput()
    {
        if (!enableControllerSupport) return;
        if (finished || isSwapping || inputLocked) return;
        if (cards == null || cards.Count == 0) return;

        if (chooseAction != null && chooseAction.action != null && chooseAction.action.WasPressedThisFrame())
            OnChoose();

        if (switchAction != null && switchAction.action != null && switchAction.action.WasPressedThisFrame())
            OnSwitch();

        if (cancelAction != null && cancelAction.action != null && cancelAction.action.WasPressedThisFrame())
        {
            if (pickB.HasValue)
            {
                pickB = null;
                if (hintText) hintText.text = "Box B cleared.";
                UpdateAllHighlights();
                RefreshUI();
            }
            else if (cursorIndex.HasValue)
            {
                cursorIndex = null;
                if (hintText) hintText.text = "Selection cleared.";
                UpdateAllHighlights();
                RefreshUI();
            }
        }

        Vector2 nav = Vector2.zero;
        if (navigateAction != null && navigateAction.action != null)
            nav = navigateAction.action.ReadValue<Vector2>();

        if (Mathf.Abs(nav.x) < navigationDeadzone)
            return;

        if (Time.unscaledTime < _nextNavTime)
            return;

        int baseIndex;
        if (cursorIndex.HasValue) baseIndex = cursorIndex.Value;
        else baseIndex = Mathf.Clamp(passIndex, 0, Mathf.Max(0, cards.Count - 1));

        int dir = nav.x > 0f ? 1 : -1;
        int next = FindNextSelectableCardIndex(baseIndex, dir);

        if (next != baseIndex && next >= 0 && next < cards.Count)
        {
            OnCardClicked(next);
            SyncScrollerToCursor();
        }

        _nextNavTime = Time.unscaledTime + Mathf.Max(0.01f, navigationRepeatDelay);
    }

    private int FindNextSelectableCardIndex(int start, int dir)
    {
        if (cards == null || cards.Count == 0)
            return -1;

        int idx = Mathf.Clamp(start, 0, cards.Count - 1);

        while (true)
        {
            int next = idx + dir;
            if (next < passIndex || next >= cards.Count)
                return idx;

            idx = next;
            if (idx >= passIndex)
                return idx;
        }
    }

    private void SyncScrollerToCursor()
    {
        if (galleryScroller == null) return;
        if (!cursorIndex.HasValue) return;

        galleryScroller.ScrollToApproxIndex(cursorIndex.Value, Mathf.Max(1, cardsPerPage));
    }

    SortKey ResolveRuntimeSortKey()
    {
        SortKey fallback = set != null ? set.sortKey : SortKey.SmallestToLargest;

        if (!randomizeSortKeyEachPlay)
            return fallback;

        List<SortKey> allowed = new List<SortKey>(4);

        if (allowSizeSortKeys)
        {
            allowed.Add(SortKey.SmallestToLargest);
            allowed.Add(SortKey.LargestToSmallest);
        }

        if (allowTimeSortKeys)
        {
            allowed.Add(SortKey.OldestToLatest);
            allowed.Add(SortKey.LatestToOldest);
        }

        if (allowed.Count == 0)
            return fallback;

        int pick = UnityEngine.Random.Range(0, allowed.Count);
        return allowed[pick];
    }

    RuntimeStats BuildRuntimeStats(PortraitData p)
    {
        var rs = new RuntimeStats();

        float value = p ? p.fileSizeValue : 100f;
        FileSizeUnit unit = p ? p.fileSizeUnit : FileSizeUnit.KB;

        if (randomizeSizeEachPlay)
            value = UnityEngine.Random.Range(fileSizeValueRange.x, fileSizeValueRange.y + 0.0001f);

        if (randomizeUnitEachPlay)
            unit = (FileSizeUnit)UnityEngine.Random.Range(0, Enum.GetValues(typeof(FileSizeUnit)).Length);

        rs.bytes = ToBytes(value, unit);
        rs.sizeLabel = $"{Mathf.RoundToInt(value)} {unit}";

        int seconds = randomizeTimeEachPlay
            ? UnityEngine.Random.Range(0, 24 * 60 * 60)
            : ParseIsoToSeconds(p != null ? p.timeIso : "");

        rs.timeSeconds = seconds;
        rs.timeLabel = SecondsToHMS(seconds);

        return rs;
    }

    void OnCardClicked(int idx)
    {
        if (finished || isSwapping || inputLocked) return;

        if (idx < passIndex)
        {
            if (hintText) hintText.text = "That slot is locked.";
            return;
        }

        cursorIndex = idx;
        if (hintText) hintText.text = "Press Choose to lock this card into Box B.";
        UpdateAllHighlights();
        RefreshUI();
    }

    void OnChoose()
    {
        if (finished || isSwapping || inputLocked) return;

        if (!cursorIndex.HasValue)
        {
            if (hintText) hintText.text = "Select a card first.";
            return;
        }

        int idx = cursorIndex.Value;
        if (idx < passIndex)
        {
            if (hintText) hintText.text = "That slot is locked.";
            return;
        }

        pickB = idx;
        if (hintText) hintText.text = $"Locked index {idx + 1} into Box B. Now press Switch.";
        UpdateAllHighlights();
        RefreshUI();
    }

    void OnSwitch()
    {
        if (finished || isSwapping || inputLocked) return;

        int trueBest = FindBestIndex(passIndex);
        bool bestIsFrontAlready = (trueBest == passIndex);

        if (bestIsFrontAlready)
        {
            AutoAdvanceWhileFrontCorrect(showHint: true);
            PostRefresh();
            return;
        }

        if (!pickB.HasValue)
        {
            if (hintText) hintText.text = "Pick the correct card for this pass.";
            PostRefresh();
            return;
        }

        int candidate = pickB.Value;

        if (candidate != trueBest)
        {
            if (hintText) hintText.text = "✖ Incorrect — try again.";
            pickB = null;
            cursorIndex = null;
            RefreshUI();
            UpdateAllHighlights();

            if (enableControllerSupport)
            {
                int startIndex = Mathf.Clamp(passIndex, 0, Mathf.Max(0, cards.Count - 1));
                if (startIndex < cards.Count)
                    OnCardClicked(startIndex);
            }

            return;
        }

        StartCoroutine(SwapAnimated(passIndex, candidate));
    }

    void PostRefresh()
    {
        RefreshUI();
        UpdateFrontHighlights();
        UpdateAllHighlights();
        UpdateLocks();
        AutoCompleteIfSingleLeft();
        SyncScrollerToCursor();
    }

    void AcceptAndAdvance()
    {
        if (passIndex >= 0 && passIndex < cards.Count)
            cards[passIndex].SetLocked(true);

        passIndex++;
        pickB = null;
        cursorIndex = null;

        if (passIndex >= order.Count - 1)
            Finish();
        else if (enableControllerSupport)
            OnCardClicked(Mathf.Clamp(passIndex, 0, Mathf.Max(0, cards.Count - 1)));
    }

    void AutoAdvanceWhileFrontCorrect(bool showHint)
    {
        if (order == null || order.Count == 0) return;

        int safety = 0;
        while (!finished && passIndex < order.Count - 1)
        {
            int best = FindBestIndex(passIndex);
            if (best != passIndex) break;

            if (showHint && hintText) hintText.text = "✔ Already correct — advancing.";
            AcceptAndAdvance();

            safety++;
            if (safety > 999) break;
        }
    }

    IEnumerator SwapAnimated(int i, int j)
    {
        if (i == j) yield break;

        isSwapping = true;

        if (chooseButton) chooseButton.interactable = false;
        if (switchButton) switchButton.interactable = false;

        var cardA = cards[i];
        var cardB = cards[j];

        var rtA = cardA.GetComponent<RectTransform>();
        var rtB = cardB.GetComponent<RectTransform>();

        if (rtA == null || rtB == null)
        {
            SwapInstant(i, j);
            if (hintText) hintText.text = "✔ Correct!";
            AcceptAndAdvance();

            AutoAdvanceWhileFrontCorrect(showHint: false);

            PostRefresh();
            isSwapping = false;
            yield break;
        }

        Vector3 aStart = rtA.position;
        Vector3 bStart = rtB.position;

        int aSibling = rtA.GetSiblingIndex();
        int bSibling = rtB.GetSiblingIndex();
        rtA.SetSiblingIndex(bSibling);
        rtB.SetSiblingIndex(aSibling);

        Canvas.ForceUpdateCanvases();
        Vector3 aTarget = rtA.position;
        Vector3 bTarget = rtB.position;

        var layout = portraitGridParent != null ? portraitGridParent.GetComponent<UnityEngine.UI.LayoutGroup>() : null;
        bool layoutWasEnabled = false;
        if (layout != null)
        {
            layoutWasEnabled = layout.enabled;
            layout.enabled = false;
        }

        rtA.position = aStart;
        rtB.position = bStart;

        float dur = Mathf.Max(0.01f, swapDuration);
        float t = 0f;

        while (t < dur)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / dur);
            float e = swapCurve != null ? swapCurve.Evaluate(u) : u;

            rtA.position = Vector3.LerpUnclamped(aStart, aTarget, e);
            rtB.position = Vector3.LerpUnclamped(bStart, bTarget, e);

            yield return null;
        }

        rtA.position = aTarget;
        rtB.position = bTarget;

        if (layout != null) layout.enabled = layoutWasEnabled;

        (order[i], order[j]) = (order[j], order[i]);
        (cards[i], cards[j]) = (cards[j], cards[i]);

        RebindCard(i);
        RebindCard(j);

        if (hintText) hintText.text = "✔ Correct!";
        AcceptAndAdvance();

        AutoAdvanceWhileFrontCorrect(showHint: false);

        PostRefresh();

        isSwapping = false;
    }

    void SwapInstant(int i, int j)
    {
        (order[i], order[j]) = (order[j], order[i]);
        RebindCard(i);
        RebindCard(j);
    }

    int FindBestIndex(int start)
    {
        int best = start;
        for (int i = start + 1; i < order.Count; i++)
        {
            if (Compare(order[i], order[best]) < 0)
                best = i;
        }
        return best;
    }

    int Compare(PortraitData a, PortraitData b)
    {
        var sa = stats[a];
        var sb = stats[b];

        switch (_runtimeSortKey)
        {
            case SortKey.SmallestToLargest:
                return sa.bytes.CompareTo(sb.bytes);

            case SortKey.LargestToSmallest:
                return sb.bytes.CompareTo(sa.bytes);

            case SortKey.OldestToLatest:
                return sa.timeSeconds.CompareTo(sb.timeSeconds);

            case SortKey.LatestToOldest:
                return sb.timeSeconds.CompareTo(sa.timeSeconds);

            default:
                return sa.bytes.CompareTo(sb.bytes);
        }
    }

    void RebindCard(int i)
    {
        int idx = i;
        var p = order[i];
        var s = stats[p];

        cards[i].Bind(p, idx, () => OnCardClicked(idx), s.sizeLabel, s.timeLabel);

        cards[i].SetLocked(i < passIndex);
        cards[i].SetFront(i == passIndex);

        bool chosen = (pickB.HasValue && pickB.Value == i);
        bool cursor = (cursorIndex.HasValue && cursorIndex.Value == i);
        cards[i].SetSelected(chosen || cursor);

        ForceEnableTree(cards[i].gameObject);
        if (cards[i].visual != null && !cards[i].visual.gameObject.activeSelf)
            cards[i].visual.gameObject.SetActive(true);
    }

    void UpdateFrontHighlights()
    {
        for (int i = 0; i < cards.Count; i++)
            cards[i].SetFront(i == passIndex);
    }

    void UpdateLocks()
    {
        for (int i = 0; i < cards.Count; i++)
            cards[i].SetLocked(i < passIndex);
    }

    void UpdateAllHighlights()
    {
        for (int i = 0; i < cards.Count; i++)
        {
            bool chosen = (pickB.HasValue && pickB.Value == i);
            bool cursor = (cursorIndex.HasValue && cursorIndex.Value == i);
            cards[i].SetSelected(chosen || cursor);
        }
    }

    string GetSortKeyDisplay()
    {
        switch (_runtimeSortKey)
        {
            case SortKey.OldestToLatest: return "Sort key: TIME [early to late]";
            case SortKey.LatestToOldest: return "Sort key: TIME [late to early]";
            case SortKey.LargestToSmallest: return "Sort key: SIZE [largest to smallest]";
            case SortKey.SmallestToLargest: return "Sort key: SIZE [smallest to largest]";
            default: return $"Sort key: {_runtimeSortKey}";
        }
    }

    void RefreshUI()
    {
        if (passMarkerText) passMarkerText.text = GetSortKeyDisplay();

        if (hudText) hudText.gameObject.SetActive(showHud);
        if (hudText && showHud) hudText.text = $"Pass {passIndex + 1}";

        if (boxAText) boxAText.text = (passIndex + 1).ToString();
        if (boxBText) boxBText.text = pickB.HasValue ? (pickB.Value + 1).ToString() : "-";

        bool canChoose = !inputLocked && cursorIndex.HasValue && cursorIndex.Value >= passIndex;
        if (chooseButton) chooseButton.interactable = !finished && !isSwapping && canChoose;

        bool canConfirm = !inputLocked && (pickB.HasValue || FindBestIndex(passIndex) == passIndex);
        if (switchButton) switchButton.interactable = !finished && !isSwapping && canConfirm;
    }

    void AutoCompleteIfSingleLeft()
    {
        if (order == null || order.Count == 0) return;
        int last = order.Count - 1;
        if (passIndex >= last)
        {
            if (last >= 0 && last < cards.Count) cards[last].SetLocked(true);
            Finish();
        }
    }

    void Finish()
    {
        if (finished) return;
        finished = true;

        RefreshUI();

        if (switchButton) switchButton.interactable = false;
        if (chooseButton) chooseButton.interactable = false;
        if (boxAText) boxAText.text = "-";
        if (boxBText) boxBText.text = "-";

        if (hintText) hintText.text = "✔ All data sorted!";

        OnFinished?.Invoke();

        StartCoroutine(WinThenReturnRoutine());
    }

    private IEnumerator WinThenReturnRoutine()
    {
        SetInputLocked(true);

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

        yield return new WaitForSeconds(Mathf.Max(0f, winHoldSeconds));

        if (finishRoot != null && finishText != null)
            yield return StartCoroutine(FinishOverlayRoutine());

        if (HubMinigameReturnContext.hasData && !string.IsNullOrWhiteSpace(HubMinigameReturnContext.returnSceneName))
        {
            HubMinigameReturnContext.ReturnToWorld();
            yield break;
        }
        else if (MinigameReturnContext.hasData && !string.IsNullOrWhiteSpace(MinigameReturnContext.returnSceneName))
        {
            MinigameReturnContext.ReturnToWorld();
        }
        else
        {
            Debug.LogWarning("[GalleryGame] No return context set. Story uses MinigameReturnContext; Hub menu uses HubMinigameReturnContext.");
            if (hintText) hintText.text = "No return context set. Enter minigame via hub menu or story trigger.";
            SetInputLocked(false);
        }
    }

    private void SetInputLocked(bool locked)
    {
        inputLocked = locked;
        RefreshUI();
    }

    private IEnumerator StartCountdownGateRoutine()
    {
        SetInputLocked(true);

        if (countdownRoot == null || countdownText == null)
        {
            SetInputLocked(false);
            yield break;
        }

        countdownRoot.SetActive(true);
        ForceEnableTree(countdownRoot);

        var textGO = countdownText.gameObject;

        var cg = textGO.GetComponent<CanvasGroup>();
        if (cg == null) cg = textGO.AddComponent<CanvasGroup>();

        var rt = countdownText.rectTransform;

        float originalFontSize = countdownText.fontSize;
        bool originalAutoSize = countdownText.enableAutoSizing;
        TextAlignmentOptions originalAlign = countdownText.alignment;

        if (forceCenterAlignForCountdown)
            countdownText.alignment = TextAlignmentOptions.Center;

        countdownText.enableAutoSizing = false;

        float mult = Mathf.Max(0.01f, countdownFontSizeMultiplier);
        countdownText.fontSize = originalFontSize * mult;

        Vector3 baseScale = (rt != null) ? rt.localScale : Vector3.one;

        for (int n = 3; n >= 1; n--)
        {
            countdownText.text = n.ToString();
            yield return AnimateOverlayInOut(cg, rt, baseScale, countdownStepSeconds);
        }

        countdownText.text = string.IsNullOrWhiteSpace(countdownStartText) ? "START" : countdownStartText;
        yield return AnimateOverlayInOut(cg, rt, baseScale, Mathf.Max(0.05f, countdownStartHoldSeconds));

        countdownText.fontSize = originalFontSize;
        countdownText.enableAutoSizing = originalAutoSize;
        countdownText.alignment = originalAlign;

        countdownRoot.SetActive(false);
        SetInputLocked(false);
    }

    private IEnumerator FinishOverlayRoutine()
    {
        if (finishRoot == null || finishText == null) yield break;

        finishRoot.SetActive(true);
        ForceEnableTree(finishRoot);

        var textGO = finishText.gameObject;

        var cg = textGO.GetComponent<CanvasGroup>();
        if (cg == null) cg = textGO.AddComponent<CanvasGroup>();

        var rt = finishText.rectTransform;

        float originalFontSize = finishText.fontSize;
        bool originalAutoSize = finishText.enableAutoSizing;
        TextAlignmentOptions originalAlign = finishText.alignment;

        if (forceCenterAlignForCountdown)
            finishText.alignment = TextAlignmentOptions.Center;

        finishText.enableAutoSizing = false;

        float mult = Mathf.Max(0.01f, finishFontSizeMultiplier);
        finishText.fontSize = originalFontSize * mult;

        Vector3 baseScale = (rt != null) ? rt.localScale : Vector3.one;

        finishText.text = string.IsNullOrWhiteSpace(finishMessage) ? "FINISHED!" : finishMessage;

        yield return AnimateOverlayInOut(cg, rt, baseScale, Mathf.Max(0.05f, finishHoldSeconds));

        finishText.fontSize = originalFontSize;
        finishText.enableAutoSizing = originalAutoSize;
        finishText.alignment = originalAlign;

        finishRoot.SetActive(false);
    }

    private IEnumerator AnimateOverlayInOut(CanvasGroup cg, RectTransform rt, Vector3 baseScale, float holdSeconds)
    {
        float inDur = 0.18f;
        float outDur = 0.18f;

        yield return AnimateOverlay(cg, rt, baseScale, 0f, 1f, countdownScaleMin, countdownScaleMax, inDur);
        yield return new WaitForSeconds(Mathf.Max(0.01f, holdSeconds));
        yield return AnimateOverlay(cg, rt, baseScale, 1f, 0f, countdownScaleMax, countdownScaleMin, outDur);
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
            float e = countdownEase != null ? countdownEase.Evaluate(u) : u;

            if (cg != null) cg.alpha = Mathf.LerpUnclamped(a0, a1, e);
            if (rt != null) rt.localScale = baseScale * Mathf.LerpUnclamped(s0, s1, e);

            yield return null;
        }

        if (cg != null) cg.alpha = a1;
        if (rt != null) rt.localScale = baseScale * s1;
    }

    static double ToBytes(float value, FileSizeUnit unit)
    {
        switch (unit)
        {
            case FileSizeUnit.Bit: return value / 8.0;
            case FileSizeUnit.Byte: return value;
            case FileSizeUnit.KB: return value * 1_000.0;
            case FileSizeUnit.MB: return value * 1_000_000.0;
            case FileSizeUnit.GB: return value * 1_000_000_000.0;
            case FileSizeUnit.TB: return value * 1_000_000_000_000.0;
            default: return value;
        }
    }

    static string SecondsToHMS(int seconds)
    {
        seconds = Mathf.Clamp(seconds, 0, 24 * 60 * 60 - 1);
        int h = seconds / 3600;
        int m = (seconds % 3600) / 60;
        int s = seconds % 60;
        return $"{h:00}:{m:00}:{s:00}";
    }

    static int ParseIsoToSeconds(string iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return 0;

        int t = iso.IndexOf('T');
        if (t < 0 || t + 1 >= iso.Length) return 0;

        string time = iso.Substring(t + 1);

        int z = time.IndexOf('Z');
        if (z >= 0) time = time.Substring(0, z);

        int plus = time.IndexOf('+');
        if (plus >= 0) time = time.Substring(0, plus);

        var parts = time.Split(':');
        if (parts.Length < 2) return 0;

        int h = 0, m = 0, s = 0;
        int.TryParse(parts[0], out h);
        int.TryParse(parts[1], out m);
        if (parts.Length >= 3) int.TryParse(parts[2], out s);

        h = Mathf.Clamp(h, 0, 23);
        m = Mathf.Clamp(m, 0, 59);
        s = Mathf.Clamp(s, 0, 59);

        return h * 3600 + m * 60 + s;
    }

    static void Shuffle<T>(IList<T> list)
    {
        var rng = new System.Random();
        for (int n = list.Count - 1; n > 0; n--)
        {
            int k = rng.Next(n + 1);
            (list[n], list[k]) = (list[k], list[n]);
        }
    }
}