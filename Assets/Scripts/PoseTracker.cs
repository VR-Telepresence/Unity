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
    [SerializeField] private float maxRadius = 0.20f;
    [Range(15, 90)]
    [SerializeField] private float sendRateHz = 60f;

    private Vector3 hmdOrigin;
    private Quaternion originRotation = Quaternion.identity;
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
            timer = 0f;
            SendPose();
        }
    }

    public void Recenter()
    {
        if (hmdCamera != null)
        {
            hmdOrigin = hmdCamera.position;
            Vector3 euler = hmdCamera.eulerAngles;
            originRotation = Quaternion.Euler(0f, euler.y, 0f);

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
            Vector3 localOffset = Quaternion.Inverse(originRotation) * worldOffset;

            Vector3 clamped = Vector3.ClampMagnitude(localOffset, maxRadius);

            LastSentYaw = hmdCamera.eulerAngles.y;
            LastSentPitch = hmdCamera.eulerAngles.x;

            HeadPosePayload payload = new HeadPosePayload
            {
                x = clamped.x,
                y = clamped.y,
                z = clamped.z,
                yaw = LastSentYaw,
                pitch = LastSentPitch,
                t = (DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds
            };

            Debug.Log($"[PoseTracker] x: {payload.x:F5}, {payload.y:F5} z: {payload.z:F5}");

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
}