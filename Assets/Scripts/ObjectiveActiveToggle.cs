using UnityEngine;

public class ObjectiveActiveToggle : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The object to enable/disable depending on the active objective.")]
    public GameObject targetObject;

    [Header("Objective Gate")]
    [Tooltip("If this objective is the CURRENT active objective, targetObject will be enabled. Leave empty to do nothing.")]
    public string requiredActiveObjectiveId = "";

    [Header("Options")]
    [Tooltip("If true, this script checks continuously every frame. Safe and simple.")]
    public bool checkEveryFrame = true;

    [Tooltip("If true, logs when the target enable state changes.")]
    public bool verboseLogs = false;

    private bool _lastAllowedState = true;
    private bool _initialized = false;

    private void Start()
    {
        ApplyState(force: true);
    }

    private void OnEnable()
    {
        if (ObjectiveManager.Instance != null)
            ObjectiveManager.Instance.OnObjectiveChanged += HandleObjectiveChanged;

        ApplyState(force: true);
    }

    private void OnDisable()
    {
        if (ObjectiveManager.Instance != null)
            ObjectiveManager.Instance.OnObjectiveChanged -= HandleObjectiveChanged;
    }

    private void Update()
    {
        if (checkEveryFrame)
            ApplyState(force: false);
    }

    private void HandleObjectiveChanged(string newObjectiveId)
    {
        ApplyState(force: true);
    }

    private bool IsAllowed()
    {
        if (targetObject == null) return false;

        // No objective assigned = do nothing / always allow
        if (string.IsNullOrWhiteSpace(requiredActiveObjectiveId))
            return true;

        if (ObjectiveManager.Instance == null)
            return false;

        return ObjectiveManager.Instance.IsObjectiveActive(requiredActiveObjectiveId);
    }

    private void ApplyState(bool force)
    {
        if (targetObject == null) return;

        bool allowed = IsAllowed();

        if (!_initialized || force || allowed != _lastAllowedState)
        {
            _initialized = true;
            _lastAllowedState = allowed;

            if (targetObject.activeSelf != allowed)
                targetObject.SetActive(allowed);

            if (verboseLogs)
            {
                Debug.Log(
                    $"[ObjectiveActiveToggle] Target='{targetObject.name}' " +
                    $"requiredObjective='{requiredActiveObjectiveId}' " +
                    $"allowed={allowed}"
                );
            }
        }
    }
}