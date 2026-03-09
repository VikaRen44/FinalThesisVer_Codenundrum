using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

public class QuickSortManager : MonoBehaviour
{
    [Header("Zones")]
    public Transform propsContainer;
    public Transform leftZone;
    public Transform pivotZone;
    public Transform rightZone;

    [SerializeField] Image leftZoneImage;
    [SerializeField] Image rightZoneImage;

    [Header("Mini Array Display")]
    [SerializeField] Transform miniArrayContainer;
    [SerializeField] GameObject miniPropPrefab;

    [Header("Sorted Array Glow")]
    [SerializeField] Color sortedGlowColor = new Color(1f, 0.9f, 0.3f);
    [SerializeField] float sortedGlowDuration = 0.25f;
    [SerializeField] float sortedGlowDelay = 0.08f;

    [Header("Feedback Settings")]
    [SerializeField] Color correctFlashColor = new Color(0.3f, 1f, 0.3f);
    [SerializeField] float flashDuration = 0.2f;

    public GameObject propPrefab;
    public PropDatabase propDatabase;

    [Header("UI")]
    public GameObject classificationPanel;
    public TextMeshProUGUI stageText;

    [Header("Completion UI")]
    public CanvasGroup completionCanvasGroup;
    public TextMeshProUGUI finalTimeText;

    [Header("Win Flash")]
    public Image winFlash;
    public float winFlashDuration = 0.6f;

    [Header("Win Background")]
    public CanvasGroup winBackground;

    [Header("Lose UI")]
    public CanvasGroup timeUpPanel;

    [Header("Fade Settings")]
    public float fadeDuration = 0.5f;

    [Header("Proceed UI")]
    public GameObject proceedPanel;
    public CanvasGroup proceedCanvasGroup;

    [Header("Screen Feedback")]
    [SerializeField] Image screenGlow;

    [SerializeField] float glowAlpha = 0.55f;
    [SerializeField] float glowDuration = 0.45f;
    [SerializeField] float glowFadeSpeed = 4f;

    [Header("Pivot Feedback")]
    [SerializeField] float pivotPulseSpeed = 2.5f;
    [SerializeField] float pivotPulseStrength = 0.15f;

    private bool sortingComplete = false;

    // ✅ NEW UI STATE FLAGS
    private bool completionVisible = false;
    private bool proceedVisible = false;

    private List<int> numbers = new List<int>();
    private Stack<(int low, int high)> rangeStack = new Stack<(int, int)>();

    Dictionary<int, Sprite> numberSprites = new Dictionary<int, Sprite>();

    private int currentLow;
    private int currentHigh;

    private int pivotValue;
    private int pivotIndex;

    private PropItem currentPivot;
    private PropItem selectedItem;

    private List<int> leftPartition = new List<int>();
    private List<int> rightPartition = new List<int>();

    private bool waitingForCompletionInput = false;
    private bool allowCompletionInput = false;

    public GameTimer gameTimer;

    // ✅ FIX FOR REPEATED SCALE STACKING
    private Dictionary<PropItem, Vector3> originalPropScales = new Dictionary<PropItem, Vector3>();
    private HashSet<PropItem> scalingItems = new HashSet<PropItem>();

    void Start()
    {

        classificationPanel.SetActive(false);

        if (completionCanvasGroup != null)
        {
            completionCanvasGroup.alpha = 0f;
            completionCanvasGroup.interactable = false;
            completionCanvasGroup.blocksRaycasts = false;
        }

        numbers = GenerateUniqueNumbers(6, 1, 20);

        foreach (int num in numbers)
        {
            numberSprites[num] = propDatabase.propSprites[
                Random.Range(0, propDatabase.propSprites.Count)
            ];
        }

        rangeStack.Push((0, numbers.Count - 1));

        StartCoroutine(ProcessNextRange());

        if (proceedCanvasGroup != null)
        {
            proceedCanvasGroup.alpha = 0f;
            proceedCanvasGroup.interactable = false;
            proceedCanvasGroup.blocksRaycasts = false;
        }

        if (winFlash != null)
        {
            Color c = winFlash.color;
            c.a = 0f;
            winFlash.color = c;
            winFlash.gameObject.SetActive(false);
        }

        if (winBackground != null)
        {
            winBackground.alpha = 0f;
            winBackground.interactable = false;
            winBackground.blocksRaycasts = false;
            winBackground.gameObject.SetActive(false);
        }

        if (proceedPanel != null)
            proceedPanel.SetActive(false);
    }

