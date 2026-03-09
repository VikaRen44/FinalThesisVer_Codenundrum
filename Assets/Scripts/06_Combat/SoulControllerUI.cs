using UnityEngine;
using UnityEngine.InputSystem;

public class SoulControllerUI : MonoBehaviour
{
    [Header("Movement")]
    public RectTransform movementArea;   // BoxMask
    public float moveSpeed = 220f;       // pixels per second
    public bool controlEnabled = false;

    private RectTransform soulRect;

    private void Awake()
    {
        soulRect = GetComponent<RectTransform>();
    }

    private void Update()
    {
        if (!controlEnabled) return;
        if (movementArea == null || soulRect == null) return;

        Vector2 input = ReadMoveInput();

        if (input.sqrMagnitude > 1f)
            input.Normalize();

        Vector2 delta = input * moveSpeed * Time.deltaTime;
        Vector2 newPos = soulRect.anchoredPosition + delta;

        // Clamp inside movement area
        Rect area = movementArea.rect;
        Vector2 halfSize = soulRect.rect.size * 0.5f;

        float x = Mathf.Clamp(newPos.x, area.xMin + halfSize.x, area.xMax - halfSize.x);
        float y = Mathf.Clamp(newPos.y, area.yMin + halfSize.y, area.yMax - halfSize.y);

        soulRect.anchoredPosition = new Vector2(x, y);
    }

    private Vector2 ReadMoveInput()
    {
        Vector2 v = Vector2.zero;

        // Keyboard WASD / Arrows
        if (Keyboard.current != null)
        {
            float h = 0f;
            float y = 0f;

            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) h -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) h += 1f;

            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) y -= 1f;
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) y += 1f;

            v = new Vector2(h, y);
        }

        // Gamepad Left Stick (if present)
        if (Gamepad.current != null)
        {
            Vector2 stick = Gamepad.current.leftStick.ReadValue();
            if (stick.sqrMagnitude > 0.0001f)
                v = stick;
        }

        return v;
    }

    public void SetControl(bool enabled)
    {
        controlEnabled = enabled;
    }
}
