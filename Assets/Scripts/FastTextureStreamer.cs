using UnityEngine;
using Unity.Collections;

public class FastTextureStreamer : MonoBehaviour
{
    private Texture2D streamTexture;
    private NativeArray<byte> pixelData;

    public int textureWidth = 512;
    public int textureHeight = 512;

    // Ziehe deinen WebSocketClient hier im Inspector rein (oder hole ihn per GetComponent)
    public WebSocketClient webSocketClient;

    void Start()
    {
        // Textur anlegen
        streamTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGB24, false);
        pixelData = streamTexture.GetRawTextureData<byte>();

        Renderer rend = GetComponent<Renderer>();
        if (rend != null)
        {
            rend.material.mainTexture = streamTexture;
            rend.material.SetTexture("_BaseMap", streamTexture); // <--- Das hier für URP ergänzen!
        }

        if (webSocketClient == null)
        {
            webSocketClient = GetComponent<WebSocketClient>();
        }
    }

    void Update()
    {
        if (webSocketClient != null && webSocketClient.ReceivedPayloads != null)
        {
            while (webSocketClient.ReceivedPayloads.TryDequeue(out byte[] rawBytes))
            {
                if (rawBytes != null && rawBytes.Length == pixelData.Length)
                {
                    pixelData.CopyFrom(rawBytes);
                    streamTexture.Apply();

                    // TEST-LOG: Siehst du das in der Unity Console?
                    Debug.Log("Bild erfolgreich in GPU-Puffer geschrieben!");
                }
                else if (rawBytes != null)
                {
                    Debug.LogWarning($"GRÖSSEN-FEHLER! Erwartet: {pixelData.Length}, Erhalten: {rawBytes.Length}");
                }
            }
        }
    }
}