    List<int> GenerateUniqueNumbers(int count, int min, int max)
    {
        HashSet<int> set = new HashSet<int>();

        while (set.Count < count)
        {
            set.Add(Random.Range(min, max));
        }

        return new List<int>(set);
    }

    void Update()
    {

        if (!sortingComplete || Keyboard.current == null)
            return;

        if (allowCompletionInput && Keyboard.current.eKey.wasPressedThisFrame)
        {
            // FIRST PRESS → show Proceed Panel
            if (waitingForCompletionInput)
            {
                waitingForCompletionInput = false;
                ShowProceedPanel();
            }
            // SECOND PRESS → next scene
            else if (proceedVisible)
            {
                ProceedToNextScene();
            }
        }
    }

    void UpdateMiniArray()
    {
        foreach (Transform child in miniArrayContainer)
            Destroy(child.gameObject);

        foreach (int num in numbers)
        {
            GameObject obj = Instantiate(miniPropPrefab, miniArrayContainer);

            obj.GetComponent<PropItem>().Initialize(
                num,
                numberSprites[num]
            );
        }
    }

    public void CloseTutorial()
    {
        if (gameTimer != null)
            gameTimer.StartTimer();
    }

    IEnumerator ProcessNextRange()
    {

        if (rangeStack.Count == 0)
        {
            sortingComplete = true;

            if (gameTimer != null)
                gameTimer.StopTimer();

            ClearZones();
            SpawnFullArray();

            yield return StartCoroutine(SortedArrayGlow());

            stageText.text = "Everything is in the right place. Sort Complete!";

            // GOLDEN WIN PULSE
            yield return StartCoroutine(ScreenGlowFlash(new Color(1f, 0.9f, 0.3f)));
            yield return StartCoroutine(PlayWinFlash());
            yield return StartCoroutine(FadeInWinBackground());

            yield return new WaitForSeconds(0.3f);

            ShowCompletionPanel();

            yield break;
        }

        var range = rangeStack.Pop();
        currentLow = range.low;
        currentHigh = range.high;

        if (currentLow >= currentHigh)
        {
            yield return StartCoroutine(ProcessNextRange());
            yield break;
        }

        ClearZones();

        int groupSize = currentHigh - currentLow + 1;
        stageText.text = $"Choose a pivot to compare with the group.";

        SpawnCurrentSubarray();
    }

    void SpawnCurrentSubarray()
    {
        float spacing = 220f;
        int count = currentHigh - currentLow + 1;
        float totalWidth = (count - 1) * spacing;

        for (int i = 0; i < count; i++)
        {
            GameObject obj = Instantiate(propPrefab, propsContainer);
            RectTransform rect = obj.GetComponent<RectTransform>();

            rect.localScale = Vector3.one;
            rect.sizeDelta = new Vector2(200, 200);

            float xPos = (i * spacing) - (totalWidth / 2f);
            rect.anchoredPosition = new Vector2(xPos, 0);

            Sprite sprite = numberSprites[numbers[currentLow + i]];

            obj.GetComponent<PropItem>().Initialize(
                numbers[currentLow + i],
                sprite
            );
        }
    }

