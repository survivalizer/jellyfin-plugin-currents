using Jellyfin.Plugin.Currents.Streams;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>Clients treat every MediaSource id as an item id (M0 S1b). Rewrites version ids in item arguments to the base item, for reads and a few safe writes only.</summary>
public sealed class SyntheticVersionIdFilter : IAsyncActionFilter
{
    // Item-id argument names in Jellyfin 12.1 actions (SubtitleController uses routeItemId, VideoAttachmentsController videoId).
    private static readonly string[] IdKeys = ["itemId", "routeItemId", "videoId"];

    // Writes that may target the base item. Deletes, edits and refreshes are never rewritten.
    private static readonly HashSet<string> SafeWrites = new(StringComparer.Ordinal)
    {
        "GetPostedPlaybackInfo",
        "MarkPlayedItem", "MarkPlayedItemLegacy", "MarkUnplayedItem", "MarkUnplayedItemLegacy",
        "MarkFavoriteItem", "MarkFavoriteItemLegacy", "UnmarkFavoriteItem", "UnmarkFavoriteItemLegacy",
    };

    private readonly VersionRegistry _registry;

    public SyntheticVersionIdFilter(VersionRegistry registry) => _registry = registry;

    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        var action = (context.ActionDescriptor as ControllerActionDescriptor)?.ActionName;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || (action is not null && SafeWrites.Contains(action)))
        {
            foreach (var key in IdKeys)
            {
                if (context.ActionArguments.TryGetValue(key, out var raw) && raw is Guid id && _registry.TryGetBaseItemId(id, out var baseId))
                {
                    context.ActionArguments[key] = baseId;
                }
            }

            if (context.ActionArguments.TryGetValue("ids", out var rawIds) && rawIds is Guid[] ids && ids.Any(i => _registry.TryGetBaseItemId(i, out _)))
            {
                context.ActionArguments["ids"] = ids.Select(i => _registry.TryGetBaseItemId(i, out var b) ? b : i).Distinct().ToArray();
            }
        }

        return next();
    }
}
