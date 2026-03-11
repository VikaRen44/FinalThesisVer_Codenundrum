using UnityEngine;
using UnityEngine.InputSystem;

public class UISavePointHotkey : MonoBehaviour
{
    [Header("Save Menu Ref")]
    [Tooltip("Drag your SaveLoadMenuUI here.")]
    public SaveLoadMenuUI saveLoadMenuUI;

    [Header("Input")]
    [Tooltip("Optional: assign a UI/Save or Player/Tab action from the Input System. If empty, keyboard Tab fallback will be used.")]
    public InputActionReference openSaveMenuAction;

    [Header("Behavior")]
    [Tooltip("If true, opening is blocked while dialogue is playing.")]
    public bool blockDuringDialogue = true;

    [Tooltip("If true, prevents trying to open again while the save menu root is already active.")]
    public bool blockIfMenuAlreadyOpen = true;

    private void OnEnable()
    {
        if (openSaveMenuAction != null)
            openSaveMenuAction.action.Enable();
    }

    private void OnDisable()
    {
        if (openSaveMenuAction != null)
            openSaveMenuAction.action.Disable();
    }

    private void Update()
    {
        if (saveLoadMenuUI == null) return;

        if (blockDuringDialogue &&
            SimpleDialogueManager.Instance != null &&
            SimpleDialogueManager.Instance.IsPlaying)
        {
            return;
        }

        if (blockIfMenuAlreadyOpen &&
            saveLoadMenuUI.root != null &&
            saveLoadMenuUI.root.activeInHierarchy)
        {
            return;
        }

        bool pressed = false;

        // Preferred: Input System action
        if (openSaveMenuAction != null)
        {
            pressed = openSaveMenuAction.action.WasPressedThisFrame();
        }
        else
        {
            // Fallback: direct keyboard Tab
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
                pressed = true;
        }

        if (!pressed) return;

        saveLoadMenuUI.OpenMenu();
    }
}