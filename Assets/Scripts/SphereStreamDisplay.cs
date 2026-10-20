using System;
using UnityEngine;

public class SphereStreamDisplay : MonoBehaviour
{
    public enum DisplayMode { CurvedViewport, FullSphere360 }
    public DisplayMode mode = DisplayMode.CurvedViewport;

    [Header("Referenzen")]
    public WebSocketClient networkClient;
    public PoseTracker poseTracker;
    public Transform hmdCamera;

    [Header("Darstellungsmodus: CurvedViewport")]
    public float displayDistance = 1.5f;
    [Range(60f, 120f)] public float fieldOfView = 100f;

    [Header("Darstellungsmodus: FullSphere360")]
    public float sphereRadius = 50f;

    [Header("Textur")]
    public FilterMode textureFilterMode = FilterMode.Bilinear;

    private Texture2D streamTexture;
    private Renderer targetRenderer;

    public float CurrentFps { get; private set; }
    public double CurrentLatencyMs { get; private set; }
    public int LastReceivedBytes { get; private set; }

    private float fpsTimer = 0f;
    private int fpsCounter = 0;

    void Start()
    {
        if (hmdCamera == null && Camera.main != null)
            hmdCamera = Camera.main.transform;

        if (networkClient == null)
            networkClient = FindFirstObjectByType<WebSocketClient>();

        if (poseTracker == null)
            poseTracker = FindFirstObjectByType<PoseTracker>();

        targetRenderer = GetComponent<Renderer>();

        SetupMode();

        streamTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        streamTexture.filterMode = textureFilterMode;
        ApplyTexture(streamTexture);
    }

    private void SetupMode()
    {
        if (mode == DisplayMode.CurvedViewport)
        {
            float size = 2f * displayDistance * Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            transform.localScale = new Vector3(size, size, 1f);
            transform.localPosition = new Vector3(0, 0, displayDistance);
            transform.localRotation = Quaternion.identity;
        }
        else
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = new Vector3(sphereRadius, sphereRadius, sphereRadius);
        }
    }

    private void ApplyTexture(Texture2D tex)
    {
        if (targetRenderer == null || targetRenderer.material == null) return;
        targetRenderer.material.mainTexture = tex;
        if (targetRenderer.material.HasProperty("_BaseMap"))
        {
            targetRenderer.material.SetTexture("_BaseMap", tex);
        }
    }

    private byte[] latestPayload = null;
    private bool textureNeedsUpdate = false;
    private float lastTextureUpdateTime = 0f;
    private float textureUpdateInterval = 0.033f; // ~30 FPS

    void Update()
    {
        // FPS Berechnen
        fpsCounter++;
        fpsTimer += Time.unscaledDeltaTime;
        if (fpsTimer >= 0.5f)
        {
            CurrentFps = fpsCounter / fpsTimer;
            fpsCounter = 0;
            fpsTimer = 0f;
        }

        // Neuestes Frame aus der Queue holen
        while (networkClient != null && networkClient.ReceivedPayloads.TryDequeue(out byte[] rawPayload))
        {
            if (rawPayload != null && rawPayload.Length > 0)
            {
                latestPayload = rawPayload;
                textureNeedsUpdate = true;
                LastReceivedBytes = rawPayload.Length;
            }
        }

        // Auf die Kugel/das Display malen
        if (textureNeedsUpdate && latestPayload != null && (Time.time - lastTextureUpdateTime >= textureUpdateInterval))
        {
            textureNeedsUpdate = false;
            lastTextureUpdateTime = Time.time;

            // Sicheres Laden des Bildes (egal ob JPEG oder PNG)
            streamTexture.LoadImage(latestPayload);
            ApplyTexture(streamTexture);
        }

        // Blickrichtung an Python senden, wenn im CurvedViewport-Modus
        if (mode == DisplayMode.CurvedViewport && networkClient != null && networkClient.IsConnected && hmdCamera != null)
        {
            Vector3 euler = hmdCamera.eulerAngles;
            float yaw = euler.y > 180f ? euler.y - 360f : euler.y;
            float pitch = euler.x > 180f ? euler.x - 360f : euler.x;

            string poseJson = $"{{\"yaw\": {yaw.ToString(System.Globalization.CultureInfo.InvariantCulture)}, \"pitch\": {pitch.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";
            _ = networkClient.SendTextAsync(poseJson);
        }
    }

    void OnGUI()
    {
        GUI.Box(new Rect(15, 15, 240, 115), $"Stream Diagnose ({mode})");
        GUI.Label(new Rect(25, 40, 220, 20), $"Display FPS: {CurrentFps:F1}");

        string col = CurrentLatencyMs < 35 ? "lime" : (CurrentLatencyMs < 60 ? "yellow" : "red");
        GUI.Label(new Rect(25, 60, 220, 20), $"Roundtrip: <color={col}>{CurrentLatencyMs:F1} ms</color>");

        string pmode = (poseTracker != null) ? poseTracker.currentMode.ToUpper() : "N/A";
        GUI.Label(new Rect(25, 80, 220, 20), $"Modus: <b>{pmode}</b> [Space]");
        GUI.Label(new Rect(25, 100, 220, 20), $"Payload: {LastReceivedBytes / 1024f:F1} KB");
    }
}