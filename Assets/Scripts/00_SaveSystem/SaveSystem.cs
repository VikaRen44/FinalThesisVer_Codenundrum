using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;

public static class SaveSystem
{
    public const int MaxSlots = 9;

    // ==============================
    // ✅ PROFILE / CONTAINER SUPPORT
    // ==============================
    private const string ProfilesFolderName = "Profiles";
    private const string PREF_ACTIVE_PROFILE = "active_profile_name";

    /// <summary>Optional manual override for the absolute root. If set, profile folders go inside it.</summary>
    public static string CustomRootOverride = null;

#if UNITY_EDITOR
    public const string EditorProjectSavesSubdir = "Saves";   // -> Assets/Saves
#endif

    // ======================================================================
    // ✅ OPTION B: Save-time forced selectedCharacterIndex provider
    // ======================================================================
    public static Func<int> GetSelectedCharacterIndexForSave;

    // Optional inference list (if you want)
    public static string[] CharacterPrefabNamesForInference;

    // ======================================================================
    // ✅ Robust load cache (NO SaveGameManager.TryGetPendingSlot needed)
    // ======================================================================
    private static int s_pendingLoadSlot = -1;
    private static int s_pendingLoadSelectedIndex = 0;
    private static bool s_hasPendingLoadSelectedIndex = false;

    // ======================================================================
    // ✅ NEW: TEMP PROFILE OVERRIDE (UI browse/search)
    // ======================================================================
    private static string s_profileOverride = null;

    public static void SetProfileOverride(string profileName)
    {
        profileName = SanitizeProfileName(profileName);
        s_profileOverride = string.IsNullOrWhiteSpace(profileName) ? null : profileName;

        Debug.Log($"[SaveSystem] Profile override set: '{s_profileOverride}' (active='{GetActiveProfile()}')");
    }

    public static void ClearProfileOverride()
    {
        if (!string.IsNullOrWhiteSpace(s_profileOverride))
            Debug.Log($"[SaveSystem] Profile override cleared (was '{s_profileOverride}')");

        s_profileOverride = null;
    }

    public static bool HasProfileOverride()
    {
        return !string.IsNullOrWhiteSpace(s_profileOverride);
    }

    /// <summary>
    /// Effective profile used by SaveSystem paths right now:
    /// override (if set) else active profile.
    /// </summary>
    public static string GetEffectiveProfile()
    {
        if (!string.IsNullOrWhiteSpace(s_profileOverride))
            return s_profileOverride;

        return GetActiveProfile();
    }

    /// <summary>
    /// Call this ONLY when the user actually decides to LOAD a save from the override profile.
    /// This converts the override into the new active gameplay profile.
    /// </summary>
    public static void CommitOverrideAsActiveProfile()
    {
        if (string.IsNullOrWhiteSpace(s_profileOverride)) return;

        string p = s_profileOverride;
        SetActiveProfile(p);
        ClearProfileOverride();

        Debug.Log($"[SaveSystem] Override committed as active profile: '{p}'");
    }

    // ======================================================================

    public static bool TryGetPendingLoadSelection(out int slot, out int selectedIndex)
    {
        slot = s_pendingLoadSlot;
        selectedIndex = s_pendingLoadSelectedIndex;
        return s_pendingLoadSlot > 0 && s_hasPendingLoadSelectedIndex;
    }

    public static void ClearPendingLoadSelection()
    {
        s_pendingLoadSlot = -1;
        s_pendingLoadSelectedIndex = 0;
        s_hasPendingLoadSelectedIndex = false;
    }

    public static void CachePendingLoadSelection(int slot)
    {
        s_pendingLoadSlot = slot;
        s_hasPendingLoadSelectedIndex = false;

        if (slot <= 0) return;

        if (PeekSelectedCharacterIndex(slot, out int idx))
        {
            s_pendingLoadSelectedIndex = idx;
            s_hasPendingLoadSelectedIndex = true;
            Debug.Log($"[SaveSystem] ✅ Cached pending load selection: slot={slot}, selectedCharacterIndex={idx}");
        }
        else
        {
            Debug.LogWarning($"[SaveSystem] CachePendingLoadSelection: could not read selectedCharacterIndex for slot {slot} (non-fatal).");
        }
    }

