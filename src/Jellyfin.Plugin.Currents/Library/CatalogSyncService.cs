using Jellyfin.Plugin.Currents.Clients.AioMetadata;
using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Library;

/// <summary>Pulls the selected AIOMetadata catalogs into the plugin library folders.</summary>
public sealed class CatalogSyncService
{
    private const int MaxPagesPerCatalog = 200;
    private readonly IAioMetadataClient _client;
    private readonly IPlayedLookup _played;
    private readonly ILibraryRefresher _refresher;
    private readonly ICurrentsSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<CatalogSyncService> _logger;

    public CatalogSyncService(
        IAioMetadataClient client,
        IPlayedLookup played,
        ILibraryRefresher refresher,
        ICurrentsSettings settings,
        TimeProvider time,
        ILogger<CatalogSyncService> logger)
    {
        _client = client;
        _played = played;
        _refresher = refresher;
        _settings = settings;
        _time = time;
        _logger = logger;
    }

    public async Task<SyncReport> SyncAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = _settings.Current;
        if (!AioMetadataEndpoint.TryParse(config.AioMetadataManifestUrl, out var endpoint, out var error))
        {
            throw new InvalidOperationException($"AIOMetadata is not configured: {error}");
        }

        var paths = LibraryPaths.FromSettings(_settings);
        var writer = new LibraryWriter(paths, new StrmSigner(config.SigningSecret), config.StrmBaseUrl, _time);
        var state = StateStore.Load(Path.Combine(_settings.DataFolderPath, "state.json"), _logger);
        var catalogs = config.Catalogs.Where(c => c.Enabled).ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var failed = new HashSet<string>(StringComparer.Ordinal);
        int written = 0, unchanged = 0;

        for (var i = 0; i < catalogs.Count; i++)
        {
            var catalog = catalogs[i];
            try
            {
                var metas = await FetchCatalogAsync(endpoint, catalog, cancellationToken).ConfigureAwait(false);
                if (metas.Count == 0 && state.Titles.Any(t => t.Catalogs.Contains(catalog.Key)))
                {
                    _logger.LogWarning("Catalog {Catalog} returned no items; treating it as unavailable this run", catalog.Key);
                    failed.Add(catalog.Key);
                }

                foreach (var meta in metas)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    (bool Changed, string? StateId) outcome;
                    try
                    {
                        outcome = await WriteTitleAsync(endpoint, catalog, meta, writer, state, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
                    {
                        _logger.LogWarning(ex, "Could not write title {Id} from catalog {Catalog}; skipping it", meta.Id, catalog.Key);
                        continue;
                    }

                    if (outcome.StateId is null)
                    {
                        continue;
                    }

                    seen.Add(outcome.StateId);
                    if (outcome.Changed)
                    {
                        written++;
                    }
                    else
                    {
                        unchanged++;
                    }
                }
            }
            catch (Exception ex) when (ex is AioMetadataException or HttpRequestException
                || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                _logger.LogWarning(ex, "Catalog {Catalog} failed; its titles are left unchanged this run", catalog.Key);
                failed.Add(catalog.Key);
            }

            progress.Report((i + 1) * 90.0 / catalogs.Count);
        }

        var pruned = Prune(state, writer, paths, seen, failed, config.PruneAfterMisses);
        state.Save();
        await _refresher.RefreshAsync([paths.Movies, paths.Shows], cancellationToken).ConfigureAwait(false);
        progress.Report(100);

        var report = new SyncReport(written, unchanged, pruned, failed.Order(StringComparer.Ordinal).ToList());
        _logger.LogInformation(
            "Currents sync finished: {Written} written, {Unchanged} unchanged, {Pruned} pruned, failed catalogs: {Failed}",
            report.Written,
            report.Unchanged,
            report.Pruned,
            report.FailedCatalogs);
        return report;
    }

    private async Task<List<StremioMeta>> FetchCatalogAsync(AioMetadataEndpoint endpoint, CatalogSelection catalog, CancellationToken cancellationToken)
    {
        var results = new List<StremioMeta>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var skip = 0;
        int? pageSize = null;

        for (var page = 0; page < MaxPagesPerCatalog && results.Count < catalog.MaxItems; page++)
        {
            var metas = await _client.GetCatalogPageAsync(endpoint, catalog.Type, catalog.Id, skip, cancellationToken).ConfigureAwait(false);
            if (metas.Count == 0)
            {
                break;
            }

            pageSize ??= metas.Count;
            skip += metas.Count;
            var added = 0;
            foreach (var meta in metas)
            {
                if (results.Count >= catalog.MaxItems)
                {
                    break;
                }

                if (ids.Add(meta.Id))
                {
                    results.Add(meta);
                    added++;
                }
            }

            if (added == 0 || metas.Count < pageSize)
            {
                break;
            }
        }

        return results;
    }

    private async Task<(bool Changed, string? StateId)> WriteTitleAsync(
        AioMetadataEndpoint endpoint,
        CatalogSelection catalog,
        StremioMeta meta,
        LibraryWriter writer,
        StateStore state,
        CancellationToken cancellationToken)
    {
        var kind = catalog.Target == CatalogTarget.Movies ? MediaKind.Movie : MediaKind.Series;
        var key = TitleKey.FromMeta(kind, meta);
        if (key is null)
        {
            _logger.LogDebug("Skipping {Id} from {Catalog}: no usable id", meta.Id, catalog.Key);
            return (false, null);
        }

        var full = meta;
        if (kind == MediaKind.Series && (meta.Videos is null || meta.Videos.Count == 0))
        {
            try
            {
                full = await _client.GetMetaAsync(endpoint, catalog.Type, meta.Id, cancellationToken).ConfigureAwait(false) ?? meta;
            }
            catch (Exception ex) when (ex is AioMetadataException or HttpRequestException)
            {
                _logger.LogWarning(ex, "Could not load episodes for {Id}; writing the series without new episodes", meta.Id);
            }
        }

        var existing = state.Get(key.StateId);
        var result = kind == MediaKind.Movie
            ? writer.WriteMovie(key, full, existing?.Folder)
            : writer.WriteSeries(key, full, existing?.Folder);

        var entry = existing ?? new TitleState { StateId = key.StateId, Kind = kind, StremioId = key.StremioId };
        entry.Folder = result.RelativeFolder;
        entry.MissCount = 0;
        entry.LastSeen = _time.GetUtcNow();
        if (!entry.Catalogs.Contains(catalog.Key))
        {
            entry.Catalogs.Add(catalog.Key);
        }

        state.Upsert(entry);
        return (result.Changed, key.StateId);
    }

    private int Prune(StateStore state, LibraryWriter writer, LibraryPaths paths, HashSet<string> seen, HashSet<string> failed, int threshold)
    {
        var pruned = 0;
        foreach (var title in state.Titles.ToList())
        {
            if (seen.Contains(title.StateId) || title.AddedBySearch || title.Catalogs.Exists(failed.Contains))
            {
                continue;
            }

            title.MissCount++;
            var played = title.MissCount >= threshold && _played.IsPlayedByAnyone(Path.Combine(paths.Root, title.Folder), title.Kind);
            if (PruningPolicy.ShouldPrune(title, threshold, played))
            {
                writer.Delete(title.Folder);
                state.Remove(title.StateId);
                pruned++;
                _logger.LogInformation("Pruned {Folder} after {Misses} syncs without it", title.Folder, title.MissCount);
            }
        }

        return pruned;
    }
}
