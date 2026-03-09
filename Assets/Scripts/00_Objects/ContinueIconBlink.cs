using UnityEngine;
using TMPro;

[RequireComponent(typeof(TMP_Text))]
public class ContinueIconBlinkTMP : MonoBehaviour
{
    public float speed = 4f;

    private TMP_Text text;
    private Color baseColor;

    void Awake()
    {
        text = GetComponent<TMP_Text>();
        baseColor = text.color;
    }

    void Update()
    {
        float alpha = (Mathf.Sin(Time.time * speed) + 1f) * 0.5f;
        text.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
    }
}

