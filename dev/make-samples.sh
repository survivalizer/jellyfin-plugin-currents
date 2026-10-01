#!/usr/bin/env bash
# Generates two 60-second test videos with the ffmpeg bundled in the Jellyfin image.
set -euo pipefail
cd "$(dirname "$0")"
for size in 1280x720:720 1920x1080:1080; do
  res="${size%%:*}"; name="${size##*:}"
  docker compose exec -T jellyfin /usr/lib/jellyfin-ffmpeg/ffmpeg -y -loglevel error \
    -f lavfi -i "testsrc2=duration=60:size=${res}:rate=24" \
    -f lavfi -i "sine=frequency=440:duration=60" \
    -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest "/media-out/sample-${name}.mp4"
done
ls -lh data/media