    // ✅ FULL FIX: uses hasSelectedCharacterIndex first, then prefab name fallback, then raw index
    private static bool PeekSelectedCharacterIndex(int slot, out int idx)
    {
        idx = 0;
        try
        {
            var path = SlotJsonReadPath(slot);
            if (!File.Exists(path)) return false;

            var text = File.ReadAllText(path, Encoding.UTF8);
            var data = JsonUtility.FromJson<SaveData>(text);
            if (data == null) return false;

            // ✅ Preferred: explicit saved index
            if (data.hasSelectedCharacterIndex)
            {
                idx = data.selectedCharacterIndex;
                return true;
            }

            // ✅ Fallback: try prefab name match
            if (!string.IsNullOrWhiteSpace(data.selectedCharacterPrefabName) &&
                CharacterPrefabNamesForInference != null && CharacterPrefabNamesForInference.Length > 0)
            {
                string want = data.selectedCharacterPrefabName.Trim();
                for (int i = 0; i < CharacterPrefabNamesForInference.Length; i++)
                {
                    var n = (CharacterPrefabNamesForInference[i] ?? "").Trim();
                    if (string.IsNullOrEmpty(n)) continue;

                    if (string.Equals(n, want, StringComparison.Ordinal))
                    {
                        idx = i;
                        return true;
                    }
                }
            }

            // ✅ Final fallback: old saves without the flag
            idx = data.selectedCharacterIndex;
            return true;
        }
        catch { return false; }
    }

    // ==============================
    // ✅ PROFILE API
    // ==============================
    public static string GetActiveProfile()
    {
        return PlayerPrefs.GetString(PREF_ACTIVE_PROFILE, "");
    }

    public static void SetActiveProfile(string profileName)
    {
        profileName = SanitizeProfileName(profileName);

        PlayerPrefs.SetString(PREF_ACTIVE_PROFILE, profileName);
        PlayerPrefs.Save();

        Debug.Log($"[SaveSystem] Active profile set: '{profileName}'");
    }

    public static bool HasActiveProfile()
    {
        return !string.IsNullOrWhiteSpace(GetActiveProfile());
    }

    public static bool DoesProfileExist(string profileName)
    {
        profileName = SanitizeProfileName(profileName);
        if (string.IsNullOrWhiteSpace(profileName)) return false;

        var dir = GetProfileDirectory(profileName);
        return Directory.Exists(dir);
    }

    public static string[] ListProfiles()
    {
        try
        {
            var baseDir = ProfilesBaseDir;
            if (!Directory.Exists(baseDir)) return Array.Empty<string>();

            var dirs = Directory.GetDirectories(baseDir);
            for (int i = 0; i < dirs.Length; i++)
                dirs[i] = Path.GetFileName(dirs[i]);

            return dirs;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    // ==============================
    // ✅ ROOT PATHS
    // ==============================
    static string BaseRoot
    {
        get
        {
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(CustomRootOverride))
                return CustomRootOverride;
            return Path.Combine(Application.dataPath, EditorProjectSavesSubdir);
#else
            return Application.persistentDataPath;
#endif
        }
    }

    static string ProfilesBaseDir => Path.Combine(BaseRoot, ProfilesFolderName);

    static string GetProfileDirectory(string profileName)
    {
        profileName = SanitizeProfileName(profileName);
        return Path.Combine(ProfilesBaseDir, profileName);
    }

    static string Root
    {
        get
        {
            // ✅ Effective profile = override (browse) OR active (gameplay)
            var p = GetEffectiveProfile();

            if (string.IsNullOrWhiteSpace(p))
                return BaseRoot;

            return GetProfileDirectory(p);
        }
    }

    private static string SanitizeProfileName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        raw = raw.Trim();

        foreach (char c in Path.GetInvalidFileNameChars())
            raw = raw.Replace(c.ToString(), "");

        raw = raw.Trim();

        if (raw.Length > 20) raw = raw.Substring(0, 20);

        return raw;
    }

    // ---------- PATH HELPERS ----------
    static string SlotJsonReadPath(int slot)
    {
        var withExt = Path.Combine(Root, $"slot_{slot}.json");
        var legacy = Path.Combine(Root, $"slot_{slot}");
        if (File.Exists(withExt)) return withExt;
        if (File.Exists(legacy)) return legacy;
        return withExt;
    }

    static string SlotPngReadPath(int slot)
    {
        var withExt = Path.Combine(Root, $"slot_{slot}.png");
        var legacy = Path.Combine(Root, $"slot_{slot}");
        if (File.Exists(withExt)) return withExt;
        if (File.Exists(legacy)) return legacy;
        return withExt;
    }

