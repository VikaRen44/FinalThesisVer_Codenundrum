using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;

public class SaveGameManager : MonoBehaviour
{
    public static SaveGameManager Instance { get; private set; }

    [Header("Refs (optional)")]
    public Transform playerTransform; // if null, auto-finds by tag
    public NameInput nameInput; // optional, used only to read the live name (NO longer forces active profile)

    [Header("Host (NEW)")]
    [Tooltip("Optional direct host transform ref. If null, SaveGameManager will auto-find by tag or SaveID.")]
    public Transform hostTransform;

    [Tooltip("Tag used to auto-find the host if hostTransform is null.")]
    public string hostTag = "Host";

    [Tooltip("If set, will try to find a SaveID with this manual ID (recommended: HOST). Leave empty to skip.")]
    public string hostSaveIdManual = "HOST";

    [Header("Character Index Mapping (IMPORTANT)")]
    [Tooltip("Must match LoadCharacter.characterPrefabs order EXACTLY (e.g., [0]=MalePrefabName, [1]=FemalePrefabName). If empty, SaveSystem.CharacterPrefabNamesForInference is used.")]
    public string[] characterPrefabNamesForIndex;

    [Header("Playtime")]
    public bool trackPlaytime = true;
    private float _sessionSeconds;

    [Header("Debug")]
    public bool verboseLogs = false;

    [Header("Load/Return Stabilization (NEW)")]
    [Tooltip("If true, after applying player pose we snap them down to ground.")]
    public bool snapPlayerToGroundAfterApply = true;

    [Tooltip("Freeze PlayerMovement briefly after applying snapshot (prevents 1-frame fall/clip).")]
    public float freezePlayerAfterApplySeconds = 0.35f;

    [Header("Load Behavior (IMPORTANT)")]
    [Tooltip("If TRUE, loading a save will reload the scene even if it's the same scene.\nThis prevents 'current session' progress from overriding the save file progression.")]
    public bool forceReloadSceneOnSameSceneLoad = true;

    // =========================================================
    // ✅ MapFirstSpawnGate-style queued load (NEW)
    // =========================================================
    [Header("Queued Load Gate (NEW)")]
    [Tooltip("If true, UI will queue the load here and this manager will execute it after UI closes safely.")]
    public bool useQueuedLoadGate = true;

    [Range(0, 6)]
    [Tooltip("Frames to wait before executing queued load (lets UI close, timescale restore, canvas settle).")]
    public int queuedLoadDelayFrames = 2;

    private bool _hasQueuedLoad;
    private int _queuedLoadSlot = -1;
    private Coroutine _queuedLoadRoutine;

    // =========================================================
    // ✅ CAMERA RETARGET GATE (NEW) — fixes camera staying at initial spawn on load
    // =========================================================
    [Header("Camera Retarget Gate (NEW)")]
    [Tooltip("After loading, keep re-targeting camera for a short time (handles player respawn/replacement during load).")]
    public bool retargetCameraUntilStable = true;

    [Tooltip("How long (seconds, realtime) to keep forcing camera to target Player after load.")]
    public float cameraRetargetWindowSeconds = 1.5f;

    [Tooltip("How often (seconds, realtime) to re-apply camera target during the window.")]
    public float cameraRetargetIntervalSeconds = 0.05f;

    private Coroutine _cameraRetargetRoutine;
    private Coroutine _cameraRetargetWindowRoutine;

    private const string PREF_PLAYER_NAME = "playerName";

    private static SaveData _pendingSnapshot;
    private static bool _hasPendingSnapshot;
    private static bool _isLoading;
    private static int _activeSlot = -1;

    public bool IsLoadInProgress => _isLoading || _hasPendingSnapshot;

    public bool TryGetPendingSelectedCharacter(out int index)
    {
        index = 0;
        if (_hasPendingSnapshot && _pendingSnapshot != null)
        {
            index = _pendingSnapshot.selectedCharacterIndex;
            return true;
        }
        return false;
    }

