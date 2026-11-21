using System;
using UnityEngine;

[Serializable]
public class HeadPosePayload
{
    public float x;
    public float y;
    public float z;
    public float yaw;
    public float pitch;
    public double t;
}

public class PoseTracker : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform hmdCamera;
    [SerializeField] private WebSocketClient networkClient;

    [Header("Tracking Parameters")]
    [SerializeField] private float maxRadius = 0.07f;
    [Range(15, 90)]
    [SerializeField] private float sendRateHz = 60f;

    private Vector3 hmdOrigin;
    private bool isOriginSet = false;
    private float sendInterval;
    private float timer = 0f;
    private bool isSending = false;

    public float LastSentYaw { get; private set; }
    public float LastSentPitch { get; private set; }

    void Awake()
    {
        sendInterval = 1f / sendRateHz;
    }

    void Start()
    {
        if (hmdCamera == null && Camera.main != null)
            hmdCamera = Camera.main.transform;
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= sendInterval)
        {
            timer -= sendInterval;
            SendPose();
        }
    }

    public void Recenter()
    {
        if (hmdCamera != null)
        {
            hmdOrigin = hmdCamera.position;
            isOriginSet = true;
        }
    }

    private async void SendPose()
    {
        if (networkClient == null || !networkClient.IsConnected || hmdCamera == null || isSending)
            return;

        isSending = true;

        try
        {
            if (!isOriginSet)
                Recenter();

            Vector3 worldOffset = hmdCamera.position - hmdOrigin;

            if (Mathf.Abs(worldOffset.x) > maxRadius)
                hmdOrigin.x = hmdCamera.position.x - Mathf.Sign(worldOffset.x) * maxRadius;
            if (Mathf.Abs(worldOffset.y) > maxRadius)
                hmdOrigin.y = hmdCamera.position.y - Mathf.Sign(worldOffset.y) * maxRadius;
            if (Mathf.Abs(worldOffset.z) > maxRadius)
                hmdOrigin.z = hmdCamera.position.z - Mathf.Sign(worldOffset.z) * maxRadius;

            worldOffset = hmdCamera.position - hmdOrigin;

            float currentYaw = hmdCamera.eulerAngles.y;
            Quaternion currentYawRotation = Quaternion.Euler(0f, currentYaw, 0f);
            Vector3 viewOffset = Quaternion.Inverse(currentYawRotation) * worldOffset;

            Vector3 clamped = new Vector3(
                Mathf.Clamp(viewOffset.x, -maxRadius, maxRadius),
                Mathf.Clamp(viewOffset.y, -maxRadius, maxRadius),
                Mathf.Clamp(viewOffset.z, -maxRadius, maxRadius)
            );

            LastSentYaw = currentYaw;
            LastSentPitch = NormalizeSignedAngle(hmdCamera.eulerAngles.x);

            HeadPosePayload payload = new HeadPosePayload
            {
                x = clamped.x,
                y = clamped.y,
                z = clamped.z,
                yaw = LastSentYaw,
                pitch = LastSentPitch,
                t = (DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds
            };

            Debug.Log($"[PoseTracker] x: {payload.x:F5}, y: {payload.y:F5} z: {payload.z:F5}");

            string json = JsonUtility.ToJson(payload);
            await networkClient.SendTextAsync(json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PoseTracker] Sending error: {ex.Message}");
        }
        finally
        {
            isSending = false;
        }
    }

    private static float NormalizeSignedAngle(float angle)
    {
        if (angle > 180f)
            angle -= 360f;
        return angle;
    }
}