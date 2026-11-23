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
    [Flags]
    public enum DrawMode
    {
        None = 0,
        Outline = 1,
        BoundingBox = 2,
        Fill = 4,
        All = Outline | BoundingBox | Fill
    }
    public enum SurfaceType { Plane, Quad }

    [Header("Server")]
    [Tooltip("Address of the Python SegmentationServer.")]
    public string serverUrl = "ws://127.0.0.1:8767";
    [Tooltip("Seconds between connection attempts while the server is not reachable.")]
    public float reconnectDelay = 2f;

    [Header("Mode")]
    public DrawMode mode = DrawMode.Outline;
    [Tooltip("Key that switches between Outline > BoundingBox > OutlineAndFill.")]
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
    public float lineWidth = 0.05f;
    public Color outlineColor = Color.green;
    public Color boxColor = Color.yellow;
    public Color fillColor = new Color(0f, 1f, 0f, 0.35f);
    [Range(64, 1024)] public int fillResolution = 384;
    [Tooltip("Optional. Empty = a simple unlit material is created at runtime.")]
    public Material lineMaterial;
    public Material fillMaterial;

    [Header("Filter")]
    [Range(0f, 1f)] public float minConfidence = 0f;
    [Tooltip("Only draw objects with this label, e.g. \"person\". Empty = draw all.")]
    public string onlyLabel = "";
    [Tooltip("Hide the lines when no message arrived for this many seconds.")]
    public float hideAfterSeconds = 0.5f;

    public bool IsConnected { get; private set; }

    // ---- JSON layout sent by Python (nested so the names cannot clash with other scripts) ----
    [Serializable] private class Point { public float x; public float y; }
    [Serializable]
    private class SegObject
    {
        public string label;
        public float confidence;
        public Point[] polygon;
        [NonSerialized] public int[] triangles;      // cached triangulation
        [NonSerialized] public bool triangulated;
    }
    [Serializable] private class Message { public string type; public int frame_id; public SegObject[] objects; }

    private class FillSlot
    {
        public GameObject go;
        public MeshRenderer renderer;
        public Mesh mesh;
        public Texture2D tex;
        public Color32[] pixels;
        public SegObject builtFor;
    }

    private readonly object lockObj = new object();
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly List<LineRenderer> lines = new List<LineRenderer>();
    private readonly List<FillSlot> fills = new List<FillSlot>();
    private readonly List<float> crossings = new List<float>(64);

    private CancellationTokenSource cts;
    private Message latest;
    private long latestTimeMs;
    private Material runtimeMaterial;
    private Material runtimeFillMaterial;

    // ------------------------------------------------------------------ public API

    public void ToggleMode()
    {
        SetMode((DrawMode)(((int)mode + 1) % 8));
    }

    public void SetMode(DrawMode newMode)
    {
        mode = newMode;
        foreach (FillSlot f in fills) f.builtFor = null;
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
        HideFillsFrom(0);
    }

    void OnDestroy()
    {
        if (runtimeMaterial != null) Destroy(runtimeMaterial);
        if (runtimeFillMaterial != null) Destroy(runtimeFillMaterial);
        foreach (FillSlot f in fills)
        {
            if (f.mesh != null) Destroy(f.mesh);
            if (f.tex != null) Destroy(f.tex);
        }
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

        bool wantOutline = (mode & DrawMode.Outline) != 0;
        bool wantBox = (mode & DrawMode.BoundingBox) != 0;
        bool wantFill = (mode & DrawMode.Fill) != 0;

        int used = 0;
        int usedFills = 0;
        bool fresh = msg != null && msg.objects != null && ageMs <= hideAfterSeconds * 1000f;

        if (fresh)
        {
            foreach (SegObject obj in msg.objects)
            {
                if (obj == null || obj.polygon == null || obj.polygon.Length < 3) continue;
                if (obj.confidence < minConfidence) continue;
                if (!string.IsNullOrEmpty(onlyLabel) && obj.label != onlyLabel) continue;

                if (wantFill) DrawFill(GetFill(usedFills++), obj);
                if (wantOutline) DrawOutline(GetLine(used++), obj.polygon);
                if (wantBox) DrawBoundingBox(GetLine(used++), obj.polygon);
            }
        }

        HideLinesFrom(used);
        HideFillsFrom(usedFills);
    }

    // ------------------------------------------------------------------ drawing

    private void DrawOutline(LineRenderer line, Point[] polygon)
    {
        ApplyLook(line, outlineColor);
        line.positionCount = polygon.Length;
        for (int i = 0; i < polygon.Length; i++)
            line.SetPosition(i, ToLocal(polygon[i].x, polygon[i].y, surfaceOffset));
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
        line.SetPosition(0, ToLocal(minX, minY, surfaceOffset));
        line.SetPosition(1, ToLocal(maxX, minY, surfaceOffset));
        line.SetPosition(2, ToLocal(maxX, maxY, surfaceOffset));
        line.SetPosition(3, ToLocal(minX, maxY, surfaceOffset));
    }

    private static void GetBounds(Point[] polygon, out float minX, out float minY, out float maxX, out float maxY)
    {
        minX = float.MaxValue; minY = float.MaxValue;
        maxX = float.MinValue; maxY = float.MinValue;
        foreach (Point p in polygon)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }
    }

    private void DrawFill(FillSlot slot, SegObject obj)
    {
        Material m = slot.renderer.sharedMaterial;
        if (m != null) m.color = fillColor;

        slot.go.SetActive(true);

        // only rebuild when this slot shows a new object
        if (slot.builtFor == obj) return;

        Point[] poly = obj.polygon;
        float minX, minY, maxX, maxY;
        GetBounds(poly, out minX, out minY, out maxX, out maxY);
        float rangeX = maxX - minX;
        float rangeY = maxY - minY;
        if (rangeX < 1e-5f || rangeY < 1e-5f)
        {
            slot.go.SetActive(false);
            return;
        }

        int w = Mathf.Clamp(Mathf.CeilToInt(rangeX * fillResolution), 2, 1024);
        int h = Mathf.Clamp(Mathf.CeilToInt(rangeY * fillResolution), 2, 1024);

        // texture + pixel buffer (reused while the size stays the same)
        if (slot.tex == null || slot.tex.width != w || slot.tex.height != h)
        {
            if (slot.tex != null) Destroy(slot.tex);
            slot.tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            slot.tex.wrapMode = TextureWrapMode.Clamp;
            slot.tex.filterMode = FilterMode.Bilinear;
            slot.pixels = new Color32[w * h];
            slot.renderer.sharedMaterial.mainTexture = slot.tex;
        }

        Color32 outside = new Color32(255, 255, 255, 0);
        Color32 inside = new Color32(255, 255, 255, 255);
        Color32[] px = slot.pixels;
        for (int i = 0; i < px.Length; i++) px[i] = outside;

        int n = poly.Length;
        for (int row = 0; row < h; row++)
        {
            float yy = minY + (row + 0.5f) / h * rangeY;

            crossings.Clear();
            for (int i = 0; i < n; i++)
            {
                Point a = poly[i];
                Point b = poly[(i + 1) % n];
                if ((a.y <= yy && b.y > yy) || (b.y <= yy && a.y > yy))
                    crossings.Add(a.x + (yy - a.y) / (b.y - a.y) * (b.x - a.x));
            }
            if (crossings.Count < 2) continue;
            crossings.Sort();

            for (int k = 0; k + 1 < crossings.Count; k += 2)
            {
                float x0 = (crossings[k] - minX) / rangeX * w;
                float x1 = (crossings[k + 1] - minX) / rangeX * w;
                int start = Mathf.Max(0, Mathf.CeilToInt(x0 - 0.5f));
                int end = Mathf.Min(w - 1, Mathf.CeilToInt(x1 - 0.5f) - 1);
                for (int c = start; c <= end; c++) px[row * w + c] = inside;
            }
        }

        slot.tex.SetPixels32(px);
        slot.tex.Apply(false);

        float fillOffset = surfaceOffset * 0.5f;
        slot.mesh.Clear();
        slot.mesh.vertices = new Vector3[]
        {
            ToLocal(minX, minY, fillOffset),
            ToLocal(maxX, minY, fillOffset),
            ToLocal(maxX, maxY, fillOffset),
            ToLocal(minX, maxY, fillOffset)
        };
        slot.mesh.uv = new Vector2[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f)
        };
        slot.mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
        slot.mesh.RecalculateBounds();

        slot.builtFor = obj;
    }

    /// <summary>Image coordinates (0..1, origin top-left) to a position in the surface's local space.</summary>
    private Vector3 ToLocal(float x, float y, float offset)
    {
        if (flipX) x = 1f - x;
        if (flipY) y = 1f - y;

        if (surfaceType == SurfaceType.Plane)
        {
            // Unity's Plane: 10 x 10 in local X/Z, visible from +Y.
            // Its texture runs towards -X (right) and -Z (up).
            return new Vector3((0.5f - x) * 10f, offset, (y - 0.5f) * 10f);
        }
        else
        {
            // Unity's Quad: 1 x 1 in local X/Y, visible from -Z.
            return new Vector3(x - 0.5f, 0.5f - y, -offset);
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
            go.transform.SetParent(surface, false);
            go.layer = surface.gameObject.layer;

            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.numCornerVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = 1;
            line.sharedMaterial = GetMaterial();
            line.enabled = false;
            lines.Add(line);
        }
        return lines[index];
    }

    private FillSlot GetFill(int index)
    {
        while (fills.Count <= index)
        {
            GameObject go = new GameObject("SegmentationFill " + fills.Count);
            go.transform.SetParent(surface, false);
            go.layer = surface.gameObject.layer;

            go.AddComponent<MeshFilter>();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingOrder = 0;
            mr.sharedMaterial = GetFillMaterial();

            Mesh mesh = new Mesh();
            mesh.name = "SegmentationFillMesh";
            mesh.MarkDynamic();
            go.GetComponent<MeshFilter>().sharedMesh = mesh;

            go.SetActive(false);
            fills.Add(new FillSlot { go = go, renderer = mr, mesh = mesh, builtFor = null });
        }
        return fills[index];
    }

    private Material GetMaterial()
    {
        if (lineMaterial != null) return lineMaterial;
        if (runtimeMaterial == null)
        {
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

    private Material GetFillMaterial()
    {
        if (fillMaterial != null) return fillMaterial;
        if (runtimeFillMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogWarning("[SegmentationReceiver] Shader 'Sprites/Default' not found, please assign a Fill Material.");
                return null;
            }
            runtimeFillMaterial = new Material(shader);
            runtimeFillMaterial.color = fillColor;
        }
        return runtimeFillMaterial;
    }

    private void HideLinesFrom(int index)
    {
        for (int i = index; i < lines.Count; i++)
            if (lines[i] != null) lines[i].enabled = false;
    }

    private void HideFillsFrom(int index)
    {
        for (int i = index; i < fills.Count; i++)
            if (fills[i] != null && fills[i].go != null) fills[i].go.SetActive(false);
    }

    private static int[] Triangulate(Point[] poly)
    {
        int n = poly.Length;
        if (n < 3) return null;

        float area = 0f;
        for (int i = 0; i < n; i++)
        {
            Point a = poly[i], b = poly[(i + 1) % n];
            area += a.x * b.y - b.x * a.y;
        }
        if (Mathf.Abs(area) < 1e-9f) return null;

        List<int> idx = new List<int>(n);
        if (area > 0f) for (int i = 0; i < n; i++) idx.Add(i);
        else for (int i = n - 1; i >= 0; i--) idx.Add(i);

        List<int> tris = new List<int>((n - 2) * 3);
        int guard = 0;
        int maxIter = n * n + 10;

        while (idx.Count > 3 && guard++ < maxIter)
        {
            bool clipped = false;
            for (int i = 0; i < idx.Count; i++)
            {
                int i0 = idx[(i + idx.Count - 1) % idx.Count];
                int i1 = idx[i];
                int i2 = idx[(i + 1) % idx.Count];
                Point a = poly[i0], b = poly[i1], c = poly[i2];

                float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                if (cross <= 1e-12f) continue;

                bool inside = false;
                for (int j = 0; j < idx.Count; j++)
                {
                    int k = idx[j];
                    if (k == i0 || k == i1 || k == i2) continue;
                    if (PointInTriangle(poly[k], a, b, c)) { inside = true; break; }
                }
                if (inside) continue;

                tris.Add(i0); tris.Add(i1); tris.Add(i2);
                idx.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped) break;
        }

        if (idx.Count == 3)
        {
            tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]);
        }
        else if (idx.Count > 3)
        {
            for (int i = 1; i < idx.Count - 1; i++)
            {
                tris.Add(idx[0]); tris.Add(idx[i]); tris.Add(idx[i + 1]);
            }
        }

        return tris.ToArray();
    }

    private static bool PointInTriangle(Point p, Point a, Point b, Point c)
    {
        float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
        float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
        float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
        bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNeg && hasPos);
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