using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class MinimapRoom : MonoBehaviour
{
    [Header("Floor / Links")]
    public int floorIndex = 1;
    public RectTransform floorContainer;
    public Image roomImage;

    [Header("World Mapping")]
    [Tooltip("Trigger/collider that covers this room in world space.")]
    public Collider worldCollider;

    [Header("UI")]
    [Tooltip("Shown in the Objectives text.")]
    public string displayName = "Unnamed Room";

    [Header("Colors")]
    public Color normalColor = new Color(1f, 1f, 1f, 0.6f);
    public Color activeColor = new Color(1f, 1f, 1f, 1f);
    public Color objectiveColor = new Color(1f, 0.85f, 0.2f, 1f);

    // cached
    private Bounds _worldBounds;
    private RectTransform _rect;
    private Vector2 _uiMin, _uiMax;

    // states
    private bool _isActive;
    private bool _isObjective;
    private Coroutine _pulseCo;

    void Awake()
    {
        _rect = GetComponent<RectTransform>();
        if (!roomImage) roomImage = GetComponent<Image>();

        if (worldCollider != null)
            _worldBounds = worldCollider.bounds;

        // cache UI rect relative to the floor container
        Vector3[] corners = new Vector3[4];
        _rect.GetWorldCorners(corners);
        var parent = floorContainer != null ? floorContainer : _rect.parent as RectTransform;
        for (int i = 0; i < 4; i++)
            corners[i] = parent.InverseTransformPoint(corners[i]);
        _uiMin = corners[0]; // bottom-left
        _uiMax = corners[2]; // top-right
    }

    public void SetActiveVisual(bool active)
    {
        _isActive = active;
        UpdateColor();
    }

    public void SetObjective(bool enabled, bool pulse = true, float pulseSpeed = 3f)
    {
        _isObjective = enabled;
        UpdateColor();

        if (_pulseCo != null) { StopCoroutine(_pulseCo); _pulseCo = null; }
        if (_isObjective && pulse && roomImage != null)
            _pulseCo = StartCoroutine(PulseObjective(pulseSpeed));
    }

    private System.Collections.IEnumerator PulseObjective(float speed)
    {
        Color dim = new Color(objectiveColor.r, objectiveColor.g, objectiveColor.b,
                              Mathf.Clamp01(objectiveColor.a * 0.6f));
        float t = 0f;
        while (_isObjective)
        {
            t += Time.unscaledDeltaTime * speed;
            float s = 0.5f + 0.5f * Mathf.Sin(t);
            roomImage.color = Color.Lerp(dim, objectiveColor, s);
            yield return null;
        }
        UpdateColor();
    }

    private void UpdateColor()
    {
        if (!roomImage) return;

        // Priority: Objective > Active > Normal
        if (_isObjective) roomImage.color = objectiveColor;
        else if (_isActive) roomImage.color = activeColor;
        else roomImage.color = normalColor;
    }

    public Vector2 WorldToUI(Vector3 worldPos)
    {
        float nx = Mathf.InverseLerp(_worldBounds.min.x, _worldBounds.max.x, worldPos.x);
        float nz = Mathf.InverseLerp(_worldBounds.min.z, _worldBounds.max.z, worldPos.z);
        float ux = Mathf.Lerp(_uiMin.x, _uiMax.x, nx);
        float uy = Mathf.Lerp(_uiMin.y, _uiMax.y, nz);
        return new Vector2(ux, uy);
    }

    public bool ContainsWorldPoint(Vector3 worldPos)
    {
        return worldCollider != null && _worldBounds.Contains(worldPos);
    }
}
