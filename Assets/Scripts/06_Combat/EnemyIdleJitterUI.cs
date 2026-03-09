using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class EnemyIdleJitterUI : MonoBehaviour
{
    [Header("Target (UI RectTransform)")]
    public RectTransform target; // if null, uses this RectTransform

    [Header("Move Range (pixels)")]
    public Vector2 xRange = new Vector2(-18f, 18f);
    public Vector2 yRange = new Vector2(-6f, 6f);

    [Header("Timing (seconds)")]
    public Vector2 moveDurationRange = new Vector2(0.15f, 0.45f);
    public Vector2 pauseRange = new Vector2(0.08f, 0.35f);

    [Header("Smoothing")]
    [Tooltip("Bigger = snappier. Smaller = floatier.")]
    public float easePower = 2.5f;

    [Header("Optional Jump/Hop")]
    public bool enableHops = true;
    [Range(0f, 1f)] public float hopChance = 0.35f;
    public Vector2 hopHeightRange = new Vector2(10f, 25f);

    [Header("Respect Layout")]
    [Tooltip("If true, uses anchoredPosition (recommended for UI).")]
    public bool useAnchoredPosition = true;

    private Vector2 _startPos;
    private Coroutine _routine;

    private void Awake()
    {
        if (target == null)
            target = GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        if (target == null) return;

        _startPos = useAnchoredPosition ? target.anchoredPosition : (Vector2)target.localPosition;

        _routine = StartCoroutine(JitterRoutine());
    }

    private void OnDisable()
    {
        if (_routine != null)
            StopCoroutine(_routine);

        // Return to start position so it doesn't "save" a weird offset
        if (target != null)
        {
            if (useAnchoredPosition) target.anchoredPosition = _startPos;
            else target.localPosition = _startPos;
        }
    }

    private IEnumerator JitterRoutine()
    {
        while (true)
        {
            // pick random offset
            float x = Random.Range(xRange.x, xRange.y);
            float y = Random.Range(yRange.x, yRange.y);

            // optional hop
            float hop = 0f;
            if (enableHops && Random.value < hopChance)
                hop = Random.Range(hopHeightRange.x, hopHeightRange.y);

            Vector2 from = GetPos();
            Vector2 to = _startPos + new Vector2(x, y);

            float dur = Random.Range(moveDurationRange.x, moveDurationRange.y);
            float t = 0f;

            while (t < dur)
            {
                t += Time.unscaledDeltaTime; // UI usually should ignore timescale (pause-safe)
                float u = Mathf.Clamp01(t / dur);

                // smooth step-ish curve
                float eased = Ease(u, easePower);

                // base movement
                Vector2 p = Vector2.LerpUnclamped(from, to, eased);

                // hop arc (parabola)
                if (hop > 0f)
                {
                    float arc = 4f * u * (1f - u); // peaks at u=0.5
                    p.y += hop * arc;
                }

                SetPos(p);
                yield return null;
            }

            SetPos(to);

            float pause = Random.Range(pauseRange.x, pauseRange.y);
            yield return new WaitForSecondsRealtime(pause);
        }
    }

    private float Ease(float t, float power)
    {
        // ease-in-out using power curve
        t = Mathf.Clamp01(t);
        float a = Mathf.Pow(t, power);
        float b = Mathf.Pow(1f - t, power);
        return a / (a + b);
    }

    private Vector2 GetPos()
    {
        return useAnchoredPosition ? target.anchoredPosition : (Vector2)target.localPosition;
    }

    private void SetPos(Vector2 p)
    {
        if (useAnchoredPosition) target.anchoredPosition = p;
        else target.localPosition = p;
    }
}