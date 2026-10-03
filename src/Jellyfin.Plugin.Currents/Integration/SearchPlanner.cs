using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Search;
using Jellyfin.Plugin.Currents.Users;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>
/// The rules the item-search and search-hints filters share (spec §4.2): who may see remote results and of which kinds,
/// how long to wait for them, and which ones fit beside the local results.
/// </summary>
public sealed class SearchPlanner
{
    private readonly RemoteSearch _search;
    private readonly ILibraryItems _library;
    private readonly StreamProfileResolver _profiles;
    private readonly RequestContext _request;
    private readonly ILogger<SearchPlanner> _logger;
    private readonly TimeSpan _wait;

    public SearchPlanner(RemoteSearch search, ILibraryItems library, StreamProfileResolver profiles, RequestContext request, ILogger<SearchPlanner> logger)
        : this(search, library, profiles, request, logger, TimeSpan.FromSeconds(3))
    {
    }

    internal SearchPlanner(RemoteSearch search, ILibraryItems library, StreamProfileResolver profiles, RequestContext request, ILogger<SearchPlanner> logger, TimeSpan wait)
    {
        _search = search;
        _library = library;
        _profiles = profiles;
        _request = request;
        _logger = logger;
        _wait = wait;
    }

    /// <summary>The kinds a request's include and exclude item types allow (only movies and series exist remotely).</summary>
    /// <param name="include">Included item types; empty or null means all.</param>
    /// <param name="exclude">Excluded item types.</param>
    /// <returns>The kinds.</returns>
    public static HashSet<MediaKind> Kinds(BaseItemKind[]? include, BaseItemKind[]? exclude)
    {
        include ??= [];
        exclude ??= [];
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

    /// <summary>Plans a remote search for the requesting user, or null when remote results do not belong in this answer.</summary>
    /// <param name="term">The search term.</param>
    /// <param name="kinds">The kinds the request allows; narrowed in place to the library and to what the user can add.</param>
    /// <param name="parentId">The library the search is scoped to, if any.</param>
    /// <param name="limit">The page size, if any.</param>
    /// <returns>The plan, or null.</returns>
    public SearchPlan? Plan(string? term, HashSet<MediaKind> kinds, Guid? parentId, int? limit)
    {
        if (term is null || RemoteSearch.Normalize(term).Length < 2)
        {
            return null;
        }

        var userId = _request.UserId;
        if (userId == Guid.Empty || !_profiles.SearchAutoAdd(userId))
        {
            return null;
        }

        if (parentId is { } parent && parent != Guid.Empty)
        {
            kinds.IntersectWith(_library.KindsIn(parent));
        }

        kinds.RemoveWhere(k => !_library.CanAdd(userId, k));
        return kinds.Count == 0 ? null : new SearchPlan(term, kinds, limit, userId);
    }

    /// <summary>Starts the remote search; call it before the local search so both run at once. The shared search fills the cache even if nobody waits.</summary>
    /// <param name="plan">The plan.</param>
    /// <returns>The running search.</returns>
    public Task<IReadOnlyList<SearchResult>> Start(SearchPlan plan) => _search.SearchAsync(plan.Query, plan.Kinds, CancellationToken.None);

    /// <summary>Waits a bounded time for the remote results; empty on timeout or failure, so local results never suffer.</summary>
    /// <param name="remote">The running search.</param>
    /// <param name="plan">The plan.</param>
    /// <returns>The results, or empty.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A remote search fault must never break Jellyfin's own library search; it degrades to local results.")]
    public async Task<IReadOnlyList<SearchResult>> WaitAsync(Task<IReadOnlyList<SearchResult>> remote, SearchPlan plan)
    {
        try
        {
            return await remote.WaitAsync(_wait).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogDebug("AIOMetadata search for a {Length}-character term took longer than {Wait}; showing library results only", plan.Query.Length, _wait);
            return [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("AIOMetadata search failed ({Error}); showing library results only", ex.GetType().Name);
            return [];
        }
    }

    /// <summary>The remote results that fit after the local ones: of the plan's kinds, not already among the local results, up to the limit.</summary>
    /// <param name="found">The remote results.</param>
    /// <param name="local">The ids of the local results on this page.</param>
    /// <param name="plan">The plan.</param>
    /// <returns>The results to append.</returns>
    public IReadOnlyList<SearchResult> Pick(IReadOnlyList<SearchResult> found, IReadOnlyCollection<Guid> local, SearchPlan plan)
    {
        var room = plan.Limit is int limit ? Math.Max(0, limit - local.Count) : int.MaxValue;
        var candidates = found.Where(r => plan.Kinds.Contains(r.Key.Kind)).ToList();
        if (room == 0 || candidates.Count == 0)
        {
            return [];
        }

        if (local.Count > 0)
        {
            var localIds = local.ToHashSet();
            var existing = _library.FindExisting(plan.UserId, candidates.Select(r => r.Key).ToList());
            candidates = candidates.Where(r => !(existing.TryGetValue(r.Key.StateId, out var id) && localIds.Contains(id))).ToList();
        }

        return candidates.Take(room).ToList();
    }
}
