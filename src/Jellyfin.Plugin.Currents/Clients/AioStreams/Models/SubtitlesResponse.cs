namespace Jellyfin.Plugin.Currents.Clients.AioStreams.Models;

/// <summary>The Stremio subtitles resource response.</summary>
public sealed class SubtitlesResponse
{
    public List<StremioSubtitle> Subtitles { get; set; } = [];
}
