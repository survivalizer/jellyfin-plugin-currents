# Currents — AIOStreams & AIOMetadata for Jellyfin

Currents is a Jellyfin 12 plugin. It syncs catalogs from [AIOMetadata](https://github.com/cedya77/aiometadata)
into your library as real titles and plays them through [AIOStreams](https://github.com/Viren070/AIOStreams).

> Status: early development. See `docs/superpowers/specs/2026-10-01-currents-design.md` for the design.

## Requirements
- Jellyfin 12.0 or newer
- A self-hosted (or hosted) AIOStreams instance with a debrid service configured
- An AIOMetadata instance and a saved configuration

## Setup (M1)
1. Install the plugin (release repository URL coming in a later release; for now build with `dev/deploy-plugin.sh`).
2. Dashboard -> Plugins -> Currents:
   - paste your AIOMetadata manifest URL, click **Load catalogs**, tick catalogs, choose Movies/Shows;
   - paste your AIOStreams manifest URL and click **Test connection**;
   - **Save**, then **Sync now**.
3. Add the two folders shown on the settings page as a Movies library and a Shows library.
   In each library's settings enable the **Currents (AIOMetadata)** metadata and image fetchers and the NFO reader.
4. Set *Jellyfin address written into .strm files* (`StrmBaseUrl`) to an address that **both your clients and
   the Jellyfin server itself** can reach, normally the server's LAN address, e.g. `http://192.168.x.y:8096`
   (or your public URL). Jellyfin's own ffmpeg opens the `.strm` URL whenever it transcodes or remuxes, and
   clients may open it directly when they can play the source as-is. `localhost`/`127.0.0.1` works for neither
   case in common setups: inside a Docker container it does not reach the published host port, and on a remote
   client it points at the client itself. In this degraded mode clients that direct-play may also follow the
   redirect to the final stream URL themselves rather than streaming through Jellyfin. This is fixed in M2 (see
   `docs/spikes/2026-10-m0-findings.md`, S4, and `docs/architecture.md`).

Currents only manages title folders it created (each has a `.currents` marker file); it never deletes or
overwrites other folders, and pruning removes only files the plugin wrote.

## Development
See [`dev/README.md`](dev/README.md), [`CONTRIBUTING.md`](CONTRIBUTING.md) and [`docs/architecture.md`](docs/architecture.md).

## License
GPL-3.0-or-later. See [`LICENSE`](LICENSE).
