#!/usr/bin/env bash
# Builds the plugin and installs it into the dev Jellyfin, then restarts Jellyfin.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEST="$ROOT/dev/data/config/plugins/Currents_0.1.0.0"

dotnet publish "$ROOT/src/Jellyfin.Plugin.Currents/Jellyfin.Plugin.Currents.csproj" \
  -c Debug -o "$ROOT/artifacts/publish"
mkdir -p "$DEST"
cp "$ROOT/artifacts/publish/Jellyfin.Plugin.Currents.dll" "$DEST/"
docker compose -f "$ROOT/dev/docker-compose.yml" restart jellyfin
echo "Deployed to $DEST"
