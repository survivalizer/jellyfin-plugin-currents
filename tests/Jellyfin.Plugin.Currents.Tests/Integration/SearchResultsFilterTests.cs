using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Search;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Jellyfin.Plugin.Currents.Users;
using MediaBrowser.Controller;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Integration;

public sealed class SearchResultsFilterTests : IDisposable
{
    private const string ServerId = "f2c8b1d4e5a64b7c9d0e1f2a3b4c5d6e";
    private static readonly Guid Alice = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly Guid MoviesLibrary = Guid.Parse("cccccccccccccccccccccccccccccccc");
    private readonly FakeSettings _settings = new();
    private readonly FakeAioMetadataClient _client = new();
    private readonly FakeLibraryItems _library = new();
    private readonly ManualTimeProvider _time = new(DateTimeOffset.UnixEpoch);
    private readonly UserStore _users;

    public SearchResultsFilterTests()
    {
        _settings.Current.AioMetadataManifestUrl = "https://meta.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/manifest.json";
        _users = new UserStore(_settings, NullLogger<UserStore>.Instance);
        _client.Searches["movie/search.movie?matrix"] = [new StremioMeta { Id = "tt0133093", Name = "The Matrix", Year = "1999", Poster = "https://img.example.com/p.jpg" }];
        _client.Searches["series/search.series?matrix"] = [new StremioMeta { Id = "tt0389564", Name = "The Matrix Show", Year = "2005" }];
        _library.Libraries[MoviesLibrary] = [MediaKind.Movie];
    }

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private SearchResultsFilter Create(Guid? user, TimeSpan? wait = null)
    {
        var http = RequestContextTests.Http(user);
        var host = InterfaceFake.Create<IServerApplicationHost>();
        host.Fake.On("get_SystemId", _ => ServerId);
        return new SearchResultsFilter(
            new RemoteSearch(_client, _settings, new SearchResultRegistry(_time), _time, NullLogger<RemoteSearch>.Instance),
            _library,
            new StreamProfileResolver(_users, _settings),
            RequestContextTests.Create(http),
            host.Instance,
            NullLogger<SearchResultsFilter>.Instance,
            wait ?? TimeSpan.FromSeconds(5));
    }

    private static ActionExecutingContext Context(Dictionary<string, object?> arguments, string action = "GetItems") =>
        new(
            new ActionContext(new Microsoft.AspNetCore.Http.DefaultHttpContext(), new RouteData(), new ControllerActionDescriptor { ControllerName = "Items", ActionName = action }),
            [],
            arguments,
            new object());

    private static Dictionary<string, object?> Search(string term = "matrix") => new()
    {
        ["searchTerm"] = term,
        ["includeItemTypes"] = new[] { BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Episode },
        ["excludeItemTypes"] = Array.Empty<BaseItemKind>(),
        ["mediaTypes"] = Array.Empty<MediaType>(),
        ["genres"] = Array.Empty<string>(),
        ["limit"] = 800,
        ["recursive"] = true,
    };

    private static QueryResult<BaseItemDto> Page(params Guid[] ids) =>
        new(0, ids.Length, ids.Select(id => new BaseItemDto { Id = id, Name = "local" }).ToArray());

    private static async Task<QueryResult<BaseItemDto>> Run(SearchResultsFilter filter, ActionExecutingContext context, QueryResult<BaseItemDto> page)
    {
        await filter.OnActionExecutionAsync(context, () => Task.FromResult(new ActionExecutedContext(context, [], new object()) { Result = new ObjectResult(page) }));
        return page;
    }

    [Fact]
    public async Task Appends_remote_results_after_local_ones()
    {
        var local = Guid.NewGuid();

        var page = await Run(Create(Alice), Context(Search()), Page(local));

        Assert.Equal(3, page.Items.Count);
        Assert.Equal(local, page.Items[0].Id);
        Assert.Equal(("The Matrix", BaseItemKind.Movie, ServerId), (page.Items[1].Name, page.Items[1].Type, page.Items[1].ServerId));
        Assert.Equal(SearchItemId.For(FakeSettings.Secret, "movie/tt0133093"), page.Items[1].Id);
        Assert.Equal(BaseItemKind.Series, page.Items[2].Type);
        Assert.Equal(3, page.TotalRecordCount);
        Assert.True(page.Items[1].ImageTags.ContainsKey(ImageType.Primary));
    }

