using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
public class SegObject
{
    public string label;
    public float confidence;
    public List<Vector2> polygon;
}

[Serializable]
public class SegPayload
{
    public string type;
    public List<SegObject> objects;
}

public class WebcamYoloReceiver : MonoBehaviour
{
    public string serverUrl = "ws://127.0.0.1:8765";
    public MeshRenderer quadRenderer;
    public LineRenderer lineRendererPrefab;

    private ClientWebSocket ws;
    private CancellationTokenSource cts;
    private Texture2D webcamTexture;
    private List<LineRenderer> activeLines = new List<LineRenderer>();

    async void Start()
    {
        webcamTexture = new Texture2D(2, 2);
        if (quadRenderer != null)
        {
            quadRenderer.material.mainTexture = webcamTexture;
        }

        cts = new CancellationTokenSource();
        await Connect();
    }

    private async Task Connect()
    {
        ws = new ClientWebSocket();
        try
        {
            await ws.ConnectAsync(new Uri(serverUrl), cts.Token);
            Debug.Log("[Unity] Connect with Python Webcam Server!");
            _ = ReceiveLoop();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Unity] Connection failed: {ex.Message}");
        }
    }

    private async Task ReceiveLoop()
    {
        byte[] buffer = new byte[1024 * 1024 * 2]; // 2MB Puffer
        while (ws != null && ws.State == WebSocketState.Open)
        {
            try
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    SegPayload payload = JsonUtility.FromJson<SegPayload>(json);
                    if (payload != null && payload.type == "segmentation")
                    {
                        DrawPolygons(payload.objects);
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    byte[] imageBytes = new byte[result.Count];
                    Array.Copy(buffer, imageBytes, result.Count);
                    webcamTexture.LoadImage(imageBytes);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Unity] Connection Error: {ex.Message}");
                break;
            }
        }
    }

    private void DrawPolygons(List<SegObject> objects)
    {
        foreach (var line in activeLines)
            if (line != null) Destroy(line.gameObject);
        activeLines.Clear();

        if (objects == null || quadRenderer == null) return;

        Transform quad = quadRenderer.transform;

        foreach (var obj in objects)
        {
            if (obj.polygon == null || obj.polygon.Count < 3) continue;

            LineRenderer line = Instantiate(lineRendererPrefab, transform);
            line.useWorldSpace = true;
            line.loop = true;
            line.widthMultiplier = 0.01f;
            line.positionCount = obj.polygon.Count;

            for (int i = 0; i < obj.polygon.Count; i++)
            {
                Vector2 p = obj.polygon[i];
                Vector3 local = new Vector3(p.x - 0.5f, 0.5f - p.y, -0.01f);
                line.SetPosition(i, quad.TransformPoint(local));
            }
            activeLines.Add(line);
        }
    }

    private async void OnDestroy()
    {
        cts?.Cancel();
        if (ws != null && ws.State == WebSocketState.Open)
        {
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None); } catch { }
            ws.Dispose();
        }
    }
}