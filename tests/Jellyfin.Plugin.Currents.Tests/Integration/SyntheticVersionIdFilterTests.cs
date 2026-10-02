using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Integration;

public class SyntheticVersionIdFilterTests
{
    private static readonly Guid Item = Guid.Parse("11111111111111111111111111111111");
    private static readonly Guid Alice = Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private readonly VersionRegistry _registry = new(new FakeSettings(), new ManualTimeProvider(DateTimeOffset.UnixEpoch));
    private readonly Guid _version;

    public SyntheticVersionIdFilterTests()
    {
        var id = StreamIdentity.VersionId(Item, Alice, "k", FakeSettings.Secret);
        _registry.Register(Item, Alice, [new VersionEntry(id, Item, Alice, new CurrentsTitle("movie", "tt1"), new RankedStream("k", new StreamResult()))]);
        _version = Guid.ParseExact(id, "N");
    }

    internal static ActionExecutingContext Context(string method, string controller, string action, Dictionary<string, object?> arguments)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        var actionContext = new ActionContext(http, new RouteData(), new ControllerActionDescriptor { ControllerName = controller, ActionName = action });
        return new ActionExecutingContext(actionContext, [], arguments, new object());
    }

    internal static Task Run(IAsyncActionFilter filter, ActionExecutingContext context) =>
        filter.OnActionExecutionAsync(context, () => Task.FromResult(new ActionExecutedContext(context, [], new object())));

    [Theory]
    [InlineData("GET", "UserLibrary", "GetItem", "itemId")]
    [InlineData("GET", "UserLibrary", "GetItemLegacy", "itemId")]
    [InlineData("GET", "Image", "GetItemImage", "itemId")]
    [InlineData("GET", "Subtitle", "GetSubtitleWithTicks", "routeItemId")]
    [InlineData("GET", "VideoAttachments", "GetAttachment", "videoId")]
    [InlineData("HEAD", "Videos", "HeadVideoStream", "itemId")]
    [InlineData("POST", "MediaInfo", "GetPostedPlaybackInfo", "itemId")]
    [InlineData("POST", "Playstate", "MarkPlayedItem", "itemId")]
    [InlineData("POST", "UserLibrary", "MarkFavoriteItem", "itemId")]
    public async Task Version_ids_become_the_base_item(string method, string controller, string action, string key)
    {
        var context = Context(method, controller, action, new() { [key] = _version });

        await Run(new SyntheticVersionIdFilter(_registry), context);

        Assert.Equal(Item, context.ActionArguments[key]);
    }

    [Theory]
    [InlineData("DELETE", "Library", "DeleteItem")]
    [InlineData("POST", "ItemUpdate", "UpdateItem")]
    [InlineData("POST", "ItemRefresh", "RefreshItem")]
    public async Task Destructive_or_editing_actions_are_never_rewritten(string method, string controller, string action)
    {
        var context = Context(method, controller, action, new() { ["itemId"] = _version });

        await Run(new SyntheticVersionIdFilter(_registry), context);

        Assert.Equal(_version, context.ActionArguments["itemId"]);
    }

    [Fact]
    public async Task Unknown_ids_are_left_alone()
    {
        var other = Guid.NewGuid();
        var context = Context("GET", "UserLibrary", "GetItem", new() { ["itemId"] = other });

        await Run(new SyntheticVersionIdFilter(_registry), context);

        Assert.Equal(other, context.ActionArguments["itemId"]);
    }

    [Fact]
    public async Task Id_lists_are_mapped_and_deduplicated()
    {
        var other = Guid.NewGuid();
        var context = Context("GET", "Items", "GetItems", new() { ["ids"] = new[] { _version, Item, other } });

        await Run(new SyntheticVersionIdFilter(_registry), context);

        Assert.Equal(new[] { Item, other }, (Guid[])context.ActionArguments["ids"]!);
    }
}
