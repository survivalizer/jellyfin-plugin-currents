using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Integration;

public sealed class JellyfinLibraryItemsTests : IDisposable
{
    private static readonly Guid MoviesLibrary = Guid.Parse("cccccccccccccccccccccccccccccccc");
    private readonly FakeSettings _settings = new();
    private readonly (ILibraryManager Instance, InterfaceFake Fake) _library = InterfaceFake.Create<ILibraryManager>();
    private readonly (IUserManager Instance, InterfaceFake Fake) _users = InterfaceFake.Create<IUserManager>();
    private readonly (IProviderManager Instance, InterfaceFake Fake) _providers = InterfaceFake.Create<IProviderManager>();
    private readonly (ILibraryMonitor Instance, InterfaceFake Fake) _monitor = InterfaceFake.Create<ILibraryMonitor>();
    private readonly (IFileSystem Instance, InterfaceFake Fake) _fileSystem = InterfaceFake.Create<IFileSystem>();
    private readonly User _alice = new("alice", "Default", "Default");
    private readonly Folder _moviesFolder;
    private readonly HashSet<Guid> _created = [];
    private readonly Dictionary<Guid, string> _paths = [];

    public JellyfinLibraryItemsTests()
    {
        _settings.Current.LibraryRoot = Path.Combine(_settings.DataFolderPath, "library");
        var paths = LibraryPaths.FromSettings(_settings);
        _moviesFolder = new Folder { Id = Guid.NewGuid(), Path = paths.Movies };
        _users.Fake.On(nameof(IUserManager.GetUserById), args => (Guid)args[0]! == _alice.Id ? _alice : null);
        _library.Fake
            .On(nameof(ILibraryManager.FindByPath), args => (string)args[0]! == paths.Movies && (bool?)args[1] == true ? _moviesFolder : null)
            .On(nameof(ILibraryManager.GetContentType), _ => CollectionType.movies)
            .On(nameof(ILibraryManager.GetCollectionFolders), args => args[0] == _moviesFolder ? new List<Folder> { new CollectionFolder { Id = MoviesLibrary } } : new List<Folder>())
            .On(nameof(ILibraryManager.GetItemById), args => _created.Contains((Guid)args[0]!) ? new Movie { Id = (Guid)args[0]!, Path = _paths.GetValueOrDefault((Guid)args[0]!) ?? Path.Combine(paths.Movies, "X", "x.strm") } : null)
            .On(nameof(ILibraryManager.CreateItem), args =>
            {
                _created.Add(((BaseItem)args[0]!).Id);
                return null;
            })
            .On(nameof(ILibraryManager.GetItemList), _ => new List<BaseItem>());
        _fileSystem.Fake.On(nameof(IFileSystem.GetDirectoryInfo), args => new FileSystemMetadata { FullName = (string)args[0]!, IsDirectory = true, Exists = true });
    }

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private JellyfinLibraryItems Create(TimeSpan? refresh = null) => new(
        _library.Instance,
        _users.Instance,
        _providers.Instance,
        _monitor.Instance,
        _fileSystem.Instance,
        _settings,
        NullLogger<JellyfinLibraryItems>.Instance,
        refresh ?? TimeSpan.FromSeconds(15),
        refresh ?? TimeSpan.FromSeconds(60));

    [Fact]
    public void Remove_items_deletes_found_items_from_the_database_only_and_skips_missing_ids()
    {
        var found = Guid.NewGuid();
        var missing = Guid.NewGuid();
        _created.Add(found);

        Create().RemoveItems([missing, found]);

        var call = Assert.Single(_library.Fake.Calls(nameof(ILibraryManager.DeleteItem)));
        Assert.Equal(found, ((BaseItem)call[0]!).Id);
        Assert.False(((DeleteOptions)call[1]!).DeleteFileLocation);
        Assert.Equal(true, call[2]);
    }

    [Fact]
    public void Remove_items_leaves_items_outside_the_currents_library()
    {
        var own = Guid.NewGuid();
        _created.Add(own);
        _paths[own] = Path.Combine(_settings.DataFolderPath, "elsewhere", "film.mkv");

        Create().RemoveItems([own]);

        Assert.Empty(_library.Fake.Calls(nameof(ILibraryManager.DeleteItem)));
    }

    private string Root => LibraryPaths.FromSettings(_settings).Root;