    [Fact]
    public async Task Results_already_in_the_local_results_are_not_repeated()
    {
        var local = Guid.NewGuid();
        _library.Existing[(Alice, "movie/tt0133093")] = local;

        var page = await Run(Create(Alice), Context(Search()), Page(local));

        Assert.Equal(new[] { "local", "The Matrix Show" }, page.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task Existing_items_missing_from_the_local_results_still_get_a_card()
    {
        _library.Existing[(Alice, "movie/tt0133093")] = Guid.NewGuid();

        var page = await Run(Create(Alice), Context(Search()), Page(Guid.NewGuid()));

        Assert.Contains(page.Items, i => i.Name == "The Matrix");
    }

    [Fact]
    public async Task Only_requested_types_are_searched()
    {
        var args = Search();
        args["includeItemTypes"] = new[] { BaseItemKind.Movie };
        var page = await Run(Create(Alice), Context(args), Page());
        Assert.Equal(new[] { "The Matrix" }, page.Items.Select(i => i.Name));

        args["includeItemTypes"] = new[] { BaseItemKind.Episode };
        _client.SearchRequests.Clear();
        await Run(Create(Alice), Context(args), Page());
        Assert.Empty(_client.SearchRequests);
    }

    [Theory]
    [InlineData("isFavorite", true)]
    [InlineData("startIndex", 100)]
    [InlineData("isMissing", true)]
    [InlineData("genres", new[] { "Action" })]
    [InlineData("searchTerm", "m")]
    public async Task Narrowed_paged_or_short_requests_are_left_alone(string key, object value)
    {
        var args = Search();
        args[key] = value;

        var page = await Run(Create(Alice), Context(args), Page());

        Assert.Empty(page.Items);
        Assert.Empty(_client.SearchRequests);
    }

    [Fact]
    public async Task Non_video_media_types_are_left_alone()
    {
        var args = Search();
        args["mediaTypes"] = new[] { MediaType.Audio };

        await Run(Create(Alice), Context(args), Page());

        Assert.Empty(_client.SearchRequests);
    }

    [Fact]
    public async Task Library_scoped_search_adds_only_that_librarys_kind()
    {
        var args = Search();
        args["parentId"] = MoviesLibrary;
        var page = await Run(Create(Alice), Context(args), Page());
        Assert.Equal(new[] { "The Matrix" }, page.Items.Select(i => i.Name));

        args["parentId"] = Guid.NewGuid();
        _client.SearchRequests.Clear();
        await Run(Create(Alice), Context(args), Page());
        Assert.Empty(_client.SearchRequests);
    }

    [Fact]
    public async Task Users_without_search_add_and_anonymous_requests_get_only_local_results()
    {
        _users.Update(Alice, r => r.SearchAutoAddDisabled = true);
        Assert.Empty((await Run(Create(Alice), Context(Search()), Page())).Items);
        Assert.Empty((await Run(Create(null), Context(Search()), Page())).Items);
        Assert.Empty(_client.SearchRequests);
    }

    [Fact]
    public async Task Kinds_the_user_cannot_add_are_not_searched()
    {
        _library.Addable.Remove(MediaKind.Series);

        await Run(Create(Alice), Context(Search()), Page());

        Assert.Equal(new[] { "movie/search.movie?matrix" }, _client.SearchRequests);
    }

    [Fact]
    public async Task Slow_remote_search_returns_local_results_in_time()
    {
        _client.SearchGate = new TaskCompletionSource();
        var local = Guid.NewGuid();

        var page = await Run(Create(Alice, TimeSpan.FromMilliseconds(50)), Context(Search()), Page(local));

        Assert.Equal(local, Assert.Single(page.Items).Id);
        _client.SearchGate.SetResult();
    }

    [Fact]
    public async Task The_limit_is_respected()
    {
        var args = Search();
        args["limit"] = 2;

        var page = await Run(Create(Alice), Context(args), Page(Guid.NewGuid()));

        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public async Task Failed_actions_and_other_actions_are_left_alone()
    {
        var context = Context(Search());
        var bad = new BadRequestObjectResult("userId is required");
        await Create(Alice).OnActionExecutionAsync(context, () => Task.FromResult(new ActionExecutedContext(context, [], new object()) { Result = bad }));
        Assert.Equal("userId is required", bad.Value);

        _client.SearchRequests.Clear();
        await Run(Create(Alice), Context(Search(), action: "GetResumeItems"), Page());
        Assert.Empty(_client.SearchRequests);
    }

    [Fact]
    public async Task A_full_local_page_does_not_wait_for_remote_results()
    {
        _client.SearchGate = new TaskCompletionSource();
        var args = Search();
        args["limit"] = 1;
        var local = Guid.NewGuid();
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var page = await Run(Create(Alice, TimeSpan.FromSeconds(5)), Context(args), Page(local));
            watch.Stop();

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"waited {watch.Elapsed}");
            Assert.Equal(local, Assert.Single(page.Items).Id);
        }
        finally
        {
            _client.SearchGate.SetResult();
        }
    }

    [Fact]
    public async Task A_failing_remote_search_returns_local_results()
    {
        // Not an upstream outage RemoteSearch absorbs: an unexpected fault from inside the search.
        _client.SearchFault = new InvalidOperationException("unexpected");
        var local = Guid.NewGuid();

        var page = await Run(Create(Alice), Context(Search()), Page(local));

        Assert.Equal(local, Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task The_web_videos_query_gets_no_remote_cards()
    {
        var args = Search();
        args["mediaTypes"] = new[] { MediaType.Video };
        args["includeItemTypes"] = Array.Empty<BaseItemKind>();
        args["excludeItemTypes"] = new[] { BaseItemKind.Movie, BaseItemKind.Episode, BaseItemKind.TvChannel };

        var page = await Run(Create(Alice), Context(args), Page());

        Assert.Empty(page.Items);
        Assert.Empty(_client.SearchRequests);
    }

    [Fact]
    public async Task Media_types_video_keeps_movies_but_not_series()
    {
        var args = Search();
        args["mediaTypes"] = new[] { MediaType.Video };
        args["includeItemTypes"] = new[] { BaseItemKind.Movie, BaseItemKind.Series };

        var page = await Run(Create(Alice), Context(args), Page());

        Assert.Equal(new[] { "The Matrix" }, page.Items.Select(i => i.Name));
        Assert.Equal(new[] { "movie/search.movie?matrix" }, _client.SearchRequests);
    }

    [Fact]
    public async Task The_legacy_user_route_is_handled_too()
    {
        var page = await Run(Create(Alice), Context(Search(), action: "GetItemsByUserIdLegacy"), Page());

        Assert.Equal(2, page.Items.Count);
    }
}
