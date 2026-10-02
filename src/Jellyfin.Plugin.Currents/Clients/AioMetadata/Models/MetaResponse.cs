namespace Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;

/// <summary>Response of /meta/{type}/{id}.json.</summary>
public sealed class MetaResponse
{
    public StremioMeta? Meta { get; set; }
}