    private void Awake()
    {
        if (transform.parent != null) transform.SetParent(null);

        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;

        EnsureActiveProfileIfEmpty();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Update()
    {
        if (trackPlaytime)
            _sessionSeconds += Time.unscaledDeltaTime;
    }

    // =========================================================
    // ✅ NEW: UI-safe load request (like MapFirstSpawnGate)
    // =========================================================
    public void QueueLoadFromSlot(int slot)
    {
        _queuedLoadSlot = slot;
        _hasQueuedLoad = true;

        if (_queuedLoadRoutine != null)
        {
            StopCoroutine(_queuedLoadRoutine);
            _queuedLoadRoutine = null;
        }

        _queuedLoadRoutine = StartCoroutine(QueuedLoadRoutine());
    }

    private IEnumerator QueuedLoadRoutine()
    {
        Time.timeScale = 1f;

        int frames = Mathf.Clamp(queuedLoadDelayFrames, 0, 6);
        for (int i = 0; i < frames; i++)
            yield return null;

        yield return new WaitForEndOfFrame();

        if (!_hasQueuedLoad)
        {
            _queuedLoadRoutine = null;
            yield break;
        }

        int slot = _queuedLoadSlot;
        _hasQueuedLoad = false;
        _queuedLoadSlot = -1;

        LoadFromSlot(slot);

        _queuedLoadRoutine = null;
    }

    public void SetPlayerName(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) newName = "Charlie";

        PlayerPrefs.SetString(PREF_PLAYER_NAME, newName);
        PlayerPrefs.Save();

        SaveSystem.SetActiveProfile(newName);

        if (verboseLogs)
            Debug.Log($"[SaveGameManager] SetPlayerName => '{newName}' (profile set)");
    }

    public string GetPlayerName()
    {
        if (nameInput != null && !string.IsNullOrWhiteSpace(nameInput.CurrentName))
        {
            string live = nameInput.CurrentName;
            if (!string.IsNullOrWhiteSpace(live)) return live;
        }

        var prefs = PlayerPrefs.GetString(PREF_PLAYER_NAME, "");
        if (!string.IsNullOrWhiteSpace(prefs)) return prefs;

        return "Charlie";
    }

    private void EnsureActiveProfileIfEmpty()
    {
        if (SaveSystem.HasActiveProfile()) return;

        string fallback = PlayerPrefs.GetString(PREF_PLAYER_NAME, "Charlie");
        SaveSystem.SetActiveProfile(fallback);

        if (verboseLogs)
            Debug.Log($"[SaveGameManager] EnsureActiveProfileIfEmpty -> '{fallback}'");
    }

    public int GetCurrentSelectedCharacterIndexFallback()
    {
        return PlayerPrefs.GetInt("selectedCharacter", 0);
    }

    public void SetCurrentSelectedCharacterIndexFallback(int index)
    {
        PlayerPrefs.SetInt("selectedCharacter", index);
        PlayerPrefs.Save();
    }

