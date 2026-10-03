using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Currents.Clients.RemuxDb;

/// <summary>One stream of a probed file; Idx is ffprobe's stream index.</summary>
public sealed class RemuxDbTrack
{
    public string? Kind { get; set; }

    public int? Idx { get; set; }

    public string? Codec { get; set; }

    public string? Language { get; set; }

    public string? Title { get; set; }

    public bool? IsDefault { get; set; }

    public bool? IsForced { get; set; }

    public bool? IsHearingImpaired { get; set; }

    public bool? IsExternal { get; set; }

    public long? BitRate { get; set; }

    public int? Channels { get; set; }

    public string? ChannelLayout { get; set; }

    public int? SampleRate { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public string? PixelFormat { get; set; }

    public string? ColorTransfer { get; set; }

    public string? ColorPrimaries { get; set; }

    public string? ColorSpace { get; set; }

    public string? Profile { get; set; }

    public int? DvProfile { get; set; }

    [JsonPropertyName("hdr10_plus_present")]
    public bool? Hdr10PlusPresent { get; set; }

    public int? BitDepth { get; set; }
}
