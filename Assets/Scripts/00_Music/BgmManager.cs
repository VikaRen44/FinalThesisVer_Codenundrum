using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;



/// <summary>
/// Global BGM manager with:
/// - Scene-based auto BGM
/// - Fade transitions
/// - Snapshot STACK (so minigame snapshot + cutscene snapshot don't overwrite each other)
/// - Override lock depth (prevents auto scene BGM from fighting temporary overrides)
/// </summary>
public class BgmManager : MonoBehaviour
{
    public static BgmManager Instance { get; private set; }

    [Serializable]
    public class SceneBgmEntry
    {
        public string sceneName;
        public AudioClip bgm;
        [Range(0f, 1f)] public float volume = 1f;   // ✅ default stays 1
        public bool loop = true;                    // ✅ default stays true
    }

    [Header("Audio Source")]
    public AudioSource source;

    [Header("Mixer Output (optional)")]
    [Tooltip("Assign MasterAudio -> Music (or Master) group here so mixer volume affects BGM.")]
    public AudioMixerGroup musicMixerGroup;

    [Header("Scene BGM Map")]
    public bool autoPlayOnSceneLoaded = true;
    public List<SceneBgmEntry> sceneBgm = new List<SceneBgmEntry>();

    [Header("Fade")]
    public float defaultFadeOut = 0.6f;   // ✅ default stays 0.6
    public float defaultFadeIn = 0.6f;    // ✅ default stays 0.6
    public bool useUnscaledTime = true;   // ✅ default stays true

    [Header("Debug")]
    public bool verboseLogs = false;      // ✅ default stays false

    private Coroutine _fadeRoutine;

    // Override lock depth (if >0, sceneLoaded auto-play won't run)
    private int _overrideDepth = 0;

    // ✅ Snapshot stack
    [Serializable]
    private struct BgmSnapshot
    {
        public AudioClip clip;
        public float time;
        public float volume;
        public bool loop;
    }

    private readonly Stack<BgmSnapshot> _snapshots = new Stack<BgmSnapshot>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // ✅ FPS cap to reduce heat / unnecessary load
        QualitySettings.vSyncCount = 1;
        Application.targetFrameRate = 60;

        DontDestroyOnLoad(gameObject);

        EnsureSource();
        ApplyMixerOutput();

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void EnsureSource()
    {
        if (source != null) return;

        source = GetComponent<AudioSource>();
        if (source == null) source = gameObject.AddComponent<AudioSource>();

        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.loop = true;
    }

