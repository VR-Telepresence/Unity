using UnityEngine;

[ExecuteAlways]
public class FitQuadToCamera : MonoBehaviour
{
    public Camera targetCamera;
    public float distanceFromCamera = 1.0f;

    void Start()
    {
        FitToFrustum();
    }

    void Update()
    {
        FitToFrustum();
    }

    public void FitToFrustum()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
            if (targetCamera == null) return;
        }

        transform.position = targetCamera.transform.position + targetCamera.transform.forward * distanceFromCamera;
        transform.rotation = targetCamera.transform.rotation;

        float frustumHeight = 2.0f * distanceFromCamera * Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float frustumWidth = frustumHeight * targetCamera.aspect;

        transform.localScale = new Vector3(frustumWidth, frustumHeight, 1.0f);
    }
}