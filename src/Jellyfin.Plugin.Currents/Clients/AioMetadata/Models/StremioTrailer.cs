namespace Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;

/// <summary>A trailer reference: a YouTube id in Source (and YtId), with an optional name.</summary>
public sealed class StremioTrailer
{
    public string? Source { get; set; }

    public string? Type { get; set; }

    public string? Name { get; set; }

    public string? YtId { get; set; }
}