    private int InferCharacterIndexFromPlayerTransform()
    {
        if (playerTransform == null) return -1;

        string have = playerTransform.gameObject.name.Replace("(Clone)", "").Trim();

        if (characterPrefabNamesForIndex != null && characterPrefabNamesForIndex.Length > 0)
        {
            for (int i = 0; i < characterPrefabNamesForIndex.Length; i++)
            {
                string want = (characterPrefabNamesForIndex[i] ?? "").Trim();
                if (string.IsNullOrEmpty(want)) continue;
                if (string.Equals(want, have, StringComparison.Ordinal)) return i;
            }
        }

        if (SaveSystem.CharacterPrefabNamesForInference != null && SaveSystem.CharacterPrefabNamesForInference.Length > 0)
        {
            for (int i = 0; i < SaveSystem.CharacterPrefabNamesForInference.Length; i++)
            {
                string want = (SaveSystem.CharacterPrefabNamesForInference[i] ?? "").Trim();
                if (string.IsNullOrEmpty(want)) continue;
                if (string.Equals(want, have, StringComparison.Ordinal)) return i;
            }
        }

        var root = playerTransform.root;
        if (root != null)
        {
            have = root.gameObject.name.Replace("(Clone)", "").Trim();

            if (characterPrefabNamesForIndex != null && characterPrefabNamesForIndex.Length > 0)
            {
                for (int i = 0; i < characterPrefabNamesForIndex.Length; i++)
                {
                    string want = (characterPrefabNamesForIndex[i] ?? "").Trim();
                    if (string.IsNullOrEmpty(want)) continue;
                    if (string.Equals(want, have, StringComparison.Ordinal)) return i;
                }
            }

            if (SaveSystem.CharacterPrefabNamesForInference != null && SaveSystem.CharacterPrefabNamesForInference.Length > 0)
            {
                for (int i = 0; i < SaveSystem.CharacterPrefabNamesForInference.Length; i++)
                {
                    string want = (SaveSystem.CharacterPrefabNamesForInference[i] ?? "").Trim();
                    if (string.IsNullOrEmpty(want)) continue;
                    if (string.Equals(want, have, StringComparison.Ordinal)) return i;
                }
            }
        }

        return -1;
    }

    public SaveData BuildSnapshot()
    {
        EnsureActiveProfileIfEmpty();
        EnsurePlayerTransform();
        EnsureHostTransform();

        var data = new SaveData
        {
            playerName = GetPlayerName(),
            sceneName = SceneManager.GetActiveScene().name,
            realWorldUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };

        int inferredIdx = InferCharacterIndexFromPlayerTransform();
        if (inferredIdx >= 0)
        {
            data.selectedCharacterIndex = inferredIdx;
            data.hasSelectedCharacterIndex = true;
        }
        else
        {
            data.selectedCharacterIndex = GetCurrentSelectedCharacterIndexFallback();
            data.hasSelectedCharacterIndex = false;
        }

        data.selectedCharacterPrefabName = (playerTransform != null)
            ? playerTransform.gameObject.name.Replace("(Clone)", "").Trim()
            : "";

        if (ObjectiveManager.Instance != null)
            data.objectivePayloadJson = ObjectiveManager.Instance.ExportSaveStateJson();

        if (RewardStateManager.Instance != null)
            data.rewardPayloadJson = RewardStateManager.Instance.ExportSaveStateJson();

        if (AssessmentScoreManager.Instance != null)
            data.assessmentScorePayloadJson = AssessmentScoreManager.Instance.ExportSaveStateJson();
        else
            data.assessmentScorePayloadJson = "";

        if (playerTransform != null)
        {
            Vector3 p = playerTransform.position;
            Vector3 r = playerTransform.eulerAngles;

            data.player = new SaveData.PlayerSnapshot
            {
                px = p.x,
                py = p.y,
                pz = p.z,
                rx = r.x,
                ry = r.y,
                rz = r.z
            };
        }

        if (hostTransform != null)
        {
            Vector3 p = hostTransform.position;
            Vector3 r = hostTransform.eulerAngles;

            data.host = new SaveData.HostSnapshot
            {
                px = p.x,
                py = p.y,
                pz = p.z,
                rx = r.x,
                ry = r.y,
                rz = r.z
            };
        }

        data.totalPlaytimeSeconds = (long)Mathf.Round(_sessionSeconds);
        data.saveLabel = $"{data.sceneName} - {FormatPlaytime(_sessionSeconds)}";
        return data;
    }