    static string SlotJsonWritePath(int slot) => Path.Combine(Root, $"slot_{slot}.json");
    static string SlotPngWritePath(int slot) => Path.Combine(Root, $"slot_{slot}.png");

    // ---------- BASIC API ----------
    public static bool SlotExists(int slot) => File.Exists(SlotJsonReadPath(slot));

    public static void DeleteSlot(int slot)
    {
        var paths = new[]
        {
            SlotJsonReadPath(slot),
            Path.Combine(Root, $"slot_{slot}.json"),
            Path.Combine(Root, $"slot_{slot}"),
            SlotPngReadPath(slot),
            Path.Combine(Root, $"slot_{slot}.png")
        };

        foreach (var p in paths)
            if (!string.IsNullOrEmpty(p) && File.Exists(p)) File.Delete(p);

#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();
#endif
    }

    [Serializable]
    public class SaveHeader
    {
        public string sceneName;
        public string saveLabel;
        public string playerName;
        public long totalPlaytimeSeconds;
        public long realWorldUnixSeconds;
        public bool hasThumbnail;
    }

    public static IEnumerable<(int slot, SaveHeader header)> EnumerateSlots()
    {
        for (int i = 1; i <= MaxSlots; i++)
        {
            var jsonPath = SlotJsonReadPath(i);
            if (!File.Exists(jsonPath)) continue;

            SaveHeader header = null;
            try
            {
                var text = File.ReadAllText(jsonPath, Encoding.UTF8);
                var data = JsonUtility.FromJson<SaveData>(text);
                if (data == null) continue;

                header = new SaveHeader
                {
                    sceneName = data.sceneName,
                    saveLabel = data.saveLabel,
                    playerName = string.IsNullOrWhiteSpace(data.playerName) ? "Player" : data.playerName,
                    totalPlaytimeSeconds = data.totalPlaytimeSeconds,
                    realWorldUnixSeconds = data.realWorldUnixSeconds,
                    hasThumbnail = File.Exists(SlotPngReadPath(i))
                };
            }
            catch { }

            if (header != null)
                yield return (i, header);
        }
    }

