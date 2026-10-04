# Currents — AIOStreams & AIOMetadata for Jellyfin

Currents is a Jellyfin 12.1 plugin. It syncs catalogs from [AIOMetadata](https://github.com/cedya77/aiometadata)
into your library as real titles and plays them through [AIOStreams](https://github.com/Viren070/AIOStreams).

## Requirements
- Jellyfin 12.1 (Jellyfin 12.0 cannot install this version)
- A self-hosted (or hosted) AIOStreams instance with a debrid service and addons configured
- An AIOMetadata instance and a saved configuration

## Install
1. Dashboard → Plugins → Repositories → **+**. Name it `Currents`, with the URL
   `https://survivalizer.github.io/jellyfin-plugin-currents/manifest.json`.
2. Dashboard → Plugins → Catalog → **Currents** → Install.
3. Restart Jellyfin.
4. Check that **Currents** shows as **Active** under Dashboard → Plugins.

Developers can also build and deploy it with `dev/deploy-plugin.sh` (see [Development](#development)).

## Quick start: from install to first play
1. **AIOMetadata.** Save your configuration on AIOMetadata's configure page and copy the manifest URL it gives you. It
   looks like `https://<host>/stremio/<uuid>/manifest.json`. Paste it into **AIOMetadata manifest URL** on
   Dashboard → Plugins → Currents.
2. **Catalogs.** Press **Load catalogs**. Tick the catalogs you want and choose **Movies** or **Shows** for each.
3. **AIOStreams.** Save your configuration on AIOStreams' configure page and copy the manifest URL it gives you. It
   looks like `https://<host>/stremio/<uuid>/<password>/manifest.json`. It contains your config password, so treat it
   as a secret. Paste it into **Default AIOStreams manifest URL** and press **Test connection**.
   - This config is the default for every user. Leave it empty if every user brings their own.
4. **Sync.** Press **Save**, then **Sync now**. Watch the progress under Dashboard → Scheduled Tasks
   ("Sync AIOMetadata catalogs"). After that, sync runs every 6 hours.
5. **Libraries.** The settings page shows two folders under **Library**. For each one:
   - Dashboard → Libraries → **Add Media Library**;
   - content type **Movies** for the `Movies` folder, **Shows** for the `Shows` folder;
   - in the library's settings, enable the **Currents (AIOMetadata)** metadata and image fetchers and the **Nfo**
     metadata reader;
   - save, and let Jellyfin scan the library.
6. **Play.** Open a title, pick a stream from the **Version** menu and press Play. The first open of a title can take
   up to 10 seconds while Currents searches AIOStreams.

`StrmBaseUrl` (**Jellyfin address written into .strm files**) matters only if you turn versions off. See
[configuration.md](docs/configuration.md#strmbaseurl-and-degraded-mode).

Every setting is explained in [docs/configuration.md](docs/configuration.md).

## What you get

**Search.** Jellyfin search also shows matching AIOMetadata titles that are not in your library yet, with a poster.
Opening one adds it to the library and shows the real item, ready to play; a series gets its seasons and episodes
within a minute. Titles added by search are never pruned, and a series gets new episodes on every catalog sync. If the
title is already in your library (a Currents title or your own file with the same IMDb, TMDB or TVDB id), you get that
item instead of a copy. It covers the `GET /Items` search used by Jellyfin Web, Android TV, Swiftfin and Findroid,
and the older `/Search/Hints` API some third-party apps use. Users with parental controls never see these results. See
[configuration.md](docs/configuration.md#search-aiometadata).

**Versions.** Opening a title searches AIOStreams with **that user's** config. Every stream becomes an entry in
Jellyfin's **Version** menu, named like `2160p DV · Atmos · 18.4 GB · cached` and ranked by the user's preferences
(cached only, largest file, excluded resolutions or tags, resolution order, HDR/DV, audio and subtitle languages;
ties keep AIOStreams' own order). Jellyfin always streams the version through itself (remux or transcode). If a link
is dead or a placeholder, it moves on to the next-ranked stream. Watch state and resume are kept on the title, not the
version. Two users see their own versions and never each other's streams or credentials. **Only show the best
stream** reduces the menu to the top stream. See [configuration.md](docs/configuration.md#versions).

**Tracks, subtitles and trailers.** The details page lists each version's audio and subtitle tracks by language, from
RemuxDB, AIOStreams' media info or the release name, so you can choose before you press Play. A stream's own subtitles
and Jellyfin's subtitle search (results named `AIOStreams n (lang)`) both work. On files over 15 GB (the default
limit), built-in text subtitles are hidden, because Jellyfin would read the whole file before showing one (this applies only while versions are on and Currents is active; in degraded mode Jellyfin plays the `.strm` itself and the wait can come back). AIOMetadata
trailers show as the Trailer button. Streams that need request headers (usenet WebDAV, Google Drive) play through the
server. See [configuration.md](docs/configuration.md#media) and
[Built-in subtitles on large files](docs/configuration.md#built-in-subtitles-on-large-files).

**Skip markers, collections and maintenance.** Jellyfin's "Skip intro" and "Skip credits" prompts work on Currents
titles, with markers from TheIntroDB, AniSkip and (with a key) PublicMetaDB. A version gets markers only when its
length matches the length the markers were made for. Tick **Collection** on a catalog to keep a Jellyfin collection
of its titles. Scheduled tasks sync catalogs, fetch markers, clear the stream cache, verify the library and purge
everything Currents added. See [configuration.md](docs/configuration.md#skip-markers) and
[Scheduled tasks](docs/configuration.md#scheduled-tasks).

**The user page.** Each user can set their own AIOStreams config, stream preferences, "only the best stream" and
their search switch at `/Currents/user`. The admin can override, lock or turn off each user. See
[docs/self-service.md](docs/self-service.md).

## Clients
Currents titles are ordinary Jellyfin library items, and playback goes through Jellyfin. Which clients were tested,
and how they did, is in [docs/client-matrix.md](docs/client-matrix.md).

## Security and privacy
- Stream URLs and subtitle URLs never reach clients: clients play through Jellyfin. (In degraded mode, with versions
  off, a client may follow a `.strm` file's redirect to the stream itself.)
- Saved manifest URLs of users, and the admin's per-user overrides, are write-only: Currents shows only their host.
  The admin's own URLs and API keys are in Jellyfin's plugin settings, which only admins can read; the diagnostics
  panel shows only whether a key is set.
- Search-result posters, stream (AIOStreams) subtitles and library artwork go only to public internet addresses. The exception is the exact host and port of the
  admin's AIOStreams and AIOMetadata manifest URLs. See [Network safety](docs/configuration.md#network-safety).
- When a stream redirects to another site, only harmless request headers go with it.
- To report a vulnerability, see [SECURITY.md](SECURITY.md).

## Troubleshooting

| What you see | Why | What to do |
|---|---|---|
| Sync finished but no titles appear | No catalog ticked, wrong AIOMetadata URL, or the libraries do not exist yet | **Load catalogs**, tick, **Save**, **Sync now**; create the two libraries; check Dashboard → Logs |
| "No streams found for this title." | AIOStreams found nothing, or debrid is not set up in AIOStreams | **Test connection**; check the user's page; check AIOStreams itself |
| Titles have no poster or description | The library does not use the Currents metadata fetchers | Enable the **Currents (AIOMetadata)** fetchers and the **Nfo** reader; refresh metadata |

More in [docs/troubleshooting.md](docs/troubleshooting.md).

## Upgrading and uninstalling
- Currents 1.0 needs Jellyfin 12.1. Upgrade Jellyfin first.
- Upgrading to 1.0: it requires Jellyfin 12.1. Built-in text subtitles on files over 15 GB are hidden after the upgrade (set the limit to 0 to keep them).
- On Jellyfin 13 or newer, Currents stands down: titles play through their
  `.strm` files, and a warning appears in the Dashboard activity log and as a banner on the plugin page. Tick
  **Run on this untested Jellyfin version** to turn Currents on anyway; no restart is needed.
- Upgrading to 0.5.0: skip markers are on by default. The first run of "Fetch skip markers" (queued after the next
  sync that adds titles) or Jellyfin's "Media Segment Scan" fills them in. Turn them off under **Skip markers**.
- To uninstall:
  1. run the **Purge Currents content** task (Dashboard → Scheduled Tasks);
  2. uninstall Currents under Dashboard → Plugins and restart Jellyfin;
  3. remove the two Currents libraries.
  - To remove everything, also delete Currents' data folder (`plugins/Jellyfin.Plugin.Currents` in Jellyfin's data
    folder, which holds users' saved settings) and its settings file
    (`plugins/configurations/Jellyfin.Plugin.Currents.xml`), if they are still there.

## Development
See [`dev/README.md`](dev/README.md), [`CONTRIBUTING.md`](CONTRIBUTING.md) and [`docs/architecture.md`](docs/architecture.md).

## License
GPL-3.0-or-later. See [`LICENSE`](LICENSE).
