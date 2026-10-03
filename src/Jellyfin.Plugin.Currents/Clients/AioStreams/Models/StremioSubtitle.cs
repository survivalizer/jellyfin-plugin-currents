namespace Jellyfin.Plugin.Currents.Clients.AioStreams.Models;

/// <summary>A Stremio subtitle entry (stream-attached or from the subtitles resource). The URL is upstream's and may embed keys.</summary>
public sealed class StremioSubtitle
{
    public string? Id { get; set; }

    public string? Url { get; set; }

    public string? Lang { get; set; }
}
