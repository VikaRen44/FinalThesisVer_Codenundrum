using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

[RequireComponent(typeof(Slider))]
public class MasterVolumeSliderUI : MonoBehaviour
{
    [Header("Mixer")]
    public AudioMixer masterMixer;
    public string exposedParam = "MasterVolume";

    [Header("Save")]
    public string prefsKey = "settings_masterVolume";
    [Range(0f, 1f)] public float defaultValue = 1f;

    [Header("Volume Mapping (Slider 0..1 -> dB)")]
    public float minDb = -40f;
    public float maxDb = 0f;

    [Header("Behavior")]
    [Tooltip("If true, writes debug logs when slider changes.")]
    public bool debugLogs = true;

    [Tooltip("Prevents tiny values from instantly going extremely quiet. Example: 0.05 means the lowest usable slider is 5%.")]
    [Range(0f, 0.2f)]
    public float minLinearClamp = 0.02f;

    private Slider _slider;
    private bool _initialized;

    private void Awake()
    {
        _slider = GetComponent<Slider>();
        ForceSliderRange();
        _slider.onValueChanged.AddListener(OnSliderChanged);
    }

    private void OnEnable()
    {
        ForceSliderRange();

        float v = PlayerPrefs.GetFloat(prefsKey, defaultValue);
        v = Mathf.Clamp01(v);

        // Apply clamp so loaded values can't be microscopic
        if (v > 0f) v = Mathf.Max(v, minLinearClamp);

        _slider.SetValueWithoutNotify(v);
        ApplyToMixer(v);

        _initialized = true;

        if (debugLogs)
            Debug.Log($"[MasterVolumeSliderUI] OnEnable load v={v:0.000} (prefsKey={prefsKey})");
    }

    private void OnDisable()
    {
        _initialized = false;
    }

    private void OnDestroy()
    {
        if (_slider != null)
            _slider.onValueChanged.RemoveListener(OnSliderChanged);
    }

    private void ForceSliderRange()
    {
        if (_slider == null) return;

        // Force correct UI behavior even if inspector has old values (0-100 etc.)
        _slider.minValue = 0f;
        _slider.maxValue = 1f;
        _slider.wholeNumbers = false;
    }

    private void OnSliderChanged(float v)
    {
        if (!_initialized) return;

        v = Mathf.Clamp01(v);

        // Avoid extreme jump to silence unless user truly hits 0
        if (v > 0f) v = Mathf.Max(v, minLinearClamp);

        ApplyToMixer(v);

        PlayerPrefs.SetFloat(prefsKey, v);
        PlayerPrefs.Save();

        if (debugLogs)
        {
            float lo = Mathf.Min(minDb, maxDb);
            float hi = Mathf.Max(minDb, maxDb);
            float dB = Mathf.Lerp(lo, hi, v);
            Debug.Log($"[MasterVolumeSliderUI] Changed v={v:0.000} => {dB:0.0} dB");
        }
    }

    private void ApplyToMixer(float linear01)
    {
        if (masterMixer == null) return;

        float lo = Mathf.Min(minDb, maxDb);
        float hi = Mathf.Max(minDb, maxDb);

        float dB = Mathf.Lerp(lo, hi, Mathf.Clamp01(linear01));

        masterMixer.SetFloat(exposedParam, dB);
    }
}
