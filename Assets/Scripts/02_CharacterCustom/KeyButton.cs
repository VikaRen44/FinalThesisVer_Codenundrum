using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class KeyButton : MonoBehaviour
{
    public char keyChar;
    public TMP_Text label;
    public Button button;

    void Awake()
    {
        if (!button) button = GetComponent<Button>();
        if (!label) label = GetComponentInChildren<TMP_Text>(true);
    }

    public void Press()
    {
        if (button != null)
            button.onClick.Invoke();
    }
}
