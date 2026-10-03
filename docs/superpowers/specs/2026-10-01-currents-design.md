# Currents — AIOStreams & AIOMetadata for Jellyfin

**Design spec** · 2026-10-01 · Status: awaiting review

## 1. Intent

### What the user asked for
- A Jellyfin plugin integrating [AIOStreams](https://github.com/Viren070/AIOStreams), with as much integration as Jellyfin can support.
- Multi-user Jellyfin support.
- Catalogs handled separately from streams: a global catalog addon ([AIOMetadata](https://github.com/cedya77/aiometadata)) decides which titles exist; AIOStreams supplies streams.
- A new public GitHub repo following best practices.

### Decisions made during brainstorming
| # | Decision | Choice |
|---|---|---|
| D1 | Feature parity target | Match [Gelato](https://github.com/lostb1t/Gelato)'s UX, including streams in the native **Version** dropdown, accepting a decorator over core services |
| D2 | Audience | Public community plugin (public repo, plugin-repo manifest, CI releases, docs) |
| D3 | How titles enter the library | Catalog sync **plus** search with auto-add on opening a result |
| D4 | Pruning | Titles leaving a catalog are pruned unless watched by anyone or search-added |
| D5 | Per-user AIOStreams config | Admin default + per-user admin overrides + optional self-service |
| D6 | v1 scope | Core + all seven features (subtitles, segments, trailers, smart selection, probing/pre-fill, collections, upgrade safety) |
| D7 | Test backends | User's self-hosted AIOStreams + AIOMetadata; throwaway Jellyfin 12.1 in a dev compose stack |
| D8 | Architecture | Approach 1: real `.strm`/`.nfo` titles + ephemeral per-user synthetic versions via a single decorator |
| D9 | Name | **Currents** — repo `survivalizer/jellyfin-plugin-currents`, assembly `Jellyfin.Plugin.Currents`, routes `/Currents/...` |
| D10 | License | GPL-3.0 |

### Why this plugin exists (vs. alternatives)
AIOStreams and AIOMetadata both ship built-in Jellyfin-*emulating* servers (direct play only, no transcoding, separate from a real library). Gelato is the closest plugin but persists streams as DB rows, decorates ~10 core services, and repeatedly breaks on Jellyfin upgrades (library wipes from 12.0's `MigrateLinkedChildren`, split watch state, client regressions). Currents' differentiators:
1. Lives **inside a real Jellyfin** alongside local media, with server transcoding and one egress IP to debrid.
2. **Survives upgrades**: titles are real files; versions are never persisted.
3. **Degrades gracefully**: if the one decorator breaks, playback still works via `.strm` → resolve endpoint.
4. **First-class multi-user** with layered configs and self-service.

### Success criteria
- A Jellyfin 12.x user with self-hosted AIOStreams + AIOMetadata installs Currents from a repo URL, enters two URLs, picks catalogs, and within one sync sees titles in normal Movies/Shows libraries.
- Opening a title shows that user's AIOStreams streams as versions; selecting one plays (direct stream or transcode) through Jellyfin on web, Android TV, Swiftfin, Findroid, Infuse, Streamyfin.
- Two users with different AIOStreams configs see different versions for the same title; neither can see the other's credentials or streams.
- Upgrading Jellyfin (minor) or restarting never removes titles or watch state.
- Disabling the decorator leaves every title playable (auto-selected stream).

## 2. Platform facts this design relies on

Verified against source on 2026-10-01 (clones of jellyfin v12.1, AIOStreams v2.35.7 @ f815290, AIOMetadata @ 6b27a7a, Gelato v0.26.21.1).

- **Jellyfin**: latest stable 12.1 (2026-09-15); versioning dropped the `10.` prefix. Plugins target `net10.0`, reference `Jellyfin.Controller`/`Jellyfin.Model` 12.1.0 (`ExcludeAssets=runtime`), `targetAbi` `12.0.0.0`. 10.11 plugins do not load on 12.
- `IMediaSourceProvider` sources only appear in `GetPlaybackMediaSources`, never in `GetStaticMediaSources` (so not in the Version dropdown), and receive no user → insufficient for D1; hence the decorator.
- `.strm` files: first non-comment line, `http/https/rtsp/rtp` only; resolved as shortcuts and probed at playback.
- `IServerEntryPoint` is gone; use `IPluginServiceRegistrator` + `AddHostedService`. `ISubtitleProvider`/`IMediaSegmentProvider` must be registered in DI; `IMediaSegmentProvider` now requires `CleanupExtractedData`. `ISearchEngine` was replaced by `ISearchProvider`.
- Legacy auth is off by default in 12: only `Authorization: MediaBrowser Token=...` and `?ApiKey=` work.
- **AIOStreams**: `GET /api/v1/search?type=&id=` with `Authorization: Basic base64(uuid:password)` (password may be raw or the encrypted one from the manifest URL) returns flat results incl. `parsedFile`, `cached`, `size`, `infoHash`, `requestHeaders`, plus separate `errors[]`/`statistics[]`. Debrid playback URLs resolve on GET via 307 (HEAD → 405), failures 307 to `/static/*.mp4` placeholders; links valid 24h by default. Default rate limits are per source IP (search 5/10s). Subtitles via the Stremio `subtitles` resource.
- **AIOMetadata**: config stored server-side by UUID; install URL `/stremio/{uuid}/manifest.json`. Catalog `skip` maps to pages (page size typically 20; MAL 25; anilist.trending 50) — request multiples of page size, stop on short page. Catalog responses carry full metas. Meta `id` is IMDb when known, else `tmdb:`/`tvdb:`/`mal:`/`kitsu:`; episodes `tt…:S:E` (or provider fallbacks). Ignore `_`-prefixed fields.

## 3. Architecture

### Solution layout
```
src/Jellyfin.Plugin.Currents/          plugin (net10.0)
tests/Jellyfin.Plugin.Currents.Tests/  xUnit unit + integration tests
dev/                                   docker-compose dev stack (Jellyfin 12.1), .env.example
docs/                                  architecture, configuration, ADRs, client matrix
```

### Modules
| Module | Responsibility | Jellyfin surface |
|---|---|---|
| `Clients/` | Typed `HttpClient`s for AIOStreams (search, subtitles, user validation) and AIOMetadata (manifest, catalog, meta, search); shared throttling, caching, retry, circuit breaker, secret-masking logging | none |
| `Library/` | Catalog sync, `.strm`/`.nfo` writer, state store, pruning, search auto-add materialization | `IScheduledTask` |
| `Metadata/` | AIOMetadata-backed metadata, images, trailers, external IDs | `IRemoteMetadataProvider` (Movie/Series/Season/Episode), `IRemoteImageProvider`, `IExternalId` |
| `Streams/` | Per-user stream fetch + cache + single-flight, ranking, `MediaSourceInfo` mapping, resolve/failover, placeholder detection, signed tokens | resolve controller |
| `Users/` | Config layering, per-user store (`users.json`), preferences, validation | plugin configuration |
| `Features/` | Subtitles, segments, collections, maintenance tasks, version-compat guard | `ISubtitleProvider`, `IMediaSegmentProvider`, `ICollectionManager`, `IScheduledTask` |
| `Integration/` | **All** code touching Jellyfin internals: `IMediaSourceManager` decorator, MVC filters (item-id resolution in M2, search in M3), DTO URL scrubbing | DI decoration, MVC action filters |
| `Web/` | Admin config page, self-service user page, diagnostics | `IHasWebPages`, API controllers |

### Boundaries
- Only `Integration/` depends on Jellyfin internals that may change. Everything else is unit-testable with fakes.
- Modules talk through interfaces (`IStreamService`, `ICatalogSource`, `IUserConfigResolver`, `IStreamRanker`, `ILibraryWriter`).
- The resolve endpoint (`/Currents/play/...`) is the **fallback contract**: every `.strm` targets it, so playback never depends on the decorator.

## 4. Data flows

### 4.1 Catalog sync (scheduled task + "Sync now")
1. Admin sets AIOMetadata manifest URL; plugin fetches manifest and lists catalogs. Per selected catalog: target (Movies/Shows), max items, enabled, optional collection.
2. Task pages each catalog (`skip` in multiples of page size) until short page or max.
3. Canonical key per title: IMDb → TMDB → TVDB → Kitsu/MAL. Files written under plugin-managed roots (admin adds these folders to normal libraries):
   - `Movies/Title (2024) [imdbid-tt123]/Title (2024).strm` + `movie.nfo` (all `uniqueid`s)
   - `Shows/Title (2019) [imdbid-tt456]/tvshow.nfo` + `Season 01/Title S01E01.strm` per released episode
   - Folder tags use the canonical provider (`[imdbid-…]`, `[tmdbid-…]`, `[tvdbid-…]`).
4. `.strm` content: `{StrmBaseUrl}/Currents/play/{type}/{urlEncodedId}?sig={hmac}` (non-expiring signature). `StrmBaseUrl` defaults to `http://127.0.0.1:8096`; see `docs/spikes/2026-10-m0-findings.md` S4 for when it must be client-reachable. Sync rewrites `.strm` files when it changes.
5. State store (`state.json` in plugin data dir, atomic writes): per title — key, source catalogs, `addedBySearch`, `lastSeenSync`, `missCount`. Writes are idempotent (only changed files rewritten, temp-file + rename). Afterwards refresh only the plugin roots.
6. "Update ongoing series" task adds newly released episodes from meta `videos[]`.
7. Pruning (D4): remove a title only when absent from all its catalogs for **N consecutive successful syncs** (default 3), not search-added, and not played by any user.

### 4.2 Search auto-add (D3)
1. Jellyfin search additionally queries AIOMetadata search catalogs; results not in the library are returned alongside local results.
2. Opening a remote result materializes it (4.1 step 3–5 for one title, `addedBySearch=true`), refreshes that folder, and returns the real item.
3. Mechanism: MVC action filters on the search and item endpoints (Jellyfin 12 `ISearchProvider` cannot return items that are not yet in the library — M0 S2).
4. Per-user toggle; admin can disable per user.

> **M3 amendment (2026-10-02).**
> - **Search ids.** A remote result carries a search id, not a library id, because the web client caches search results for 24 h and reuses `#/details?id=X` on reload. Search ids are GUIDs derived from the title's state id with HMAC-SHA256 under the install `SigningSecret` (first 16 bytes of the MAC over `currents/search/{stateId}`), so a materialized title is found again from `state.json` after a restart, and nobody without the secret can compute the id of a title from its public IMDb/TMDB id. Opening one returns the real item, and the client uses the real `Id` from then on. A search id for a title that was never opened, cached in a browser across a server restart, stops resolving and gets Jellyfin's 404; the user searches again.
> - **Direct creation.** Titles are created directly, not by a scan (a scan or file-change event takes 60 s or more and reprocesses the whole library). Opening a result resolves only the new title folder (`ResolvePath` + `CreateItem`) and refreshes only that item (a series: its own seasons and episodes). The realtime library monitor is paused on the kind's Currents root (Movies/ or Shows/) from before the title's files are written until its Jellyfin item exists; Jellyfin un-ignores it 45 s after resume.
> - **Existing items win.** A result whose IMDb, TMDB or TVDB id matches any library item the user can see, a Currents title or the user's own file, opens that item instead of adding a copy.
> - **Posters are proxied.** AIOMetadata poster URLs can embed the user's RPDB key, so the server fetches the poster (raster images only, 10 MB, 10 s for the whole request including the body) and never sends its URL to clients. A network error mid-body gives a 404. Poster requests are anonymous, so posters are cached in memory per search id (one upstream fetch at a time, 1 h for a poster, 5 min for a missing one, at most 200 posters and 64 MB).
> - **Scope of the search filter.** It covers `GET /Items` with `searchTerm`, which is what jellyfin-web 12.1, Android TV, Swiftfin and Findroid use. `/Search/Hints` (legacy clients) is left for the M6 client matrix. Local results are returned immediately when the local page already fills `limit`, and any remote search failure degrades to local-only results.
> - **One switch.** The per-user toggle is one switch. The global `EnableSearch` must be on; the admin's per-user "search add off" flag wins over the user's own choice; the user's self-service choice (when self-service is allowed and the user is not locked) comes next; `DefaultSearchAutoAdd` is the fallback. A user with the switch off sees no remote results and cannot add titles; so does any user with parental controls (maximum rating, blocked unrated items, blocked or allowed tags), because remote results carry no rating to filter on. The self-service page sends the switch only when the user changed it.
> - **Afterwards.** Search-added series get new episodes on each catalog sync; titles added by search are never pruned (a catalog title that is only opened from search keeps its catalog origin and can still be pruned).
> - **Deleted titles.** A search-added title deleted in Jellyfin (its folder is gone) is forgotten by the next catalog sync, not written again; a Movies/ or Shows/ root missing when the sync starts (e.g. an unmounted share) is not a deletion, and search-added series are then not rewritten. Opening a known title whose files are gone (no folder, or a movie without its `.strm`) fetches its meta again and writes it like a new add, in the old folder.

### 4.3 Playback
1. **Item detail**: the decorated `IMediaSourceManager.GetStaticMediaSources` detects Currents items (path under a plugin root), resolves the requesting user from `IHttpContextAccessor`, and calls `IStreamService.GetStreams(user, item)` — cached per (user, title) for `StreamCacheTtl` (default 1h), single-flight to absorb duplicate web-client calls.
2. **Mapping** each ranked stream to `MediaSourceInfo`:
   - `Id`: deterministic GUID from (itemId, user, stream identity — infoHash+fileIdx, else filename+size, else url hash), HMAC-keyed with the install secret (see the M2 amendment below).
   - `Name`: e.g. `2160p DV · Atmos · 18.4 GB · cached`.
   - `MediaStreams`: pre-filled from `parsedFile` (+ RemuxDB, §5.5).
   - `Path`: redacted to `currents://version/{id}` for clients; Jellyfin's own ffmpeg/ffprobe get a signed loopback URL `{internal base}/Currents/play/s/{token}` (see the M2 amendment below). `Protocol=Http`; `SupportsDirectPlay=false` so clients always stream through Jellyfin; `SupportsDirectStream` (remux) and `SupportsTranscoding` follow the user's Jellyfin remux/transcode permissions.
3. **Synthetic ids must resolve as items.** The web client looks up every MediaSource id as a library item (`GET /Items/{id}`) before PlaybackInfo, so an `Integration/` MVC filter resolves synthetic version ids to the base item. Every version must carry `MediaStreams` (parsed data / RemuxDB, with a one-time cached probe as fallback), or ffmpeg gets no codec arguments. The base item must get `RunTimeTicks` (metadata runtime or first probe), or resume never works. See `docs/spikes/2026-10-m0-findings.md` S1b, S1c.
4. **PlaybackInfo**: decorated `GetPlaybackMediaSources` returns the same list; probes only the chosen source when track info is insufficient.
5. **Stream**: Jellyfin fetches the resolve URL → plugin validates token, gets the AIOStreams playback URL from cache (re-searches if missing/expired), follows redirects, detects placeholders, fails over to next-ranked stream (max `FailoverAttempts`, default 3), then 302s to the final URL — or proxies when the stream requires headers.
6. **Watch state** is recorded on the base `.strm` item (one item, no per-version split).

> **M2 amendment (2026-10-01).**
> - **Version ids** are derived from (item, **user**, stream), not (item, stream). Jellyfin looks versions up by id with no user (streaming, session reporting, subtitles, attachments, some from timers with no request), and two users with different debrid accounts must never share an id. A process-wide registry (`Streams/VersionRegistry`) resolves ids to their version.
> - **Paths**: client-facing `Path` values are redacted to `currents://version/{id}` (whenever Jellyfin passes `enablePathSubstitution`). Only Jellyfin's ffmpeg/ffprobe get the real path, a loopback URL `{internal base}/Currents/play/s/{token}` built from Jellyfin's bind addresses (not `StrmBaseUrl`). The token is HMAC-signed, domain-separated from `.strm` signatures, and expires after `VersionTokenHours` (default 24 h). `/Currents/play/s/{token}` answers only loopback or the server's own addresses.
> - **Search budget**: list views (home screens, library grids, `Fields=MediaSources`) never search AIOStreams; they use cached results or show a single pending source. Only item detail and PlaybackInfo search (10 s wait), plus version resolution during playback for a known user.
> - **Failure cache**: a failed search, or an empty result because AIOStreams' addons failed, is cached for only 30 s; a successful result for `StreamCacheMinutes` (default 60).
> - **PlaybackInfo** (an `Integration/` MVC filter) maps a stale or item-level `MediaSourceId` to the user's version (same stream when still offered, else the best one), probes that version when its parsed info is not enough (§5.5), and saves the item runtime when it is missing.
> - **Pre-filled tracks** use `Index = -1` (no guessed stream index for ffmpeg to map), an estimated video bitrate and an explicit HDR range; without them remux turns into a full transcode or maps the wrong tracks.

> **M4 amendment (2026-10-02).** Streams that require request headers (`requestHeaders`) are proxied by the loopback version route (`/Currents/play/s/{token}`). It relays `Range` and the `206` answer and only content headers, never follows redirects (a 3xx or 5xx answer is a 502), and drops `Authorization`, `Cookie` and `Proxy-Authorization` on a cross-origin redirect. `StreamHeaders` drops reserved headers (Host, Range, Content-Length, hop-by-hop) and values with CR/LF. The headers stay server-side: no client field carries them. Degraded `.strm` playback skips header-bound streams, because its URL reaches clients.

### 4.4 Degraded mode
`.strm` resolve URLs carry no user. If the decorator is disabled (manually or by the compat guard), playing a title uses the global default config with auto-selection. Titles, metadata, and watch state are unaffected. `StrmBaseUrl` must be reachable by clients, because clients may direct-play the resolve URL themselves and follow its redirect (M0 S4).

> **M2 amendment (2026-10-01).** With versions on (`EnableVersions = true`, the default) Jellyfin never plays the `.strm` URL; degraded mode is what `EnableVersions = false` (or, from M5, the compat guard) gives. `StrmBaseUrl` therefore matters only in degraded mode; versions never use it.

## 5. Features

### 5.1 Subtitles
`ISubtitleProvider` queries AIOStreams `subtitles` for the title (user from HTTP context when available, else default config). Stream-embedded `subtitles` become external tracks on that version, served via a plugin proxy route so URLs stay hidden. Error entries are filtered.

> **M4 amendment (2026-10-02).** Subtitles are three things:
> - **Stream subtitles** become external tracks (indexes 1000+). The server fetches them through a loopback route (`/Currents/subtitles/{token}.srt`) and converts them to SRT: 5 MB cap, 15 s for the whole download, gzip detected by magic bytes, cue times validated (malformed cues are skipped). An unreadable or non-subtitle upstream file gives 502.
> - **The search provider** (`Features/Subtitles/CurrentsSubtitleProvider`) serves Jellyfin's subtitle dialog from AIOStreams' Stremio subtitles route, for the requesting user's config (the default config without a user). Results carry no URLs.
> - **Downloaded subtitles** that Jellyfin saves next to the `.strm` appear in every version (indexes 2000+).
>
> Deferred: automated subtitle downloads (scheduled searches return nothing), and Jellyfin's subtitle dialog does not list the existing subtitles of a Currents item.

### 5.2 Skip intro / credits
`IMediaSegmentProvider` (incl. `CleanupExtractedData`) sourcing markers from IntroDB and AniSkip (and PublicMetaDB when a key is configured). Applied only when the playing version's runtime is within tolerance (default ±2%) of the reference runtime.

> **M5 amendments (2026-10-03).**
> - **"IntroDB" is TheIntroDB** (`api.theintrodb.org`): TMDB, IMDb and TVDB ids, movies and episodes, an optional key.
> - **Reference runtime.** When an AniSkip marker is used, the reference is AniSkip's matched `episodeLength`. Otherwise it is AIOMetadata's runtime for the title (for an episode, the series runtime). A version's runtime comes only from the probe, RemuxDB or AIOStreams. When either runtime is unknown, the admin setting "markers when the runtime is unknown" decides (default: no markers). The tolerance is an admin setting, default ±2 %, clamped to 1-10 %.
> - **When markers are fetched.** Jellyfin's "Media Segment Scan" task (every 12 h) fetches them, and so does a new manual task, "Fetch skip markers", which every sync that wrote titles queues. They are never fetched at play time. The gate applies at play time: `HasSegments` per version in PlaybackInfo, and a filter on `GET /MediaSegments/{id}`.
> - **Degraded mode** (versions off, or the compat guard inactive). Currents cannot tell which file plays, so markers show only when "markers when the runtime is unknown" is on.
> - **Source coverage.** PublicMetaDB is used only for TMDB-keyed titles, because its API takes TMDB ids. AniSkip is used only for anime-provider ids (mal, kitsu, anilist, anidb); IMDb-keyed anime use TheIntroDB.

### 5.3 Trailers
AIOMetadata `trailers` → item `RemoteTrailers` via the metadata provider.

> **M4 amendment (2026-10-02).** Trailers come from the metadata provider as `RemoteTrailers` (`https://www.youtube.com/watch?v={id}`, at most 5 per title). They are not written into the NFO, so titles added before 0.4.0 get them on the next metadata refresh.

### 5.4 Smart selection
Pure `IStreamRanker`:
1. Drop AIOStreams `error`/`statistic` entries and anything without a `url`.
2. Apply user filters: cached-only, max size, excluded resolutions/visual tags.
3. Stable re-rank by user preferences (resolution order, HDR/DV preference, audio/subtitle languages); ties keep AIOStreams' order (respects the user's AIOStreams sort rules).
Auto-select mode exposes only the top version. Placeholder detection at resolve: final URL path under `/static/` or matching known placeholder filenames.

### 5.5 Probing / pre-fill
Map `parsedFile` → `MediaStream`s (video codec, resolution, HDR type, audio codec/channels/languages, subtitle tracks). Optional RemuxDB lookup by infoHash/NZB for full track lists and runtime (configurable, on by default). ffprobe only on the selected version when needed, with raised `probesize`/`analyzeduration` (defaults 40M/5M), cached by stream identity.

> **M2 amendment (2026-10-01).**
> - The **selected-version probe** and the **runtime fallback** moved into M2, because M0 showed that versions without tracks cannot play. The probe runs in the PlaybackInfo filter (never in the synchronous decorator, which list views reach), times out after 20 s with `AnalyzeDurationMs` 5000, remembers failures for 10 min and caches results in memory for 7 days. RemuxDB and persisting probe results across restarts stay in M4.
> - **Runtimes from AIOMetadata** are set by a custom metadata provider (`Metadata/AioRuntimeProvider`), because Jellyfin's metadata merge drops `RunTimeTicks` for videos. When a title still has no runtime, the first probe (or the AIOStreams duration) saves it; without one, resume and HLS fail.

> **M4 amendment (2026-10-02).**
> - **Two track views per version.** Item DTOs (details page) list every known track with synthetic indexes 500-999 (display only). Sources for PlaybackInfo, streaming and ffmpeg carry either the probed tracks (real indexes) or the M2 `-1` stubs. PlaybackInfo probes and maps a synthetic choice to the probed track; if the probe fails, ffmpeg uses its default tracks. This replaces "pre-fill `MediaStreams`" for a version that has not been probed.
> - **RemuxDB is looked up by title, not by info hash.** `GET {RemuxDbUrl}/api/media/{tt...|tmdb:...}[:S:E]/versions` with an `x-client-id` derived from the install secret (`currents-` + 32 hex); IMDb and TMDB ids only. Currents matches locally: same info hash (case-insensitive), then the same file index or file name; a candidate that names a different file never matches; the size fallback (within 1 %) applies only when a single candidate remains. RemuxDB data is display-only (crowd-sourced); playback still probes when a non-default track is chosen. The service receives only the ids and the client id. Total budget 5 s for the whole lookup (body read included), body cap 8 MB, cache 6 h for a hit, 30 min for a miss, 60 s after any error; every failure is fail-soft (release-name tracks).
> - **Probe results persist** in `{plugin data}/probes/{key}.json` for 30 days (at most 5000 files), keyed by stream identity, so a file is probed once per install, not once per restart. For AIOStreams-listed tracks the release-name reasons to probe (Dolby Vision tags, missing width, guessed channel layout) still apply.

### 5.6 Collections
Per catalog, optional Jellyfin BoxSet kept in sync with catalog membership each run.

> **M5 amendment (2026-10-03).**
> - Collections are opt-in per catalog, locked, and kept in catalog order (new titles are appended).
> - The first collection creates Jellyfin's Collections library, which runs one full library scan.
> - Currents removes only Currents titles from its collections; items an admin added by hand stay.
> - Unticking a catalog leaves its collection in place.
> - Currents restores `DisplayOrder` "Default" on its collections each sync, which overrides a manual sort on a Currents collection.
> - When a collection name is taken by a collection Currents does not own, Currents uses "{name} (Currents)". Two ticked catalogs with the same name are told apart as "{name} ({type})".

### 5.7 Upgrade safety
- Tasks: **Verify library** (rebuild missing files from state), **Purge Currents content**, **Clear stream cache**.
- **Compat guard** at startup: if the server version is outside the tested range (`[12.0, 13.0)` initially), the decorator is not registered (degraded mode) and an admin warning is shown; admin can force-enable.

> **M5 amendments (2026-10-03).**
> - **Compat guard as a runtime switch** (ADR 0006). The decorator and filters are always registered and stand down at runtime outside the tested range `[12.0, 13.0)`, unless the admin ticks "Run on this untested Jellyfin version". This replaces "decorator not registered"; force-enable needs no restart. Search stays governed by its own switch. A Jellyfin whose interfaces changed fails to load the plugin before any guard runs.
> - **Purge and Verify.** "Purge Currents content" removes every Currents title, the sync state, and the probe and skip-marker caches, then removes the purged titles' Jellyfin entries (only for folders Currents actually removed) and refreshes the library; the next sync writes the enabled catalogs again. "Verify library" rewrites titles whose files are missing and drops `users.json` records of deleted Jellyfin users. Jellyfin's own subtitle-extraction cache is left to Jellyfin's cache cleanup.

## 6. Multi-user (D5)

### Precedence (first match wins)
1. Self-service (if allowed for that user and not locked)
2. Admin per-user override
3. Global default
4. None → titles visible, a single non-playable "Streams not configured" version

Admin per-user controls: assign override, allow/lock self-service, disable streams entirely, disable search auto-add.

### Per-user record
AIOStreams credentials (parsed from a pasted manifest URL: base URL, UUID, encrypted password; validated on save), preferences (§5.4), auto-select vs. show-all, search auto-add.

### Storage & secrets
- Global settings: standard plugin configuration XML.
- Per-user records: `users.json` in plugin data dir, keyed by Jellyfin user ID.
- Credentials are write-only via the API (masked on read). Stored unencrypted at rest, documented as equivalent to Jellyfin's own data-dir trust boundary.

### Self-service page
Served by the plugin at `/Currents/user` (same origin), authenticated with the user's Jellyfin session; endpoints are `[Authorize]` and scoped to the caller's own record. Docs explain adding a custom menu link; the admin page shows the URL.

> **M2 amendment (2026-10-01).** The menu entry is a jellyfin-web `config.json` `menuLinks` entry (`{"name": "Currents", "icon": "tune", "url": "/Currents/user"}`); there is no plugin API for a user-menu entry (plugin pages appear only in the admin dashboard). The page itself is served anonymously (a browser navigation carries no Jellyfin auth header); its API calls are authenticated. Saving a manifest URL validates it, which makes the **server** contact the URL the user entered (any signed-in user can therefore make the server send one request to a host of their choice; see `docs/architecture.md`).

### Visibility & shared limits
Library visibility uses Jellyfin's native permissions. All users share the server's AIOStreams rate limit bucket; global throttle + per-user cache absorb load; docs recommend raising limits / `TRUSTED_IPS` on self-hosted instances.

## 7. Error handling

- **HTTP pipeline** (Microsoft.Extensions.Http.Resilience): per-host rate limiter (defaults: AIOStreams search 5/10s, catalog 30/5s, meta 15/5s; configurable), honor `Retry-After` on 429, 2 retries with jittered backoff on 5xx/timeouts, circuit breaker per host.
- **Item page**: cold stream fetch timeout 10s → single "Streams unavailable — retry" version; cache untouched.
- **AIOStreams envelopes**: `errors[]`/`statistics[]` logged structurally; user-facing message surfaced where applicable.
- **Catalog sync**: per-catalog isolation; failed pages retried next run; prune only after N consecutive successful syncs; atomic file writes.
- **Playback**: failover on dead/placeholder/expired; when exhausted, a clear Jellyfin playback error.
- **Tokens**: HMAC-SHA256 with a per-install secret, embedded expiry (default 24h).
- **Security**: secret-masking log enricher (UUIDs, passwords, tokens, debrid keys); assert no internal/debrid URLs in DTO/PlaybackInfo responses; self-service endpoints scoped to caller.
- **Diagnostics panel**: connection tests, cache hit rate, recent errors, decorator active/degraded.

> **M5 amendment (2026-10-03).** "Recent errors" is an in-memory list of the last 50 problems recorded by sync, stream search and skip-marker lookups; it is not a log reader. "Cache hit rate" covers stream lists and skip-marker lookups.

> **M2 amendment (2026-10-01).** Streams that require request headers (`requestHeaders`) are skipped. M4 proxies them (see the M4 amendment in section 4.3); degraded `.strm` playback still skips them.

## 8. Testing

- **Unit (every CI run)**: ranker, parsedFile→MediaStream mapping, ID canonicalization, path/NFO generation, pruning rules, token sign/verify/expiry, placeholder detection, config precedence, clients against recorded JSON fixtures.
- **Integration (`Category=Integration`, opt-in)**: `dev/docker-compose.yml` runs Jellyfin 12.1 with the built plugin mounted, pointed at the user's self-hosted AIOStreams/AIOMetadata via `dev/.env`. Covers sync, versions in item DTOs, PlaybackInfo, resolve/failover, per-user isolation.
- **Contract tests**: reflection assertions on the Jellyfin internal signatures `Integration/` depends on, so upgrades fail in CI.
- **Manual client matrix** (`docs/client-matrix.md`): web, Android TV, Swiftfin, Findroid, Infuse, Streamyfin, external players (VLC/MX/ExoPlayer).
- The user's existing local Jellyfin is **not** used for automated tests; the dev stack is.

## 9. Repository & delivery

- Public GitHub repo `survivalizer/jellyfin-plugin-currents`, GPL-3.0.
- Tooling: `global.json` (.NET 10), `Directory.Build.props` (version, `Nullable`, `TreatWarningsAsErrors`, `AnalysisMode=All`, StyleCop, SerilogAnalyzer, MultithreadingAnalyzer, doc generation), `.editorconfig`, Renovate.
- Packaging: `build.yaml` for jprm (`targetAbi: 12.0.0.0`, `framework: net10.0`).
- Docs: `README.md`, `docs/` (architecture, configuration, self-service, troubleshooting, client matrix), ADRs in `docs/adr/` (record D1, D3, D5, D8), `CLAUDE.md`, `CONTRIBUTING.md`, `SECURITY.md`, `CODE_OF_CONDUCT.md`, issue/PR templates, `CODEOWNERS`.
- CI (GitHub Actions): build + test + format check + CodeQL on push/PR; on release tag: jprm build, GitHub Release with zip, update `manifest.json` on `gh-pages` (`https://survivalizer.github.io/jellyfin-plugin-currents/manifest.json`). Conventional Commits + release-please.

## 10. Milestones

| Milestone | Contents | Exit criterion |
|---|---|---|
| **M0 Spike** | Synthetic versions (S1) and `.strm` client behaviour (S4); S2/S3 answered from source | Written findings; spec amended if any assumption fails (user informed before continuing) |
| **M1 Foundation & core** | Repo scaffolding, CI, clients, catalog sync, `.strm`/`.nfo`, metadata provider, resolve endpoint (degraded mode), admin page | Titles appear and play via default config |
| **M2 Versions & users** | Decorator, user store + precedence, self-service page, ranker, failover, placeholder detection | Per-user versions in dropdown; isolation test passes |
| **M3 Search** | Search + auto-add | Search result opens as library item |
| **M4 Media** | Pre-fill/RemuxDB/probing, subtitles, trailers | Tracks shown before playback; subtitles searchable |
| **M5 Extras** | Segments, collections, maintenance tasks, compat guard, diagnostics | Tasks run; guard verified on fake version |
| **M6 Release** | Release pipeline, manifest, docs, client matrix pass | v1.0.0 installable from repo URL |

> **M5 amendment (2026-10-03).** The exit criterion is checked at unit level and on the dev stack (`docs/spikes/2026-10-m5-e2e.md`): the five Currents tasks run, skip markers appear only on versions of the right length, a collection follows its catalog, and the compat guard stands down on a fake Jellyfin 13.0 (`CURRENTS_COMPAT_TEST_VERSION`) and comes back when forced on, with no restart. Two gaps from that run were fixed afterwards (32e37e8, e5956a2; unit-tested, dev-stack re-check pending): Purge now also removes the purged titles' Jellyfin entries (Jellyfin skips an empty library folder, so a refresh alone left them listed), and Currents restores `DisplayOrder` "Default" on its collections (the first collection once came out in premiere-date order).

## 11. Risks

| Risk | Mitigation |
|---|---|
| Synthetic (non-persisted) version IDs rejected by some Jellyfin path | M0 spike (`docs/spikes/2026-10-m0-findings.md`): ids must resolve as items via an `Integration/` MVC filter; fallback: persist version rows only (Gelato-style), keeping `.strm` base items |
| Decorator breaks on Jellyfin upgrade | Isolated in `Integration/`, contract tests, compat guard → degraded mode |
| Clients render versions differently | Auto-select mode; client matrix; document per-client behavior |
| AIOStreams rate limits with many users | Global throttle, per-user cache, docs for self-hosted limit tuning |
| Search auto-add clutter | `addedBySearch` tag, per-user toggle, purge-by-tag |
| Search-added titles created outside a scan are removed by a concurrent folder scan | created directly (ResolvePath + CreateItem), re-added once if a scan removed them, and found again by path or `Currents` id |
| RemuxDB (single-maintainer, crowd-sourced, undocumented API) changes or disappears | Display-only, fail-soft (5 s total budget, 60 s error cache), switchable, playback probes before using a chosen track |
| Segment data unavailable | IntroDB/AniSkip (optional PublicMetaDB) queried directly (M0 S3); feature degrades to no markers when a title has none |

## 12. Out of scope (v1)
P2P/torrent streams without debrid; Live TV; tracker sync (Trakt/Simkl — use existing Jellyfin plugins); non-AIOStreams stream backends; per-user catalogs; editing AIOStreams/AIOMetadata configs from Jellyfin.
