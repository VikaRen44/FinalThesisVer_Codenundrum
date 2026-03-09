using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UIDragPoint : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    public bool lockPosition = false;
    public RectTransform dragArea;
    public UIPathManager pathManager;

    [Header("Debug")]
    public bool verboseLogs = false;

    private RectTransform rectTransform;
    private Canvas _rootCanvas;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();

        if (dragArea == null)
            dragArea = GetComponentInParent<RectTransform>();

        if (pathManager == null)
            pathManager = GetComponentInParent<UIPathManager>();

        _rootCanvas = GetComponentInParent<Canvas>();

        if (verboseLogs)
        {
            Debug.Log($"[UIDragPoint] Awake on '{name}'. dragArea='{(dragArea ? dragArea.name : "NULL")}', " +
                      $"canvas='{(_rootCanvas ? _rootCanvas.renderMode.ToString() : "NULL")}'");
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (lockPosition) return;
        UpdatePosition(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (lockPosition) return;
        UpdatePosition(eventData);
    }

    private void UpdatePosition(PointerEventData eventData)
    {
        if (rectTransform == null || dragArea == null)
            return;

        // 1) Pick the correct event camera:
        // - Screen Space Overlay => null is correct
        // - Screen Space Camera / World Space => MUST use the canvas/world camera
        Camera cam = eventData.pressEventCamera;

        if (cam == null)
        {
            // enterEventCamera is often correct in Camera/World Space when pressEventCamera is null
            cam = eventData.enterEventCamera;
        }

        // If still null but canvas is NOT overlay, try canvas.worldCamera
        if (cam == null && _rootCanvas != null && _rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            cam = _rootCanvas.worldCamera;
        }

        // 2) Convert pointer position into dragArea local space
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                dragArea,
                eventData.position,
                cam,
                out Vector2 localPoint))
        {
            // 3) Apply anchored position (this assumes rectTransform is a child of dragArea or same space)
            rectTransform.anchoredPosition = localPoint;

            if (pathManager != null)
                pathManager.RebuildPath();
        }
        else
        {
            if (verboseLogs)
                Debug.LogWarning($"[UIDragPoint] ScreenPointToLocalPointInRectangle FAILED on '{name}'. " +
                                 $"cam={(cam ? cam.name : "null")} dragArea='{dragArea.name}'");
        }
    }
}
