using System.Collections.Concurrent;
using Jellyfin.Plugin.Currents.Clients.AioMetadata;
using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Search;

/// <summary>Decides what a search id stands for: an existing item, or a title to add now (spec §4.2).</summary>
public sealed class SearchTitleOpener
{
    private readonly TitleLibrary _titles;
    private readonly SearchResultRegistry _registry;
    private readonly ILibraryItems _library;
    private readonly IAioMetadataClient _client;
    private readonly ICurrentsSettings _settings;
    private readonly ILogger<SearchTitleOpener> _logger;
    private readonly ConcurrentDictionary<string, Lazy<Task<OpenOutcome>>> _adding = new(StringComparer.Ordinal);

    public SearchTitleOpener(TitleLibrary titles, SearchResultRegistry registry, ILibraryItems library, IAioMetadataClient client, ICurrentsSettings settings, ILogger<SearchTitleOpener> logger)
    {
        _titles = titles;
        _registry = registry;
        _library = library;
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Opens a search id.</summary>
    /// <param name="id">The requested item id.</param>
    /// <param name="userId">The signed-in user, or <see cref="Guid.Empty"/>.</param>
    /// <param name="mayAdd">Whether the user may add titles; asked only when a title would be added.</param>
    /// <param name="cancellationToken">Cancels this caller's wait (not a shared add).</param>
    /// <returns>What the id stands for.</returns>
    public async Task<OpenOutcome> OpenAsync(Guid id, Guid userId, Func<bool> mayAdd, CancellationToken cancellationToken)
    {
        // Every item request passes through here, so ordinary ids leave before any lock or user lookup.
        var isKnown = _titles.IsKnownSearchId(id);
        SearchResult? shown = null;
        if (!isKnown && !_registry.TryGet(id, out shown))
        {
            return OpenOutcome.NotSearchId;
        }

        // The shown result (if still registered) also helps re-add a known title whose files were deleted.
        var known = isKnown ? _titles.FindBySearchId(id) : null;
        if (shown is null && !_registry.TryGet(id, out shown) && known is null)
        {
            return OpenOutcome.NotSearchId;
        }

        if (known is not null && _library.FindTitle(known) is { } itemId)
        {
            return OpenOutcome.Opened(itemId);
        }

        TitleKey? key = shown?.Key;
        if (key is null && !TitleKey.TryParse(known!.Kind, known.StremioId, out key))
        {
            return OpenOutcome.NotSearchId;
        }

        if (userId != Guid.Empty && _library.FindExisting(userId, [key]).TryGetValue(key.StateId, out var existing))
        {
            return OpenOutcome.Opened(existing);
        }

        if (userId == Guid.Empty || !mayAdd() || !_library.CanAdd(userId, key.Kind))
        {
            return OpenOutcome.NotAllowed;
        }

        var lazy = _adding.GetOrAdd(key.StateId, _ => new Lazy<Task<OpenOutcome>>(() => AddAsync(key, known, shown)));
        var add = lazy.Value;
        _ = add.ContinueWith(_ => _adding.TryRemove(new KeyValuePair<string, Lazy<Task<OpenOutcome>>>(key.StateId, lazy)), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return await add.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    // Shared by concurrent callers, so it never observes one caller's cancellation; the HttpClient and refresh timeouts bound it.
    private async Task<OpenOutcome> AddAsync(TitleKey key, TitleState? known, SearchResult? shown)
    {
        var folders = key.Kind == MediaKind.Movie ? "Movies" : "Shows";
        try
        {
            // Paused before any file is written, so the monitor ignores our own tmp+move writes too.
            using (_library.PauseMonitoring(key.Kind))
            {
                // A known title whose files are gone (deleted in Jellyfin) is written again like a new one, in its old folder.
                var folder = known is null || _titles.IsMissingOnDisk(known) ? null : known.Folder;
                if (folder is null)
                {
                    var meta = await FetchMetaAsync(key, shown).ConfigureAwait(false);
                    if (meta is null)
                    {
                        var name = shown?.Name ?? Path.GetFileName(known!.Folder);
                        return OpenOutcome.Failed(key.Kind == MediaKind.Series
                            ? $"AIOMetadata has no episode list for {name} right now."
                            : $"AIOMetadata has no details for {name} right now.");
                    }

                    folder = _titles.AddFromSearch(key, meta).RelativeFolder;
                    if (known is null)
                    {
                        _logger.LogInformation("Added {Folder} from search", folder);
                    }
                    else
                    {
                        _logger.LogInformation("Wrote {Folder} again from search; its files were gone", folder);
                    }
                }

                var itemId = await _library.AddAsync(key.Kind, folder, CancellationToken.None).ConfigureAwait(false);
                return itemId is { } id
                    ? OpenOutcome.Opened(id)
                    : OpenOutcome.Failed($"Add the Currents {folders} folder to a Jellyfin library first.");
            }
        }
        catch (Exception ex) when (ex is AioMetadataException or HttpRequestException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Could not add {StateId} from search: {Error}", key.StateId, SecretMasker.Mask(ex.Message));
            return OpenOutcome.Failed("Currents could not add this title. The server log has details.");
        }
    }

    /// <summary>
    /// The title's own type first (AIOMetadata's meta route branches on the id), then the search catalog's when a
    /// result was shown; a movie may use the shown search meta itself.
    /// </summary>
    private async Task<StremioMeta?> FetchMetaAsync(TitleKey key, SearchResult? shown)
    {
        StremioMeta? meta = null;
        var id = shown?.Meta.Id ?? key.StremioId;
        if (AioMetadataEndpoint.TryParse(_settings.Current.AioMetadataManifestUrl, out var endpoint, out _))
        {
            try
            {
                meta = await _client.GetMetaAsync(endpoint, key.StremioType, id, CancellationToken.None).ConfigureAwait(false);
                if (meta is null && shown is not null && !string.Equals(shown.CatalogType, key.StremioType, StringComparison.Ordinal))
                {
                    meta = await _client.GetMetaAsync(endpoint, shown.CatalogType, id, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is AioMetadataException or HttpRequestException or OperationCanceledException)
            {
                _logger.LogWarning("Could not load the AIOMetadata meta for {StateId}: {Error}", key.StateId, SecretMasker.Mask(ex.Message));
            }
        }

        return meta ?? (key.Kind == MediaKind.Movie ? shown?.Meta : null);
    }
}
