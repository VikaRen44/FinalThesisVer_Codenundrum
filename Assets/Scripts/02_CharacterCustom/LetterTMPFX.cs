using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(TMP_Text))]
public class LetterTMPFX : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler
{
    [Header("Idle Jitter (vertex wobble)")]
    public bool idleJitter = true;
    public float jitterAmplitude = 1.5f;   // pixels
    public float jitterSpeed = 6f;

    [Header("Hover / Select")]
    public Color normalColor = Color.white;
    public Color hoverColor = new Color(1f, 1f, 0.4f);
    public Color pressedColor = new Color(1f, 0.8f, 0.2f);

    public float hoverScale = 1.12f;
    public float pressScale = 1.18f;
    public float scaleLerpSpeed = 12f;

    private TMP_Text tmp;
    private Mesh mesh;
    private Vector3[] baseVertices;

    private Vector3 baseScale;
    private bool isHovering;
    private bool isPressed;

    // ✅ unique per-letter randomness
    private float seed;
    private float speedMul;
    private float ampMul;

    void Awake()
    {
        tmp = GetComponent<TMP_Text>();
        baseScale = transform.localScale;

        tmp.raycastTarget = true;
        tmp.color = normalColor;

        // each letter gets its own random animation personality
        seed = Random.Range(0f, 9999f);
        speedMul = Random.Range(0.75f, 1.35f);
        ampMul = Random.Range(0.7f, 1.3f);
    }

    void Start()
    {
        CacheBaseVertices();
    }

    void CacheBaseVertices()
    {
        tmp.ForceMeshUpdate();
        mesh = tmp.mesh;
        baseVertices = mesh.vertices;
    }

    void Update()
    {
        // ----- Idle jitter via vertex wobble -----
        if (idleJitter && baseVertices != null && baseVertices.Length > 0)
        {
            tmp.ForceMeshUpdate();
            mesh = tmp.mesh;
            var verts = mesh.vertices;

            float t = (Time.unscaledTime + seed) * jitterSpeed * speedMul;

            for (int i = 0; i < verts.Length; i++)
            {
                // uncoordinated wobble per vertex + per letter seed
                float xOff = Mathf.Sin(t + i * 0.17f + seed * 0.01f) * jitterAmplitude * ampMul;
                float yOff = Mathf.Cos(t * 1.1f + i * 0.13f + seed * 0.02f) * jitterAmplitude * ampMul;

                verts[i] = baseVertices[i] + new Vector3(xOff, yOff, 0);
            }

            mesh.vertices = verts;
            tmp.canvasRenderer.SetMesh(mesh);
        }

        // ----- Smooth hover/press scale -----
        Vector3 desiredScale = baseScale;

        if (isPressed) desiredScale = baseScale * pressScale;
        else if (isHovering) desiredScale = baseScale * hoverScale;

        transform.localScale = Vector3.Lerp(
            transform.localScale,
            desiredScale,
            Time.unscaledDeltaTime * scaleLerpSpeed
        );
    }

    // Pointer events
    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovering = true;
        tmp.color = hoverColor;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;
        if (!isPressed) tmp.color = normalColor;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        isPressed = true;
        tmp.color = pressedColor;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPressed = false;
        tmp.color = isHovering ? hoverColor : normalColor;
    }

    void OnRectTransformDimensionsChange()
    {
        CacheBaseVertices();
    }

    void OnDisable()
    {
        transform.localScale = baseScale;
        if (tmp != null) tmp.color = normalColor;
    }
}
