namespace Jellyfin.Plugin.Currents.Clients.RemuxDb;

/// <summary>One probed file of a title, as RemuxDB stores it (crowd-sourced ffprobe data).</summary>
public sealed class RemuxDbVersion
{
    /// <summary>Gets or sets ffprobe's format name, e.g. "matroska,webm".</summary>
    public string? Container { get; set; }

    /// <summary>Gets or sets the runtime in seconds.</summary>
    public double? Duration { get; set; }

    public long? Size { get; set; }

    public long? Bitrate { get; set; }

    public List<RemuxDbSource>? Sources { get; set; }

    public List<RemuxDbTrack>? Tracks { get; set; }
}
