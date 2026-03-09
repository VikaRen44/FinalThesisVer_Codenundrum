using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class AssessmentMusicOverride : MonoBehaviour
{
    [Header("Assessment Music")]
    [Tooltip("AudioSource that plays ONLY during assessment.")]
    public AudioSource assessmentMusicSource;

    public AudioClip assessmentBgm;

    [Tooltip("Mixer group used by normal BGM (must be controlled by master volume).")]
    public AudioMixerGroup bgmMixerGroup;

    [Header("Override Other BGM")]
    [Tooltip("Other BGM sources to pause while assessment is active.")]
    public List<AudioSource> otherBgmSources = new List<AudioSource>();

    [Header("Fade Settings")]
    public bool useFade = true;
    public float fadeInSeconds = 0.25f;
    public float fadeOutSeconds = 0.25f;

    private readonly Dictionary<AudioSource, bool> _wasPlaying = new();
    private Coroutine _fadeRoutine;

    private void Awake()
    {
        // Safety: auto-create AudioSource if missing
        if (assessmentMusicSource == null)
        {
            assessmentMusicSource = GetComponent<AudioSource>();
            if (assessmentMusicSource == null)
                assessmentMusicSource = gameObject.AddComponent<AudioSource>();
        }

        assessmentMusicSource.playOnAwake = false;
        assessmentMusicSource.loop = true;

        // IMPORTANT: route through same mixer as normal BGM
        if (bgmMixerGroup != null)
            assessmentMusicSource.outputAudioMixerGroup = bgmMixerGroup;
    }

    // ===================== PUBLIC API =====================

    public void StartAssessmentMusic()
    {
        if (assessmentBgm == null || assessmentMusicSource == null)
        {
            Debug.LogWarning("[AssessmentMusicOverride] Missing assessment BGM or AudioSource.");
            return;
        }

        PauseOtherBgm();

        assessmentMusicSource.clip = assessmentBgm;

        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);

        if (!useFade)
        {
            assessmentMusicSource.Play();
        }
        else
        {
            _fadeRoutine = StartCoroutine(FadeIn());
        }
    }

    public void StopAssessmentMusic()
    {
        if (assessmentMusicSource == null) return;

        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);

        if (!useFade)
        {
            assessmentMusicSource.Stop();
            ResumeOtherBgm();
        }
        else
        {
            _fadeRoutine = StartCoroutine(FadeOutThenStop());
        }
    }

    // ===================== INTERNAL =====================

    private void PauseOtherBgm()
    {
        _wasPlaying.Clear();

        foreach (var src in otherBgmSources)
        {
            if (src == null) continue;

            bool playing = src.isPlaying;
            _wasPlaying[src] = playing;

            if (playing)
                src.Pause();
        }
    }

    private void ResumeOtherBgm()
    {
        foreach (var kv in _wasPlaying)
        {
            if (kv.Key == null) continue;

            if (kv.Value)
                kv.Key.UnPause();
        }

        _wasPlaying.Clear();
    }

    private IEnumerator FadeIn()
    {
        assessmentMusicSource.volume = 0f;
        assessmentMusicSource.Play();

        float t = 0f;
        float dur = Mathf.Max(0.001f, fadeInSeconds);

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            assessmentMusicSource.volume = Mathf.Clamp01(t / dur);
            yield return null;
        }

        assessmentMusicSource.volume = 1f;
        _fadeRoutine = null;
    }

    private IEnumerator FadeOutThenStop()
    {
        float startVol = assessmentMusicSource.volume;
        float t = 0f;
        float dur = Mathf.Max(0.001f, fadeOutSeconds);

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            assessmentMusicSource.volume = Mathf.Lerp(startVol, 0f, t / dur);
            yield return null;
        }

        assessmentMusicSource.Stop();
        assessmentMusicSource.volume = startVol;

        ResumeOtherBgm();
        _fadeRoutine = null;
    }
}
