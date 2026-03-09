using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class PropItem : MonoBehaviour
{
    public int value;
    public bool isPivot = false;

    public TextMeshProUGUI valueText;
    public Image propImage;

    private QuickSortManager manager;

    void Awake()
    {
        manager = FindObjectOfType<QuickSortManager>();
        GetComponent<Button>().onClick.AddListener(OnClick);
    }

    public void Initialize(int number, Sprite sprite)
    {
        value = number;

        if (valueText != null)
            valueText.text = number.ToString();

        if (propImage != null && sprite != null)
            propImage.sprite = sprite;
    }

    void OnClick()
    {
        manager.OnPropClicked(this);
    }
}