using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Search;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Search;

public sealed class SearchTitleOpenerTests : IDisposable
{
    private static readonly Guid Alice = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly Guid Item = Guid.Parse("11111111111111111111111111111111");
    private readonly FakeSettings _settings = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero));
    private readonly FakeAioMetadataClient _client = new();
    private readonly FakeLibraryItems _library = new();
    private readonly SearchResultRegistry _registry;
    private TitleLibrary _titles;

    public SearchTitleOpenerTests()
    {
        _settings.Current.AioMetadataManifestUrl = "https://meta.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/manifest.json";
        _settings.Current.LibraryRoot = Path.Combine(_settings.DataFolderPath, "library");
        _registry = new SearchResultRegistry(_time);
        _titles = new TitleLibrary(_settings, _time, NullLogger<TitleLibrary>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private SearchTitleOpener Create() => new(_titles, _registry, _library, _client, _settings, NullLogger<SearchTitleOpener>.Instance);

    private SearchResult Shown(MediaKind kind, string imdb, string name, string catalogType = "movie")
    {
        var result = SearchResults.For(new TitleKey(kind, "imdb", imdb), new StremioMeta { Id = imdb, Name = name, Year = "1999" }, catalogType);
        _registry.Add(result);
        return result;
    }

    private string MoviesDir => Path.Combine(_settings.Current.LibraryRoot, "Movies");

    [Fact]
    public async Task Unknown_ids_are_not_search_ids()
    {
        var outcome = await Create().OpenAsync(Guid.NewGuid(), Alice, mayAdd: () => true, CancellationToken.None);

        Assert.Equal(OpenStatus.NotSearchId, outcome.Status);
    }

    [Fact]
    public async Task An_unknown_id_needs_neither_the_state_lock_nor_the_users_permission()
    {
        _titles.AddFromSearch(new TitleKey(MediaKind.Movie, "imdb", "tt1"), new StremioMeta { Id = "tt1", Name = "Loaded" });
        var opener = Create();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() => _titles.Use(_ =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        }));
        entered.Wait(TimeSpan.FromSeconds(10));
        var asked = false;

        var open = Task.Run(() => opener.OpenAsync(Guid.NewGuid(), Alice, () => asked = true, CancellationToken.None));
        var finishedWhileLocked = await Task.WhenAny(open, Task.Delay(TimeSpan.FromSeconds(5))) == open;
        release.Set();
        await holder;

        Assert.True(finishedWhileLocked);
        Assert.Equal(OpenStatus.NotSearchId, (await open).Status);
        Assert.False(asked);
    }

    [Fact]
    public async Task Opening_an_existing_title_never_asks_whether_the_user_may_add()
    {
        _titles.Use(s => s.Upsert(new TitleState { StateId = "movie/tt1", StremioId = "tt1", Folder = "Movies/A [imdbid-tt1]", Catalogs = ["movie/top"] }));
        _library.Titles["movie/tt1"] = Item;
        var shown = Shown(MediaKind.Movie, "tt0133093", "The Matrix");
        _library.Existing[(Alice, "movie/tt0133093")] = Item;
        var asked = 0;

        Assert.Equal(OpenOutcome.Opened(Item), await Create().OpenAsync(SearchItemId.For(FakeSettings.Secret, "movie/tt1"), Guid.Empty, () => ++asked > 0, CancellationToken.None));
        Assert.Equal(OpenOutcome.Opened(Item), await Create().OpenAsync(shown.Id, Alice, () => ++asked > 0, CancellationToken.None));
        Assert.Equal(OpenStatus.NotAllowed, (await Create().OpenAsync(Shown(MediaKind.Movie, "tt2", "B").Id, Guid.Empty, () => ++asked > 0, CancellationToken.None)).Status);
        Assert.Equal(0, asked);
    }

    [Fact]
    public async Task Adds_a_movie_and_returns_its_item()
    {
        var shown = Shown(MediaKind.Movie, "tt0133093", "The Matrix");
        _client.Metas["movie/tt0133093"] = new StremioMeta { Id = "tt0133093", Name = "The Matrix", Year = "1999" };
        _library.AddResult = (_, _) => Item;

        var outcome = await Create().OpenAsync(shown.Id, Alice, mayAdd: () => true, CancellationToken.None);

        Assert.Equal(OpenOutcome.Opened(Item), outcome);
        Assert.True(File.Exists(Path.Combine(MoviesDir, "The Matrix (1999) [imdbid-tt0133093]", "The Matrix (1999).strm")));
        Assert.True(_titles.Get("movie/tt0133093")!.AddedBySearch);
        Assert.Equal((MediaKind.Movie, Path.Combine("Movies", "The Matrix (1999) [imdbid-tt0133093]")), Assert.Single(_library.Added));
    }

    [Fact]
    public async Task A_movie_falls_back_to_the_search_meta()
    {
        var shown = Shown(MediaKind.Movie, "tt0133093", "The Matrix");
        _client.FailingMetas.Add("movie/tt0133093");

        var outcome = await Create().OpenAsync(shown.Id, Alice, mayAdd: () => true, CancellationToken.None);

        Assert.Equal(OpenStatus.Opened, outcome.Status);
        Assert.True(Directory.Exists(Path.Combine(MoviesDir, "The Matrix (1999) [imdbid-tt0133093]")));
    }

    [Fact]
    public async Task Meta_is_fetched_with_the_title_type_then_the_catalog_type()
    {
        var shown = Shown(MediaKind.Series, "tt0388629", "One Piece", catalogType: "anime.series");
        _client.Metas["anime.series/tt0388629"] = new StremioMeta
        {
            Id = "tt0388629",
            Name = "One Piece",
            Year = "1999",
            Videos = [new StremioVideo { Id = "tt0388629:1:1", Season = 1, Episode = 1, Released = "1999-10-20T00:00:00Z" }],
        };

        var outcome = await Create().OpenAsync(shown.Id, Alice, mayAdd: () => true, CancellationToken.None);

        Assert.Equal(OpenStatus.Opened, outcome.Status);
        Assert.Equal(new[] { "series/tt0388629", "anime.series/tt0388629" }, _client.MetaRequests);
        Assert.True(File.Exists(Path.Combine(_settings.Current.LibraryRoot, "Shows", "One Piece (1999) [imdbid-tt0388629]", "Season 01", "One Piece (1999) S01E01.strm")));
    }

    [Fact]
    public async Task A_series_without_a_meta_fails_and_writes_nothing()
    {
        var shown = Shown(MediaKind.Series, "tt0944947", "Game of Thrones", catalogType: "series");

        var outcome = await Create().OpenAsync(shown.Id, Alice, mayAdd: () => true, CancellationToken.None);

        Assert.Equal(OpenStatus.Failed, outcome.Status);
        Assert.Equal("AIOMetadata has no episode list for Game of Thrones right now.", outcome.Message);
        Assert.Null(_titles.Get("series/tt0944947"));
        Assert.Empty(_library.Added);
    }

    [Fact]
    public async Task Existing_currents_title_is_opened_not_rewritten()
    {
        _titles.Use(s => s.Upsert(new TitleState { StateId = "movie/tt1", StremioId = "tt1", Folder = "Movies/A [imdbid-tt1]", Catalogs = ["movie/top"] }));
        _library.Titles["movie/tt1"] = Item;

        var outcome = await Create().OpenAsync(SearchItemId.For(FakeSettings.Secret, "movie/tt1"), Alice, mayAdd: () => true, CancellationToken.None);

        Assert.Equal(OpenOutcome.Opened(Item), outcome);
        Assert.Empty(_client.MetaRequests);
        Assert.Empty(_library.Added);
        Assert.False(Directory.Exists(MoviesDir));
    }

    [Fact]
    public async Task Users_own_item_with_the_same_imdb_id_is_opened()
    {
        var shown = Shown(MediaKind.Movie, "tt0133093", "The Matrix");
        _library.Existing[(Alice, "movie/tt0133093")] = Item;

        var outcome = await Create().OpenAsync(shown.Id, Alice, mayAdd: () => true, CancellationToken.None);

        Assert.Equal(OpenOutcome.Opened(Item), outcome);
        Assert.Null(_titles.Get("movie/tt0133093"));
        Assert.Empty(_library.Added);
    }

    [Fact]
    public async Task User_without_search_add_cannot_add_but_can_open_existing()
    {
        var shown = Shown(MediaKind.Movie, "tt0133093", "The Matrix");

        Assert.Equal(OpenStatus.NotAllowed, (await Create().OpenAsync(shown.Id, Alice, mayAdd: () => false, CancellationToken.None)).Status);
        Assert.Null(_titles.Get("movie/tt0133093"));

        _library.Existing[(Alice, "movie/tt0133093")] = Item;
        Assert.Equal(OpenOutcome.Opened(Item), await Create().OpenAsync(shown.Id, Alice, mayAdd: () => false, CancellationToken.None));
    }

    [Fact]
    public async Task Nobody_adds_when_no_visible_library_holds_the_folder()
    {
        var shown = Shown(MediaKind.Movie, "tt0133093", "The Matrix");
        _library.Addable.Clear();

        Assert.Equal(OpenStatus.NotAllowed, (await Create().OpenAsync(shown.Id, Alice, mayAdd: () => true, CancellationToken.None)).Status);
        Assert.Equal(OpenStatus.NotAllowed, (await Create().OpenAsync(shown.Id, Guid.Empty, mayAdd: () => true, CancellationToken.None)).Status);
        Assert.Null(_titles.Get("movie/tt0133093"));
    }

    [Fact]
    public async Task Concurrent_opens_add_the_title_once()
    {
        var shown = Shown(MediaKind.Movie, "tt0133093", "The Matrix");
        _client.Metas["movie/tt0133093"] = new StremioMeta { Id = "tt0133093", Name = "The Matrix", Year = "1999" };
        _client.MetaGate = new TaskCompletionSource();
        _library.AddResult = (_, _) => Item;
        var opener = Create();

        var first = opener.OpenAsync(shown.Id, Alice, mayAdd: () => true, CancellationToken.None);
        var second = opener.OpenAsync(shown.Id, Alice, mayAdd: () => true, CancellationToken.None);
        _client.MetaGate.SetResult();

        Assert.Equal(OpenOutcome.Opened(Item), await first);
        Assert.Equal(OpenOutcome.Opened(Item), await second);
        Assert.Single(_client.MetaRequests);
        Assert.Single(_library.Added);
    }

    [Fact]
    public async Task A_cancelled_caller_does_not_cancel_the_add()
    {
        var shown = Shown(MediaKind.Movie, "tt0133093", "The Matrix");
        _client.Metas["movie/tt0133093"] = new StremioMeta { Id = "tt0133093", Name = "The Matrix", Year = "1999" };
        _library.AddGate = new TaskCompletionSource();
        _library.AddResult = (_, _) => Item;
        var opener = Create();
        using var cancel = new CancellationTokenSource();

        var cancelled = opener.OpenAsync(shown.Id, Alice, mayAdd: () => true, cancel.Token);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        var waiting = opener.OpenAsync(shown.Id, Alice, mayAdd: () => true, CancellationToken.None);
        _library.AddGate.SetResult();

        Assert.Equal(OpenOutcome.Opened(Item), await waiting);
        Assert.Single(_library.Added);
    }

    [Fact]
    public async Task Materialized_title_resolves_from_state_after_restart()
    {
        var shown = Shown(MediaKind.Movie, "tt0133093", "The Matrix");
        _client.Metas["movie/tt0133093"] = new StremioMeta { Id = "tt0133093", Name = "The Matrix", Year = "1999" };
        _library.AddResult = (_, _) => Item;
        await Create().OpenAsync(shown.Id, Alice, mayAdd: () => true, CancellationToken.None);

        // Restart: a new state instance and an empty result registry; Jellyfin has the item.
        _titles = new TitleLibrary(_settings, _time, NullLogger<TitleLibrary>.Instance);
        var opener = new SearchTitleOpener(_titles, new SearchResultRegistry(_time), _library, _client, _settings, NullLogger<SearchTitleOpener>.Instance);
        _library.Titles["movie/tt0133093"] = Item;

        Assert.Equal(OpenOutcome.Opened(Item), await opener.OpenAsync(shown.Id, Guid.Empty, mayAdd: () => false, CancellationToken.None));
    }

    [Fact]
    public async Task Known_title_missing_from_jellyfin_is_added_from_its_folder()
    {
        var folder = _titles.AddFromSearch(new TitleKey(MediaKind.Movie, "imdb", "tt1"), new StremioMeta { Id = "tt1", Name = "A" }).RelativeFolder;
        _library.AddResult = (_, _) => Item;

        var outcome = await Create().OpenAsync(SearchItemId.For(FakeSettings.Secret, "movie/tt1"), Alice, mayAdd: () => true, CancellationToken.None);

        Assert.Equal(OpenOutcome.Opened(Item), outcome);
        Assert.Equal((MediaKind.Movie, folder), Assert.Single(_library.Added));
        Assert.Empty(_client.MetaRequests);
    }

    [Fact]
    public async Task A_known_movie_whose_folder_was_deleted_is_added_again()
    {
        var matrix = new StremioMeta { Id = "tt0133093", Name = "The Matrix", Year = "1999" };
        var folder = _titles.AddFromSearch(new TitleKey(MediaKind.Movie, "imdb", "tt0133093"), matrix).RelativeFolder;
        Directory.Delete(Path.Combine(_settings.Current.LibraryRoot, folder), recursive: true); // deleted in Jellyfin
        _client.Metas["movie/tt0133093"] = matrix;
        _library.AddResult = (_, _) => Item;

        // After a restart: the result registry is empty, the state still knows the title.
        var opener = new SearchTitleOpener(_titles, new SearchResultRegistry(_time), _library, _client, _settings, NullLogger<SearchTitleOpener>.Instance);
        var outcome = await opener.OpenAsync(SearchItemId.For(FakeSettings.Secret, "movie/tt0133093"), Alice, () => true, CancellationToken.None);

        Assert.Equal(OpenOutcome.Opened(Item), outcome);
        Assert.Equal(new[] { "movie/tt0133093" }, _client.MetaRequests);
        Assert.True(File.Exists(Path.Combine(_settings.Current.LibraryRoot, folder, "The Matrix (1999).strm")));
        Assert.Equal((MediaKind.Movie, folder), Assert.Single(_library.Added));
        Assert.True(_titles.Get("movie/tt0133093")!.AddedBySearch);
    }

    [Fact]
    public async Task A_known_movie_whose_strm_was_deleted_is_written_again_from_the_shown_result()
    {
        var shown = Shown(MediaKind.Movie, "tt0133093", "The Matrix");
        var folder = _titles.AddFromSearch(shown.Key, shown.Meta).RelativeFolder;
        var strm = Path.Combine(_settings.Current.LibraryRoot, folder, "The Matrix (1999).strm");
        File.Delete(strm);
        _client.FailingMetas.Add("movie/tt0133093");
        _library.AddResult = (_, _) => Item;

        var outcome = await Create().OpenAsync(shown.Id, Alice, () => true, CancellationToken.None);

        Assert.Equal(OpenOutcome.Opened(Item), outcome);
        Assert.True(File.Exists(strm));
        Assert.Equal((MediaKind.Movie, folder), Assert.Single(_library.Added));
    }

    [Fact]
    public async Task A_known_series_whose_folder_was_deleted_fails_without_a_meta_and_writes_nothing()
    {
        var key = new TitleKey(MediaKind.Series, "imdb", "tt0944947");
        var folder = _titles.AddFromSearch(key, new StremioMeta { Id = "tt0944947", Name = "Game of Thrones", ReleaseInfo = "2011" }).RelativeFolder;
        Directory.Delete(Path.Combine(_settings.Current.LibraryRoot, folder), recursive: true);

        var outcome = await Create().OpenAsync(SearchItemId.For(FakeSettings.Secret, "series/tt0944947"), Alice, () => true, CancellationToken.None);

        Assert.Equal(OpenStatus.Failed, outcome.Status);
        Assert.False(Directory.Exists(Path.Combine(_settings.Current.LibraryRoot, folder)));
        Assert.Empty(_library.Added);
    }

    [Fact]
    public async Task Library_not_holding_the_folder_fails_with_advice()
    {
        var shown = Shown(MediaKind.Movie, "tt0133093", "The Matrix");
        _library.AddResult = (_, _) => null;

        var outcome = await Create().OpenAsync(shown.Id, Alice, mayAdd: () => true, CancellationToken.None);

        Assert.Equal(OpenOutcome.Failed("Add the Currents Movies folder to a Jellyfin library first."), outcome);
    }

    [Fact]
    public async Task An_unmanaged_folder_of_the_same_name_fails_and_is_left_alone()
    {
        var shown = Shown(MediaKind.Movie, "tt0133093", "The Matrix");
        var folder = Directory.CreateDirectory(Path.Combine(MoviesDir, "The Matrix (1999) [imdbid-tt0133093]")).FullName;
        File.WriteAllText(Path.Combine(folder, "poster.jpg"), "user file");

        var outcome = await Create().OpenAsync(shown.Id, Alice, mayAdd: () => true, CancellationToken.None);

        Assert.Equal(OpenStatus.Failed, outcome.Status);
        Assert.Equal(new[] { "poster.jpg" }, Directory.GetFiles(folder).Select(Path.GetFileName));
        Assert.Null(_titles.Get("movie/tt0133093"));
    }

    [Fact]
    public async Task The_monitor_is_paused_before_the_title_is_written_and_resumed_after_it_is_added()
    {
        var shown = Shown(MediaKind.Movie, "tt0133093", "The Matrix");
        var titleFolder = Path.Combine(MoviesDir, "The Matrix (1999) [imdbid-tt0133093]");
        var existedAtPause = true;
        _library.OnPause = _ => existedAtPause = Directory.Exists(titleFolder);

        await Create().OpenAsync(shown.Id, Alice, mayAdd: () => true, CancellationToken.None);

        Assert.False(existedAtPause);
        Assert.Equal(new[] { "pause:Movie", "add", "resume:Movie" }, _library.Events);
    }

    [Fact]
    public async Task The_monitor_is_resumed_when_the_add_fails()
    {
        var shown = Shown(MediaKind.Movie, "tt0133093", "The Matrix");
        _library.AddResult = (_, _) => null;

        await Create().OpenAsync(shown.Id, Alice, mayAdd: () => true, CancellationToken.None);

        Assert.Equal(new[] { "pause:Movie", "add", "resume:Movie" }, _library.Events);
    }
}