    void SpawnFullArray()
    {
        foreach (Transform child in propsContainer)
            Destroy(child.gameObject);

        float spacing = 220f;
        float totalWidth = (numbers.Count - 1) * spacing;

        for (int i = 0; i < numbers.Count; i++)
        {
            GameObject obj = Instantiate(propPrefab, propsContainer);
            RectTransform rect = obj.GetComponent<RectTransform>();

            rect.localScale = Vector3.one;
            rect.sizeDelta = new Vector2(200, 200);

            float xPos = (i * spacing) - (totalWidth / 2f);
            rect.anchoredPosition = new Vector2(xPos, 0);

            Sprite sprite = numberSprites[numbers[i]];

            obj.GetComponent<PropItem>().Initialize(
                numbers[i],
                sprite
            );
        }
    }

    public void OnPropClicked(PropItem item)
    {
        if (sortingComplete)
            return;

        if (currentPivot == null)
        {
            SetPivot(item);
        }
        else if (item != currentPivot)
        {
            if (selectedItem != null && selectedItem != item)
            {
                ResetPropToOriginalScale(selectedItem);
            }

            selectedItem = item;
            StartCoroutine(ScaleSelectedProp(item));
            classificationPanel.SetActive(true);
        }
    }

    void SetPivot(PropItem item)
    {
        currentPivot = item;
        pivotValue = item.value;

        item.transform.SetParent(pivotZone, false);

        RectTransform rect = item.GetComponent<RectTransform>();
        rect.localScale = Vector3.one;
        rect.sizeDelta = new Vector2(200, 200);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;

        Image img = item.GetComponent<Image>();
        if (img != null)
            img.color = new Color(1f, 0.8f, 0.2f);

        StartCoroutine(PivotPulse(item));

        leftPartition.Clear();
        rightPartition.Clear();

        stageText.text = "Now compare the others to this one.";
    }

    public void ChooseLess()
    {
        if (selectedItem == null) return;

        if (selectedItem.value < pivotValue)
        {
            PlaceItem(selectedItem, leftZone, leftPartition);
        }
        else
        {
            StartCoroutine(ScreenGlowFlash(new Color(1f, 0.2f, 0.2f)));
            StartCoroutine(WrongItemFlash(selectedItem));
            StartCoroutine(ShakeProp(selectedItem));
        }
    }

    public void ChooseGreater()
    {
        if (selectedItem == null) return;

        if (selectedItem.value > pivotValue)
        {
            PlaceItem(selectedItem, rightZone, rightPartition);
        }
        else
        {
            StartCoroutine(ScreenGlowFlash(new Color(1f, 0.2f, 0.2f)));
            StartCoroutine(WrongItemFlash(selectedItem));
            StartCoroutine(ShakeProp(selectedItem));
        }
    }

    void PlaceItem(PropItem item, Transform zone, List<int> targetList)
    {

        RectTransform rect = item.GetComponent<RectTransform>();
        rect.localScale = Vector3.one;

        item.transform.SetParent(zone, false);
        targetList.Add(item.value);

        classificationPanel.SetActive(false);
        selectedItem = null;

        StartCoroutine(ScreenGlowFlash(new Color(0.3f, 1f, 0.6f)));

        CheckPartitionComplete();
    }

    void CheckPartitionComplete()
    {
        if (propsContainer.childCount == 0)
        {
            StartCoroutine(PartitionCompleteSequence());
        }
    }

    IEnumerator ZoneFlash(Transform zone)
    {
        Image targetImage = null;

        if (zone == leftZone)
            targetImage = leftZoneImage;

        else if (zone == rightZone)
            targetImage = rightZoneImage;

        if (targetImage == null)
            yield break;

        Color original = targetImage.color;

        targetImage.color = correctFlashColor;

        yield return new WaitForSeconds(flashDuration);

        targetImage.color = original;
    }

    IEnumerator PartitionCompleteSequence()
    {

        stageText.text = "Group sorted. Moving on.";
        yield return new WaitForSeconds(1f);

        int index = currentLow;

        foreach (int val in leftPartition)
            numbers[index++] = val;

        numbers[index] = pivotValue;
        pivotIndex = index;
        index++;

        foreach (int val in rightPartition)
            numbers[index++] = val;

        UpdateMiniArray();

        rangeStack.Push((pivotIndex + 1, currentHigh));
        rangeStack.Push((currentLow, pivotIndex - 1));

        yield return new WaitForSeconds(1f);

        StartCoroutine(ProcessNextRange());
    }

