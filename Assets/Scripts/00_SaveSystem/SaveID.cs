using UnityEngine;
using System;

[DisallowMultipleComponent]
public class SaveID : MonoBehaviour
{
    [Header("ID")]
    [Tooltip("If enabled, you control the ID manually (recommended for singletons like ObjectiveManager).")]
    public bool useManualId = false;

    [Tooltip("Unique stable ID. If useManualId is ON, set this yourself.")]
    [SerializeField] private string id;

    public string ID => id;

    private void OnValidate()
    {
        if (useManualId)
        {
            if (string.IsNullOrWhiteSpace(id))
                id = "UNSET_ID";
            return;
        }

        if (string.IsNullOrWhiteSpace(id))
            id = Guid.NewGuid().ToString("N");

        if (IsDuplicateIdInScene(id, this))
            id = Guid.NewGuid().ToString("N");
    }

    private void Awake()
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            if (useManualId) id = "UNSET_ID";
            else id = Guid.NewGuid().ToString("N");
        }

        if (!string.IsNullOrWhiteSpace(id) && id != "UNSET_ID")
        {
            var all = FindAllSaveIDs(includeInactive: true);
            int dup = 0;

            for (int i = 0; i < all.Length; i++)
            {
                var other = all[i];
                if (other == null || other == this) continue;
                if (other.ID == this.ID) dup++;
            }

            if (dup > 0)
                Debug.LogWarning($"[SaveID] Duplicate ID detected: '{id}' on '{name}'. This can break restores.");
        }
    }

    private static bool IsDuplicateIdInScene(string checkId, SaveID self)
    {
        if (string.IsNullOrWhiteSpace(checkId)) return false;

        var all = FindAllSaveIDs(includeInactive: true);
        for (int i = 0; i < all.Length; i++)
        {
            var other = all[i];
            if (other == null || other == self) continue;
            if (other.ID == checkId) return true;
        }
        return false;
    }

    private static SaveID[] FindAllSaveIDs(bool includeInactive)
    {
#if UNITY_2023_1_OR_NEWER
        return UnityEngine.Object.FindObjectsByType<SaveID>(
            includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );
#else
        return UnityEngine.Object.FindObjectsOfType<SaveID>(includeInactive);
#endif
    }
}
