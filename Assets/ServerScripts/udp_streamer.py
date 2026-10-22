import subprocess

video_path = "video.mp4"
quest_ip = "192.168.178.92"  # Hier die lokale WLAN-IP der Quest 3 eintragen
port = 5000

# Erzeugt den von Quest-GStreamer geforderten RTP/H.264-Stream (pt=96)
command = [
    'ffmpeg',
    '-stream_loop', '-1',
    '-re',
    '-i', video_path,
    '-vf', 'scale=1280:720',
    '-c:v', 'libx264',
    '-preset', 'ultrafast',
    '-tune', 'zerolatency',
    '-profile:v', 'high',
    '-x264-params', 'repeat-headers=1',  # Entspricht GStreamer config-interval=-1
    '-b:v', '4M',
    '-f', 'rtp',
    '-payload_type', '96',               # Entspricht rtph264pay pt=96
    f'rtp://{quest_ip}:{port}'
]

print(f"Sende H.264 RTP/UDP-Stream an {quest_ip}:{port}...")

try:
    subprocess.run(command)
except KeyboardInterrupt:
    print("\nStream beendet.")