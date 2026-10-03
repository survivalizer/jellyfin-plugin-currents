using System.Text.Json;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Deep copies of MediaStreams; Jellyfin mutates the streams it is handed (DeliveryUrl, labels).</summary>
public static class MediaStreamCopy
{
    // Computed getters (DisplayTitle, VideoRange, …) are skipped; Jellyfin recomputes them from the stored fields.
    private static readonly JsonSerializerOptions Options = new() { IgnoreReadOnlyProperties = true };

    public static MediaStream Of(MediaStream stream) =>
        JsonSerializer.Deserialize<MediaStream>(JsonSerializer.Serialize(stream, Options), Options)!;
}
