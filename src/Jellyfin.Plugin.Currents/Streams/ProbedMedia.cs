using System.Text.Json;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>An immutable copy of an ffprobe result. Streams are stored as JSON so every reader gets fresh objects.</summary>
public sealed record ProbedMedia(string StreamsJson, string? Container, long? RunTimeTicks, int? Bitrate, long? Size)
{
    // Computed getters (VideoRange, DisplayTitle, …) are skipped; Jellyfin recomputes them from the stored fields.
    private static readonly JsonSerializerOptions Options = new() { IgnoreReadOnlyProperties = true };

    public static ProbedMedia From(MediaSourceInfo probed) =>
        new(JsonSerializer.Serialize(probed.MediaStreams ?? [], Options), probed.Container, probed.RunTimeTicks, probed.Bitrate, probed.Size);

    public IReadOnlyList<MediaStream> Streams() =>
        JsonSerializer.Deserialize<List<MediaStream>>(StreamsJson, Options) ?? [];
}
