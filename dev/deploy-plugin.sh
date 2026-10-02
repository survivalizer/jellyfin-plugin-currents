#!/usr/bin/env bash
# Builds the plugin and installs it into the dev Jellyfin.
# Jellyfin is stopped before the copy: overwriting a loaded DLL crashes it on shutdown (BadImageFormatException).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="$(sed -n 's:.*<AssemblyVersion>\(.*\)</AssemblyVersion>.*:\1:p' "$ROOT/Directory.Build.props" | head -n 1)"
DEST="$ROOT/dev/data/config/plugins/Currents_${VERSION}"
COMPOSE=(docker compose -f "$ROOT/dev/docker-compose.yml")

dotnet publish "$ROOT/src/Jellyfin.Plugin.Currents/Jellyfin.Plugin.Currents.csproj" \
  -c Debug -o "$ROOT/artifacts/publish"
"${COMPOSE[@]}" stop jellyfin
mkdir -p "$DEST"
cp "$ROOT/artifacts/publish/Jellyfin.Plugin.Currents.dll" "$DEST/"
"${COMPOSE[@]}" start jellyfin
echo "Deployed to $DEST"
