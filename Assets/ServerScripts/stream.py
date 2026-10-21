import asyncio
import websockets
import cv2
import av
import json

# Pfad zu deinem Testvideo
video_path = "video.mp4" 
cap = cv2.VideoCapture(video_path)

if not cap.isOpened():
    print(f"Fehler: Konnte Video '{video_path}' nicht öffnen!")
    exit()

WIDTH = 256
HEIGHT = 256

# H.264 Encoder via PyAV initialisieren (sendet per UDP an Unity)
output_url = "udp://127.0.0.1:5000?pkt_size=1316"
container = av.open(output_url, mode="w", format="mpegts")
stream = container.add_stream("libx264", rate=30)
stream.width = WIDTH
stream.height = HEIGHT
stream.pix_fmt = "yuv420p"
stream.options = {"preset": "ultrafast", "tune": "zerolatency"}

def encode_and_send_frame(frame_bgr):
    # Auf Zielgröße skalieren
    frame_resized = cv2.resize(frame_bgr, (WIDTH, HEIGHT))
    frame_rgb = cv2.cvtColor(frame_resized, cv2.COLOR_BGR2RGB)
    
    av_frame = av.VideoFrame.from_ndarray(frame_rgb, format="rgb24")
    for packet in stream.encode(av_frame):
        container.mux(packet)

async def handler(websocket):
    print("\n>>> [VERBUNDEN] Unity-Client verbunden. <<<")
    
    # Optionaler WebSocket-Empfänger für Posen (läuft einfach mit, stört das Video nicht)
    async def receiver():
        try:
            async for message in websocket:
                pass # Hier können später die Posen rein
        except Exception:
            pass

    recv_task = asyncio.create_task(receiver())

    try:
        while cap.isOpened():
            ret, frame = cap.read()
            if not ret:
                # Video am Ende: Wieder von vorne starten (Loop)
                cap.set(cv2.CAP_PROP_POS_FRAMES, 0)
                continue

            # Frame über H.264 / UDP streamen
            encode_and_send_frame(frame)
            
            await asyncio.sleep(0.033) # ~30 FPS Taktung
            
    except websockets.exceptions.ConnectionClosed:
        print("\n>>> Getrennt. <<<")
    finally:
        recv_task.cancel()

async def main():
    async with websockets.serve(handler, "127.0.0.1", 8765, ping_interval=None):
        print(f"Server läuft. Streame Video '{video_path}' an udp://127.0.0.1:5000 ...")
        await asyncio.Future()

if __name__ == "__main__":
    try:
        asyncio.run(main())
    finally:
        cap.release()
        container.close()