using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection; // ✅ NEW (for safe reflection)
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ObjectiveManager : MonoBehaviour
{
    public static ObjectiveManager Instance { get; private set; }

    [Serializable]
    public class ObjectiveEntry
    {
        public string id;
        [TextArea(1, 3)] public string uiText;

        [Header("Battle Counter (optional)")]
        [Tooltip("If > 0, this objective requires multiple battles. Remaining battles will decrement on each win return.")]
        public int requiredBattles = 0;

        [Tooltip("When remaining battles hits 0, ObjectiveManager moves to this objective id (if not empty).")]
        public string nextObjectiveIdWhenDone = "";
    }

    [Header("Hub Objectives (was Act 1)")]
    public List<ObjectiveEntry> act1Objectives = new List<ObjectiveEntry>();

    [Header("Chapter 1 Objectives")]
    public List<ObjectiveEntry> chapter1Objectives = new List<ObjectiveEntry>();

    [Header("UI (Optional direct target)")]
    public TMP_Text objectiveText;

    [Header("UI Auto-Rebind (Recommended)")]
    public bool autoFindObjectiveTextOnSceneLoad = true;
    public string objectiveTextObjectName = "ObjectiveNotes";
    public bool includeInactiveWhenFindingText = true;

    [Header("Objective Completion Animation")]
    public bool animateObjectiveCompletion = true;

    [Tooltip("CanvasGroup controlling the objective text fade. If empty, ObjectiveManager will auto-add/find one on the objectiveText object.")]
    public CanvasGroup objectiveCanvasGroup;

    // ✅ field kept for compatibility, not used
    public string checkmarkPrefix = "✓ ";

    public float completedHoldSeconds = 0.25f;
    public float fadeOutDuration = 0.35f;
    public float fadeInDuration = 0.35f;

    [Header("Completion Color (NEW)")]
    [Tooltip("When completing an objective, the text turns this color before fading out.")]
    public Color completedColor = new Color(0.25f, 1f, 0.25f, 1f);

    [Tooltip("Normal color used after completion (restored before showing the next objective). If alpha is 0, ObjectiveManager will auto-capture from TMP on first bind.")]
    public Color normalColor = new Color(0f, 0f, 0f, 0f);

    [Header("Checkmark Bounce")]
    public bool bounceOnCheckmark = true;
    public float bounceDuration = 0.22f;
    public float bouncePeakScale = 1.12f;
    public float bounceStartScale = 0.92f;

    [Header("SFX")]
    public AudioClip objectiveChangedSfx;
    [Range(0f, 1f)] public float objectiveSfxVolume = 1f;
    public bool playSfxOnRestoreOrLoad = false;
    public AudioSource sfxSource;

    [Header("Persistence")]
    public bool persistAcrossSceneLoads = true;

    [Tooltip("Current save-slot/profile key. SaveGameManager will set this per slot.")]
    public string profileKey = "default";

    public bool fallbackToFirstObjectiveIfInvalid = true;

    [Header("Editor Only")]
    public bool resetProgressOnPlay = false;

    [Header("Legacy Persistence (NOT recommended)")]
    [Tooltip("User request: keep OFF. This script will NOT rely on PlayerPrefs.")]
    public bool usePlayerPrefsPersistence = false;

    // =========================================================
    // ✅ NEW: Auto-sync profileKey from SaveGameManager (fixes your screenshot issue)
    // =========================================================
    [Header("Auto Profile Sync (IMPORTANT)")]
    [Tooltip("If true, ObjectiveManager will try to read the active profile key from SaveGameManager every scene load. Fixes: profileKey stuck at 'default'.")]
    public bool autoSyncProfileKeyFromSaveGameManager = true;

    [Tooltip("Debug log when profile key changes via auto-sync.")]
    public bool debugProfileSyncLogs = false;

    // =========================================================
    // ✅ NEW: Pending Hub objective override (applied when Hub loads)
    // =========================================================
    [Header("Hub Override (Optional)")]
    [Tooltip("If set, when the Hub scene loads ObjectiveManager will force this objective id once (then clears).")]
    public bool enablePendingHubOverride = true;

    [SerializeField] private string _pendingHubObjectiveId = "";
    [SerializeField] private bool _pendingHubOverridePlaySfx = false;

    public event Action<string> OnObjectiveChanged;
    public event Action<string, int> OnBattleCounterChanged;
    public event Action OnObjectiveUIRefreshRequested;

    private enum ObjectiveContext { Hub, Chapter1 }

    [Header("Objective Context (Auto by Scene)")]
    [SerializeField] private bool autoContextByScene = true;

    [Tooltip("Scene name for Hub context.")]
    [SerializeField] private string hubSceneName = "04_Gamehub";

    [Tooltip("Scene name for Chapter 1 context.")]
    [SerializeField] private string chapter1SceneName = "05_Chapter1";

    [SerializeField] private ObjectiveContext _context = ObjectiveContext.Hub;

    private List<ObjectiveEntry> ActiveObjectives =>
        (_context == ObjectiveContext.Chapter1) ? chapter1Objectives : act1Objectives;

    [SerializeField] private int _currentIndex = -1;
    private readonly Dictionary<string, int> _remainingBattles = new Dictionary<string, int>();

    private bool _initialized;
    private Coroutine _uiAnimRoutine;
    private bool _isAnimatingUI;
    private Vector3 _baseScale = Vector3.one;

    public bool HasLoadedState { get; private set; }

    // ✅ NEW: if objective changes while anim is running, we queue it (prevents "instant replace")
    private bool _hasQueuedObjectiveChange = false;
    private int _queuedObjectiveIndex = -1;
    private bool _queuedPlaySfx = false;

    // =========================================================
    // ✅ FIX: TMP VERTEX GRADIENT OVERRIDES COLOR (THIS IS WHY IT WONT TURN GREEN)
    // =========================================================
    private bool _cachedTmpGradientValid = false;
    private bool _cachedEnableVertexGradient = false;
    private VertexGradient _cachedColorGradient;

    [Serializable]
    private class RemainingWrapper
    {
        public List<string> keys = new List<string>();
        public List<int> values = new List<int>();
    }

    [Serializable]
    private class ContextState
    {
        public int currentIndex = -1;
        public RemainingWrapper remaining = new RemainingWrapper();
    }

    private readonly Dictionary<string, ContextState> _contextStates = new Dictionary<string, ContextState>();
    private string ContextKey => $"{profileKey}|{_context}";

    [Serializable]
    private class ObjectiveSavePayload
    {
        public string profileKey;
        public int context; // 0 = Hub, 1 = Chapter1
        public int currentIndex;

        public List<string> battleKeys = new List<string>();
        public List<int> battleValues = new List<int>();

        public List<string> contextKeys = new List<string>();
        public List<int> contextCurrentIndex = new List<int>();

        public List<string> ctxOwner = new List<string>();
        public List<string> ctxKey = new List<string>();
        public List<int> ctxVal = new List<int>();
    }

    public string CurrentObjectiveId => GetCurrentObjectiveId();
    public int GetObjectiveIndexById(string objectiveId) => FindObjectiveIndex(objectiveId);

    public bool IsObjectiveActive(string objectiveId)
    {
        if (string.IsNullOrEmpty(objectiveId)) return true;
        return string.Equals(GetCurrentObjectiveId(), objectiveId, StringComparison.Ordinal);
    }

    public bool IsObjectivePassed(string objectiveId)
    {
        if (string.IsNullOrEmpty(objectiveId)) return false;
        int idx = FindObjectiveIndex(objectiveId);
        if (idx < 0) return false;
        return _currentIndex > idx;
    }

    public bool IsObjectiveCompleted(string objectiveId) => IsObjectivePassed(objectiveId);

    private void Awake()
    {
        if (transform.parent != null)
            transform.SetParent(null);

        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (persistAcrossSceneLoads)
            DontDestroyOnLoad(gameObject);

        if (sfxSource == null)
            sfxSource = GetComponent<AudioSource>();

        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;

        usePlayerPrefsPersistence = false;

        // ✅ NEW: try sync profile once at boot (helps if ObjectiveManager exists in first scene too)
        if (autoSyncProfileKeyFromSaveGameManager)
            TrySyncProfileKeyFromSaveGameManager(reloadContextIfChanged: false);

        if (!_initialized)
        {
            if (resetProgressOnPlay)
            {
                ClearAllContextsForProfile_NoSave();
            }
            else
            {
                LoadContextStateFromMemoryOrInit(playSfx: false);
            }

            _initialized = true;
        }

        RebindUI(force: true);
        RefreshUI();

        HasLoadedState = true;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void HandleSceneLoaded(Scene s, LoadSceneMode m)
    {
        // ✅ 1) Sync profileKey from SaveGameManager FIRST (this fixes your current bug)
        if (autoSyncProfileKeyFromSaveGameManager)
        {
            bool changed = TrySyncProfileKeyFromSaveGameManager(reloadContextIfChanged: false);
            if (changed)
            {
                // If profile changed, we want to load that profile's state for the new scene's context
                // AFTER we resolve context below.
            }
        }

        // ✅ 2) Resolve context for this scene
        if (autoContextByScene)
        {
            ObjectiveContext newContext = ResolveContextFromSceneName(s.name);
            if (newContext != _context)
            {
                SaveContextStateToMemory();
                _context = newContext;
                LoadContextStateFromMemoryOrInit(playSfx: false);
            }
            else
            {
                SaveContextStateToMemory();
            }
        }

        // ✅ 3) Apply pending hub override WHEN hub loads (before triggers in hub run)
        if (enablePendingHubOverride && IsSameScene(s.name, hubSceneName))
        {
            if (!string.IsNullOrEmpty(_pendingHubObjectiveId))
            {
                // Force hub objective NOW (in hub context)
                ForceSetObjective(_pendingHubObjectiveId);
                // ForceSetObjective plays sfx; if you want strict control, use internal function:
                // SetObjectiveInContext_Internal(ObjectiveContext.Hub, _pendingHubObjectiveId, _pendingHubOverridePlaySfx, updateUIIfCurrentContext: true);

                // Clear so it applies only once
                _pendingHubObjectiveId = "";
                _pendingHubOverridePlaySfx = false;
            }
        }

        if (!objectiveText) objectiveText = null;

        StopUIAnimation(resetVisuals: true);
        RebindUI(force: true);
        RefreshUI();

        HasLoadedState = true;
    }

    // =========================================================
    // ✅ NEW: PUBLIC API — call this BEFORE returning to Hub
    // This sets the HUB objective state even if you're currently in another context/scene.
    // =========================================================
    public void ForceSetHubObjective(string objectiveId, bool playSfx = false, bool applyOnNextHubLoadToo = true)
    {
        if (string.IsNullOrEmpty(objectiveId)) return;

        // Update HUB context state immediately (without breaking current scene context)
        SetObjectiveInContext_Internal(ObjectiveContext.Hub, objectiveId, playSfx, updateUIIfCurrentContext: IsSameScene(SceneManager.GetActiveScene().name, hubSceneName));

        // Also store as "pending" so when Hub scene loads it forcibly applies once (extra safety)
        if (applyOnNextHubLoadToo && enablePendingHubOverride)
        {
            _pendingHubObjectiveId = objectiveId;
            _pendingHubOverridePlaySfx = playSfx;
        }
    }

    // =========================================================
    // ✅ NEW: Safe profileKey sync (no hard dependency)
    // Looks for common fields/properties on SaveGameManager:
    // - profileKey, ProfileKey, currentProfileKey, CurrentProfileKey, activeProfileKey, ActiveProfileKey
    // =========================================================
    private bool TrySyncProfileKeyFromSaveGameManager(bool reloadContextIfChanged)
    {
        try
        {
            var sgm = SaveGameManager.Instance;
            if (sgm == null) return false;

            string found = TryGetStringMember(sgm, "profileKey")
                        ?? TryGetStringMember(sgm, "ProfileKey")
                        ?? TryGetStringMember(sgm, "currentProfileKey")
                        ?? TryGetStringMember(sgm, "CurrentProfileKey")
                        ?? TryGetStringMember(sgm, "activeProfileKey")
                        ?? TryGetStringMember(sgm, "ActiveProfileKey")
                        ?? TryGetStringMember(sgm, "slotProfileKey")
                        ?? TryGetStringMember(sgm, "SlotProfileKey");

            if (string.IsNullOrEmpty(found))
                return false;

            if (string.Equals(profileKey, found, StringComparison.Ordinal))
                return false;

            string old = profileKey;
            profileKey = found;

            if (debugProfileSyncLogs)
                Debug.Log($"[ObjectiveManager] Auto-synced profileKey: '{old}' -> '{profileKey}'");

            // If requested, reload state for current context
            if (reloadContextIfChanged)
            {
                StopUIAnimation(resetVisuals: true);
                LoadContextStateFromMemoryOrInit(playSfx: false);
                RebindUI(force: true);
                RefreshUI();
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private string TryGetStringMember(object obj, string name)
    {
        if (obj == null || string.IsNullOrEmpty(name)) return null;

        var t = obj.GetType();

        // Property
        var prop = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop != null && prop.PropertyType == typeof(string))
        {
            try { return prop.GetValue(obj) as string; } catch { }
        }

        // Field
        var field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null && field.FieldType == typeof(string))
        {
            try { return field.GetValue(obj) as string; } catch { }
        }

        return null;
    }

    // =========================================================
    // EXISTING PUBLIC API (kept)
    // =========================================================
    public void ResetForNewGame(string newProfileKey = "default")
    {
        if (string.IsNullOrEmpty(newProfileKey))
            newProfileKey = "default";

        HasLoadedState = false;

        StopUIAnimation(resetVisuals: true);

        profileKey = newProfileKey;

        string hubKey = $"{profileKey}|{ObjectiveContext.Hub}";
        string ch1Key = $"{profileKey}|{ObjectiveContext.Chapter1}";
        _contextStates.Remove(hubKey);
        _contextStates.Remove(ch1Key);

        if (autoContextByScene)
            _context = ResolveContextFromSceneName(SceneManager.GetActiveScene().name);

        _currentIndex = (ActiveObjectives.Count > 0) ? 0 : -1;
        _remainingBattles.Clear();
        InitializeCounterIfNeededForCurrentObjective();

        SaveContextStateToMemory();

        RebindUI(force: true);
        RefreshUI();

        OnObjectiveChanged?.Invoke(GetCurrentObjectiveId());

        HasLoadedState = true;
    }

    public void ForceRefreshForActiveScene(bool playSfx = false)
    {
        HasLoadedState = false;

        var s = SceneManager.GetActiveScene().name;
        var newCtx = ResolveContextFromSceneName(s);

        if (newCtx != _context)
        {
            SaveContextStateToMemory();
            _context = newCtx;
            LoadContextStateFromMemoryOrInit(playSfx: playSfx);
        }
        else
        {
            LoadContextStateFromMemoryOrInit(playSfx: playSfx);
        }

        StopUIAnimation(resetVisuals: true);
        RebindUI(force: true);
        RefreshUI();

        OnObjectiveChanged?.Invoke(GetCurrentObjectiveId());

        HasLoadedState = true;
    }

    private ObjectiveContext ResolveContextFromSceneName(string sceneName)
    {
        if (IsSameScene(sceneName, chapter1SceneName))
            return ObjectiveContext.Chapter1;

        return ObjectiveContext.Hub;
    }

    private bool IsSameScene(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public void SetProfileKeyAndLoad(string key, bool createNew)
    {
        if (string.IsNullOrEmpty(key))
            key = "default";

        bool changed = !string.Equals(profileKey, key, StringComparison.Ordinal);
        profileKey = key;

        StopUIAnimation(resetVisuals: true);

        if (createNew)
        {
            ClearAllContextsForProfile_NoSave();
            EnsureValidCurrentObjective(playSfx: false);
            SaveContextStateToMemory();

            RebindUI(force: false);
            RefreshUI();

            HasLoadedState = true;
            return;
        }

        if (!changed && _initialized)
        {
            RebindUI(force: false);
            RefreshUI();

            HasLoadedState = true;
            return;
        }

        LoadContextStateFromMemoryOrInit(playSfx: playSfxOnRestoreOrLoad);

        RebindUI(force: false);
        RefreshUI();

        HasLoadedState = true;
    }

    public string ExportSaveStateJson()
    {
        var p = new ObjectiveSavePayload();

        p.profileKey = profileKey;
        p.context = (_context == ObjectiveContext.Chapter1) ? 1 : 0;
        p.currentIndex = _currentIndex;

        foreach (var kv in _remainingBattles)
        {
            p.battleKeys.Add(kv.Key);
            p.battleValues.Add(kv.Value);
        }

        foreach (var kv in _contextStates)
        {
            p.contextKeys.Add(kv.Key);
            p.contextCurrentIndex.Add(kv.Value != null ? kv.Value.currentIndex : -1);

            if (kv.Value != null && kv.Value.remaining != null)
            {
                var rk = kv.Value.remaining.keys;
                var rv = kv.Value.remaining.values;

                int n = Mathf.Min(rk != null ? rk.Count : 0, rv != null ? rv.Count : 0);
                for (int i = 0; i < n; i++)
                {
                    if (string.IsNullOrEmpty(rk[i])) continue;

                    p.ctxOwner.Add(kv.Key);
                    p.ctxKey.Add(rk[i]);
                    p.ctxVal.Add(rv[i]);
                }
            }
        }

        return JsonUtility.ToJson(p);
    }

    public void ImportSaveStateJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        ObjectiveSavePayload p = null;
        try { p = JsonUtility.FromJson<ObjectiveSavePayload>(json); }
        catch { p = null; }

        if (p == null) return;

        HasLoadedState = false;

        StopUIAnimation(resetVisuals: true);

        if (!string.IsNullOrEmpty(p.profileKey))
            profileKey = p.profileKey;

        _context = (p.context == 1) ? ObjectiveContext.Chapter1 : ObjectiveContext.Hub;
        _currentIndex = p.currentIndex;

        _remainingBattles.Clear();
        int n = Mathf.Min(p.battleKeys != null ? p.battleKeys.Count : 0,
                          p.battleValues != null ? p.battleValues.Count : 0);
        for (int i = 0; i < n; i++)
        {
            string k = p.battleKeys[i];
            if (string.IsNullOrEmpty(k)) continue;
            _remainingBattles[k] = p.battleValues[i];
        }

        _contextStates.Clear();

        int cn = Mathf.Min(p.contextKeys != null ? p.contextKeys.Count : 0,
                           p.contextCurrentIndex != null ? p.contextCurrentIndex.Count : 0);

        for (int i = 0; i < cn; i++)
        {
            string ck = p.contextKeys[i];
            if (string.IsNullOrEmpty(ck)) continue;

            var st = new ContextState();
            st.currentIndex = p.contextCurrentIndex[i];
            st.remaining = new RemainingWrapper();
            _contextStates[ck] = st;
        }

        int bn = Mathf.Min(p.ctxOwner != null ? p.ctxOwner.Count : 0,
                   Mathf.Min(p.ctxKey != null ? p.ctxKey.Count : 0,
                             p.ctxVal != null ? p.ctxVal.Count : 0));

        for (int i = 0; i < bn; i++)
        {
            string owner = p.ctxOwner[i];
            string key2 = p.ctxKey[i];
            int val = p.ctxVal[i];

            if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(key2)) continue;
            if (!_contextStates.TryGetValue(owner, out var st) || st == null) continue;

            st.remaining.keys.Add(key2);
            st.remaining.values.Add(val);
        }

        EnsureValidCurrentObjective(playSfx: false);
        SaveContextStateToMemory();

        RebindUI(force: true);
        RefreshUI();

        HasLoadedState = true;
    }

    public void SetObjectiveTextTarget(TMP_Text target)
    {
        objectiveText = target;
        RebindUI(force: true);
        RefreshUI();
    }

    public string GetCurrentObjectiveId()
    {
        if (_currentIndex < 0 || _currentIndex >= ActiveObjectives.Count) return "";
        return ActiveObjectives[_currentIndex].id ?? "";
    }

    public int GetCurrentObjectiveIndex() => _currentIndex;

    public string GetCurrentObjectiveUITextSafe()
    {
        if (_currentIndex < 0 || _currentIndex >= ActiveObjectives.Count) return "";
        return ActiveObjectives[_currentIndex].uiText ?? "";
    }

    public void ForceSetObjective(string objectiveId)
    {
        int idx = FindObjectiveIndex(objectiveId);
        if (idx < 0)
        {
            if (fallbackToFirstObjectiveIfInvalid)
                idx = (ActiveObjectives.Count > 0) ? 0 : -1;
            else
                return;
        }

        StopUIAnimation(resetVisuals: true);
        SetObjectiveIndex(idx, playSfx: true);
    }

    public void CompleteObjective(string objectiveId)
    {
        int idx = FindObjectiveIndex(objectiveId);
        if (idx < 0) return;

        if (_currentIndex != idx)
            SetObjectiveIndex(idx, playSfx: false);

        CompleteCurrentObjective();
    }

    public void CompleteCurrentObjective()
    {
        if (_currentIndex < 0 || _currentIndex >= ActiveObjectives.Count) return;
        if (_isAnimatingUI) return;

        int nextIndex = _currentIndex + 1;

        if (nextIndex < 0 || nextIndex >= ActiveObjectives.Count)
        {
            if (animateObjectiveCompletion && objectiveText != null)
                StartUIRoutine(CompleteThenClearRoutine());
            else
                ClearObjectiveImmediate();

            return;
        }

        if (animateObjectiveCompletion && objectiveText != null)
            StartUIRoutine(CompleteThenAdvanceRoutine(nextIndex));
        else
            SetObjectiveIndex(nextIndex, playSfx: true);
    }

    public void RestoreObjectiveIndex(int index)
    {
        if (index < 0 || index >= ActiveObjectives.Count)
        {
            if (fallbackToFirstObjectiveIfInvalid)
                index = (ActiveObjectives.Count > 0) ? 0 : -1;
            else
                return;
        }

        StopUIAnimation(resetVisuals: true);
        SetObjectiveIndex(index, playSfx: playSfxOnRestoreOrLoad);
    }

    public void RestoreObjectiveId(string id)
    {
        int idx = FindObjectiveIndex(id);
        if (idx < 0)
        {
            if (fallbackToFirstObjectiveIfInvalid)
                idx = (ActiveObjectives.Count > 0) ? 0 : -1;
            else
                return;
        }

        StopUIAnimation(resetVisuals: true);
        SetObjectiveIndex(idx, playSfx: playSfxOnRestoreOrLoad);
    }

    public bool TryGetRemainingBattles(string objectiveId, out int remaining)
    {
        remaining = 0;
        if (string.IsNullOrEmpty(objectiveId)) return false;

        var entry = GetObjectiveEntry(objectiveId);
        if (entry == null) return false;
        if (entry.requiredBattles <= 0) return false;

        if (!_remainingBattles.TryGetValue(objectiveId, out remaining))
        {
            remaining = Mathf.Max(0, entry.requiredBattles);
            _remainingBattles[objectiveId] = remaining;
            SaveContextStateToMemory();
        }

        return true;
    }

    public void DecrementBattleCounter(string objectiveId)
    {
        if (string.IsNullOrEmpty(objectiveId)) return;

        var entry = GetObjectiveEntry(objectiveId);
        if (entry == null) return;
        if (entry.requiredBattles <= 0) return;

        if (!_remainingBattles.TryGetValue(objectiveId, out int remaining))
            remaining = Mathf.Max(0, entry.requiredBattles);

        remaining = Mathf.Max(0, remaining - 1);
        _remainingBattles[objectiveId] = remaining;

        OnBattleCounterChanged?.Invoke(objectiveId, remaining);
        RefreshUI();
        SaveContextStateToMemory();

        if (remaining <= 0)
        {
            if (!string.IsNullOrEmpty(entry.nextObjectiveIdWhenDone))
                ForceSetObjective(entry.nextObjectiveIdWhenDone);
            else
                CompleteCurrentObjective();
        }
    }

    public void ClearAllProgressAndCounters()
    {
        StopUIAnimation(resetVisuals: true);

        _currentIndex = (ActiveObjectives.Count > 0) ? 0 : -1;
        _remainingBattles.Clear();
        InitializeCounterIfNeededForCurrentObjective();

        OnObjectiveChanged?.Invoke(GetCurrentObjectiveId());
        RefreshUI();
        SaveContextStateToMemory();
    }

    public void CompleteCurrentObjectiveAndSet(string nextObjectiveId)
    {
        if (_isAnimatingUI) return;

        if (!animateObjectiveCompletion || objectiveText == null)
        {
            ForceSetObjective(nextObjectiveId);
            return;
        }

        int nextIndex = FindObjectiveIndex(nextObjectiveId);
        if (nextIndex < 0) nextIndex = _currentIndex + 1;

        if (nextIndex < 0 || nextIndex >= ActiveObjectives.Count)
        {
            StartUIRoutine(CompleteThenClearRoutine());
            return;
        }

        StartUIRoutine(CompleteThenAdvanceRoutine(nextIndex));
    }

    // =========================================================
    // ✅ NEW: internal set objective for a specific context (Hub/Chapter1)
    // DOES NOT break your current context unless you want it to.
    // =========================================================
    private void SetObjectiveInContext_Internal(ObjectiveContext ctx, string objectiveId, bool playSfx, bool updateUIIfCurrentContext)
    {
        if (string.IsNullOrEmpty(objectiveId)) return;

        // Save current
        var oldCtx = _context;
        int oldIndex = _currentIndex;

        // swap context temporarily
        _context = ctx;

        // Load that context state so we modify the correct one
        LoadContextStateFromMemoryOrInit(playSfx: false);

        int idx = FindObjectiveIndex(objectiveId);
        if (idx < 0)
        {
            if (fallbackToFirstObjectiveIfInvalid)
                idx = (ActiveObjectives.Count > 0) ? 0 : -1;
            else
            {
                // restore
                _context = oldCtx;
                _currentIndex = oldIndex;
                LoadContextStateFromMemoryOrInit(playSfx: false);
                return;
            }
        }

        // set directly (no UI anim inside other scenes)
        _currentIndex = idx;
        InitializeCounterIfNeededForCurrentObjective();
        SaveContextStateToMemory();

        if (playSfx && updateUIIfCurrentContext)
            PlayObjectiveSfx();

        if (updateUIIfCurrentContext)
        {
            StopUIAnimation(resetVisuals: true);
            RebindUI(force: true);
            RefreshUI();
            OnObjectiveChanged?.Invoke(GetCurrentObjectiveId());
        }

        // restore old context back
        _context = oldCtx;
        LoadContextStateFromMemoryOrInit(playSfx: false);

        // keep current index consistent for restored context
        _currentIndex = oldIndex;
    }

    // =========================================================
    // ✅ FIX: animate ALL objective changes (not just completion)
    // =========================================================
    private void SetObjectiveIndex(int idx, bool playSfx)
    {
        if (_isAnimatingUI)
        {
            _hasQueuedObjectiveChange = true;
            _queuedObjectiveIndex = idx;
            _queuedPlaySfx = _queuedPlaySfx || playSfx;
            return;
        }

        bool canAnimateChange = animateObjectiveCompletion && objectiveText != null;

        if (canAnimateChange && idx != _currentIndex && _currentIndex != -1)
        {
            StartUIRoutine(AnimateChangeToIndexRoutine(idx, playSfx));
            return;
        }

        _currentIndex = idx;
        InitializeCounterIfNeededForCurrentObjective();

        if (playSfx) PlayObjectiveSfx();

        OnObjectiveChanged?.Invoke(GetCurrentObjectiveId());

        RebindUI(force: false);
        RefreshUI();
        SaveContextStateToMemory();
    }

    private void EnsureValidCurrentObjective(bool playSfx)
    {
        if (_currentIndex >= 0 && _currentIndex < ActiveObjectives.Count) return;

        _currentIndex = (ActiveObjectives.Count > 0) ? 0 : -1;
        InitializeCounterIfNeededForCurrentObjective();

        if (playSfx) PlayObjectiveSfx();
        OnObjectiveChanged?.Invoke(GetCurrentObjectiveId());

        SaveContextStateToMemory();
    }

    private void ClearObjectiveImmediate()
    {
        _currentIndex = -1;
        OnObjectiveChanged?.Invoke("");
        RefreshUI();
        SaveContextStateToMemory();
    }

    private int FindObjectiveIndex(string id)
    {
        if (string.IsNullOrEmpty(id)) return -1;

        for (int i = 0; i < ActiveObjectives.Count; i++)
        {
            if (string.Equals(ActiveObjectives[i].id, id, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    private ObjectiveEntry GetObjectiveEntry(string id)
    {
        int idx = FindObjectiveIndex(id);
        if (idx < 0) return null;
        return ActiveObjectives[idx];
    }

    private void InitializeCounterIfNeededForCurrentObjective()
    {
        string id = GetCurrentObjectiveId();
        if (string.IsNullOrEmpty(id)) return;

        var entry = GetObjectiveEntry(id);
        if (entry == null) return;

        if (entry.requiredBattles > 0 && !_remainingBattles.ContainsKey(id))
        {
            _remainingBattles[id] = Mathf.Max(0, entry.requiredBattles);
            OnBattleCounterChanged?.Invoke(id, _remainingBattles[id]);
        }
    }

    private void SaveContextStateToMemory()
    {
        if (string.IsNullOrEmpty(profileKey)) profileKey = "default";

        var st = new ContextState();
        st.currentIndex = _currentIndex;

        st.remaining.keys.Clear();
        st.remaining.values.Clear();

        foreach (var kv in _remainingBattles)
        {
            st.remaining.keys.Add(kv.Key);
            st.remaining.values.Add(kv.Value);
        }

        _contextStates[ContextKey] = st;
    }

    private void LoadContextStateFromMemoryOrInit(bool playSfx)
    {
        if (string.IsNullOrEmpty(profileKey)) profileKey = "default";

        _remainingBattles.Clear();

        if (_contextStates.TryGetValue(ContextKey, out var st) && st != null)
        {
            _currentIndex = st.currentIndex;

            if (st.remaining != null && st.remaining.keys != null && st.remaining.values != null)
            {
                int n = Mathf.Min(st.remaining.keys.Count, st.remaining.values.Count);
                for (int i = 0; i < n; i++)
                {
                    string k = st.remaining.keys[i];
                    if (string.IsNullOrEmpty(k)) continue;
                    _remainingBattles[k] = st.remaining.values[i];
                }
            }
        }
        else
        {
            _currentIndex = (ActiveObjectives.Count > 0) ? 0 : -1;
            _remainingBattles.Clear();
        }

        if (_currentIndex < 0 || _currentIndex >= ActiveObjectives.Count)
            _currentIndex = (fallbackToFirstObjectiveIfInvalid && ActiveObjectives.Count > 0) ? 0 : -1;

        InitializeCounterIfNeededForCurrentObjective();

        if (playSfx) PlayObjectiveSfx();
        OnObjectiveChanged?.Invoke(GetCurrentObjectiveId());

        SaveContextStateToMemory();
    }

    private void ClearAllContextsForProfile_NoSave()
    {
        string pk = string.IsNullOrEmpty(profileKey) ? "default" : profileKey;

        string hubKey = $"{pk}|{ObjectiveContext.Hub}";
        string ch1Key = $"{pk}|{ObjectiveContext.Chapter1}";

        _contextStates.Remove(hubKey);
        _contextStates.Remove(ch1Key);

        _currentIndex = (ActiveObjectives.Count > 0) ? 0 : -1;
        _remainingBattles.Clear();
        InitializeCounterIfNeededForCurrentObjective();
        OnObjectiveChanged?.Invoke(GetCurrentObjectiveId());
        RefreshUI();

        SaveContextStateToMemory();
    }

    private void RebindUI(bool force)
    {
        if (autoFindObjectiveTextOnSceneLoad)
            TryAutoBindObjectiveText(force);

        TryBindCanvasGroup(force);
        CacheBaseScale();
        CacheNormalColorIfNeeded();

        CacheTmpOverridesIfNeeded();
    }

    private void CacheNormalColorIfNeeded()
    {
        if (objectiveText == null) return;
        if (normalColor.a <= 0.0001f)
            normalColor = objectiveText.color;
    }

    private void RefreshUI()
    {
        if (_isAnimatingUI)
        {
            OnObjectiveUIRefreshRequested?.Invoke();
            return;
        }

        if (objectiveText != null)
        {
            ResetObjectiveVisualState();
            objectiveText.text = GetCurrentObjectiveUITextSafe();
        }

        OnObjectiveUIRefreshRequested?.Invoke();
    }

    private void TryBindCanvasGroup(bool force)
    {
        if (!force && objectiveCanvasGroup != null) return;
        if (objectiveText == null) return;

        var cg = objectiveText.GetComponent<CanvasGroup>();
        if (cg == null) cg = objectiveText.gameObject.AddComponent<CanvasGroup>();
        objectiveCanvasGroup = cg;
    }

    private void TryAutoBindObjectiveText(bool force)
    {
        if (!autoFindObjectiveTextOnSceneLoad) return;

        if (!force && objectiveText != null) return;
        if (objectiveText != null) return;

        if (!string.IsNullOrEmpty(objectiveTextObjectName))
        {
            var go = GameObject.Find(objectiveTextObjectName);
            if (go != null)
            {
                var tmp = go.GetComponent<TMP_Text>();
                if (tmp != null)
                {
                    objectiveText = tmp;
                    return;
                }
            }
        }

#if UNITY_2023_1_OR_NEWER
        var texts = UnityEngine.Object.FindObjectsByType<TMP_Text>(
            includeInactiveWhenFindingText ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
#else
        var texts = UnityEngine.Object.FindObjectsOfType<TMP_Text>(includeInactiveWhenFindingText);
#endif
        foreach (var t in texts)
        {
            if (t == null) continue;

            if (!string.IsNullOrEmpty(objectiveTextObjectName) &&
                string.Equals(t.gameObject.name, objectiveTextObjectName, StringComparison.Ordinal))
            {
                objectiveText = t;
                return;
            }
        }
    }

    private void StartUIRoutine(IEnumerator routine)
    {
        StopUIAnimation(resetVisuals: true);
        _uiAnimRoutine = StartCoroutine(routine);
    }

    private void StopUIAnimation(bool resetVisuals)
    {
        if (_uiAnimRoutine != null)
        {
            StopCoroutine(_uiAnimRoutine);
            _uiAnimRoutine = null;
        }

        _isAnimatingUI = false;

        if (resetVisuals)
            ResetObjectiveVisualState();

        _hasQueuedObjectiveChange = false;
        _queuedObjectiveIndex = -1;
        _queuedPlaySfx = false;
    }

    private IEnumerator AnimateChangeToIndexRoutine(int nextIndex, bool playSfx)
    {
        _isAnimatingUI = true;
        RebindUI(force: false);

        if (objectiveText == null)
        {
            _currentIndex = nextIndex;
            InitializeCounterIfNeededForCurrentObjective();
            if (playSfx) PlayObjectiveSfx();
            OnObjectiveChanged?.Invoke(GetCurrentObjectiveId());
            SaveContextStateToMemory();
            _isAnimatingUI = false;
            _uiAnimRoutine = null;
            yield break;
        }

        ResetObjectiveVisualState();

        yield return FadeRoutine(0f, fadeOutDuration);

        _currentIndex = nextIndex;
        InitializeCounterIfNeededForCurrentObjective();
        SaveContextStateToMemory();

        if (playSfx) PlayObjectiveSfx();
        OnObjectiveChanged?.Invoke(GetCurrentObjectiveId());

        ResetObjectiveVisualState();
        objectiveText.text = GetCurrentObjectiveUITextSafe();

        yield return FadeRoutine(1f, fadeInDuration);

        _isAnimatingUI = false;
        _uiAnimRoutine = null;

        if (_hasQueuedObjectiveChange && _queuedObjectiveIndex != -1)
        {
            int qi = _queuedObjectiveIndex;
            bool qsfx = _queuedPlaySfx;

            _hasQueuedObjectiveChange = false;
            _queuedObjectiveIndex = -1;
            _queuedPlaySfx = false;

            StartUIRoutine(AnimateChangeToIndexRoutine(qi, qsfx));
        }
    }

    private IEnumerator CompleteThenAdvanceRoutine(int nextIndex)
    {
        _isAnimatingUI = true;
        RebindUI(force: false);

        if (objectiveText == null)
        {
            _currentIndex = nextIndex;
            InitializeCounterIfNeededForCurrentObjective();
            OnObjectiveChanged?.Invoke(GetCurrentObjectiveId());
            SaveContextStateToMemory();
            _isAnimatingUI = false;
            _uiAnimRoutine = null;
            yield break;
        }

        ResetObjectiveVisualState();

        ApplyGreenFlashVisuals();

        PlayObjectiveSfx();

        if (bounceOnCheckmark)
            yield return BounceRoutine();

        if (completedHoldSeconds > 0f)
            yield return new WaitForSecondsRealtime(completedHoldSeconds);

        yield return FadeRoutine(0f, fadeOutDuration);

        RestoreAfterGreenFlashVisuals();

        _currentIndex = nextIndex;
        InitializeCounterIfNeededForCurrentObjective();
        OnObjectiveChanged?.Invoke(GetCurrentObjectiveId());
        SaveContextStateToMemory();

        ResetObjectiveVisualState();
        objectiveText.text = GetCurrentObjectiveUITextSafe();

        yield return FadeRoutine(1f, fadeInDuration);

        _isAnimatingUI = false;
        _uiAnimRoutine = null;

        if (_hasQueuedObjectiveChange && _queuedObjectiveIndex != -1)
        {
            int qi = _queuedObjectiveIndex;
            bool qsfx = _queuedPlaySfx;

            _hasQueuedObjectiveChange = false;
            _queuedObjectiveIndex = -1;
            _queuedPlaySfx = false;

            StartUIRoutine(AnimateChangeToIndexRoutine(qi, qsfx));
        }
    }

    private IEnumerator CompleteThenClearRoutine()
    {
        _isAnimatingUI = true;
        RebindUI(force: false);

        if (objectiveText == null)
        {
            ClearObjectiveImmediate();
            _isAnimatingUI = false;
            _uiAnimRoutine = null;
            yield break;
        }

        ResetObjectiveVisualState();

        ApplyGreenFlashVisuals();

        PlayObjectiveSfx();

        if (bounceOnCheckmark)
            yield return BounceRoutine();

        if (completedHoldSeconds > 0f)
            yield return new WaitForSecondsRealtime(completedHoldSeconds);

        yield return FadeRoutine(0f, fadeOutDuration);

        RestoreAfterGreenFlashVisuals();

        _currentIndex = -1;
        OnObjectiveChanged?.Invoke("");
        SaveContextStateToMemory();

        ResetObjectiveVisualState();
        if (objectiveText) objectiveText.text = "";

        yield return FadeRoutine(1f, 0.01f);

        _isAnimatingUI = false;
        _uiAnimRoutine = null;

        if (_hasQueuedObjectiveChange && _queuedObjectiveIndex != -1)
        {
            int qi = _queuedObjectiveIndex;
            bool qsfx = _queuedPlaySfx;

            _hasQueuedObjectiveChange = false;
            _queuedObjectiveIndex = -1;
            _queuedPlaySfx = false;

            StartUIRoutine(AnimateChangeToIndexRoutine(qi, qsfx));
        }
    }

    private IEnumerator FadeRoutine(float target, float duration)
    {
        if (objectiveCanvasGroup == null) yield break;

        float start = objectiveCanvasGroup.alpha;

        if (Mathf.Approximately(duration, 0f))
        {
            objectiveCanvasGroup.alpha = target;
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            objectiveCanvasGroup.alpha = Mathf.Lerp(start, target, k);
            yield return null;
        }

        objectiveCanvasGroup.alpha = target;
    }

    private IEnumerator BounceRoutine()
    {
        if (objectiveText == null) yield break;

        RectTransform rt = objectiveText.rectTransform;
        if (rt == null) yield break;

        CacheBaseScale();

        float half = Mathf.Max(0.001f, bounceDuration * 0.5f);

        float t = 0f;
        Vector3 startScale = _baseScale * bounceStartScale;
        Vector3 peakScale = _baseScale * bouncePeakScale;

        rt.localScale = startScale;

        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / half);
            rt.localScale = Vector3.Lerp(startScale, peakScale, k);
            yield return null;
        }

        t = 0f;
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / half);
            rt.localScale = Vector3.Lerp(peakScale, _baseScale, k);
            yield return null;
        }

        rt.localScale = _baseScale;
    }

    private void CacheTmpOverridesIfNeeded()
    {
        if (objectiveText == null) { _cachedTmpGradientValid = false; return; }

        _cachedEnableVertexGradient = objectiveText.enableVertexGradient;
        _cachedColorGradient = objectiveText.colorGradient;
        _cachedTmpGradientValid = true;
    }

    private void ApplyGreenFlashVisuals()
    {
        if (objectiveText == null) return;

        CacheTmpOverridesIfNeeded();

        objectiveText.enableVertexGradient = false;
        objectiveText.color = completedColor;
        objectiveText.ForceMeshUpdate();
    }

    private void RestoreAfterGreenFlashVisuals()
    {
        if (objectiveText == null) return;
        if (!_cachedTmpGradientValid) return;

        objectiveText.enableVertexGradient = _cachedEnableVertexGradient;
        objectiveText.colorGradient = _cachedColorGradient;

        CacheNormalColorIfNeeded();
        objectiveText.color = normalColor;

        objectiveText.ForceMeshUpdate();
    }

    private void ResetObjectiveVisualState()
    {
        if (objectiveText == null) return;

        CacheNormalColorIfNeeded();
        objectiveText.color = normalColor;

        if (_cachedTmpGradientValid)
        {
            objectiveText.enableVertexGradient = _cachedEnableVertexGradient;
            objectiveText.colorGradient = _cachedColorGradient;
        }

        if (objectiveCanvasGroup != null)
            objectiveCanvasGroup.alpha = 1f;

        var rt = objectiveText.rectTransform;
        if (rt != null)
        {
            CacheBaseScale();
            rt.localScale = _baseScale;
        }
    }

    private void CacheBaseScale()
    {
        if (objectiveText == null) return;
        var rt = objectiveText.rectTransform;
        if (rt == null) return;

        if (_baseScale == Vector3.zero) _baseScale = Vector3.one;
        _baseScale = rt.localScale == Vector3.zero ? Vector3.one : rt.localScale;
    }

    private void PlayObjectiveSfx()
    {
        if (objectiveChangedSfx == null) return;

        if (sfxSource == null)
            sfxSource = GetComponent<AudioSource>();

        if (sfxSource == null) return;

        sfxSource.PlayOneShot(objectiveChangedSfx, objectiveSfxVolume);
    }

    // PlayerPrefs legacy kept but disabled by design
    private void WipeProfileProgress() { }
    private void LoadFromProfileOrDefault(bool playSfx) { LoadContextStateFromMemoryOrInit(playSfx); }
    private void SaveToProfile() { }
}
