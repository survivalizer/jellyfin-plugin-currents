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
| Moonfin | | — | |
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
| Moonfin | | to test | to test | to test | to test | needs the maintainer's device |

## M3: search auto-add

"Search shows AIOMetadata titles / opening adds the title": a search for a title that is in no synced catalog
shows an AIOMetadata card with a poster, and opening it adds the title and shows the real item.

| Client | Version | Search shows AIOMetadata titles / opening adds the title | Notes |
|---|---|---|---|
| Jellyfin Web | 12.1 (Chromium) | works | M3 end-to-end (2026-10-02, `docs/spikes/2026-10-m3-e2e.md`): AIOMetadata cards with proxied posters in the Movies and Shows rows about 3.8 s after typing (cold); opening a movie card showed the real item in 5.0 s, a series in 34.7 s with all seasons and episodes; both played with the user's versions; reload, restart and catalog sync kept the same items. Series cards no longer appear a second time under "Videos" (fixed in 73aadfd, re-checked R-1 to R-3); a browser that searched a term before the fix may show the old row for that term until jellyfin-web refreshes its query cache (R-4). |
| Android TV | | untested (M6) | |
| Swiftfin | | untested (M6) | |
| Findroid | | untested (M6) | |
| Infuse | | untested (M6) | |
| Streamyfin | | untested (M6) | |
| Moonfin | | untested (M6) | |
| External player (VLC/MX) | | untested (M6) | |

## M4: media

"Tracks shown before playback": the details page lists the Audio and Subtitles tracks of a version before it is played.
"Stream subtitle (External) plays": a subtitle carried by a stream shows as an External track and renders.
"Subtitle search and download": the subtitle dialog finds AIOStreams subtitles and a downloaded one plays.
"Trailer button": a Currents title shows a Trailer button that plays.

| Client | Version | Tracks shown before playback | Stream subtitle (External) plays | Subtitle search and download | Trailer button | Notes |
|---|---|---|---|---|---|---|
| Jellyfin Web | 12.1 (Chromium) | pass | not exercised (dev config has no stream subtitles) | pass | pass | embedded text subtitles play as External after the Defect 1 fix (the first use of a version waits for Jellyfin to read the whole remote file); `docs/spikes/2026-10-m4-e2e.md` |
| Android TV | | untested (M6) | untested (M6) | untested (M6) | untested (M6) | |
| Swiftfin | | untested (M6) | untested (M6) | untested (M6) | untested (M6) | |
| Findroid | | untested (M6) | untested (M6) | untested (M6) | untested (M6) | |
| Infuse | | untested (M6) | untested (M6) | untested (M6) | untested (M6) | |
| Streamyfin | | untested (M6) | untested (M6) | untested (M6) | untested (M6) | |
| Moonfin | | untested (M6) | untested (M6) | untested (M6) | untested (M6) | |
| External player (VLC/MX) | | untested (M6) | untested (M6) | untested (M6) | untested (M6) | |

## M5: skip markers and collections

"Skip intro/credits prompt": the prompt appears on a version whose length fits the markers and not on one that does not.
"Catalog collections": a catalog ticked for a collection shows as a collection in the client.

| Client | Version | Skip intro/credits prompt | Catalog collections | Notes |
|---|---|---|---|---|
| Jellyfin Web | 12.1 (Chromium) | M6 | M6 | |
| Android TV | | M6 | M6 | |
| Swiftfin | | M6 | M6 | |
| Findroid | | M6 | M6 | |
| Infuse | | M6 | M6 | |
| Streamyfin | | M6 | M6 | |
| Moonfin | | M6 | M6 | |
| External player (VLC/MX) | | M6 | M6 | |

## M6: release pass

Tested against the 1.0 release candidate (branch `feat/m6`) on Jellyfin 12.1.

| Client | Version | Browse and versions | Play, seek, resume | Subtitles | Skip prompt | Collections | Search | Notes |
|---|---|---|---|---|---|---|---|---|
| Jellyfin Web | 12.1 (Chromium) | works | works | caveats | not tested | not tested | works | M6 e2e (`docs/spikes/2026-10-m6-e2e.md`, Step 7): 20 versions listed, switching plays the picked version and is remembered; first frame 8-20 s, seek and Resume at the right point. Subtitles: the downloaded subtitle shows (cue rendered) but, when preselected, only after switching it off and on; after a probe, an over-limit version's player menu still lists built-in SUBRIP tracks that answer 404 (Defect 2). Skip prompt: no dev title had markers fitting a version and TheIntroDB answered 429 (M5 verified the prompt). Collections: no catalog had a collection on the dev server (M5 verified them). Search: posters, open adds the title, plays |
| Moonfin (macOS) | | to test | to test | to test | to test | to test | to test | maintainer, checklist below |
| Android TV | | optional | optional | optional | optional | optional | optional | when the device is at hand |
| Moonfin (TV) | | optional | optional | optional | optional | optional | optional | when the device is at hand |
| Swiftfin, Findroid, Infuse, Streamyfin, external players | | not tested in M6 | | | | | | reports welcome (issue template) |

### Moonfin (macOS) checklist

Sign in as a user with versions on. For each line write works / caveats / broken and a note.

1. Browse the Currents Movies and Shows libraries: posters and titles show.
2. Open a movie: is there a version picker? Pick the second version: it stays selected.
3. Play: note the time to the first frame. Seek forward once. Stop, reopen: Resume is offered at the right point.
4. Audio: switch to another audio track while playing.
5. Subtitles: turn on a subtitle from AIOStreams or one downloaded in Jellyfin. If a small file (under 15 GB) has a built-in subtitle, try it too.
6. Open a series, play an episode, let it reach the end or skip to the next episode.
7. Skip intro: on an episode with an intro marker, does a skip prompt appear?
8. Collections: is a Currents collection listed?
9. Search for a title that is not in the library: does it appear, and does opening it add and play it?
