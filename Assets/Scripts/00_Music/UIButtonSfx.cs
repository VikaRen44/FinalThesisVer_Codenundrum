using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class UIButtonSfx : MonoBehaviour, IPointerEnterHandler
{
    [Header("Which sounds?")]
    public bool playHover = true;
    public bool playClick = true;

    [Header("Overrides (optional)")]
    public AudioClip hoverOverride;
    public AudioClip clickOverride;

    [Range(0f, 1f)] public float hoverVolumeOverride = 1f;
    public bool useHoverVolumeOverride = false;

    [Range(0f, 1f)] public float clickVolumeOverride = 1f;
    public bool useClickVolumeOverride = false;

    [Header("Safety")]
    [Tooltip("If true, hover only plays when the Button is interactable.")]
    public bool requireInteractableForHover = true;

    private Button _button;

    private void Awake()
    {
        _button = GetComponent<Button>();
    }

    private void OnEnable()
    {
        if (playClick && _button != null)
        {
            _button.onClick.AddListener(HandleClick);
        }
    }

    private void OnDisable()
    {
        if (_button != null)
        {
            _button.onClick.RemoveListener(HandleClick);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!playHover) return;
        if (UISfxManager.Instance == null) return;

        if (requireInteractableForHover && _button != null && !_button.interactable)
            return;

        if (hoverOverride != null)
        {
            float vol = useHoverVolumeOverride ? hoverVolumeOverride : 1f;
            UISfxManager.Instance.PlayClip(hoverOverride, vol);
        }
        else
        {
            float? vol = useHoverVolumeOverride ? (float?)hoverVolumeOverride : null;
            UISfxManager.Instance.PlayHover(vol);
        }
    }

    private void HandleClick()
    {
        if (!playClick) return;
        if (UISfxManager.Instance == null) return;

        if (clickOverride != null)
        {
            float vol = useClickVolumeOverride ? clickVolumeOverride : 1f;
            UISfxManager.Instance.PlayClip(clickOverride, vol);
        }
        else
        {
            float? vol = useClickVolumeOverride ? (float?)clickVolumeOverride : null;
            UISfxManager.Instance.PlayClick(vol);
        }
    }
}
