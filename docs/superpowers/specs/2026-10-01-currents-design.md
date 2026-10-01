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
| `Integration/` | **All** code touching Jellyfin internals: `IMediaSourceManager` decorator, search hook, DTO URL scrubbing | DI decoration, `ISearchProvider` or MVC action filter |
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
4. `.strm` content: `{InternalBaseUrl}/Currents/play/{type}/{canonicalId}` where `InternalBaseUrl` defaults to `http://127.0.0.1:8096` (server-side only; external hostname never baked in).
5. State store (`state.json` in plugin data dir, atomic writes): per title — key, source catalogs, `addedBySearch`, `lastSeenSync`, `missCount`. Writes are idempotent (only changed files rewritten, temp-file + rename). Afterwards refresh only the plugin roots.
6. "Update ongoing series" task adds newly released episodes from meta `videos[]`.
7. Pruning (D4): remove a title only when absent from all its catalogs for **N consecutive successful syncs** (default 3), not search-added, and not played by any user.

### 4.2 Search auto-add (D3)
1. Jellyfin search additionally queries AIOMetadata search catalogs; results not in the library are returned alongside local results.
2. Opening a remote result materializes it (4.1 step 3–5 for one title, `addedBySearch=true`), refreshes that folder, and returns the real item.
3. Mechanism: Jellyfin 12 `ISearchProvider` if it can surface remote results and an open hook; otherwise MVC action filters (Gelato's pattern). Decided in M0.
4. Per-user toggle; admin can disable per user.

### 4.3 Playback
1. **Item detail**: the decorated `IMediaSourceManager.GetStaticMediaSources` detects Currents items (path under a plugin root), resolves the requesting user from `IHttpContextAccessor`, and calls `IStreamService.GetStreams(user, item)` — cached per (user, title) for `StreamCacheTtl` (default 1h), single-flight to absorb duplicate web-client calls.
2. **Mapping** each ranked stream to `MediaSourceInfo`:
   - `Id`: deterministic GUID from (itemId, stream identity — infoHash+fileIdx, else filename+size, else url hash).
   - `Name`: e.g. `2160p DV · Atmos · 18.4 GB · cached`.
   - `MediaStreams`: pre-filled from `parsedFile` (+ RemuxDB, §5.5).
   - `Path`: signed internal resolve URL `{InternalBaseUrl}/Currents/play/s/{token}`; `Protocol=Http`; `SupportsDirectPlay=false`, `SupportsDirectStream=true`, `SupportsTranscoding=true` so clients always stream through Jellyfin. (Exact flags confirmed in M0.)
3. **PlaybackInfo**: decorated `GetPlaybackMediaSources` returns the same list; probes only the chosen source when track info is insufficient.
4. **Stream**: Jellyfin fetches the resolve URL → plugin validates token, gets the AIOStreams playback URL from cache (re-searches if missing/expired), follows redirects, detects placeholders, fails over to next-ranked stream (max `FailoverAttempts`, default 3), then 302s to the final URL — or proxies when the stream requires headers.
5. **Watch state** is recorded on the base `.strm` item (one item, no per-version split).

### 4.4 Degraded mode
`.strm` resolve URLs carry no user. If the decorator is disabled (manually or by the compat guard), playing a title uses the global default config with auto-selection. Titles, metadata, and watch state are unaffected.

## 5. Features

### 5.1 Subtitles
`ISubtitleProvider` queries AIOStreams `subtitles` for the title (user from HTTP context when available, else default config). Stream-embedded `subtitles` become external tracks on that version, served via a plugin proxy route so URLs stay hidden. Error entries are filtered.

### 5.2 Skip intro / credits
`IMediaSegmentProvider` (incl. `CleanupExtractedData`) sourcing markers from AIOMetadata. Applied only when the playing version's runtime is within tolerance (default ±2%) of the reference runtime. **M0 confirms the data source; if none is usable, this feature is dropped from v1 and the spec is amended.**

### 5.3 Trailers
AIOMetadata `trailers` → item `RemoteTrailers` via the metadata provider.

### 5.4 Smart selection
Pure `IStreamRanker`:
1. Drop AIOStreams `error`/`statistic` entries and anything without a `url`.
2. Apply user filters: cached-only, max size, excluded resolutions/visual tags.
3. Stable re-rank by user preferences (resolution order, HDR/DV preference, audio/subtitle languages); ties keep AIOStreams' order (respects the user's AIOStreams sort rules).
Auto-select mode exposes only the top version. Placeholder detection at resolve: final URL path under `/static/` or matching known placeholder filenames.

### 5.5 Probing / pre-fill
Map `parsedFile` → `MediaStream`s (video codec, resolution, HDR type, audio codec/channels/languages, subtitle tracks). Optional RemuxDB lookup by infoHash/NZB for full track lists and runtime (configurable, on by default). ffprobe only on the selected version when needed, with raised `probesize`/`analyzeduration` (defaults 40M/5M), cached by stream identity.

### 5.6 Collections
Per catalog, optional Jellyfin BoxSet kept in sync with catalog membership each run.

### 5.7 Upgrade safety
- Tasks: **Verify library** (rebuild missing files from state), **Purge Currents content**, **Clear stream cache**.
- **Compat guard** at startup: if the server version is outside the tested range (`[12.0, 13.0)` initially), the decorator is not registered (degraded mode) and an admin warning is shown; admin can force-enable.

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
| **M0 Spike** | Throwaway proofs on 12.1: synthetic version IDs through item DTO → PlaybackInfo → direct stream + transcode; `ISearchProvider` remote results; AIOMetadata segment data | Written findings; spec amended if any assumption fails (user informed before continuing) |
| **M1 Foundation & core** | Repo scaffolding, CI, clients, catalog sync, `.strm`/`.nfo`, metadata provider, resolve endpoint (degraded mode), admin page | Titles appear and play via default config |
| **M2 Versions & users** | Decorator, user store + precedence, self-service page, ranker, failover, placeholder detection | Per-user versions in dropdown; isolation test passes |
| **M3 Search** | Search + auto-add | Search result opens as library item |
| **M4 Media** | Pre-fill/RemuxDB/probing, subtitles, trailers | Tracks shown before playback; subtitles searchable |
| **M5 Extras** | Segments, collections, maintenance tasks, compat guard, diagnostics | Tasks run; guard verified on fake version |
| **M6 Release** | Release pipeline, manifest, docs, client matrix pass | v1.0.0 installable from repo URL |

## 11. Risks

| Risk | Mitigation |
|---|---|
| Synthetic (non-persisted) version IDs rejected by some Jellyfin path | M0 spike; fallback: persist version rows only (Gelato-style), keeping `.strm` base items |
| Decorator breaks on Jellyfin upgrade | Isolated in `Integration/`, contract tests, compat guard → degraded mode |
| Clients render versions differently | Auto-select mode; client matrix; document per-client behavior |
| AIOStreams rate limits with many users | Global throttle, per-user cache, docs for self-hosted limit tuning |
| Search auto-add clutter | `addedBySearch` tag, per-user toggle, purge-by-tag |
| Segment data unavailable | Feature dropped from v1 after M0 |

## 12. Out of scope (v1)
P2P/torrent streams without debrid; Live TV; tracker sync (Trakt/Simkl — use existing Jellyfin plugins); non-AIOStreams stream backends; per-user catalogs; editing AIOStreams/AIOMetadata configs from Jellyfin.
