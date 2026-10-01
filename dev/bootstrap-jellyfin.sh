#!/usr/bin/env bash
# Completes the Jellyfin setup wizard (if needed), creates an API key and stores it in dev/.env.
set -euo pipefail
cd "$(dirname "$0")"
BASE="${JELLYFIN_URL:-http://localhost:8097}"
ENV_FILE=".env"
AUTH='MediaBrowser Client="currents-dev", Device="cli", DeviceId="currents-dev-cli", Version="0.1.0"'
J='Content-Type: application/json'

until curl -fs "$BASE/System/Info/Public" >/dev/null; do sleep 2; done

completed=$(curl -s "$BASE/System/Info/Public" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("StartupWizardCompleted", False))')
if [ "$completed" != "True" ]; then
  echo "Running startup wizard"
  curl -fs -X POST -H "$J" -d '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}' "$BASE/Startup/Configuration" >/dev/null
  curl -fs "$BASE/Startup/User" >/dev/null
  curl -fs -X POST -H "$J" -d '{"Name":"dev","Password":"dev"}' "$BASE/Startup/User" >/dev/null
  curl -fs -X POST "$BASE/Startup/Complete" >/dev/null
fi

TOKEN=$(curl -fs -X POST -H "$J" -H "Authorization: $AUTH" \
  -d '{"Username":"dev","Pw":"dev"}' "$BASE/Users/AuthenticateByName" \
  | python3 -c 'import json,sys; print(json.load(sys.stdin)["AccessToken"])')

list_key() {
  curl -fs -H "Authorization: MediaBrowser Token=\"$TOKEN\"" "$BASE/Auth/Keys" | python3 -c '
import json,sys
items=[i for i in json.load(sys.stdin)["Items"] if i.get("AppName")=="currents-dev"]
print(items[-1]["AccessToken"] if items else "")'
}

KEY=$(list_key)
if [ -z "$KEY" ]; then
  curl -fs -X POST -H "Authorization: MediaBrowser Token=\"$TOKEN\"" "$BASE/Auth/Keys?app=currents-dev" >/dev/null
  KEY=$(list_key)
fi
[ -n "$KEY" ] || { echo "Failed to obtain API key" >&2; exit 1; }

touch "$ENV_FILE"
if grep -q '^JELLYFIN_API_KEY=' "$ENV_FILE"; then
  python3 - "$ENV_FILE" "$KEY" <<'PY'
import sys
p, k = sys.argv[1], sys.argv[2]
lines = open(p).read().splitlines()
lines = ["JELLYFIN_API_KEY=" + k if l.startswith("JELLYFIN_API_KEY=") else l for l in lines]
open(p, "w").write("\n".join(lines) + "\n")
PY
else
  printf 'JELLYFIN_API_KEY=%s\n' "$KEY" >> "$ENV_FILE"
fi
echo "JELLYFIN_API_KEY written to dev/.env (${KEY:0:4}...${KEY: -4})"
