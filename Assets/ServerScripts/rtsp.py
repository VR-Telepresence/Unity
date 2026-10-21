import subprocess

video_path = "video.mp4" 

# Wir nutzen einen lokalen UDP/RTSP-kompatiblen Stream, den Unity direkt lesen kann
# Oder wir lassen FFmpeg in einen echten RTSP-Modus laufen:
command = [
    'ffmpeg',
    '-stream_loop', '-1',
    '-re',
    '-i', video_path,
    '-vf', 'scale=1280:720',
    '-c:v', 'libx264',
    '-preset', 'ultrafast',
    '-tune', 'zerolatency',
    '-b:v', '2M',
    '-f', 'rtsp',                      # Erzwingt das RTSP-Protokoll
    'rtsp://127.0.0.1:8554/live'       # RTSP-Zieladresse
]

print("Starte RTSP-Streamer...")
print("Unity-URL: rtsp://127.0.0.1:8554/live")

try:
    subprocess.run(command)
except KeyboardInterrupt:
    print("\nStream gestoppt.")