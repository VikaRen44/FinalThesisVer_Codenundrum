using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SimpleDialogueUI : MonoBehaviour
{
    // ✅ optional global access if you want
    public static SimpleDialogueUI Instance { get; private set; }

    [Header("Role")]
    [Tooltip("If TRUE: this UI is intended for CUTSCENES.\nIf FALSE: this UI is intended for NORMAL dialogues.")]
    public bool isCutsceneUI = false;

    [Header("Root")]
    [Tooltip("DialogueRoot (the parent). This should stay active.")]
    public GameObject root;

    [Tooltip("DialoguePanel (the visual panel you want to hide/show).")]
    public GameObject visualRoot;

    [Header("Texts")]
    public TMP_Text nameText;
    public TMP_Text bodyText;
    public GameObject continueIcon;

    [Header("Portrait")]
    public Image portraitImage;

    [Header("Choices")]
    public Transform choicesRoot;
    public Button choiceButtonPrefab;

    [Header("Choice Navigation")]
    [Tooltip("If true, auto-selects the first spawned choice button so controller can navigate immediately.")]
    public bool autoSelectFirstChoice = true;

    [Header("Start State")]
    public bool hideVisualOnAwake = true;

    private readonly List<Button> _spawnedChoices = new List<Button>();

    private Coroutine _emotionRoutine;
    private Vector3 _portraitBaseScale = Vector3.one;
    private bool _hasBaseScale = false;

    private void Awake()
    {
        // ✅ Make Instance prefer NORMAL UI if both exist
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            // If current Instance is cutscene but this is normal, replace it
            if (Instance.isCutsceneUI && !this.isCutsceneUI)
                Instance = this;
        }

        if (root == null)
            root = gameObject;

        if (visualRoot == null)
        {
            var t = transform.Find("DialoguePanel");
            if (t != null) visualRoot = t.gameObject;
        }

        if (hideVisualOnAwake)
            SetVisualActive(false);

        ClearChoices();
        SetContinueIconVisible(false);
        SetPortrait(null, true);
    }

    public void Show()
    {
        Canvas canvas = (root != null) ? root.GetComponentInParent<Canvas>(true) : GetComponentInParent<Canvas>(true);
        if (canvas != null && !canvas.gameObject.activeSelf)
            canvas.gameObject.SetActive(true);

        if (root != null && !root.activeSelf)
            root.SetActive(true);

        SetVisualActive(true);
    }

    public void Hide()
    {
        SetVisualActive(false);
        ClearChoices();
        ResetPortraitTransform();
    }

    private void SetVisualActive(bool active)
    {
        if (visualRoot != null)
            visualRoot.SetActive(active);

        // ChoicesRoot should follow the panel visibility
        if (choicesRoot != null)
            choicesRoot.gameObject.SetActive(active);
    }

    public void SetSpeakerName(string speaker)
    {
        if (!nameText) return;

        if (string.IsNullOrEmpty(speaker))
        {
            nameText.gameObject.SetActive(false);
        }
        else
        {
            nameText.gameObject.SetActive(true);
            nameText.text = speaker;
        }
    }

    public void SetBodyText(string text)
    {
        if (bodyText != null)
            bodyText.text = text;
    }

    public void SetContinueIconVisible(bool visible)
    {
        if (continueIcon != null)
            continueIcon.SetActive(visible);
    }

    public void SetPortrait(Sprite sprite, bool clearWhenNull)
    {
        if (portraitImage == null) return;

        if (sprite == null && clearWhenNull)
        {
            portraitImage.sprite = null;
            portraitImage.enabled = false;
            return;
        }

        if (sprite != null)
        {
            portraitImage.sprite = sprite;
            portraitImage.enabled = true;
        }
    }

    public void ClearChoices()
    {
        foreach (var b in _spawnedChoices)
        {
            if (b != null) Destroy(b.gameObject);
        }
        _spawnedChoices.Clear();
    }

    public void ShowChoices(SimpleDialogueChoice[] choices, System.Action<int> onSelected)
    {
        ClearChoices();

        if (choices == null || choices.Length == 0) return;
        if (choicesRoot == null || choiceButtonPrefab == null)
        {
            Debug.LogWarning("[SimpleDialogueUI] Choices requested but choicesRoot or prefab is missing.");
            return;
        }

        SetVisualActive(true);

        Button first = null;

        for (int i = 0; i < choices.Length; i++)
        {
            int capturedIndex = i;

            Button btn = Instantiate(choiceButtonPrefab, choicesRoot);
            _spawnedChoices.Add(btn);

            btn.onClick.RemoveAllListeners();

            TMP_Text label = btn.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                label.text = choices[i].text;

            btn.onClick.AddListener(() =>
            {
                onSelected?.Invoke(capturedIndex);
            });

            if (first == null) first = btn;
        }

        if (autoSelectFirstChoice && first != null)
        {
            var es = EventSystem.current;
            if (es != null)
                es.SetSelectedGameObject(first.gameObject);
        }
    }

    public void PlayEmotion(DialogueEmotion emotion)
    {
        if (portraitImage == null) return;

        var rt = portraitImage.rectTransform;

        if (_emotionRoutine != null)
        {
            StopCoroutine(_emotionRoutine);
            if (_hasBaseScale)
                rt.localScale = _portraitBaseScale;
        }

        _portraitBaseScale = rt.localScale;
        _hasBaseScale = true;

        switch (emotion)
        {
            case DialogueEmotion.Happy:
                _emotionRoutine = StartCoroutine(HappyBounceRoutine());
                break;
            case DialogueEmotion.Angry:
                _emotionRoutine = StartCoroutine(AngryShakeScaleRoutine());
                break;
            case DialogueEmotion.Surprised:
                _emotionRoutine = StartCoroutine(SurprisedPopRoutine());
                break;
            default:
                rt.localScale = _portraitBaseScale;
                _emotionRoutine = null;
                break;
        }
    }

    private void ResetPortraitTransform()
    {
        if (!portraitImage || !_hasBaseScale) return;
        portraitImage.rectTransform.localScale = _portraitBaseScale;
    }

    private IEnumerator HappyBounceRoutine()
    {
        var rt = portraitImage.rectTransform;
        float duration = 0.8f, time = 0f, amp = 0.06f;

        while (time < duration)
        {
            time += Time.deltaTime;
            float s = 1f + Mathf.Sin(time * Mathf.PI * 2f) * amp;
            rt.localScale = _portraitBaseScale * s;
            yield return null;
        }

        rt.localScale = _portraitBaseScale;
        _emotionRoutine = null;
    }

    private IEnumerator AngryShakeScaleRoutine()
    {
        var rt = portraitImage.rectTransform;
        float duration = 0.4f, time = 0f, amp = 0.10f;

        while (time < duration)
        {
            time += Time.deltaTime;
            float s = 1f + Random.Range(-amp, amp);
            rt.localScale = _portraitBaseScale * s;
            yield return null;
        }

        rt.localScale = _portraitBaseScale;
        _emotionRoutine = null;
    }

    private IEnumerator SurprisedPopRoutine()
    {
        var rt = portraitImage.rectTransform;
        float duration = 0.3f, time = 0f, maxScale = 1.18f;

        while (time < duration)
        {
            time += Time.deltaTime;
            float t = time / duration;
            float s = Mathf.Lerp(maxScale, 1f, t);
            rt.localScale = _portraitBaseScale * s;
            yield return null;
        }

        rt.localScale = _portraitBaseScale;
        _emotionRoutine = null;
    }
}