    public static void Save(int slot, SaveData data, Texture2D optionalThumbnail = null)
    {
        // ✅ Always save to the CURRENT EFFECTIVE profile.
        Directory.CreateDirectory(Root);

        // ✅ FULL FIX: never override a verified index
        try
        {
            if (data.hasSelectedCharacterIndex)
            {
                // optional clamp if inference list exists
                if (CharacterPrefabNamesForInference != null && CharacterPrefabNamesForInference.Length > 0)
                    data.selectedCharacterIndex = Mathf.Clamp(data.selectedCharacterIndex, 0, CharacterPrefabNamesForInference.Length - 1);
            }
            else
            {
                int forced = -1;

                if (GetSelectedCharacterIndexForSave != null)
                {
                    forced = GetSelectedCharacterIndexForSave.Invoke();
                    if (forced < 0) forced = -1;
                }

                if (forced < 0)
                {
                    if (TryInferSelectedCharacterIndexFromSceneNames(out int inferred))
                        forced = inferred;
                }

                if (forced >= 0)
                {
                    data.selectedCharacterIndex = forced;
                    data.hasSelectedCharacterIndex = true;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveSystem] selectedCharacterIndex forcing failed (non-fatal).\n{e}");
        }

        CaptureWorldInto(data);

        var jsonPath = SlotJsonWritePath(slot);
        var json = JsonUtility.ToJson(data, prettyPrint: true);
        File.WriteAllText(jsonPath, json, Encoding.UTF8);

        if (optionalThumbnail != null)
        {
            var pngPath = SlotPngWritePath(slot);
            var png = optionalThumbnail.EncodeToPNG();
            File.WriteAllBytes(pngPath, png);
        }

#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();
#endif

        Debug.Log($"[SaveSystem] Saved slot {slot} at {jsonPath}");
    }

    public static SaveData Load(int slot)
    {
        var path = SlotJsonReadPath(slot);
        if (!File.Exists(path))
        {
            Debug.Log($"[SaveSystem] Slot {slot} missing for effectiveProfile='{GetEffectiveProfile()}' at: {path}");
            return null;
        }

        try
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            var data = JsonUtility.FromJson<SaveData>(text);

            if (data == null)
                Debug.LogError($"[SaveSystem] Slot {slot} JSON parsed NULL. Path={path}");
            else
                Debug.Log($"[SaveSystem] Loaded slot {slot} OK. scene={data.sceneName}, label={data.saveLabel}, objects={(data.objects != null ? data.objects.Count : 0)}");

            return data;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] Slot {slot} failed to load.\nPath={path}\n{e}");
            return null;
        }
    }

    public static Texture2D LoadThumbnail(int slot)
    {
        var path = SlotPngReadPath(slot);
        if (!File.Exists(path)) return null;

        try
        {
            var bytes = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(bytes);
            return tex;
        }
        catch { return null; }
    }

    // ---------- AUTO-SLOT HELPERS ----------
    public static int GetNextSaveSlot()
    {
        for (int i = 1; i <= MaxSlots; i++)
            if (!SlotExists(i)) return i;
        return -1;
    }

    public static int GetOldestSlot()
    {
        long oldestTs = long.MaxValue;
        int oldestSlot = -1;

        for (int i = 1; i <= MaxSlots; i++)
        {
            if (!SlotExists(i)) continue;

            try
            {
                string path = SlotJsonReadPath(i);
                string text = File.ReadAllText(path, Encoding.UTF8);
                SaveData data = JsonUtility.FromJson<SaveData>(text);

                if (data != null && data.realWorldUnixSeconds < oldestTs)
                {
                    oldestTs = data.realWorldUnixSeconds;
                    oldestSlot = i;
                }
            }
            catch { }
        }

        return oldestSlot;
    }

    public static int ChooseAutoSaveSlot()
    {
        int empty = GetNextSaveSlot();
        if (empty != -1) return empty;

        int oldest = GetOldestSlot();
        if (oldest != -1) return oldest;

        return 1;
    }

    // ======================================================================
    // ✅ SaveID + ISaveable capture/restore (Unity 2022 compatible)
    // ======================================================================

    [Serializable]
    private class MultiPayload
    {
        public List<Entry> entries = new List<Entry>();

        [Serializable]
        public class Entry
        {
            public string type;  // AssemblyQualifiedName (preferred)
            public string state; // JSON/string state
        }
    }

    public static void CaptureWorldInto(SaveData data)
    {
        if (data == null) return;

        if (data.objects == null)
            data.objects = new List<SaveData.ObjectSnapshot>();
        else
            data.objects.Clear();

        var saveIds = FindAllSaveIDs(includeInactive: true);
        if (saveIds == null || saveIds.Length == 0) return;

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var sid in saveIds)
        {
            if (sid == null) continue;

            if (sid.CompareTag("Player")) continue;
            if (sid.ID == "PLAYER") continue;

            string id = sid.ID;
            if (string.IsNullOrWhiteSpace(id) || id == "UNSET_ID") continue;

            if (!seen.Add(id))
                Debug.LogWarning($"[SaveSystem] Duplicate SaveID detected: '{id}'. Object='{sid.name}'");

            var monos = sid.GetComponents<MonoBehaviour>();
            MultiPayload mp = null;

            for (int i = 0; i < monos.Length; i++)
            {
                var mb = monos[i];
                if (mb == null) continue;

                if (mb is ISaveable saveable)
                {
                    try
                    {
                        var state = saveable.CaptureState() ?? "";

                        if (mp == null) mp = new MultiPayload();
                        mp.entries.Add(new MultiPayload.Entry
                        {
                            type = mb.GetType().AssemblyQualifiedName,
                            state = state
                        });
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[SaveSystem] CaptureState failed on '{sid.name}' '{mb.GetType().Name}'.\n{e}");
                    }
                }
            }

            if (mp == null || mp.entries == null || mp.entries.Count == 0)
                continue;

            data.objects.Add(new SaveData.ObjectSnapshot
            {
                id = id,
                payload = JsonUtility.ToJson(mp)
            });
        }
    }

    public static void RestoreWorldFrom(SaveData data, bool warnOnMissing = true)
    {
        if (data == null) return;
        if (data.objects == null || data.objects.Count == 0) return;

        var saveIds = FindAllSaveIDs(includeInactive: true);
        var map = new Dictionary<string, SaveID>(StringComparer.Ordinal);

        for (int i = 0; i < saveIds.Length; i++)
        {
            var sid = saveIds[i];
            if (sid == null) continue;
            if (string.IsNullOrWhiteSpace(sid.ID) || sid.ID == "UNSET_ID") continue;

            if (map.ContainsKey(sid.ID))
                Debug.LogWarning($"[SaveSystem] Duplicate SaveID '{sid.ID}' found during restore. Using '{sid.name}'");

            map[sid.ID] = sid;
        }

        for (int i = 0; i < data.objects.Count; i++)
        {
            var snap = data.objects[i];
            if (snap == null) continue;
            if (string.IsNullOrWhiteSpace(snap.id)) continue;

            if (!map.TryGetValue(snap.id, out var targetSid) || targetSid == null)
            {
                if (warnOnMissing)
                    Debug.LogWarning($"[SaveSystem] Restore: SaveID '{snap.id}' not found in scene.");
                continue;
            }

            MultiPayload mp = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(snap.payload))
                    mp = JsonUtility.FromJson<MultiPayload>(snap.payload);
            }
            catch { mp = null; }

            if (mp == null || mp.entries == null || mp.entries.Count == 0)
                continue;

            var monos = targetSid.GetComponents<MonoBehaviour>();

            for (int e = 0; e < mp.entries.Count; e++)
            {
                var entry = mp.entries[e];
                if (entry == null) continue;

                Type wanted = ResolveType(entry.type);

                MonoBehaviour best = null;

                for (int m = 0; m < monos.Length; m++)
                {
                    var mb = monos[m];
                    if (mb == null) continue;
                    if (!(mb is ISaveable)) continue;

                    var mbType = mb.GetType();

                    if (wanted != null && wanted == mbType)
                    {
                        best = mb;
                        break;
                    }

                    if (wanted == null && !string.IsNullOrWhiteSpace(entry.type))
                    {
                        if (entry.type.Contains(mbType.FullName))
                            best = mb;
                    }
                }

                if (best == null)
                {
                    if (warnOnMissing)
                        Debug.LogWarning($"[SaveSystem] Restore: Component '{entry.type}' not found on '{targetSid.name}' (SaveID='{snap.id}').");
                    continue;
                }

                try
                {
                    ((ISaveable)best).RestoreState(entry.state);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SaveSystem] RestoreState failed on '{targetSid.name}' '{best.GetType().Name}'.\n{ex}");
                }
            }
        }
    }

    private static Type ResolveType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return null;

        var t = Type.GetType(typeName);
        if (t != null) return t;

        try
        {
            string fullName = typeName;
            int comma = typeName.IndexOf(',');
            if (comma > 0) fullName = typeName.Substring(0, comma).Trim();

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type tt = null;
                try { tt = assemblies[i].GetType(fullName); }
                catch { tt = null; }
                if (tt != null) return tt;
            }
        }
        catch { }

        return null;
    }

    public static bool TryInferSelectedCharacterIndexFromSceneNames(out int index)
    {
        index = 0;

        if (CharacterPrefabNamesForInference == null || CharacterPrefabNamesForInference.Length == 0)
            return false;

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return false;

        string have = player.name.Replace("(Clone)", "").Trim();

        for (int i = 0; i < CharacterPrefabNamesForInference.Length; i++)
        {
            var want = (CharacterPrefabNamesForInference[i] ?? "").Trim();
            if (string.IsNullOrEmpty(want)) continue;

            if (string.Equals(want, have, StringComparison.Ordinal))
            {
                index = i;
                return true;
            }
        }

        var root = player.transform.root;
        if (root != null)
        {
            have = root.name.Replace("(Clone)", "").Trim();
            for (int i = 0; i < CharacterPrefabNamesForInference.Length; i++)
            {
                var want = (CharacterPrefabNamesForInference[i] ?? "").Trim();
                if (string.IsNullOrEmpty(want)) continue;

                if (string.Equals(want, have, StringComparison.Ordinal))
                {
                    index = i;
                    return true;
                }
            }
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

    public static void EnsureProfileFolderExists(string profileName)
    {
        profileName = SanitizeProfileName(profileName);
        if (string.IsNullOrWhiteSpace(profileName)) return;

        var dir = GetProfileDirectory(profileName);
        Directory.CreateDirectory(dir);
    }

    public static bool ProfileHasAnySlots(string profileName)
    {
        profileName = SanitizeProfileName(profileName);
        if (string.IsNullOrWhiteSpace(profileName)) return false;

        string dir = GetProfileDirectory(profileName);
        if (!Directory.Exists(dir)) return false;

        for (int slot = 1; slot <= MaxSlots; slot++)
        {
            string json = Path.Combine(dir, $"slot_{slot}.json");
            string legacy = Path.Combine(dir, $"slot_{slot}");

            if (File.Exists(json) || File.Exists(legacy))
                return true;
        }

        return false;
    }
}