    private void ApplyMixerOutput()
    {
        if (source == null) return;

        // If assigned, route BGM through the mixer so MasterVolume affects it.
        if (musicMixerGroup != null)
            source.outputAudioMixerGroup = musicMixerGroup;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // In editor, keep routing updated when you drag a mixer group in inspector.
        if (!Application.isPlaying) return;

        EnsureSource();
        ApplyMixerOutput();
    }
#endif

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!autoPlayOnSceneLoaded) return;
        if (_overrideDepth > 0) return;

        if (TryGetSceneEntry(scene.name, out var entry) && entry.bgm != null)
        {
            if (verboseLogs) Debug.Log($"[BgmManager] SceneLoaded => '{scene.name}' BGM='{entry.bgm.name}'");
            Play(entry.bgm, entry.volume, entry.loop, defaultFadeOut, defaultFadeIn, forceRestart: false);
        }
    }

    private bool TryGetSceneEntry(string sceneName, out SceneBgmEntry entry)
    {
        entry = null;
        if (sceneBgm == null) return false;

        for (int i = 0; i < sceneBgm.Count; i++)
        {
            var e = sceneBgm[i];
            if (e == null) continue;
            if (string.Equals(e.sceneName, sceneName, StringComparison.Ordinal))
            {
                entry = e;
                return true;
            }
        }
        return false;
    }

    // =========================================================
    // Public API
    // =========================================================
    public void Play(AudioClip clip, float volume = 1f, bool loop = true,
                     float fadeOut = -1f, float fadeIn = -1f,
                     bool forceRestart = false)
    {
        EnsureSource();
        ApplyMixerOutput(); // ✅ NEW (in case mixer group assigned later)
        if (clip == null) return;

        if (!forceRestart && source.clip == clip && source.isPlaying)
        {
            source.volume = Mathf.Clamp01(volume);
            source.loop = loop;
            return;
        }

        float outDur = (fadeOut < 0f) ? defaultFadeOut : Mathf.Max(0f, fadeOut);
        float inDur = (fadeIn < 0f) ? defaultFadeIn : Mathf.Max(0f, fadeIn);

        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeSwapRoutine(clip, Mathf.Clamp01(volume), loop, outDur, inDur));
    }

    public void PlayInstant(AudioClip clip, float volume = 1f, bool loop = true)
    {
        EnsureSource();
        ApplyMixerOutput(); // ✅ NEW
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = null;

        source.clip = clip;
        source.volume = Mathf.Clamp01(volume);
        source.loop = loop;
        if (clip != null) source.Play();
    }

    public void PlaySceneBgmNow(string sceneName, float fadeOut = -1f, float fadeIn = -1f)
    {
        if (_overrideDepth > 0) return;

        if (TryGetSceneEntry(sceneName, out var entry) && entry.bgm != null)
            Play(entry.bgm, entry.volume, entry.loop, fadeOut, fadeIn, forceRestart: false);
    }

    // =========================================================
    // ✅ Snapshot Stack API
    // =========================================================
    public void PushSnapshot()
    {
        EnsureSource();

        if (source == null || source.clip == null)
        {
            _snapshots.Push(new BgmSnapshot { clip = null, time = 0f, volume = 1f, loop = true });
            return;
        }

        var snap = new BgmSnapshot
        {
            clip = source.clip,
            time = SafeGetTime(),
            volume = source.volume,
            loop = source.loop
        };

        _snapshots.Push(snap);

        if (verboseLogs && snap.clip != null)
            Debug.Log($"[BgmManager] PushSnapshot clip='{snap.clip.name}' time={snap.time:0.00} stack={_snapshots.Count}");
    }

    public void PopSnapshotAndRestore(bool fade = false, float fadeOut = 0.25f, float fadeIn = 0.25f)
    {
        if (_snapshots.Count == 0) return;

        var snap = _snapshots.Pop();
        if (snap.clip == null)
        {
            if (verboseLogs) Debug.Log($"[BgmManager] PopSnapshot => empty snapshot stack={_snapshots.Count}");
            return;
        }

        if (fade)
        {
            Play(snap.clip, snap.volume, snap.loop, fadeOut, fadeIn, forceRestart: true);
            StartCoroutine(SetTimeNextFrame(snap.time));
        }
        else
        {
            PlayInstant(snap.clip, snap.volume, snap.loop);
            TrySetTime(snap.time);
        }

        if (verboseLogs)
            Debug.Log($"[BgmManager] PopSnapshot restored clip='{snap.clip.name}' time={snap.time:0.00} stack={_snapshots.Count}");
    }

    private float SafeGetTime()
    {
        try { return source.time; } catch { return 0f; }
    }

    private IEnumerator SetTimeNextFrame(float t)
    {
        yield return null;
        TrySetTime(t);
    }

    private void TrySetTime(float t)
    {
        try
        {
            if (source != null && source.clip != null && source.clip.length > 0.05f)
                source.time = Mathf.Clamp(t, 0f, source.clip.length - 0.02f);
        }
        catch { }
    }

    // =========================================================
    // ✅ Override lock
    // =========================================================
    public void PushOverride() => _overrideDepth++;
    public void PopOverride() => _overrideDepth = Mathf.Max(0, _overrideDepth - 1);

    // =========================================================
    // Fade routine
    // =========================================================
    private IEnumerator FadeSwapRoutine(AudioClip newClip, float newVol, bool loop, float fadeOut, float fadeIn)
    {
        EnsureSource();

        float startVol = source.volume;

        if (source.isPlaying && fadeOut > 0f)
        {
            float t = 0f;
            while (t < fadeOut)
            {
                t += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                source.volume = Mathf.Lerp(startVol, 0f, t / fadeOut);
                yield return null;
            }
        }

        source.volume = 0f;
        source.Stop();
        source.clip = newClip;
        source.loop = loop;

        if (newClip != null)
            source.Play();

        if (fadeIn > 0f)
        {
            float t = 0f;
            while (t < fadeIn)
            {
                t += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                source.volume = Mathf.Lerp(0f, newVol, t / fadeIn);
                yield return null;
            }
        }

        source.volume = newVol;
        _fadeRoutine = null;
    }
}
