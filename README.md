# Currents — AIOStreams & AIOMetadata for Jellyfin

Currents is a Jellyfin 12 plugin. It syncs catalogs from [AIOMetadata](https://github.com/cedya77/aiometadata)
into your library as real titles and plays them through [AIOStreams](https://github.com/Viren070/AIOStreams).

> Status: early development. See `docs/superpowers/specs/2026-10-01-currents-design.md` for the design.

## Requirements
- Jellyfin 12.0 or newer (built and tested against 12.1; 12.0 is untested)
- A self-hosted (or hosted) AIOStreams instance with a debrid service configured
- An AIOMetadata instance and a saved configuration

## Setup
1. Install the plugin: Dashboard -> Plugins -> Repositories -> **+**, name it `Currents`, URL
   `https://survivalizer.github.io/jellyfin-plugin-currents/manifest.json`. Then Dashboard -> Plugins -> Catalog ->
   **Currents** -> Install, and restart Jellyfin. (Developers can also build with `dev/deploy-plugin.sh`.)
2. Dashboard -> Plugins -> Currents:
   - paste your AIOMetadata manifest URL, click **Load catalogs**, tick catalogs, choose Movies/Shows;
   - paste your AIOStreams manifest URL and click **Test connection** (this is the default for every user; leave it
     empty if every user brings their own);
   - **Save**, then **Sync now**.
   Unticking a catalog removes its unwatched titles after the configured number of syncs (`PruneAfterMisses`,
   default 3). If no catalog is ticked at all, sync skips pruning and keeps every title.
3. Add the two folders shown on the settings page as a Movies library and a Shows library.
   In each library's settings enable the **Currents (AIOMetadata)** metadata and image fetchers and the NFO reader.

## Search
Search in Jellyfin also shows matching titles from AIOMetadata that are not in your library yet, in the Movies and
Shows results with a poster. Opening one adds it to the Currents Movies/Shows folder and shows the real item,
ready to play with your versions; a series gets its seasons and episodes within a minute. Titles added by search are
kept: they are never pruned, and a series receives new episodes on each catalog sync. (A title that was already in
the library from a synced catalog and is only opened from search stays a catalog title and can still be pruned.) If a title is already in
your library (a Currents title or your own file with the same IMDb, TMDB or TVDB id), opening it shows that item
instead of adding a copy. Posters are fetched by the server; poster URLs never reach clients.

Settings (Dashboard -> Plugins -> Currents):
- **Show AIOMetadata titles in Jellyfin search** (`EnableSearch`, default on) and **default for users**
  (`DefaultSearchAutoAdd`, default on).
- **Search catalogs**: **Load search catalogs** lists your AIOMetadata search catalogs (`search.movie`,
  `search.series`, anime variants); the movie and series ones are ticked by default.
- Per user: **Search add off** (admin) turns it off for one account.

Each user has one switch on `/Currents/user` (when self-service is allowed and the user is not locked). The
admin's per-user off wins over the user's choice, which wins over the default. A user with it off sees no
AIOMetadata results and cannot add titles.

Users with parental controls (a maximum parental rating, blocked unrated items, or blocked or allowed tags) never see
AIOMetadata results and cannot add titles, whatever their switch says: remote results carry no rating or tags that
Jellyfin could filter on.

The Currents Movies and Shows folders must be in Movies/Shows libraries the user can see. Search covers the main
`GET /Items` search used by Jellyfin Web, Android TV, Swiftfin and Findroid, not the legacy `/Search/Hints`.

## Versions: every stream in the Version menu
Opening a Currents title searches AIOStreams with **that user's** config and lists every stream as an entry in
Jellyfin's native **Version** dropdown, named like `2160p DV · Atmos · 18.4 GB · cached` and ranked by the user's
preferences (cached only, maximum size, excluded resolutions/visual tags, resolution order, HDR/DV, audio and
subtitle languages; ties keep AIOStreams' own order). Pick a version and press Play; Jellyfin always streams it
through itself (remux or transcode), fails over to the next-ranked stream if a link is dead or a placeholder, and
records watch state and resume on the title, not the version. Two users see their own versions and never each
other's streams or credentials. **Show only the best stream** (auto-select) reduces the menu to the top stream.

Each user's settings come from, field by field: their own self-service settings (if self-service is allowed and the
admin has not locked them) -> an admin per-user override -> the server default -> nothing (titles then show a single
"Streams are not configured" version). Admin settings live under **Versions** and **Users** on the plugin page:
`EnableVersions` (default on), `AllowSelfService` (default on), default preferences, `MaxVersions` (20),
`StreamCacheMinutes` (60), `VersionTokenHours` (24), and per user an override config, preferences, auto-select
(Inherit/Only best/Show all), lock self-service, and streams disabled.

## Tracks, subtitles and trailers
- **Before playback.** The details page lists every audio and subtitle track a version has, labeled by language name,
  so you can choose before you press Play. Tracks come from RemuxDB when it knows the exact file, else from AIOStreams'
  media info, else from the release name.
- **First play.** The first play of a multi-track file probes it once (a few seconds) so your chosen track is the one that
  plays. The result is kept for 30 days, also across restarts.
- **RemuxDB.** Under **Media** on the plugin page you can switch RemuxDB off or change its server. Currents sends it
  only the title's IMDb or TMDB id (with the season and episode) and an anonymous client id derived from your install; it
  never sends your AIOStreams config or any stream URL. If RemuxDB is slow or down, the release-name tracks show.
- **Subtitle search.** On a title, use the three-dot menu, **Edit subtitles**, then **Search**. Results named
  `AIOStreams n (lang)` need a subtitle addon (for example OpenSubtitles) in the user's AIOStreams config. A downloaded
  subtitle is saved next to the title's `.strm` and shows in every version.
- **Stream subtitles.** Subtitles that a stream itself carries appear in the Subtitles select as "External" tracks.
- **Trailers.** AIOMetadata trailers show as the Trailer button. Titles added before 0.4.0 get theirs after a metadata
  refresh.
- **Header-bound streams.** Streams that need request headers (usenet WebDAV, Google Drive) now play through the server.
  Degraded `.strm` playback (versions off) still skips them.

### Self-service page
Users manage their own AIOStreams manifest URL and preferences at **`/Currents/user`** on your Jellyfin address
(for example `http://192.168.x.y:8096/Currents/user`, including Jellyfin's base URL path if you set one); the admin
page shows the exact URL. The saved manifest URL is never shown again, only its host. Saving a URL makes the
Jellyfin server contact that address to validate it.

Jellyfin has no plugin API for a user-menu entry. To add one, edit jellyfin-web's `config.json` and add:
```json
"menuLinks": [{ "name": "Currents", "icon": "tune", "url": "/Currents/user" }]
```
If Jellyfin has a base URL path, include it in `url` (for example `"/jellyfin/Currents/user"`).
In the official Docker image the file is `/jellyfin/jellyfin-web/config.json`. Jellyfin overwrites it on every
upgrade, so keep a copy (or mount your own file over it).

### `StrmBaseUrl` and degraded mode
With versions on, Jellyfin's own ffmpeg reaches streams through an internal loopback URL and `StrmBaseUrl` is not
used. It matters only in **degraded mode** (`EnableVersions` off), where every title plays the server default config
via its `.strm` file. Then set *Jellyfin address written into .strm files* (`StrmBaseUrl`) to an address that
**both your clients and the Jellyfin server itself** can reach, normally the server's LAN address, e.g.
`http://192.168.x.y:8096` (or your public URL), including Jellyfin's base URL path if one is configured (e.g.
`http://192.168.x.y:8096/jellyfin`). `localhost`/`127.0.0.1` works for neither case in common setups: inside a Docker
container it does not reach the published host port, and on a remote client it points at the client itself. In
degraded mode clients that direct-play may also follow the redirect to the final stream URL themselves rather than
streaming through Jellyfin (see `docs/spikes/2026-10-m0-findings.md`, S4, and `docs/architecture.md`).

### Reverse proxies
If Jellyfin sits behind a reverse proxy on the same host, add the proxy to Dashboard -> Networking -> **Known
proxies**. Otherwise every request looks local to Jellyfin and the internal stream endpoint is protected only by its
signed, expiring token (see `docs/architecture.md`).

Currents only manages title folders that carry its `.currents` marker file:
- **Writing**: a new title folder gets the marker. An existing folder without the marker is adopted only when it
  contains nothing but files Currents writes itself (`*.strm`, `movie.nfo`, `tvshow.nfo`, and `Season NN`/`Specials`
  folders holding only `*.strm`); any other folder is left untouched and the title is skipped with a warning, even
  if it is the folder recorded for that title (for example after `LibraryRoot` was pointed at a real media folder).
- **Pruning**: an unmarked folder is never touched. In a marked folder Currents deletes only the files it writes;
  the marker is removed last, together with the folder, and only when nothing else is left. If the folder still
  holds other files (posters, subtitles, season NFOs Jellyfin saved there, your own files), those files, the marker
  and the folder all stay, so the title can be written into the same folder again if it returns to a catalog.

## Skip markers, collections and maintenance
- **Skip markers.** Jellyfin's "Skip intro" and "Skip credits" prompts work on Currents titles. Markers come from
  TheIntroDB, AniSkip (anime) and, with a key, PublicMetaDB. Jellyfin keeps one set of markers per title, but each
  version is a different file, so Currents offers markers only on versions whose length is within the allowed
  difference of the length the markers were made for. Other versions get none.
  Settings under **Skip markers**: on or off (default on), *Allowed length difference* (default 2 %, 1-10 %),
  *Also offer markers when a version's length is unknown* (default off), and optional TheIntroDB and PublicMetaDB
  keys. TheIntroDB works without a key but limits anonymous use to about 500 requests a day (1000 with a key), so a big
  library gains markers for roughly that many new titles a day. Titles with no markers are asked again about weekly,
  titles with markers about monthly. Markers are fetched by Jellyfin's "Media Segment Scan" task and by "Fetch skip markers", never while
  you play.
- **Collections.** Tick **Collection** on a catalog to keep a Jellyfin collection of its titles, in catalog order.
  The first one creates Jellyfin's Collections library and runs one library scan. Currents removes only its own titles
  from a collection; items you added stay. Unticking Collection leaves the collection in place.
- **Tasks.** Dashboard -> Scheduled Tasks, under "Currents":
  - *Sync AIOMetadata catalogs*: the catalog sync.
  - *Fetch skip markers*: finds markers for every Currents title (manual; a sync that adds titles queues it).
  - *Clear stream cache*: forgets cached stream lists, so the next open searches AIOStreams again.
  - *Verify library*: rewrites titles whose files are missing and drops saved settings of deleted Jellyfin users.
  - *Purge Currents content*: removes every Currents title (files and their Jellyfin entries), the sync state, and
    the probe and skip-marker caches.
    The next sync writes the enabled catalogs again. Use it to start over.
- **Untested Jellyfin versions.** Currents was tested on Jellyfin 12.x. On another version it stands down: titles play
  through their `.strm` files, a warning appears in the Dashboard activity log and a banner on the plugin page. Tick
  **Run on this untested Jellyfin version** to turn Currents on anyway; no restart is needed.
- **Diagnostics.** On the plugin page, **Run connection tests** checks AIOStreams, AIOMetadata, RemuxDB
  and each marker source (a source without a key shows `off`). The panel also shows whether versions are active or
  degraded, cache hit rates for stream lists and skip markers, and the last 50 problems (kept in memory, cleared on
  restart).

Upgrading to 0.5.0: skip markers are on by default. The first run of "Fetch skip markers" (queued after the next sync
that adds titles) or Jellyfin's "Media Segment Scan" fills them in. Turn them off under **Skip markers**.

## Development
See [`dev/README.md`](dev/README.md), [`CONTRIBUTING.md`](CONTRIBUTING.md) and [`docs/architecture.md`](docs/architecture.md).

## License
GPL-3.0-or-later. See [`LICENSE`](LICENSE).
