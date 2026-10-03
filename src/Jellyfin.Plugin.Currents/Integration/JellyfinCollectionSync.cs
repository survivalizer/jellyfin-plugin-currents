using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Library;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>
/// Keeps one locked Jellyfin collection per ticked catalog. It finds the collection by its CurrentsCatalog provider id
/// (users may rename it), creates it when missing, adds the catalog's titles and removes Currents titles that left.
/// Items an admin added by hand stay.
/// </summary>
public sealed class JellyfinCollectionSync : ICollectionSync
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
    private readonly ICollectionManager _collections;
    private readonly ILibraryManager _library;
    private readonly ILibraryItems _items;
    private readonly ICurrentsSettings _settings;
    private readonly ILogger<JellyfinCollectionSync> _logger;

    public JellyfinCollectionSync(ICollectionManager collections, ILibraryManager library, ILibraryItems items, ICurrentsSettings settings, ILogger<JellyfinCollectionSync> logger)
    {
        _collections = collections;
        _library = library;
        _items = items;
        _settings = settings;
        _logger = logger;
    }

    public async Task SyncAsync(IReadOnlyList<CollectionPlan> plans, CancellationToken cancellationToken)
    {
        if (plans.Count == 0)
        {
            return;
        }

        // The first call creates Jellyfin's Collections library (one library scan); doing it here keeps it outside any scan.
        await _collections.GetCollectionsFolder(true).ConfigureAwait(false);
        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // AddToCollectionAsync throws for ids Jellyfin does not know, so titles it has not scanned yet wait for the next sync.
            var wanted = plan.Titles
                .Select(_items.FindTitle)
                .OfType<Guid>()
                .Where(id => _library.GetItemById(id) is not null)
                .Distinct()
                .ToList();
            if (Find(plan.CatalogKey) is not { } boxSet)
            {
                await CreateAsync(plan, wanted, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var linked = boxSet.LinkedChildren.Select(c => c.ItemId).OfType<Guid>().ToHashSet();
            var keep = wanted.ToHashSet();
            var add = wanted.Where(id => !linked.Contains(id)).ToList();
            var remove = linked.Where(id => !keep.Contains(id) && IsCurrentsItem(id)).ToList();
            if (add.Count > 0)
            {
                await _collections.AddToCollectionAsync(boxSet.Id, add).ConfigureAwait(false);
            }

            if (remove.Count > 0)
            {
                await _collections.RemoveFromCollectionAsync(boxSet.Id, remove).ConfigureAwait(false);
            }

            if (add.Count > 0 || remove.Count > 0)
            {
                _logger.LogInformation("Collection {Name}: {Added} added, {Removed} removed", boxSet.Name, add.Count, remove.Count);
            }
        }
    }

    private BoxSet? Find(string catalogKey) =>
        _library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.BoxSet],
            Recursive = true,
            HasAnyProviderIds = new Dictionary<string, string[]>(StringComparer.Ordinal) { [CurrentsProviderIds.Catalog] = [catalogKey] },
        })
            .OfType<BoxSet>()
            .FirstOrDefault();

    private async Task CreateAsync(CollectionPlan plan, List<Guid> items, CancellationToken cancellationToken)
    {
        var boxSet = await _collections.CreateCollectionAsync(new CollectionCreationOptions
        {
            Name = AvailableName(plan.Name),
            IsLocked = true,
            ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [CurrentsProviderIds.Catalog] = plan.CatalogKey },
            ItemIdList = items.Select(i => i.ToString("N", CultureInfo.InvariantCulture)).ToArray(),
        }).ConfigureAwait(false);

        // Catalog order instead of the default premiere-date order.
        boxSet.DisplayOrder = "Default";
        await _library.UpdateItemAsync(boxSet, boxSet.GetParent(), ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Created collection {Name} for catalog {Catalog} with {Count} titles", boxSet.Name, plan.CatalogKey, items.Count);
    }

    // Jellyfin derives a collection's folder and id from its name, so a name another collection already has would merge into it.
    private string AvailableName(string name)
    {
        var taken = _library.GetItemList(new InternalItemsQuery { IncludeItemTypes = [BaseItemKind.BoxSet], Recursive = true, Name = name }).Count > 0;
        return taken ? $"{name} (Currents)" : name;
    }

    private bool IsCurrentsItem(Guid id)
    {
        var path = _library.GetItemById(id)?.Path;
        var root = LibraryPaths.FromSettings(_settings).Root + Path.DirectorySeparatorChar;
        return path is not null && Path.GetFullPath(path).StartsWith(root, PathComparison);
    }
}
