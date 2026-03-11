using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine.InputSystem;

[System.Serializable]
public class DialogueLine
{
    public string speaker;
    public string portraitName;
    public string text;
    public string backgroundName;

    public DialogueLine(string speaker, string portraitName, string text, string backgroundName = "")
    {
        this.speaker = speaker;
        this.portraitName = portraitName;
        this.text = text;
        this.backgroundName = backgroundName;
    }
}

public class DialogueManager : MonoBehaviour
{
    [Header("UI References")]
    public GameObject dialoguePanel;
    public TextMeshProUGUI characterNameText;
    public TextMeshProUGUI dialogueText;
    public Image portraitImage;
    public Button dialogueButton;
    public GameObject gameplayRoot;

    [Header("Continue Icon (Optional)")]
    [Tooltip("Optional continue / press E icon shown while dialogue is active.")]
    public GameObject continueIcon;

    [Tooltip("If true, the continue icon is shown while dialogue is active.")]
    public bool showContinueIconWhileDialogue = true;

    [Header("Background System")]
    public Image backgroundImage;
    public Sprite[] allBackgroundSprites;

    private Dictionary<string, Sprite> backgroundDictionary = new Dictionary<string, Sprite>();

    [Header("All Available Portrait Sprites")]
    public Sprite[] allPortraitSprites;

    [Header("Portrait Lookup Behavior")]
    public bool allowContainsFallback = true;
    public bool verboseLogs = true;

    [Header("Portrait Aliases")]
    public bool supportFlippedUnderscoreFormat = true;
    public bool supportAllCapsCharacterFormat = true;

    [Header("Portrait Typo Fixes")]
    public bool autoFixCommonTypos = true;

    [Header("Advance Input (New Input System)")]
    public bool useEToAdvance = true;
    public bool allowButtonClick = true;

    [Header("Controller Advance Input")]
    [Tooltip("If true, controller input can also advance dialogue.")]
    public bool useGamepadToAdvance = true;

    [Tooltip("If true, the South button advances dialogue (A on Xbox / Cross on PlayStation).")]
    public bool allowSouthButtonToAdvance = true;

    [Header("Advance SFX")]
    public AudioSource sfxSource;
    public AudioClip advanceSfx;

    [Range(0f, 1f)]
    public float advanceSfxVolume = 1f;

    public bool playSfxOnFirstLine = false;

    private readonly Dictionary<string, Sprite> portraitDictionary = new Dictionary<string, Sprite>(1024);
    private readonly Queue<DialogueLine> dialogueQueue = new Queue<DialogueLine>();
    private bool dialogueActive = false;

    private int _lastAdvanceFrame = -999;
    private bool _warnedMissingSfxOnce = false;

    // optional callback fired when dialogue fully ends
    private Action _onDialogueComplete;

    private static readonly Regex _parenSuffix = new Regex(@"\s*\(\d+\)\s*$", RegexOptions.Compiled);
    private static readonly Regex _cloneSuffix = new Regex(@"\s*\(clone\)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex _trailingDigits = new Regex(@"\d+$", RegexOptions.Compiled);

    private void Awake()
    {
        RefreshPortraitDictionary();
        EnsureSfxSourceReady();
        BuildBackgroundDictionary();
        SetContinueIconVisible(false);
    }

    private void OnEnable()
    {
        RefreshPortraitDictionary();
        EnsureSfxSourceReady();
        BuildBackgroundDictionary();

        if (!dialogueActive)
            SetContinueIconVisible(false);
    }

    private void OnDisable()
    {
        SetContinueIconVisible(false);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying) return;
        RefreshPortraitDictionary();
        EnsureSfxSourceReady();
        BuildBackgroundDictionary();
    }
#endif

