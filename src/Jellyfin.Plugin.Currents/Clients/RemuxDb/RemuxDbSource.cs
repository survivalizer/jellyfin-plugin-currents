namespace Jellyfin.Plugin.Currents.Clients.RemuxDb;

/// <summary>Where a probed file came from (a torrent file or an NZB).</summary>
public sealed class RemuxDbSource
{
    public string? Kind { get; set; }

    /// <summary>Gets or sets the file name, possibly with the torrent's folder in front.</summary>
    public string? Filename { get; set; }

    public string? TorrentInfoHash { get; set; }

    public int? TorrentFileIdx { get; set; }
}
