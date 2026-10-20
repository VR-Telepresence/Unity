using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class WebSocketClient : MonoBehaviour
{
    [Header("Server Konfiguration")]
    public string serverUrl = "ws://127.0.0.1:8765";

    private ClientWebSocket ws;
    private CancellationTokenSource cts;

    // Thread-sichere Queue für empfangene Frames
    public readonly ConcurrentQueue<byte[]> ReceivedPayloads = new ConcurrentQueue<byte[]>();

    public bool IsConnected => ws != null && ws.State == WebSocketState.Open;

    async void Start()
    {
        cts = new CancellationTokenSource();
        await Connect();
    }

    public async Task Connect()
    {
        ws = new ClientWebSocket();
        try
        {
            await ws.ConnectAsync(new Uri(serverUrl), cts.Token);
            Debug.Log("<color=green>[WebSocketClient]</color> Verbunden mit " + serverUrl);
            _ = Task.Run(ReceiveLoop);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"<color=orange>[WebSocketClient]</color> Verbindung fehlgeschlagen: {ex.Message}");
        }
    }

    public async Task SendTextAsync(string text)
    {
        if (!IsConnected) return;
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
    }

    private async Task ReceiveLoop()
    {
        byte[] buffer = new byte[1024 * 64];

        while (IsConnected && !cts.IsCancellationRequested)
        {
            using (var ms = new MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Binary && ms.Length > 0)
                {
                    while (ReceivedPayloads.TryDequeue(out _)) { }

                    // Nur das allerneueste Paket reinlegen
                    ReceivedPayloads.Enqueue(ms.ToArray());
                }
            }
        }
    }

    private async void OnDestroy()
    {
        cts?.Cancel();
        if (IsConnected)
        {
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
        }
    }
}