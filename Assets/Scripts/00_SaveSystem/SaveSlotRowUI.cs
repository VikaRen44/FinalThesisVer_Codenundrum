using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using UnityEngine.EventSystems;

public class SaveSlotRowUI : MonoBehaviour, IPointerClickHandler, ISubmitHandler, ISelectHandler
{
    [Header("UI")]
    public Button button;

    public TMP_Text slotNumberText;
    public TMP_Text playerNameText;
    public TMP_Text saveLabelText;
    public TMP_Text timeText;

    public RawImage thumbnail;

    [Header("Safety")]
    [Tooltip("Prevents double-trigger if both Button.onClick and PointerClick fire in the same frame.")]
    public bool debounceSameFrame = true;

    [Tooltip("Debug logs to verify whether the slot is receiving pointer/submit and whether the action is null.")]
    public bool debugClicks = false;

    [Header("Controller Navigation Fix")]
    [Tooltip("Forces Button.navigation = Explicit so parent menu can wire Up/Down correctly.")]
    public bool forceExplicitNavigation = true;

    private Action _onClick;
    private bool _hooked;
    private int _lastInvokeFrame = -9999;

    private void Awake()
    {
        if (button == null)
            button = GetComponentInChildren<Button>(true);

        HookButtonOnce();
        ApplyNavigationSettings();
    }

    private void OnEnable()
    {
        HookButtonOnce();
        ApplyNavigationSettings();
    }

    private void OnDestroy()
    {
        if (button != null && _hooked)
        {
            button.onClick.RemoveListener(InvokeActionSafe);
            _hooked = false;
        }
    }

    private void HookButtonOnce()
    {
        if (_hooked) return;
        if (button == null) return;

        button.onClick.AddListener(InvokeActionSafe);
        _hooked = true;
    }

    private void ApplyNavigationSettings()
    {
        if (button == null) return;

        var nav = button.navigation;
        nav.mode = forceExplicitNavigation ? Navigation.Mode.Explicit : Navigation.Mode.Automatic;
        nav.wrapAround = false;
        button.navigation = nav;
    }

    public Button GetButton()
    {
        return button;
    }

    private void InvokeActionSafe()
    {
        if (button != null && !button.IsInteractable())
        {
            if (debugClicks) Debug.Log($"[SaveSlotRowUI] '{name}' click ignored (button not interactable).");
            return;
        }

        if (debounceSameFrame)
        {
            int f = Time.frameCount;
            if (_lastInvokeFrame == f)
            {
                if (debugClicks) Debug.Log($"[SaveSlotRowUI] '{name}' click debounced (same frame).");
                return;
            }
            _lastInvokeFrame = f;
        }

        if (debugClicks)
            Debug.Log($"[SaveSlotRowUI] '{name}' InvokeActionSafe() actionNull={_onClick == null}");

        _onClick?.Invoke();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
            return;

        if (debugClicks)
            Debug.Log($"[SaveSlotRowUI] '{name}' OnPointerClick()");

        if (EventSystem.current != null && button != null && button.gameObject.activeInHierarchy)
        {
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(button.gameObject);
        }

        InvokeActionSafe();
    }

    public void OnSubmit(BaseEventData eventData)
    {
        if (debugClicks)
            Debug.Log($"[SaveSlotRowUI] '{name}' OnSubmit()");

        if (EventSystem.current != null && button != null && button.gameObject.activeInHierarchy)
        {
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(button.gameObject);
        }

        InvokeActionSafe();
    }

    public void OnSelect(BaseEventData eventData)
    {
        if (debugClicks)
            Debug.Log($"[SaveSlotRowUI] '{name}' OnSelect()");
    }

    public void Bind(int slotIndex, SaveData data, bool clickable)
    {
        if (slotNumberText) slotNumberText.text = slotIndex.ToString();

        if (data == null)
        {
            if (playerNameText) playerNameText.text = "(Empty)";
            if (saveLabelText) saveLabelText.text = "";
            if (timeText) timeText.text = "";
            if (thumbnail) thumbnail.texture = null;
        }
        else
        {
            if (playerNameText) playerNameText.text = string.IsNullOrEmpty(data.playerName) ? "(No Name)" : data.playerName;
            if (saveLabelText) saveLabelText.text = data.saveLabel;

            if (timeText)
            {
                var dt = DateTimeOffset.FromUnixTimeSeconds(data.realWorldUnixSeconds).LocalDateTime;
                timeText.text = dt.ToString("yyyy-MM-dd HH:mm");
            }

            if (thumbnail)
            {
                var tex = SaveSystem.LoadThumbnail(slotIndex);
                thumbnail.texture = tex;
            }
        }

        if (button) button.interactable = clickable;

        ApplyNavigationSettings();
    }

    public void SetOnClick(Action action)
    {
        _onClick = action;

        if (debugClicks)
            Debug.Log($"[SaveSlotRowUI] '{name}' SetOnClick() actionNull={_onClick == null}");
    }
}