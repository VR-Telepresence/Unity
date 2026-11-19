using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Receives segmentation results from the Python SegmentationServer (JSON text over WebSocket)
/// and draws them with one LineRenderer per detected object.
///
/// Put this on the object that shows the PVSNet stream, e.g. "StreamPlane"
/// (or assign that object as "surface").
/// Switch between outline and bounding box with the "mode" field, the toggle key,
/// or by calling ToggleMode() / SetMode() from a button or controller event.
/// </summary>
public class SegmentationReceiver : MonoBehaviour
{
    public enum DrawMode { Outline, BoundingBox }
    public enum SurfaceType { Plane, Quad }

    [Header("Server")]
    [Tooltip("Address of the Python SegmentationServer.")]
    public string serverUrl = "ws://127.0.0.1:8767";
    [Tooltip("Seconds between connection attempts while the server is not reachable.")]
    public float reconnectDelay = 2f;

    [Header("Mode")]
    public DrawMode mode = DrawMode.Outline;
    [Tooltip("Key that switches between Outline and BoundingBox (old Input Manager only).")]
    public KeyCode toggleKey = KeyCode.B;

    [Header("Surface the picture is shown on")]
    [Tooltip("Transform of the object that shows the stream. Empty = this object.")]
    public Transform surface;
    [Tooltip("Mesh of the surface: Unity's Plane (10 x 10, lies in X/Z) or Unity's Quad (1 x 1, stands in X/Y).")]
    public SurfaceType surfaceType = SurfaceType.Plane;
    [Tooltip("Distance the lines float in front of the surface (local units).")]
    public float surfaceOffset = 0.01f;
    [Tooltip("Tick these if the lines are mirrored compared to the picture.")]
    public bool flipX;
    public bool flipY;

    [Header("Look")]
    public float lineWidth = 0.03f;
    public Color outlineColor = Color.green;
    public Color boxColor = Color.yellow;
    [Tooltip("Optional. Empty = a simple unlit material is created at runtime.")]
    public Material lineMaterial;

    [Header("Filter")]
    [Range(0f, 1f)] public float minConfidence = 0f;
    [Tooltip("Only draw objects with this label, e.g. \"person\". Empty = draw all.")]
    public string onlyLabel = "";
    [Tooltip("Hide the lines when no message arrived for this many seconds.")]
    public float hideAfterSeconds = 0.5f;

    public bool IsConnected { get; private set; }

    // ---- JSON layout sent by Python (nested so the names cannot clash with other scripts) ----
    [Serializable] private class Point { public float x; public float y; }
    [Serializable] private class SegObject { public string label; public float confidence; public Point[] polygon; }
    [Serializable] private class Message { public string type; public int frame_id; public SegObject[] objects; }

    private readonly object lockObj = new object();
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly List<LineRenderer> lines = new List<LineRenderer>();

    private CancellationTokenSource cts;
    private Message latest;
    private long latestTimeMs;
    private Material runtimeMaterial;

    // ------------------------------------------------------------------ public API

    public void ToggleMode()
    {
        SetMode(mode == DrawMode.Outline ? DrawMode.BoundingBox : DrawMode.Outline);
    }

    public void SetMode(DrawMode newMode)
    {
        mode = newMode;
        Debug.Log("[SegmentationReceiver] Mode: " + mode);
    }

    // ------------------------------------------------------------------ Unity events

    void Awake()
    {
        if (surface == null) surface = transform;
    }

    void OnEnable()
    {
        cts = new CancellationTokenSource();
        string url = serverUrl;
        int delayMs = Mathf.Max(200, Mathf.RoundToInt(reconnectDelay * 1000f));
        CancellationToken token = cts.Token;
        Task.Run(() => ConnectionLoop(url, delayMs, token));
    }

    void OnDisable()
    {
        if (cts != null)
        {
            cts.Cancel();
            cts.Dispose();
            cts = null;
        }
        IsConnected = false;
        HideLinesFrom(0);
    }

    void OnDestroy()
    {
        if (runtimeMaterial != null) Destroy(runtimeMaterial);
    }

    void Update()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey)) ToggleMode();