    private void Update()
    {
        if (!dialogueActive) return;

        bool keyboardAdvance =
            useEToAdvance &&
            Keyboard.current != null &&
            Keyboard.current.eKey.wasPressedThisFrame;

        bool gamepadAdvance =
            useGamepadToAdvance &&
            allowSouthButtonToAdvance &&
            Gamepad.current != null &&
            Gamepad.current.buttonSouth.wasPressedThisFrame;

        if (keyboardAdvance || gamepadAdvance)
        {
            Advance();
        }
    }

    public bool IsDialogueActive() => dialogueActive;

    // OLD VERSION STILL SUPPORTED
    public void StartDialogue(DialogueLine[] dialogueLines)
    {
        StartDialogue(dialogueLines, null);
    }

    // NEW OVERLOAD WITH CALLBACK
    public void StartDialogue(DialogueLine[] dialogueLines, Action onComplete)
    {
        _onDialogueComplete = onComplete;

        if (gameplayRoot != null)
            gameplayRoot.SetActive(false);

        if (dialoguePanel != null)
            dialoguePanel.SetActive(true);

        dialogueQueue.Clear();

        if (dialogueLines != null)
        {
            foreach (DialogueLine line in dialogueLines)
                dialogueQueue.Enqueue(line);
        }

        dialogueActive = true;
        SetContinueIconVisible(showContinueIconWhileDialogue);

        if (dialogueButton != null)
        {
            dialogueButton.onClick.RemoveAllListeners();
            if (allowButtonClick)
                dialogueButton.onClick.AddListener(Advance);
        }

        ShowNextLine();

        if (playSfxOnFirstLine)
            PlayAdvanceSfx();
    }

    private void Advance()
    {
        if (_lastAdvanceFrame == Time.frameCount) return;
        _lastAdvanceFrame = Time.frameCount;

        PlayAdvanceSfx();
        ShowNextLine();
    }

    public void ShowNextLine()
    {
        if (dialogueQueue.Count == 0)
        {
            EndDialogue();
            return;
        }

        DialogueLine currentLine = dialogueQueue.Dequeue();

        if (characterNameText != null)
            characterNameText.text = currentLine.speaker;

        if (dialogueText != null)
            dialogueText.text = currentLine.text;

        UpdatePortrait(currentLine.portraitName);
        UpdateBackground(currentLine.backgroundName);

        SetContinueIconVisible(showContinueIconWhileDialogue && dialogueActive);
    }

    void UpdateBackground(string backgroundName)
    {
        if (backgroundImage == null) return;
        if (string.IsNullOrEmpty(backgroundName)) return;

        if (backgroundDictionary.TryGetValue(backgroundName, out Sprite bg))
        {
            backgroundImage.sprite = bg;
        }
        else if (verboseLogs)
        {
            Debug.LogWarning("Background not found: " + backgroundName);
        }
    }

    void BuildBackgroundDictionary()
    {
        backgroundDictionary.Clear();

        if (allBackgroundSprites == null) return;

        foreach (Sprite bg in allBackgroundSprites)
        {
            if (bg == null) continue;

            if (!backgroundDictionary.ContainsKey(bg.name))
                backgroundDictionary.Add(bg.name, bg);
        }
    }

    private void UpdatePortrait(string portraitName)
    {
        if (portraitImage == null) return;

        if (string.IsNullOrWhiteSpace(portraitName))
        {
            portraitImage.gameObject.SetActive(false);
            return;
        }

        if (TryGetPortrait(portraitName, out Sprite sprite))
        {
            portraitImage.sprite = sprite;
            portraitImage.gameObject.SetActive(true);

            if (verboseLogs)
                Debug.Log($"[DialogueManager] Portrait FOUND: request='{portraitName}' -> sprite='{sprite.name}'");
        }
        else
        {
            if (verboseLogs)
                Debug.LogWarning($"[DialogueManager] Portrait NOT found for request='{portraitName}'.");

            portraitImage.gameObject.SetActive(false);
        }
    }