    public void ApplySnapshot(SaveData data)
    {
        if (data == null)
        {
            Debug.LogError("[SaveGameManager] ApplySnapshot: data is NULL");
            return;
        }

        if (!string.IsNullOrWhiteSpace(data.playerName))
            SetPlayerName(data.playerName);

        int idx = data.selectedCharacterIndex;
        if (!data.hasSelectedCharacterIndex &&
            !string.IsNullOrWhiteSpace(data.selectedCharacterPrefabName) &&
            characterPrefabNamesForIndex != null &&
            characterPrefabNamesForIndex.Length > 0)
        {
            string want = data.selectedCharacterPrefabName.Trim();
            for (int i = 0; i < characterPrefabNamesForIndex.Length; i++)
            {
                string n = (characterPrefabNamesForIndex[i] ?? "").Trim();
                if (string.IsNullOrEmpty(n)) continue;
                if (string.Equals(n, want, StringComparison.Ordinal))
                {
                    idx = i;
                    break;
                }
            }
        }

        SetCurrentSelectedCharacterIndexFallback(idx);

        if (ObjectiveManager.Instance != null)
            ObjectiveManager.Instance.ImportSaveStateJson(data.objectivePayloadJson);

        if (RewardStateManager.Instance != null)
            RewardStateManager.Instance.ImportSaveStateJson(data.rewardPayloadJson);

        if (AssessmentScoreManager.Instance != null)
            AssessmentScoreManager.Instance.ImportSaveStateJson(data.assessmentScorePayloadJson);

        EnsurePlayerTransform();
        EnsureHostTransform();

        if (playerTransform != null && data.player != null)
        {
            var pos = new Vector3(data.player.px, data.player.py, data.player.pz);
            var rot = Quaternion.Euler(data.player.rx, data.player.ry, data.player.rz);
            SafeSetPose(playerTransform, pos, rot);

            var pm = playerTransform.GetComponent<PlayerMovement>();
            if (pm != null)
            {
                if (freezePlayerAfterApplySeconds > 0f)
                    pm.FreezeForSeconds(freezePlayerAfterApplySeconds);

                if (snapPlayerToGroundAfterApply)
                    pm.ForceSnapToGroundNow();
            }
        }

        if (hostTransform != null && data.host != null)
        {
            var pos = new Vector3(data.host.px, data.host.py, data.host.pz);
            var rot = Quaternion.Euler(data.host.rx, data.host.ry, data.host.rz);
            SafeSetPose(hostTransform, pos, rot);

            var agent = hostTransform.GetComponent<NavMeshAgent>();
            if (agent != null && agent.enabled)
                agent.ResetPath();
        }

        _sessionSeconds = data.totalPlaytimeSeconds;

        OptionalInvokeStatic("SaveSystem", "RestoreWorldFrom", data);
        OptionalInvokeStatic("HostRoomAutoCuller", "UpdateVisibilityStatic");

        // ✅ Old retarget (kept)
        ForceCameraRetargetAndSnap();

        // ✅ NEW: keep retargeting for a short window (handles respawn/replacement)
        StartCameraRetargetWindow();

        if (ObjectiveManager.Instance != null)
            ObjectiveManager.Instance.SetObjectiveTextTarget(ObjectiveManager.Instance.objectiveText);
    }

    public void SaveToSlot(int slot, Texture2D optionalThumb = null)
    {
        _activeSlot = slot;
        EnsureActiveProfileIfEmpty();

        var snapshot = BuildSnapshot();
        SaveSystem.Save(slot, snapshot, optionalThumb);

        if (verboseLogs)
            Debug.Log($"[SaveGameManager] Saved slot {slot}: activeProfile='{SaveSystem.GetActiveProfile()}', name='{snapshot.playerName}', scene='{snapshot.sceneName}', charIndex={snapshot.selectedCharacterIndex}, trusted={snapshot.hasSelectedCharacterIndex}");
    }

