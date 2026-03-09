using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RewardStateManager : MonoBehaviour
{
    public static RewardStateManager Instance { get; private set; }

    [Header("Hub Scene")]
    public string hubSceneName = "04_Gamehub";

    [Header("Auto Claim On Hub Load")]
    public bool autoClaimPendingOnHubLoad = true;

    [Header("Persistence Mode")]
    [Tooltip("If TRUE, rewards are saved per PROFILE file (rewards_<profile>.json). " +
             "This makes rewards shared across all saves of that profile.\n\n" +
             "If FALSE (recommended), rewards are stored PER SAVE SLOT via SaveData.rewardPayloadJson.")]
    public bool persistPerProfileFile = false;

    [Header("Debug")]
    public bool verboseLogs = false;

    // =========================================================
    // ✅ NEW: EVENTS (for UI popups / analytics / etc.)
    // =========================================================
    /// <summary>
    /// Fired ONLY when a reward is newly earned (not already unlocked/pending).
    /// earnedAsPending = true when MarkRewardPending() added it
    /// earnedAsPending = false when UnlockRewardImmediate() added it
    /// </summary>
    public event Action<string, bool> OnRewardEarned;

    /// <summary>
    /// Fired when pending rewards are claimed on hub load (pending -> unlocked).
    /// This is OPTIONAL for popups; you can ignore it if you only want "earned" popups.
    /// </summary>
    public event Action<List<string>> OnPendingRewardsClaimed;

    // =========================================================
    // Save Data (per runtime / per save-slot JSON)
    // =========================================================
    [Serializable]
    private class RewardSaveData
    {
        public List<string> unlockedRewards = new List<string>();
        public List<string> pendingRewards = new List<string>();
    }

    private RewardSaveData _save = new RewardSaveData();

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

        if (persistPerProfileFile)
            LoadForActiveProfile();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (persistPerProfileFile)
            LoadForActiveProfile();

        if (!autoClaimPendingOnHubLoad) return;

        if (IsSameScene(scene.name, hubSceneName))
        {
            ClaimAllPendingRewards();
        }
    }

    private bool IsSameScene(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================
    // EXPORT / IMPORT (PER SAVE SLOT)
    // =========================================================
    public string ExportSaveStateJson()
    {
        if (persistPerProfileFile)
            LoadForActiveProfile();

        if (_save == null) _save = new RewardSaveData();
        if (_save.unlockedRewards == null) _save.unlockedRewards = new List<string>();
        if (_save.pendingRewards == null) _save.pendingRewards = new List<string>();

        return JsonUtility.ToJson(_save);
    }

    public void ImportSaveStateJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            _save = new RewardSaveData();
            if (verboseLogs) Debug.Log("[RewardStateManager] ImportSaveStateJson: empty -> cleared rewards");
            return;
        }

        try
        {
            var loaded = JsonUtility.FromJson<RewardSaveData>(json);
            _save = loaded ?? new RewardSaveData();

            if (_save.unlockedRewards == null) _save.unlockedRewards = new List<string>();
            if (_save.pendingRewards == null) _save.pendingRewards = new List<string>();

            if (verboseLogs)
                Debug.Log($"[RewardStateManager] Imported rewards from save JSON (unlocked={_save.unlockedRewards.Count}, pending={_save.pendingRewards.Count})");

            if (persistPerProfileFile)
                SaveForActiveProfile();
        }
        catch (Exception e)
        {
            Debug.LogError("[RewardStateManager] ImportSaveStateJson failed: " + e.Message);
            _save = new RewardSaveData();
        }
    }

    public void ClearAllRewards()
    {
        _save = new RewardSaveData();

        if (persistPerProfileFile)
            SaveForActiveProfile();

        if (verboseLogs)
            Debug.Log("[RewardStateManager] Cleared all rewards.");
    }

    // =========================================================
    // PUBLIC API
    // =========================================================
    public void MarkRewardPending(string rewardId)
    {
        if (string.IsNullOrWhiteSpace(rewardId)) return;

        if (persistPerProfileFile)
            LoadForActiveProfile();

        if (_save.unlockedRewards.Contains(rewardId)) return;
        if (_save.pendingRewards.Contains(rewardId)) return;

        _save.pendingRewards.Add(rewardId);

        if (persistPerProfileFile)
            SaveForActiveProfile();

        if (verboseLogs)
            Debug.Log($"[RewardStateManager] MarkRewardPending: {rewardId}");

        // ✅ NEW EVENT
        OnRewardEarned?.Invoke(rewardId, true);
    }

    public void UnlockRewardImmediate(string rewardId)
    {
        if (string.IsNullOrWhiteSpace(rewardId)) return;

        if (persistPerProfileFile)
            LoadForActiveProfile();

        bool wasUnlocked = _save.unlockedRewards.Contains(rewardId);
        bool wasPending = _save.pendingRewards.Contains(rewardId);

        if (!wasUnlocked)
            _save.unlockedRewards.Add(rewardId);

        _save.pendingRewards.Remove(rewardId);

        if (persistPerProfileFile)
            SaveForActiveProfile();

        if (verboseLogs)
            Debug.Log($"[RewardStateManager] UnlockRewardImmediate: {rewardId}");

        // ✅ NEW EVENT (only if it was truly new)
        if (!wasUnlocked && !wasPending)
            OnRewardEarned?.Invoke(rewardId, false);
    }

    public bool IsRewardUnlocked(string rewardId)
    {
        if (string.IsNullOrWhiteSpace(rewardId)) return false;

        if (persistPerProfileFile)
            LoadForActiveProfile();

        return _save.unlockedRewards.Contains(rewardId);
    }

    public bool IsRewardPending(string rewardId)
    {
        if (string.IsNullOrWhiteSpace(rewardId)) return false;

        if (persistPerProfileFile)
            LoadForActiveProfile();

        return _save.pendingRewards.Contains(rewardId);
    }

    public void ClaimAllPendingRewards()
    {
        if (persistPerProfileFile)
            LoadForActiveProfile();

        if (_save.pendingRewards == null || _save.pendingRewards.Count == 0)
            return;

        // Copy before clearing (for event)
        var claimed = new List<string>(_save.pendingRewards);

        for (int i = 0; i < _save.pendingRewards.Count; i++)
        {
            string id = _save.pendingRewards[i];
            if (string.IsNullOrWhiteSpace(id)) continue;

            if (!_save.unlockedRewards.Contains(id))
                _save.unlockedRewards.Add(id);

            if (verboseLogs)
                Debug.Log($"[RewardStateManager] Claimed pending reward: {id}");
        }

        _save.pendingRewards.Clear();

        if (persistPerProfileFile)
            SaveForActiveProfile();

        // ✅ NEW EVENT
        OnPendingRewardsClaimed?.Invoke(claimed);
    }

    // =========================================================
    // SAVE / LOAD PER ACTIVE PROFILE (OPTIONAL)
    // =========================================================
    private string GetActiveProfileSafe()
    {
        try
        {
            string p = SaveSystem.GetActiveProfile();
            if (string.IsNullOrWhiteSpace(p)) p = "default";
            return p;
        }
        catch
        {
            return "default";
        }
    }

    private string GetSavePathForProfile(string profile)
    {
        if (string.IsNullOrWhiteSpace(profile)) profile = "default";
        return Path.Combine(Application.persistentDataPath, $"rewards_{profile}.json");
    }

    private string _loadedProfile = null;

    private void LoadForActiveProfile()
    {
        if (!persistPerProfileFile)
            return;

        string profile = GetActiveProfileSafe();

        if (!string.IsNullOrWhiteSpace(_loadedProfile) &&
            string.Equals(_loadedProfile, profile, StringComparison.Ordinal))
            return;

        _loadedProfile = profile;

        string path = GetSavePathForProfile(profile);

        try
        {
            if (!File.Exists(path))
            {
                _save = new RewardSaveData();
                if (verboseLogs)
                    Debug.Log($"[RewardStateManager] No reward file found. New profile file will be created when saving. ({profile})");
                return;
            }

            string json = File.ReadAllText(path);
            var loaded = JsonUtility.FromJson<RewardSaveData>(json);

            _save = loaded ?? new RewardSaveData();
            if (_save.unlockedRewards == null) _save.unlockedRewards = new List<string>();
            if (_save.pendingRewards == null) _save.pendingRewards = new List<string>();

            if (verboseLogs)
                Debug.Log($"[RewardStateManager] Loaded rewards for profile '{profile}' (unlocked={_save.unlockedRewards.Count}, pending={_save.pendingRewards.Count})");
        }
        catch (Exception e)
        {
            Debug.LogError("[RewardStateManager] Load failed: " + e.Message);
            _save = new RewardSaveData();
        }
    }

    private void SaveForActiveProfile()
    {
        if (!persistPerProfileFile)
            return;

        string profile = GetActiveProfileSafe();
        _loadedProfile = profile;

        string path = GetSavePathForProfile(profile);

        try
        {
            string json = JsonUtility.ToJson(_save, true);
            File.WriteAllText(path, json);

            if (verboseLogs)
                Debug.Log($"[RewardStateManager] Saved rewards for profile '{profile}'");
        }
        catch (Exception e)
        {
            Debug.LogError("[RewardStateManager] Save failed: " + e.Message);
        }
    }
}