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
}
