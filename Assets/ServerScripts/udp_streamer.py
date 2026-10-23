import subprocess

quest_ip = "192.168.178.93"
port = 5000
video_path = "video.mp4"  # Stelle sicher, dass die Datei im selben Ordner liegt

command = [
    'ffmpeg',
    '-stream_loop', '-1',
    '-re',
    '-i', video_path,
    '-vf', 'scale=1280:720,format=yuv420p',
    '-c:v', 'libx264',
    '-preset', 'ultrafast',
    '-tune', 'zerolatency',
    '-profile:v', 'baseline',
    '-level', '4.0',
    '-g', '15',
    '-keyint_min', '15',
    '-x264-params', 'repeat-headers=1',
    '-an',
    '-f', 'rtp',
    '-payload_type', '96',
    f'rtp://{quest_ip}:{port}'
]

print(f"Starte Stream an {quest_ip}:{port}...")
subprocess.run(command)