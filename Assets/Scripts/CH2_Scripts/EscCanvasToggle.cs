using UnityEngine;
using UnityEngine.InputSystem;

public class TabCanvasToggle : MonoBehaviour
{
    public GameObject targetUI;

    public bool hideOnStart = true;

    void Start()
    {
        if (targetUI != null && hideOnStart)
            targetUI.SetActive(false);
    }

    void Update()
    {
        if (targetUI == null)
            return;

        if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
        {
            bool newState = !targetUI.activeSelf;

            targetUI.SetActive(newState);

            if (newState)
            {
                foreach (Transform child in targetUI.transform)
                {
                    child.gameObject.SetActive(true);
                }
            }
        }
    }
}