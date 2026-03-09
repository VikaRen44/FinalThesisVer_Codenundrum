using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Dialogue/Simple Sequence")]
public class SimpleDialogueSequenceSO : ScriptableObject
{
    [Header("Default Typing SFX (optional)")]
    public AudioClip defaultTypingSfx;
    [Range(0f, 1f)] public float defaultTypingSfxVolume = 0.6f;

    [Header("Master Volume Integration")]
    [Tooltip("If TRUE, typing SFX volume will be multiplied by the game's current Master Volume (0..1).")]
    public bool multiplyTypingByMasterVolume = true;

    [Tooltip("If TRUE, will also multiply by AudioListener.volume (global Unity volume). Usually keep FALSE when using AudioMixer master volume.")]
    public bool multiplyTypingByAudioListenerVolume = false;

    [Header("Master Volume Source (PlayerPrefs)")]
    [Tooltip("PlayerPrefs key used by your Settings UI to store master volume (0..1).")]
    public string masterVolumePrefsKey = "settings_masterVolume";

    [Tooltip("If the key does not exist yet, create it with this value when first read.")]
    public bool autoCreateMasterPrefsKeyIfMissing = true;

    [Range(0f, 1f)]
    public float defaultMasterVolumeIfMissing = 1f;

    [Header("Tandem Tutorial Hooks (optional)")]
    [Tooltip("If assigned, the tutorial can be shown BEFORE dialogue starts.")]
    public TutorialSequenceSO tutorialBeforeDialogue;

    [Tooltip("If assigned, the tutorial can be shown AFTER dialogue ends.")]
    public TutorialSequenceSO tutorialAfterDialogue;

    [Tooltip("Optional: show tutorial at a specific dialogue line index.")]
    public DialogueTutorialLink[] tutorialLinksPerLine;

    // ✅ NEW: AUTO-ADVANCE DEFAULTS
    [Header("Auto Advance Defaults (NEW)")]
    [Tooltip("If TRUE: lines auto-advance after typing finishes, using the delay below (unless overridden per-line).")]
    public bool autoAdvanceByDefault = false;

    [Tooltip("If auto-advance is enabled and a line doesn't override, wait this many seconds after typing ends.")]
    public float defaultAutoAdvanceSeconds = 0.8f;

    [Tooltip("If TRUE: pressing advance during the auto-advance wait skips the remaining delay.")]
    public bool allowSkipAutoAdvanceWait = true;

    public SimpleDialogueLine[] lines;

    // ==========================
    // EXISTING FUNCTIONS (kept)
    // ==========================

    /// <summary>
    /// Returns the current master volume in linear 0..1.
    /// Uses PlayerPrefs key stored by your Settings UI.
    /// </summary>
    public float GetCurrentMasterVolume01()
    {
        if (string.IsNullOrWhiteSpace(masterVolumePrefsKey))
            return 1f;

        if (PlayerPrefs.HasKey(masterVolumePrefsKey))
            return Mathf.Clamp01(PlayerPrefs.GetFloat(masterVolumePrefsKey, 1f));

        // Key doesn't exist yet
        float fallback = Mathf.Clamp01(defaultMasterVolumeIfMissing);

        if (autoCreateMasterPrefsKeyIfMissing)
        {
            PlayerPrefs.SetFloat(masterVolumePrefsKey, fallback);
            PlayerPrefs.Save();
        }

        return fallback;
    }

    /// <summary>
    /// Returns a final typing volume (0..1) that includes master volume if enabled.
    /// </summary>
    public float ApplyMasterVolumeIfEnabled(float baseVolume01)
    {
        float v = Mathf.Clamp01(baseVolume01);

        if (multiplyTypingByMasterVolume)
            v *= GetCurrentMasterVolume01();

        if (multiplyTypingByAudioListenerVolume)
            v *= Mathf.Clamp01(AudioListener.volume);

        return Mathf.Clamp01(v);
    }

