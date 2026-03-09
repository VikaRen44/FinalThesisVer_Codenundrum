using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class LetterGenerator : MonoBehaviour
{
    [Header("Prefab + Target")]
    [Tooltip("Button prefab to spawn. Must have a Button component (recommended).")]
    public GameObject letterPrefab;

    [Tooltip("Where letters will be sent (AddLetter / Backspace, etc.).")]
    public NameInput nameInput;

    [Header("Characters")]
    [TextArea] public string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    [Header("Layout / Controller Navigation")]
    [Tooltip("If true, buttons use Automatic navigation (good for simple grids).")]
    public bool setNavigationAutomatic = true;

    [Tooltip("If true, force each spawned button to be interactable.")]
    public bool forceInteractable = true;

    [Header("Optional: Input Mode Router")]
    [Tooltip("Assign the InputModeUIRouter so typing can snap-to-keys and gamepad can select first key.")]
    public InputModeUIRouter routerToRefresh;

    [Tooltip("If true, each generated key will notify the router when the mouse hovers it (remember last hovered key).")]
    public bool addPointerEnterToRememberHover = true;

    [Header("Optional: Visuals")]
    [Tooltip("If true, ensures LetterTMPFX exists on the TMP label child.")]
    public bool addTmpFxIfMissing = true;

    [Header("Debug")]
    public bool logSpawn = false;

    private readonly List<GameObject> _spawned = new();

    void Start()
    {
        Generate();
    }

    /// <summary>
    /// Clears existing spawned keys and spawns fresh ones (safe to call again if you re-open UI).
    /// </summary>
    public void Generate()
    {
        if (letterPrefab == null)
        {
            Debug.LogError("[LetterGenerator] letterPrefab is not assigned.");
            return;
        }

        if (nameInput == null)
        {
            Debug.LogWarning("[LetterGenerator] nameInput is not assigned. Buttons will still animate but won't type letters.");
        }

        CleanupSpawned();

        if (string.IsNullOrEmpty(chars))
        {
            Debug.LogWarning("[LetterGenerator] chars is empty. Nothing to generate.");
            return;
        }

        foreach (char c in chars)
        {
            if (char.IsWhiteSpace(c)) continue;

            GameObject btnObj = Instantiate(letterPrefab, transform);
            _spawned.Add(btnObj);

            // --- Button ---
            Button b = btnObj.GetComponent<Button>();
            if (b == null)
            {
                Debug.LogError($"[LetterGenerator] letterPrefab '{letterPrefab.name}' has no Button component. Skipping '{c}'.");
                continue;
            }

            if (forceInteractable)
                b.interactable = true;

            if (setNavigationAutomatic)
            {
                var nav = b.navigation;
                nav.mode = Navigation.Mode.Automatic;
                b.navigation = nav;
            }

            // IMPORTANT: make sure it can actually be selected
            var selectable = b as Selectable;
            if (selectable != null) selectable.enabled = true;

            // --- Label (TMP) ---
            TMP_Text t = btnObj.GetComponentInChildren<TMP_Text>(true);
            if (t != null) t.text = c.ToString();

            // --- Click types the letter (mouse/controller click uses key char as-is) ---
            b.onClick.RemoveAllListeners();
            char localChar = c;
            b.onClick.AddListener(() =>
            {
                if (nameInput != null)
                    nameInput.AddLetter(localChar);
            });

            // --- KeyButton metadata ---
            KeyButton kb = btnObj.GetComponent<KeyButton>();
            if (kb == null) kb = btnObj.AddComponent<KeyButton>();
            kb.keyChar = c;
            kb.label = t;
            kb.button = b;

            // --- Optional TMP FX ---
            if (addTmpFxIfMissing && t != null && !t.TryGetComponent<LetterTMPFX>(out _))
                t.gameObject.AddComponent<LetterTMPFX>();

            // --- Optional: remember last hovered key ---
            if (addPointerEnterToRememberHover && routerToRefresh != null)
                EnsurePointerEnterTrigger(btnObj);

            if (logSpawn)
                Debug.Log($"[LetterGenerator] Spawned key '{c}' -> {btnObj.name}");
        }

        // ✅ After keys are spawned:
        if (routerToRefresh != null)
        {
            routerToRefresh.BuildKeyMap();

            // ✅ makes controller navigation work immediately (no mouse click needed)
            routerToRefresh.ForceSelectFirstKey();
        }
    }

    void EnsurePointerEnterTrigger(GameObject btnObj)
    {
        var trigger = btnObj.GetComponent<EventTrigger>();
        if (trigger == null) trigger = btnObj.AddComponent<EventTrigger>();

        if (trigger.triggers == null)
            trigger.triggers = new List<EventTrigger.Entry>();

        // Remove old PointerEnter to avoid duplicates
        for (int i = trigger.triggers.Count - 1; i >= 0; i--)
        {
            if (trigger.triggers[i] != null && trigger.triggers[i].eventID == EventTriggerType.PointerEnter)
                trigger.triggers.RemoveAt(i);
        }

        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        entry.callback.AddListener(_ =>
        {
            if (routerToRefresh != null)
                routerToRefresh.NotifyMouseHover(btnObj);
        });
        trigger.triggers.Add(entry);
    }

    void CleanupSpawned()
    {
        for (int i = _spawned.Count - 1; i >= 0; i--)
        {
            if (_spawned[i] != null)
                Destroy(_spawned[i]);
        }
        _spawned.Clear();
    }
}
