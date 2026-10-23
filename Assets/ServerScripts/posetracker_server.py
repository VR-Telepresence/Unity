import asyncio
import json
import websockets

HOST = "0.0.0.0"
PORT = 8765

async def handle_pose(websocket):
    print(f"[PoseServer] Quest 3 verbunden von {websocket.remote_address}")
    try:
        async for message in websocket:
            data = json.loads(message)
            x, y, z = data.get("x", 0.0), data.get("y", 0.0), data.get("z", 0.0)
            yaw, pitch = data.get("yaw", 0.0), data.get("pitch", 0.0)
            t = data.get("t", 0.0)

            # Pose Ausgabe im Terminal
            print(f"\r[Pose] Pos: ({x:+.2f}, {y:+.2f}, {z:+.2f})m | Yaw: {yaw:6.1f}° | Pitch: {pitch:6.1f}° | T: {t:.0f}ms", end="")
    except websockets.exceptions.ConnectionClosed:
        print("\n[PoseServer] Quest 3 getrennt.")
    except Exception as e:
        print(f"\n[PoseServer] Fehler: {e}")

async def main():
    print(f"[PoseServer] Starte WebSocket-Server auf ws://{HOST}:{PORT}...")
    async with websockets.serve(handle_pose, HOST, PORT):
        await asyncio.Future()  # Läuft dauerhaft

if __name__ == "__main__":
    # Falls websockets noch nicht installiert ist: pip install websockets
    asyncio.run(main())