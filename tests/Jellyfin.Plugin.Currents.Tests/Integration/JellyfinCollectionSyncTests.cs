using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Integration;

public sealed class JellyfinCollectionSyncTests : IDisposable
{
    private const string CatalogKey = "movie/tmdb.top";
    private readonly FakeSettings _settings = new();
    private readonly (ICollectionManager Instance, InterfaceFake Fake) _collections = InterfaceFake.Create<ICollectionManager>();
    private readonly (ILibraryManager Instance, InterfaceFake Fake) _library = InterfaceFake.Create<ILibraryManager>();
    private readonly FakeLibraryItems _items = new();
    private readonly Dictionary<Guid, BaseItem> _byId = [];
    private readonly List<BoxSet> _boxSets = [];

    public JellyfinCollectionSyncTests()
    {
        _settings.Current.LibraryRoot = Path.Combine(_settings.DataFolderPath, "library");
        _library.Fake.On(nameof(ILibraryManager.GetItemById), args => _byId.GetValueOrDefault((Guid)args[0]!));
        _library.Fake.On(nameof(ILibraryManager.GetItemList), args =>
        {
            var query = (InternalItemsQuery)args[0]!;
            var found = query.Name is { } name
                ? _boxSets.Where(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase))
                : _boxSets.Where(b => query.HasAnyProviderIds!.All(p => p.Value.Contains(b.GetProviderId(p.Key))));
            return (IReadOnlyList<BaseItem>)found.Cast<BaseItem>().ToList();
        });
        _collections.Fake.On(nameof(ICollectionManager.CreateCollectionAsync), args =>
        {
            var options = (CollectionCreationOptions)args[0]!;
            var boxSet = new BoxSet { Id = Guid.NewGuid(), Name = options.Name, IsLocked = options.IsLocked, ProviderIds = options.ProviderIds };
            _boxSets.Add(boxSet);
            return Task.FromResult(boxSet);
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private Guid CurrentsItem(string stateId)
    {
        var id = Guid.NewGuid();
        _byId[id] = new Movie { Id = id, Path = Path.Combine(_settings.Current.LibraryRoot, "Movies", stateId.Replace('/', '_'), "x.strm") };
        _items.Titles[stateId] = id;
        return id;
    }

    private static TitleState Title(string stateId) =>
        new() { StateId = stateId, Kind = MediaKind.Movie, StremioId = stateId.Split('/')[1], Folder = "Movies/" + stateId.Replace('/', '_') };

    private BoxSet ExistingCollection(params Guid[] linked)
    {
        var boxSet = new BoxSet
        {
            Id = Guid.NewGuid(),
            Name = "Popular",
            ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [CurrentsProviderIds.Catalog] = CatalogKey },
            LinkedChildren = linked.Select(id => new LinkedChild { ItemId = id }).ToArray(),
        };
        _boxSets.Add(boxSet);
        return boxSet;
    }

    private JellyfinCollectionSync Create() =>
        new(_collections.Instance, _library.Instance, _items, _settings, NullLogger<JellyfinCollectionSync>.Instance);

    private static List<Guid> Ids(object? argument) => ((IEnumerable<Guid>)argument!).ToList();

    [Fact]
    public async Task Creates_a_locked_collection_in_catalog_order()
    {
        var alpha = CurrentsItem("movie/tt1");
        var beta = CurrentsItem("movie/tt2");

        await Create().SyncAsync([new CollectionPlan(CatalogKey, "Popular", [Title("movie/tt2"), Title("movie/tt1"), Title("movie/tt3")])], CancellationToken.None);

        Assert.True((bool)Assert.Single(_collections.Fake.Calls(nameof(ICollectionManager.GetCollectionsFolder)))[0]!);
        var options = (CollectionCreationOptions)Assert.Single(_collections.Fake.Calls(nameof(ICollectionManager.CreateCollectionAsync)))[0]!;
        Assert.Equal("Popular", options.Name);
        Assert.True(options.IsLocked);
        Assert.Equal(CatalogKey, options.ProviderIds[CurrentsProviderIds.Catalog]);
        Assert.Equal(new[] { beta.ToString("N"), alpha.ToString("N") }, options.ItemIdList);
        var saved = (BoxSet)Assert.Single(_library.Fake.Calls(nameof(ILibraryManager.UpdateItemAsync)))[0]!;
        Assert.Equal("Default", saved.DisplayOrder);
    }

    [Fact]
    public async Task Updates_an_existing_collection_and_keeps_items_added_by_hand()
    {
        var kept = CurrentsItem("movie/tt1");
        var departed = CurrentsItem("movie/tt2");
        var added = CurrentsItem("movie/tt3");
        var byHand = Guid.NewGuid();
        _byId[byHand] = new Movie { Id = byHand, Path = Path.Combine(_settings.DataFolderPath, "own", "film.mkv") };
        var boxSet = ExistingCollection(kept, departed, byHand);

        await Create().SyncAsync([new CollectionPlan(CatalogKey, "Popular", [Title("movie/tt1"), Title("movie/tt3")])], CancellationToken.None);

        Assert.Empty(_collections.Fake.Calls(nameof(ICollectionManager.CreateCollectionAsync)));
        var add = Assert.Single(_collections.Fake.Calls(nameof(ICollectionManager.AddToCollectionAsync)));
        Assert.Equal(boxSet.Id, add[0]);
        Assert.Equal(new[] { added }, Ids(add[1]));
        var remove = Assert.Single(_collections.Fake.Calls(nameof(ICollectionManager.RemoveFromCollectionAsync)));
        Assert.Equal(new[] { departed }, Ids(remove[1]));
    }

    [Fact]
    public async Task An_unchanged_collection_makes_no_calls()
    {
        var kept = CurrentsItem("movie/tt1");
        ExistingCollection(kept);

        await Create().SyncAsync([new CollectionPlan(CatalogKey, "Popular", [Title("movie/tt1")])], CancellationToken.None);

        Assert.Empty(_collections.Fake.Calls(nameof(ICollectionManager.AddToCollectionAsync)));
        Assert.Empty(_collections.Fake.Calls(nameof(ICollectionManager.RemoveFromCollectionAsync)));
    }

    [Fact]
    public async Task A_name_taken_by_a_collection_currents_does_not_own_gets_a_suffix()
    {
        _boxSets.Add(new BoxSet { Id = Guid.NewGuid(), Name = "Popular" });
        CurrentsItem("movie/tt1");

        await Create().SyncAsync([new CollectionPlan(CatalogKey, "Popular", [Title("movie/tt1")])], CancellationToken.None);

        var options = (CollectionCreationOptions)Assert.Single(_collections.Fake.Calls(nameof(ICollectionManager.CreateCollectionAsync)))[0]!;
        Assert.Equal("Popular (Currents)", options.Name);
    }

    [Fact]
    public async Task No_plans_do_nothing()
    {
        await Create().SyncAsync([], CancellationToken.None);

        Assert.Empty(_collections.Fake.Calls(nameof(ICollectionManager.GetCollectionsFolder)));
    }
}
