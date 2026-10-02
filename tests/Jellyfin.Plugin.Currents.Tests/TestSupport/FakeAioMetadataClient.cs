using Jellyfin.Plugin.Currents.Clients.AioMetadata;
using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;

namespace Jellyfin.Plugin.Currents.Tests.TestSupport;

internal sealed class FakeAioMetadataClient : IAioMetadataClient
{
    public StremioManifest Manifest { get; set; } = new();

    public Dictionary<string, List<StremioMeta>> Catalogs { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, StremioMeta> Metas { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Failing { get; } = new(StringComparer.Ordinal);

    public HashSet<string> FailingMetas { get; } = new(StringComparer.Ordinal);

    public HashSet<string> TimingOutMetas { get; } = new(StringComparer.Ordinal);

    public Queue<int> PageSizeSequence { get; } = new();

    public int PageSize { get; set; } = 2;

    public bool IgnoreSkip { get; set; }

    public List<(string Catalog, int Skip)> PageRequests { get; } = [];

    public List<string> MetaRequests { get; } = [];

    /// <summary>When set, a meta request is recorded and then waits for this gate before answering.</summary>
    public TaskCompletionSource? MetaGate { get; set; }

    /// <summary>Gets search results keyed "{type}/{catalogId}?{query}".</summary>
    public Dictionary<string, List<StremioMeta>> Searches { get; } = new(StringComparer.Ordinal);

    public HashSet<string> FailingSearches { get; } = new(StringComparer.Ordinal);

    public List<string> SearchRequests { get; } = [];

    /// <summary>When set, a search request is recorded and then waits for this gate before answering.</summary>
    public TaskCompletionSource? SearchGate { get; set; }

    public Task<StremioManifest> GetManifestAsync(AioMetadataEndpoint endpoint, CancellationToken cancellationToken) =>
        Task.FromResult(Manifest);

    public Task<IReadOnlyList<StremioMeta>> GetCatalogPageAsync(AioMetadataEndpoint endpoint, string type, string catalogId, int skip, CancellationToken cancellationToken)
    {
        var key = $"{type}/{catalogId}";
        PageRequests.Add((key, skip));
        if (Failing.Contains(key))
        {
            throw new AioMetadataException("simulated outage");
        }

        var all = Catalogs.GetValueOrDefault(key) ?? [];
        IReadOnlyList<StremioMeta> page = all.Skip(IgnoreSkip ? 0 : skip).Take(PageSizeSequence.Count > 0 ? PageSizeSequence.Dequeue() : PageSize).ToList();
        return Task.FromResult(page);
    }

    public async Task<StremioMeta?> GetMetaAsync(AioMetadataEndpoint endpoint, string type, string id, CancellationToken cancellationToken)
    {
        var key = $"{type}/{id}";
        lock (MetaRequests)
        {
            MetaRequests.Add(key);
        }

        if (FailingMetas.Contains(key))
        {
            throw new AioMetadataException("simulated meta outage");
        }

        if (TimingOutMetas.Contains(key))
        {
            throw new TaskCanceledException("simulated timeout");
        }

        if (MetaGate is { } gate)
        {
            await gate.Task.ConfigureAwait(false);
        }

        return Metas.GetValueOrDefault(key);
    }

    public async Task<IReadOnlyList<StremioMeta>> SearchAsync(AioMetadataEndpoint endpoint, string type, string catalogId, string query, CancellationToken cancellationToken)
    {
        var key = $"{type}/{catalogId}?{query}";
        lock (SearchRequests)
        {
            SearchRequests.Add(key);
        }

        if (SearchGate is { } gate)
        {
            await gate.Task.ConfigureAwait(false);
        }

        if (FailingSearches.Contains($"{type}/{catalogId}"))
        {
            throw new AioMetadataException("simulated search outage");
        }

        return Searches.GetValueOrDefault(key) ?? [];
    }
}
