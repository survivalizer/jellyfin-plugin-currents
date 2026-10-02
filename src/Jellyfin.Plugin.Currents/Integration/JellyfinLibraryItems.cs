using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>
/// Search-add's view of Jellyfin's library. New titles are created directly (ResolvePath + CreateItem on their own
/// folder) and refreshed alone, because a scan or file-change event takes a minute and reprocesses the whole library.
/// </summary>
public sealed class JellyfinLibraryItems : ILibraryItems
{
    private readonly ILibraryManager _library;
    private readonly IUserManager _users;
    private readonly IProviderManager _providers;
    private readonly ILibraryMonitor _monitor;
    private readonly IFileSystem _fileSystem;
    private readonly ICurrentsSettings _settings;
    private readonly ILogger<JellyfinLibraryItems> _logger;
    private readonly TimeSpan _movieRefresh;
    private readonly TimeSpan _seriesRefresh;

    public JellyfinLibraryItems(ILibraryManager library, IUserManager users, IProviderManager providers, ILibraryMonitor monitor, IFileSystem fileSystem, ICurrentsSettings settings, ILogger<JellyfinLibraryItems> logger)
        : this(library, users, providers, monitor, fileSystem, settings, logger, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60))
    {
    }

    internal JellyfinLibraryItems(ILibraryManager library, IUserManager users, IProviderManager providers, ILibraryMonitor monitor, IFileSystem fileSystem, ICurrentsSettings settings, ILogger<JellyfinLibraryItems> logger, TimeSpan movieRefresh, TimeSpan seriesRefresh)
    {
        _library = library;
        _users = users;
        _providers = providers;
        _monitor = monitor;
        _fileSystem = fileSystem;
        _settings = settings;
        _logger = logger;
        _movieRefresh = movieRefresh;
        _seriesRefresh = seriesRefresh;
    }

    public Guid? FindTitle(TitleState title)
    {
        var folder = Path.Combine(Paths().Root, title.Folder);
        var item = title.Kind == MediaKind.Movie
            ? _library.FindByPath(Path.Combine(folder, PathNaming.MovieFile(Path.GetFileName(folder))), isFolder: false)
            : _library.FindByPath(folder, isFolder: true);
        if (item is null)
        {
            var matches = _library.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [ItemKind(title.Kind)],
                Recursive = true,
                HasAnyProviderIds = new Dictionary<string, string[]>(StringComparer.Ordinal) { [CurrentsProviderIds.Currents] = [title.StremioId] },
                Limit = 1,
            });
            item = matches.Count > 0 ? matches[0] : null;
        }

        return item?.Id;
    }

    public IReadOnlyDictionary<string, Guid> FindExisting(Guid userId, IReadOnlyCollection<TitleKey> keys)
    {
        var found = new Dictionary<string, Guid>(StringComparer.Ordinal);
        if (keys.Count == 0 || _users.GetUserById(userId) is not { } user)
        {
            return found;
        }

        var ids = keys.SelectMany(ProviderPairs)
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Value).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var items = _library.GetItemList(new InternalItemsQuery(user)
        {
            IncludeItemTypes = keys.Select(k => ItemKind(k.Kind)).Distinct().ToArray(),
            Recursive = true,
            HasAnyProviderIds = ids,
        });

        foreach (var key in keys)
        {
            var match = items.FirstOrDefault(i => IsKind(i, key.Kind)
                && ProviderPairs(key).Any(p => string.Equals(i.GetProviderId(p.Name), p.Value, StringComparison.OrdinalIgnoreCase)));
            if (match is not null)
            {
                found[key.StateId] = match.Id;
            }
        }

        return found;
    }

    public bool CanAdd(Guid userId, MediaKind kind)
    {
        // Remote results carry no rating or tags Jellyfin could filter on, so users with parental controls get none.
        if (_users.GetUserById(userId) is not { } user
            || HasParentalControls(user)
            || PhysicalFolder(kind) is not { } physical
            || !ContentTypeFits(kind, _library.GetContentType(physical)))
        {
            return false;
        }

        return _library.GetCollectionFolders(physical).Exists(c => c.IsVisible(user));
    }

    public IReadOnlyCollection<MediaKind> KindsIn(Guid libraryId) =>
        new[] { MediaKind.Movie, MediaKind.Series }
            .Where(k => PhysicalFolder(k) is { } physical && _library.GetCollectionFolders(physical).Exists(c => c.Id == libraryId))
            .ToList();

    public async Task<Guid?> AddAsync(MediaKind kind, string relativeFolder, CancellationToken cancellationToken)
    {
        if (PhysicalFolder(kind) is not { } physical)
        {
            return null;
        }

        var folder = Path.Combine(Paths().Root, relativeFolder);

        // A fresh DirectoryService: a cached listing would not show the files just written.
        var directoryService = new DirectoryService(_fileSystem);
        var resolved = _library.ResolvePath(_fileSystem.GetDirectoryInfo(folder), physical, directoryService, _library.GetContentType(physical))
            ?? throw new InvalidOperationException($"Jellyfin did not recognise {Path.GetFileName(folder)} as a title.");
        var item = _library.GetItemById(resolved.Id) ?? Create(resolved, physical);
        await RefreshAsync(item, kind, directoryService, cancellationToken).ConfigureAwait(false);

        if (_library.GetItemById(item.Id) is null)
        {
            // A folder scan that listed the disk before this item existed deletes it as "removed" (Folder.cs:438-553).
            _logger.LogInformation("A library scan removed {Name} while Currents added it; adding it again", item.Name);
            _library.CreateItem(item, physical);

            // The scan also removed the children the refresh made (a series' seasons and episodes); refresh it again.
            _providers.QueueRefresh(item.Id, RefreshOptions(new DirectoryService(_fileSystem)), RefreshPriority.High);
        }

        return item.Id;
    }

    public IDisposable PauseMonitoring(MediaKind kind)
    {
        // Jellyfin's monitor ignores events for an ignored path and everything under it, so pausing the kind's root covers every title folder.
        var root = Paths().RootFor(kind);
        _monitor.ReportFileSystemChangeBeginning(root);
        return new MonitorPause(_monitor, root);
    }

    private static bool HasParentalControls(User user) =>
        user.MaxParentalRatingScore.HasValue
        || user.GetPreference(PreferenceKind.BlockUnratedItems).Length > 0
        || user.GetPreference(PreferenceKind.BlockedTags).Length > 0
        || user.GetPreference(PreferenceKind.AllowedTags).Length > 0;

    private static bool ContentTypeFits(MediaKind kind, CollectionType? type) =>
        kind == MediaKind.Movie ? type is null or CollectionType.movies : type == CollectionType.tvshows;

    private static BaseItemKind ItemKind(MediaKind kind) => kind == MediaKind.Movie ? BaseItemKind.Movie : BaseItemKind.Series;

    private static bool IsKind(BaseItem item, MediaKind kind) => kind == MediaKind.Movie ? item is Movie : item is Series;

    private static IEnumerable<(string Name, string Value)> ProviderPairs(TitleKey key)
    {
        var name = key.Provider switch
        {
            "imdb" => nameof(MetadataProvider.Imdb),
            "tmdb" => nameof(MetadataProvider.Tmdb),
            "tvdb" => nameof(MetadataProvider.Tvdb),
            _ => null,
        };
        if (name is not null)
        {
            yield return (name, key.Value);
        }

        yield return (CurrentsProviderIds.Currents, key.StremioId);
    }

    private LibraryPaths Paths() => LibraryPaths.FromSettings(_settings);

    private Folder? PhysicalFolder(MediaKind kind) => _library.FindByPath(Paths().RootFor(kind), isFolder: true) as Folder;

    private BaseItem Create(BaseItem item, Folder parent)
    {
        _library.CreateItem(item, parent);
        return item;
    }

    private static MetadataRefreshOptions RefreshOptions(IDirectoryService directoryService) => new(directoryService)
    {
        MetadataRefreshMode = MetadataRefreshMode.Default,
        ImageRefreshMode = MetadataRefreshMode.Default,
    };

    private async Task RefreshAsync(BaseItem item, MediaKind kind, IDirectoryService directoryService, CancellationToken cancellationToken)
    {
        var options = RefreshOptions(directoryService);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(kind == MediaKind.Movie ? _movieRefresh : _seriesRefresh);
        try
        {
            if (kind == MediaKind.Movie)
            {
                await _providers.RefreshSingleItem(item, options, timeout.Token).ConfigureAwait(false);
            }
            else
            {
                await _providers.RefreshFullItem(item, options, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Jellyfin's own on-demand refresh does the same (UserLibraryController.RefreshOnDemandIfNeeded).
            _logger.LogInformation("Metadata for {Name} is taking a while; it finishes in the background", item.Name);
            _providers.QueueRefresh(item.Id, options, RefreshPriority.High);
        }
    }

    private sealed class MonitorPause(ILibraryMonitor monitor, string path) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                monitor.ReportFileSystemChangeComplete(path, refreshPath: false);
            }
        }
    }
}
