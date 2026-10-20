using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[Serializable]
public class HeadPosePayload
{
    public float x;
    public float y;
    public float z;
    public float yaw;
    public float pitch;
    public string mode;
    public double t;
}

public class PoseTracker : MonoBehaviour
{
    [Header("Referenzen")]
    public Transform hmdCamera;
    public WebSocketClient networkClient;

    [Header("Tracking Parameter")]
    public float maxRadius = 0.20f;
    [Range(15, 90)] public float sendRateHz = 45f;

    [Header("Status")]
    public string currentMode = "pvsnet";

    private Vector3 hmdOrigin;
    private bool isOriginSet = false;
    private float lastSendTime = 0f;
    private bool isSending = false;

    public float LastSentYaw { get; private set; }
    public float LastSentPitch { get; private set; }

    void Start()
    {
        if (hmdCamera == null && Camera.main != null)
            hmdCamera = Camera.main.transform;

        if (networkClient == null)
            networkClient = GetComponent<WebSocketClient>();
    }

    void Update()
    {
        HandleInput();
        SendPose();
    }

    private void HandleInput()
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.rKey.wasPressedThisFrame)
            {
                Recenter();
            }
            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                currentMode = (currentMode == "pvsnet") ? "original" : "pvsnet";
                Debug.Log($"<color=yellow>[PoseTracker]</color> Modus gewechselt: {currentMode}");
            }
        }
#endif
    }

    public void Recenter()
    {
        if (hmdCamera != null)
        {
            hmdOrigin = hmdCamera.position;
            isOriginSet = true;
            Debug.Log("<color=cyan>[PoseTracker]</color> Tracking neu zentriert!");
        }
    }

    private async void SendPose()
    {
        if (networkClient == null || !networkClient.IsConnected || hmdCamera == null || isSending)
            return;

        if (Time.time - lastSendTime < (1f / sendRateHz))
            return;

        lastSendTime = Time.time;
        isSending = true;

        try
        {
            if (!isOriginSet) Recenter();

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
                mode = currentMode,
                t = (DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds
            };

            string json = JsonUtility.ToJson(payload);
            await networkClient.SendTextAsync(json);
        }
        finally
        {
            isSending = false;
        }
    }
}