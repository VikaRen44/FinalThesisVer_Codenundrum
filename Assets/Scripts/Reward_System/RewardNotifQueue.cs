using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RewardNotifQueue : MonoBehaviour
{
    public static RewardNotifQueue Instance { get; private set; }

    [Header("Refs")]
    public RewardNotifUI notifUI;
    public RewardCatalogSO catalog;

    [Header("Auto Resolve UI (FIX for hub return)")]
    [Tooltip("If true, whenever a new scene loads we try to find RewardNotifUI in that scene.")]
    public bool autoResolveNotifUIOnSceneLoad = true;

    [Tooltip("Optional: if your notif UI object has a known name, we can prefer finding it by name.")]
    public string notifUIObjectName = "RewardNotif_UI";

    [Tooltip("If true, we will keep trying to find notifUI while queue is playing.")]
    public bool keepTryingToResolveWhilePlaying = true;

    [Header("Behavior")]
    public bool listenToRewardStateManager = true;

    [Header("Timing")]
    [Tooltip("How long the notification stays on-screen before sliding out.")]
    [Min(0.1f)] public float displaySeconds = 1.4f;

    [Tooltip("Extra gap between notifications (after one finishes sliding out).")]
    [Min(0f)] public float gapSeconds = 0.08f;

    private readonly Queue<QueueItem> _queue = new Queue<QueueItem>();
    private bool _playing;

    private struct QueueItem
    {
        public string rewardId;
        public float holdSeconds;
        public QueueItem(string id, float hold)
        {
            rewardId = id;
            holdSeconds = hold;
        }
    }

    private Coroutine _hookRoutine;

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

        if (autoResolveNotifUIOnSceneLoad)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this && autoResolveNotifUIOnSceneLoad)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnEnable()
    {
        // Hook safely (RewardStateManager might not exist yet on the first frame)
        StartHookRoutine();
    }

    private void OnDisable()
    {
        StopHookRoutine();
        Hook(false);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // UI got destroyed and recreated -> reacquire it
        ResolveNotifUI();
    }

    // ---------------- HOOKING ----------------
    private void StartHookRoutine()
    {
        StopHookRoutine();
        _hookRoutine = StartCoroutine(HookWhenReady());
    }

    private void StopHookRoutine()
    {
        if (_hookRoutine != null)
        {
            StopCoroutine(_hookRoutine);
            _hookRoutine = null;
        }
    }

    private IEnumerator HookWhenReady()
    {
        // try immediately
        ResolveNotifUI();

        // wait for RewardStateManager instance if needed
        float timeout = 2.0f;
        float t = 0f;

        while (listenToRewardStateManager && RewardStateManager.Instance == null && t < timeout)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        Hook(true);
        _hookRoutine = null;
    }

    private void Hook(bool on)
    {
        if (!listenToRewardStateManager) return;
        if (RewardStateManager.Instance == null) return;

        if (on)
            RewardStateManager.Instance.OnRewardEarned += HandleRewardEarned;
        else
            RewardStateManager.Instance.OnRewardEarned -= HandleRewardEarned;
    }

    private void HandleRewardEarned(string rewardId, bool earnedAsPending)
    {
        Enqueue(rewardId);
    }

    // ---------------- PUBLIC API ----------------

    /// <summary>Change the default on-screen duration for future notifications.</summary>
    public void SetDisplaySeconds(float seconds)
    {
        displaySeconds = Mathf.Max(0.1f, seconds);
    }

    /// <summary>Enqueue with default duration.</summary>
    public void Enqueue(string rewardId)
    {
        Enqueue(rewardId, displaySeconds);
    }

    /// <summary>Enqueue with a custom duration for THIS reward only.</summary>
    public void Enqueue(string rewardId, float holdSecondsOverride)
    {
        if (string.IsNullOrWhiteSpace(rewardId)) return;

        // Prevent duplicates stacking in the same burst
        foreach (var item in _queue)
            if (item.rewardId == rewardId)
                return;

        _queue.Enqueue(new QueueItem(rewardId, Mathf.Max(0.1f, holdSecondsOverride)));

        if (!_playing)
            StartCoroutine(PlayQueue());
    }

    // ---------------- CORE ----------------

    private IEnumerator PlayQueue()
    {
        _playing = true;

        while (_queue.Count > 0)
        {
            // If notifUI is missing because we changed scenes, reacquire
            if (notifUI == null || notifUI.Equals(null))
            {
                ResolveNotifUI();

                if ((notifUI == null || notifUI.Equals(null)) && keepTryingToResolveWhilePlaying)
                {
                    // Wait until it exists (ex: returning to hub)
                    while (notifUI == null || notifUI.Equals(null))
                    {
                        ResolveNotifUI();
                        yield return null;
                    }
                }
            }

            var item = _queue.Dequeue();
            string id = item.rewardId;

            string display = id;
            Sprite icon = null;

            if (catalog != null && catalog.TryGet(id, out var entry) && entry != null)
            {
                if (!string.IsNullOrWhiteSpace(entry.displayName)) display = entry.displayName;
                icon = entry.icon;
            }

            if (notifUI != null && !notifUI.Equals(null))
            {
                // IMPORTANT: RewardNotifUI must have ShowForSeconds(...)
                notifUI.ShowForSeconds(display, icon, item.holdSeconds);

                // wait until notifUI disables itself (end of animation)
                while (notifUI != null && notifUI.gameObject.activeSelf)
                    yield return null;

                if (gapSeconds > 0f)
                    yield return new WaitForSecondsRealtime(gapSeconds);
            }
            else
            {
                Debug.LogWarning("[RewardNotifQueue] notifUI could not be resolved. Waiting...");
                yield return null;
            }
        }

        _playing = false;
    }

    // ---------------- UI RESOLVE ----------------

    private void ResolveNotifUI()
    {
        // If reference is still valid, keep it
        if (notifUI != null && !notifUI.Equals(null))
            return;

        notifUI = null;

        // Prefer by exact name if provided
        if (!string.IsNullOrWhiteSpace(notifUIObjectName))
        {
            var go = GameObject.Find(notifUIObjectName);
            if (go != null)
            {
                var ui = go.GetComponentInChildren<RewardNotifUI>(true);
                if (ui != null)
                {
                    notifUI = ui;
                    return;
                }
            }
        }

        // Fallback: find any RewardNotifUI in the loaded scene
        var any = Object.FindFirstObjectByType<RewardNotifUI>(FindObjectsInactive.Include);
        if (any != null)
            notifUI = any;
    }
}