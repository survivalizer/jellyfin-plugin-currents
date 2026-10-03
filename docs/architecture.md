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
    (item, user, stream key) with HMAC-SHA256 under the install `SigningSecret`, so nobody can compute another user's
    version id from public data, and registers them in `VersionRegistry`, the process-wide id map that Jellyfin's
    user-less lookups (streaming, sessions, subtitles) rely on. Replaced lists keep old ids resolvable until expiry.
  - `VersionSourceBuilder` builds each `MediaSourceInfo` (always new objects): `SupportsDirectPlay = false`,
    tracks pre-filled from AIOStreams' `parsedFile` (`PrefilledMedia`, `MediaStreamMapper`; `Index = -1`, estimated
    video bitrate, explicit HDR range) or from `ProbeCache`, and `Path` = `currents://version/{id}` for clients or
    `{internal base}/Currents/play/s/{token}` for Jellyfin itself.
  - `VersionTokenSigner` signs `VersionTicket`s (user id, type, Stremio id, stream key; no config: the resolver reads
    the user's current profile) with the install `SigningSecret`,
    domain-separated from `.strm` signatures, expiring after `VersionTokenHours` (default 24).
  - `StreamResolver` resolves a ticket: the chosen stream first, then failover through the user's next-ranked
    streams; single-flight, 45 s deadline (30 s of it for the search), resolved URLs cached for 5 min.
- `Integration/` (the only folder touching Jellyfin internals):
  - `CurrentsMediaSourceManager`: the `IMediaSourceManager` decorator (registered last via
    `ServiceCollectionDecoratorExtensions.Decorate`). For Currents items it replaces the `.strm` source with the
    user's versions; everything else goes to Jellyfin's own manager. A signed-in user only ever gets their own
    versions. An anonymous HTTP request (no user, no API key) gets only default-config versions (user id empty) or a
    pending source, never another user's. Only background work with no HTTP request at all (sessions, timers) may look
    any version up by id or fall back to an item's latest list. When a known user's fresh search comes back empty
    (for example a playback resumed after the stream cache expired), the user's still-registered versions are served.
    A version that fails to build (bad parse data or probe result) is skipped and logged; if none can be built the
    title shows "No playable streams for this title."
  - `SyntheticVersionIdFilter` (MVC filter, order -1000): rewrites version ids in item arguments (`GET /Items/{id}`
    and a few safe writes) to the base item, because clients treat every MediaSource id as an item id (M0 S1b).
  - `PlaybackInfoFilter` (order -999) + `VersionProber`: map PlaybackInfo's `MediaSourceId` to the user's version,
    probe it once if its parsed info is not enough (20 s timeout, failures remembered 10 min, results 7 days, in
    memory), and save the base item's runtime when it is missing.
  - `CurrentsItemLocator`: recognises Currents items (a `.strm` under the plugin root holding a validly signed
    Currents URL).
  - `RequestContext`: the requesting user (claims; `?userId=` only for API keys), whether the request is anonymous
    (an HTTP request with no user and no API key, as opposed to no request at all), and whether the request is a
    single-item one (`UserLibrary.GetItem`/`GetItemLegacy`, `MediaInfo.GetPostedPlaybackInfo`/`GetPlaybackInfo`),
    the only requests allowed a cold AIOStreams search (10 s wait). List views get cached versions or one pending
    source.
  - `InternalBaseUrl`: the loopback URL ffmpeg/ffprobe use (`http://127.0.0.1:{port}{basePath}`, or `[::1]`, or
    HTTPS when Jellyfin requires it), derived from Jellyfin's bind addresses, never from `StrmBaseUrl`.
  - `ServerAddresses` + `Common/LocalCallerPolicy`: `/Currents/play/s/{token}` answers only loopback or the
    server's own interface addresses, and refuses (403) any request carrying `X-Forwarded-For`, `X-Original-For`,
    `Forwarded` or `X-Real-IP`: ffmpeg never sends them, a reverse proxy does.
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
  choice and see whether it failed. Self-service answers a refused config only with a generic message ("AIOStreams
  did not accept this config. Check the URL and try again."; parse errors keep their fixed explanation) and logs the
  masked detail as a warning, so the response does not echo what the remote host said; the admin API keeps the
  detailed, masked message. `AioStreamsClient` reads at most 16 MB of any response. Admins who do not want users to
  trigger these requests can turn `AllowSelfService` off or lock individual users.

### Known limitation: same-host reverse proxy without `KnownProxies`
The version endpoint trusts `HttpContext.Connection.RemoteIpAddress`. If a reverse proxy on the same host (or in the
same container network namespace) forwards to Jellyfin and Jellyfin's **Known proxies** setting does not list it,
Jellyfin does not apply `X-Forwarded-For`, so every request, including ones from the internet, arrives from a
loopback/local address and looks like the server itself. The local-only check is then void and
`/Currents/play/s/{token}` is protected only by its signed token (24 h by default, `VersionTokenHours`). Tokens never
reach clients (paths are redacted), so this matters only if one leaks (for example from a log the masker missed).
Fix: add the proxy's address to Dashboard -> Networking -> Known proxies.

Mitigation since the M2 final review: the version endpoint refuses requests carrying forwarded-for headers
(`X-Forwarded-For`, `X-Original-For`, `Forwarded`, `X-Real-IP`), which a typical reverse proxy adds, so a proxied
request is refused even when the proxy is not in Known proxies. A proxy that strips or never sets these headers is
still indistinguishable from the server itself.

Related: Jellyfin's own `TranscodeManager` logs the full ffmpeg command line, and Jellyfin writes the same command
line into the per-transcode `FFmpeg.*.log` files in its log folder, so version tokens (like `.strm` signatures in
degraded mode) appear unmasked in the Jellyfin logs; `SecretMasker` covers only Currents' own log lines.
The log is admin-only, and a token works only from the server itself and for `VersionTokenHours`, but with the proxy
misconfiguration above a leaked log line is replayable from outside until it expires.

## Search auto-add (M3)

### Modules
- `Library/TitleLibrary`: the one locked state (`state.json` in memory). Catalog sync and search-add both go through
  it, so neither loses the other's entry. `AddFromSearch` writes a title through the M1 `LibraryWriter` and marks it
  `AddedBySearch`. State is kept in memory between runs; a cancelled or failed sync's unsaved changes are saved by the
  next writer. Search ids (`Library/SearchItemId`) are the first 16 bytes of HMAC-SHA256 over
  `currents/search/` + the state id (`movie/tt123`), keyed with the install `SigningSecret`, so they are stable and
  recomputable after a restart but cannot be computed from public ids by anyone without the secret. `Library/ILibraryItems` is what `Search/` needs from Jellyfin (`FindTitle`,
  `FindExisting`, `CanAdd`, `AddAsync`, `KindsIn`, `PauseMonitoring`), implemented in `Integration/`.
- `Search/`:
  - `RemoteSearch` queries the enabled AIOMetadata search catalogs (`search.movie`, `search.series`, anime variants)
    and drops error items and unusable ids; one failing catalog does not hide the others, and failures are cached briefly.
  - `SearchResultRegistry` remembers current results by search id. `SearchDtoFactory` builds the card DTOs (`Virtual`
    location, a `Primary` image tag, the server's `SystemId`).
  - `SearchTitleOpener` opens a result, single-flight per title (the details page and the theme-media player ask at
    the same time): an existing Currents title or any library item the user can see with the same IMDb/TMDB/TVDB id
    wins; otherwise it fetches the full meta, writes the files, and creates the Jellyfin item. `OpenOutcome` says which.
- `Integration/`:
  - `SearchResultsFilter` (MVC order -998) wraps `GET /Items` with `searchTerm`. It starts the remote search before
    the action runs, waits at most 3 s after it, and appends cards the local results do not contain. It returns the
    local results at once when the local page already fills `limit`, and on any remote failure.
  - `SearchItemFilter` (order -1001, ahead of `SyntheticVersionIdFilter`) sees a search id in an `itemId` argument,
    opens the title and rewrites the argument to the real id. Image requests for a search id get the proxied poster
    until the title exists. Anonymous image requests never add a title. Every item request passes through it, so an
    id that is neither in `TitleLibrary`'s lock-free search-id index nor in the result registry leaves at once, with no
    state lock and no user lookup; the user's search switch is read only when a title would be added.
  - `JellyfinLibraryItems` implements `ILibraryItems`. A title is created with `ILibraryManager.ResolvePath` +
    `CreateItem` on the Currents folder, then refreshed (movie: its own refresh; series: `RefreshFullItem`, which
    creates seasons and episodes) with a timeout, falling back to a queued refresh. The realtime library monitor is
    paused on the kind's Currents root from before the files are written until the item exists; Jellyfin un-ignores
    it 45 s after resume.
- `Clients/Posters/PosterClient`: fetches a search card's poster. Raster images only, at most 10 MB, 10 s for the
  whole request including the body, logs only the host. A network error mid-body gives a 404. Poster URLs can embed
  the user's RPDB key, so they never reach clients.
- `Clients/Posters/PosterCache`: poster requests are anonymous, so posters are cached in memory per search id: one
  upstream fetch per id at a time, a poster kept for 1 h and a missing one for 5 min, at most 200 posters and 64 MB
  (the oldest go first).

Series added by search get new episodes on every catalog sync. Titles added by search (`AddedBySearch`) are never
pruned; a catalog title that was only opened from search keeps its catalog origin and is pruned as usual.

Deleting a search-added title in Jellyfin removes its folder. The next catalog sync then forgets it (`state.json`
entry removed, logged at Information) instead of writing it again, unless the kind's Currents root was missing when
the sync started (an unmounted share is not a deletion; the check runs before catalog writes, which would recreate the
root locally). With the Shows root missing, search-added series are not refreshed either, so nothing is written in
place of the share. Opening its search id before that sync, or a known title whose files are gone
(no folder, or a movie folder without its `.strm`), writes it again like a new add, in its old folder name.

### Request walkthrough (web client)
1. **Search**: `GET /Items?searchTerm=T&includeItemTypes=...&limit=800&userId=U` (after jellyfin-web's 500 ms
   debounce; it never calls `/Search/Hints`). `SearchResultsFilter` checks the user's search switch, starts the remote
   search, lets Jellyfin answer, then appends cards for results not already present.
2. **Card poster**: `GET /Items/{searchId}/Images/Primary`. `SearchItemFilter` answers with the proxied poster
   (`image/jpeg`, png, webp, gif or avif); the poster URL is never in any response.
3. **Open**: the user clicks the card; `GET /Users/{U}/Items/{searchId}` (and `GET /Items/{searchId}?userId=U` from
   the theme-media player). `SearchItemFilter` calls `SearchTitleOpener`, which adds the title once (concurrent opens
   share one result), then rewrites the id. The returned DTO's `Id` is the real item.
4. **Details page**: every later call (images, seasons, similar items) uses the real `Id`. Reloading the page with the
   search id in the URL resolves from `state.json` to the same item.
5. **Play**: the item is an ordinary Currents title, so the M2 version pipeline applies: the user's versions appear
   in the dropdown and playback goes through Jellyfin.

### Known limitations
- A search id for a title that was never opened, cached in a browser across a server restart, gets a 404 until the
  user searches again (the id is only recomputable once the title is in `state.json`).
- `/Search/Hints` is not covered; legacy clients that use it see no AIOMetadata results (M6 client matrix).
- The Currents folders must be in Movies/Shows libraries the user can see; otherwise nothing can be added for them.
- Users with parental controls never see AIOMetadata results and cannot add titles: `JellyfinLibraryItems.CanAdd`
  is false for a user with a maximum parental rating, blocked unrated items, blocked tags or allowed tags, because a
  remote result has no rating or tags Jellyfin could filter on. Both the search filter and the opener use `CanAdd`.

## Media (M4)

### Track sources and views
Every version has two track views (`Streams/VersionTracks`):
- **Display** (item DTOs, the details page): every known track with synthetic indexes 500-999. Never reaches ffmpeg.
- **Playback** (PlaybackInfo, streaming, ffmpeg): the probed tracks with real indexes, or the M2 `-1` stubs.

Index ranges per media source (`Streams/TrackIndexes`):

| Range | Tracks |
|---|---|
| 0-499 | real ffprobe indexes (probed versions only) |
| 500-999 | synthetic display indexes (display only, never in playback sources) |
| 1000-1999 | stream-attached external subtitles: 1000 + position in the stream's usable subtitle list |
| 2000+ | subtitles Jellyfin downloaded for the item: 2000 + Jellyfin's own stream index |
| -1 | the playback stubs of unprobed versions (M2 behavior) |

Sources of track data, in order of trust:
- `Streams/ProbeCache`: memory plus disk (`{plugin data}/probes/{key}.json`, atomic tmp + move), kept 30 days, at most
  5000 files, a disk miss remembered 10 min.
- `Streams/RemuxDbCache` + `RemuxDbIndex`: per-title lookups. `GET {RemuxDbUrl}/api/media/{tt...|tmdb:...}[:S:E]/versions`
  with the header `x-client-id: currents-` + 32 hex (HMAC of the install secret), 5 s total budget for the whole lookup (headers and body), 8 MB body cap. Cache 6 h for a
  hit, 30 min for a miss, 60 s after any error. Any failure is fail-soft: the release-name tracks show. Matching is local:
  same info hash (case-insensitive), then the same file index or file name; candidates that name a different file never
  match; the size (1 %) fallback applies only with a single remaining candidate. RemuxDB only sees ids and the client id.
- AIOStreams media info (`parsedFile` audio/subtitle tracks) and the release name (`MediaStreamMapper`).
- `Streams/TrackComposer` merges these into the display tracks; `VersionTracks` carries `Display`, `Playback`,
  `NeedsProbe`, `Container` and `RunTimeTicks`. For AIOStreams-listed tracks the release-name reasons to probe (Dolby
  Vision tags, missing width, a guessed channel layout) still apply.
- `Streams/TrackMatcher` maps a synthetic choice in a PlaybackInfo request to the probed track (same type as the request's index kind,
  same language, forced flag preferred, then position among that language's tracks). `PlaybackInfoFilter` looks the synthetic index up
  in `VersionSourceBuilder.DisplayBeforeProbe` (the display without the probe), so a choice made on a page loaded before the version
  was probed, by this or another user, still maps. If the probe failed, the choice is cleared and ffmpeg uses its default tracks.

### Subtitles
- **Stream-attached.** AIOStreams stream `subtitles` become external tracks 1000+. Clients see a placeholder path; the
  server fetches `/Currents/subtitles/{token}.srt` (signed loopback token) through `SubtitleDownloader` (5 MB cap, 15 s for
  the whole download, gzip by magic bytes) and `SubtitleText` (converts to SRT, validates cue times, skips malformed
  cues). An unreadable or non-subtitle upstream file gives 502. At most 5 per language and 40 per version.
- **Search provider.** `Features/Subtitles/CurrentsSubtitleProvider` (an `ISubtitleProvider`) asks AIOStreams'
  subtitles route for the requesting user's config (default config without a user): at most 25 results;
  lists cached 1 h, failures 2 min. Results are named `AIOStreams n (lang)`, carry no URL, and download through
  the same `SubtitleDownloader`.
- **Downloaded files.** Jellyfin saves a download next to the `.strm`; `GetMediaStreams` copies it into every version at
  index 2000 + its own index. A companion subtitle `{strm name}.….{srt|vtt|ass|ssa|sub|idx|sup|smi}` counts as a
  plugin file for adoption and deletion.

### Trailers
`MetaMapper.Trailers` maps AIOMetadata trailers to `RemoteTrailers` (`https://www.youtube.com/watch?v={id}`, at most 5).
They are not written into the NFO; a metadata refresh adds them to titles created earlier.

### Header-bound streams
`Streams/StreamHeaders` sanitises a stream's `requestHeaders`: it drops reserved headers (Host, Range, Content-Length,
hop-by-hop) and values containing CR/LF. The resolver's `allowHeaders` flag decides whether a header-bound stream may be
used: the loopback version route allows them, degraded `.strm` resolves skip them (their URL reaches clients).
The resolver follows redirects (up to 5 hops) and drops `Authorization`, `Cookie` and `Proxy-Authorization` when the
origin changes. `Web/ProxyStreamResult` then streams the resolved URL without following any redirect (a 3xx or 5xx
answer is a 502) and relays `Range` and the 206 answer and only content headers. Headers never appear in a client-facing field.

### Request walkthrough (web client)
1. **Details page**: `GET /Users/{U}/Items/{id}` returns the display tracks (synthetic indexes 500+), from RemuxDB or
   AIOStreams media info when matched, else the release name.
2. **Pick a French track** in the Audio select and press Play.
3. **PlaybackInfo**: `VersionProber` probes the version (a few seconds, cached on disk), `TrackMatcher` maps the synthetic
   audio index to the probed one (the index is looked up in the pre-probe display, so a page loaded before the probe still
   maps afterwards); if the probe fails, the choice is cleared and ffmpeg plays the file's default tracks.
4. **Stream**: ffmpeg opens the loopback URL with `-map 0:1` (the real index of the chosen stream).
5. **Subtitle fetch**: `/Videos/{id}/{versionId}/Subtitles/1000/0/Stream.vtt`; the decorator maps the index to the
   stream subtitle and the server fetches the loopback SRT, which Jellyfin converts to VTT.

### Known limitations
- No automated subtitle downloads: scheduled or automatic subtitle searches return nothing.
- Jellyfin's subtitle dialog does not list the existing subtitles of Currents items.
- Embedded subtitles of a probed version play as External tracks (`TrackComposer` sets `SupportsExternalStream` on
  probed subtitle streams, as Jellyfin does for library streams; without it Jellyfin chose burn-in and never extracted
  the file). The first request makes Jellyfin extract every subtitle stream of the version, which reads the whole remote
  file (about 21 min for 44.6 GB on the dev stack); the result is cached per version under `data/subtitles/`. Burn-in of
  an embedded text subtitle waits for the same extraction before the video starts.
- Loopback subtitle tokens appear in ffmpeg logs, like version tokens do (accepted, as in M2).
- The companion-subtitle rule also matches a user's own `{strm name}.srt` in a Currents folder; it is then treated as a
  plugin file and removed with the title.

## Extras (M5)

### Modules
- `Segments/*`: skip markers. `TheIntroDbSource`, `AniSkipSource` and `PublicMetaDbSource` (all `ISegmentSource`, each
  with its own `SourcePacer` rate limit) feed `SegmentService`, which asks the sources that apply in parallel, keeps for
  each marker kind the markers of the highest-priority source that has it, sanitises them against the reference runtime
  (drops markers under 1 s, clamps to the runtime) and hands the result to `SegmentStore` (memory plus
  `{plugin data}/segments/{key}.json`, fresh about 30 days when found, about 7 when not, each scaled by a stable per-title factor of 0.8 to 1.2). `SegmentGate` decides whether a title's
  markers fit one version's runtime.
- `Features/Segments/CurrentsSegmentProvider`: the `IMediaSegmentProvider` (name `Currents`, which keys the stored
  segments). Jellyfin builds it before the plugin is initialised, so it reads no settings in its constructor and resolves
  Jellyfin services lazily.
- `Integration/RefreshSkipMarkersTask`: "Fetch skip markers", a manual task that runs the segment providers for every
  Currents movie and episode. `Library/SkipMarkerQueue` queues it after a sync that wrote titles.
- `Integration/SegmentPresence`: asks Jellyfin whether an item has stored segments (resolved lazily).
- `Integration/SegmentRequestFilter`: answers `GET /MediaSegments/{id}` with an empty list when the markers do not fit.
- `Integration/JellyfinCollectionSync` (an `ICollectionSync`, planned by `Library/CollectionPlan`): one locked BoxSet per
  ticked catalog.
- `Integration/CompatWarning`: logs a warning and adds a Dashboard activity entry on an untested Jellyfin version.
- `Library/LibraryMaintenance` with `VerifyLibraryTask` and `PurgeContentTask`; `Library/LibraryJobGate` makes sync,
  verify and purge run one at a time. `Streams/ClearStreamCacheTask` clears the stream lists.
- `Web/DiagnosticsController`: the diagnostics endpoint and the connection tests.
- `Common/CompatState` (the compat decision, see ADR 0006), `Common/DiagnosticsLog` (the last 50 problems, in memory),
  `Common/TaskKeys` (the task keys).

### Tasks
Under "Currents" in Dashboard -> Scheduled Tasks: Sync AIOMetadata catalogs, Fetch skip markers (manual), Clear stream
cache, Verify library, Purge Currents content. Their class names and namespaces are stable once released.

### Request walkthrough: skip markers, from fetch to the skip button
1. **Fetch**: "Fetch skip markers" (or Jellyfin's "Media Segment Scan") calls `CurrentsSegmentProvider` for an item.
2. **Lookup**: the provider calls `SegmentService`, which asks the sources that apply to the title's ids (TheIntroDB for
   TMDB/IMDb/TVDB ids, AniSkip for anime-provider ids, PublicMetaDB for TMDB ids with a key), merges, sanitises and
   stores the lookup. Jellyfin saves the markers in its database.
3. **PlaybackInfo**: for each version `SegmentPresence` says whether Jellyfin has segments, and `SegmentGate` compares the
   version's real runtime with the reference runtime; `HasSegments` is true only when both hold.
4. **Skip button**: jellyfin-web calls `GET /MediaSegments/{versionId}`. `SegmentRequestFilter` (order -1002) checks the
   gate for that version and answers an empty list when it fails. Otherwise `SyntheticVersionIdFilter` (-1000) rewrites
   the version id to the item id and Jellyfin returns the stored segments.

The reference runtime is AniSkip's matched `episodeLength` when an AniSkip marker is used, else AIOMetadata's runtime for
the title (the series runtime for an episode). A version's runtime comes only from the probe, RemuxDB or AIOStreams.
When either is unknown, "markers when the runtime is unknown" decides (default off). The tolerance is ±2 % by default,
clamped to 1-10 %. In degraded mode (versions off or the compat guard inactive) only that unknown-runtime setting applies.

### MVC filter order
| Order | Filter |
|---|---|
| -1002 | `SegmentRequestFilter` (must see the version id before it is rewritten) |
| -1001 | `SearchItemFilter` |
| -1000 | `SyntheticVersionIdFilter` |
| -999 | `PlaybackInfoFilter` |
| -998 | `SearchResultsFilter` |

### Collections
After each sync `CollectionPlan` lists, per ticked catalog, the titles in catalog order and `JellyfinCollectionSync`
applies it: it finds the BoxSet by its `CurrentsCatalog` provider id (an admin may rename it), creates it locked when
missing, appends new titles and removes only Currents titles that left the catalog. Items added by hand stay. A name
taken by a collection Currents does not own becomes "{name} (Currents)"; two ticked catalogs with one name become
"{name} ({type})".

### Compat guard
`CompatState` is created at registration from the server version (or `CURRENTS_COMPAT_TEST_VERSION`) and read on every
call by the decorator, `PlaybackInfoFilter` and `SegmentRequestFilter`. See `docs/adr/0006-compat-guard-as-a-runtime-switch.md`.

### Known limitations
- The series runtime is the reference for TheIntroDB episodes, so episodes far from the typical length lose markers under a
  strict tolerance.
- IMDb-keyed anime get no AniSkip markers (AniSkip needs an anime-provider id); TheIntroDB covers them.
- Unticking Collection leaves the BoxSet in place.
- The first collection creates Jellyfin's Collections library and runs one full library scan.
- TheIntroDB's daily limit is about 500 requests anonymously (1000 with a key). A library converges at roughly that many
  new lookups a day; Currents pauses for the `Retry-After` of a 429, a failed lookup is retried on the next run, and a
  title found without markers is asked again after about a week (with markers, after about a month).
- Diagnostics "Recent problems" is an in-memory list of the last 50 events, lost on restart.

## Admin API
`Web/AdminController` (admin only), used by the configuration page:
- `POST /Currents/admin/catalogs` with JSON body `{"ManifestUrl": "..."}` lists AIOMetadata catalogs.
- `POST /Currents/admin/test-streams` with JSON body `{"ManifestUrl": "..."}` tests an AIOStreams manifest.
- `GET /Currents/admin/paths` returns the Movies/Shows folders to add as libraries.
- `POST /Currents/admin/sync` starts a sync now.
- `GET /Currents/admin/diagnostics` (`Web/DiagnosticsController`) returns the compat state, whether versions and skip
  markers are on, whether a TheIntroDB or PublicMetaDB key is set (never the key), stream and skip-marker cache hit
  rates, the number of probed files and the recent problems.
- `POST /Currents/admin/diagnostics/test` runs the connection tests: AIOStreams, AIOMetadata, RemuxDB and each marker
  source. Each reports `ok`, `off` (not configured) or a failure.

Manifest URLs travel in request bodies, never query strings, because they contain credentials.

## Degraded mode and `StrmBaseUrl`
This section applies only when versions are off (`EnableVersions = false`); versions never use `StrmBaseUrl`.
Without the media-source decorator, Jellyfin exposes each `.strm` as a remote media source whose path is the
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

Degraded mode also applies on a Jellyfin version outside the tested range `[12.1, 13.0)`, unless the admin ticks "Run on
this untested Jellyfin version" (see "Extras (M5)"). Skip markers then show only when "markers when the runtime is
unknown" is on, because Currents cannot tell which file plays.

## Module rules
- `Integration/` is the only folder that touches Jellyfin internals. Everything else is unit-tested with fakes.
- Outbound HTTP goes through named clients with rate limiting, retries and a circuit breaker (`Clients/Http`). The
  rate limiter is shared per client (one AIOStreams bucket); the circuit breaker is per (client, host[:port]), so one
  user's dead self-hosted AIOStreams does not pause calls to other hosts.
- Secrets never reach logs (`Common/SecretMasker`).

## Planned (later milestones)
M6 releases, the `/Search/Hints` decision and the `targetAbi` decision.
