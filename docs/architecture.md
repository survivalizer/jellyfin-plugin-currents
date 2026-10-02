# Architecture

Currents puts AIOMetadata catalog titles into a real Jellyfin library and plays them via AIOStreams.
Design spec: `docs/superpowers/specs/2026-10-01-currents-design.md`.

## Flow (M1)
1. **Catalog sync** (`Library/CatalogSyncService`, scheduled every 6 h or via `POST /Currents/admin/sync`):
   AIOMetadata catalogs become `.strm` + `.nfo` files under `<LibraryRoot>/Movies` and `<LibraryRoot>/Shows`,
   tracked in `state.json`. `LibraryWriter` stamps every managed title folder with a `.currents` marker file.
   It writes into an existing unmarked folder only when that folder holds nothing but plugin-owned files
   (`*.strm`, `movie.nfo`, `tvshow.nfo`, `Season NN`/`Specials` folders with only `*.strm`), adopting it with a marker;
   otherwise it throws ("exists and is not managed by Currents"), even for the folder recorded in `state.json`.
   `Delete` (pruning) is a no-op for unmarked folders; in marked ones it removes plugin files first and the marker
   last, deleting marker and folder only when nothing else remains. Folders that still hold other files (posters,
   subtitles, season NFOs) keep the marker, so a returning title is written into them again.
   Items AIOMetadata uses to report errors (`aiom.error.*`) and items without a usable id do not count: a catalog
   that yields no usable titles while titles from it are in `state.json` is treated as an outage (failed), and a
   failed catalog's titles are never counted as missing. A malformed item fails only that title; a malformed page
   fails only that catalog. With no catalog enabled, pruning is skipped entirely.
2. **Metadata** (`Metadata/*Provider`): Jellyfin reads the NFO (ids incl. `Currents`), then the AIOMetadata
   providers fill details and images by the `Currents` id.
3. **Playback**: each `.strm` contains `{StrmBaseUrl}/Currents/play/{type}/{id}?sig=...`.
   `Web/PlayController` verifies the HMAC, `Streams/StreamResolver` searches AIOStreams with the default config,
   follows redirects, skips placeholders and dead links, and redirects Jellyfin to the stream.

## Admin API
`Web/AdminController` (admin only), used by the configuration page:
- `POST /Currents/admin/catalogs` with JSON body `{"ManifestUrl": "..."}` lists AIOMetadata catalogs.
- `POST /Currents/admin/test-streams` with JSON body `{"ManifestUrl": "..."}` tests an AIOStreams manifest.
- `GET /Currents/admin/paths` returns the Movies/Shows folders to add as libraries.
- `POST /Currents/admin/sync` starts a sync now.

Manifest URLs travel in request bodies, never query strings, because they contain credentials.

## Degraded mode and `StrmBaseUrl`
M1 has no media-source decorator, so Jellyfin exposes each `.strm` as a remote media source whose path is the
`.strm` URL. The M0 spike (S4, `docs/spikes/2026-10-m0-findings.md`) showed the stock web client direct-plays
that URL itself, and falls back to a full server transcode only if that fails. Consequences in M1:
- `StrmBaseUrl` (default `http://127.0.0.1:8096`) must be reachable by **both** clients and Jellyfin's own ffmpeg
  (which opens the `.strm` URL for every transcode or remux), normally the server's LAN address such as
  `http://192.168.x.y:8096`, plus Jellyfin's base URL path if one is configured (e.g.
  `http://192.168.x.y:8096/jellyfin`). `localhost` fails both ways: a containerised ffmpeg cannot reach the published host port
  through it, and a remote client resolves it to itself. (M1 end-to-end: real 4K/EAC3 streams were all served via
  server HLS; direct play of the `.strm` URL was only observed with browser-compatible placeholder/sample videos.)
- Clients may follow the redirect from `/Currents/play/...` to the final stream URL themselves, bypassing Jellyfin.
This is fixed in M2, where the `IMediaSourceManager` decorator forces streaming through Jellyfin
(`SupportsDirectPlay=false`).

## Module rules
- `Integration/` is the only folder that touches Jellyfin internals. Everything else is unit-tested with fakes.
- Outbound HTTP goes through named clients with rate limiting, retries and a circuit breaker (`Clients/Http`).
- Secrets never reach logs (`Common/SecretMasker`).

## Planned (later milestones)
M2 per-user versions (single `IMediaSourceManager` decorator, synthetic version ids resolving as items),
M3 search auto-add (MVC filters), M4 media info/subtitles/trailers, M5 segments/collections/maintenance,
M6 releases.
