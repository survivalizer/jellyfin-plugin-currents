using System.Diagnostics.CodeAnalysis;
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
    private readonly TitleLibrary _titles;
    private readonly TimeProvider _time;
    private readonly ILogger<CatalogSyncService> _logger;

    public CatalogSyncService(
        IAioMetadataClient client,
        IPlayedLookup played,
        ILibraryRefresher refresher,
        ICurrentsSettings settings,
        TitleLibrary titles,
        TimeProvider time,
        ILogger<CatalogSyncService> logger)
    {
        _client = client;
        _played = played;
        _refresher = refresher;
        _settings = settings;
        _titles = titles;
        _time = time;
        _logger = logger;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A malformed upstream item or catalog (e.g. null entries) must only fail that title or catalog, never the whole sync; cancellation still propagates.")]
    public async Task<SyncReport> SyncAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = _settings.Current;
        if (!AioMetadataEndpoint.TryParse(config.AioMetadataManifestUrl, out var endpoint, out var error))
        {
            throw new InvalidOperationException($"AIOMetadata is not configured: {error}");
        }

        var paths = LibraryPaths.FromSettings(_settings);
        var writer = _titles.CreateWriter();
        var catalogs = config.Catalogs.Where(c => c.Enabled).ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var failed = new HashSet<string>(StringComparer.Ordinal);
        var protectedIds = new HashSet<string>(StringComparer.Ordinal);
        int written = 0, unchanged = 0;

        for (var i = 0; i < catalogs.Count; i++)
        {
            var catalog = catalogs[i];
            var kind = catalog.Target == CatalogTarget.Movies ? MediaKind.Movie : MediaKind.Series;
            try
            {
                var metas = await FetchCatalogAsync(endpoint, catalog, cancellationToken).ConfigureAwait(false);
                var usable = metas
                    .Where(m => !IsErrorItem(m))
                    .Select(m => (Meta: m, Key: TitleKey.FromMeta(kind, m)))
                    .ToList();
                if (usable.TrueForAll(u => u.Key is null) && _titles.Use(s => s.Titles.Any(t => t.Catalogs.Contains(catalog.Key))))
                {
                    _logger.LogWarning("Catalog {Catalog} returned no usable items; treating it as unavailable this run", catalog.Key);
                    failed.Add(catalog.Key);
                }

                foreach (var (meta, key) in usable)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (key is null)
                    {
                        _logger.LogDebug("Skipping {Id} from {Catalog}: no usable id", meta.Id, catalog.Key);
                        continue;
                    }

                    bool changed;
                    try
                    {
                        changed = await WriteTitleAsync(endpoint, catalog, kind, key, meta, writer, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                    {
                        // Protect the title: a failed write must not count as "missing from its catalog".
                        _logger.LogWarning(ex, "Could not write title {Id} from catalog {Catalog}; leaving it as it is", meta.Id, catalog.Key);
                        protectedIds.Add(key.StateId);
                        continue;
                    }

                    seen.Add(key.StateId);
                    if (changed)
                    {
                        written++;
                    }
                    else
                    {
                        unchanged++;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Catalog {Catalog} failed; its titles are left unchanged this run", catalog.Key);
                failed.Add(catalog.Key);
            }

            progress.Report((i + 1) * 90.0 / catalogs.Count);
        }

        var (refreshed, kept) = await RefreshSearchAddedSeriesAsync(endpoint, writer, seen, cancellationToken).ConfigureAwait(false);
        written += refreshed;
        unchanged += kept;

        var pruned = 0;
        if (catalogs.Count == 0)
        {
            _logger.LogInformation("No catalogs are enabled; skipping pruning so existing titles are kept");
            _titles.Use(state => state.Save());
        }
        else
        {
            pruned = _titles.Use(state =>
            {
                var count = Prune(state, writer, paths, seen, protectedIds, failed, config.PruneAfterMisses);
                state.Save();
                return count;
            });
        }

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

    /// <summary>AIOMetadata reports catalog errors as fake items whose id starts with "aiom.error.".</summary>
    private static bool IsErrorItem(StremioMeta meta) =>
        meta.Id?.StartsWith("aiom.error.", StringComparison.Ordinal) == true;

    private async Task<List<StremioMeta>> FetchCatalogAsync(AioMetadataEndpoint endpoint, CatalogSelection catalog, CancellationToken cancellationToken)
    {
        var results = new List<StremioMeta>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var skip = 0;

        for (var page = 0; page < MaxPagesPerCatalog && results.Count < catalog.MaxItems; page++)
        {
            var metas = await _client.GetCatalogPageAsync(endpoint, catalog.Type, catalog.Id, skip, cancellationToken).ConfigureAwait(false);
            if (metas.Count == 0)
            {
                break;
            }

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

            if (added == 0)
            {
                break;
            }
        }

        return results;
    }

    private async Task<bool> WriteTitleAsync(
        AioMetadataEndpoint endpoint,
        CatalogSelection catalog,
        MediaKind kind,
        TitleKey key,
        StremioMeta meta,
        LibraryWriter writer,
        CancellationToken cancellationToken)
    {
        var full = meta;
        if (kind == MediaKind.Series && (meta.Videos is null || meta.Videos.Count == 0))
        {
            try
            {
                full = await FetchSeriesMetaAsync(endpoint, catalog.Type, key, meta.Id, cancellationToken).ConfigureAwait(false) ?? meta;
            }
            catch (Exception ex) when (ex is AioMetadataException or HttpRequestException
                || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                _logger.LogWarning(ex, "Could not load episodes for {Id}; writing the series without new episodes", meta.Id);
            }
        }

        return _titles.Use(state =>
        {
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
            return result.Changed;
        });
    }

    /// <summary>
    /// AIOMetadata's meta route branches on the id, not the catalog type; Currents' own metadata providers ask with
    /// "series". Anime catalogs are typed "anime.series", so ask with the title's type first and the catalog's after.
    /// </summary>
    private async Task<StremioMeta?> FetchSeriesMetaAsync(AioMetadataEndpoint endpoint, string catalogType, TitleKey key, string id, CancellationToken cancellationToken)
    {
        var meta = await _client.GetMetaAsync(endpoint, key.StremioType, id, cancellationToken).ConfigureAwait(false);
        if (meta is null && !string.Equals(catalogType, key.StremioType, StringComparison.Ordinal))
        {
            meta = await _client.GetMetaAsync(endpoint, catalogType, id, cancellationToken).ConfigureAwait(false);
        }

        return meta;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The Jellyfin played lookup can throw arbitrary exceptions; one title must never stop the sync.")]
    private int Prune(StateStore state, LibraryWriter writer, LibraryPaths paths, HashSet<string> seen, HashSet<string> protectedIds, HashSet<string> failed, int threshold)
    {
        var pruned = 0;
        foreach (var title in state.Titles)
        {
            if (seen.Contains(title.StateId) || protectedIds.Contains(title.StateId) || title.AddedBySearch || title.Catalogs.Exists(failed.Contains))
            {
                continue;
            }

            title.MissCount++;
            try
            {
                var played = title.MissCount >= threshold && _played.IsPlayedByAnyone(Path.Combine(paths.Root, title.Folder), title.Kind);
                if (PruningPolicy.ShouldPrune(title, threshold, played))
                {
                    writer.Delete(title.Folder);
                    state.Remove(title.StateId);
                    pruned++;
                    _logger.LogInformation("Pruned {Folder} after {Misses} syncs without it", title.Folder, title.MissCount);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not prune {Folder}; keeping it", title.Folder);
            }
        }

        return pruned;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "One search-added series must never stop the sync; cancellation still propagates.")]
    private async Task<(int Written, int Unchanged)> RefreshSearchAddedSeriesAsync(AioMetadataEndpoint endpoint, LibraryWriter writer, HashSet<string> seen, CancellationToken cancellationToken)
    {
        int written = 0, unchanged = 0;
        var titles = _titles.Use(s => s.Titles.Where(t => t.AddedBySearch && t.Kind == MediaKind.Series && !seen.Contains(t.StateId)).ToList());
        foreach (var title in titles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TitleKey.TryParse(MediaKind.Series, title.StremioId, out var key))
            {
                continue;
            }

            try
            {
                var meta = await _client.GetMetaAsync(endpoint, key.StremioType, key.StremioId, cancellationToken).ConfigureAwait(false);
                if (meta is null)
                {
                    _logger.LogWarning("AIOMetadata has no meta for search-added series {Folder}; leaving it as it is", title.Folder);
                    continue;
                }

                var changed = _titles.Use(state =>
                {
                    var result = writer.WriteSeries(key, meta, title.Folder);
                    if (state.Get(title.StateId) is { } entry)
                    {
                        entry.Folder = result.RelativeFolder;
                        entry.LastSeen = _time.GetUtcNow();
                    }

                    return result.Changed;
                });
                if (changed)
                {
                    written++;
                }
                else
                {
                    unchanged++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Could not update search-added series {Folder}; leaving it as it is", title.Folder);
            }
        }

        return (written, unchanged);
    }
}