    [Fact]
    public void Find_title_looks_up_the_movie_strm_and_the_series_folder()
    {
        var movie = new Movie { Id = Guid.NewGuid() };
        var series = new Series { Id = Guid.NewGuid() };
        var strm = Path.Combine(Root, "Movies", "A (2000) [imdbid-tt1]", "A (2000).strm");
        var show = Path.Combine(Root, "Shows", "B [imdbid-tt2]");
        _library.Fake.On(nameof(ILibraryManager.FindByPath), args => (string)args[0]! == strm && (bool?)args[1] == false ? movie
            : (string)args[0]! == show && (bool?)args[1] == true ? series : null);

        var items = Create();

        Assert.Equal(movie.Id, items.FindTitle(new TitleState { StateId = "movie/tt1", StremioId = "tt1", Kind = MediaKind.Movie, Folder = Path.Combine("Movies", "A (2000) [imdbid-tt1]") }));
        Assert.Equal(series.Id, items.FindTitle(new TitleState { StateId = "series/tt2", StremioId = "tt2", Kind = MediaKind.Series, Folder = Path.Combine("Shows", "B [imdbid-tt2]") }));
    }

    [Fact]
    public void Find_title_falls_back_to_the_currents_provider_id()
    {
        var movie = new Movie { Id = Guid.NewGuid() };
        _library.Fake.On(nameof(ILibraryManager.GetItemList), _ => new List<BaseItem> { movie });

        var id = Create().FindTitle(new TitleState { StateId = "movie/mal:5", StremioId = "mal:5", Kind = MediaKind.Movie, Folder = Path.Combine("Movies", "X [mal-5]") });

        Assert.Equal(movie.Id, id);
        var query = (InternalItemsQuery)Assert.Single(_library.Fake.Calls(nameof(ILibraryManager.GetItemList)))[0]!;
        Assert.Equal(new[] { BaseItemKind.Movie }, query.IncludeItemTypes);
        Assert.Equal(new[] { "mal:5" }, query.HasAnyProviderIds!["Currents"]);
    }

    [Fact]
    public void Find_existing_asks_once_for_all_ids_and_matches_kind_and_id()
    {
        var movie = new Movie { Id = Guid.NewGuid(), ProviderIds = new() { ["Imdb"] = "tt1" } };
        var series = new Series { Id = Guid.NewGuid(), ProviderIds = new() { ["Tmdb"] = "2" } };
        var wrongKind = new Series { Id = Guid.NewGuid(), ProviderIds = new() { ["Imdb"] = "tt1" } };
        _library.Fake.On(nameof(ILibraryManager.GetItemList), _ => new List<BaseItem> { wrongKind, movie, series });

        var found = Create().FindExisting(_alice.Id, [new TitleKey(MediaKind.Movie, "imdb", "tt1"), new TitleKey(MediaKind.Series, "tmdb", "2"), new TitleKey(MediaKind.Movie, "imdb", "tt3")]);

        Assert.Equal(new Dictionary<string, Guid> { ["movie/tt1"] = movie.Id, ["series/tmdb:2"] = series.Id }, found);
        var query = (InternalItemsQuery)Assert.Single(_library.Fake.Calls(nameof(ILibraryManager.GetItemList)))[0]!;
        Assert.Same(_alice, query.User);
        Assert.True(query.Recursive);
        Assert.Equal(new[] { BaseItemKind.Movie, BaseItemKind.Series }, query.IncludeItemTypes);
        Assert.Equal(new[] { "tt1", "tt3" }, query.HasAnyProviderIds!["Imdb"]);
        Assert.Equal(new[] { "2" }, query.HasAnyProviderIds["Tmdb"]);
        Assert.Equal(new[] { "tt1", "tmdb:2", "tt3" }, query.HasAnyProviderIds["Currents"]);
    }

    [Fact]
    public void Find_existing_for_an_unknown_user_asks_nothing()
    {
        Assert.Empty(Create().FindExisting(Guid.NewGuid(), [new TitleKey(MediaKind.Movie, "imdb", "tt1")]));
        Assert.Empty(_library.Fake.Calls(nameof(ILibraryManager.GetItemList)));
    }

    [Fact]
    public void Can_add_needs_a_known_user_a_library_and_the_right_content_type()
    {
        Assert.False(Create().CanAdd(Guid.NewGuid(), MediaKind.Movie)); // unknown user
        Assert.False(Create().CanAdd(_alice.Id, MediaKind.Series));      // no library holds Shows/

        _library.Fake.On(nameof(ILibraryManager.GetContentType), _ => CollectionType.tvshows);
        Assert.False(Create().CanAdd(_alice.Id, MediaKind.Movie));       // Movies/ is in a TV library
    }

    [Fact]
    public void Can_add_for_a_user_without_parental_controls_who_sees_the_library()
    {
        UsePlainLibraryFolder();

        Assert.True(Create().CanAdd(_alice.Id, MediaKind.Movie));
    }

