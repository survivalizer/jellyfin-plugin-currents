namespace Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;

/// <summary>Stremio addon manifest (only the fields Currents uses).</summary>
public sealed class StremioManifest
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Version { get; set; }

    public List<StremioCatalog> Catalogs { get; set; } = [];
}
