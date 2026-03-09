using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public class UIManager_TrueHeap : MonoBehaviour
{
    [Header("Refs")]
    public HeapManager heapManager;
    public Transform numbersContainer;
    public GameObject numberButtonPrefab;
    public TextMeshProUGUI statusText;

    [Header("Sort Key UI")]
    public TextMeshProUGUI sortKeyText;

    [Header("Completion UI")]
    public GameObject completionPanel;

    [Header("Timer")]
    public GameTimer gameTimer;

    [Header("Swap Animation")]
    [Tooltip("Seconds to animate a swap between two nodes.")]
    public float swapAnimDuration = 0.28f;

    [Header("Proceed UI")]
    public GameObject proceedPanel;

    [Header("Tutorial UI")]
    public GameObject tutorialPanel;
    public CanvasGroup continuePrompt;

    [Header("Lose UI")]
    public CanvasGroup timeUpPanel;

    [Header("Focus Glow")]
    public RectTransform focusGlowPanel;
    public float glowPadding = 120f;

    [Header("Node Colors")]
    [Tooltip("Default color for all boxes before highlights are applied.")]
    public Color defaultNodeColor = Color.white;

    [Header("Heap Completion Glow")]
    public Color completionGlowColor = new Color(1f, 0.9f, 0.35f);
    public float completionGlowDuration = 0.5f;
    public float completionGlowScale = 1.28f;

    [Header("Flash Feedback")]
    public Image winFlash;
    public float winFlashDuration = 0.6f;

    [Header("Feedback Colors")]
    public Color wrongNodeColor = new Color(1f, 0.3f, 0.3f);
    public Color correctNodeColor = new Color(0.3f, 1f, 0.3f);

    [Header("Feedback Timing")]
    public float correctFlashDuration = 0.35f;
    public float shakeDuration = 0.45f;
    public float shakeStrength = 8f;

    private bool _isFeedbackPlaying = false;

    [Tooltip("Color for the current parent box.")]
    public Color parentNodeColor = new Color(1f, 0.75f, 0.3f);

    [Tooltip("Color for child boxes being compared.")]
    public Color childNodeColor = new Color(0.6f, 1f, 0.6f);

    private bool tutorialActive = false;

    [Tooltip("If true, uses a soft ease in/out feel.")]
    public bool useEaseInOut = true;

    [Tooltip("If true, prevents clicks while nodes are animating.")]
    public bool blockInputDuringSwap = true;

    private int currentParent;
    private int heapSize;
    private bool heapCompleted = false;
    private bool _isSwapping = false;

    private Color originalStatusColor;

    private Coroutine statusRoutine;
    private Coroutine delayedStatusRoutine;
    private string baseInstructionText = "";

    // UI cache (IMPORTANT: we no longer destroy/recreate each refresh)
    private readonly List<Button> _nodeButtons = new List<Button>();
    private readonly List<Image> _nodeImages = new List<Image>();
    private readonly List<TextMeshProUGUI> _nodeTexts = new List<TextMeshProUGUI>();
    private readonly List<RectTransform> _nodeRects = new List<RectTransform>();

    private enum ScriptSortType
    {
        PageNumber,
        DialogueLines,
        SceneLength
    }

    private ScriptSortType currentSortType;

    // Mapping: heap index -> which UI object represents that node
    // We swap these references when animating to keep indices correct.
    private readonly List<int> _uiIndexAtHeapIndex = new List<int>();

    void Start()
    {

        if (statusText != null)
        originalStatusColor = statusText.color;

        if (completionPanel != null)
            completionPanel.SetActive(false);

        if (proceedPanel != null)
            proceedPanel.SetActive(false);

        SelectRandomSortType();
        heapManager.GenerateRandomHeap(7);
        AdjustValuesForSortKey();

        heapSize = heapManager.heap.Count;
        currentParent = heapSize / 2 - 1;

        BuildUIOnce(heapSize);
        RefreshUI();
        UpdateSortKeyText();
        UpdateStatus();

        if (timeUpPanel != null)
        {
            timeUpPanel.alpha = 0f;
            timeUpPanel.interactable = false;
            timeUpPanel.blocksRaycasts = false;
        }

    }

    string FormatScriptValue(int value)
    {
        switch (currentSortType)
        {
            case ScriptSortType.PageNumber:
                return "Page\n" + value;

            case ScriptSortType.DialogueLines:
                return "Dialogue\n" + value + " lines";

            case ScriptSortType.SceneLength:
                float minutes = value / 10f;
                return "Scene Length\n" + value + " min";

            default:
                return value.ToString();
        }
    }

    // =========================
    // UI BUILD ONCE (no more destroy/recreate)
    // =========================
    void BuildUIOnce(int count)
    {
        // If already built for the right size, do nothing
        if (_nodeButtons.Count == count && _uiIndexAtHeapIndex.Count == count)
            return;

        // Clear old children (only when rebuilding for different size)
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

            // ui index == heap index initially
            _uiIndexAtHeapIndex.Add(i);
        }
    }

    // Compute the anchored position for a given heap index
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

    void SelectRandomSortType()
    {
        int pick = Random.Range(0, 3);
        currentSortType = (ScriptSortType)pick;
    }

    void AdjustValuesForSortKey()
    {
        if (heapManager == null || heapManager.heap == null)
            return;

        for (int i = 0; i < heapManager.heap.Count; i++)
        {
            switch (currentSortType)
            {
                case ScriptSortType.PageNumber:
                    heapManager.heap[i] = Random.Range(1, 121); // pages 1–120
                    break;

                case ScriptSortType.DialogueLines:
                    heapManager.heap[i] = Random.Range(10, 81); // dialogue lines 10–80
                    break;

                case ScriptSortType.SceneLength:
                    heapManager.heap[i] = Random.Range(10, 61); // scene length 1–6 minutes
                    break;
            }
        }
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

        center.y += 220f;
        center.x += 110f;
        
        focusGlowPanel.anchoredPosition = center;
        focusGlowPanel.sizeDelta = new Vector2(width, height);
    }

    public void RefreshUI()
    {
        if (heapManager == null || heapManager.heap == null) return;

        // Update positions + labels based on mapping
        for (int heapIndex = 0; heapIndex < heapManager.heap.Count; heapIndex++)
        {
            int uiIndex = _uiIndexAtHeapIndex[heapIndex];

            // Ensure node sits at its heap index position
            _nodeRects[uiIndex].anchoredPosition = GetNodePosition(heapIndex);

            // Ensure text shows heap value at that index
            _nodeTexts[uiIndex].text = FormatScriptValue(heapManager.heap[heapIndex]);
        }

        // Apply colors + clickable logic
        ApplyHighlightAndClicks();
        DelayedUpdateStatus(1f);
    }

    void UpdateSortKeyText()
    {
        if (sortKeyText == null)
            return;

        switch (currentSortType)
        {
            case ScriptSortType.PageNumber:
                sortKeyText.text = "Sort key: PAGE NUMBER [later pages on top]";
                break;

            case ScriptSortType.DialogueLines:
                sortKeyText.text = "Sort key: DIALOGUE LINES [more dialogue on top]";
                break;

            case ScriptSortType.SceneLength:
                sortKeyText.text = "Sort key: SCENE LENGTH [longer scenes on top]";
                break;
        }
    }

    void ApplyHighlightAndClicks()
    {
        // Clear listeners each refresh
        for (int ui = 0; ui < _nodeButtons.Count; ui++)
        {
            _nodeButtons[ui].onClick.RemoveAllListeners();
            _nodeButtons[ui].interactable = false;

            if (_nodeImages[ui] != null)
                _nodeImages[ui].color = defaultNodeColor;
        }

        if (heapCompleted) return;
        if (blockInputDuringSwap && _isSwapping) return;

        int left = 2 * currentParent + 1;
        int right = 2 * currentParent + 2;

        UpdateFocusGlow(currentParent, left, right);

        // --------------------
        // Highlight parent
        // --------------------
        if (currentParent >= 0 && currentParent < heapSize)
        {
            int parentUI = _uiIndexAtHeapIndex[currentParent];

            _nodeImages[parentUI].color = parentNodeColor;

            _nodeButtons[parentUI].interactable = true;
            _nodeButtons[parentUI].onClick.AddListener(() => OnNodeClicked(currentParent));
        }

        // --------------------
        // Highlight left child
        // --------------------
        if (left < heapSize)
        {
            int leftUI = _uiIndexAtHeapIndex[left];

            _nodeImages[leftUI].color = childNodeColor;

            _nodeButtons[leftUI].interactable = true;
            _nodeButtons[leftUI].onClick.AddListener(() => OnNodeClicked(left));
        }

        // --------------------
        // Highlight right child
        // --------------------
        if (right < heapSize)
        {
            int rightUI = _uiIndexAtHeapIndex[right];

            _nodeImages[rightUI].color = childNodeColor;

            _nodeButtons[rightUI].interactable = true;
            _nodeButtons[rightUI].onClick.AddListener(() => OnNodeClicked(right));
        }
    }

    int GetLargestIndex(int parent, int left, int right)
    {
        int largest = parent;

        if (left < heapSize && heapManager.heap[left] > heapManager.heap[largest])
            largest = left;

        if (right < heapSize && heapManager.heap[right] > heapManager.heap[largest])
            largest = right;

        return largest;
    }

    void OnNodeClicked(int index)
    {
        if (_isFeedbackPlaying || _isSwapping)
            return;

        if (tutorialActive || heapCompleted)
            return;

        int left = 2 * currentParent + 1;
        int right = 2 * currentParent + 2;

        int largest = GetLargestIndex(currentParent, left, right);

        bool swapNeeded = (largest != currentParent);

        // --------------------
        // CASE 1: No swap needed
        // --------------------
        if (!swapNeeded)
        {
            if (index == currentParent)
            {
                StartCoroutine(CorrectParentNode(index));
            }
            else
            {
                int uiIndex = _uiIndexAtHeapIndex[index];

                statusText.text = "This script is already correct. Select the orange script.";

                StartCoroutine(ShakeNode(_nodeRects[uiIndex], uiIndex));
            }

            return;
        }

        // --------------------
        // CASE 2: Swap needed
        // --------------------

        // Correct node selected
        if (index == largest)
        {
            StartCoroutine(CorrectThenSwap(index, currentParent));
            return;
        }

        // Wrong parent selected
        if (index == currentParent)
        {
            int uiIndex = _uiIndexAtHeapIndex[index];

            statusText.text = "This script is larger. Choose the larger script below.";

            StartCoroutine(ShakeNode(_nodeRects[uiIndex], uiIndex));
            return;
        }

        // Wrong child selected
        {
            int uiIndex = _uiIndexAtHeapIndex[index];

            ShowStatus("Look carefully. Which script should move up?");

            StartCoroutine(ShakeNode(_nodeRects[uiIndex], uiIndex));
        }
    }

    IEnumerator SwapAndContinue(int a, int b)
    {
        _isSwapping = true;
        ApplyHighlightAndClicks();

        // UI indices for those heap indices
        int uiA = _uiIndexAtHeapIndex[a];
        int uiB = _uiIndexAtHeapIndex[b];

        RectTransform rectA = _nodeRects[uiA];
        RectTransform rectB = _nodeRects[uiB];

        Vector2 startA = rectA.anchoredPosition;
        Vector2 startB = rectB.anchoredPosition;

        Vector2 targetA = GetNodePosition(b);
        Vector2 targetB = GetNodePosition(a);

        float t = 0f;
        float dur = Mathf.Max(0.01f, swapAnimDuration);

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

        // Swap heap VALUES (this is the actual algorithm swap)
        int temp = heapManager.heap[a];
        heapManager.heap[a] = heapManager.heap[b];
        heapManager.heap[b] = temp;

        // Swap UI mapping so future highlights/clicks follow the heap indices
        int tempUI = _uiIndexAtHeapIndex[a];
        _uiIndexAtHeapIndex[a] = _uiIndexAtHeapIndex[b];
        _uiIndexAtHeapIndex[b] = tempUI;

        // Continue heapify down
        currentParent = b;

        statusText.text =
        "Correct. The script with the larger value moves up. Now check the replaced script again.";

        _isSwapping = false;
        RefreshUI();
    }

    IEnumerator CorrectThenSwap(int selectedIndex, int parentIndex)
    {
        int uiIndex = _uiIndexAtHeapIndex[selectedIndex];

        ShowStatus("Correct!", 2f);
        StartCoroutine(FlashStatusGold());

        yield return StartCoroutine(FlashCorrectNode(uiIndex));

        yield return StartCoroutine(SwapAndContinue(parentIndex, selectedIndex));
    }

    IEnumerator CorrectParentNode(int parentIndex)
    {
        int uiIndex = _uiIndexAtHeapIndex[parentIndex];

        ShowStatus("Good. This page is already in the correct place.", 2f);
        StartCoroutine(FlashStatusGold());

        yield return StartCoroutine(FlashCorrectNode(uiIndex));

        currentParent--;

        if (currentParent < 0)
        {
            CompleteHeap();
            yield break;
        }

        RefreshUI();
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

            winFlash.color = new Color(c.r, c.g, c.b, alpha * 0.8f);

            yield return null;
        }

        winFlash.color = new Color(c.r, c.g, c.b, 0f);
        winFlash.gameObject.SetActive(false);
    }

    float EaseInOut(float x)
    {
        x = Mathf.Clamp01(x);
        // smoothstep
        return x * x * (3f - 2f * x);
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

    IEnumerator FlashCorrectNode(int uiIndex)
    {
        _isFeedbackPlaying = true;

        Image img = _nodeImages[uiIndex];
        RectTransform rect = _nodeRects[uiIndex];

        Vector3 startScale = rect.localScale;
        Vector3 pulseScale = startScale * 1.15f;

        if (img != null)
            img.color = correctNodeColor;

        float t = 0f;

        while (t < correctFlashDuration)
        {
            t += Time.deltaTime;
            float normalized = t / correctFlashDuration;

            float pulse = Mathf.Sin(normalized * Mathf.PI);

            rect.localScale = Vector3.Lerp(startScale, pulseScale, pulse);

            yield return null;
        }

        rect.localScale = startScale;

        _isFeedbackPlaying = false;
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

    void CompleteHeap()
    {
        heapCompleted = true;

        if (focusGlowPanel != null)
            focusGlowPanel.gameObject.SetActive(false);

        if (gameTimer != null)
            gameTimer.StopTimer();

        statusText.text = "All scripts are organized. Give them to Faith.";
        
        //gold flash feedback
        StartCoroutine(FlashStatusGold());
        StartCoroutine(HeapWinSequence());

        if (completionPanel != null)
        {
            completionPanel.SetActive(true);

            CanvasGroup cg = completionPanel.GetComponent<CanvasGroup>();
            if (cg != null)
                cg.alpha = 1f;
        }

        if (proceedPanel != null)
            proceedPanel.SetActive(false);

        ApplyHighlightAndClicks();
    }

    IEnumerator ShowProceedAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (completionPanel != null)
            completionPanel.SetActive(false);

        if (proceedPanel != null)
            proceedPanel.SetActive(true);
    }

    IEnumerator PlayHeapCompletionGlow()
    {
        _isFeedbackPlaying = true;

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

        // Fade to glow
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float progress = t / fadeDuration;

            for (int i = 0; i < heapSize; i++)
            {
                int ui = _uiIndexAtHeapIndex[i];

                _nodeImages[ui].color =
                    Color.Lerp(startColors[i], completionGlowColor, progress);

                _nodeRects[ui].localScale =
                    Vector3.Lerp(startScales[i], startScales[i] * completionGlowScale, progress);
            }

            yield return null;
        }

        StartCoroutine(PlayWinFlash());
        yield return new WaitForSeconds(completionGlowDuration);

        for (int i = 0; i < heapSize; i++)
        {
            int ui = _uiIndexAtHeapIndex[i];

            _nodeRects[ui].localScale = startScales[i];
            _nodeImages[ui].color = correctNodeColor;
        }

        _isFeedbackPlaying = false;
    }

    IEnumerator ShowCompletionThenProceed()
    {
        yield return new WaitForSeconds(0.5f);

        if (completionPanel != null)
            completionPanel.SetActive(true);

        // Wait until player presses E
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

        StartCoroutine(ShowCompletionThenProceed());
    }

    public IEnumerator FadeInTimeUpPanel()
    {
        if (completionPanel != null)
            completionPanel.SetActive(false);

        if (proceedPanel != null)
            proceedPanel.SetActive(false);

        CanvasGroup cg = timeUpPanel;

        if (cg == null)
            yield break;

        cg.gameObject.SetActive(true);

        cg.alpha = 0f;
        cg.interactable = false;
        cg.blocksRaycasts = false;

        float t = 0f;
        float duration = 0.4f;

        while (t < duration)
        {
            t += Time.deltaTime;
            cg.alpha = Mathf.Lerp(0f, 1f, t / duration);
            yield return null;
        }

        cg.alpha = 1f;
        cg.interactable = true;
        cg.blocksRaycasts = true;
    }

    public void ShowProceedPanel()
    {
        if (completionPanel != null)
            completionPanel.SetActive(false);

        if (proceedPanel != null)
            proceedPanel.SetActive(true);
    }

    public void OnReplayGame()
    {
        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.name);
    }

    public void CloseTutorial()
    {
        Debug.Log("Tutorial closed. Starting timer.");

        if (tutorialPanel != null)
            tutorialPanel.SetActive(false);

        tutorialActive = false;

        statusText.text = "Compare the pages of scripts. If there is a higher page number below the orange page, select it. If not, confirm by selecting the orange page.";

        RefreshUI();

        if (gameTimer != null)
            gameTimer.StartTimer();
    }

    public void OnGiveScripts()
    {
        SceneManager.LoadScene("06_QuickSort");
    }

    void UpdateStatus()
    {
        if (statusText == null)
            return;

        if (heapCompleted)
        {
            baseInstructionText = "All script pages are organized.";
            statusText.text = baseInstructionText;
            return;
        }

        switch (currentSortType)
        {
            case ScriptSortType.PageNumber:

                baseInstructionText =
                "Compare the page numbers. If there is a higher page number below the orange page, select it. Otherwise confirm the orange page.";

                break;

            case ScriptSortType.DialogueLines:

                baseInstructionText =
                "Compare dialogue lines. If a scene below has more dialogue lines, select it. Otherwise confirm the orange scene.";

                break;

            case ScriptSortType.SceneLength:

                baseInstructionText =
                "Compare scene lengths. If a scene below lasts longer, select it. Otherwise confirm the orange scene.";

                break;
        }

        statusText.text = baseInstructionText;
    }

    public void GoToAfterTrueHeap()
    {
        SceneManager.LoadScene("05_After_TrueHeap");
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

}