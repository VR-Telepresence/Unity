import asyncio
import websockets
import cv2
import numpy as np
import json

pano_path = "pano.jpg"
pano_image = cv2.imread(pano_path)

if pano_image is None:
    print(f"Fehler: Konnte '{pano_path}' nicht laden!")
    exit()

pano_height, pano_width = pano_image.shape[:2]
WIDTH = 1024
HEIGHT = 1024

current_yaw = 0.0
current_pitch = 0.0

def extract_viewport(yaw, pitch):
    x_center = int((yaw + 180.0) / 360.0 * pano_width)
    y_center = int((pitch + 90.0) / 90.0 * pano_height) # Falls pitch klemmt, neutral halten
    
    half_w = WIDTH // 2
    half_h = HEIGHT // 2
    
    x1 = np.clip(x_center - half_w, 0, pano_width - WIDTH)
    y1 = np.clip(y_center - half_h, 0, pano_height - HEIGHT)
    
    viewport = pano_image[y1:y1+HEIGHT, x1:x1+WIDTH]
    if viewport.shape[0] != HEIGHT or viewport.shape[1] != WIDTH:
        viewport = cv2.resize(viewport, (WIDTH, HEIGHT))
    return viewport

async def handler(websocket):
    global current_yaw, current_pitch
    print("\n>>> [VERBUNDEN] Unity-Client verbunden. <<<")
    
    async def receiver():
        global current_yaw, current_pitch
        try:
            async for message in websocket:
                data = json.loads(message)
                if "yaw" in data:
                    current_yaw = float(data["yaw"])
                if "pitch" in data:
                    current_pitch = float(data["pitch"])
        except Exception:
            pass

    # Starte den Empfänger im Hintergrund, damit der Haupt-Stream nicht blockiert
    recv_task = asyncio.create_task(receiver())

    try:
        while True:
            viewport = extract_viewport(current_yaw, current_pitch)
            success, encoded_image = cv2.imencode('.jpg', viewport, [int(cv2.IMWRITE_JPEG_QUALITY), 80])
            if success:
                await websocket.send(encoded_image.tobytes())
            await asyncio.sleep(0.033)
    except websockets.exceptions.ConnectionClosed:
        print("\n>>> Getrennt. <<<")
    finally:
        recv_task.cancel()

async def main():
    async with websockets.serve(handler, "127.0.0.1", 8765, ping_interval=None):
        print("Server läuft auf ws://127.0.0.1:8765 ...")
        await asyncio.Future()

if __name__ == "__main__":
    asyncio.run(main())