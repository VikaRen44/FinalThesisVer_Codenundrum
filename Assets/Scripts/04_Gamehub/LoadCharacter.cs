using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using System.Collections;

public class LoadCharacter : MonoBehaviour
{
    [Header("Character Prefabs (used only if no player exists)")]
    public GameObject[] characterPrefabs;

    [Header("Spawn Point")]
    public Transform spawnPoint;
    public string spawnTag = "PlayerSpawn";

    [Header("Inject Input Actions into PlayerMovement")]
    public InputActionReference moveAction;
    public InputActionReference sprintAction;
    public InputActionReference interactAction;
    public InputActionReference openSettingsAction;

    [Header("Existing Player Behaviour")]
    public bool reuseExistingPlayerIfFound = true;
    public bool teleportExistingPlayerToSpawn = true;

    [Header("Hub Return Pose (NO PlayerPrefs)")]
    public string hubSceneName = "04_Gamehub";
    public bool applySavedHubPoseInHub = true;
    [Range(0, 10)] public int hubPoseApplyDelayFrames = 3;

    // ✅ NEW: Generic Return Pose (world return from minigames/battles, no PlayerPrefs)
    [Header("World Return Pose (NEW, NO PlayerPrefs)")]
    [Tooltip("If true, applies return pose for ANY scene (not just hub) when a return pose exists.")]
    public bool applyReturnPoseInAnyScene = true;

    [Tooltip("Frames to wait before applying return pose (lets spawners/scene settle).")]
    [Range(0, 10)] public int returnPoseApplyDelayFrames = 3;

    [Header("Debug")]
    public bool verboseLogs = true;

    private GameObject spawnedCharacter;
    private bool _saveLoadInProgress;

    // =========================================================
    // ✅ HUB POSE (IN-MEMORY, NO PlayerPrefs)
    // =========================================================
    private static bool s_hasHubPose;
    private static Vector3 s_hubPos;
    private static Quaternion s_hubRot;
    private static string s_hubSceneName;

    public static void SaveHubPoseNow(Transform player, string hubSceneName)
    {
        if (player == null) return;
        s_hasHubPose = true;
        s_hubPos = player.position;
        s_hubRot = player.rotation;
        s_hubSceneName = hubSceneName;
        Debug.Log($"[LoadCharacter] ✅ Saved HUB pose in-memory for scene '{s_hubSceneName}' at {s_hubPos}");
    }

    public static void ClearHubPose()
    {
        s_hasHubPose = false;
        s_hubSceneName = null;
    }

    private static bool TryGetHubPoseForScene(string sceneName, out Vector3 pos, out Quaternion rot)
    {
        pos = default;
        rot = default;

        if (!s_hasHubPose) return false;
        if (string.IsNullOrEmpty(s_hubSceneName)) return false;
        if (!string.Equals(sceneName, s_hubSceneName, System.StringComparison.Ordinal)) return false;

        pos = s_hubPos;
        rot = s_hubRot;
        return true;
    }

    // =========================================================
    // ✅ GENERIC RETURN POSE (IN-MEMORY, NO PlayerPrefs)
    // Used for returning from ANY minigame/battle scene back to a world scene.
    // =========================================================
    private static bool s_hasReturnPose;
    private static Vector3 s_returnPos;
    private static Quaternion s_returnRot;
    private static string s_returnSceneName;

    /// <summary>
    /// Call this RIGHT BEFORE you load a minigame/battle scene.
    /// Example:
    /// LoadCharacter.SaveReturnPoseNow(player.transform, SceneManager.GetActiveScene().name);
    /// </summary>
    public static void SaveReturnPoseNow(Transform player, string sceneName)
    {
        if (player == null) return;
        if (string.IsNullOrEmpty(sceneName)) return;

        s_hasReturnPose = true;
        s_returnPos = player.position;
        s_returnRot = player.rotation;
        s_returnSceneName = sceneName;

        Debug.Log($"[LoadCharacter] ✅ Saved RETURN pose in-memory for scene '{s_returnSceneName}' at {s_returnPos}");
    }

    public static void ClearReturnPose()
    {
        s_hasReturnPose = false;
        s_returnSceneName = null;
    }

    private static bool TryGetReturnPoseForScene(string sceneName, out Vector3 pos, out Quaternion rot)
    {
        pos = default;
        rot = default;

        if (!s_hasReturnPose) return false;
        if (string.IsNullOrEmpty(s_returnSceneName)) return false;
        if (!string.Equals(sceneName, s_returnSceneName, System.StringComparison.Ordinal)) return false;

        pos = s_returnPos;
        rot = s_returnRot;
        return true;
    }

