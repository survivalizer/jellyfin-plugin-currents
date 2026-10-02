using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.Currents.Common;

namespace Jellyfin.Plugin.Currents.Search;

/// <summary>Search results shown recently, by search id. Opening a result needs its key and meta; jellyfin-web caches search results for 24 h.</summary>
public sealed class SearchResultRegistry
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);
    private readonly TtlCache<Guid, SearchResult> _results;

    public SearchResultRegistry(TimeProvider time) => _results = new TtlCache<Guid, SearchResult>(time);

    public void Add(SearchResult result) => _results.Set(result.Id, result, Ttl);

    public bool TryGet(Guid id, [NotNullWhen(true)] out SearchResult? result) => _results.TryGet(id, out result);
}
