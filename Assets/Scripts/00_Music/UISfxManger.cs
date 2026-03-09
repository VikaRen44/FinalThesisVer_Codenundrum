using UnityEngine;
using UnityEngine.Audio;

public class UISfxManager : MonoBehaviour
{
    public static UISfxManager Instance { get; private set; }

    [Header("Output (optional)")]
    public AudioMixerGroup sfxMixerGroup;

    [Header("Clips")]
    public AudioClip hoverClip;
    public AudioClip clickClip;

    [Header("Volume")]
    [Range(0f, 1f)] public float hoverVolume = 0.7f; // ✅ default stays 0.7
    [Range(0f, 1f)] public float clickVolume = 0.9f; // ✅ default stays 0.9

    [Header("Pitch Variation (optional)")]
    public bool randomizePitch = true;               // ✅ default stays true
    [Range(0f, 0.5f)] public float pitchJitter = 0.05f; // ✅ default stays 0.05

    private AudioSource _source;

    private void Awake()
    {
        // Singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        _source = GetComponent<AudioSource>();
        if (_source == null) _source = gameObject.AddComponent<AudioSource>();

        _source.playOnAwake = false;
        _source.loop = false;
        _source.spatialBlend = 0f; // 2D

        ApplyMixerOutput(); // ✅ NEW
    }

    private void ApplyMixerOutput()
    {
        if (_source == null) return;

        if (sfxMixerGroup != null)
            _source.outputAudioMixerGroup = sfxMixerGroup;
        // If null, it will use default output (same behavior as before).
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Lets you drag the mixer group in inspector and have it apply immediately during play mode.
        if (!Application.isPlaying) return;
        ApplyMixerOutput();
    }
#endif

    public void PlayHover(float? volumeOverride = null)
    {
        ApplyMixerOutput(); // ✅ ensure routing stays correct
        PlayOneShot(hoverClip, volumeOverride ?? hoverVolume);
    }

    public void PlayClick(float? volumeOverride = null)
    {
        ApplyMixerOutput(); // ✅ ensure routing stays correct
        PlayOneShot(clickClip, volumeOverride ?? clickVolume);
    }

    public void PlayClip(AudioClip clip, float volume01 = 1f)
    {
        ApplyMixerOutput(); // ✅ ensure routing stays correct
        PlayOneShot(clip, volume01);
    }

    private void PlayOneShot(AudioClip clip, float volume01)
    {
        if (clip == null) return;

        if (randomizePitch)
            _source.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        else
            _source.pitch = 1f;

        _source.PlayOneShot(clip, Mathf.Clamp01(volume01));
    }
}
