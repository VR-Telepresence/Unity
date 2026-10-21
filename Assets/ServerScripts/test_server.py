import asyncio
import cv2
import json
import numpy as np
import websockets

async def handler(websocket):
    print("\n>>> [VERBUNDEN] Unity-Frontend hat angedockt. <<<")
    try:
        while True:
            # 1. Neueste Nachricht abholen (wartet auf Input)
            message = await websocket.recv()

            # Trick: Liegen bereits neuere Nachrichten im Puffer? 
            # Wenn ja, hole sofort die neueste und verwirf die alten.
            while True:
                try:
                    # Nicht-blockierender Check auf Folgemeldungen
                    message = await asyncio.wait_for(websocket.recv(), timeout=0.001)
                except asyncio.TimeoutError:
                    break

            if isinstance(message, str):
                data = json.loads(message)
                x, y, z = data.get("x", 0.0), data.get("y", 0.0), data.get("z", 0.0)
                yaw, pitch = data.get("yaw", 0.0), data.get("pitch", 0.0)
                mode = data.get("mode", "pvsnet")

                # Testbild zeichnen
                bg_color = (35, 35, 35) if mode == "pvsnet" else (100, 45, 20)
                img = np.full((512, 512, 3), bg_color, dtype=np.uint8)

                center_x = int(256 + (yaw % 360) / 360.0 * 200 - 100)
                center_y = int(256 + np.clip(pitch, -45, 45) * 3)
                cv2.circle(img, (center_x, center_y), 35, (0, 255, 0), -1)

                cv2.putText(img, f"MODE: {mode.upper()}", (30, 60), 
                            cv2.FONT_HERSHEY_SIMPLEX, 0.9, (0, 255, 255), 2)
                cv2.putText(img, f"Pos: [{x:.2f}, {y:.2f}, {z:.2f}]", (30, 100), 
                            cv2.FONT_HERSHEY_SIMPLEX, 0.6, (200, 200, 200), 1)

                _, jpeg_bytes = cv2.imencode('.jpg', img, [cv2.IMWRITE_JPEG_QUALITY, 75])
                await websocket.send(jpeg_bytes.tobytes())

    except websockets.exceptions.ConnectionClosed:
        print("\n>>> Unity getrennt. <<<")

async def main():
    # TCP_NODELAY aktivieren für sofortige Paketübertragung
    async with websockets.serve(handler, "127.0.0.1", 8765, ping_interval=None):
        print("Optimierter Test-Server läuft auf ws://127.0.0.1:8765 ...")
        await asyncio.Future()

if __name__ == "__main__":
    asyncio.run(main())