using UnityEngine;

public class SelectionModeDisabler : MonoBehaviour
{
    [Header("Selection Scene Mode")]
    [Tooltip("If true, disables gameplay components when this prefab is used as a DISPLAY model.")]
    public bool selectionMode = true;

    [Header("Disable These (auto found if left empty)")]
    public CharacterController characterController;
    public MonoBehaviour[] scriptsToDisable; // PlayerMovement, combat, etc.

    private void Awake()
    {
        if (selectionMode)
            ApplySelectionMode();
    }

    [ContextMenu("Apply Selection Mode Now")]
    public void ApplySelectionMode()
    {
        if (!characterController) characterController = GetComponent<CharacterController>();
        if (characterController) characterController.enabled = false;

        if (scriptsToDisable != null)
        {
            for (int i = 0; i < scriptsToDisable.Length; i++)
            {
                if (scriptsToDisable[i]) scriptsToDisable[i].enabled = false;
            }
        }
    }

    [ContextMenu("Apply Gameplay Mode Now")]
    public void ApplyGameplayMode()
    {
        if (!characterController) characterController = GetComponent<CharacterController>();
        if (characterController) characterController.enabled = true;

        if (scriptsToDisable != null)
        {
            for (int i = 0; i < scriptsToDisable.Length; i++)
            {
                if (scriptsToDisable[i]) scriptsToDisable[i].enabled = true;
            }
        }
    }
}
