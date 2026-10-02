namespace Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;

/// <summary>Response of /catalog/{type}/{id}.json.</summary>
public sealed class CatalogResponse
{
    public List<StremioMeta> Metas { get; set; } = [];
}
