using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Configuration;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Library;

public sealed class CatalogSyncServiceTests : IDisposable
{
    private const string MovieCatalog = "movie/tmdb.top";
    private const string ShowCatalog = "series/tmdb.trending";

    private static readonly ManualTimeProvider Time = new(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "currents-sync-" + Guid.NewGuid().ToString("N"));
    private readonly FakeAioMetadataClient _client = new();
    private readonly FakePlayedLookup _played = new();
    private readonly FakeRefresher _refresher = new();
    private readonly FakeSettings _settings;
    private readonly TitleLibrary _titles;

    public CatalogSyncServiceTests()
    {
        _settings = new FakeSettings { DataFolderPath = Path.Combine(_root, "data") };
        _titles = new TitleLibrary(_settings, Time, NullLogger<TitleLibrary>.Instance);
        _settings.Current.AioMetadataManifestUrl = "https://meta.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/manifest.json";
        _settings.Current.LibraryRoot = Path.Combine(_root, "library");
        _settings.Current.Catalogs =
        [
            new CatalogSelection { Type = "movie", Id = "tmdb.top", Target = CatalogTarget.Movies, MaxItems = 100 },
            new CatalogSelection { Type = "series", Id = "tmdb.trending", Target = CatalogTarget.Shows, MaxItems = 100 },
        ];
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private CatalogSyncService CreateService() =>
        new(_client, _played, _refresher, _settings, _titles, Time, NullLogger<CatalogSyncService>.Instance);

    private Task<SyncReport> SyncAsync() => CreateService().SyncAsync(new Progress<double>(), CancellationToken.None);

    private static StremioMeta Movie(string id, string name) => new() { Id = id, Name = name, Year = "2000" };

    private string MoviesDir => Path.Combine(_root, "library", "Movies");

    private string[] MovieFolders() =>
        Directory.Exists(MoviesDir) ? Directory.GetDirectories(MoviesDir).Select(d => Path.GetFileName(d)).Order(StringComparer.Ordinal).ToArray() : [];

    [Fact]
    public async Task Writes_movies_and_series_and_refreshes_both_roots()
    {
        _client.Catalogs[MovieCatalog] = [Movie("tt1", "Alpha"), Movie("tt2", "Beta")];
        _client.Catalogs[ShowCatalog] = [new StremioMeta { Id = "tt0944947", Name = "Game of Thrones", ReleaseInfo = "2011" }];
        _client.Metas["series/tt0944947"] = new StremioMeta
        {
            Id = "tt0944947",
            Name = "Game of Thrones",
            ReleaseInfo = "2011",
            Videos = [new StremioVideo { Id = "tt0944947:1:1", Season = 1, Episode = 1, Released = "2011-04-17T00:00:00Z" }],
        };

        var report = await SyncAsync();

        Assert.Equal(3, report.Written);
        Assert.Empty(report.FailedCatalogs);
        Assert.Equal(new[] { "Alpha (2000) [imdbid-tt1]", "Beta (2000) [imdbid-tt2]" }, MovieFolders());
        Assert.True(File.Exists(Path.Combine(_root, "library", "Shows", "Game of Thrones (2011) [imdbid-tt0944947]", "Season 01", "Game of Thrones (2011) S01E01.strm")));
        Assert.Contains("series/tt0944947", _client.MetaRequests);
        Assert.Equal(new[] { Path.Combine(_root, "library", "Movies"), Path.Combine(_root, "library", "Shows") }, _refresher.Refreshed.Single());
    }

    [Fact]
    public async Task Pages_until_an_empty_page_and_respects_max_items()
    {
        _client.Catalogs[MovieCatalog] = [Movie("tt1", "A"), Movie("tt2", "B"), Movie("tt3", "C"), Movie("tt4", "D"), Movie("tt5", "E")];

        await SyncAsync();
        Assert.Equal(new[] { 0, 2, 4, 5 }, _client.PageRequests.Where(r => r.Catalog == MovieCatalog).Select(r => r.Skip));
        Assert.Equal(5, MovieFolders().Length);

        _client.PageRequests.Clear();
        _settings.Current.Catalogs[0].MaxItems = 3;
        Directory.Delete(Path.Combine(_root, "library"), recursive: true);
        await SyncAsync();
        Assert.Equal(3, MovieFolders().Length);
    }

    [Fact]
    public async Task Stops_when_the_server_ignores_skip()
    {
        _client.IgnoreSkip = true;
        _client.Catalogs[MovieCatalog] = [Movie("tt1", "A"), Movie("tt2", "B"), Movie("tt3", "C")];

        await SyncAsync();

        Assert.Equal(2, _client.PageRequests.Count(r => r.Catalog == MovieCatalog));
        Assert.Equal(2, MovieFolders().Length);
    }

    [Fact]
    public async Task Prunes_after_three_successful_syncs_without_the_title()
    {
        _client.Catalogs[MovieCatalog] = [Movie("tt1", "Keep"), Movie("tt2", "Drop")];
        await SyncAsync();

        _client.Catalogs[MovieCatalog] = [Movie("tt1", "Keep")];
        await SyncAsync();
        await SyncAsync();
        Assert.Equal(2, MovieFolders().Length);

        var report = await SyncAsync();

        Assert.Equal(1, report.Pruned);
        Assert.Equal(new[] { "Keep (2000) [imdbid-tt1]" }, MovieFolders());
    }

    [Fact]
    public async Task Never_prunes_played_titles()
    {
        _client.Catalogs[MovieCatalog] = [Movie("tt1", "Keep"), Movie("tt2", "Watched")];
        await SyncAsync();
        _played.PlayedFolderNames.Add("Watched (2000) [imdbid-tt2]");

        _client.Catalogs[MovieCatalog] = [Movie("tt1", "Keep")];
        for (var i = 0; i < 5; i++)
        {
            await SyncAsync();
        }

        Assert.Equal(2, MovieFolders().Length);
    }

    [Fact]
    public async Task Never_prunes_search_added_titles()
    {
        _client.Catalogs[MovieCatalog] = [Movie("tt1", "Keep"), Movie("tt2", "Searched")];
        await SyncAsync();
        _titles.Use(state =>
        {
            state.Get("movie/tt2")!.AddedBySearch = true;
            state.Save();
        });

        _client.Catalogs[MovieCatalog] = [Movie("tt1", "Keep")];
        for (var i = 0; i < 5; i++)
        {
            await SyncAsync();
        }

        Assert.Equal(2, MovieFolders().Length);
    }

    [Fact]
    public async Task Failing_catalog_is_reported_and_its_titles_are_not_counted_as_missing()
    {
        _client.Catalogs[MovieCatalog] = [Movie("tt1", "A")];
        await SyncAsync();

        _client.Failing.Add(MovieCatalog);
        SyncReport report = null!;
        for (var i = 0; i < 5; i++)
        {
            report = await SyncAsync();
        }

        Assert.Equal(new[] { MovieCatalog }, report.FailedCatalogs);
        Assert.Single(MovieFolders());
        var state = StateStore.Load(Path.Combine(_settings.DataFolderPath, "state.json"), NullLogger.Instance);
        Assert.Equal(0, state.Get("movie/tt1")!.MissCount);
    }

    [Fact]
    public async Task Empty_response_for_a_catalog_that_had_titles_is_treated_as_an_outage()
    {
        _client.Catalogs[MovieCatalog] = [Movie("tt1", "A")];
        await SyncAsync();

        _client.Catalogs[MovieCatalog] = [];
        SyncReport report = null!;
        for (var i = 0; i < 5; i++)
        {
            report = await SyncAsync();
        }

        Assert.Equal(new[] { MovieCatalog }, report.FailedCatalogs);
        Assert.Single(MovieFolders());
    }

    [Fact]
    public async Task A_page_of_only_error_items_for_a_catalog_that_had_titles_is_treated_as_an_outage()
    {
        _client.Catalogs[MovieCatalog] = [Movie("tt1", "A")];
        await SyncAsync();

        _client.Catalogs[MovieCatalog] = [new StremioMeta { Id = "aiom.error.x", Name = "Error" }];
        SyncReport report = null!;
        for (var i = 0; i < 5; i++)
        {
            report = await SyncAsync();
        }

        Assert.Equal(new[] { MovieCatalog }, report.FailedCatalogs);
        Assert.Equal(0, report.Pruned);
        Assert.Equal(new[] { "A (2000) [imdbid-tt1]" }, MovieFolders());
        var state = StateStore.Load(Path.Combine(_settings.DataFolderPath, "state.json"), NullLogger.Instance);
        Assert.Equal(0, state.Get("movie/tt1")!.MissCount);
    }

    [Fact]
    public async Task A_title_that_throws_unexpectedly_is_isolated_and_the_sync_completes()
    {
        _client.Catalogs[MovieCatalog] = [Movie("tt1", "A")];
        _client.Catalogs[ShowCatalog] =
        [
            new StremioMeta { Id = "tt0944947", Name = "Broken", ReleaseInfo = "2011", Videos = [null!] },
            new StremioMeta { Id = "tt2", Name = "Fine", ReleaseInfo = "2012", Videos = [new StremioVideo { Id = "tt2:1:1", Season = 1, Episode = 1 }] },
        ];

        var report = await SyncAsync();

        Assert.Empty(report.FailedCatalogs);
        Assert.Equal(2, report.Written);
        Assert.Equal(new[] { "A (2000) [imdbid-tt1]" }, MovieFolders());
        Assert.True(File.Exists(Path.Combine(_root, "library", "Shows", "Fine (2012) [imdbid-tt2]", "tvshow.nfo")));
        var state = StateStore.Load(Path.Combine(_settings.DataFolderPath, "state.json"), NullLogger.Instance);
        Assert.NotNull(state.Get("movie/tt1"));
        Assert.NotNull(state.Get("series/tt2"));
        Assert.Single(_refresher.Refreshed);
    }

    [Fact]
    public async Task A_catalog_that_throws_unexpectedly_fails_alone_and_the_sync_completes()
    {
        _client.Catalogs[MovieCatalog] = [Movie("tt1", "A")];
        _client.Catalogs[ShowCatalog] = [null!];

        var report = await SyncAsync();

        Assert.Equal(new[] { ShowCatalog }, report.FailedCatalogs);
        Assert.Equal(new[] { "A (2000) [imdbid-tt1]" }, MovieFolders());
        var state = StateStore.Load(Path.Combine(_settings.DataFolderPath, "state.json"), NullLogger.Instance);
        Assert.NotNull(state.Get("movie/tt1"));
    }

    [Fact]
    public async Task No_enabled_catalogs_skips_pruning()
    {
        _client.Catalogs[MovieCatalog] = [Movie("tt1", "A")];
        await SyncAsync();

        foreach (var catalog in _settings.Current.Catalogs)
        {
            catalog.Enabled = false;
        }

        SyncReport report = null!;
        for (var i = 0; i < 5; i++)
        {
            report = await SyncAsync();
        }

        Assert.Equal(0, report.Pruned);
        Assert.Equal(new[] { "A (2000) [imdbid-tt1]" }, MovieFolders());
        var state = StateStore.Load(Path.Combine(_settings.DataFolderPath, "state.json"), NullLogger.Instance);
        Assert.Equal(0, state.Get("movie/tt1")!.MissCount);
    }

    [Fact]
    public async Task Skips_items_without_a_usable_id()
    {
        _client.Catalogs[MovieCatalog] = [new StremioMeta { Id = "weird-id", Name = "Nope" }, Movie("tt1", "A")];

        var report = await SyncAsync();

        Assert.Equal(1, report.Written);
        Assert.Single(MovieFolders());
    }

    [Fact]
    public async Task Missing_aiometadata_url_throws_a_clear_error()
    {
        _settings.Current.AioMetadataManifestUrl = string.Empty;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(SyncAsync);

        Assert.Contains("AIOMetadata", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Series_whose_episode_lookup_fails_is_still_written_without_episodes()
    {
        _client.Catalogs[ShowCatalog] = [new StremioMeta { Id = "tt0944947", Name = "Game of Thrones", ReleaseInfo = "2011" }];
        _client.FailingMetas.Add("series/tt0944947");

        var report = await SyncAsync();

        Assert.Equal(1, report.Written);
        Assert.Empty(report.FailedCatalogs);
        Assert.True(File.Exists(Path.Combine(_root, "library", "Shows", "Game of Thrones (2011) [imdbid-tt0944947]", "tvshow.nfo")));
    }

    [Fact]
    public async Task A_write_failure_for_one_title_does_not_abort_other_catalogs()
    {
        Directory.CreateDirectory(Path.Combine(_root, "library"));
        await File.WriteAllTextAsync(Path.Combine(_root, "library", "Shows"), "not a directory");
        _client.Catalogs[MovieCatalog] = [Movie("tt1", "A"), Movie("tt2", "B")];
        _client.Catalogs[ShowCatalog] = [new StremioMeta { Id = "tt0944947", Name = "Game of Thrones", ReleaseInfo = "2011" }];

        var report = await SyncAsync();

        Assert.Equal(2, report.Written);
        Assert.Empty(report.FailedCatalogs);
        Assert.Equal(2, MovieFolders().Length);
        var state = StateStore.Load(Path.Combine(_settings.DataFolderPath, "state.json"), NullLogger.Instance);
        Assert.Null(state.Get("series/tt0944947"));
    }

    [Fact]
    public async Task A_short_page_in_the_middle_does_not_end_paging()
    {
        _client.Catalogs[MovieCatalog] = [Movie("tt1", "A"), Movie("tt2", "B"), Movie("tt3", "C"), Movie("tt4", "D"), Movie("tt5", "E")];
        foreach (var size in new[] { 2, 1, 2 })
        {
            _client.PageSizeSequence.Enqueue(size);
        }

        await SyncAsync();

        Assert.Equal(5, MovieFolders().Length);
        Assert.Equal(new[] { 0, 2, 3, 5 }, _client.PageRequests.Where(r => r.Catalog == MovieCatalog).Select(r => r.Skip));
    }

    [Fact]
    public async Task Series_whose_episode_lookup_times_out_is_written_and_the_catalog_is_not_failed()
    {
        _client.Catalogs[ShowCatalog] = [new StremioMeta { Id = "tt0944947", Name = "Game of Thrones", ReleaseInfo = "2011" }];
        _client.TimingOutMetas.Add("series/tt0944947");

        var report = await SyncAsync();

        Assert.Equal(1, report.Written);
        Assert.Empty(report.FailedCatalogs);
    }

    [Fact]
    public async Task A_title_whose_write_keeps_failing_is_protected_from_pruning()
    {
        _client.Catalogs[ShowCatalog] = [new StremioMeta { Id = "tt0944947", Name = "Game of Thrones", ReleaseInfo = "2011" }];
        await SyncAsync();
        var shows = Path.Combine(_root, "library", "Shows");
        var moved = shows + "-moved";
        Directory.Move(shows, moved);
        await File.WriteAllTextAsync(shows, "not a directory");

        for (var i = 0; i < 5; i++)
        {
            await SyncAsync();
        }

        var state = StateStore.Load(Path.Combine(_settings.DataFolderPath, "state.json"), NullLogger.Instance);
        Assert.Equal(0, state.Get("series/tt0944947")!.MissCount);
        File.Delete(shows);
        Directory.Move(moved, shows);
        Assert.True(Directory.Exists(Path.Combine(shows, "Game of Thrones (2011) [imdbid-tt0944947]")));
    }

    [Fact]
    public async Task A_prune_failure_does_not_stop_the_sync_and_state_is_saved()
    {
        _client.Catalogs[MovieCatalog] = [Movie("tt1", "Keep"), Movie("tt2", "Drop")];
        await SyncAsync();
        var statePath = Path.Combine(_settings.DataFolderPath, "state.json");
        _titles.Use(state =>
        {
            state.Upsert(new TitleState { StateId = "movie/imdb:bad", Kind = MediaKind.Movie, StremioId = "bad", Folder = string.Empty, Catalogs = [MovieCatalog], MissCount = 2 });
            state.Get("movie/tt2")!.MissCount = 2;
            state.Save();
        });
        _client.Catalogs[MovieCatalog] = [Movie("tt1", "Keep")];

        var report = await SyncAsync();

        Assert.Equal(1, report.Pruned);
        Assert.Equal(new[] { "Keep (2000) [imdbid-tt1]" }, MovieFolders());
        var after = StateStore.Load(statePath, NullLogger.Instance);
        Assert.Null(after.Get("movie/tt2"));
        Assert.Equal(3, after.Get("movie/imdb:bad")!.MissCount);
        Assert.Equal(2, _refresher.Refreshed.Count);
    }

    [Fact]
    public async Task Search_add_during_a_sync_is_kept()
    {
        _client.Catalogs[MovieCatalog] = [Movie("tt1", "Alpha")];
        _client.Catalogs[ShowCatalog] = [new StremioMeta { Id = "tt0944947", Name = "Game of Thrones", ReleaseInfo = "2011" }];
        _client.MetaGate = new TaskCompletionSource(); // the sync pauses inside the series meta fetch
        var sync = SyncAsync();
        while (!_client.MetaRequests.Contains("series/tt0944947"))
        {
            await Task.Delay(5);
        }

        _titles.AddFromSearch(new TitleKey(MediaKind.Movie, "imdb", "tt9"), Movie("tt9", "Searched"));
        _client.MetaGate.SetResult();
        await sync;

        var reloaded = new TitleLibrary(_settings, Time, NullLogger<TitleLibrary>.Instance);
        Assert.True(reloaded.Get("movie/tt9")?.AddedBySearch);
        Assert.NotNull(reloaded.Get("movie/tt1"));
        Assert.NotNull(reloaded.Get("series/tt0944947"));
    }

    [Fact]
    public async Task Series_metas_are_fetched_with_the_title_type_first()
    {
        _settings.Current.Catalogs =
        [
            new CatalogSelection { Type = "anime.series", Id = "mal.airing", Target = CatalogTarget.Shows, MaxItems = 10 },
        ];
        _client.Catalogs["anime.series/mal.airing"] = [new StremioMeta { Id = "mal:1", Name = "Cowboy Bebop", ReleaseInfo = "1998" }];
        _client.Metas["series/mal:1"] = new StremioMeta
        {
            Id = "mal:1",
            Name = "Cowboy Bebop",
            ReleaseInfo = "1998",
            Videos = [new StremioVideo { Id = "mal:1:1", Season = 1, Episode = 1, Released = "1998-04-03T00:00:00Z" }],
        };

        await SyncAsync();

        Assert.Equal("series/mal:1", Assert.Single(_client.MetaRequests));
        Assert.True(File.Exists(Path.Combine(_root, "library", "Shows", "Cowboy Bebop (1998) [mal-1]", "Season 01", "Cowboy Bebop (1998) S01E01.strm")));
    }

    [Fact]
    public async Task Series_metas_fall_back_to_the_catalog_type()
    {
        _settings.Current.Catalogs =
        [
            new CatalogSelection { Type = "anime.series", Id = "mal.airing", Target = CatalogTarget.Shows, MaxItems = 10 },
        ];
        _client.Catalogs["anime.series/mal.airing"] = [new StremioMeta { Id = "mal:1", Name = "Cowboy Bebop", ReleaseInfo = "1998" }];
        _client.Metas["anime.series/mal:1"] = new StremioMeta
        {
            Id = "mal:1",
            Name = "Cowboy Bebop",
            ReleaseInfo = "1998",
            Videos = [new StremioVideo { Id = "mal:1:1", Season = 1, Episode = 1, Released = "1998-04-03T00:00:00Z" }],
        };

        await SyncAsync();

        Assert.Equal(new[] { "series/mal:1", "anime.series/mal:1" }, _client.MetaRequests);
        Assert.True(File.Exists(Path.Combine(_root, "library", "Shows", "Cowboy Bebop (1998) [mal-1]", "Season 01", "Cowboy Bebop (1998) S01E01.strm")));
    }

    [Fact]
    public async Task Search_added_series_get_new_episodes()
    {
        _settings.Current.Catalogs = [];
        var key = new TitleKey(MediaKind.Series, "imdb", "tt0944947");
        _titles.AddFromSearch(key, new StremioMeta
        {
            Id = "tt0944947",
            Name = "Game of Thrones",
            ReleaseInfo = "2011",
            Videos = [new StremioVideo { Id = "tt0944947:1:1", Season = 1, Episode = 1, Released = "2011-04-17T00:00:00Z" }],
        });
        _client.Metas["series/tt0944947"] = new StremioMeta
        {
            Id = "tt0944947",
            Name = "Game of Thrones",
            ReleaseInfo = "2011",
            Videos =
            [
                new StremioVideo { Id = "tt0944947:1:1", Season = 1, Episode = 1, Released = "2011-04-17T00:00:00Z" },
                new StremioVideo { Id = "tt0944947:1:2", Season = 1, Episode = 2, Released = "2011-04-24T00:00:00Z" },
            ],
        };

        var report = await SyncAsync();

        Assert.Equal(1, report.Written);
        Assert.True(File.Exists(Path.Combine(_root, "library", "Shows", "Game of Thrones (2011) [imdbid-tt0944947]", "Season 01", "Game of Thrones (2011) S01E02.strm")));
        Assert.True(_titles.Get("series/tt0944947")!.AddedBySearch);
    }

    [Fact]
    public async Task A_failing_search_added_series_is_left_alone()
    {
        _settings.Current.Catalogs = [];
        _titles.AddFromSearch(new TitleKey(MediaKind.Series, "imdb", "tt0944947"), new StremioMeta
        {
            Id = "tt0944947",
            Name = "Game of Thrones",
            ReleaseInfo = "2011",
            Videos = [new StremioVideo { Id = "tt0944947:1:1", Season = 1, Episode = 1, Released = "2011-04-17T00:00:00Z" }],
        });
        _client.FailingMetas.Add("series/tt0944947");

        var report = await SyncAsync();

        Assert.Equal(0, report.Written);
        Assert.Empty(report.FailedCatalogs);
        Assert.True(File.Exists(Path.Combine(_root, "library", "Shows", "Game of Thrones (2011) [imdbid-tt0944947]", "Season 01", "Game of Thrones (2011) S01E01.strm")));
    }

    [Fact]
    public async Task Search_added_movies_are_not_refetched()
    {
        _settings.Current.Catalogs = [];
        _titles.AddFromSearch(new TitleKey(MediaKind.Movie, "imdb", "tt9"), Movie("tt9", "Searched"));

        await SyncAsync();

        Assert.Empty(_client.MetaRequests);
    }

    private sealed class FakePlayedLookup : IPlayedLookup
    {
        public HashSet<string> PlayedFolderNames { get; } = new(StringComparer.Ordinal);

        public bool IsPlayedByAnyone(string absoluteFolder, MediaKind kind) =>
            PlayedFolderNames.Contains(Path.GetFileName(absoluteFolder));
    }

    private sealed class FakeRefresher : ILibraryRefresher
    {
        public List<string[]> Refreshed { get; } = [];

        public Task RefreshAsync(IReadOnlyCollection<string> folders, CancellationToken cancellationToken)
        {
            Refreshed.Add([.. folders]);
            return Task.CompletedTask;
        }
    }
}
