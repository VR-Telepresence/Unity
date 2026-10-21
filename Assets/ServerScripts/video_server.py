import asyncio
import websockets
import cv2
import time

# Video öffnen (Stelle sicher, dass 'test_video.mp4' im selben Ordner liegt)
video_path = "video.mp4"
cap = cv2.VideoCapture(video_path)

if not cap.isOpened():
    print(f"Fehler: Konnte das Video '{video_path}' nicht öffnen!")
    exit()

FPS = 30
frame_interval = 1.0 / FPS

async def handler(websocket):
    print("\n>>> [VERBUNDEN] Unity-Client verbunden. Streame Video-Frames... <<<")
    try:
        while True:
            start_time = time.time()
            
            ret, frame = cap.read()
            if not ret:
                cap.set(cv2.CAP_PROP_POS_FRAMES, 0)
                ret, frame = cap.read()
                if not ret:
                    break

            # ---NEU: Auflösung für Unity halbieren (z.B. auf 1280x640) ---
            frame = cv2.resize(frame, (1280, 640))

            # JPEG-Qualität etwas senken (z.B. auf 60), damit die Pakete winzig sind
            _, jpeg_bytes = cv2.imencode('.jpg', frame, [cv2.IMWRITE_JPEG_QUALITY, 60])

            # An Unity senden
            await websocket.send(jpeg_bytes.tobytes())

            # Framerate einhalten (~30 FPS)
            elapsed = time.time() - start_time
            sleep_time = frame_interval - elapsed
            if sleep_time > 0:
                await asyncio.sleep(sleep_time)

    except websockets.exceptions.ConnectionClosed:
        print("\n>>> Unity-Client getrennt. <<<")

async def main():
    async with websockets.serve(handler, "127.0.0.1", 8766, ping_interval=None):
        print("Video-Stream Server läuft auf ws://127.0.0.1:8766 ...")
        await asyncio.Future()

if __name__ == "__main__":
    asyncio.run(main())