using UnityEngine;
using System.Collections;

public class FrameRateVSync : MonoBehaviour
{
    IEnumerator Start()
    {
        // wait one frame so Unity finishes applying Quality settings
        yield return null;

        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = 4;

        Debug.Log("VSync Applied: " + QualitySettings.vSyncCount);
    }
}