    private void EnsureSfxSourceReady()
    {
        if (sfxSource == null && advanceSfx != null)
        {
            sfxSource = GetComponent<AudioSource>();
            if (sfxSource == null) sfxSource = gameObject.AddComponent<AudioSource>();
        }

        if (sfxSource != null)
        {
            sfxSource.enabled = true;
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;
        }
    }

    private void PlayAdvanceSfx()
    {
        if (advanceSfx == null)
        {
            if (verboseLogs && !_warnedMissingSfxOnce)
            {
                _warnedMissingSfxOnce = true;
                Debug.LogWarning("[DialogueManager] advanceSfx is NOT assigned.");
            }
            return;
        }

        EnsureSfxSourceReady();

        if (sfxSource == null)
            return;

        sfxSource.PlayOneShot(advanceSfx, advanceSfxVolume);
    }

    public bool IsLastLine()
    {
        return dialogueQueue.Count == 1;
    }

    private void EndDialogue()
    {
        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);

        dialogueActive = false;
        SetContinueIconVisible(false);

        if (gameplayRoot != null)
            gameplayRoot.SetActive(true);

        Action callback = _onDialogueComplete;
        _onDialogueComplete = null;

        callback?.Invoke();
    }

    private void SetContinueIconVisible(bool visible)
    {
        if (continueIcon == null) return;
        if (continueIcon.activeSelf == visible) return;

        continueIcon.SetActive(visible);
    }

    // =========================
    // Portrait Dictionary
    // =========================

    private void RefreshPortraitDictionary()
    {
        portraitDictionary.Clear();
        if (allPortraitSprites == null) return;

        foreach (Sprite s in allPortraitSprites)
        {
            if (s == null) continue;

            foreach (string key in BuildKeysForSpriteName(s.name))
            {
                if (string.IsNullOrEmpty(key)) continue;

                if (!portraitDictionary.ContainsKey(key))
                    portraitDictionary.Add(key, s);
            }
        }
    }

    private bool TryGetPortrait(string requestName, out Sprite sprite)
    {
        sprite = null;

        string fixedRequest = autoFixCommonTypos ? ApplyCommonTypoFixes(requestName) : requestName;

        if (portraitDictionary.TryGetValue(fixedRequest, out sprite))
            return true;

        string norm = NormalizeKey(fixedRequest);
        if (!string.IsNullOrEmpty(norm) && portraitDictionary.TryGetValue(norm, out sprite))
            return true;

        if (allowContainsFallback && !string.IsNullOrEmpty(norm))
        {
            foreach (var kvp in portraitDictionary)
            {
                if (kvp.Key.Contains(norm))
                {
                    sprite = kvp.Value;
                    return true;
                }
            }
        }

        return false;
    }

    private IEnumerable<string> BuildKeysForSpriteName(string spriteName)
    {
        string fixedName = AutoFixCommonTypoFixes(spriteName);

        yield return fixedName;

        string stripped = StripSuffixes(fixedName);
        yield return stripped;

        string norm = NormalizeKey(fixedName);
        if (!string.IsNullOrEmpty(norm))
            yield return norm;
    }

    private string ApplyCommonTypoFixes(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;

        s = Regex.Replace(s, "neautral", "neutral", RegexOptions.IgnoreCase);

        return s;
    }

    private string StripSuffixes(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        s = _cloneSuffix.Replace(s, "");
        s = _parenSuffix.Replace(s, "");
        return s.Trim();
    }

    private string NormalizeKey(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";

        if (autoFixCommonTypos)
            s = ApplyCommonTypoFixes(s);

        s = StripSuffixes(s);
        s = s.ToLowerInvariant();

        var sb = new StringBuilder(s.Length);

        foreach (char c in s)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
        }

        return sb.ToString();
    }

    private string AutoFixCommonTypoFixes(string s)
    {
        if (!autoFixCommonTypos) return s;
        return ApplyCommonTypoFixes(s);
    }
}