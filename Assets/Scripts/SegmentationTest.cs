using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

[Serializable] public class Pt { public float x, y; }
[Serializable] public class Obj { public string label; public float confidence; public Pt[] polygon; }
[Serializable] public class SegMsg { public Obj[] objects; }

public class SegmentationTest : MonoBehaviour
{
    public string url = "ws://127.0.0.1:8766";
    public float size = 4f;

    ClientWebSocket ws;
    CancellationTokenSource cts = new CancellationTokenSource();
    readonly object lockObj = new object();
    SegMsg latest;
    uint latestFrameId, latestSegId;

    async void Start()
    {
        ws = new ClientWebSocket();
        try
        {
            await ws.ConnectAsync(new Uri(url), cts.Token);
            Debug.Log("[Unity] Viewport-Server connected!");
            _ = Task.Run(ReceiveLoop);
        }
        catch (Exception e) { Debug.LogError("[Unity] Connect failed: " + e.Message); }
    }

    async Task ReceiveLoop()
    {
        var buf = new byte[1 << 20]; // 1MB Puffer
        try
        {
            while (ws.State == WebSocketState.Open && !cts.IsCancellationRequested)
            {
                int len = 0;
                WebSocketReceiveResult r;
                do
                {
                    r = await ws.ReceiveAsync(new ArraySegment<byte>(buf, len, buf.Length - len), cts.Token);
                    len += r.Count;
                } while (!r.EndOfMessage);

                if (len < 12) continue;

                // 12 Bytes Header auslesen
                uint frameId = ReadU32(buf, 0);
                uint segId = ReadU32(buf, 4);
                int jsonLen = (int)ReadU32(buf, 8);

                if (jsonLen > 0 && (12 + jsonLen) <= len)
                {
                    string json = Encoding.UTF8.GetString(buf, 12, jsonLen);
                    var seg = JsonUtility.FromJson<SegMsg>(json);

                    lock (lockObj)
                    {
                        latest = seg;
                        latestFrameId = frameId;
                        latestSegId = segId;
                    }
                }
            }
        }
        catch (Exception e) { Debug.LogWarning("[Unity] Receive stopped: " + e.Message); }
    }

    static uint ReadU32(byte[] b, int o) =>
        ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3];

    void Update()
    {
        SegMsg m; uint fid, sid;
        lock (lockObj) { m = latest; fid = latestFrameId; sid = latestSegId; }

        if (Time.frameCount % 30 == 0)
        {
            if (m == null)
                Debug.Log("[Unity] Noch keine Segmentierungsdaten empfangen.");
            else
                Debug.Log($"[Unity] frame={fid} seg={sid} lag={fid - sid} objects={m.objects?.Length ?? 0}");
        }

        if (m?.objects == null) return;

        foreach (var o in m.objects)
        {
            if (o.polygon == null || o.polygon.Length < 2) continue;

            for (int i = 0; i < o.polygon.Length; i++)
            {
                var a = o.polygon[i];
                var b = o.polygon[(i + 1) % o.polygon.Length];
                Debug.DrawLine(
                    new Vector3(a.x * size, (1f - a.y) * size, 0),
                    new Vector3(b.x * size, (1f - b.y) * size, 0),
                    Color.green);
            }
        }
    }

    void OnDestroy()
    {
        cts?.Cancel();
        ws?.Dispose();
        cts?.Dispose();
    }
}