#endif

        Message msg;
        long ageMs;
        lock (lockObj)
        {
            msg = latest;
            ageMs = clock.ElapsedMilliseconds - latestTimeMs;
        }

        int used = 0;
        bool fresh = msg != null && msg.objects != null && ageMs <= hideAfterSeconds * 1000f;

        if (fresh)
        {
            foreach (SegObject obj in msg.objects)
            {
                if (obj == null || obj.polygon == null || obj.polygon.Length < 3) continue;
                if (obj.confidence < minConfidence) continue;
                if (!string.IsNullOrEmpty(onlyLabel) && obj.label != onlyLabel) continue;

                LineRenderer line = GetLine(used++);
                if (mode == DrawMode.Outline) DrawOutline(line, obj.polygon);
                else DrawBoundingBox(line, obj.polygon);
            }
        }

        HideLinesFrom(used);
    }

    // ------------------------------------------------------------------ drawing

    private void DrawOutline(LineRenderer line, Point[] polygon)
    {
        ApplyLook(line, outlineColor);
        line.positionCount = polygon.Length;
        for (int i = 0; i < polygon.Length; i++)
            line.SetPosition(i, ToLocal(polygon[i].x, polygon[i].y));
    }

    private void DrawBoundingBox(LineRenderer line, Point[] polygon)
    {
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        foreach (Point p in polygon)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }

        ApplyLook(line, boxColor);
        line.positionCount = 4;
        line.SetPosition(0, ToLocal(minX, minY));
        line.SetPosition(1, ToLocal(maxX, minY));
        line.SetPosition(2, ToLocal(maxX, maxY));
        line.SetPosition(3, ToLocal(minX, maxY));
    }

    /// <summary>Image coordinates (0..1, origin top-left) to a position in the surface's local space.</summary>
    private Vector3 ToLocal(float x, float y)
    {
        if (flipX) x = 1f - x;
        if (flipY) y = 1f - y;

        if (surfaceType == SurfaceType.Plane)
        {
            // Unity's Plane: 10 x 10 in local X/Z, visible from +Y.
            // Its texture runs towards -X (right) and -Z (up).
            return new Vector3((0.5f - x) * 10f, surfaceOffset, (y - 0.5f) * 10f);
        }
        else
        {
            // Unity's Quad: 1 x 1 in local X/Y, visible from -Z.
            return new Vector3(x - 0.5f, 0.5f - y, -surfaceOffset);
        }
    }

    private void ApplyLook(LineRenderer line, Color color)
    {
        line.enabled = true;
        line.startWidth = lineWidth;
        line.endWidth = lineWidth;
        line.startColor = color;
        line.endColor = color;
    }

    private LineRenderer GetLine(int index)
    {
        while (lines.Count <= index)
        {
            GameObject go = new GameObject("SegmentationLine " + lines.Count);
            // child of the surface + local coordinates: the lines stay glued to the
            // picture even when the surface is attached to the VR camera
            go.transform.SetParent(surface, false);
            go.layer = surface.gameObject.layer;

            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.numCornerVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = GetMaterial();
            line.enabled = false;
            lines.Add(line);
        }
        return lines[index];
    }

    private Material GetMaterial()
    {
        if (lineMaterial != null) return lineMaterial;
        if (runtimeMaterial == null)
        {
            // Unlit and uses the line's vertex colour; works in the built-in pipeline and URP.
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogWarning("[SegmentationReceiver] Shader 'Sprites/Default' not found, please assign a Line Material.");
                return null;
            }
            runtimeMaterial = new Material(shader);
        }
        return runtimeMaterial;
    }

    private void HideLinesFrom(int index)
    {
        for (int i = index; i < lines.Count; i++)
            if (lines[i] != null) lines[i].enabled = false;
    }

    // ------------------------------------------------------------------ networking (background thread)

    private async Task ConnectionLoop(string url, int delayMs, CancellationToken token)
    {
        bool failureLogged = false;

        while (!token.IsCancellationRequested)
        {
            using (ClientWebSocket ws = new ClientWebSocket())
            {
                try
                {
                    await ws.ConnectAsync(new Uri(url), token);
                    IsConnected = true;
                    failureLogged = false;
                    Debug.Log("[SegmentationReceiver] Connected to " + url);

                    await ReceiveLoop(ws, token);
                    Debug.Log("[SegmentationReceiver] Connection closed, reconnecting ...");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    if (!failureLogged && !token.IsCancellationRequested)
                    {
                        Debug.LogWarning("[SegmentationReceiver] No connection to " + url + " (" + e.Message + "). Retrying every " + delayMs + " ms.");
                        failureLogged = true;
                    }
                }
            }

            IsConnected = false;

            try { await Task.Delay(delayMs, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ReceiveLoop(ClientWebSocket ws, CancellationToken token)
    {
        byte[] buffer = new byte[64 * 1024];

        while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            // one message can arrive in several pieces, collect until EndOfMessage
            int length = 0;
            WebSocketReceiveResult result;
            do
            {
                if (length == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), token);
                length += result.Count;
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close) return;
            if (result.MessageType != WebSocketMessageType.Text) continue;

            Message msg;
            try
            {
                msg = JsonUtility.FromJson<Message>(Encoding.UTF8.GetString(buffer, 0, length));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SegmentationReceiver] Could not parse message: " + e.Message);
                continue;
            }
            if (msg == null) continue;

            lock (lockObj)
            {
                latest = msg;
                latestTimeMs = clock.ElapsedMilliseconds;
            }
        }
    }
}