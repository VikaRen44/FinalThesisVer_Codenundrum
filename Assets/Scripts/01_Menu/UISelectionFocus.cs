using UnityEngine;
using UnityEngine.EventSystems;

public class UISelectionFocus : MonoBehaviour
{
    [Header("Focus Targets")]
    [Tooltip("First selectable in Settings (ex: Master slider, Apply button).")]
    public GameObject firstSelectedInThisUI;

    [Tooltip("Where selection returns when this UI closes (ex: Settings button on main menu).")]
    public GameObject returnSelectedOnClose;

    private GameObject _previousSelected;

    private void OnEnable()
    {
        var es = EventSystem.current;
        if (es == null) return;

        // Save what was selected (main menu button)
        _previousSelected = es.currentSelectedGameObject;

        // Clear then set to force EventSystem to update properly
        es.SetSelectedGameObject(null);
        if (firstSelectedInThisUI != null)
            es.SetSelectedGameObject(firstSelectedInThisUI);
    }

    private void OnDisable()
    {
        var es = EventSystem.current;
        if (es == null) return;

        es.SetSelectedGameObject(null);

        // Prefer explicit return target, else restore previous
        if (returnSelectedOnClose != null)
            es.SetSelectedGameObject(returnSelectedOnClose);
        else if (_previousSelected != null)
            es.SetSelectedGameObject(_previousSelected);
    }
}
