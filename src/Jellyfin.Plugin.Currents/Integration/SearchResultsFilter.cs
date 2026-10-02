using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Search;
using Jellyfin.Plugin.Currents.Users;
using MediaBrowser.Controller;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

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

    private readonly RemoteSearch _search;
    private readonly ILibraryItems _library;
    private readonly StreamProfileResolver _profiles;
    private readonly RequestContext _request;
    private readonly IServerApplicationHost _host;
    private readonly ILogger<SearchResultsFilter> _logger;
    private readonly TimeSpan _wait;

    public SearchResultsFilter(RemoteSearch search, ILibraryItems library, StreamProfileResolver profiles, RequestContext request, IServerApplicationHost host, ILogger<SearchResultsFilter> logger)
        : this(search, library, profiles, request, host, logger, TimeSpan.FromSeconds(3))
    {
    }

    internal SearchResultsFilter(RemoteSearch search, ILibraryItems library, StreamProfileResolver profiles, RequestContext request, IServerApplicationHost host, ILogger<SearchResultsFilter> logger, TimeSpan wait)
    {
        _search = search;
        _library = library;
        _profiles = profiles;
        _request = request;
        _host = host;
        _logger = logger;
        _wait = wait;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A remote search fault must never break Jellyfin's own library search; it degrades to local results.")]
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var plan = Plan(context);
        if (plan is null)
        {
            await next().ConfigureAwait(false);
            return;
        }

        // Started before the local search so both run at once; the shared search fills the cache even if we stop waiting.
        var remote = _search.SearchAsync(plan.Query, plan.Kinds, CancellationToken.None);
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

        IReadOnlyList<SearchResult> found;
        try
        {
            found = await remote.WaitAsync(_wait).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogDebug("AIOMetadata search for a {Length}-character term took longer than {Wait}; showing library results only", plan.Query.Length, _wait);
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("AIOMetadata search failed ({Error}); showing library results only", ex.GetType().Name);
            return;
        }

        var cards = Cards(found, page.Items, plan);
        if (cards.Count > 0)
        {
            page.Items = [.. page.Items, .. cards];
            page.TotalRecordCount += cards.Count;
        }
    }

    private static bool HasValue(object? value) => value switch
    {
        null => false,
        Array array => array.Length > 0,
        string text => text.Length > 0,
        _ => true,
    };

    private static HashSet<MediaKind> Kinds(IDictionary<string, object?> args)
    {
        var include = args.TryGetValue("includeItemTypes", out var i) && i is BaseItemKind[] inc ? inc : [];
        var exclude = args.TryGetValue("excludeItemTypes", out var e) && e is BaseItemKind[] exc ? exc : [];
        var kinds = new HashSet<MediaKind>();
        if ((include.Length == 0 || include.Contains(BaseItemKind.Movie)) && !exclude.Contains(BaseItemKind.Movie))
        {
            kinds.Add(MediaKind.Movie);
        }

        if ((include.Length == 0 || include.Contains(BaseItemKind.Series)) && !exclude.Contains(BaseItemKind.Series))
        {
            kinds.Add(MediaKind.Series);
        }

        return kinds;
    }

    private SearchPlan? Plan(ActionExecutingContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor { ControllerName: "Items" } action || !Actions.Contains(action.ActionName))
        {
            return null;
        }

        var args = context.ActionArguments;
        if (!args.TryGetValue("searchTerm", out var term) || term is not string query || RemoteSearch.Normalize(query).Length < 2
            || (args.TryGetValue("startIndex", out var start) && start is int s && s > 0)
            || (args.TryGetValue("isMissing", out var missing) && missing is true)
            || (args.TryGetValue("mediaTypes", out var media) && media is MediaType[] { Length: > 0 } types && !types.Contains(MediaType.Video))
            || args.Any(a => !Neutral.Contains(a.Key) && HasValue(a.Value)))
        {
            return null;
        }

        var userId = _request.UserId;
        if (userId == Guid.Empty || !_profiles.SearchAutoAdd(userId))
        {
            return null;
        }

        var kinds = Kinds(args);

        // A media-type filter (one without Video already left above) means a video-only list: series cards are
        // folders (MediaType Unknown) and never belong there.
        if (args.TryGetValue("mediaTypes", out var mediaFilter) && mediaFilter is MediaType[] { Length: > 0 })
        {
            kinds.Remove(MediaKind.Series);
        }

        if (args.TryGetValue("parentId", out var parent) && parent is Guid parentId && parentId != Guid.Empty)
        {
            kinds.IntersectWith(_library.KindsIn(parentId));
        }

        kinds.RemoveWhere(k => !_library.CanAdd(userId, k));
        if (kinds.Count == 0)
        {
            return null;
        }

        int? limit = args.TryGetValue("limit", out var l) && l is int max ? max : null;
        return new SearchPlan(query, kinds, limit, userId);
    }

    private List<BaseItemDto> Cards(IReadOnlyList<SearchResult> found, IReadOnlyList<BaseItemDto> local, SearchPlan plan)
    {
        var room = plan.Limit is int limit ? Math.Max(0, limit - local.Count) : int.MaxValue;
        var candidates = found.Where(r => plan.Kinds.Contains(r.Key.Kind)).ToList();
        if (room == 0 || candidates.Count == 0)
        {
            return [];
        }

        if (local.Count > 0)
        {
            var localIds = local.Select(i => i.Id).ToHashSet();
            var existing = _library.FindExisting(plan.UserId, candidates.Select(r => r.Key).ToList());
            candidates = candidates.Where(r => !(existing.TryGetValue(r.Key.StateId, out var id) && localIds.Contains(id))).ToList();
        }

        var serverId = _host.SystemId;
        return candidates.Take(room).Select(r => SearchDtoFactory.Create(r, serverId)).ToList();
    }

    private sealed record SearchPlan(string Query, HashSet<MediaKind> Kinds, int? Limit, Guid UserId);
}
