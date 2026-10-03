using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Filters and re-ranks AIOStreams results by preferences. Header-bound streams are kept; the resolver filters them per use. The sort is stable: ties keep AIOStreams' order, which carries the user's own AIOStreams sort rules (spec §5.4).</summary>
public static class StreamRanker
{
    public static IReadOnlyList<StreamResult> Rank(IEnumerable<StreamResult> results) => Rank(results, new StreamPreferences());

    public static IReadOnlyList<StreamResult> Rank(IEnumerable<StreamResult> results, StreamPreferences preferences)
    {
        var maxBytes = preferences.MaxSizeGb > 0 ? preferences.MaxSizeGb * 1_000_000_000d : double.MaxValue;
        return results
            .Where(IsPlayable)
            .Where(r => !preferences.CachedOnly || r.Cached != false)
            .Where(r => r.Size is null || r.Size <= maxBytes)
            .Where(r => IndexIn(preferences.ExcludedResolutions, r.ParsedFile?.Resolution) == preferences.ExcludedResolutions.Length)
            .Where(r => BestIndex(preferences.ExcludedVisualTags, r.ParsedFile?.VisualTags) == preferences.ExcludedVisualTags.Length)
            .OrderBy(r => IndexIn(preferences.ResolutionOrder, r.ParsedFile?.Resolution))
            .ThenBy(r => HdrRank(preferences.Hdr, r))
            .ThenBy(r => BestIndex(preferences.AudioLanguages, r.ParsedFile?.Languages))
            .ThenBy(r => BestIndex(preferences.SubtitleLanguages, r.ParsedFile?.Subtitles))
            .ToList();
    }

    public static bool IsHdr(StreamResult result) => result.ParsedFile?.VisualTags?.Any(VisualTags.IsHdr) == true;

    private static bool IsPlayable(StreamResult result) =>
        result.Type is not ("error" or "statistic")
        && Uri.TryCreate(result.Url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static int HdrRank(HdrPreference preference, StreamResult result) => preference switch
    {
        HdrPreference.Prefer => IsHdr(result) ? 0 : 1,
        HdrPreference.Avoid => IsHdr(result) ? 1 : 0,
        _ => 0,
    };

    // Position of value in list (case-insensitive); list.Length when absent, so unlisted values sort last.
    private static int IndexIn(string[] list, string? value)
    {
        if (value is null)
        {
            return list.Length;
        }

        var index = Array.FindIndex(list, x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? list.Length : index;
    }

    private static int BestIndex(string[] list, List<string>? values) =>
        values is null || values.Count == 0 ? list.Length : values.Min(v => IndexIn(list, v));
}
