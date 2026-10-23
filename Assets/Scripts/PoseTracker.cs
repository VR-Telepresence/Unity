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
    [Header("Referenzen")]
    [SerializeField] private Transform hmdCamera;
    [SerializeField] private WebSocketClient networkClient;

    [Header("Tracking Parameter")]
    [Tooltip("Maximaler Bewegungsradius in Metern vor Artefakt-Bildung")]
    [SerializeField] private float maxRadius = 0.20f;
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

        if (networkClient == null)
            networkClient = GetComponent<WebSocketClient>();
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

            Vector3 offset = hmdCamera.position - hmdOrigin;
            Vector3 clamped = Vector3.ClampMagnitude(offset, maxRadius);

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

            string json = JsonUtility.ToJson(payload);
            await networkClient.SendTextAsync(json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PoseTracker] Sendefehler: {ex.Message}");
        }
        finally
        {
            isSending = false;
        }
    }
}