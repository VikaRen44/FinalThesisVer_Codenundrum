using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class AttackQTEUI : MonoBehaviour
{
    [Header("Refs")]
    public RectTransform barRect;     // full bar
    public RectTransform cursorRect;  // moving line/knob
    public RectTransform targetRect;  // target zone (usually center)

    [Tooltip("Optional: drag your QTE_Slider (Slider component) here so fill follows cursor.")]
    public Slider qteSlider;

    [Header("Speed (recommended)")]
    [Tooltip("How many seconds it takes the cursor to travel across the full bar (left to right).")]
    [Range(0.15f, 6f)]
    public float secondsToCrossBar = 1.2f;

    [Tooltip("Use unscaled time so speed is stable even if Time.timeScale changes.")]
    public bool useUnscaledTime = true;

    [Header("Legacy Speed (optional)")]
    [Tooltip("If true, uses cursorSpeedPixelsPerSecond instead of secondsToCrossBar.")]
    public bool useLegacyPixelsPerSecond = false;

    [Tooltip("Legacy pixels/sec speed. Only used if useLegacyPixelsPerSecond = true.")]
    public float cursorSpeedPixelsPerSecond = 250f;

    [Header("Behavior")]
    [Tooltip("If true, cursor bounces. If false, it sweeps once L->R and times out.")]
    public bool bounce = false;

    [Header("Output")]
    [Tooltip("If using Play(Action<int>), accuracy 0..1 converts to 0..intOutputMax.")]
    public int intOutputMax = 100;

    [Header("Options")]
    public bool autoHideWhenDone = true;

    [Header("Debug")]
    public bool logOnStart = false;
    public bool logSpeedEveryFrame = false;

    private Action<float> onStopFloat;  // passes 0..1 accuracy
    private Action<int> onStopInt;      // passes 0..intOutputMax
    private bool running;
    private float dir = 1f;

    // Optional runtime override (OFF by default)
    private bool _overrideSpeed;
    private float _overrideSecondsToCrossBar;

    void OnEnable()
    {
        if (!ValidateRefs())
        {
            running = false;
            return;
        }

        ResetCursor();
        running = false;
        SyncSliderToCursor();
    }

    bool ValidateRefs()
    {
        if (barRect == null || cursorRect == null || targetRect == null)
        {
            Debug.LogError("[AttackQTEUI] Missing RectTransform refs. Assign barRect, cursorRect, targetRect.");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Optional: override speed for one run (only if you call this explicitly).
    /// </summary>
    public void SetSpeedOverrideSeconds(float seconds)
    {
        _overrideSpeed = true;
        _overrideSecondsToCrossBar = Mathf.Max(0.05f, seconds);
    }

    public void ClearSpeedOverride()
    {
        _overrideSpeed = false;
    }

    // MAIN API (accuracy float 0..1)
    public void StartQTE(Action<float> onStopCallback)
    {
        if (!ValidateRefs()) return;

        onStopFloat = onStopCallback;
        onStopInt = null;

        ResetCursor();
        SyncSliderToCursor();

        running = true;
        gameObject.SetActive(true);

        if (logOnStart)
            Debug.Log($"[AttackQTEUI] StartQTE | secondsToCrossBar={secondsToCrossBar} (override={_overrideSpeed}) legacy={useLegacyPixelsPerSecond}");
    }

    // COMPAT API (int result 0..intOutputMax)
    public void Play(Action<int> onStopCallback)
    {
        if (!ValidateRefs()) return;

        onStopInt = onStopCallback;
        onStopFloat = null;

        ResetCursor();
        SyncSliderToCursor();

        running = true;
        gameObject.SetActive(true);

        if (logOnStart)
            Debug.Log($"[AttackQTEUI] Play | secondsToCrossBar={secondsToCrossBar} (override={_overrideSpeed}) legacy={useLegacyPixelsPerSecond}");
    }

    void Update()
    {
        if (!running) return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        float halfWidth = barRect.rect.width * 0.5f;
        float fullWidth = barRect.rect.width;

        Vector2 pos = cursorRect.anchoredPosition;

        float speedPxPerSec = GetEffectivePixelsPerSecond(fullWidth);

        if (logSpeedEveryFrame)
            Debug.Log($"[AttackQTEUI] px/s={speedPxPerSec:F2} secondsToCross={GetEffectiveSecondsToCross():F2}");

        // move cursor
        pos.x += dir * speedPxPerSec * dt;

        if (bounce)
        {
            if (pos.x >= halfWidth)
            {
                pos.x = halfWidth;
                dir = -1f;
            }
            else if (pos.x <= -halfWidth)
            {
                pos.x = -halfWidth;
                dir = 1f;
            }
        }
        else
        {
            // sweep once then timeout MISS
            if (pos.x >= halfWidth)
            {
                pos.x = halfWidth;
                cursorRect.anchoredPosition = pos;
                SyncSliderToCursor();

                TimeoutMiss();
                return;
            }
        }

        cursorRect.anchoredPosition = pos;
        SyncSliderToCursor();

        // stop on confirm (Input System)
        if (ConfirmPressedThisFrame())
        {
            StopQTE(manualStop: true);
        }
    }

    private bool ConfirmPressedThisFrame()
    {
        // Keyboard: Z / Space / Enter
        if (Keyboard.current != null)
        {
            if (Keyboard.current.zKey.wasPressedThisFrame) return true;
            if (Keyboard.current.spaceKey.wasPressedThisFrame) return true;
            if (Keyboard.current.enterKey.wasPressedThisFrame) return true;
            if (Keyboard.current.numpadEnterKey.wasPressedThisFrame) return true;
        }

        // Mouse: left click
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;

        // Gamepad: A / Cross (Submit)
        if (Gamepad.current != null)
        {
            if (Gamepad.current.buttonSouth.wasPressedThisFrame) return true;
            if (Gamepad.current.startButton.wasPressedThisFrame) return true; // optional
        }

        return false;
    }

    private float GetEffectiveSecondsToCross()
    {
        if (_overrideSpeed) return _overrideSecondsToCrossBar;
        return Mathf.Max(0.05f, secondsToCrossBar);
    }

    private float GetEffectivePixelsPerSecond(float fullWidth)
    {
        if (useLegacyPixelsPerSecond)
            return Mathf.Max(1f, cursorSpeedPixelsPerSecond);

        float seconds = GetEffectiveSecondsToCross();
        return Mathf.Max(1f, fullWidth / seconds);
    }

    void TimeoutMiss()
    {
        StopQTE(manualStop: false); // returns 0 accuracy
    }

    void StopQTE(bool manualStop)
    {
        if (!running) return;
        running = false;

        float accuracy = manualStop ? ComputeAccuracy01() : 0f;

        onStopFloat?.Invoke(accuracy);

        if (onStopInt != null)
        {
            int resultInt = Mathf.RoundToInt(accuracy * intOutputMax);
            onStopInt.Invoke(resultInt);
        }

        // one-shot override only (so it doesn't "stick")
        _overrideSpeed = false;

        if (autoHideWhenDone)
            gameObject.SetActive(false);
    }

    float ComputeAccuracy01()
    {
        float cursorX = cursorRect.anchoredPosition.x;
        float targetX = targetRect.anchoredPosition.x;

        float dist = Mathf.Abs(cursorX - targetX);
        float maxDist = barRect.rect.width * 0.5f;

        if (maxDist <= 0.0001f) return 0f;
        return Mathf.Clamp01(1f - (dist / maxDist));
    }

    void SyncSliderToCursor()
    {
        if (qteSlider == null) return;

        float halfWidth = barRect.rect.width * 0.5f;
        float cursorX = cursorRect.anchoredPosition.x;

        float t = Mathf.InverseLerp(-halfWidth, halfWidth, cursorX);
        qteSlider.normalizedValue = t;
    }

    void ResetCursor()
    {
        float halfWidth = barRect.rect.width * 0.5f;
        cursorRect.anchoredPosition = new Vector2(-halfWidth, 0f);
        dir = 1f;
    }
}
