using UnityEngine;
using UnityEngine.InputSystem;

public class MenuCursorSwitcher : MonoBehaviour
{
    void Update()
    {
        // Mouse activity → show cursor
        if (Mouse.current != null)
        {
            if (Mouse.current.delta.ReadValue().sqrMagnitude > 0.01f ||
                Mouse.current.leftButton.wasPressedThisFrame ||
                Mouse.current.rightButton.wasPressedThisFrame ||
                Mouse.current.scroll.ReadValue().sqrMagnitude > 0.01f)
            {
                ShowMouse();
                return;
            }
        }

        // Gamepad activity → hide cursor
        if (Gamepad.current != null)
        {
            if (Gamepad.current.dpad.ReadValue().sqrMagnitude > 0.01f ||
                Gamepad.current.leftStick.ReadValue().sqrMagnitude > 0.2f ||
                Gamepad.current.buttonSouth.wasPressedThisFrame ||
                Gamepad.current.buttonEast.wasPressedThisFrame)
            {
                HideMouse();
            }
        }
    }

    void ShowMouse()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    void HideMouse()
    {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.None; // DO NOT lock in UI
    }
}
