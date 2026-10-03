using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Search;
using MediaBrowser.Model.Search;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>
/// Appends AIOMetadata search results that are not in the library to Jellyfin's legacy search hints
/// (GET /Search/Hints, used by some third-party clients), under the same rules as <see cref="SearchResultsFilter"/>.
/// A hint's Id is the search id, which <see cref="SearchItemFilter"/> serves posters for and opens.
/// </summary>
public sealed class SearchHintsFilter : IAsyncActionFilter
{
    private readonly SearchPlanner _planner;

    public SearchHintsFilter(SearchPlanner planner) => _planner = planner;

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
        if (executed.Exception is not null || executed.Result is not ObjectResult { StatusCode: null or 200, Value: SearchHintResult hints } result)
        {
            return;
        }

        if (plan.Limit is int limit && hints.SearchHints.Count >= limit)
        {
            return;
        }

        var found = await _planner.WaitAsync(remote, plan).ConfigureAwait(false);
        var picked = _planner.Pick(found, hints.SearchHints.Select(h => h.Id).ToList(), plan);
        if (picked.Count > 0)
        {
            // SearchHintResult is immutable: replace it.
            result.Value = new SearchHintResult([.. hints.SearchHints, .. picked.Select(SearchDtoFactory.Hint)], hints.TotalRecordCount + picked.Count);
        }
    }

    private static bool Is(IDictionary<string, object?> args, string key, bool value) =>
        args.TryGetValue(key, out var raw) && raw is bool flag && flag == value;

    private SearchPlan? Plan(ActionExecutingContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor { ControllerName: "Search", ActionName: "GetSearchHints" })
        {
            return null;
        }

        var args = context.ActionArguments;
        if (!args.TryGetValue("searchTerm", out var term) || term is not string query
            || (args.TryGetValue("startIndex", out var start) && start is int s && s > 0)
            || Is(args, "includeMedia", false)
            || Is(args, "isNews", true) || Is(args, "isKids", true) || Is(args, "isSports", true)
            || (args.TryGetValue("mediaTypes", out var media) && media is MediaType[] { Length: > 0 } types && !types.Contains(MediaType.Video)))
        {
            return null;
        }

        var kinds = SearchPlanner.Kinds(
            args.TryGetValue("includeItemTypes", out var include) ? include as BaseItemKind[] : null,
            args.TryGetValue("excludeItemTypes", out var exclude) ? exclude as BaseItemKind[] : null);

        // Series are folders (MediaType Unknown): a video-only list never holds them.
        if (args.TryGetValue("mediaTypes", out var mediaFilter) && mediaFilter is MediaType[] { Length: > 0 })
        {
            kinds.Remove(MediaKind.Series);
        }

        if (Is(args, "isMovie", true))
        {
            kinds.IntersectWith([MediaKind.Movie]);
        }
        else if (Is(args, "isMovie", false))
        {
            kinds.Remove(MediaKind.Movie);
        }

        if (Is(args, "isSeries", true))
        {
            kinds.IntersectWith([MediaKind.Series]);
        }
        else if (Is(args, "isSeries", false))
        {
            kinds.Remove(MediaKind.Series);
        }

        Guid? parentId = args.TryGetValue("parentId", out var parent) && parent is Guid p ? p : null;
        int? limit = args.TryGetValue("limit", out var l) && l is int max ? max : null;
        return _planner.Plan(query, kinds, parentId, limit);
    }
}
