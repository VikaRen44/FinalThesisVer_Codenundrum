using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Collider))]
public class AssessmentTrigger : MonoBehaviour
{
    [Header("Assessment UI Ref")]
    public AssessmentUI assessmentUI;

    [Header("Prompt UI")]
    public GameObject promptObject;

    [Header("Interaction")]
    public InputActionReference interactAction;
    public bool blockDuringDialogue = true;

    [Header("One Shot")]
    public bool oneShot = true;

    private bool playerInRange;
    private bool _used;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    private void Start()
    {
        if (promptObject != null) promptObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (interactAction != null) interactAction.action.Enable();
    }

    private void OnDisable()
    {
        if (interactAction != null) interactAction.action.Disable();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (oneShot && _used) return;

        playerInRange = true;
        if (promptObject != null) promptObject.SetActive(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        playerInRange = false;
        if (promptObject != null) promptObject.SetActive(false);
    }

    private void Update()
    {
        if (!playerInRange) return;
        if (oneShot && _used) return;
        if (assessmentUI == null) return;

        if (blockDuringDialogue && SimpleDialogueManager.Instance != null && SimpleDialogueManager.Instance.IsPlaying)
            return;

        if (interactAction != null && interactAction.action.WasPressedThisFrame())
        {
            _used = true;
            if (promptObject != null) promptObject.SetActive(false);

            assessmentUI.OpenAssessment(); // ✅ this calls musicOverride + begins assessment
        }
    }
}
