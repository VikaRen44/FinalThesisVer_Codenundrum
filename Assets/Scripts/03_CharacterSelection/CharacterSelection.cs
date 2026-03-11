using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class CharacterSelection : MonoBehaviour
{
    public GameObject[] characters;
    public int selectedCharacter = 0;

    [Header("Scene")]
    public string gameHubSceneName = "04_Gamehub";

    [Header("Intro Dialogue (SimpleDialogueManager)")]
    public SimpleDialogueSequenceSO introSequence;
    public bool playIntroOnlyOnce = true;
    public bool debugResetIntroPlayedOnStart = false;

    [Header("Intro UI Root (optional)")]
    public GameObject cutsceneIntroRoot;

    [Header("Optional: Disable buttons while starting")]
    public Button chooseButton;
    public Button nextButton;
    public Button prevButton;
    public Button backButton;

    [Header("Intro Start Fade (NO WINDOW)")]
    public bool whiteFadeBeforeIntro = true;
    public float introFadeOut = 0.35f;
    public float introFadeIn = 0.45f;

    [Header("New Game Runtime Reset (NEW)")]
    [Tooltip("If true, clears temporary in-memory world/hub return poses before starting a fresh New Game.")]
    public bool clearRuntimePoseMemoryOnNewGame = true;

    [Tooltip("If true, clears pending cached load selection so New Game won't inherit any previous load-selection cache.")]
    public bool clearPendingLoadSelectionOnNewGame = true;

    private const string PREF_PLAYER_NAME = "playerName";
    private const string PREF_INTRO_PLAYED = "introPlayed";

    private bool _starting;
    private bool _useWhiteTransitionForHubLoad;

    private void Start()
    {
        if (debugResetIntroPlayedOnStart)
        {
            PlayerPrefs.DeleteKey(PREF_INTRO_PLAYED);
            PlayerPrefs.Save();
            Debug.Log("[CharacterSelection] DEBUG: introPlayed flag cleared.");
        }

        if (characters != null && characters.Length > 0)
        {
            selectedCharacter = Mathf.Clamp(selectedCharacter, 0, characters.Length - 1);
            for (int i = 0; i < characters.Length; i++)
                characters[i].SetActive(i == selectedCharacter);
        }
        else
        {
            Debug.LogWarning("[CharacterSelection] characters array is empty.");
        }
    }

    public void NextCharacter()
    {
        if (_starting) return;
        if (characters == null || characters.Length == 0) return;

        characters[selectedCharacter].SetActive(false);
        selectedCharacter = (selectedCharacter + 1) % characters.Length;
        characters[selectedCharacter].SetActive(true);

        LoadCharacter.SaveSelectedCharacterIndex(selectedCharacter);
    }

    public void PreviousCharacter()
    {
        if (_starting) return;
        if (characters == null || characters.Length == 0) return;

        characters[selectedCharacter].SetActive(false);
        selectedCharacter = (selectedCharacter - 1 + characters.Length) % characters.Length;
        characters[selectedCharacter].SetActive(true);

        LoadCharacter.SaveSelectedCharacterIndex(selectedCharacter);
    }

    public void StartGame()
    {
        if (_starting) return;
        _starting = true;

        Debug.Log("[CharacterSelection] Choose pressed.");
        SetButtonsInteractable(false);

        // ✅ Keep selected character memory
        LoadCharacter.SaveSelectedCharacterIndex(selectedCharacter);

        // ✅ NEW: Fresh New Game should not inherit old in-memory world/hub poses
        PrepareFreshNewGameRuntimeState();

        string n = PlayerPrefs.GetString(PREF_PLAYER_NAME, "Charlie");
        if (string.IsNullOrWhiteSpace(n)) n = "Charlie";
        PlayerPrefs.SetString(PREF_PLAYER_NAME, n);

        if (SaveGameManager.Instance != null)
            SaveGameManager.Instance.SetPlayerName(n);

        PlayerPrefs.Save();

        bool introAlreadyPlayed = PlayerPrefs.GetInt(PREF_INTRO_PLAYED, 0) == 1;

        if (introSequence == null)
        {
            Debug.LogWarning("[CharacterSelection] introSequence is NULL -> loading hub.");
            _useWhiteTransitionForHubLoad = false;
            LoadHubNow();
            return;
        }

        if (playIntroOnlyOnce && introAlreadyPlayed)
        {
            Debug.Log("[CharacterSelection] Intro already played -> loading hub.");
            _useWhiteTransitionForHubLoad = false;
            LoadHubNow();
            return;
        }

        if (playIntroOnlyOnce)
        {
            PlayerPrefs.SetInt(PREF_INTRO_PLAYED, 1);
            PlayerPrefs.Save();
        }

        _useWhiteTransitionForHubLoad = true;

        StartCoroutine(IntroFlow_NoWindow());
    }

    // ✅ NEW: clears only TEMP runtime state that can wrongly affect a fresh New Game spawn
    private void PrepareFreshNewGameRuntimeState()
    {
        if (clearRuntimePoseMemoryOnNewGame)
        {
            LoadCharacter.ClearAllRuntimePoseMemory();

            Debug.Log("[CharacterSelection] Cleared runtime pose memory for fresh New Game.");
        }

        if (clearPendingLoadSelectionOnNewGame)
        {
            SaveSystem.ClearPendingLoadSelection();
            Debug.Log("[CharacterSelection] Cleared pending load selection cache for fresh New Game.");
        }
    }

    // ✅ This is the fixed flow: cover -> start intro while covered -> reveal
    private IEnumerator IntroFlow_NoWindow()
    {
        var st = SceneTransition.Instance;
        var dm = SimpleDialogueManager.Instance;

        if (dm == null)
        {
            Debug.LogError("[CharacterSelection] SimpleDialogueManager.Instance not found -> loading hub.");
            _useWhiteTransitionForHubLoad = false;
            LoadHubNow();
            yield break;
        }

        // 1) Fade OUT to white and KEEP the cover (no fade-in yet!)
        if (whiteFadeBeforeIntro && st != null)
            yield return st.FadeOutToColorAndKeep(Color.white, introFadeOut, freezeDuring: false);

        // 2) While still covered: enable intro UI and start the dialogue
        if (cutsceneIntroRoot != null)
            cutsceneIntroRoot.SetActive(true);

        yield return null; // let UI enable

        bool started = dm.TryStartDialogue(introSequence);
        if (!started)
        {
            Debug.LogError("[CharacterSelection] Failed to start intro dialogue -> loading hub.");
            if (cutsceneIntroRoot != null) cutsceneIntroRoot.SetActive(false);

            // reveal back if we covered
            if (whiteFadeBeforeIntro && st != null)
                yield return st.FadeInFromKeptColor(introFadeIn);

            _useWhiteTransitionForHubLoad = false;
            LoadHubNow();
            yield break;
        }

        // 3) Now reveal the intro (fade IN from white)
        if (whiteFadeBeforeIntro && st != null)
            yield return st.FadeInFromKeptColor(introFadeIn);

        // 4) Wait for dialogue to end
        bool ended = false;
        void OnEnded() => ended = true;

        dm.OnDialogueEnded -= OnEnded;
        dm.OnDialogueEnded += OnEnded;

        while (!ended)
            yield return null;

        dm.OnDialogueEnded -= OnEnded;

        if (cutsceneIntroRoot != null)
            cutsceneIntroRoot.SetActive(false);

        LoadHubNow();
    }

    private void LoadHubNow()
    {
        if (SceneTransition.Instance != null)
        {
            if (_useWhiteTransitionForHubLoad)
                SceneTransition.Instance.LoadSceneWhite(gameHubSceneName);
            else
                SceneTransition.Instance.LoadScene(gameHubSceneName);
        }
        else
        {
            SceneManager.LoadScene(gameHubSceneName, LoadSceneMode.Single);
        }

        SetButtonsInteractable(true);
        _starting = false;
        _useWhiteTransitionForHubLoad = false;
    }

    private void SetButtonsInteractable(bool on)
    {
        if (chooseButton) chooseButton.interactable = on;
        if (nextButton) nextButton.interactable = on;
        if (prevButton) prevButton.interactable = on;
        if (backButton) backButton.interactable = on;
    }
}