# Architecture

Currents puts AIOMetadata catalog titles into a real Jellyfin library and plays them via AIOStreams.
Design spec: `docs/superpowers/specs/2026-10-01-currents-design.md`.

## Catalog, metadata and degraded playback (M1)
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
   `Metadata/AioRuntimeProvider` (a custom provider) sets movie/episode runtimes, because Jellyfin's metadata merge
   drops `RunTimeTicks` for videos.
3. **Degraded playback**: each `.strm` contains `{StrmBaseUrl}/Currents/play/{type}/{id}?sig=...`.
   `Web/PlayController` verifies the HMAC, `Streams/StreamResolver` searches AIOStreams with the default config,
   follows redirects, skips placeholders and dead links, and redirects Jellyfin to the stream. With versions on
   (M2, below) Jellyfin never plays this URL; it remains the fallback when `EnableVersions` is off.

## Per-user versions (M2)

### Modules
- `Users/`: per-user records in `users.json` (plugin data folder, mode `0600` on Unix; `UserStore`), each with an
  admin layer and a self-service layer (`UserLayer`: AIOStreams credentials, preferences, auto-select) plus admin
  flags (lock self-service, streams disabled, search auto-add disabled). `StreamProfileResolver` applies the
  precedence field by field: self-service (when `AllowSelfService` is on and the user is not locked) > admin override >
  global default > none. `IUserDirectory` lists Jellyfin users without touching `IUserManager` outside `Integration/`.
- `Streams/` version pipeline:
  - `StreamService` caches AIOStreams searches per (config fingerprint, title) for `StreamCacheMinutes`, coalesces
    concurrent searches, and caches failures (and "no results because addons failed") for only 30 s. It ranks per
    caller with `StreamRanker` (filters, then a stable re-rank by preferences).
  - `StreamIdentity`/`StreamLabel` give each stream a stable key and a name like `2160p DV · Atmos · 18.4 GB · cached`.
  - `VersionCatalog` turns a user's ranked streams into `VersionEntry`s whose ids are GUIDs derived from
    (item, user, stream key), and registers them in `VersionRegistry`, the process-wide id map that Jellyfin's
    user-less lookups (streaming, sessions, subtitles) rely on. Replaced lists keep old ids resolvable until expiry.
  - `VersionSourceBuilder` builds each `MediaSourceInfo` (always new objects): `SupportsDirectPlay = false`,
    tracks pre-filled from AIOStreams' `parsedFile` (`PrefilledMedia`, `MediaStreamMapper`; `Index = -1`, estimated
    video bitrate, explicit HDR range) or from `ProbeCache`, and `Path` = `currents://version/{id}` for clients or
    `{internal base}/Currents/play/s/{token}` for Jellyfin itself.
  - `VersionTokenSigner` signs `VersionTicket`s (title, stream, user's config) with the install `SigningSecret`,
    domain-separated from `.strm` signatures, expiring after `VersionTokenHours` (default 24).
  - `StreamResolver` resolves a ticket: the chosen stream first, then failover through the user's next-ranked
    streams; single-flight, 45 s deadline (30 s of it for the search), resolved URLs cached for 5 min.
- `Integration/` (the only folder touching Jellyfin internals):
  - `CurrentsMediaSourceManager`: the `IMediaSourceManager` decorator (registered last via
    `ServiceCollectionDecoratorExtensions.Decorate`). For Currents items it replaces the `.strm` source with the
    user's versions; everything else goes to Jellyfin's own manager. A signed-in user only ever gets their own
    versions; user-less callers may look a version up by id.
  - `SyntheticVersionIdFilter` (MVC filter, order -1000): rewrites version ids in item arguments (`GET /Items/{id}`
    and a few safe writes) to the base item, because clients treat every MediaSource id as an item id (M0 S1b).
  - `PlaybackInfoFilter` (order -999) + `VersionProber`: map PlaybackInfo's `MediaSourceId` to the user's version,
    probe it once if its parsed info is not enough (20 s timeout, failures remembered 10 min, results 7 days, in
    memory), and save the base item's runtime when it is missing.
  - `CurrentsItemLocator`: recognises Currents items (a `.strm` under the plugin root holding a validly signed
    Currents URL).
  - `RequestContext`: the requesting user (claims; `?userId=` only for API keys) and whether the request is a
    single-item one (`UserLibrary.GetItem`/`GetItemLegacy`, `MediaInfo.GetPostedPlaybackInfo`/`GetPlaybackInfo`),
    the only requests allowed a cold AIOStreams search (10 s wait). List views get cached versions or one pending
    source.
  - `InternalBaseUrl`: the loopback URL ffmpeg/ffprobe use (`http://127.0.0.1:{port}{basePath}`, or `[::1]`, or
    HTTPS when Jellyfin requires it), derived from Jellyfin's bind addresses, never from `StrmBaseUrl`.
  - `ServerAddresses` + `Common/LocalCallerPolicy`: `/Currents/play/s/{token}` answers only loopback or the
    server's own interface addresses.
- `Web/`: `PlayController` (`play/{type}/{id}` for `.strm`, `play/s/{token}` for versions), `UserSettingsController`
  (`/Currents/user` page and `settings`), `AdminUsersController` (`/Currents/admin/users`), `ManifestValidator`.

### Request walkthrough (web client, one user)
1. **Item page**: `GET /Users/{userId}/Items/{id}` (or `/Items/{id}?userId=`). The DTO builder calls
   `GetStaticMediaSources`; the decorator sees a single-item request, searches AIOStreams with the user's profile
   (waiting up to 10 s; a slow search returns a "retry" notice and still fills the cache), ranks, registers the
   versions and returns them with redacted paths. The Version dropdown lists them.
2. **Version switch**: the web client calls `GET /Items/{versionId}`. `SyntheticVersionIdFilter` rewrites the id to
   the base item, so the response is the base item (200).
3. **PlaybackInfo**: `POST /Items/{id}/PlaybackInfo` with `MediaSourceId`. `PlaybackInfoFilter` maps a stale or
   item-level id to the user's version (same stream if still offered, else the best), and `VersionProber` probes it
   when needed (log: "Probed a version of … in N s") and saves the runtime. Jellyfin then builds its answer from the
   decorator's sources: no direct play, so the client gets an HLS (remux/transcode) URL.
4. **HLS**: the client requests the playlist and segments with its own token. Jellyfin's streaming code looks the
   version up by id (`GetMediaSource`); the decorator finds it in the registry, checks it belongs to the token's user,
   and returns it with its real path.
5. **ffmpeg** opens `http://127.0.0.1:8096/Currents/play/s/{token}` (logged as `/Currents/play/s/***`).
   `PlayController.PlayVersion` accepts only loopback/server addresses and a valid, unexpired token.
6. `StreamResolver` resolves the ticket (chosen stream, then failover) and the endpoint answers **302** to the debrid
   URL, which ffmpeg follows. Watch state and resume are recorded on the base item.

### Self-service and admin APIs
- `GET /Currents/user` serves the page anonymously (a browser navigation has no Jellyfin auth header); the page's
  calls `GET`/`PUT /Currents/user/settings` are `[Authorize]` and scoped to the caller's own record.
- `GET /Currents/admin/users`, `PUT /Currents/admin/users/{userId}` (admin only): per-user overrides and flags.
- Credentials are write-only: responses carry the AIOStreams **host** of a saved config, never the manifest URL or
  password.
- Saving a manifest URL (self-service or admin) validates it with `ManifestValidator`, which makes the **server**
  fetch the URL entered. Any signed-in user can therefore make the server send a request to an http(s) host of their
  choice and see whether it failed. Admins who do not want that can turn `AllowSelfService` off or lock individual users.

### Known limitation: same-host reverse proxy without `KnownProxies`
The version endpoint trusts `HttpContext.Connection.RemoteIpAddress`. If a reverse proxy on the same host (or in the
same container network namespace) forwards to Jellyfin and Jellyfin's **Known proxies** setting does not list it,
Jellyfin does not apply `X-Forwarded-For`, so every request, including ones from the internet, arrives from a
loopback/local address and looks like the server itself. The local-only check is then void and
`/Currents/play/s/{token}` is protected only by its signed token (24 h by default, `VersionTokenHours`). Tokens never
reach clients (paths are redacted), so this matters only if one leaks (for example from a log the masker missed).
Fix: add the proxy's address to Dashboard -> Networking -> Known proxies.

Related: Jellyfin's own `TranscodeManager` logs the full ffmpeg command line, so version tokens (like `.strm`
signatures in degraded mode) appear unmasked in the Jellyfin log; `SecretMasker` covers only Currents' own log lines.
The log is admin-only, and a token works only from the server itself and for `VersionTokenHours`, but with the proxy
misconfiguration above a leaked log line is replayable from outside until it expires.

