# Configuration

All Currents settings are on one page: Dashboard → Plugins → **Currents**. Change a setting, then press **Save** at
the bottom of the page. Settings apply at once, without a restart, unless a row says otherwise.

The sections below follow the page from top to bottom. The last table lists the few settings that are not on the
page. The name in brackets is the setting's name in the plugin's settings file.

## Catalogs (AIOMetadata)

| Setting (page label) | Default | What it does |
|---|---|---|
| **AIOMetadata manifest URL** (`AioMetadataManifestUrl`) | empty | Your AIOMetadata manifest URL, in the form `https://<host>/stremio/<uuid>/manifest.json`. Catalogs, search results, titles, posters and descriptions all come from it. |
| Catalog table (`Catalogs`) | no catalog ticked | **Load catalogs** lists the catalogs of your AIOMetadata configuration. Each row has **Sync** (sync this catalog), **Library** (Movies or Shows), **Max items** (default 100) and **Collection**. Changes apply on the next sync. |

- Unticking **Sync** removes the catalog's titles after the number of syncs set in **Remove a title after this many
  syncs without it** (see [Library](#library)).
- Titles that someone has watched are never removed. Titles added from search are never removed either.
- If no catalog is ticked at all, sync removes nothing and keeps every title.
- **Collection** keeps a Jellyfin collection of the catalog's titles, in catalog order.
  - The first collection creates Jellyfin's Collections library. That runs one library scan.
  - Currents removes only its own titles from a collection. Items you added by hand stay.
  - Unticking **Collection** leaves the collection in place.

## Search (AIOMetadata)

| Setting (page label) | Default | What it does |
|---|---|---|
| **Show AIOMetadata titles in Jellyfin search, and add them when opened** (`EnableSearch`) | on | Jellyfin search also lists AIOMetadata titles that are not in your library yet. Opening one adds it to the library. Off turns search results off for everyone. |
| **On for users who have not chosen** (`DefaultSearchAutoAdd`) | on | Whether a user who has not set their own search switch sees and can add these results. |
| Search catalog table (`SearchCatalogs`) | `search.movie` (Movies, 20 results) and `search.series` (Shows, 20 results) ticked | **Load search catalogs** lists the search catalogs of your AIOMetadata configuration (movies, series, anime variants). Each row has **Use**, **Library** and **Results** (1 to 100). |

How search works:
- Results show in the Movies and Shows rows of Jellyfin's search, with a poster.
- The server fetches the posters. Poster URLs never reach clients.
- Opening a result adds the title to the Currents Movies or Shows folder and shows the real item, ready to play.
- A series gets its seasons and episodes within a minute.
- Titles added by search are never removed. A series added by search gets new episodes on every catalog sync.
- A title that was already in the library from a catalog stays a catalog title when you open it from search. It can
  still be removed when it leaves its catalog.
- If a title is already in your library, opening the result shows that item instead of adding a copy. This covers
  Currents titles and your own files with the same IMDb, TMDB or TVDB id.

Who sees results:
- The admin's per-user **Search add off** (see [Users](#users)) wins over the user's own choice. The user's own
  choice wins over **On for users who have not chosen**.
- A user with search-add off sees no AIOMetadata results and cannot add titles.
- Users with parental controls never see AIOMetadata results and cannot add titles, whatever their switch says. This
  covers a maximum parental rating, blocked unrated items, and blocked or allowed tags. Remote results carry no rating
  or tags that Jellyfin could filter on.
- The Currents Movies and Shows folders must be in Movies and Shows libraries the user can see.
- A search term must have at least 2 characters. Only the first page of results gets AIOMetadata titles.

Which clients:
- Search covers the main `GET /Items` search used by Jellyfin Web, Android TV, Swiftfin and Findroid.
- Clients that use Jellyfin's older search-hints API (`/Search/Hints`, used by some third-party apps) get the same
  results under the same rules.

## Streams (AIOStreams)

| Setting (page label) | Default | What it does |
|---|---|---|
| **Default AIOStreams manifest URL** (`AioStreamsManifestUrl`) | empty | The AIOStreams config used by every user who has no config of their own and none assigned by you. The form is `https://<host>/stremio/<uuid>/<password>/manifest.json`. It contains your AIOStreams config password: treat it as a secret. Leave it empty if every user brings their own. **Test connection** checks it. |

## Media

| Setting (page label) | Default | What it does |
|---|---|---|
| **Subtitles from AIOStreams: a stream's own subtitles become tracks, and Jellyfin's subtitle search also asks AIOStreams** (`EnableSubtitles`) | on | Off: streams' own subtitles are not offered, and Jellyfin's subtitle search does not ask AIOStreams. |
| **Hide built-in text subtitles on files larger than (GB, 0 = never hide)** (`EmbeddedSubtitleMaxGb`) | 15 | See [Built-in subtitles on large files](#built-in-subtitles-on-large-files). |
| **Show full track lists from RemuxDB before playback** (`EnableRemuxDb`) | on | Looks up a file's full audio and subtitle list on RemuxDB, a public, crowd-sourced database of probed files. |
| **RemuxDB server** (`RemuxDbUrl`) | `https://remuxdb.1632022.xyz` | The RemuxDB server to ask. |

Tracks and subtitles in more detail:
- **Before playback.** The details page lists every audio and subtitle track of a version, by language name. You can
  choose before you press Play.
  - Tracks come from RemuxDB when it knows the exact file.
  - Otherwise they come from AIOStreams' media info, else from the release name.
- **First play.** The first play of a file with several tracks checks the file once (a few seconds). That makes sure
  the track you chose is the one that plays. The result is kept for 30 days, also across restarts.
- **RemuxDB privacy.** Currents sends RemuxDB only the title's IMDb or TMDB id (with season and episode) and an
  anonymous client id derived from your install. It never sends your AIOStreams config or any stream URL. If RemuxDB
  is slow or down, the tracks from the release name show.
- **Subtitle search.** On a title, open the three-dot menu, then **Edit subtitles**, then **Search**.
  - Results named `AIOStreams n (lang)` need a subtitle addon (for example OpenSubtitles) in the user's AIOStreams
    config.
  - A downloaded subtitle is saved next to the title's `.strm` file and shows in every version.
- **Stream subtitles.** Subtitles that a stream itself carries appear in the Subtitles menu as "External" tracks.
- **Trailers.** AIOMetadata trailers show as the Trailer button. Titles added before 0.4.0 get theirs after a
  metadata refresh.
- **Streams that need request headers** (usenet WebDAV, Google Drive) play through the server. In degraded mode
  (versions off) Currents skips them.

## Skip markers

| Setting (page label) | Default | What it does |
|---|---|---|
| **Fetch skip-intro and credits markers from TheIntroDB and AniSkip (and PublicMetaDB with a key)** (`EnableSegments`) | on | Gets markers from TheIntroDB and AniSkip (anime), and from PublicMetaDB with a key. Jellyfin then shows "Skip intro" and "Skip credits". |
| **Allowed length difference (%)** (`SegmentTolerancePercent`) | 2 | How far a version's length may differ from the length the markers were made for. Allowed range 1 to 10. A longer or shorter cut would put the markers in the wrong place. |
| **Also offer markers when a version's length is unknown** (`SegmentsWhenRuntimeUnknown`) | off | Offers markers on versions whose length Currents does not know, and in degraded mode. |
| **TheIntroDB API key (optional)** (`TheIntroDbApiKey`) | empty | Without a key TheIntroDB allows about 500 lookups a day; with one, 1000. |
| **PublicMetaDB API key (optional)** (`PublicMetaDbApiKey`) | empty | Turns PublicMetaDB on for titles with a TMDB id. Empty skips PublicMetaDB. |

- Jellyfin keeps one set of markers per title, but each version is a different file. So a version gets markers only
  when its length is within the allowed difference.
- Markers are fetched by Jellyfin's "Media Segment Scan" task and by Currents' "Fetch skip markers" task, never
  while you play. A sync that adds titles queues "Fetch skip markers".
- A big library gains markers for roughly 500 new titles a day without a TheIntroDB key (1000 with one).
- Titles without markers are asked again about weekly. Titles with markers are asked again about monthly.

## Versions

| Setting (page label) | Default | What it does |
|---|---|---|
| **Show each stream as a version (off: every title plays the default config's best stream)** (`EnableVersions`) | on | Each stream shows as an entry in Jellyfin's **Version** menu, per user. Off is degraded mode: every title plays the default config's best stream through its `.strm` file. |
| **Run on this untested Jellyfin version** (`ForceEnableOnUntestedServer`) | off | Currents is tested with Jellyfin 12.1 up to (not including) 13.0. On another version it turns per-user versions off until you tick this. |
| **Let users set their own AIOStreams config and preferences** (`AllowSelfService`) | on | Turns the user page on. Off: users cannot change anything there, and their saved settings are not used. See [self-service.md](self-service.md). |
| **By default show only the best stream** (`DefaultAutoSelect`) | off | The Version menu shows only the top-ranked stream, for users who have not chosen. |
| **Most versions per title** (`MaxVersions`) | 20 | The most streams listed in the Version menu (1 to 50). |
| **Keep stream lists for (minutes)** (`StreamCacheMinutes`) | 60 | How long a title's stream list is reused before AIOStreams is searched again. The "Clear stream cache" task empties it at once. |
| **Default preferences** (`DefaultPreferences`) | no preferences | The ranking and filters for users who have not set their own. See the next table. |

**Default preferences.** Lists are comma-separated and in order of preference. Users and per-user overrides can
change them.

| Setting (page label) | Default | What it does |
|---|---|---|
| **Resolutions** | empty | Preferred resolutions in order, for example `2160p, 1080p`. Unlisted ones rank after listed ones. |
| **HDR / Dolby Vision** | No preference | **Prefer HDR** or **Avoid HDR**. |
| **Audio languages** | empty | Preferred audio languages in order, as AIOStreams names them, for example `English, Japanese`. |
| **Subtitle languages** | empty | Preferred subtitle languages in order. |
| **Largest file in GB (0 = no limit)** | 0 | Hides bigger streams. |
| **Never show these resolutions** | empty | Hides these resolutions. |
| **Never show these tags (e.g. 3D)** | empty | Hides streams with these visual tags. |
| **Only cached debrid streams** | off | Hides uncached debrid streams. Streams without a cache state stay. |

Streams that tie keep AIOStreams' own order.

## Users

One row per Jellyfin user. Each row has its own **Save** button. These settings are kept per user, not in the plugin
settings file.

| Column | Default | What it does |
|---|---|---|
| **Using** | — | Which AIOStreams config the user gets: their own (with its host), your override (with its host), the server default, or none. |
| **Override AIOStreams URL** | none | An AIOStreams manifest URL for this user only. It is checked with AIOStreams before saving and never shown again, only its host. Paste a new one to replace it; enter `-` to remove it. |
| **Preferences** | default | A summary of the preferences you assigned to this user. |
| **Show only best** | Inherit | **Only best** or **Show all** for this user, or **Inherit** the user's own choice or the default. An admin choice applies unless the user set their own choice and is not locked, as with the URL override. |
| **Lock** | off | The user cannot change their settings on the user page, and their own saved settings are not used. |
| **Streams off** | off | The user gets no streams at all. |
| **Search add off** | off | The user sees no AIOMetadata search results and cannot add titles. |

Each setting comes from the first of these that has a value: the user's own setting (when self-service is allowed and
the user is not locked), your per-user override, the server default. A user with none of these has no streams.

## Library

| Setting (page label) | Default | What it does |
|---|---|---|
| **Library root (leave empty for the plugin data folder)** (`LibraryRoot`) | empty | The folder that holds `Movies/` and `Shows/`. See [Library folders and Docker](#library-folders-and-docker). Currents writes into a new root on the next sync; it does not move titles already written. |
| **Jellyfin address written into .strm files** (`StrmBaseUrl`) | `http://127.0.0.1:8096` | Used only in degraded mode (versions off). See [StrmBaseUrl and degraded mode](#strmbaseurl-and-degraded-mode). Changing it rewrites the `.strm` files on the next sync. |
| **Remove a title after this many syncs without it** (`PruneAfterMisses`) | 3 | A catalog title that is missing from its catalog this many syncs in a row is removed, unless someone watched it. |
| **Streams to try before giving up** (`FailoverAttempts`) | 3 | When a stream link is dead or a placeholder, Currents tries the next-ranked stream, up to this many (1 to 10). |

## Diagnostics

This section has no settings.
- The first line says whether versions are active or degraded, and whether skip markers and the marker keys are set.
- It shows cache hit rates for stream lists and skip markers, and how many files were checked.
- **Recent problems** lists the last 50 problems. They are kept in memory and cleared on restart.
- **Run connection tests** checks AIOStreams, AIOMetadata, RemuxDB and each marker source. A source without a key
  shows `off`.

## Settings not on the page

These are in the plugin's settings file, `plugins/configurations/Jellyfin.Plugin.Currents.xml` in Jellyfin's data
folder. To change one, stop Jellyfin, edit the file, and start Jellyfin again.

| Setting | Default | What it does |
|---|---|---|
| `SigningSecret` | generated on first start | Signs the `.strm` files, version links and search ids. Keep it when you move or restore the server: a new secret makes existing titles unrecognized. Catalog titles come back when the next sync rewrites their `.strm` files, but titles added by search stay unrecognized until you purge and re-add them (a sync refreshes only search-added series, never movies), and every search id changes. Keep a copy of the secret when you move servers. Treat it as a secret. |
| `VersionTokenHours` | 24 | How long a version link stays valid for Jellyfin's own player (at least 1 hour). |
| `AioStreamsPermitsPer10Seconds` | 5 | The most AIOStreams requests in 10 seconds, for all users together. Needs a restart. |
| `AioMetadataPermitsPer5Seconds` | 15 | The most AIOMetadata requests in 5 seconds. Needs a restart. |

## Library folders and Docker

- Currents writes each title as a folder with a `.strm` file and an `.nfo` file.
- Movies go under `Movies/` and series under `Shows/`, both inside the **Library root**.
- With **Library root** empty, the root is the `library` folder inside Currents' data folder. That is
  `plugins/Jellyfin.Plugin.Currents/library` in Jellyfin's data folder. In the official Docker image it is
  `/config/plugins/Jellyfin.Plugin.Currents/library`.
- The settings page shows the two exact folders under **Library**. Add `Movies` as a Movies library and `Shows` as a
  Shows library.
- Jellyfin itself must see the folder. With Docker, use a path inside the container, inside a mounted volume. The
  default folder is inside Jellyfin's own data folder, so Jellyfin always sees it.

Currents only manages title folders that carry its `.currents` marker file:
- **Writing.** A new title folder gets the marker.
  - An existing folder without the marker is taken over only when it holds nothing but files Currents writes itself:
    `*.strm`, `movie.nfo`, `tvshow.nfo`, subtitles Jellyfin saved for one of those `.strm` files, and `Season NN` or
    `Specials` folders that hold only `*.strm` files and such subtitles.
  - Any other folder is left untouched. The title is skipped with a warning in the log. This holds even for the folder
    recorded for that title (for example after **Library root** was pointed at a real media folder).
- **Removing.** An unmarked folder is never touched.
  - In a marked folder Currents deletes only the files it writes, and those subtitles.
  - The marker is removed last, together with the folder, and only when nothing else is left.
  - If the folder still holds other files (posters, season NFOs Jellyfin saved there, your own files), those files,
    the marker and the folder all stay. The title can then be written into the same folder again if it returns to a
    catalog.

## StrmBaseUrl and degraded mode

- With versions on, Jellyfin's own ffmpeg reaches streams through an internal loopback address. `StrmBaseUrl` is not
  used.
- It matters only in **degraded mode** (**Show each stream as a version** off). Then every title plays the server
  default config through its `.strm` file.
- In degraded mode, set **Jellyfin address written into .strm files** to an address that **both your clients and the
  Jellyfin server itself** can reach.
  - Normally that is the server's LAN address, for example `http://192.168.x.y:8096`, or your public URL.
  - Include Jellyfin's base URL path if one is set, for example `http://192.168.x.y:8096/jellyfin`.
- `localhost` and `127.0.0.1` work for neither in common setups. Inside a Docker container they do not reach the
  published host port. On a remote client they point at the client itself.
- In degraded mode, clients that direct-play may follow the redirect to the final stream URL themselves, instead of
  streaming through Jellyfin. See `docs/spikes/2026-10-m0-findings.md` (S4) and [architecture.md](architecture.md).
- Streams that need request headers do not play in degraded mode.

## Reverse proxies

If Jellyfin sits behind a reverse proxy on the same host, add the proxy to Dashboard → Networking → **Known proxies**.
Otherwise every request looks local to Jellyfin. Currents' internal stream endpoint is then protected only by its
signed, expiring token (see [architecture.md](architecture.md)).

## Built-in subtitles on large files

Why there is a limit:
- Before Jellyfin can show a subtitle that is built into a file, it reads the whole file once.
- For a remote 45 GB remux that takes about 20 minutes, while the player waits.

What the limit does:
- **Hide built-in text subtitles on files larger than** is 15 GB by default (1 GB = 1,000,000,000 bytes).
- On a version larger than that, Currents hides the file's built-in text subtitles and refuses requests for them.
- 0 never hides them. A version whose size is unknown is never limited.
- The limit applies only while versions are on and Currents is active on this Jellyfin version. In degraded mode (versions off, or an untested Jellyfin not forced on) Jellyfin plays the `.strm` itself and the long wait can come back.

What stays on large files:
- subtitles that AIOStreams lists for the stream;
- subtitles you downloaded with Jellyfin's subtitle search;
- picture-based subtitles (PGS and similar). The server burns these in, which does not read the whole file first. A client that is set to draw PGS itself (jellyfin-web: Settings → Subtitles → render PGS) fetches the track as a file and gets nothing on these files, so turn that setting off.

A rare case: some files have built-in text subtitles that are not at the end of the file's track list (for example
before an audio track or cover art). Those stay in the player's track list, so the audio you picked still plays. A
request for one of them is still refused. Only forcing such a subtitle to be burned in would read the whole file.

## Network safety

- **Posters, subtitles and artwork.** Currents downloads search posters, stream subtitles and the library artwork its
  metadata providers fetch only from public internet addresses. It refuses loopback, private (LAN), link-local and
  other non-public addresses, also after a redirect. (Images an admin picks by hand in Jellyfin's image editor are
  downloaded by Jellyfin itself.)
- **Your own servers.** One exception: the exact host **and port** of the two manifest URLs on this page (AIOStreams
  and AIOMetadata). That keeps a self-hosted AIOStreams or AIOMetadata on your LAN working. Only those two URLs are
  exempt: a per-user **Override AIOStreams URL** and users' own URLs are not.
  - A URL without a port counts as port 443 for `https` and 80 for `http`.
  - Images or subtitles served from another port, or another host, on a private network are refused.
  - A user's own AIOStreams URL from the user page is not exempt.
- **Outbound HTTP proxy.** If Jellyfin's outbound traffic goes through an HTTP proxy (for example set with the
  `HTTP_PROXY` or `HTTPS_PROXY` environment variables), the proxy decides where these downloads may go.
- **Stream redirects.** When a stream redirects to another site, only harmless request headers go with it:
  `User-Agent`, `Referer`, `Origin`, `Accept` and `Accept-Language`. Anything that could be a credential stays behind.

## Scheduled tasks

Dashboard → Scheduled Tasks, under "Currents":
- **Sync AIOMetadata catalogs**: the catalog sync, every 6 hours and on **Sync now**.
- **Fetch skip markers**: finds markers for every Currents title. Manual; a sync that adds titles queues it.
- **Clear stream cache**: forgets cached stream lists, so the next open searches AIOStreams again.
- **Verify library**: rewrites titles whose files are missing, and drops saved settings of deleted Jellyfin users.
- **Purge Currents content**: removes every Currents title (files and their Jellyfin entries), the sync state, and
  the file-check (probe) and skip-marker caches. The next sync writes the enabled catalogs again. Use it to start over.
