using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.Currents.Clients.AioMetadata;
using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Segments;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Users;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Library;

/// <summary>The "Verify library" and "Purge Currents content" jobs. Both hold the library job gate, so they never overlap a sync.</summary>
public sealed class LibraryMaintenance
{
    private readonly IAioMetadataClient _client;
    private readonly TitleLibrary _titles;
    private readonly ILibraryRefresher _refresher;
    private readonly ILibraryItems _items;
    private readonly UserStore _users;
    private readonly IUserDirectory _directory;
    private readonly IStreamService _streams;
    private readonly ProbeCache _probes;
    private readonly SegmentStore _segments;
    private readonly LibraryJobGate _jobs;
    private readonly ICurrentsSettings _settings;
    private readonly ILogger<LibraryMaintenance> _logger;

    public LibraryMaintenance(
        IAioMetadataClient client,
        TitleLibrary titles,
        ILibraryRefresher refresher,
        ILibraryItems items,
        UserStore users,
        IUserDirectory directory,
        IStreamService streams,
        ProbeCache probes,
        SegmentStore segments,
        LibraryJobGate jobs,
        ICurrentsSettings settings,
        ILogger<LibraryMaintenance> logger)
    {
        _client = client;
        _titles = titles;
        _refresher = refresher;
        _items = items;
        _users = users;
        _directory = directory;
        _streams = streams;
        _probes = probes;
        _segments = segments;
        _jobs = jobs;
        _settings = settings;
        _logger = logger;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "One title that cannot be fetched or written must not stop the others; it stays in state for the next run. Cancellation still propagates.")]
    public async Task<VerifyReport> VerifyAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        using var job = await _jobs.EnterAsync(cancellationToken).ConfigureAwait(false);
        var usersRemoved = ForgetDeletedUsers();

        var paths = LibraryPaths.FromSettings(_settings);
        var mounted = Enum.GetValues<MediaKind>().Where(k => Directory.Exists(paths.RootFor(k))).ToHashSet();
        var ids = _titles.Use(state => state.Titles.Select(t => t.StateId).ToList());
        var missing = ids
            .Select(_titles.Get)
            .OfType<TitleState>()
            .Where(t => mounted.Contains(t.Kind) && _titles.IsMissingOnDisk(t))
            .ToList();
        if (missing.Count == 0)
        {
            progress.Report(100);
            return new VerifyReport(0, 0, usersRemoved);
        }

        if (!AioMetadataEndpoint.TryParse(_settings.Current.AioMetadataManifestUrl, out var endpoint, out _))
        {
            _logger.LogWarning("Verify library: AIOMetadata is not configured, so {Count} missing titles cannot be rewritten", missing.Count);
            return new VerifyReport(0, missing.Count, usersRemoved);
        }

        var writer = _titles.CreateWriter();
        int rewritten = 0, failed = 0;
        for (var i = 0; i < missing.Count; i++)
        {
            var title = missing[i];
            try
            {
                if (TitleKey.TryParse(title.Kind, title.StremioId, out var key)
                    && await FetchMetaAsync(endpoint, key, title, cancellationToken).ConfigureAwait(false) is { } meta)
                {
                    _titles.Use(state =>
                    {
                        if (state.Get(title.StateId) is { } entry)
                        {
                            var result = entry.Kind == MediaKind.Movie ? writer.WriteMovie(key, meta, entry.Folder) : writer.WriteSeries(key, meta, entry.Folder);
                            entry.Folder = result.RelativeFolder;
                        }
                    });
                    rewritten++;
                }
                else
                {
                    failed++;
                    _logger.LogWarning("Verify library: no metadata for {Id}; leaving it for the next run", title.StremioId);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failed++;
                _logger.LogWarning("Verify library: could not rewrite {Folder}: {Reason}", title.Folder, SecretMasker.Mask(ex.Message));
            }

            progress.Report((i + 1) * 90.0 / missing.Count);
        }

        _titles.Use(state => state.Save());
        if (rewritten > 0)
        {
            await _refresher.RefreshAsync([paths.Movies, paths.Shows], cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Verify library: {Rewritten} rewritten, {Failed} failed, {Users} deleted users forgotten", rewritten, failed, usersRemoved);
        progress.Report(100);
        return new VerifyReport(rewritten, failed, usersRemoved);
    }

    /// <summary>Removes every Currents title (plugin files only, through <see cref="LibraryWriter.Delete"/>), the sync state and the probe and skip-marker caches, then refreshes the library.</summary>
    /// <param name="progress">Task progress.</param>
    /// <param name="cancellationToken">Cancels the wait for the job gate and the refresh.</param>
    /// <returns>The number of titles removed.</returns>
    public async Task<int> PurgeAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        using var job = await _jobs.EnterAsync(cancellationToken).ConfigureAwait(false);
        var writer = _titles.CreateWriter();
        var itemIds = new List<Guid>();
        var libraryPaths = LibraryPaths.FromSettings(_settings);
        var root = libraryPaths.Root;
        var removed = _titles.Use(state =>
        {
            var count = 0;
            foreach (var title in state.Titles)
            {
                if (!Directory.Exists(libraryPaths.RootFor(title.Kind)))
                {
                    // An unmounted library root looks like an empty one; forgetting the title would orphan its folder for good.
                    _logger.LogInformation("Purge: keeping {Folder} because its library folder is not available", title.Folder);
                    continue;
                }

                try
                {
                    // Only a folder Currents manages (marker present) and actually removed gives up its Jellyfin item.
                    var folder = Path.Combine(root, title.Folder);
                    var managed = File.Exists(Path.Combine(folder, LibraryWriter.MarkerFile));
                    var itemId = managed ? _items.FindTitle(title) : null;
                    writer.Delete(title.Folder);
                    if (itemId is { } id && !Directory.Exists(folder))
                    {
                        itemIds.Add(id);
                    }

                    state.Remove(title.StateId);
                    count++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    _logger.LogWarning("Purge: could not remove {Folder}: {Reason}", title.Folder, SecretMasker.Mask(ex.Message));
                }
            }

            state.Save();
            return count;
        });
        progress.Report(50);

        _streams.Clear();
        _probes.Clear();
        _segments.Clear();
        if (itemIds.Count > 0)
        {
            // Jellyfin skips an empty library folder, so a refresh alone would keep listing the purged titles.
            _items.RemoveItems(itemIds);
        }

        var paths = LibraryPaths.FromSettings(_settings);
        await _refresher.RefreshAsync([paths.Movies, paths.Shows], cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Purged {Count} Currents titles and cleared the Currents caches", removed);
        progress.Report(100);
        return removed;
    }

    // AIOMetadata's meta route branches on the id; anime catalogs are typed "anime.series"/"anime.movie", so the catalog type is the fallback.
    private async Task<StremioMeta?> FetchMetaAsync(AioMetadataEndpoint endpoint, TitleKey key, TitleState title, CancellationToken cancellationToken)
    {
        var meta = await _client.GetMetaAsync(endpoint, key.StremioType, key.StremioId, cancellationToken).ConfigureAwait(false);
        var catalogType = title.Catalogs.Select(c => c.Split('/')[0]).FirstOrDefault(t => !string.Equals(t, key.StremioType, StringComparison.Ordinal));
        if (meta is null && catalogType is not null)
        {
            meta = await _client.GetMetaAsync(endpoint, catalogType, key.StremioId, cancellationToken).ConfigureAwait(false);
        }

        return meta;
    }

    private int ForgetDeletedUsers()
    {
        var known = _directory.All().Select(u => u.Id).ToHashSet();
        if (known.Count == 0)
        {
            // An empty directory means Jellyfin's users could not be read, not that everyone was deleted.
            return 0;
        }

        var removed = 0;
        foreach (var id in _users.All().Keys.Where(id => !known.Contains(id)).ToList())
        {
            if (_users.Remove(id))
            {
                removed++;
            }
        }

        return removed;
    }
}