    /// <summary>
    /// Gets the clip + effective volume for a given line index.
    /// </summary>
    public bool TryGetTypingSfxForLine(int lineIndex, out AudioClip clip, out float effectiveVolume01)
    {
        clip = null;
        effectiveVolume01 = 0f;

        if (lines == null || lineIndex < 0 || lineIndex >= lines.Length)
            return false;

        var line = lines[lineIndex];
        if (line == null)
            return false;

        // Choose clip
        clip = line.typingSfxOverride != null ? line.typingSfxOverride : defaultTypingSfx;
        if (clip == null)
            return false;

        // Choose base volume
        float baseVol = (line.typingSfxOverride != null) ? line.typingSfxVolume : defaultTypingSfxVolume;

        // Apply master
        effectiveVolume01 = ApplyMasterVolumeIfEnabled(baseVol);
        return true;
    }

    /// <summary>
    /// Convenience: same as TryGetTypingSfxForLine but returns defaults if invalid.
    /// Useful if you don't want to check bool every time.
    /// </summary>
    public void GetTypingSfxForLineSafe(int lineIndex, out AudioClip clip, out float effectiveVolume01)
    {
        if (!TryGetTypingSfxForLine(lineIndex, out clip, out effectiveVolume01))
        {
            clip = null;
            effectiveVolume01 = 0f;
        }
    }

    // ==========================
    // NEW FUNCTIONS (added)
    // ==========================

    /// <summary>
    /// True if this dialogue wants a tutorial before it starts.
    /// </summary>
    public bool HasTutorialBeforeDialogue()
    {
        return tutorialBeforeDialogue != null;
    }

    /// <summary>
    /// True if this dialogue wants a tutorial after it ends.
    /// </summary>
    public bool HasTutorialAfterDialogue()
    {
        return tutorialAfterDialogue != null;
    }

    /// <summary>
    /// Tries to get a tutorial that should trigger at a specific dialogue line index.
    /// </summary>
    public bool TryGetTutorialForLine(int lineIndex, out TutorialSequenceSO tutorial, out bool showBeforeLine)
    {
        tutorial = null;
        showBeforeLine = true;

        if (tutorialLinksPerLine == null || tutorialLinksPerLine.Length == 0)
            return false;

        for (int i = 0; i < tutorialLinksPerLine.Length; i++)
        {
            var link = tutorialLinksPerLine[i];
            if (link == null) continue;

            if (link.lineIndex == lineIndex && link.tutorial != null)
            {
                tutorial = link.tutorial;
                showBeforeLine = link.showBeforeLine;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Safe line count accessor.
    /// </summary>
    public int GetLineCount()
    {
        return lines != null ? lines.Length : 0;
    }
}

public enum DialogueEmotion
{
    None,
    Neutral,
    Happy,
    Angry,
    Sad,
    Surprised
}

[System.Serializable]
public class SimpleDialogueLine
{
    [Tooltip("Name of the speaker shown in the name box.")]
    public string speakerName;

    [TextArea(2, 5)]
    public string text;

    [Header("Portrait (optional)")]
    [Tooltip("Sprite to show for this line. Leave empty if you want no portrait.")]
    public Sprite portrait;

    [Tooltip("If TRUE and portrait is empty, the portrait will be cleared (no portrait).")]
    public bool clearPortraitIfNone = true;

    [Tooltip("Simple emotion tag used to drive portrait effects.")]
    public DialogueEmotion emotion = DialogueEmotion.None;

    [Header("Typing SFX (optional)")]
    [Tooltip("Overrides the sequence default typing SFX for this line.")]
    public AudioClip typingSfxOverride;

    [Range(0f, 1f)]
    public float typingSfxVolume = 0.6f;

    [Header("Choices (optional)")]
    public SimpleDialogueChoice[] choices;

    // ✅ NEW: per-line auto-advance override
    [Header("Auto Advance (NEW)")]
    [Tooltip("If >= 0: this line auto-advances after typing ends (seconds). -1 = use sequence default / manual.")]
    public float autoAdvanceSeconds = -1f;
}

[System.Serializable]
public class SimpleDialogueChoice
{
    [Tooltip("Text shown on the choice button.")]
    public string text;

    [Tooltip("Next line index when this choice is selected. -1 = end dialogue.")]
    public int nextLineIndex = -1;
}

[Serializable]
public class DialogueTutorialLink
{
    [Tooltip("Line index that should trigger a tutorial (0-based).")]
    public int lineIndex = 0;

    [Tooltip("Tutorial to show.")]
    public TutorialSequenceSO tutorial;

    [Tooltip("If true, show tutorial BEFORE that line appears. If false, show AFTER that line is shown.")]
    public bool showBeforeLine = true;
}
