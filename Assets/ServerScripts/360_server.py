import asyncio
import websockets
import cv2
import numpy as np
import struct
import os

# 360-Grad Panorama laden
if os.path.exists("pano.jpg"):
    pano = cv2.imread("pano.jpg")
    print("pano.jpg (Vollbild) erfolgreich geladen!")
else:
    print("Keine pano.jpg gefunden - erstelle Test-Panorama...")
    pano = np.zeros((1024, 2048, 3), dtype=np.uint8)
    cv2.rectangle(pano, (0, 0), (512, 1024), (0, 0, 255), -1)
    cv2.rectangle(pano, (512, 0), (1024, 1024), (0, 255, 0), -1)
    cv2.putText(pano, "360 VOLLBILD STREAM", (600, 512), cv2.FONT_HERSHEY_SIMPLEX, 1.5, (255, 255, 255), 3)

async def handler(websocket):
    print("\n>>> [360 KUGEL] Client verbunden. Streame gesamtes Panorama... <<<")
    try:
        while True:
            # Bei der Kugel müssen wir nicht mal zwingend auf Posen warten, 
            # aber wir können sie empfangen, um den Takt zu halten.
            try:
                await asyncio.wait_for(websocket.recv(), timeout=0.033) # ca. 30 FPS
            except asyncio.TimeoutError:
                pass

            # Das GESAMTE 360-Bild als JPEG komprimieren
            _, jpeg_bytes = cv2.imencode('.jpg', pano, [cv2.IMWRITE_JPEG_QUALITY, 80])

            # An Unity senden
            await websocket.send(jpeg_bytes.tobytes())

    except websockets.exceptions.ConnectionClosed:
        print("\n>>> Kugel-Client getrennt. <<<")

async def main():
    async with websockets.serve(handler, "127.0.0.1", 8766, ping_interval=None):
        print("360°-Vollbild Server läuft auf ws://127.0.0.1:8766 ...")
        await asyncio.Future()

if __name__ == "__main__":
    asyncio.run(main())