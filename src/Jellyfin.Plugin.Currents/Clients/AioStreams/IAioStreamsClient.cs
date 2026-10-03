using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;

namespace Jellyfin.Plugin.Currents.Clients.AioStreams;

/// <summary>Finds streams for a title through the AIOStreams search API.</summary>
public interface IAioStreamsClient
{
    Task<SearchOutcome> SearchAsync(AioStreamsCredentials credentials, string type, string id, CancellationToken cancellationToken);

    /// <summary>Lists subtitles from the user's AIOStreams subtitle addons (Stremio route, path-authenticated).</summary>
    Task<IReadOnlyList<StremioSubtitle>> SubtitlesAsync(AioStreamsCredentials credentials, string type, string id, CancellationToken cancellationToken);
}