    [Theory]
    [InlineData("rating")]
    [InlineData("unrated")]
    [InlineData("blocked-tags")]
    [InlineData("allowed-tags")]
    public void Users_with_parental_controls_can_never_add(string restriction)
    {
        switch (restriction)
        {
            case "rating":
                _alice.MaxParentalRatingScore = 13;
                break;
            case "unrated":
                _alice.SetPreference(PreferenceKind.BlockUnratedItems, new[] { UnratedItem.Movie });
                break;
            case "blocked-tags":
                _alice.SetPreference(PreferenceKind.BlockedTags, ["gore"]);
                break;
            case "allowed-tags":
                _alice.SetPreference(PreferenceKind.AllowedTags, ["kids"]);
                break;
        }

        UsePlainLibraryFolder();
        Assert.False(Create().CanAdd(_alice.Id, MediaKind.Movie));
    }

    [Fact]
    public void Kinds_in_maps_a_library_to_its_currents_folders()
    {
        Assert.Equal(new[] { MediaKind.Movie }, Create().KindsIn(MoviesLibrary));
        Assert.Empty(Create().KindsIn(Guid.NewGuid()));
    }

    [Fact]
    public void Pause_monitoring_ignores_the_kind_root_until_disposed()
    {
        var handle = Create().PauseMonitoring(MediaKind.Movie);

        var moviesRoot = LibraryPaths.FromSettings(_settings).Movies;
        Assert.Equal(moviesRoot, Assert.Single(_monitor.Fake.Calls(nameof(ILibraryMonitor.ReportFileSystemChangeBeginning)))[0]);
        Assert.Empty(_monitor.Fake.Calls(nameof(ILibraryMonitor.ReportFileSystemChangeComplete)));

        handle.Dispose();
        handle.Dispose();

        var complete = Assert.Single(_monitor.Fake.Calls(nameof(ILibraryMonitor.ReportFileSystemChangeComplete)));
        Assert.Equal((object?)moviesRoot, complete[0]);
        Assert.Equal(false, complete[1]);
    }

    [Fact]
    public async Task Add_returns_null_when_no_library_holds_the_folder()
    {
        Assert.Null(await Create().AddAsync(MediaKind.Series, Path.Combine("Shows", "B [imdbid-tt2]"), CancellationToken.None));
        Assert.Empty(_library.Fake.Calls(nameof(ILibraryManager.ResolvePath)));
    }

    [Fact]
    public async Task Add_resolves_only_the_title_folder_creates_and_refreshes_it()
    {
        var folder = Path.Combine(Root, "Movies", "A (2000) [imdbid-tt1]");
        var movie = new Movie { Id = Guid.NewGuid(), Path = Path.Combine(folder, "A (2000).strm") };
        _library.Fake.On(nameof(ILibraryManager.ResolvePath), _ => movie);

        var id = await Create().AddAsync(MediaKind.Movie, Path.Combine("Movies", "A (2000) [imdbid-tt1]"), CancellationToken.None);

        Assert.Equal(movie.Id, id);
        var resolve = Assert.Single(_library.Fake.Calls(nameof(ILibraryManager.ResolvePath)));
        Assert.Equal(folder, ((FileSystemMetadata)resolve[0]!).FullName);
        Assert.Same(_moviesFolder, resolve[1]);
        Assert.IsType<DirectoryService>(resolve[2]);
        Assert.Equal(CollectionType.movies, resolve[3]);
        var create = Assert.Single(_library.Fake.Calls(nameof(ILibraryManager.CreateItem)));
        Assert.Same(movie, create[0]);
        Assert.Same(_moviesFolder, create[1]);
        Assert.Same(movie, Assert.Single(_providers.Fake.Calls(nameof(IProviderManager.RefreshSingleItem)))[0]);
        Assert.Empty(_providers.Fake.Calls(nameof(IProviderManager.QueueRefresh)));
        Assert.Empty(_monitor.Fake.Calls(nameof(ILibraryMonitor.ReportFileSystemChangeBeginning)));
        Assert.Empty(_monitor.Fake.Calls(nameof(ILibraryMonitor.ReportFileSystemChangeComplete)));
    }

    [Fact]
    public async Task Add_does_not_create_an_item_jellyfin_already_has()
    {
        var movie = new Movie { Id = Guid.NewGuid() };
        _created.Add(movie.Id);
        _library.Fake.On(nameof(ILibraryManager.ResolvePath), _ => movie);

        Assert.Equal(movie.Id, await Create().AddAsync(MediaKind.Movie, Path.Combine("Movies", "A [imdbid-tt1]"), CancellationToken.None));
        Assert.Empty(_library.Fake.Calls(nameof(ILibraryManager.CreateItem)));
    }