    public void LoadFromSlot(int slot)
    {
        _activeSlot = slot;
        EnsureActiveProfileIfEmpty();

        var data = SaveSystem.Load(slot);
        if (data == null)
        {
            Debug.LogWarning($"[SaveGameManager] LoadFromSlot({slot}) failed: no data (activeProfile='{SaveSystem.GetActiveProfile()}').");
            return;
        }

        _pendingSnapshot = data;
        _hasPendingSnapshot = true;
        _isLoading = true;

        string current = SceneManager.GetActiveScene().name;

        if (verboseLogs)
            Debug.Log($"[SaveGameManager] LoadFromSlot({slot}) activeProfile='{SaveSystem.GetActiveProfile()}', current='{current}', target='{data.sceneName}', charIndex={data.selectedCharacterIndex}");

        bool sameScene = (current == data.sceneName);

        if (sameScene && forceReloadSceneOnSameSceneLoad)
            StartCoroutine(LoadSceneAndApply(data.sceneName));
        else if (sameScene)
            StartCoroutine(ApplyPendingAfterSceneReady());
        else
            StartCoroutine(LoadSceneAndApply(data.sceneName));
    }

    private IEnumerator LoadSceneAndApply(string sceneName)
    {
        var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        while (!op.isDone) yield return null;
        yield return null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (_hasPendingSnapshot && _pendingSnapshot != null)
            StartCoroutine(ApplyPendingAfterSceneReady());
    }

    private IEnumerator ApplyPendingAfterSceneReady()
    {
        yield return null;
        yield return null;
        yield return new WaitForFixedUpdate();

        float timeout = 3f;
        float t = 0f;

        while (t < timeout)
        {
            EnsurePlayerTransform();
            EnsureHostTransform();
            if (playerTransform != null) break;

            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (_pendingSnapshot != null)
            ApplySnapshot(_pendingSnapshot);

        _pendingSnapshot = null;
        _hasPendingSnapshot = false;

        yield return null;
        _isLoading = false;

        // ✅ Extra safety: after load finishes, still keep camera retargeting briefly
        StartCameraRetargetWindow();
    }

    private void EnsurePlayerTransform()
    {
        if (playerTransform != null && !playerTransform.Equals(null)) return;

        playerTransform = null;
        var go = GameObject.FindGameObjectWithTag("Player");
        if (go != null) playerTransform = go.transform;
    }

    private void EnsureHostTransform()
    {
        if (hostTransform != null && !hostTransform.Equals(null)) return;

        hostTransform = null;

        if (!string.IsNullOrWhiteSpace(hostSaveIdManual))
        {
            var all = FindObjectsByType<SaveID>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                var sid = all[i];
                if (sid == null) continue;

                if (string.Equals(sid.ID, hostSaveIdManual, StringComparison.Ordinal))
                {
                    hostTransform = sid.transform;
                    return;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(hostTag))
        {
            var go = GameObject.FindGameObjectWithTag(hostTag);
            if (go != null) hostTransform = go.transform;
        }
    }

    // =========================================================
    // ✅ Old camera retarget (kept exactly) — ONLY internal loop changed to call ForceAcquirePlayerAndSnap
    // =========================================================
    private void ForceCameraRetargetAndSnap()
    {
        if (_cameraRetargetRoutine != null)
            StopCoroutine(_cameraRetargetRoutine);

        _cameraRetargetRoutine = StartCoroutine(ForceCameraRetargetAndSnapRoutine());
    }

    private IEnumerator ForceCameraRetargetAndSnapRoutine()
    {
        EnsurePlayerTransform();
        if (playerTransform == null) yield break;

        yield return null;
        yield return null;
        yield return new WaitForFixedUpdate();

        Scene active = SceneManager.GetActiveScene();
        const int frames = 4;

        for (int f = 0; f < frames; f++)
        {
            var cams = FindObjectsByType<CameraFollow>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var cam in cams)
            {
                if (cam == null) continue;
                if (!cam.gameObject.scene.IsValid() || cam.gameObject.scene != active) continue;

                // ✅ CRITICAL FIX: let CameraFollow choose a safe offset on load retarget
                TryInvokeInstanceVoid(cam, "ApplySceneCameraSettings");
                cam.ForceAcquirePlayerAndSnap(recalcOffsetIfZero: true);
                cam.ForceSnap();
            }
            yield return null;
        }

        _cameraRetargetRoutine = null;
    }

    // =========================================================
    // ✅ NEW: Camera retarget WINDOW (keeps camera glued to latest Player)
    // =========================================================
    private void StartCameraRetargetWindow()
    {
        if (!retargetCameraUntilStable) return;

        if (_cameraRetargetWindowRoutine != null)
        {
            StopCoroutine(_cameraRetargetWindowRoutine);
            _cameraRetargetWindowRoutine = null;
        }

        _cameraRetargetWindowRoutine = StartCoroutine(CameraRetargetWindowRoutine());
    }

    private IEnumerator CameraRetargetWindowRoutine()
    {
        float end = Time.unscaledTime + Mathf.Max(0.1f, cameraRetargetWindowSeconds);
        var wait = new WaitForSecondsRealtime(Mathf.Max(0.01f, cameraRetargetIntervalSeconds));

        while (Time.unscaledTime < end)
        {
            EnsurePlayerTransform();
            if (playerTransform != null)
            {
                Scene active = SceneManager.GetActiveScene();

                var cams = FindObjectsByType<CameraFollow>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var cam in cams)
                {
                    if (cam == null) continue;
                    if (!cam.gameObject.scene.IsValid() || cam.gameObject.scene != active) continue;

                    // ✅ CRITICAL FIX: let CameraFollow choose a safe offset on load retarget
                    TryInvokeInstanceVoid(cam, "ApplySceneCameraSettings");
                    cam.ForceAcquirePlayerAndSnap(recalcOffsetIfZero: true);
                    cam.ForceSnap();
                }
            }

            yield return wait;
        }

        _cameraRetargetWindowRoutine = null;
    }

