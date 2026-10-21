import subprocess

video_path = "video.mp4" 
http_url = "http://0.0.0.0:5050/stream"

command = [
    'ffmpeg',
    '-stream_loop', '-1',
    '-re',
    '-i', video_path,
    '-vf', 'scale=1280:720',      # Skaliert das schwere 4K-Video auf flüssiges 720p herunter
    '-c:v', 'libx264',
    '-preset', 'ultrafast',
    '-tune', 'zerolatency',
    '-b:v', '2M',                 # Bitrate auf stabile 2 Mbps begrenzen
    '-f', 'mpegts',
    '-listen', '1',
    http_url
]

print(f"Starte HTTP-Streaming-Server...")
print(f"Unity-URL zum Testen: http://127.0.0.1:5050/stream")

try:
    subprocess.run(command)
except KeyboardInterrupt:
    print("\nStream gestoppt.")