using UnityEngine;

public class FPSCounter : MonoBehaviour
{
    float deltaTime = 0.0f;
    public int fontSize = 50; // ← Adjust size here

    void Update()
    {
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
    }

    void OnGUI()
    {
        float fps = 1.0f / deltaTime;

        GUIStyle style = new GUIStyle();
        style.fontSize = fontSize;
        style.normal.textColor = Color.white;

        GUI.Label(new Rect(10, 10, 300, 50), "FPS: " + Mathf.Ceil(fps), style);
    }
}