    IEnumerator ScaleSelectedProp(PropItem item)
    {
        if (item == null) yield break;

        RectTransform rect = item.GetComponent<RectTransform>();
        if (rect == null) yield break;

        if (!originalPropScales.ContainsKey(item))
            originalPropScales[item] = rect.localScale;

        if (scalingItems.Contains(item))
            yield break;

        scalingItems.Add(item);

        Vector3 baseScale = originalPropScales[item];
        Vector3 targetScale = baseScale * 1.15f;

        if (Vector3.Distance(rect.localScale, targetScale) <= 0.001f)
        {
            rect.localScale = targetScale;
            scalingItems.Remove(item);
            yield break;
        }

        Vector3 startScale = rect.localScale;

        float t = 0f;
        float duration = 0.15f;

        while (t < duration)
        {
            if (rect == null)
            {
                scalingItems.Remove(item);
                yield break;
            }

            t += Time.deltaTime;
            rect.localScale = Vector3.Lerp(startScale, targetScale, t / duration);
            yield return null;
        }

        rect.localScale = targetScale;
        scalingItems.Remove(item);
    }

    void ResetPropToOriginalScale(PropItem item)
    {
        if (item == null)
            return;

        RectTransform rect = item.GetComponent<RectTransform>();
        if (rect == null)
            return;

        if (originalPropScales.TryGetValue(item, out Vector3 originalScale))
        {
            rect.localScale = originalScale;
        }
        else
        {
            rect.localScale = Vector3.one;
        }

        scalingItems.Remove(item);
    }

    IEnumerator ShakeProp(PropItem item)
    {
        if (item == null) yield break;

        RectTransform rect = item.GetComponent<RectTransform>();
        Vector2 startPos = rect.anchoredPosition;

        float duration = 0.35f;
        float strength = 12f;
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;

            rect.anchoredPosition = startPos + new Vector2(
                Random.Range(-strength, strength),
                Random.Range(-strength, strength)
            );

            yield return null;
        }