## Admin API
`Web/AdminController` (admin only), used by the configuration page:
- `POST /Currents/admin/catalogs` with JSON body `{"ManifestUrl": "..."}` lists AIOMetadata catalogs.
- `POST /Currents/admin/test-streams` with JSON body `{"ManifestUrl": "..."}` tests an AIOStreams manifest.
- `GET /Currents/admin/paths` returns the Movies/Shows folders to add as libraries.
- `POST /Currents/admin/sync` starts a sync now.

Manifest URLs travel in request bodies, never query strings, because they contain credentials.

## Degraded mode and `StrmBaseUrl`
This section applies only when versions are off (`EnableVersions = false`); versions never use `StrmBaseUrl`.
Without the media-source decorator so Jellyfin exposes each `.strm` as a remote media source whose path is the
`.strm` URL. The M0 spike (S4, `docs/spikes/2026-10-m0-findings.md`) showed the stock web client direct-plays
that URL itself, and falls back to a full server transcode only if that fails. Consequences:
- `StrmBaseUrl` (default `http://127.0.0.1:8096`) must be reachable by **both** clients and Jellyfin's own ffmpeg
  (which opens the `.strm` URL for every transcode or remux), normally the server's LAN address such as
  `http://192.168.x.y:8096`, plus Jellyfin's base URL path if one is configured (e.g.
  `http://192.168.x.y:8096/jellyfin`). `localhost` fails both ways: a containerised ffmpeg cannot reach the published host port
  through it, and a remote client resolves it to itself. (M1 end-to-end: real 4K/EAC3 streams were all served via
  server HLS; direct play of the `.strm` URL was only observed with browser-compatible placeholder/sample videos.)
- Clients may follow the redirect from `/Currents/play/...` to the final stream URL themselves, bypassing Jellyfin.
With versions on, the `IMediaSourceManager` decorator forces streaming through Jellyfin (`SupportsDirectPlay=false`)
and ffmpeg uses the internal loopback URL instead.

## Module rules
- `Integration/` is the only folder that touches Jellyfin internals. Everything else is unit-tested with fakes.
- Outbound HTTP goes through named clients with rate limiting, retries and a circuit breaker (`Clients/Http`).
- Secrets never reach logs (`Common/SecretMasker`).

## Planned (later milestones)
M3 search auto-add (MVC filters), M4 media info (RemuxDB, persisted probes), proxying header-bound streams,
subtitles and trailers, M5 segments/collections/maintenance/compat guard/diagnostics, M6 releases.
