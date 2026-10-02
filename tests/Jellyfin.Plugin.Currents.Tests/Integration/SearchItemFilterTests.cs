using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Clients.Posters;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Search;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Jellyfin.Plugin.Currents.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Integration;

public sealed class SearchItemFilterTests : IDisposable
{
    private static readonly Guid Alice = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly Guid Item = Guid.Parse("11111111111111111111111111111111");
    private readonly FakeSettings _settings = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero));
    private readonly FakeAioMetadataClient _client = new();
    private readonly FakeLibraryItems _library = new();
    private readonly FakePosterClient _posters = new();
    private readonly PosterCache _posterCache;
    private readonly SearchResultRegistry _registry;
    private readonly TitleLibrary _titles;
    private readonly UserStore _users;
    private readonly SearchTitleOpener _opener;
    private readonly SearchResult _matrix;

    public SearchItemFilterTests()
    {
        _settings.Current.AioMetadataManifestUrl = "https://meta.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/manifest.json";
        _settings.Current.LibraryRoot = Path.Combine(_settings.DataFolderPath, "library");
        _registry = new SearchResultRegistry(_time);
        _posterCache = new PosterCache(_posters, _time);
        _titles = new TitleLibrary(_settings, _time, NullLogger<TitleLibrary>.Instance);
        _users = new UserStore(_settings, NullLogger<UserStore>.Instance);
        _opener = new SearchTitleOpener(_titles, _registry, _library, _client, _settings, NullLogger<SearchTitleOpener>.Instance);
        _matrix = SearchResults.For(new TitleKey(MediaKind.Movie, "imdb", "tt0133093"), new StremioMeta { Id = "tt0133093", Name = "The Matrix", Year = "1999", Poster = "https://img.example.com/matrix.jpg" }, "movie");
        _registry.Add(_matrix);
        _client.Metas["movie/tt0133093"] = _matrix.Meta;
        _library.AddResult = (_, _) => Item;
    }

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private SearchItemFilter Create(Guid? user) =>
        new(_opener, _registry, new StreamProfileResolver(_users, _settings), RequestContextTests.Create(RequestContextTests.Http(user)), _posterCache);

    private static ActionExecutingContext Context(string method, string controller, string action, Guid id)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        return new ActionExecutingContext(
            new ActionContext(http, new RouteData(), new ControllerActionDescriptor { ControllerName = controller, ActionName = action }),
            [],
            new Dictionary<string, object?> { ["itemId"] = id },
            new object());
    }

    private static async Task<bool> Run(IAsyncActionFilter filter, ActionExecutingContext context)
    {
        var called = false;
        await filter.OnActionExecutionAsync(context, () =>
        {
            called = true;
            return Task.FromResult(new ActionExecutedContext(context, [], new object()));
        });
        return called;
    }

    [Theory]
    [InlineData("GET", "UserLibrary", "GetItemLegacy")]
    [InlineData("GET", "UserLibrary", "GetItem")]
    [InlineData("POST", "Playstate", "MarkPlayedItemLegacy")]
    [InlineData("POST", "UserLibrary", "MarkFavoriteItemLegacy")]
    public async Task Opening_a_result_adds_it_and_rewrites_the_item_id(string method, string controller, string action)
    {
        var context = Context(method, controller, action, _matrix.Id);

        Assert.True(await Run(Create(Alice), context));

        Assert.Equal(Item, context.ActionArguments["itemId"]);
        Assert.True(_titles.Get("movie/tt0133093")!.AddedBySearch);
    }

    [Fact]
    public async Task Concurrent_item_requests_share_one_open()
    {
        _library.AddGate = new TaskCompletionSource();
        var legacy = Context("GET", "UserLibrary", "GetItemLegacy", _matrix.Id);
        var theme = Context("GET", "UserLibrary", "GetItem", _matrix.Id);

        var first = Run(Create(Alice), legacy);
        var second = Run(Create(Alice), theme);
        _library.AddGate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(Item, legacy.ActionArguments["itemId"]);
        Assert.Equal(Item, theme.ActionArguments["itemId"]);
        Assert.Single(_library.Added);
    }

    [Fact]
    public async Task Other_ids_pass_through_unchanged()
    {
        var other = Guid.NewGuid();
        var context = Context("GET", "UserLibrary", "GetItem", other);

        Assert.True(await Run(Create(Alice), context));
        Assert.Equal(other, context.ActionArguments["itemId"]);
    }

    [Fact]
    public async Task Users_without_search_add_get_404()
    {
        _users.Update(Alice, r => r.SearchAutoAddDisabled = true);
        var context = Context("GET", "UserLibrary", "GetItemLegacy", _matrix.Id);

        Assert.False(await Run(Create(Alice), context));
        Assert.IsType<NotFoundResult>(context.Result);
        Assert.Null(_titles.Get("movie/tt0133093"));
    }

    [Fact]
    public async Task A_failed_add_answers_503_with_the_reason()
    {
        _library.AddResult = (_, _) => null;
        var context = Context("GET", "UserLibrary", "GetItemLegacy", _matrix.Id);

        Assert.False(await Run(Create(Alice), context));

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(503, result.StatusCode);
        Assert.Equal("Add the Currents Movies folder to a Jellyfin library first.", Assert.IsType<ProblemDetails>(result.Value).Title);
    }

    [Theory]
    [InlineData("GetItemImage")]
    [InlineData("GetItemImageByIndex")]
    [InlineData("GetItemImage2")]
    public async Task Anonymous_image_requests_never_add_titles_and_get_the_proxied_poster(string action)
    {
        var context = Context("GET", "Image", action, _matrix.Id);

        Assert.False(await Run(Create(null), context));

        var file = Assert.IsType<FileContentResult>(context.Result);
        Assert.Equal("image/jpeg", file.ContentType);
        Assert.Equal(new byte[] { 1, 2, 3 }, file.FileContents);
        Assert.Equal("public, max-age=86400", context.HttpContext.Response.Headers.CacheControl.ToString());
        Assert.Equal("nosniff", context.HttpContext.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.Equal(new Uri("https://img.example.com/matrix.jpg"), Assert.Single(_posters.Requests));
        Assert.Null(_titles.Get("movie/tt0133093"));
        Assert.Empty(_library.Added);
    }

    [Fact]
    public async Task A_second_anonymous_image_request_does_not_fetch_the_poster_again()
    {
        var first = Context("GET", "Image", "GetItemImage", _matrix.Id);
        var second = Context("GET", "Image", "GetItemImage", _matrix.Id);

        await Run(Create(null), first);
        await Run(Create(null), second);

        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.IsType<FileContentResult>(second.Result).FileContents);
        Assert.Single(_posters.Requests);
    }

    [Fact]
    public async Task Signed_in_image_requests_do_not_add_titles_either()
    {
        var context = Context("GET", "Image", "GetItemImage", _matrix.Id);

        await Run(Create(Alice), context);

        Assert.IsType<FileContentResult>(context.Result);
        Assert.Empty(_library.Added);
    }

    [Fact]
    public async Task Images_of_added_titles_are_the_real_items()
    {
        _titles.AddFromSearch(_matrix.Key, _matrix.Meta);
        _library.Titles["movie/tt0133093"] = Item;
        var context = Context("GET", "Image", "GetItemImage", _matrix.Id);

        Assert.True(await Run(Create(null), context));
        Assert.Equal(Item, context.ActionArguments["itemId"]);
        Assert.Empty(_posters.Requests);
    }

    [Fact]
    public async Task A_missing_poster_is_404()
    {
        _posters.Image = null;
        var context = Context("GET", "Image", "GetItemImage", _matrix.Id);

        await Run(Create(null), context);

        Assert.IsType<NotFoundResult>(context.Result);
    }

    [Theory]
    [InlineData("DELETE", "Library", "DeleteItem")]
    [InlineData("POST", "ItemUpdate", "UpdateItem")]
    [InlineData("POST", "ItemRefresh", "RefreshItem")]
    public async Task Destructive_actions_are_never_touched(string method, string controller, string action)
    {
        var context = Context(method, controller, action, _matrix.Id);

        Assert.True(await Run(Create(Alice), context));
        Assert.Equal(_matrix.Id, context.ActionArguments["itemId"]);
        Assert.Empty(_library.Added);
    }
}
