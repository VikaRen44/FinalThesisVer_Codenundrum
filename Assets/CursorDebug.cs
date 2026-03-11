using UnityEngine;

public class CursorRestoreOnStart : MonoBehaviour
{
    [Header("Cursor Settings")]
    public bool showCursor = true;
    public CursorLockMode lockMode = CursorLockMode.None;

    void Start()
    {
        RestoreCursor();
    }

    public void RestoreCursor()
    {
        Cursor.visible = showCursor;
        Cursor.lockState = lockMode;
    }
}