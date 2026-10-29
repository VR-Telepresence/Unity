using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class WebSocketClient : MonoBehaviour
{
    [Header("Server Konfiguration")]
    public string serverUrl = "ws://192.168.178.XX:8765";

    public event Action<byte[]> OnBinaryMessageReceived;

    private ClientWebSocket ws;
    private CancellationTokenSource cts;

    public bool IsConnected => ws != null && ws.State == WebSocketState.Open;

    async void Start()
    {
        cts = new CancellationTokenSource();
        await Connect();
    }

    public async Task Connect()
    {
        if (IsConnected) return;

        ws?.Dispose();
        ws = new ClientWebSocket();

        try
        {
            await ws.ConnectAsync(new Uri(serverUrl), cts.Token);
            Debug.Log($"[WebSocketClient] Verbunden mit {serverUrl}");
            _ = ReceiveLoop();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WebSocketClient] Verbindung fehlgeschlagen: {ex.Message}");
        }
    }

    private async Task ReceiveLoop()
    {
        byte[] buffer = new byte[1024 * 1024 * 4];

        while (IsConnected)
        {
            try
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    byte[] data = new byte[result.Count];
                    Array.Copy(buffer, data, result.Count);
                    OnBinaryMessageReceived?.Invoke(data);
                }
            }
            catch (Exception ex)
            {
                if (ws != null && ws.State != WebSocketState.Aborted)
                    Debug.LogWarning($"[WebSocketClient] Empfangsfehler: {ex.Message}");
                break;
            }
        }
    }

    public async Task SendTextAsync(string text)
    {
        if (!IsConnected) return;

        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WebSocketClient] Sende-Unterbrechung: {ex.Message}");
        }
    }

    private async void OnDestroy()
    {
        cts?.Cancel();

        if (ws != null)
        {
            if (ws.State == WebSocketState.Open)
            {
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "App Closed", CancellationToken.None);
                }
                catch { }
            }
            ws.Dispose();
            ws = null;
        }

        cts?.Dispose();
    }
}