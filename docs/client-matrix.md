# Client matrix

Result of manual playback testing per milestone. Legend: works / works with caveats / broken / not tested (—).

| Client | Version | M1 degraded (.strm) | Notes |
|---|---|---|---|
| Jellyfin Web | 12.1 (Chromium) | caveats | M1 end-to-end (2026-10-01, real AIOStreams + AIOMetadata, TorBox): movie (4K HEVC DV + TrueHD) and episodes played 30 s+ and seeked once without errors. Time to first frame 3-7 s (first resolve of a title up to ~17 s when an upstream addon times out; cached repeats 0.04 s). Play method follows the probed source: 4K DV/TrueHD and EAC3 sources go straight to server HLS (video copy + AAC, or full transcode), so the browser never opens the `.strm` URL; a browser-compatible source is direct-played, i.e. the browser opens the `.strm` URL itself and follows the 302 to the debrid CDN. With the default `StrmBaseUrl` (`http://127.0.0.1:8096`) that direct play fails (`DirectPlayError`) and falls back to a server transcode. `StrmBaseUrl` must be reachable by **both** clients and the server's own ffmpeg: `http://localhost:8097` fixed direct play but broke every transcode in the dev stack (ffmpeg inside the container got `Connection refused`); the host's LAN address (`http://<lan-ip>:8097`) worked for both. |
| Android TV | | — | |
| Swiftfin | | — | |
| Findroid | | — | |
| Infuse | | — | |
| Streamyfin | | — | |
| External player (VLC/MX) | | — | |
