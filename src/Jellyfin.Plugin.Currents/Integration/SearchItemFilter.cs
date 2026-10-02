using Jellyfin.Plugin.Currents.Clients.Posters;
using Jellyfin.Plugin.Currents.Search;
using Jellyfin.Plugin.Currents.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>
/// Search cards carry search ids (Task 1). When a client asks for one, this opens it: the real item when it exists,
/// otherwise the title is added (signed-in users with search auto-add only). Image requests are anonymous and never
/// add titles; until the title exists they get the poster, fetched by the server.
/// </summary>
public sealed class SearchItemFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> ImageActions = new(StringComparer.Ordinal) { "GetItemImage", "GetItemImageByIndex", "GetItemImage2" };

    private static readonly HashSet<string> SafeWrites = new(StringComparer.Ordinal)
    {
        "GetPostedPlaybackInfo",
        "MarkPlayedItem", "MarkPlayedItemLegacy", "MarkUnplayedItem", "MarkUnplayedItemLegacy",
        "MarkFavoriteItem", "MarkFavoriteItemLegacy", "UnmarkFavoriteItem", "UnmarkFavoriteItemLegacy",
    };

    private readonly SearchTitleOpener _opener;
    private readonly SearchResultRegistry _registry;
    private readonly StreamProfileResolver _profiles;
    private readonly RequestContext _request;
    private readonly IPosterClient _posters;

    public SearchItemFilter(SearchTitleOpener opener, SearchResultRegistry registry, StreamProfileResolver profiles, RequestContext request, IPosterClient posters)
    {
        _opener = opener;
        _registry = registry;
        _profiles = profiles;
        _request = request;
        _posters = posters;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        var action = context.ActionDescriptor as ControllerActionDescriptor;
        var allowed = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || (action is not null && SafeWrites.Contains(action.ActionName));
        if (!allowed || !context.ActionArguments.TryGetValue("itemId", out var raw) || raw is not Guid id || id == Guid.Empty)
        {
            await next().ConfigureAwait(false);
            return;
        }

        var image = action is { ControllerName: "Image" } && ImageActions.Contains(action.ActionName);
        var userId = image ? Guid.Empty : _request.UserId;
        var mayAdd = !image && userId != Guid.Empty && _profiles.SearchAutoAdd(userId);
        var outcome = await _opener.OpenAsync(id, userId, mayAdd, context.HttpContext.RequestAborted).ConfigureAwait(false);
        switch (outcome.Status)
        {
            case OpenStatus.Opened:
                context.ActionArguments["itemId"] = outcome.ItemId;
                await next().ConfigureAwait(false);
                return;
            case OpenStatus.Failed:
                context.Result = new ObjectResult(new ProblemDetails { Title = outcome.Message, Status = StatusCodes.Status503ServiceUnavailable })
                {
                    StatusCode = StatusCodes.Status503ServiceUnavailable,
                };
                return;
            case OpenStatus.NotAllowed:
                context.Result = image ? await PosterAsync(context.HttpContext, id).ConfigureAwait(false) : new NotFoundResult();
                return;
            default:
                await next().ConfigureAwait(false);
                return;
        }
    }

    private async Task<IActionResult> PosterAsync(HttpContext http, Guid id)
    {
        if (!_registry.TryGet(id, out var result) || !Uri.TryCreate(result.Meta.Poster, UriKind.Absolute, out var uri)
            || await _posters.GetAsync(uri, http.RequestAborted).ConfigureAwait(false) is not { } poster)
        {
            return new NotFoundResult();
        }

        http.Response.Headers.CacheControl = "public, max-age=86400";
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return new FileContentResult(poster.Bytes, poster.ContentType);
    }
}
