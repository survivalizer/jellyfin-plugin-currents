using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;

namespace Jellyfin.Plugin.Currents.Streams;

/// <summary>Picks the usable entries of a Stremio subtitle list and names them by a key that does not reveal their URL.</summary>
public static class StreamSubtitles
{
    public const int MaxPerLanguage = 5;
    public const int MaxPerVersion = 40;

    /// <summary>
    /// In order: http(s) URLs only, never AIOStreams' error entries (id "error.…", language "[❌] …"), the first entry per URL,
    /// at most <paramref name="perLanguage"/> per language and <paramref name="total"/> in all.
    /// </summary>
    public static IReadOnlyList<StremioSubtitle> Usable(IEnumerable<StremioSubtitle>? subtitles, int perLanguage = MaxPerLanguage, int total = MaxPerVersion)
    {
        var urls = new HashSet<string>(StringComparer.Ordinal);
        var perLanguageCount = new Dictionary<string, int>(StringComparer.Ordinal);
        var usable = new List<StremioSubtitle>();
        foreach (var subtitle in subtitles ?? [])
        {
            if (usable.Count == total)
            {
                break;
            }

            if (subtitle is null || IsError(subtitle) || !IsHttp(subtitle.Url) || !urls.Add(subtitle.Url!))
            {
                continue;
            }

            var language = LanguageCodes.ToIso6392(subtitle.Lang) ?? subtitle.Lang?.Trim() ?? string.Empty;
            var count = perLanguageCount.GetValueOrDefault(language);
            if (count == perLanguage)
            {
                continue;
            }

            perLanguageCount[language] = count + 1;
            usable.Add(subtitle);
        }

        return usable;
    }

    /// <summary>A short, stable key for a subtitle URL; tokens and search ids carry it instead of the URL.</summary>
    public static string Key(string url) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)).AsSpan(0, 8));

    private static bool IsError(StremioSubtitle subtitle) =>
        (subtitle.Id?.StartsWith("error.", StringComparison.Ordinal) ?? false)
        || (subtitle.Lang?.Contains('❌', StringComparison.Ordinal) ?? false);

    private static bool IsHttp(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
