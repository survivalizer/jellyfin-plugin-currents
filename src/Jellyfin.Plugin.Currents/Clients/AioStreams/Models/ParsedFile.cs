namespace Jellyfin.Plugin.Currents.Clients.AioStreams.Models;

/// <summary>AIOStreams' parse of the release file name.</summary>
public sealed class ParsedFile
{
    public string? Resolution { get; set; }

    public string? Quality { get; set; }

    public string? Encode { get; set; }

    public List<string>? VisualTags { get; set; }

    public List<string>? AudioTags { get; set; }

    public List<string>? Languages { get; set; }

    public List<string>? AudioChannels { get; set; }

    public List<string>? Subtitles { get; set; }

    public string? Container { get; set; }

    public string? Extension { get; set; }

    /// <summary>Gets or sets where the track lists came from ("probe", "indexer" or "addon"); null when AIOStreams only parsed the name.</summary>
    public string? MediaInfoQuality { get; set; }

    public List<MediaTrack>? AudioTracks { get; set; }

    public List<MediaTrack>? SubtitleTracks { get; set; }
}
