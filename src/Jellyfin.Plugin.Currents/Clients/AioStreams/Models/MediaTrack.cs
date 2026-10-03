using System.Text.Json.Serialization;
using Jellyfin.Plugin.Currents.Common;

namespace Jellyfin.Plugin.Currents.Clients.AioStreams.Models;

/// <summary>One audio or subtitle track from AIOStreams media info, in container order.</summary>
public sealed class MediaTrack
{
    public string? Lang { get; set; }

    public string? Codec { get; set; }

    public string? Title { get; set; }

    /// <summary>Gets or sets the channel layout ("5.1") or count ("6"); AIOStreams sends a string, upstreams sometimes a number.</summary>
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Channels { get; set; }

    public bool? Default { get; set; }

    public bool? Forced { get; set; }

    public bool? HearingImpaired { get; set; }

    public bool? Commentary { get; set; }
}