        rect.anchoredPosition = startPos;
    }

    IEnumerator SortedArrayGlow()
    {
        List<Image> items = new List<Image>();

        foreach (Transform child in propsContainer)
        {
            Image img = child.GetComponent<Image>();
            if (img != null)
                items.Add(img);
        }

        foreach (Image img in items)
        {
            Color original = img.color;

            img.color = sortedGlowColor;

            yield return new WaitForSeconds(sortedGlowDuration);

            img.color = original;

            yield return new WaitForSeconds(sortedGlowDelay);
        }
    }

    void ClearZones()
    {
        foreach (Transform child in propsContainer)
            Destroy(child.gameObject);

        foreach (Transform child in leftZone)
            Destroy(child.gameObject);

        foreach (Transform child in pivotZone)
            Destroy(child.gameObject);

        foreach (Transform child in rightZone)
            Destroy(child.gameObject);

        currentPivot = null;
        selectedItem = null;
        originalPropScales.Clear();
        scalingItems.Clear();
    }

    void ShowCompletionPanel()
    {
        completionVisible = true;
        waitingForCompletionInput = true;

        allowCompletionInput = false;

        StartCoroutine(FadeInCompletion());
        StartCoroutine(EnableCompletionInputDelay());
    }

    IEnumerator EnableCompletionInputDelay()
    {
        yield return new WaitForSeconds(0.5f);
        allowCompletionInput = true;
    }

    IEnumerator FadeInCompletion()
    {
        float elapsed = 0f;

        completionCanvasGroup.alpha = 0f;
        completionCanvasGroup.interactable = false;
        completionCanvasGroup.blocksRaycasts = false;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            completionCanvasGroup.alpha = Mathf.Clamp01(elapsed / fadeDuration);
            yield return null;
        }

        completionCanvasGroup.alpha = 1f;
        completionCanvasGroup.interactable = true;
        completionCanvasGroup.blocksRaycasts = true;
    }

    IEnumerator FadeInWinBackground()
    {
        if (winBackground == null)
            yield break;

        winBackground.gameObject.SetActive(true);

        float t = 0f;

        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            winBackground.alpha = Mathf.Lerp(0f, 1f, t / fadeDuration);
            yield return null;
        }

        winBackground.alpha = 1f;
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

    IEnumerator ScreenGlowFlash(Color glowColor)
    {
        if (screenGlow == null)
            yield break;

        Color c = glowColor;
        c.a = glowAlpha;

        screenGlow.color = c;

        yield return new WaitForSeconds(glowDuration);

        while (screenGlow.color.a > 0.01f)
        {
            Color fade = screenGlow.color;
            fade.a = Mathf.Lerp(fade.a, 0f, Time.deltaTime * glowFadeSpeed);
            screenGlow.color = fade;

            yield return null;
        }

        Color final = screenGlow.color;
        final.a = 0f;
        screenGlow.color = final;
    }

    IEnumerator WrongItemFlash(PropItem item)
    {
        if (item == null)
            yield break;

        Image img = item.GetComponent<Image>();
        if (img == null)
            yield break;

        Color original = img.color;

        img.color = new Color(1f, 0.3f, 0.3f);

        yield return new WaitForSeconds(0.2f);

        img.color = original;
    }

    IEnumerator PivotPulse(PropItem pivot)
    {
        if (pivot == null)
            yield break;

        Image img = pivot.GetComponent<Image>();
        if (img == null)
            yield break;

        Color baseColor = img.color;

        while (pivot != null && pivot == currentPivot)
        {
            float pulse = Mathf.Sin(Time.time * pivotPulseSpeed) * pivotPulseStrength;

            Color c = baseColor;
            c.r = Mathf.Clamp01(baseColor.r + pulse);
            c.g = Mathf.Clamp01(baseColor.g + pulse);

            img.color = c;

            yield return null;
        }

        img.color = baseColor;
    }

    public IEnumerator FadeInTimeUpPanel()
    {
        if (timeUpPanel == null)
            yield break;

        if (completionCanvasGroup != null)
            completionCanvasGroup.gameObject.SetActive(false);

        if (proceedPanel != null)
            proceedPanel.SetActive(false);

        timeUpPanel.gameObject.SetActive(true);

        timeUpPanel.alpha = 0f;
        timeUpPanel.interactable = false;
        timeUpPanel.blocksRaycasts = false;

        float t = 0f;
        float duration = 0.4f;

        while (t < duration)
        {
            t += Time.deltaTime;
            timeUpPanel.alpha = Mathf.Lerp(0f, 1f, t / duration);
            yield return null;
        }

        timeUpPanel.alpha = 1f;
        timeUpPanel.interactable = true;
        timeUpPanel.blocksRaycasts = true;
    }

    void ShowProceedPanel()
    {

        completionCanvasGroup.alpha = 0f;
        completionCanvasGroup.interactable = false;
        completionCanvasGroup.blocksRaycasts = false;

        proceedVisible = true;
        proceedPanel.SetActive(true);
        StartCoroutine(FadeInProceedPanel());
    }

    public void ReplayLevel()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void ExitLevel()
    {
        SceneManager.LoadScene("07_Ending");
    }

    IEnumerator FadeInProceedPanel()
    {
        float elapsed = 0f;

        proceedCanvasGroup.alpha = 0f;
        proceedCanvasGroup.interactable = false;
        proceedCanvasGroup.blocksRaycasts = false;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            proceedCanvasGroup.alpha = Mathf.Clamp01(elapsed / fadeDuration);
            yield return null;
        }

        proceedCanvasGroup.alpha = 1f;
        proceedCanvasGroup.interactable = true;
        proceedCanvasGroup.blocksRaycasts = true;
    }

    public void ProceedToNextScene()
    {
        SceneManager.LoadScene("06_After_QuickSort");
    }
}