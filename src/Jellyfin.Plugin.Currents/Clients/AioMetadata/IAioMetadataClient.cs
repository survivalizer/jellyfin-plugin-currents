using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;

namespace Jellyfin.Plugin.Currents.Clients.AioMetadata;

/// <summary>Reads catalogs and metadata from an AIOMetadata configuration.</summary>
public interface IAioMetadataClient
{
    Task<StremioManifest> GetManifestAsync(AioMetadataEndpoint endpoint, CancellationToken cancellationToken);

    Task<IReadOnlyList<StremioMeta>> GetCatalogPageAsync(AioMetadataEndpoint endpoint, string type, string catalogId, int skip, CancellationToken cancellationToken);

    Task<StremioMeta?> GetMetaAsync(AioMetadataEndpoint endpoint, string type, string id, CancellationToken cancellationToken);

    Task<IReadOnlyList<StremioMeta>> SearchAsync(AioMetadataEndpoint endpoint, string type, string catalogId, string query, CancellationToken cancellationToken);
}
