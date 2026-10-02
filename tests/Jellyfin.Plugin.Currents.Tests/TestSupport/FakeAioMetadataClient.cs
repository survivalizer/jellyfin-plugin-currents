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

    public Task<StremioMeta?> GetMetaAsync(AioMetadataEndpoint endpoint, string type, string id, CancellationToken cancellationToken)
    {
        var key = $"{type}/{id}";
        MetaRequests.Add(key);
        if (FailingMetas.Contains(key))
        {
            throw new AioMetadataException("simulated meta outage");
        }

        if (TimingOutMetas.Contains(key))
        {
            throw new TaskCanceledException("simulated timeout");
        }

        return Task.FromResult(Metas.GetValueOrDefault(key));
    }
}
