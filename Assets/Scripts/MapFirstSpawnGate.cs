using System.Collections;
using UnityEngine;

public class MapFirstSpawnGate : MonoBehaviour
{
    [Header("Gate")]
    [Tooltip("Assign the LoadCharacter component in this scene. This script will enable it only after map is ready.")]
    public LoadCharacter loadCharacterToEnable;

    [Tooltip("If null, will find by tag.")]
    public string spawnTag = "PlayerSpawn";

    [Header("Timing")]
    [Range(0, 30)] public int waitFramesBeforePrewarm = 1;
    [Range(0, 30)] public int waitFramesAfterPrewarm = 2;

    [Header("Debug")]
    public bool verboseLogs = true;

    private void Awake()
    {
        // Ensure LoadCharacter doesn't spawn the player too early.
        if (loadCharacterToEnable != null)
            loadCharacterToEnable.enabled = false;
    }

    private IEnumerator Start()
    {
        for (int i = 0; i < Mathf.Max(0, waitFramesBeforePrewarm); i++)
            yield return null;

        // Find spawn point
        Transform sp = null;

        if (loadCharacterToEnable != null && loadCharacterToEnable.spawnPoint != null)
            sp = loadCharacterToEnable.spawnPoint;

        if (sp == null)
        {
            var go = GameObject.FindGameObjectWithTag(spawnTag);
            if (go != null) sp = go.transform;
        }

        if (sp == null)
        {
            Debug.LogError("[MapFirstSpawnGate] No spawn point found. LoadCharacter will be enabled anyway.");
            if (loadCharacterToEnable != null) loadCharacterToEnable.enabled = true;
            yield break;
        }

        // ✅ PREWARM: Activate the room containing the spawn point BEFORE spawning player
        bool activated = RoomCullingZone.ActivateZoneContainingPoint(sp.position);

        if (verboseLogs)
            Debug.Log($"[MapFirstSpawnGate] Prewarm at {sp.position} -> activatedZone={activated}");

        // Let activation propagate (enable objects, colliders, nav, etc.)
        for (int i = 0; i < Mathf.Max(0, waitFramesAfterPrewarm); i++)
            yield return null;

        yield return new WaitForFixedUpdate();

        // Nudge your host culler if it exists (safe call)
        try { HostRoomAutoCuller.UpdateVisibilityStatic(); } catch { }

        // ✅ Now allow player spawn
        if (loadCharacterToEnable != null)
            loadCharacterToEnable.enabled = true;
        else
            Debug.LogWarning("[MapFirstSpawnGate] loadCharacterToEnable not assigned.");

        if (verboseLogs)
            Debug.Log("[MapFirstSpawnGate] LoadCharacter enabled AFTER map prewarm.");
    }
}