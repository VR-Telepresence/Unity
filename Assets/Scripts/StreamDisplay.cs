using System;
using UnityEngine;

/// Receives packets from ViewportProvidingServer, shows the JPEG on the quad,
/// rotates the world-fixed StreamAnchor and hands detection boxes to BoxOverlay.
///
/// Packet (big-endian):
///   packetHasPose = false : [fid u32][segFid u32][segLen u32]                 [json][jpeg]
///   packetHasPose = true  : [fid u32][segFid u32][segLen u32][yaw f32][pitch f32] [json][jpeg]
/// Pose convention: yaw = degrees to the right, pitch = degrees UP (same as e2p h_deg / v_deg).
[RequireComponent(typeof(MeshRenderer))]
public class StreamDisplay : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private WebSocketClient networkClient;   // ws://<PC-IP>:8766
    [SerializeField] private MeshRenderer targetRenderer;
    [SerializeField] private Transform streamAnchor;          // parent of this quad and of BoxOverlay
    [SerializeField] private BoxOverlay boxOverlay;
    [SerializeField] private PoseTracker poseTracker;

    [Header("Viewport geometry (must match e2p fov_deg=(90,60))")]
    [SerializeField] private float distance = 2f;
    [SerializeField] private float fovHorizontal = 90f;
    [SerializeField] private float fovVertical = 60f;

    [Header("Packet format")]
    [Tooltip("Off: 12-byte header, anchor uses PoseTracker.LastSent* (approximate). " +
             "On: 20-byte header with the yaw/pitch the frame was cut at (exact).")]
    [SerializeField] private bool packetHasPose = false;

    private Texture2D videoTexture;
    private byte[] latestPacket;
    private readonly object lockObj = new object();
    private long lastSegFid = -1;
    private readonly byte[] f4 = new byte[4];

    void Start()
    {
        float width = 2f * distance * Mathf.Tan(fovHorizontal * 0.5f * Mathf.Deg2Rad);
        float height = 2f * distance * Mathf.Tan(fovVertical * 0.5f * Mathf.Deg2Rad);

        transform.localPosition = new Vector3(0f, 0f, distance);
        transform.localRotation = Quaternion.identity;
        transform.localScale = new Vector3(width, height, 1f);

        if (boxOverlay != null)
            boxOverlay.Configure(width, height, distance - 0.002f);

        videoTexture = new Texture2D(256, 256, TextureFormat.RGB24, false);
        videoTexture.wrapMode = TextureWrapMode.Clamp;

        Material mat = targetRenderer.material;
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", videoTexture);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", videoTexture);
        mat.mainTexture = videoTexture;

        networkClient.OnBinaryMessageReceived += HandlePacket;
    }

    private void HandlePacket(byte[] data)
    {
        lock (lockObj) { latestPacket = data; }   // keep only the newest packet
    }

    void Update()
    {
        byte[] d;
        lock (lockObj) { d = latestPacket; latestPacket = null; }
        if (d == null) return;

        int hdr = packetHasPose ? 20 : 12;
        if (d.Length < hdr) return;

        uint fid = U32(d, 0);
        uint segFid = U32(d, 4);
        int segLen = (int)U32(d, 8);
        int jpegOff = hdr + segLen;
        if (segLen < 0 || jpegOff >= d.Length) return;

        float yaw, pitch;
        if (packetHasPose) { yaw = F32(d, 12); pitch = F32(d, 16); }
        else if (poseTracker != null) { yaw = poseTracker.LastSentYaw; pitch = poseTracker.LastSentPitch; }
        else { yaw = 0f; pitch = 0f; }

        // World-fixed anchor, rotated to where the frame was cut.
        if (streamAnchor != null)
        {
            if (poseTracker != null && poseTracker.HasOrigin)
                streamAnchor.position = poseTracker.HmdOrigin;
            streamAnchor.rotation = Quaternion.Euler(-pitch, yaw, 0f);   // Unity pitch is positive DOWN
        }

        var jpeg = new byte[d.Length - jpegOff];
        Buffer.BlockCopy(d, jpegOff, jpeg, 0, jpeg.Length);
        videoTexture.LoadImage(jpeg);

        // Detections are re-sent with every packet, parse only when they changed.
        if (boxOverlay != null && segFid != lastSegFid)
        {
            lastSegFid = segFid;
            string json = System.Text.Encoding.UTF8.GetString(d, hdr, segLen);
            var frame = JsonUtility.FromJson<DetFrame>(json);
            boxOverlay.Show(frame != null ? frame.objects : null);
            // fid - segFid = age of the detections in frames (log it while tuning)
        }
    }

    private static uint U32(byte[] b, int o) =>
        (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);

    private float F32(byte[] b, int o)
    {
        f4[0] = b[o + 3]; f4[1] = b[o + 2]; f4[2] = b[o + 1]; f4[3] = b[o];
        return BitConverter.ToSingle(f4, 0);
    }

    void OnDestroy()
    {
        if (networkClient != null) networkClient.OnBinaryMessageReceived -= HandlePacket;
        if (videoTexture != null) Destroy(videoTexture);
    }
}