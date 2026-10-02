# Client matrix

Result of manual playback testing per milestone. Legend: works / works with caveats / broken / not tested (—).

| Client | Version | M1 degraded (.strm) | Notes |
|---|---|---|---|
| Jellyfin Web | 12.1 (Chromium) | caveats | M1 end-to-end (2026-10-01, real AIOStreams + AIOMetadata, TorBox): a 4K HEVC DV + TrueHD movie and episodes played 30 s+ and seeked once without errors. Time to first frame 1.8-7 s (the first resolve of one title took ~17 s when an upstream addon timed out; cached repeats 0.04 s). Every real debrid stream in this run was transcoded or stream-copied by the server (HLS); the browser never opened the `.strm` URL for them. Direct play of the `.strm` URL was observed only with a placeholder video (an ElfHosted "Still downloading" slate, h264/aac) and, in M0, with a sample file: with the default `StrmBaseUrl` (`http://127.0.0.1:8096`) that attempt failed (`DirectPlayError`; on the test machine another local service answered on 8096 with 404, which is machine-specific) and fell back to a server transcode; with `http://localhost:8097` the browser followed the 302 to the CDN itself. `StrmBaseUrl` must be reachable by **both** clients and the server's own ffmpeg: `http://localhost:8097` broke every server transcode in the dev stack (ffmpeg inside the container got `Connection refused`); the host's LAN address (`http://<lan-ip>:8097`) worked for both. |
| Android TV | | — | |
| Swiftfin | | — | |
| Findroid | | — | |
| Infuse | | — | |
| Streamyfin | | — | |
| External player (VLC/MX) | | — | |
