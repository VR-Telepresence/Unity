using UnityEngine;

[RequireComponent(typeof(MeshRenderer))]
public class VideoStreamDisplay : MonoBehaviour
{
    [SerializeField] private WebSocketClient networkClient;
    [SerializeField] private MeshRenderer targetRenderer;

    private Texture2D videoTexture;
    private byte[] latestFrameBytes;
    private readonly object lockObj = new object();
    private bool hasNewFrame = false;

    void Start()
    {
        videoTexture = new Texture2D(256, 256, TextureFormat.RGB24, false);

        Material mat = targetRenderer.material;
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", videoTexture);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", videoTexture);
        mat.mainTexture = videoTexture;

        networkClient.OnBinaryMessageReceived += HandleBinaryFrame;
    }

    private void HandleBinaryFrame(byte[] data)
    {
        lock (lockObj)
        {
            latestFrameBytes = data;
            hasNewFrame = true;
        }
    }

    void Update()
    {
        if (!hasNewFrame) return;

        byte[] frameToLoad = null;
        lock (lockObj)
        {
            frameToLoad = latestFrameBytes;
            hasNewFrame = false;
        }

        if (frameToLoad != null && videoTexture != null)
        {
            videoTexture.LoadImage(frameToLoad);
        }
    }

    void OnDestroy()
    {
        networkClient.OnBinaryMessageReceived -= HandleBinaryFrame;

        if (videoTexture != null)
            Destroy(videoTexture);
    }
}