using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>M1 ranking: keep AIOStreams' own order, drop entries that cannot be played over HTTP. Preferences arrive in M2.</summary>
public static class StreamRanker
{
    public static IReadOnlyList<StreamResult> Rank(IEnumerable<StreamResult> results) =>
        results.Where(IsPlayable).ToList();

    private static bool IsPlayable(StreamResult result) =>
        result.Type is not ("error" or "statistic")
        && Uri.TryCreate(result.Url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
