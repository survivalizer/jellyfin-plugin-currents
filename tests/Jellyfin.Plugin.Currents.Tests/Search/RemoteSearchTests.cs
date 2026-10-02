using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Configuration;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Search;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Search;

public class RemoteSearchTests
{
    private const string Uuid = "0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d";
    private static readonly MediaKind[] Both = [MediaKind.Movie, MediaKind.Series];
    private readonly FakeAioMetadataClient _client = new();
    private readonly FakeSettings _settings = new();
    private readonly ManualTimeProvider _time = new(DateTimeOffset.UnixEpoch);
    private readonly ListLogger<RemoteSearch> _logger = new();
    private readonly SearchResultRegistry _registry;

    public RemoteSearchTests()
    {
        _settings.Current.AioMetadataManifestUrl = $"https://meta.example.com/stremio/{Uuid}/manifest.json";
        _registry = new SearchResultRegistry(_time);
    }

    private RemoteSearch Create() => new(_client, _settings, _registry, _time, _logger);

    private static StremioMeta Meta(string id, string? name = "Title", string? imdb = null) => new() { Id = id, Name = name, ImdbId = imdb, Year = "2001" };

    [Fact]
    public async Task Searches_only_catalogs_for_the_requested_kinds()
    {
        _client.Searches["movie/search.movie?matrix"] = [Meta("tt0133093", "The Matrix")];

        var results = await Create().SearchAsync("matrix", [MediaKind.Movie], CancellationToken.None);

        Assert.Equal("movie/tt0133093", Assert.Single(results).Key.StateId);
        Assert.Equal(new[] { "movie/search.movie?matrix" }, _client.SearchRequests);
    }

    [Fact]
    public async Task Error_items_and_unusable_ids_are_dropped()
    {
        _client.Searches["movie/search.movie?matrix"] =
        [
            Meta("aiom.error.%7B%7D", "⚠️ Search unavailable"),
            Meta("garbage"),
            Meta("tt1", name: " "),
            Meta("tt2", "Kept"),
        ];

        var results = await Create().SearchAsync("matrix", Both, CancellationToken.None);

        Assert.Equal("Kept", Assert.Single(results).Name);
    }

    [Fact]
    public async Task Imdb_ids_are_preferred_and_duplicates_across_catalogs_are_dropped()
    {
        _settings.Current.SearchCatalogs =
        [
            new CatalogSelection { Type = "movie", Id = "search.movie", Target = CatalogTarget.Movies, MaxItems = 20 },
            new CatalogSelection { Type = "anime.movie", Id = "search.anime_movie", Target = CatalogTarget.Movies, MaxItems = 20 },
        ];
        _client.Searches["movie/search.movie?akira"] = [Meta("tmdb:149", "Akira", imdb: "tt0094625")];
        _client.Searches["anime.movie/search.anime_movie?akira"] = [Meta("tt0094625", "AKIRA"), Meta("mal:47", "Akira (TV)")];

        var results = await Create().SearchAsync("akira", Both, CancellationToken.None);

        Assert.Equal(new[] { "movie/tt0094625", "movie/mal:47" }, results.Select(r => r.Key.StateId));
        Assert.Equal("Akira", results[0].Name);
        Assert.Equal("anime.movie", results[1].CatalogType);
    }

    [Fact]
    public async Task A_failing_catalog_does_not_hide_the_others()
    {
        _client.FailingSearches.Add("movie/search.movie");
        _client.Searches["series/search.series?office"] = [Meta("tt0386676", "The Office")];

        var results = await Create().SearchAsync("office", Both, CancellationToken.None);

        Assert.Equal("series/tt0386676", Assert.Single(results).Key.StateId);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("search.movie", StringComparison.Ordinal));
        Assert.DoesNotContain(_logger.Entries, e => e.Message.Contains(Uuid, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Failures_are_cached_briefly()
    {
        _client.FailingSearches.Add("movie/search.movie");
        var search = Create();

        await search.SearchAsync("office", [MediaKind.Movie], CancellationToken.None);
        await search.SearchAsync("office", [MediaKind.Movie], CancellationToken.None);
        Assert.Single(_client.SearchRequests);

        _time.Advance(TimeSpan.FromSeconds(31));
        await search.SearchAsync("office", [MediaKind.Movie], CancellationToken.None);
        Assert.Equal(2, _client.SearchRequests.Count);
    }

    [Fact]
    public async Task Results_are_cached_for_ten_minutes_per_catalog_and_normalized_query()
    {
        _client.Searches["movie/search.movie?The Matrix"] = [Meta("tt0133093", "The Matrix")];
        var search = Create();

        await search.SearchAsync("The Matrix", [MediaKind.Movie], CancellationToken.None);
        var again = await search.SearchAsync("  the   matrix ", [MediaKind.Movie], CancellationToken.None);
        Assert.Single(_client.SearchRequests);
        Assert.Single(again);

        _time.Advance(TimeSpan.FromMinutes(11));
        await search.SearchAsync("the matrix", [MediaKind.Movie], CancellationToken.None);
        Assert.Equal(2, _client.SearchRequests.Count);
    }

    [Fact]
    public async Task Concurrent_searches_share_one_request_and_a_cancelled_caller_does_not_cancel_it()
    {
        _client.Searches["movie/search.movie?dune"] = [Meta("tt1160419", "Dune")];
        _client.SearchGate = new TaskCompletionSource();
        var search = Create();
        using var cancel = new CancellationTokenSource();

        var first = search.SearchAsync("dune", [MediaKind.Movie], cancel.Token);
        var second = search.SearchAsync("dune", [MediaKind.Movie], CancellationToken.None);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        _client.SearchGate.SetResult();

        Assert.Single(await second);
        Assert.Single(_client.SearchRequests);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" a ")]
    public async Task Short_queries_return_nothing(string query)
    {
        Assert.Empty(await Create().SearchAsync(query, Both, CancellationToken.None));
        Assert.Empty(_client.SearchRequests);
    }

    [Fact]
    public async Task Disabled_search_or_missing_aiometadata_returns_nothing()
    {
        _settings.Current.EnableSearch = false;
        Assert.Empty(await Create().SearchAsync("matrix", Both, CancellationToken.None));

        _settings.Current.EnableSearch = true;
        _settings.Current.AioMetadataManifestUrl = string.Empty;
        Assert.Empty(await Create().SearchAsync("matrix", Both, CancellationToken.None));
        Assert.Empty(_client.SearchRequests);
    }

    [Fact]
    public async Task Respects_max_items_and_registers_results()
    {
        _settings.Current.SearchCatalogs[0].MaxItems = 2;
        _client.Searches["movie/search.movie?star"] = [Meta("tt1", "A"), Meta("tt2", "B"), Meta("tt3", "C")];

        var results = await Create().SearchAsync("star", [MediaKind.Movie], CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.True(_registry.TryGet(results[1].Id, out var registered));
        Assert.Equal("movie/tt2", registered!.Key.StateId);
        Assert.False(_registry.TryGet(SearchItemId.For("movie/tt3"), out _));
    }
}
