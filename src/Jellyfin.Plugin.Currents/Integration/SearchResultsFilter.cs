using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Search;
using MediaBrowser.Controller;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>
/// Appends AIOMetadata search results that are not in the library to Jellyfin's item search (spec §4.2). Jellyfin's
/// ISearchProvider can only rank existing items (M0 S2), so this wraps GET /Items?searchTerm= instead.
/// </summary>
public sealed class SearchResultsFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> Actions = new(StringComparer.Ordinal) { "GetItems", "GetItemsByUserIdLegacy" };

    // Arguments a remote title can satisfy. Any other argument with a value (favourites, genres, years…) narrows the query.
    private static readonly HashSet<string> Neutral = new(StringComparer.Ordinal)
    {
        "userId", "searchTerm", "includeItemTypes", "excludeItemTypes", "recursive", "limit", "startIndex", "fields",
        "enableTotalRecordCount", "imageTypeLimit", "enableImageTypes", "enableImages", "enableUserData",
        "parentId", "mediaTypes", "isMissing", "sortBy", "sortOrder", "collapseBoxSetItems",
    };

    private readonly SearchPlanner _planner;
    private readonly IServerApplicationHost _host;

    public SearchResultsFilter(SearchPlanner planner, IServerApplicationHost host)
    {
        _planner = planner;
        _host = host;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var plan = Plan(context);
        if (plan is null)
        {
            await next().ConfigureAwait(false);
            return;
        }

        var remote = _planner.Start(plan);
        var executed = await next().ConfigureAwait(false);
        if (executed.Exception is not null || executed.Result is not ObjectResult { StatusCode: null or 200, Value: QueryResult<BaseItemDto> page })
        {
            return;
        }

        // Nothing fits on this page; the search already running still warms the cache.
        if (plan.Limit is int limit && page.Items.Count >= limit)
        {
            return;
        }

        var found = await _planner.WaitAsync(remote, plan).ConfigureAwait(false);
        var picked = _planner.Pick(found, page.Items.Select(i => i.Id).ToList(), plan);
        if (picked.Count > 0)
        {
            var serverId = _host.SystemId;
            page.Items = [.. page.Items, .. picked.Select(r => SearchDtoFactory.Create(r, serverId))];
            page.TotalRecordCount += picked.Count;
        }
    }

    private static bool HasValue(object? value) => value switch
    {
        null => false,
        Array array => array.Length > 0,
        string text => text.Length > 0,
        _ => true,
    };

    private SearchPlan? Plan(ActionExecutingContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor { ControllerName: "Items" } action || !Actions.Contains(action.ActionName))
        {
            return null;
        }

        var args = context.ActionArguments;
        if (!args.TryGetValue("searchTerm", out var term) || term is not string query
            || (args.TryGetValue("startIndex", out var start) && start is int s && s > 0)
            || (args.TryGetValue("isMissing", out var missing) && missing is true)
            || (args.TryGetValue("mediaTypes", out var media) && media is MediaType[] { Length: > 0 } types && !types.Contains(MediaType.Video))
            || args.Any(a => !Neutral.Contains(a.Key) && HasValue(a.Value)))
        {
            return null;
        }

        var kinds = SearchPlanner.Kinds(
            args.TryGetValue("includeItemTypes", out var include) ? include as BaseItemKind[] : null,
            args.TryGetValue("excludeItemTypes", out var exclude) ? exclude as BaseItemKind[] : null);

        // A media-type filter (one without Video already left above) means a video-only list: series cards are
        // folders (MediaType Unknown) and never belong there.
        if (args.TryGetValue("mediaTypes", out var mediaFilter) && mediaFilter is MediaType[] { Length: > 0 })
        {
            kinds.Remove(MediaKind.Series);
        }

        Guid? parentId = args.TryGetValue("parentId", out var parent) && parent is Guid p ? p : null;
        int? limit = args.TryGetValue("limit", out var l) && l is int max ? max : null;
        return _planner.Plan(query, kinds, parentId, limit);
    }
}
