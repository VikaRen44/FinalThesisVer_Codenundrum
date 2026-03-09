using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ChoiceButtonUI : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    ISelectHandler, IDeselectHandler
{
    [Header("Refs")]
    public Button button;
    public TMP_Text label;

    [Header("Optional Animation")]
    public Animator animator;

    private int _choiceIndex;
    private System.Action<int> _onClick;

    [Header("Hover / Select Scale")]
    [Tooltip("Base scale when not hovered/selected.")]
    public float normalScale = 1f;

    [Tooltip("Scale when hovered/selected.")]
    public float hoverScale = 1.06f;

    [Tooltip("How fast scale changes.")]
    public float scaleLerpSpeed = 12f;

    [Header("Idle Wobble")]
    public bool enableWobble = true;

    [Tooltip("Random wait time before each wobble event.")]
    public Vector2 wobbleIntervalRange = new Vector2(1.2f, 3.2f);

    [Tooltip("How long a wobble lasts.")]
    public Vector2 wobbleDurationRange = new Vector2(0.35f, 0.75f);

    [Tooltip("Max extra scale during wobble (added on top of current target scale).")]
    public float wobbleScaleAmount = 0.02f;

    [Tooltip("Max rotation (degrees) during wobble.")]
    public float wobbleRotationDegrees = 1.4f;

    [Tooltip("If true, wobble pauses while hovered/selected (keeps hover clean).")]
    public bool pauseWobbleWhileFocused = true;

    [Header("Result Flash")]
    [Tooltip("Graphic to flash. If null, uses button.targetGraphic.")]
    public Graphic flashGraphic;

    public Color correctFlashColor = new Color(0.25f, 1f, 0.35f, 1f);
    public Color wrongFlashColor = new Color(1f, 0.25f, 0.25f, 1f);

    [Tooltip("How long each ON/OFF step lasts (realtime).")]
    public float flashStepDuration = 0.14f;

    [Tooltip("How many ON flashes.")]
    public int flashCount = 2;

    private Vector3 _baseScale;
    private Quaternion _baseRot;

    private bool _focused;
    private float _wobbleScaleOffset;
    private float _wobbleRotOffset;

    private Coroutine _wobbleRoutine;
    private Coroutine _flashRoutine;

    private Color _originalGraphicColor = Color.white;
    private bool _originalInteractable;
    private Selectable.Transition _originalTransition;
    private ColorBlock _originalColors;
    private bool _cachedSelectable;

    public void Setup(string text, int choiceIndex, System.Action<int> onClick)
    {
        _choiceIndex = choiceIndex;
        _onClick = onClick;

        if (label) label.text = text;

        if (button)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(HandleClick);
        }
    }

    private void Awake()
    {
        AutoWire();

        _baseScale = transform.localScale;
        _baseRot = transform.localRotation;

        CacheSelectable();
        CacheOriginalGraphicColor();

        ApplyImmediate(normalScale);

        if (enableWobble)
            _wobbleRoutine = StartCoroutine(WobbleLoop());
    }

    private void OnEnable()
    {
        if (enableWobble && _wobbleRoutine == null && gameObject.activeInHierarchy)
            _wobbleRoutine = StartCoroutine(WobbleLoop());
    }

    private void OnDisable()
    {
        if (_wobbleRoutine != null)
        {
            StopCoroutine(_wobbleRoutine);
            _wobbleRoutine = null;
        }

        if (_flashRoutine != null)
        {
            StopCoroutine(_flashRoutine);
            _flashRoutine = null;
        }

        _wobbleScaleOffset = 0f;
        _wobbleRotOffset = 0f;
        _focused = false;

        RestoreSelectable();
        RestoreGraphicColor();
        ApplyImmediate(normalScale);
    }

    private void Update()
    {
        float target = _focused ? hoverScale : normalScale;

        float wobbleExtra = (enableWobble && (!pauseWobbleWhileFocused || !_focused)) ? _wobbleScaleOffset : 0f;

        Vector3 desiredScale = _baseScale * (target + wobbleExtra);
        transform.localScale = Vector3.Lerp(transform.localScale, desiredScale, Time.unscaledDeltaTime * scaleLerpSpeed);

        float rot = (enableWobble && (!pauseWobbleWhileFocused || !_focused)) ? _wobbleRotOffset : 0f;
        Quaternion desiredRot = _baseRot * Quaternion.Euler(0f, 0f, rot);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, desiredRot, Time.unscaledDeltaTime * scaleLerpSpeed);
    }

    private void HandleClick()
    {
        if (animator) animator.SetTrigger("Click");
        _onClick?.Invoke(_choiceIndex);
    }

    public void SetInteractable(bool value)
    {
        if (button) button.interactable = value;
    }

    public bool IsInteractable()
    {
        return button != null && button.interactable && button.gameObject.activeInHierarchy;
    }

    public void FocusNow()
    {
        AutoWire();
        if (button == null || !button.gameObject.activeInHierarchy) return;

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(button.gameObject);
    }

    public void Submit()
    {
        if (!IsInteractable()) return;
        if (button != null)
            button.onClick.Invoke();
    }

    public void PlayResultFlash(bool correct)
    {
        AutoWire();
        CacheSelectable();
        CacheOriginalGraphicColor();

        if (flashGraphic == null) return;

        if (_flashRoutine != null)
            StopCoroutine(_flashRoutine);

        _flashRoutine = StartCoroutine(ResultFlashRoutine(correct));
    }

    private IEnumerator ResultFlashRoutine(bool correct)
    {
        _originalInteractable = button ? button.interactable : true;

        if (button != null)
        {
            _originalTransition = button.transition;
            _originalColors = button.colors;

            button.transition = Selectable.Transition.None;
            button.interactable = true;
        }

        Color flashColor = correct ? correctFlashColor : wrongFlashColor;
        int loops = Mathf.Max(1, flashCount);
        float step = Mathf.Max(0.01f, flashStepDuration);

        for (int i = 0; i < loops; i++)
        {
            ForceGraphicColor(flashColor);
            yield return new WaitForSecondsRealtime(step);

            ForceGraphicColor(_originalGraphicColor);
            yield return new WaitForSecondsRealtime(step);
        }

        ForceGraphicColor(_originalGraphicColor);

        if (button != null)
        {
            button.transition = _originalTransition;
            button.colors = _originalColors;
            button.interactable = _originalInteractable;
        }

        _flashRoutine = null;
    }

    private void ForceGraphicColor(Color c)
    {
        if (flashGraphic == null) return;

        flashGraphic.canvasRenderer.SetColor(c);
        flashGraphic.color = c;
    }

    private void RestoreGraphicColor()
    {
        if (flashGraphic == null) return;
        ForceGraphicColor(_originalGraphicColor);
    }

    public void OnPointerEnter(PointerEventData eventData) => _focused = true;
    public void OnPointerExit(PointerEventData eventData) => _focused = false;

    public void OnSelect(BaseEventData eventData) => _focused = true;
    public void OnDeselect(BaseEventData eventData) => _focused = false;

    private IEnumerator WobbleLoop()
    {
        yield return new WaitForSecondsRealtime(Random.Range(0f, 0.6f));

        while (true)
        {
            float wait = Random.Range(Mathf.Min(wobbleIntervalRange.x, wobbleIntervalRange.y),
                                      Mathf.Max(wobbleIntervalRange.x, wobbleIntervalRange.y));
            yield return new WaitForSecondsRealtime(wait);

            if (pauseWobbleWhileFocused && _focused)
                continue;

            float dur = Random.Range(Mathf.Min(wobbleDurationRange.x, wobbleDurationRange.y),
                                     Mathf.Max(wobbleDurationRange.x, wobbleDurationRange.y));

            float targetScale = Random.Range(0f, wobbleScaleAmount);
            float targetRot = Random.Range(-wobbleRotationDegrees, wobbleRotationDegrees);

            float t = 0f;
            while (t < dur)
            {
                if (pauseWobbleWhileFocused && _focused)
                    break;

                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / dur);

                float wave = Mathf.Sin(u * Mathf.PI);

                _wobbleScaleOffset = targetScale * wave;
                _wobbleRotOffset = targetRot * wave;

                yield return null;
            }

            _wobbleScaleOffset = 0f;
            _wobbleRotOffset = 0f;
        }
    }

    private void ApplyImmediate(float targetScale01)
    {
        transform.localScale = _baseScale * targetScale01;
        transform.localRotation = _baseRot;
    }

    private void AutoWire()
    {
        if (button == null)
            button = GetComponent<Button>();

        if (label == null)
            label = GetComponentInChildren<TMP_Text>(true);

        if (animator == null)
            animator = GetComponent<Animator>();

        if (flashGraphic == null && button != null)
            flashGraphic = button.targetGraphic;
    }

    private void CacheSelectable()
    {
        _cachedSelectable = (button != null);
        if (!_cachedSelectable) return;

        _originalTransition = button.transition;
        _originalColors = button.colors;
    }

    private void RestoreSelectable()
    {
        if (button == null || !_cachedSelectable) return;
        button.transition = _originalTransition;
        button.colors = _originalColors;
    }

    private void CacheOriginalGraphicColor()
    {
        if (flashGraphic == null) return;
        _originalGraphicColor = flashGraphic.color;
    }
}