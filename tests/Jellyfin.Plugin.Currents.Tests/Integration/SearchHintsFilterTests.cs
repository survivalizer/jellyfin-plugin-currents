using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Search;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Jellyfin.Plugin.Currents.Users;
using MediaBrowser.Model.Search;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Integration;

public sealed class SearchHintsFilterTests : IDisposable
{
    private static readonly Guid Alice = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private readonly FakeSettings _settings = new();
    private readonly FakeAioMetadataClient _client = new();
    private readonly FakeLibraryItems _library = new();
    private readonly ManualTimeProvider _time = new(DateTimeOffset.UnixEpoch);
    private readonly UserStore _users;

    public SearchHintsFilterTests()
    {
        _settings.Current.AioMetadataManifestUrl = "https://meta.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/manifest.json";
        _users = new UserStore(_settings, NullLogger<UserStore>.Instance);
        _client.Searches["movie/search.movie?matrix"] = [new StremioMeta { Id = "tt0133093", Name = "The Matrix", Year = "1999", Poster = "https://img.example.com/p.jpg" }];
        _client.Searches["series/search.series?matrix"] = [new StremioMeta { Id = "tt0389564", Name = "The Matrix Show", Year = "2005" }];
    }

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private SearchHintsFilter Create(Guid? user) => new(new SearchPlanner(
        new RemoteSearch(_client, _settings, new SearchResultRegistry(_time), _time, NullLogger<RemoteSearch>.Instance),
        _library,
        new StreamProfileResolver(_users, _settings),
        RequestContextTests.Create(RequestContextTests.Http(user)),
        NullLogger<SearchPlanner>.Instance,
        TimeSpan.FromSeconds(5)));

    private static ActionExecutingContext Context(Dictionary<string, object?> arguments) =>
        new(
            new ActionContext(new Microsoft.AspNetCore.Http.DefaultHttpContext(), new RouteData(), new ControllerActionDescriptor { ControllerName = "Search", ActionName = "GetSearchHints" }),
            [],
            arguments,
            new object());

    // The arguments Jellyfin binds for GET /Search/Hints?searchTerm=…
    private static Dictionary<string, object?> Hints(string term = "matrix") => new()
    {
        ["startIndex"] = null,
        ["limit"] = null,
        ["userId"] = null,
        ["searchTerm"] = term,
        ["includeItemTypes"] = Array.Empty<BaseItemKind>(),
        ["excludeItemTypes"] = Array.Empty<BaseItemKind>(),
        ["mediaTypes"] = Array.Empty<MediaType>(),
        ["parentId"] = null,
        ["isMovie"] = null,
        ["isSeries"] = null,
        ["isNews"] = null,
        ["isKids"] = null,
        ["isSports"] = null,
        ["includePeople"] = true,
        ["includeMedia"] = true,
        ["includeGenres"] = true,
        ["includeStudios"] = true,
        ["includeArtists"] = true,
    };

    private static SearchHintResult Local(params Guid[] ids) =>
        new(ids.Select(id => new SearchHint { Id = id, Name = "local" }).ToArray(), ids.Length);

    private static async Task<SearchHintResult> Run(SearchHintsFilter filter, ActionExecutingContext context, SearchHintResult local)
    {
        var result = new ObjectResult(local);
        await filter.OnActionExecutionAsync(context, () => Task.FromResult(new ActionExecutedContext(context, [], new object()) { Result = result }));
        return Assert.IsType<SearchHintResult>(result.Value);
    }

    [Fact]
    public async Task Appends_remote_results_after_local_hints()
    {
        var local = Guid.NewGuid();

        var hints = await Run(Create(Alice), Context(Hints()), Local(local));

        Assert.Equal(3, hints.SearchHints.Count);
        Assert.Equal(3, hints.TotalRecordCount);
        Assert.Equal(local, hints.SearchHints[0].Id);
        var movie = hints.SearchHints[1];
        Assert.Equal(SearchItemId.For(FakeSettings.Secret, "movie/tt0133093"), movie.Id);
        Assert.Equal(("The Matrix", 1999, BaseItemKind.Movie, MediaType.Video, false), (movie.Name, movie.ProductionYear, movie.Type, movie.MediaType, movie.IsFolder));
        Assert.StartsWith("currents", movie.PrimaryImageTag, StringComparison.Ordinal);
        var series = hints.SearchHints[2];
        Assert.Equal((BaseItemKind.Series, true), (series.Type, series.IsFolder));
        Assert.Null(series.PrimaryImageTag);
    }

    [Fact]
    public async Task Hints_already_in_the_local_results_are_not_repeated()
    {
        var local = Guid.NewGuid();
        _library.Existing[(Alice, "movie/tt0133093")] = local;

        var hints = await Run(Create(Alice), Context(Hints()), Local(local));

        Assert.Equal(new[] { "local", "The Matrix Show" }, hints.SearchHints.Select(h => h.Name));
    }

    [Fact]
    public async Task A_full_page_is_left_alone()
    {
        var args = Hints();
        args["limit"] = 1;
        var local = Local(Guid.NewGuid());

        var hints = await Run(Create(Alice), Context(args), local);

        Assert.Same(local, hints);
    }

    [Theory]
    [InlineData("searchTerm", "m")]
    [InlineData("startIndex", 20)]
    [InlineData("includeMedia", false)]
    [InlineData("isKids", true)]
    [InlineData("isNews", true)]
    [InlineData("isSports", true)]
    public async Task Narrowed_paged_or_short_requests_are_left_alone(string key, object value)
    {
        var args = Hints();
        args[key] = value;

        var hints = await Run(Create(Alice), Context(args), Local());

        Assert.Empty(hints.SearchHints);
        Assert.Empty(_client.SearchRequests);
    }

    [Fact]
    public async Task Is_movie_searches_only_movies()
    {
        var args = Hints();
        args["isMovie"] = true;

        var hints = await Run(Create(Alice), Context(args), Local());

        Assert.Equal(new[] { "The Matrix" }, hints.SearchHints.Select(h => h.Name));
        Assert.Equal(new[] { "movie/search.movie?matrix" }, _client.SearchRequests);
    }

    [Fact]
    public async Task Is_series_searches_only_series()
    {
        var args = Hints();
        args["isSeries"] = true;

        var hints = await Run(Create(Alice), Context(args), Local());

        Assert.Equal(new[] { "The Matrix Show" }, hints.SearchHints.Select(h => h.Name));
    }

    [Fact]
    public async Task Non_video_media_types_are_left_alone()
    {
        var args = Hints();
        args["mediaTypes"] = new[] { MediaType.Audio };

        await Run(Create(Alice), Context(args), Local());

        Assert.Empty(_client.SearchRequests);
    }

    [Fact]
    public async Task Users_without_search_add_and_anonymous_requests_get_only_local_hints()
    {
        _users.Update(Alice, r => r.SearchAutoAddDisabled = true);

        Assert.Empty((await Run(Create(Alice), Context(Hints()), Local())).SearchHints);
        Assert.Empty((await Run(Create(null), Context(Hints()), Local())).SearchHints);
        Assert.Empty(_client.SearchRequests);
    }
}
