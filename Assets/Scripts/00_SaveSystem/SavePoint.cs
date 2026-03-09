using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Collider))]
public class SavePoint : MonoBehaviour
{
    [Header("Save Menu Ref")]
    [Tooltip("Drag your SaveLoadMenuUI root here.")]
    public SaveLoadMenuUI saveLoadMenuUI;

    [Header("Prompt UI (like WorldInteractable)")]
    [Tooltip("Optional: a world-space UI prompt object to show when player is in range.")]
    public GameObject promptObject;

    [Header("Interaction (New Input System)")]
    [Tooltip("Assign Player/Interact from your Input Actions asset.")]
    public InputActionReference interactAction;

    [Tooltip("If true, interaction is blocked while dialogue is playing.")]
    public bool blockDuringDialogue = true;

    private bool playerInRange;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    private void Start()
    {
        if (promptObject != null)
            promptObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (interactAction != null)
            interactAction.action.Enable();
    }

    private void OnDisable()
    {
        if (interactAction != null)
            interactAction.action.Disable();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        playerInRange = true;

        if (promptObject != null)
            promptObject.SetActive(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        playerInRange = false;

        if (promptObject != null)
            promptObject.SetActive(false);
    }

    private void Update()
    {
        if (!playerInRange) return;
        if (saveLoadMenuUI == null) return;

        if (blockDuringDialogue && SimpleDialogueManager.Instance != null && SimpleDialogueManager.Instance.IsPlaying)
            return;

        if (interactAction != null && interactAction.action.WasPressedThisFrame())
        {
            saveLoadMenuUI.OpenMenu();
        }
    }
}