    [Fact]
    public async Task Series_are_refreshed_with_their_seasons_and_episodes()
    {
        var showsFolder = new Folder { Id = Guid.NewGuid(), Path = LibraryPaths.FromSettings(_settings).Shows };
        var series = new Series { Id = Guid.NewGuid() };
        _library.Fake
            .On(nameof(ILibraryManager.FindByPath), args => (string)args[0]! == showsFolder.Path ? showsFolder : null)
            .On(nameof(ILibraryManager.GetContentType), _ => CollectionType.tvshows)
            .On(nameof(ILibraryManager.ResolvePath), _ => series);

        Assert.Equal(series.Id, await Create().AddAsync(MediaKind.Series, Path.Combine("Shows", "B [imdbid-tt2]"), CancellationToken.None));
        Assert.Same(series, Assert.Single(_providers.Fake.Calls(nameof(IProviderManager.RefreshFullItem)))[0]);
        Assert.Empty(_providers.Fake.Calls(nameof(IProviderManager.RefreshSingleItem)));
    }

    [Fact]
    public async Task A_slow_refresh_is_queued_and_the_item_is_returned()
    {
        var movie = new Movie { Id = Guid.NewGuid() };
        _library.Fake.On(nameof(ILibraryManager.ResolvePath), _ => movie);
        _providers.Fake.On(nameof(IProviderManager.RefreshSingleItem), args => Hang((CancellationToken)args[2]!));

        var id = await Create(TimeSpan.FromMilliseconds(50)).AddAsync(MediaKind.Movie, Path.Combine("Movies", "A [imdbid-tt1]"), CancellationToken.None);

        Assert.Equal(movie.Id, id);
        var queued = Assert.Single(_providers.Fake.Calls(nameof(IProviderManager.QueueRefresh)));
        Assert.Equal(movie.Id, queued[0]);
        Assert.Equal(RefreshPriority.High, queued[2]);
    }

    [Fact]
    public async Task An_item_removed_by_a_concurrent_scan_is_added_back_once()
    {
        var movie = new Movie { Id = Guid.NewGuid() };
        _library.Fake
            .On(nameof(ILibraryManager.ResolvePath), _ => movie)
            .On(nameof(ILibraryManager.GetItemById), _ => null); // the scan keeps deleting it

        await Create().AddAsync(MediaKind.Movie, Path.Combine("Movies", "A [imdbid-tt1]"), CancellationToken.None);

        Assert.Equal(2, _library.Fake.Calls(nameof(ILibraryManager.CreateItem)).Count);
    }

    [Fact]
    public async Task A_series_added_back_after_a_scan_gets_a_queued_refresh_for_its_seasons()
    {
        var showsFolder = new Folder { Id = Guid.NewGuid(), Path = LibraryPaths.FromSettings(_settings).Shows };
        var series = new Series { Id = Guid.NewGuid() };
        _library.Fake
            .On(nameof(ILibraryManager.FindByPath), args => (string)args[0]! == showsFolder.Path ? showsFolder : null)
            .On(nameof(ILibraryManager.GetContentType), _ => CollectionType.tvshows)
            .On(nameof(ILibraryManager.ResolvePath), _ => series)
            .On(nameof(ILibraryManager.GetItemById), _ => null); // a scan removed it during the refresh

        await Create().AddAsync(MediaKind.Series, Path.Combine("Shows", "B [imdbid-tt2]"), CancellationToken.None);

        var queued = Assert.Single(_providers.Fake.Calls(nameof(IProviderManager.QueueRefresh)));
        Assert.Equal(series.Id, queued[0]);
        Assert.Equal(RefreshPriority.High, queued[2]);
    }

    [Fact]
    public async Task An_unresolvable_folder_throws()
    {
        _library.Fake.On(nameof(ILibraryManager.ResolvePath), _ => null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Create().AddAsync(MediaKind.Movie, Path.Combine("Movies", "A [imdbid-tt1]"), CancellationToken.None));

        Assert.Equal("Jellyfin did not recognise A [imdbid-tt1] as a title.", error.Message);
        Assert.Empty(_monitor.Fake.Calls(nameof(ILibraryMonitor.ReportFileSystemChangeComplete)));
    }

    // A CollectionFolder's IsVisible reads library options through Jellyfin's static XML serializer; a plain Folder
    // runs the same user checks (IsParentalAllowed) without it.
    private void UsePlainLibraryFolder() =>
        _library.Fake.On(nameof(ILibraryManager.GetCollectionFolders), args => args[0] == _moviesFolder ? new List<Folder> { new() { Id = MoviesLibrary } } : new List<Folder>());

    private static async Task<ItemUpdateType> Hang(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        return ItemUpdateType.None;
    }
}
