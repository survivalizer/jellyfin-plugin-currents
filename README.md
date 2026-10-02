# Currents — AIOStreams & AIOMetadata for Jellyfin

Currents is a Jellyfin 12 plugin. It syncs catalogs from [AIOMetadata](https://github.com/cedya77/aiometadata)
into your library as real titles and plays them through [AIOStreams](https://github.com/Viren070/AIOStreams).

> Status: early development. See `docs/superpowers/specs/2026-10-01-currents-design.md` for the design.

## Requirements
- Jellyfin 12.0 or newer (built and tested against 12.1; 12.0 is untested)
- A self-hosted (or hosted) AIOStreams instance with a debrid service configured
- An AIOMetadata instance and a saved configuration

## Setup (M1)
1. Install the plugin: Dashboard -> Plugins -> Repositories -> **+**, name it `Currents`, URL
   `https://survivalizer.github.io/jellyfin-plugin-currents/manifest.json`. Then Dashboard -> Plugins -> Catalog ->
   **Currents** -> Install, and restart Jellyfin. (Developers can also build with `dev/deploy-plugin.sh`.)
2. Dashboard -> Plugins -> Currents:
   - paste your AIOMetadata manifest URL, click **Load catalogs**, tick catalogs, choose Movies/Shows;
   - paste your AIOStreams manifest URL and click **Test connection**;
   - **Save**, then **Sync now**.
   Unticking a catalog removes its unwatched titles after the configured number of syncs (`PruneAfterMisses`,
   default 3). If no catalog is ticked at all, sync skips pruning and keeps every title.
3. Add the two folders shown on the settings page as a Movies library and a Shows library.
   In each library's settings enable the **Currents (AIOMetadata)** metadata and image fetchers and the NFO reader.
4. Set *Jellyfin address written into .strm files* (`StrmBaseUrl`) to an address that **both your clients and
   the Jellyfin server itself** can reach, normally the server's LAN address, e.g. `http://192.168.x.y:8096`
   (or your public URL). If Jellyfin has a base URL configured (Dashboard -> Networking -> Base URL), include it,
   e.g. `http://192.168.x.y:8096/jellyfin`. Jellyfin's own ffmpeg opens the `.strm` URL whenever it transcodes or remuxes, and
   clients may open it directly when they can play the source as-is. `localhost`/`127.0.0.1` works for neither
   case in common setups: inside a Docker container it does not reach the published host port, and on a remote
   client it points at the client itself. In this degraded mode clients that direct-play may also follow the
   redirect to the final stream URL themselves rather than streaming through Jellyfin. This is fixed in M2 (see
   `docs/spikes/2026-10-m0-findings.md`, S4, and `docs/architecture.md`).

Currents only manages title folders that carry its `.currents` marker file:
- **Writing**: a new title folder gets the marker. An existing folder without the marker is adopted only when it
  contains nothing but files Currents writes itself (`*.strm`, `movie.nfo`, `tvshow.nfo`, and `Season NN`/`Specials`
  folders holding only `*.strm`); any other folder is left untouched and the title is skipped with a warning, even
  if it is the folder recorded for that title (for example after `LibraryRoot` was pointed at a real media folder).
- **Pruning**: an unmarked folder is never touched. In a marked folder Currents deletes only the files it writes;
  the marker is removed last, together with the folder, and only when nothing else is left. If the folder still
  holds other files (posters, subtitles, season NFOs Jellyfin saved there, your own files), those files, the marker
  and the folder all stay, so the title can be written into the same folder again if it returns to a catalog.

## Development
See [`dev/README.md`](dev/README.md), [`CONTRIBUTING.md`](CONTRIBUTING.md) and [`docs/architecture.md`](docs/architecture.md).

## License
GPL-3.0-or-later. See [`LICENSE`](LICENSE).
