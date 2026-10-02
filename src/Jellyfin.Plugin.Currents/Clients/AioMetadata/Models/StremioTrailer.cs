namespace Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;

/// <summary>A trailer reference (YouTube id in Source).</summary>
public sealed class StremioTrailer
{
    public string? Source { get; set; }

    public string? Type { get; set; }
}
