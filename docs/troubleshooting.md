# Troubleshooting

Find what you see in the first column. Quoted text is the exact wording Currents shows or logs.

| What you see | Why | What to do |
|---|---|---|
| Currents is not in the plugin catalog | Jellyfin is older than 12.1 (Currents 1.0 needs 12.1), or the repository URL has a typo. | Update Jellyfin. Check the repository URL under Dashboard → Plugins → Repositories. |
| Banner on the plugin page: "This Jellyfin (…) is outside the range Currents was tested with (…)." | Jellyfin 13 or newer. Currents is tested with 12.1 up to (not including) 13.0, and turns per-user versions off on other versions. | Wait for a Currents update, or tick **Run on this untested Jellyfin version** under **Versions**. No restart is needed. |
| Sync finished but no titles appear | No catalog is ticked, the AIOMetadata URL is wrong, or the Movies and Shows libraries do not exist yet. | Press **Load catalogs**, tick catalogs, **Save**, then **Sync now**. Create the two libraries (see the [README](../README.md#quick-start-from-install-to-first-play)). In Dashboard → Logs, look for "Catalog … returned no usable items; treating it as unavailable this run". |
| Titles have no poster or description | The library does not use the Currents metadata fetchers. | In the library's settings, enable the **Currents (AIOMetadata)** metadata and image fetchers and the **Nfo** metadata reader. Then refresh the library's metadata. |
| A version is called "Streams load when you open this title" | Normal in lists. Currents searches for streams when a title is opened. | Open the title. |
| "Streams are taking a while to load. Reopen this title in a moment." | AIOStreams took longer than 10 seconds. The search goes on in the background. | Reopen the title after a few seconds. |
| "No streams found for this title." or "No playable streams for this title." | AIOStreams found nothing for this title, or debrid is not set up in AIOStreams. | Press **Test connection** on the plugin page. Check the AIOStreams config itself (debrid service, addons). |
| "Streams are not configured. Ask your admin, or add your AIOStreams config on the Currents user page." | The user has no AIOStreams config of their own, none assigned, and there is no default. | Set a **Default AIOStreams manifest URL**, an override in the **Users** table, or let the user add one on their page ([self-service.md](self-service.md)). |
| "Streams are disabled for your account." | **Streams off** is ticked for this user in the **Users** table. | Untick it and press the row's **Save**. |
| "No streams match your stream preferences." | The user's filters (largest file, excluded resolutions or tags, cached only) hide every stream. | Loosen the preferences on the user page or in **Default preferences**. |
| Playback fails at once with versions off | **Jellyfin address written into .strm files** is not reachable by the client or by Jellyfin's own ffmpeg. | Set it to the server's LAN address, including Jellyfin's base URL path if one is set. See [configuration.md](configuration.md#strmbaseurl-and-degraded-mode). |
| With versions off: "Every stream for this title needs request headers, which .strm playback cannot send. Turn on versions to play it." | Streams that need request headers (usenet WebDAV, Google Drive) cannot play from a `.strm` file. | Turn **Show each stream as a version** back on. |
| Built-in subtitles are missing on a big file | The file is over the subtitle size limit (15 GB by default). | Raise **Hide built-in text subtitles on files larger than**, set it to 0, or use AIOStreams subtitles. See [configuration.md](configuration.md#built-in-subtitles-on-large-files). |
| A PGS (picture) subtitle shows nothing on a big file | The player is set to draw PGS subtitles itself. It asks for the track as a file, and Currents refuses that on files over the subtitle size limit. | Turn off the player's PGS rendering (jellyfin-web: Settings → Subtitles → render PGS) so the server burns the subtitle in. |
| The first subtitle takes many minutes on a big file | The limit is 0 or too high. Jellyfin reads the whole file before it shows a built-in subtitle. | Set a limit (the default is 15 GB). |
| Log: "Could not write title … from catalog …; leaving it as it is" with "The target folder … exists and is not managed by Currents." | A folder with that name exists without Currents' `.currents` marker and holds other files. Currents never writes into it. | Move or rename that folder. See [configuration.md](configuration.md#library-folders-and-docker). |
| Search shows no AIOMetadata results | Search is off, the user turned search-add off (or the admin did), the user has parental controls, the Currents folders are not in a library the user can see, or the term is one character. | Check the **Search** settings, the **Users** table and the user's page. |
| Search results have no posters (self-hosted AIOMetadata). Log: "Could not fetch a poster from …: HttpRequestException" | The poster images come from a private address that is not the AIOMetadata host and port. Currents refuses those. | Serve images from the AIOMetadata host and port itself, or from a public host. See [configuration.md](configuration.md#network-safety). |
| No skip-intro prompt | The version's length differs from the markers' length by more than the allowed difference, or the markers were not fetched yet. | Run the **Fetch skip markers** task. Check **Allowed length difference (%)**. |

## Where to look

- **Dashboard → Logs**: the Jellyfin log. Currents lines name their source as `Jellyfin.Plugin.Currents.…`, for
  example `Jellyfin.Plugin.Currents.Library.CatalogSyncService`. Currents masks manifest URLs, keys and tokens in its
  own log lines.
- **Dashboard → Plugins → Currents → Diagnostics**: **Run connection tests** checks each service. **Recent problems**
  lists the last 50 problems since Jellyfin started.
- **Dashboard → Scheduled Tasks**: the Currents tasks and their last result.
