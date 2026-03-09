using UnityEngine;

public class HoverSpin : MonoBehaviour
{
    [Header("Hover")]
    public bool hover = true;
    public float hoverAmplitude = 0.08f;   // height in meters
    public float hoverFrequency = 1.5f;    // cycles per second
    public bool useUnscaledTime = false;   // keep animating even if Time.timeScale = 0

    [Header("Spin")]
    public bool spin = true;
    public Vector3 spinAxis = Vector3.up;  // coin-like: up axis
    public float spinSpeed = 120f;         // degrees per second

    [Header("Extra (optional)")]
    public bool bobRotate = false;         // tiny tilt while hovering
    public float tiltAngle = 6f;
    public float tiltFrequency = 1.2f;

    Vector3 _startLocalPos;
    Quaternion _startLocalRot;

    void Awake()
    {
        _startLocalPos = transform.localPosition;
        _startLocalRot = transform.localRotation;
    }

    void OnEnable()
    {
        // Re-cache in case you reposition it at runtime
        _startLocalPos = transform.localPosition;
        _startLocalRot = transform.localRotation;
    }

    void Update()
    {
        float t = useUnscaledTime ? Time.unscaledTime : Time.time;

        // Hover (sine wave)
        if (hover)
        {
            float y = Mathf.Sin(t * hoverFrequency * Mathf.PI * 2f) * hoverAmplitude;
            transform.localPosition = _startLocalPos + new Vector3(0f, y, 0f);
        }

        // Spin
        if (spin)
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            transform.Rotate(spinAxis.normalized, spinSpeed * dt, Space.Self);
        }

        // Optional gentle tilt
        if (bobRotate)
        {
            float tilt = Mathf.Sin(t * tiltFrequency * Mathf.PI * 2f) * tiltAngle;
            transform.localRotation = _startLocalRot * Quaternion.Euler(tilt, 0f, 0f);
        }
    }
}
