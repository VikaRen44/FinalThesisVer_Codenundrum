using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public class UIManager_Intro : MonoBehaviour
{
    [Header("Refs")]
    public HeapManager heapManager;
    public Transform numbersContainer;
    public GameObject numberButtonPrefab;
    public TextMeshProUGUI statusText;
    public CanvasGroup continuePrompt;

    [Header("Screen Fade")]
    public Image fadeOverlay;
    public float fadeDuration = 1.8f;

    [Header("Instruction UI")]
    public GameObject instructionPanel;

    [Header("Sort Key UI")]
    [Tooltip("Displays the current sorting rule/content type.")]
    public TextMeshProUGUI sortKeyText;

    [Header("Heap Generation")]
    public int nodeCount = 7;
    public int minRandomValue = 1;
    public int maxRandomValue = 999;

    [Header("Runtime Content Randomization")]
    public bool randomizeContentEachPlay = true;

    [Header("Swap Animation")]
    public float swapAnimDuration = 0.28f;

    [Header("Completion UI")]
    public GameObject completionPanel;

    [Header("Timer")]
    public GameTimer gameTimer;

    [Header("Timer UI")]
    public TextMeshProUGUI timerText;

    [Header("Proceed UI")]
    public GameObject proceedPanel;

    [Header("Game Over UI")]
    public CanvasGroup timeUpPanel;
    public float timeUpFadeDuration = 0.4f;

    [Header("Focus Glow Panel")]
    public RectTransform focusGlowPanel;
    public float glowPadding = 120f;

    [Header("Flash Feedback")]
    public Image winFlash;
    public float winFlashDuration = 0.6f;

    [Header("Node Colors")]
    public Color defaultNodeColor = Color.white;
    public Color selectedNodeColor = new Color(1f, 1f, 0.5f);
    public Color completedNodeColor = new Color(0.6f, 1f, 0.6f);

    [Header("Heap Completion Glow")]
    public Color completionGlowColor = new Color(1f, 0.9f, 0.35f);
    public float completionGlowDuration = 0.5f;
    public float completionGlowScale = 1.2f;

    [Tooltip("Color shown when player selects the wrong child.")]
    public Color wrongNodeColor = new Color(1f, 0.3f, 0.3f);

    [Tooltip("Color shown briefly when the correct node is selected.")]
    public Color correctNodeColor = new Color(0.3f, 1f, 0.3f);

    public float correctFlashDuration = 0.35f;

    [Header("Heap Focus Colors")]
    public Color parentNodeColor = new Color(1f,0.75f,0.3f);
    public Color childNodeColor = new Color(0.6f,1f,0.6f);

    [Header("Focus Animation")]
    public float focusScale = 1.15f;
    public float focusSpeed = 8f;

    [Header("Wrong Selection Feedback")]
    public float shakeDuration = 0.5f;
    public float shakeStrength = 10f;

    public bool useEaseInOut = true;
    public bool blockInputDuringSwap = true;

    private bool _isSwapping = false;
    private bool _isFeedbackPlaying = false;

    private Coroutine statusRoutine;
    private Coroutine delayedStatusRoutine;
    private string baseInstructionText = "";
    private Color originalStatusColor;

    private int currentParent;
    private int heapSize;

    private bool heapIsComplete = false;

    private readonly List<Button> _nodeButtons = new();
    private readonly List<Image> _nodeImages = new();
    private readonly List<TextMeshProUGUI> _nodeTexts = new();
    private readonly List<RectTransform> _nodeRects = new();
    private readonly List<int> _uiIndexAtHeapIndex = new();

    private enum DisplayContentType
    {
        PlainNumber,
        WeightKg,
        StorageHours
    }

    private DisplayContentType currentContentType = DisplayContentType.PlainNumber;

    public void ShowInstructions()
    {
        if (instructionPanel != null)
            instructionPanel.SetActive(true);
    }

    public void HideInstructions()
    {
        if (instructionPanel != null)
            instructionPanel.SetActive(false);
    }

    void Start()
    {
    
        if (statusText != null)
            originalStatusColor = statusText.color;

        if (timeUpPanel != null)
        {
            timeUpPanel.alpha = 0f;
            timeUpPanel.interactable = false;
            timeUpPanel.blocksRaycasts = false;
            timeUpPanel.gameObject.SetActive(true);
        }

        // Start screen fully black so the fade-in can play when player presses Play
        if (fadeOverlay != null)
            fadeOverlay.color = new Color(0f, 0f, 0f, 1f);

        // Check if tutorial was already shown
        if (PlayerPrefs.GetInt("IntroHeapTutorialShown", 0) == 0)
        {
            ShowInstructions();
        }
        else
        {
            // Skip tutorial and start game immediately
            HideInstructions();
        }

        // Reset state
        heapIsComplete = false;
    }

    public void OnPressPlay()
    {

        PlayerPrefs.SetInt("IntroHeapTutorialShown", 1);
        PlayerPrefs.Save();

        HideInstructions();

        StartIntroGame();
    }

    void Update()
    {
        // smooth reset of scale
        foreach (RectTransform rect in _nodeRects)
        {
            rect.localScale = Vector3.Lerp(rect.localScale, Vector3.one, Time.deltaTime * focusSpeed);
        }
    }

    void SelectRandomContentType()
    {
        if (!randomizeContentEachPlay)
        {
            currentContentType = DisplayContentType.PlainNumber;
            return;
        }

        int pick = Random.Range(0, 3);
        currentContentType = (DisplayContentType)pick;
    }

    void BuildUIOnce(int count)
    {
        foreach (Transform child in numbersContainer)
            Destroy(child.gameObject);

        _nodeButtons.Clear();
        _nodeImages.Clear();
        _nodeTexts.Clear();
        _nodeRects.Clear();
        _uiIndexAtHeapIndex.Clear();

        for (int i = 0; i < count; i++)
        {
            GameObject btnGO = Instantiate(numberButtonPrefab, numbersContainer);

            Button b = btnGO.GetComponent<Button>();
            Image img = btnGO.GetComponent<Image>();
            TextMeshProUGUI txt = btnGO.GetComponentInChildren<TextMeshProUGUI>();
            RectTransform rect = btnGO.GetComponent<RectTransform>();

            _nodeButtons.Add(b);
            _nodeImages.Add(img);
            _nodeTexts.Add(txt);
            _nodeRects.Add(rect);

            _uiIndexAtHeapIndex.Add(i);
        }
    }

    Vector2 GetNodePosition(int heapIndex)
    {
        float verticalSpacing = 120f;
        float baseHorizontalSpacing = 500f;

        int level = Mathf.FloorToInt(Mathf.Log(heapIndex + 1, 2));
        int levelStartIndex = (int)Mathf.Pow(2, level) - 1;
        int indexInLevel = heapIndex - levelStartIndex;
        int nodesInLevel = (int)Mathf.Pow(2, level);

        float horizontalSpacing = baseHorizontalSpacing / (level + 1);
        float xPos = (indexInLevel - (nodesInLevel - 1) / 2f) * horizontalSpacing;
        float yPos = -level * verticalSpacing;

        return new Vector2(xPos, yPos);
    }

    void UpdateFocusGlow(int parent, int left, int right)
    {
        if (focusGlowPanel == null)
            return;

        List<RectTransform> targets = new List<RectTransform>();

        targets.Add(_nodeRects[_uiIndexAtHeapIndex[parent]]);

        if (left < heapSize)
            targets.Add(_nodeRects[_uiIndexAtHeapIndex[left]]);

        if (right < heapSize)
            targets.Add(_nodeRects[_uiIndexAtHeapIndex[right]]);

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;

        foreach (RectTransform rect in targets)
        {
            Vector2 pos = rect.anchoredPosition;
            Vector2 size = rect.sizeDelta * rect.localScale;

            float leftEdge = pos.x - size.x / 4f;
            float rightEdge = pos.x + size.x / 4f;
            float bottomEdge = pos.y - size.y / 4f;
            float topEdge = pos.y + size.y / 4f;

            minX = Mathf.Min(minX, leftEdge);
            maxX = Mathf.Max(maxX, rightEdge);
            minY = Mathf.Min(minY, bottomEdge);
            maxY = Mathf.Max(maxY, topEdge);
        }

        float width = (maxX - minX) + glowPadding;
        float height = (maxY - minY) + glowPadding;

        Vector2 center = new Vector2((minX + maxX) / 2f, (minY + maxY) / 2f);

        center.y += 170f;
        center.x -= 15f;
        
        focusGlowPanel.anchoredPosition = center;
        focusGlowPanel.sizeDelta = new Vector2(width, height);
    }

    public void RefreshUI()
    {
        for (int heapIndex = 0; heapIndex < heapSize; heapIndex++)
        {
            int uiIndex = _uiIndexAtHeapIndex[heapIndex];

            _nodeRects[uiIndex].anchoredPosition = GetNodePosition(heapIndex);
            _nodeTexts[uiIndex].text = FormatDisplayValue(heapManager.heap[heapIndex]);
        }

        ApplyHighlightAndClicks();
        UpdateSortKeyText();
        DelayedUpdateStatus(1f);
    }

    string FormatDisplayValue(int value)
    {
        switch (currentContentType)
        {
            case DisplayContentType.WeightKg:
                return value + " kg";
            case DisplayContentType.StorageHours:
                return value + " hr";
            default:
                return value.ToString();
        }
    }

    void UpdateSortKeyText()
    {
        if (sortKeyText == null)
            return;

        switch (currentContentType)
        {
            case DisplayContentType.WeightKg:
                sortKeyText.text = "Sort key: WEIGHT [lighter values on top]";
                break;
            case DisplayContentType.StorageHours:
                sortKeyText.text = "Sort key: STORAGE TIME [shorter time on top]";
                break;
            default:
                sortKeyText.text = "Sort key: VALUE [smaller numbers on top]";
                break;
        }
    }

    void ApplyHighlightAndClicks()
    {
        for (int ui = 0; ui < _nodeButtons.Count; ui++)
        {
            _nodeButtons[ui].onClick.RemoveAllListeners();
            _nodeButtons[ui].interactable = false;

            if (_nodeImages[ui] != null)
                _nodeImages[ui].color = defaultNodeColor;
        }

        if (heapIsComplete) return;
        if (_isSwapping) return;

        int left = 2 * currentParent + 1;
        int right = 2 * currentParent + 2;

        UpdateFocusGlow(currentParent, left, right);

        int smallest = currentParent;

        if (left < heapSize && heapManager.heap[left] < heapManager.heap[smallest])
            smallest = left;

        if (right < heapSize && heapManager.heap[right] < heapManager.heap[smallest])
            smallest = right;

        int parentUI = _uiIndexAtHeapIndex[currentParent];

        _nodeImages[parentUI].color = parentNodeColor;
        _nodeImages[parentUI].canvasRenderer.SetAlpha(1f);
        FocusNode(currentParent);

        if (left < heapSize)
        {
            int ui = _uiIndexAtHeapIndex[left];
            _nodeImages[ui].color = childNodeColor;
            _nodeButtons[ui].interactable = true;
            _nodeButtons[ui].onClick.AddListener(() => OnNodeClicked(left));
            FocusNode(left);
        }

        if (right < heapSize)
        {
            int ui = _uiIndexAtHeapIndex[right];
            _nodeImages[ui].color = childNodeColor;
            _nodeButtons[ui].interactable = true;
            _nodeButtons[ui].onClick.AddListener(() => OnNodeClicked(right));
            FocusNode(right);
        }

        _nodeButtons[parentUI].interactable = true;
        _nodeButtons[parentUI].onClick.AddListener(() => OnNodeClicked(currentParent));

    }

    void FocusNode(int heapIndex)
    {
        int ui = _uiIndexAtHeapIndex[heapIndex];
        _nodeRects[ui].localScale = Vector3.Lerp(
            _nodeRects[ui].localScale,
            Vector3.one * focusScale,
            Time.deltaTime * focusSpeed
        );
    }

    void OnNodeClicked(int index)
    {
        
        if (_isSwapping || _isFeedbackPlaying)
        return;

        int left = 2 * currentParent + 1;
        int right = 2 * currentParent + 2;

        int smallest = currentParent;

        if (left < heapSize && heapManager.heap[left] < heapManager.heap[smallest])
            smallest = left;

        if (right < heapSize && heapManager.heap[right] < heapManager.heap[smallest])
            smallest = right;

        if (smallest == currentParent)
        {
            if (index == currentParent)
            {
                StartCoroutine(CorrectParent(index));
            }
            else
            {
                int uiIndex = _uiIndexAtHeapIndex[index];
                StartCoroutine(ShakeNode(_nodeRects[uiIndex], uiIndex));
                ShowStatus("This box is already correct. Select the parent box.");
            }

            return;
        }

        if (index == smallest)
        {
            StartCoroutine(CorrectChildThenSwap(index, currentParent));
        }
        else if (index == currentParent)
        {
            int uiIndex = _uiIndexAtHeapIndex[index];
            StartCoroutine(ShakeNode(_nodeRects[uiIndex], uiIndex));
            ShowStatus("This box is heavier. Choose the smaller box below.");
        }
        else
        {
            int uiIndex = _uiIndexAtHeapIndex[index];
            StartCoroutine(ShakeNode(_nodeRects[uiIndex], uiIndex));
            ShowStatus("Look carefully. Which box should go above?");
        }
    }

    IEnumerator StartGameSequence()
    {

        HideInstructions();

        SelectRandomContentType();

        int maxExclusive = Mathf.Max(minRandomValue + 1, maxRandomValue + 1);
        heapManager.GenerateRandomHeap(nodeCount, minRandomValue, maxExclusive);

        heapSize = heapManager.heap.Count;
        currentParent = heapSize / 2 - 1;

        BuildUIOnce(heapSize);
        RefreshUI();
        UpdateStatus();
        UpdateSortKeyText();

        yield return StartCoroutine(FadeFromBlack());
    }

    IEnumerator FadeFromBlack()
    {
        if (fadeOverlay == null)
            yield break;

        float t = 0f;
        Color c = fadeOverlay.color;

        while (t < fadeDuration)
        {
            t += Time.deltaTime;

            float alpha = Mathf.Lerp(1f, 0f, t / fadeDuration);
            fadeOverlay.color = new Color(c.r, c.g, c.b, alpha);

            yield return null;
        }

        fadeOverlay.color = new Color(c.r, c.g, c.b, 0f);
    }

    IEnumerator StartTimerNextFrame()
    {
        yield return null; // wait one frame to ensure UI is ready

        if (gameTimer != null)
            gameTimer.StartTimer();
    }

    IEnumerator CorrectParent(int parentIndex)
    {
        int uiIndex = _uiIndexAtHeapIndex[parentIndex];

        ShowStatus("Good. This box is already in the correct place.", 2f);

        yield return StartCoroutine(FlashCorrectNode(uiIndex));

        currentParent--;

        if (currentParent < 0)
        {
            heapIsComplete = true;
            CompleteIntroHeap();
            yield break;
        }

        RefreshUI();
    }

    IEnumerator CorrectChildThenSwap(int childIndex, int parentIndex)
    {
        _isFeedbackPlaying = true;

        int uiIndex = _uiIndexAtHeapIndex[childIndex];

        ShowStatus("Correct!", 2f);

        yield return StartCoroutine(FlashCorrectNode(uiIndex));

        _isFeedbackPlaying = false;

        yield return StartCoroutine(SwapIndicesAnimated(parentIndex, childIndex));
    }

    IEnumerator ShakeNode(RectTransform rect, int uiIndex)
    {

        _isFeedbackPlaying = true;

        Vector2 start = rect.anchoredPosition;
        float elapsed = 0f;

        if (_nodeImages[uiIndex] != null)
            _nodeImages[uiIndex].color = wrongNodeColor;

        while (elapsed < shakeDuration)
        {
            elapsed += Time.deltaTime;

            rect.anchoredPosition = start + new Vector2(
                Random.Range(-shakeStrength, shakeStrength),
                Random.Range(-shakeStrength, shakeStrength)
            );

            yield return null;
        }

        rect.anchoredPosition = start;

        _isFeedbackPlaying = false;

        RefreshUI();
    }

    IEnumerator FlashCorrectNode(int uiIndex)
    {
        _isFeedbackPlaying = true;

        Image img = _nodeImages[uiIndex];
        RectTransform rect = _nodeRects[uiIndex];

        Vector3 startScale = rect.localScale;
        Vector3 pulseScale = startScale * 1.2f;

        if (img != null)
            img.color = correctNodeColor;

        float t = 0f;
        float duration = correctFlashDuration;

        while (t < duration)
        {
            t += Time.deltaTime;
            float normalized = t / duration;

            float pulse = Mathf.Sin(normalized * Mathf.PI);

            rect.localScale = Vector3.Lerp(startScale, pulseScale, pulse);

            yield return null;
        }

        rect.localScale = startScale;

        _isFeedbackPlaying = false;
    }

    IEnumerator CorrectThenSwap(int selectedIndex, int parentIndex)
    {
        int uiIndex = _uiIndexAtHeapIndex[selectedIndex];

        yield return StartCoroutine(FlashCorrectNode(uiIndex));

        yield return StartCoroutine(SwapIndicesAnimated(parentIndex, selectedIndex));
    }

    IEnumerator SwapIndicesAnimated(int a, int b)
    {
        _isSwapping = true;

        int uiA = _uiIndexAtHeapIndex[a];
        int uiB = _uiIndexAtHeapIndex[b];

        RectTransform rectA = _nodeRects[uiA];
        RectTransform rectB = _nodeRects[uiB];

        Vector2 startA = rectA.anchoredPosition;
        Vector2 startB = rectB.anchoredPosition;

        Vector2 targetA = GetNodePosition(b);
        Vector2 targetB = GetNodePosition(a);

        float dur = Mathf.Max(0.01f, swapAnimDuration);
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            float eased = useEaseInOut ? EaseInOut(t) : t;

            rectA.anchoredPosition = Vector2.Lerp(startA, targetA, eased);
            rectB.anchoredPosition = Vector2.Lerp(startB, targetB, eased);

            yield return null;
        }

        rectA.anchoredPosition = targetA;
        rectB.anchoredPosition = targetB;

        int temp = heapManager.heap[a];
        heapManager.heap[a] = heapManager.heap[b];
        heapManager.heap[b] = temp;

        int tempUI = _uiIndexAtHeapIndex[a];
        _uiIndexAtHeapIndex[a] = _uiIndexAtHeapIndex[b];
        _uiIndexAtHeapIndex[b] = tempUI;

        currentParent = b;

        _isSwapping = false;

        RefreshUI();
    }

    IEnumerator PlayHeapCompletionGlow()
    {
        _isFeedbackPlaying = true;

        // Restore visibility of all nodes before glow animation
        for (int i = 0; i < heapSize; i++)
        {
            int ui = _uiIndexAtHeapIndex[i];

            if (_nodeImages[ui] != null)
                _nodeImages[ui].canvasRenderer.SetAlpha(1f);
                yield return new WaitForSeconds(0.05f);
        }

        List<Color> startColors = new List<Color>();
        List<Vector3> startScales = new List<Vector3>();

        for (int i = 0; i < heapSize; i++)
        {
            int ui = _uiIndexAtHeapIndex[i];

            startColors.Add(_nodeImages[ui].color);
            startScales.Add(_nodeRects[ui].localScale);
        }

        float fadeDuration = 0.15f;
        float t = 0f;

        // Fade in glow
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float progress = t / fadeDuration;

            for (int i = 0; i < heapSize; i++)
            {
                int ui = _uiIndexAtHeapIndex[i];

                _nodeImages[ui].color = Color.Lerp(startColors[i], completionGlowColor, progress);

                _nodeRects[ui].localScale =
                    Vector3.Lerp(startScales[i], startScales[i] * completionGlowScale, progress);
            }

            yield return null;
        }

        yield return new WaitForSeconds(completionGlowDuration);

        // Return to normal scale but keep completed color
        for (int i = 0; i < heapSize; i++)
        {
            int ui = _uiIndexAtHeapIndex[i];

            _nodeRects[ui].localScale = startScales[i];
            _nodeImages[ui].color = completedNodeColor;
        }

        _isFeedbackPlaying = false;
    }

    IEnumerator PlayWinFlash()
    {
        if (winFlash == null)
            yield break;

        winFlash.gameObject.SetActive(true);

        float t = 0f;
        Color c = winFlash.color;

        while (t < winFlashDuration)
        {
            t += Time.deltaTime;

            float alpha = Mathf.Sin((t / winFlashDuration) * Mathf.PI);
            winFlash.color = new Color(c.r, c.g, c.b, alpha * 0.6f);

            yield return null;
        }

        winFlash.color = new Color(c.r, c.g, c.b, 0f);
        winFlash.gameObject.SetActive(false);
    }

    IEnumerator FlashStatusGold(float duration = 1.2f)
    {
        if (statusText == null)
            yield break;

        Color gold = new Color(1f, 0.85f, 0.2f);

        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;

            float pulse = Mathf.Sin((t / duration) * Mathf.PI);

            statusText.color = Color.Lerp(originalStatusColor, gold, pulse);

            yield return null;
        }

        statusText.color = originalStatusColor;
    }

    public IEnumerator FadeInTimeUpPanel()
    {
        if (timeUpPanel == null)
            yield break;

        float t = 0f;

        while (t < timeUpFadeDuration)
        {
            t += Time.deltaTime;

            float alpha = Mathf.Lerp(0f, 1f, t / timeUpFadeDuration);

            timeUpPanel.alpha = alpha;

            yield return null;
        }

        timeUpPanel.alpha = 1f;
        timeUpPanel.interactable = true;
        timeUpPanel.blocksRaycasts = true;
    }

    public void StartIntroGame()
    {
        StartCoroutine(StartGameSequence());

        if (gameTimer != null)
            gameTimer.StartTimer();
    }

    float EaseInOut(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    void CompleteIntroHeap()
    {

        if (focusGlowPanel != null)
            focusGlowPanel.gameObject.SetActive(false);

        if (gameTimer != null)
        gameTimer.StopTimer();

        // Show congratulatory message
        ShowStatus("Great work! The boxes are now perfectly stacked.", 3f);
        StartCoroutine(FlashStatusGold());

        StartCoroutine(HeapWinSequence());
    }

    IEnumerator ShowCompletionThenProceed()
    {

        yield return new WaitForSeconds(.5f);

        if (completionPanel != null)
            completionPanel.SetActive(true);

        while (Keyboard.current == null || !Keyboard.current.eKey.wasPressedThisFrame)
            yield return null;

        if (completionPanel != null)
            completionPanel.SetActive(false);

        if (proceedPanel != null)
            proceedPanel.SetActive(true);
    }

    IEnumerator HeapWinSequence()
    {

        yield return StartCoroutine(PlayHeapCompletionGlow());

        yield return StartCoroutine(PlayWinFlash());

        StartCoroutine(ShowCompletionThenProceed());
    }

    void UpdateStatus()
    {
        if (statusText == null)
            return;

        if (heapIsComplete)
        {
            baseInstructionText = "All boxes are safely stacked!";
            statusText.text = baseInstructionText;
            return;
        }

        if (currentParent < 0 || currentParent >= heapManager.heap.Count)
            return;

        int parentValue = heapManager.heap[currentParent];
        string parentDisplay = FormatDisplayValue(parentValue);

        int left = 2 * currentParent + 1;
        int right = 2 * currentParent + 2;

        bool leftExists = left < heapSize;
        bool rightExists = right < heapSize;

        switch (currentContentType)
        {
            case DisplayContentType.WeightKg:

                if (leftExists || rightExists)
                    baseInstructionText = "Which box should go above " + parentDisplay + "? Choose the lightest box.";
                else
                    baseInstructionText = "Check if " + parentDisplay + " is already in the correct place.";

                break;

            case DisplayContentType.StorageHours:

                if (leftExists || rightExists)
                    baseInstructionText = "Which box should go above " + parentDisplay + "? Choose the shortest storage time.";
                else
                    baseInstructionText = "Check if " + parentDisplay + " is already in the correct place.";

                break;

            default:

                if (leftExists || rightExists)
                    baseInstructionText = "Which box should go above " + parentDisplay + "? Choose the smallest number.";
                else
                    baseInstructionText = "Check if " + parentDisplay + " is already in the correct place.";

                break;
        }

        statusText.text = baseInstructionText;
    }

    void ShowStatus(string message, float duration = 2f)
    {
        if (statusText == null)
            return;

        if (statusRoutine != null)
            StopCoroutine(statusRoutine);

        statusRoutine = StartCoroutine(StatusRoutine(message, duration));
    }

    void DelayedUpdateStatus(float delay = 0.5f)
    {
        if (delayedStatusRoutine != null)
            StopCoroutine(delayedStatusRoutine);

        delayedStatusRoutine = StartCoroutine(DelayedStatusRoutine(delay));
    }

    IEnumerator DelayedStatusRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);

        UpdateStatus();
    }

    IEnumerator StatusRoutine(string message, float duration)
    {
        statusText.text = message;

        yield return new WaitForSeconds(duration);

        statusText.text = baseInstructionText;
    }

    public void OnAskFaith()
    {
        SceneManager.LoadScene("03_Faith_PostMinHeapDialogue");
    }


    public void OnRetry()
    {
        if (completionPanel != null)
            completionPanel.SetActive(false);

        if (proceedPanel != null)
            proceedPanel.SetActive(false);

        if (timeUpPanel != null)
        {
            timeUpPanel.alpha = 0f;
            timeUpPanel.interactable = false;
            timeUpPanel.blocksRaycasts = false;
        }

        heapIsComplete = false;

        StartIntroGame();
    }

    float EaseOutBack(float x)
    {
        float c1 = 1.70158f;
        float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    float EaseInBack(float x)
    {
        float c1 = 1.70158f;
        float c3 = c1 + 1f;
        return c3 * x * x * x - c1 * x * x;
    }
}