    // =========================================================
    // ✅ SELECTED CHARACTER (IN-MEMORY, NO PlayerPrefs)
    // =========================================================
    private static bool s_hasSelectedCharacter;
    private static int s_selectedCharacterIndex = 0;

    public static void SaveSelectedCharacterIndex(int index)
    {
        s_hasSelectedCharacter = true;
        s_selectedCharacterIndex = Mathf.Max(0, index);
        Debug.Log($"[LoadCharacter] ✅ Saved selectedCharacterIndex in-memory = {s_selectedCharacterIndex}");
    }

    public static bool TryGetSelectedCharacterIndex(out int index)
    {
        index = 0;
        if (!s_hasSelectedCharacter) return false;
        index = s_selectedCharacterIndex;
        return true;
    }

    public static void ClearSelectedCharacterMemory()
    {
        s_hasSelectedCharacter = false;
        s_selectedCharacterIndex = 0;
    }

    /// <summary>
    /// Robustly infers character index by matching player object name (or root name) vs prefab names.
    /// Returns -1 if cannot match.
    /// </summary>
    public static int InferCharacterIndexFromPlayer(GameObject playerObj, GameObject[] prefabs)
    {
        if (playerObj == null || prefabs == null || prefabs.Length == 0) return -1;

        string have = playerObj.name.Replace("(Clone)", "").Trim();

        for (int i = 0; i < prefabs.Length; i++)
        {
            if (prefabs[i] == null) continue;
            string want = prefabs[i].name.Trim();
            if (string.Equals(want, have, System.StringComparison.Ordinal))
                return i;
        }

        var root = playerObj.transform.root;
        if (root != null)
        {
            have = root.name.Replace("(Clone)", "").Trim();
            for (int i = 0; i < prefabs.Length; i++)
            {
                if (prefabs[i] == null) continue;
                string want = prefabs[i].name.Trim();
                if (string.Equals(want, have, System.StringComparison.Ordinal))
                    return i;
            }
        }

        return -1;
    }

    public static void SaveSelectedCharacterFromPlayer(GameObject playerObj, GameObject[] prefabs)
    {
        int idx = InferCharacterIndexFromPlayer(playerObj, prefabs);
        if (idx >= 0) SaveSelectedCharacterIndex(idx);
        else Debug.LogWarning($"[LoadCharacter] Could not match player '{playerObj?.name}' to prefabs. In-memory selection unchanged.");
    }

    // =========================================================

    private void Start()
    {
        if (verboseLogs) Debug.Log("[LoadCharacter] Start()");

        ResolveSpawnPoint();
        if (spawnPoint == null)
        {
            Debug.LogError("[LoadCharacter] No spawnPoint assigned and no object tagged 'PlayerSpawn' found.");
            return;
        }

        _saveLoadInProgress = (SaveGameManager.Instance != null && SaveGameManager.Instance.IsLoadInProgress);

        // ❗ CRITICAL FIX (kept):
        // When a save-load is in progress, DO NOT infer selection from any existing Player yet.
        if (!_saveLoadInProgress)
        {
            var existingEarly = FindAnyExistingPlayer();
            if (existingEarly != null)
                SaveSelectedCharacterFromPlayer(existingEarly, characterPrefabs);
        }

        int desiredIndex = GetDesiredCharacterIndex(_saveLoadInProgress);

        // ✅ Decide if we SHOULD override spawn with a remembered pose
        bool hasReturnPoseForThisScene = false;
        bool hasHubPoseForThisScene = false;

        string currentScene = SceneManager.GetActiveScene().name;

        if (!_saveLoadInProgress && applyReturnPoseInAnyScene)
        {
            hasReturnPoseForThisScene = TryGetReturnPoseForScene(currentScene, out _, out _);
        }

        if (!_saveLoadInProgress && applySavedHubPoseInHub && string.Equals(currentScene, hubSceneName, System.StringComparison.Ordinal))
        {
            hasHubPoseForThisScene = TryGetHubPoseForScene(currentScene, out _, out _);
        }

        // If we have a return pose (or hub pose), we must NOT let default spawn teleport override it.
        bool shouldAvoidSpawnTeleportBecausePoseWillApply = hasReturnPoseForThisScene || hasHubPoseForThisScene;

        var existing = FindAnyExistingPlayer();
        if (existing != null && reuseExistingPlayerIfFound)
        {
            if (verboseLogs) Debug.Log($"[LoadCharacter] Found existing player: {existing.name}");

            // If loading: SAVE decides model and we do NOT teleport to spawn
            if (_saveLoadInProgress)
            {
                if (!DoesExistingMatchIndex(existing, desiredIndex))
                {
                    if (verboseLogs) Debug.Log($"[LoadCharacter] Existing player does NOT match desired index {desiredIndex}. Respawning correct model.");
                    ForceSpawnCharacterForSave(desiredIndex);
                    return;
                }

                ForcePlayerIdentity(existing);
                RebindPlayer(existing);
                ForceVisible(existing);
                DestroyDuplicatePlayers(existing);

                SaveSelectedCharacterIndex(desiredIndex);

                // NOTE: During save-load, we do not apply return/hub pose here.
                // Save system should set player transform itself.
                return;
            }

            // Normal gameplay reuse
            ForcePlayerIdentity(existing);

            // ✅ IMPORTANT: if returning from minigame/battle and we have a pose, do NOT teleport to spawn.
            if (teleportExistingPlayerToSpawn && !shouldAvoidSpawnTeleportBecausePoseWillApply)
                existing.transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);

            SaveSelectedCharacterFromPlayer(existing, characterPrefabs);

            RebindPlayer(existing);
            ForceVisible(existing);
            DestroyDuplicatePlayers(existing);

            // ✅ Apply return pose first (any scene), then hub pose (hub only) as fallback
            TryApplySavedReturnPose(existing);
            TryApplySavedHubPose(existing);
            return;
        }

