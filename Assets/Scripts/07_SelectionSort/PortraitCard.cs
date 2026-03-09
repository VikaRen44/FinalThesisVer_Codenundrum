using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class PortraitCard : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler
{
    [Header("Refs (drag from prefab)")]
    public RectTransform visual;
    public Image portraitImage;
    public TMP_Text nameText;

    [Header("Stats UI")]
    public CanvasGroup statsRow;
    public TMP_Text sizeText;
    public TMP_Text timeText;

    [Header("Index Badge")]
    public TMP_Text indexBadge;
    public bool oneBasedIndex = true;
    public string indexFormat = "0";

    [Header("Outlines (either Image or Outline)")]
    public Image hoverOutlineImg;
    public Outline hoverOutlineFx;
    public Image frontOutlineImg;
    public Outline frontOutlineFx;

    [Header("Lock overlay (optional)")]
    public CanvasGroup lockOverlay;

    [Header("FX")]
    public float hoverScale = 1.04f;
    public float selectedScale = 1.07f;
    public float animTime = 0.08f;

    public PortraitData Data { get; private set; }
    public int SlotIndex { get; private set; }

    private Action onClick;
    private bool isLocked, isHover, isSelected;
    private Coroutine scaleCo;

    // Runtime overrides (set by GalleryGame)
    private string _runtimeSizeLabel = "";
    private string _runtimeTimeLabel = "";

    // ----------------------------
    // ✅ NEW SAFETY: never allow prefab visuals to stay disabled
    // ----------------------------
    [Header("Safety (auto-fix disabled visuals)")]
    [Tooltip("If true, PortraitCard will force-enable Visual and critical UI components on Awake/OnEnable/Bind. This fixes cases where a spawner/template disables children.")]
    public bool forceEnableVisualTree = true;

    [Tooltip("Logs when PortraitCard had to re-enable Visual at runtime.")]
    public bool logWhenForcingVisualOn = true;

    private void Awake()
    {
        // ✅ If something saved the prefab/template with Visual OFF, fix it immediately
        ForceVisualTreeOn("Awake");
    }

    private void OnEnable()
    {
        // ✅ Some systems toggle the card or its children off during layout/spawn.
        // OnEnable is the earliest reliable time to bring it back.
        ForceVisualTreeOn("OnEnable");
    }

    /// <summary>
    /// Binds data to this card and resets visuals/FX.
    /// </summary>
    public void Bind(PortraitData data, int slotIndex, Action onClickCB, string sizeLabel, string timeLabel)
    {
        // ✅ FIRST: ensure visuals are active before we assign sprites/text
        ForceVisualTreeOn("Bind");

        Data = data;
        onClick = onClickCB;

        _runtimeSizeLabel = sizeLabel;
        _runtimeTimeLabel = timeLabel;

        // sprite
        if (portraitImage)
        {
            portraitImage.sprite = data ? data.portrait : null;

            // optional debug
            if (data != null && data.portrait == null)
                Debug.LogWarning($"[PortraitCard] Portrait sprite is NULL for PortraitData: {data.name}", this);
        }

        // name
        if (nameText) nameText.text = data ? data.displayName : "(null)";

        // stats
        if (sizeText) sizeText.text = $"Size {_runtimeSizeLabel}";
        if (timeText) timeText.text = $"Time {_runtimeTimeLabel}";

        // index badge
        SetIndex(slotIndex, true);

        // reset fx
        SetFront(false);
        SetLocked(false);
        SetSelected(false, true);
        SetHover(false, true);
    }

    public void SetIndex(int slotIndex, bool instant = false)
    {
        // ✅ Ensure visuals exist even if SetIndex is called before Bind
        ForceVisualTreeOn("SetIndex");

        SlotIndex = slotIndex;

        if (indexBadge)
        {
            int shown = oneBasedIndex ? slotIndex + 1 : slotIndex;
            indexBadge.text = shown.ToString(indexFormat);
        }
    }

    public void SetStatsVisible(bool on)
    {
        ForceVisualTreeOn("SetStatsVisible");

        if (!statsRow) return;

        statsRow.alpha = on ? 1f : 0f;
        statsRow.interactable = on;
        statsRow.blocksRaycasts = false;
    }

    public void SetFront(bool on)
    {
        ForceVisualTreeOn("SetFront");

        if (frontOutlineImg) frontOutlineImg.enabled = on;
        if (frontOutlineFx) frontOutlineFx.enabled = on;
    }

    public void SetLocked(bool on)
    {
        // ✅ IMPORTANT: do NOT disable Visual here.
        // Lock should only affect overlay/interaction, not the card rendering itself.
        ForceVisualTreeOn("SetLocked");

        isLocked = on;
        if (lockOverlay) lockOverlay.alpha = on ? 0.35f : 0f;
    }

    public void SetSelected(bool on, bool instant = false)
    {
        ForceVisualTreeOn("SetSelected");

        isSelected = on;

        if (hoverOutlineImg) hoverOutlineImg.enabled = on || isHover;
        if (hoverOutlineFx) hoverOutlineFx.enabled = on || isHover;

        TweenScale(on ? selectedScale : (isHover ? hoverScale : 1f), instant);
    }

    public void OnPointerEnter(PointerEventData e)
    {
        ForceVisualTreeOn("OnPointerEnter");

        if (!isLocked) SetHover(true);
    }

    public void OnPointerExit(PointerEventData e)
    {
        ForceVisualTreeOn("OnPointerExit");

        if (!isLocked) SetHover(false);
    }

    public void OnPointerClick(PointerEventData e)
    {
        ForceVisualTreeOn("OnPointerClick");

        if (!isLocked) onClick?.Invoke();
    }

    private void SetHover(bool on, bool instant = false)
    {
        ForceVisualTreeOn("SetHover");

        isHover = on;

        if (hoverOutlineImg) hoverOutlineImg.enabled = on || isSelected;
        if (hoverOutlineFx) hoverOutlineFx.enabled = on || isSelected;

        TweenScale(isSelected ? selectedScale : (on ? hoverScale : 1f), instant);
    }

    private void TweenScale(float target, bool instant)
    {
        ForceVisualTreeOn("TweenScale");

        if (!visual) return;

        if (instant)
        {
            visual.localScale = Vector3.one * target;
            return;
        }

        if (scaleCo != null) StopCoroutine(scaleCo);
        scaleCo = StartCoroutine(LerpScale(target, animTime));
    }

    private IEnumerator LerpScale(float target, float t)
    {
        // ✅ If something disabled visuals mid-animation, bring them back
        ForceVisualTreeOn("LerpScale");

        Vector3 a = visual.localScale;
        Vector3 b = Vector3.one * target;

        float s = 0f;
        while (s < t)
        {
            s += Time.unscaledDeltaTime;
            if (visual) visual.localScale = Vector3.Lerp(a, b, s / t);
            yield return null;
        }

        if (visual) visual.localScale = b;
        scaleCo = null;
    }

    // -------------------------------------------------------
    // ✅ NEW: Force enable Visual + core UI pieces if something disabled them
    // -------------------------------------------------------
    private void ForceVisualTreeOn(string where)
    {
        if (!forceEnableVisualTree) return;

        // If Visual is inactive, the whole card looks "disabled" in inspector and renders nothing.
        if (visual != null && !visual.gameObject.activeSelf)
        {
            visual.gameObject.SetActive(true);
            if (logWhenForcingVisualOn)
                Debug.LogWarning($"[PortraitCard] Visual was OFF -> forced ON ({where})", this);
        }

        // Sometimes scripts disable individual components (not the GO). Ensure they're enabled.
        if (portraitImage != null && !portraitImage.enabled) portraitImage.enabled = true;
        if (nameText != null && !nameText.enabled) nameText.enabled = true;
        if (sizeText != null && !sizeText.enabled) sizeText.enabled = true;
        if (timeText != null && !timeText.enabled) timeText.enabled = true;
        if (indexBadge != null && !indexBadge.enabled) indexBadge.enabled = true;

        // If statsRow was zeroed, it can look like "missing content"
        // Do NOT force it visible always; just ensure it isn't breaking the whole card.
        if (statsRow != null && float.IsNaN(statsRow.alpha))
            statsRow.alpha = 1f;

        // Never touch outlines/lock overlay here beyond enabling the component itself
        // because those are intentionally controlled by selection/lock states.
    }
}
