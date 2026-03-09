using System;
using System.Collections.Generic;
using UnityEngine;

public class AssessmentScoreManager : MonoBehaviour
{
    public static AssessmentScoreManager Instance { get; private set; }

    [Header("Debug")]
    public bool verboseLogs = false;

    // ✅ UI can subscribe to this
    public event Action OnScoresChanged;

    [Serializable]
    public class ScoreEntry
    {
        public string key;
        public int bestScore;
        public int totalQuestions;
    }

    [Serializable]
    private class SavePayload
    {
        public List<ScoreEntry> entries = new List<ScoreEntry>();
    }

    private readonly Dictionary<string, ScoreEntry> _map =
        new Dictionary<string, ScoreEntry>(StringComparer.Ordinal);

    // =========================================================
    // ✅ Ensure Instance Exists (AUTO CREATE IF MISSING)
    // =========================================================
    public static AssessmentScoreManager EnsureInstance()
    {
        if (Instance != null) return Instance;

        // Try find existing in scene
        Instance = FindObjectOfType<AssessmentScoreManager>();
        if (Instance != null) return Instance;

        // Auto-create (prevents "Instance null" at hub refresh time)
        var go = new GameObject("AssessmentScoreManager");
        Instance = go.AddComponent<AssessmentScoreManager>();
        return Instance;
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
    }

    // =========================================================
    // PUBLIC API
    // =========================================================
    public void ClearAll()
    {
        _map.Clear();
        if (verboseLogs) Debug.Log("[AssessmentScoreManager] Cleared all scores.");
        NotifyChanged();
    }

    /// <summary>
    /// Record a score. Only updates if score is better than existing.
    /// </summary>
    public void RecordScore(string key, int score, int totalQuestions)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        key = key.Trim();
        score = Mathf.Max(0, score);
        totalQuestions = Mathf.Max(0, totalQuestions);

        bool changed = false;

        if (!_map.TryGetValue(key, out var e) || e == null)
        {
            e = new ScoreEntry { key = key, bestScore = score, totalQuestions = totalQuestions };
            _map[key] = e;

            changed = true;
            if (verboseLogs) Debug.Log($"[AssessmentScoreManager] New score key='{key}' score={score}/{totalQuestions}");
        }
        else
        {
            // Update if better
            if (score > e.bestScore)
            {
                e.bestScore = score;
                e.totalQuestions = totalQuestions;
                changed = true;

                if (verboseLogs) Debug.Log($"[AssessmentScoreManager] Improved key='{key}' => {score}/{totalQuestions}");
            }
            else
            {
                // Keep best score, but update total if it was missing/0
                if (e.totalQuestions <= 0 && totalQuestions > 0)
                {
                    e.totalQuestions = totalQuestions;
                    changed = true;
                }

                if (verboseLogs) Debug.Log($"[AssessmentScoreManager] Not improved key='{key}' kept {e.bestScore}/{e.totalQuestions}");
            }
        }

        if (changed) NotifyChanged();
    }

    public bool TryGetBest(string key, out int bestScore, out int totalQuestions)
    {
        bestScore = 0;
        totalQuestions = 0;

        if (string.IsNullOrWhiteSpace(key)) return false;
        key = key.Trim();

        if (_map.TryGetValue(key, out var e) && e != null)
        {
            bestScore = e.bestScore;
            totalQuestions = e.totalQuestions;
            return true;
        }

        return false;
    }

    // =========================================================
    // SAVE / LOAD (PER SAVE SLOT via SaveData)
    // =========================================================
    public string ExportSaveStateJson()
    {
        var payload = new SavePayload();

        foreach (var kv in _map)
        {
            var e = kv.Value;
            if (e == null || string.IsNullOrWhiteSpace(e.key)) continue;

            payload.entries.Add(new ScoreEntry
            {
                key = e.key,
                bestScore = e.bestScore,
                totalQuestions = e.totalQuestions
            });
        }

        return JsonUtility.ToJson(payload);
    }

    public void ImportSaveStateJson(string json)
    {
        _map.Clear();

        if (string.IsNullOrWhiteSpace(json))
        {
            if (verboseLogs) Debug.Log("[AssessmentScoreManager] Import: empty -> cleared");
            NotifyChanged();
            return;
        }

        try
        {
            var payload = JsonUtility.FromJson<SavePayload>(json);
            if (payload == null || payload.entries == null)
            {
                NotifyChanged();
                return;
            }

            for (int i = 0; i < payload.entries.Count; i++)
            {
                var e = payload.entries[i];
                if (e == null || string.IsNullOrWhiteSpace(e.key)) continue;

                e.key = e.key.Trim();
                _map[e.key] = e;
            }

            if (verboseLogs) Debug.Log($"[AssessmentScoreManager] Imported {payload.entries.Count} entries");
            NotifyChanged();
        }
        catch (Exception ex)
        {
            Debug.LogError("[AssessmentScoreManager] Import failed: " + ex.Message);
            _map.Clear();
            NotifyChanged();
        }
    }

    private void NotifyChanged()
    {
        try { OnScoresChanged?.Invoke(); } catch { }
    }
}