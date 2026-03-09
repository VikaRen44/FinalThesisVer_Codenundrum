using UnityEngine;

public class BillboardUI : MonoBehaviour
{
    public Camera targetCamera;

    private void LateUpdate()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;
        if (targetCamera == null) return;

        // Face the camera (you can remove the Y line if you want full 3D facing)
        Vector3 dir = transform.position - targetCamera.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;

        transform.rotation = Quaternion.LookRotation(dir);
    }
}