        // No existing -> spawn desired
        SpawnCharacter(desiredIndex);
        DestroyDuplicatePlayers(spawnedCharacter);

        SaveSelectedCharacterIndex(desiredIndex);

        // ✅ Apply return pose first (any scene), then hub pose (hub only) as fallback
        TryApplySavedReturnPose(spawnedCharacter);
        TryApplySavedHubPose(spawnedCharacter);
    }

    public GameObject ForceSpawnCharacterForSave(int desiredIndex)
    {
        _saveLoadInProgress = true;

        ResolveSpawnPoint();
        if (spawnPoint == null)
        {
            Debug.LogError("[LoadCharacter] ForceSpawnCharacterForSave: No spawnPoint found.");
            return null;
        }

        DestroyAllPlayersNow();

        if (characterPrefabs == null || characterPrefabs.Length == 0)
        {
            Debug.LogError("[LoadCharacter] ForceSpawnCharacterForSave: characterPrefabs empty.");
            return null;
        }

        if (desiredIndex < 0 || desiredIndex >= characterPrefabs.Length)
            desiredIndex = 0;

        SpawnCharacter(desiredIndex);
        DestroyDuplicatePlayers(spawnedCharacter);

        SaveSelectedCharacterIndex(desiredIndex);

        return spawnedCharacter;
    }

    private int GetDesiredCharacterIndex(bool saveLoadInProgress)
    {
        int desiredIndex = 0;

        // ✅ If loading: prefer SaveGameManager pending snapshot (if you have it)
        if (saveLoadInProgress && SaveGameManager.Instance != null)
        {
            if (SaveGameManager.Instance.TryGetPendingSelectedCharacter(out int pendingIdx))
            {
                desiredIndex = pendingIdx;
                if (verboseLogs) Debug.Log($"[LoadCharacter] Using pending save selectedCharacterIndex={desiredIndex}");

                SaveSelectedCharacterIndex(desiredIndex);
                return desiredIndex;
            }

            // ✅ HARD FALLBACK (NO TryGetPendingSlot needed):
            // Use SaveSystem cached selection that was set when the user clicked Load.
            if (SaveSystem.TryGetPendingLoadSelection(out int slot, out int cachedIdx))
            {
                desiredIndex = cachedIdx;
                if (verboseLogs) Debug.Log($"[LoadCharacter] Pending index not ready. Using SaveSystem cached selection -> slot={slot}, selectedCharacterIndex={desiredIndex}");

                SaveSelectedCharacterIndex(desiredIndex);
                return desiredIndex;
            }
        }

        // ✅ Normal flow: use in-memory selection
        if (s_hasSelectedCharacter)
        {
            desiredIndex = Mathf.Clamp(s_selectedCharacterIndex, 0, (characterPrefabs != null ? characterPrefabs.Length - 1 : 0));
            if (verboseLogs) Debug.Log($"[LoadCharacter] Using in-memory selectedCharacterIndex={desiredIndex}");
            return desiredIndex;
        }

        // Final fallback: infer from existing player BEFORE defaulting
        var existing = FindAnyExistingPlayer();
        if (existing != null)
        {
            SaveSelectedCharacterFromPlayer(existing, characterPrefabs);
            if (s_hasSelectedCharacter)
            {
                desiredIndex = Mathf.Clamp(s_selectedCharacterIndex, 0, (characterPrefabs != null ? characterPrefabs.Length - 1 : 0));
                if (verboseLogs) Debug.Log($"[LoadCharacter] Inferred selectedCharacterIndex from existing player = {desiredIndex}");
                return desiredIndex;
            }
        }

        if (verboseLogs) Debug.Log("[LoadCharacter] No save + no in-memory selection. Falling back to prefab index 0.");
        return 0;
    }

    private bool DoesExistingMatchIndex(GameObject existing, int desiredIndex)
    {
        if (existing == null) return false;

        if (characterPrefabs == null || characterPrefabs.Length == 0) return true;
        if (desiredIndex < 0 || desiredIndex >= characterPrefabs.Length) desiredIndex = 0;

        var prefab = characterPrefabs[desiredIndex];
        if (prefab == null) return true;

        string want = prefab.name.Trim();
        string have = existing.name.Replace("(Clone)", "").Trim();

        return string.Equals(want, have, System.StringComparison.Ordinal);
    }

    private void DestroyAllPlayersNow()
    {
        var all = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (all == null) return;

        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null) continue;
            if (verboseLogs) Debug.Log($"[LoadCharacter] Destroying player for respawn: {all[i].name}");
            Destroy(all[i].gameObject);
        }
    }

    private void ResolveSpawnPoint()
    {
        if (spawnPoint != null) return;
        var go = GameObject.FindGameObjectWithTag(spawnTag);
        if (go != null) spawnPoint = go.transform;
    }

    private GameObject FindAnyExistingPlayer()
    {
        var tagged = GameObject.FindGameObjectWithTag("Player");
        if (tagged != null) return tagged;

        var pms = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (pms != null && pms.Length > 0 && pms[0] != null)
            return pms[0].gameObject;

        return null;
    }

    private void SpawnCharacter(int index)
    {
        if (characterPrefabs == null || characterPrefabs.Length == 0)
        {
            Debug.LogError("[LoadCharacter] characterPrefabs is empty.");
            return;
        }

        if (index < 0 || index >= characterPrefabs.Length)
            index = 0;

        if (characterPrefabs[index] == null)
        {
            Debug.LogError($"[LoadCharacter] characterPrefabs[{index}] is NULL.");
            return;
        }

        spawnedCharacter = Instantiate(characterPrefabs[index], spawnPoint.position, spawnPoint.rotation);

        if (verboseLogs) Debug.Log($"[LoadCharacter] Spawned: {spawnedCharacter.name} (index={index})");

        ForcePlayerIdentity(spawnedCharacter);

        var disabler = spawnedCharacter.GetComponent<SelectionModeDisabler>();
        if (disabler != null) disabler.ApplyGameplayMode();

        RebindPlayer(spawnedCharacter);
        ForceVisible(spawnedCharacter);
    }

    private void ForcePlayerIdentity(GameObject playerObj)
    {
        if (playerObj == null) return;
        if (playerObj.tag != "Player")
            playerObj.tag = "Player";
    }

    private void DestroyDuplicatePlayers(GameObject keep)
    {
        var all = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (all == null || all.Length <= 1) return;

        foreach (var pm in all)
        {
            if (pm == null) continue;
            if (keep != null && pm.gameObject == keep) continue;

            if (verboseLogs) Debug.Log($"[LoadCharacter] Destroying duplicate player: {pm.name}");
            Destroy(pm.gameObject);
        }
    }

    private void RebindPlayer(GameObject playerObj)
    {
        if (playerObj == null) return;

        var pm = playerObj.GetComponent<PlayerMovement>();
        if (pm != null)
        {
            pm.moveAction = moveAction;
            pm.sprintAction = sprintAction;
            pm.interactAction = interactAction;
            pm.openSettingsAction = openSettingsAction;

            pm.canMove = true;

            EnableAction(pm.moveAction);
            EnableAction(pm.sprintAction);
            EnableAction(pm.interactAction);
            EnableAction(pm.openSettingsAction);

            if (verboseLogs) Debug.Log("[LoadCharacter] Rebound actions + forced canMove=true");
        }
    }

    private void ForceVisible(GameObject playerObj)
    {
        if (playerObj == null) return;

        if (!playerObj.activeSelf) playerObj.SetActive(true);

        var renderers = playerObj.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
            if (r != null) r.enabled = true;
    }

    private void EnableAction(InputActionReference a)
    {
        if (a == null || a.action == null) return;
        if (!a.action.enabled) a.action.Enable();
    }

    // =========================================================
    // ✅ APPLY RETURN POSE (ANY SCENE)
    // Priority: ReturnPose -> HubPose
    // =========================================================
    private void TryApplySavedReturnPose(GameObject playerObj)
    {
        if (!applyReturnPoseInAnyScene) return;
        if (playerObj == null) return;

        if (_saveLoadInProgress) return;

        string scene = SceneManager.GetActiveScene().name;

        if (TryGetReturnPoseForScene(scene, out var pos, out var rot))
        {
            if (verboseLogs) Debug.Log($"[LoadCharacter] ✅ Return pose found (in-memory) for scene '{scene}'. Applying after {returnPoseApplyDelayFrames} frames.");
            StartCoroutine(ApplyPoseAfterFrames(playerObj.transform, pos, rot, returnPoseApplyDelayFrames, label: "RETURN"));

            // Consume so it doesn't keep forcing every time you re-enter the scene later
            ClearReturnPose();
        }
    }

    private void TryApplySavedHubPose(GameObject playerObj)
    {
        if (!applySavedHubPoseInHub) return;
        if (playerObj == null) return;

        string scene = SceneManager.GetActiveScene().name;
        if (!string.Equals(scene, hubSceneName, System.StringComparison.Ordinal))
            return;

        if (_saveLoadInProgress) return;

        if (TryGetHubPoseForScene(scene, out var pos, out var rot))
        {
            if (verboseLogs) Debug.Log($"[LoadCharacter] ✅ Hub pose found (in-memory). Applying after {hubPoseApplyDelayFrames} frames.");
            StartCoroutine(ApplyHubPoseAfterFrames(playerObj.transform, pos, rot, hubPoseApplyDelayFrames));
        }
        else
        {
            if (verboseLogs) Debug.Log("[LoadCharacter] Hub pose not found (no in-memory hub pose). Not overriding position.");
        }
    }

    private IEnumerator ApplyPoseAfterFrames(Transform player, Vector3 pos, Quaternion rot, int frames, string label)
    {
        for (int i = 0; i < Mathf.Max(0, frames); i++)
            yield return null;

        yield return new WaitForFixedUpdate();

        if (player == null) yield break;

        var cc = player.GetComponent<CharacterController>();
        bool wasCC = false;
        if (cc != null)
        {
            wasCC = cc.enabled;
            cc.enabled = false;
        }

        player.SetPositionAndRotation(pos, rot);
        Physics.SyncTransforms();

        if (cc != null) cc.enabled = wasCC;

        var pm = player.GetComponent<PlayerMovement>();
        if (pm != null)
        {
            try { pm.FreezeForSeconds(0.15f); } catch { }
            try { pm.ForceSnapToGroundNow(); } catch { }
        }

        if (verboseLogs) Debug.Log($"[LoadCharacter] ✅ Applied {label} pose: {pos}");
    }

    private IEnumerator ApplyHubPoseAfterFrames(Transform player, Vector3 pos, Quaternion rot, int frames)
    {
        for (int i = 0; i < Mathf.Max(0, frames); i++)
            yield return null;

        yield return new WaitForFixedUpdate();

        if (player == null) yield break;

        var cc = player.GetComponent<CharacterController>();
        bool wasCC = false;
        if (cc != null)
        {
            wasCC = cc.enabled;
            cc.enabled = false;
        }

        player.SetPositionAndRotation(pos, rot);
        Physics.SyncTransforms();

        if (cc != null) cc.enabled = wasCC;

        var pm = player.GetComponent<PlayerMovement>();
        if (pm != null)
        {
            try { pm.FreezeForSeconds(0.15f); } catch { }
            try { pm.ForceSnapToGroundNow(); } catch { }
        }

        if (verboseLogs) Debug.Log($"[LoadCharacter] ✅ Applied HUB pose: {pos}");
    }
}