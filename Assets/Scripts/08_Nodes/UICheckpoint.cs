using UnityEngine;
using UnityEngine.UI;

public class UICheckpoint : MonoBehaviour
{
    [Tooltip("0 for first checkpoint, 1 for second, etc.")]
    public int orderIndex;

    [Tooltip("Radius in pixels around this object that counts as a hit.")]
    public float radius = 40f;

    [HideInInspector] public bool reached;

    private RectTransform rect;
    private Image img;
    private Color defaultColor;

    // ✅ NEW: per-instance assigned sprites
    private Sprite _inactiveSprite;
    private Sprite _passedSprite;

    public RectTransform Rect => rect;

    private void Awake()
    {
        rect = GetComponent<RectTransform>();
        img = GetComponent<Image>();
        if (img != null)
            defaultColor = img.color;
    }

    // ✅ NEW: called by UIPathManager after generation
    public void AssignSpritesByOrder(Sprite[] inactiveSet, Sprite[] passedSet, bool oneBased)
    {
        if (img == null) return;

        // Convert orderIndex -> sprite array index
        // If oneBased, orderIndex 0 => "1" => index 0 (same), but we keep the flag
        // because some people store sprites starting at index 1; we support both styles safely.
        int idx = oneBased ? orderIndex : orderIndex;

        // If someone uses non-oneBased meaning "sprite[0] is 0", then orderIndex already matches.
        // If you ever decide to store sprite[0] as "0", just set oneBasedSprites=false in manager.

        // Clamp/fallback so it never errors
        if (inactiveSet != null && inactiveSet.Length > 0)
            _inactiveSprite = inactiveSet[Mathf.Clamp(idx, 0, inactiveSet.Length - 1)];

        if (passedSet != null && passedSet.Length > 0)
            _passedSprite = passedSet[Mathf.Clamp(idx, 0, passedSet.Length - 1)];

        // If we haven't reached it yet, show inactive immediately
        if (!reached) ResetVisual();
        else OnCorrectHit();
    }

    public void OnCorrectHit()
    {
        // keep your existing feedback
        if (img != null)
        {
            img.color = Color.green; // simple feedback

            // ✅ NEW: swap to passed sprite if assigned
            if (_passedSprite != null)
                img.sprite = _passedSprite;
        }
    }

    public void ResetVisual()
    {
        if (img != null)
        {
            img.color = defaultColor;

            // ✅ NEW: swap back to inactive sprite if assigned
            if (_inactiveSprite != null)
                img.sprite = _inactiveSprite;
        }
    }
}