    private static void TryInvokeInstanceVoid(object obj, string methodName)
    {
        if (obj == null) return;
        try
        {
            var t = obj.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var m = t.GetMethod(methodName, flags);
            if (m == null) return;
            if (m.GetParameters().Length != 0) return;
            m.Invoke(obj, null);
        }
        catch { }
    }

    private void SafeSetPose(Transform t, Vector3 position, Quaternion rotation)
    {
        var cc = t.GetComponent<CharacterController>();
        var rb = t.GetComponent<Rigidbody>();
        var agent = t.GetComponent<NavMeshAgent>();

        bool ccWasEnabled = false;
        if (cc != null)
        {
            ccWasEnabled = cc.enabled;
            cc.enabled = false;
        }

        if (agent != null && agent.enabled)
        {
            agent.Warp(position);
            t.rotation = rotation;
        }
        else if (rb != null && !rb.isKinematic)
        {
            rb.MovePosition(position);
            rb.MoveRotation(rotation);
            rb.WakeUp();
        }
        else
        {
            t.SetPositionAndRotation(position, rotation);
        }

        if (cc != null)
            cc.enabled = ccWasEnabled;
    }

    public static string FormatPlaytime(float seconds)
    {
        var ts = TimeSpan.FromSeconds((long)seconds);
        return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
    }

    private static void OptionalInvokeStatic(string typeName, string methodName, params object[] args)
    {
        try
        {
            var t = FindTypeByName(typeName);
            if (t == null) return;

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var m = (args == null || args.Length == 0)
                ? t.GetMethod(methodName, flags, null, Type.EmptyTypes, null)
                : t.GetMethod(methodName, flags);

            if (m == null) return;
            m.Invoke(null, args);
        }
        catch { }
    }

    private static Type FindTypeByName(string typeName)
    {
        var t = Type.GetType(typeName);
        if (t != null) return t;

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            try
            {
                var tt = assemblies[i].GetType(typeName);
                if (tt != null) return tt;

                var types = assemblies[i].GetTypes();
                for (int k = 0; k < types.Length; k++)
                    if (types[k].Name == typeName)
                        return types[k];
            }
            catch { }
        }

        return null;
    }
}