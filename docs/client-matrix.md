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

## M2: per-user versions

Versions on (`EnableVersions = true`), a user with their own AIOStreams config. "Versions shown": the Version
dropdown lists the user's streams. "Switch works": picking another version keeps it selected with no error.
"Plays": playback starts through Jellyfin (HLS). "Resume": stopping and reopening offers Resume at the right position.

| Client | Version | Versions shown | Switch works | Plays | Resume | Notes |
|---|---|---|---|---|---|---|
| Jellyfin Web | 12.1 (Chromium) | works | works | works | works | M2 end-to-end (2026-10-02, `docs/spikes/2026-10-m2-e2e.md`): 20 versions on a movie, 18 on an episode; switching to the second version kept the selection with no 404; 4K HEVC DV played as HLS (video stream-copied, audio transcoded), first frame about 9 s (movie, incl. a 3.3 s probe) and 6 s (episode); Resume offered at the stop position (Jellyfin only saves a position after 5 % of the runtime). Non-fatal hls.js buffer warnings in the console. |
| Android TV | | to test | to test | to test | to test | needs the maintainer's device |
| Swiftfin | | to test | to test | to test | to test | needs the maintainer's device |
| Findroid | | to test | to test | to test | to test | needs the maintainer's device |
| Infuse | | to test | to test | to test | to test | needs the maintainer's device |

## M3: search auto-add

"Search shows AIOMetadata titles / opening adds the title": a search for a title that is in no synced catalog
shows an AIOMetadata card with a poster, and opening it adds the title and shows the real item.

| Client | Version | Search shows AIOMetadata titles / opening adds the title | Notes |
|---|---|---|---|
| Jellyfin Web | 12.1 (Chromium) | to be filled by the M3 end-to-end check | `docs/spikes/2026-10-m3-e2e.md` |
| Android TV | | untested (M6) | |
| Swiftfin | | untested (M6) | |
| Findroid | | untested (M6) | |
| Infuse | | untested (M6) | |
| Streamyfin | | untested (M6) | |
| External player (VLC/MX) | | untested (M6) | |
