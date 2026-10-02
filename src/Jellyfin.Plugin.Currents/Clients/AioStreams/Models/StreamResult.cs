namespace Jellyfin.Plugin.Currents.Clients.AioStreams.Models;

/// <summary>One stream from the AIOStreams search API.</summary>
public sealed class StreamResult
{
    public string? Url { get; set; }

    public string? Filename { get; set; }

    public long? Size { get; set; }

    public bool? Cached { get; set; }

    public string? Type { get; set; }

    public string? InfoHash { get; set; }

    public int? FileIdx { get; set; }

    public string? Service { get; set; }

    public string? Addon { get; set; }

    public ParsedFile? ParsedFile { get; set; }

    public Dictionary<string, string>? RequestHeaders { get; set; }
}
