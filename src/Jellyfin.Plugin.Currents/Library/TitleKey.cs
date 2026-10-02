using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;

namespace Jellyfin.Plugin.Currents.Library;

/// <summary>Canonical identity of a title: IMDb when known, otherwise the provider id AIOMetadata used.</summary>
public sealed record TitleKey(MediaKind Kind, string Provider, string Value)
{
    private static readonly string[] PrefixedProviders = ["tmdb", "tvdb", "kitsu", "mal", "anilist", "anidb"];
    private static readonly string[] SeasonAwareProviders = ["imdb", "tmdb", "tvdb"];

    /// <summary>Gets a value indicating whether episode ids carry a season ("{id}:{season}:{episode}").</summary>
    public bool IsSeasonAware => SeasonAwareProviders.Contains(Provider, StringComparer.Ordinal);

    public string StremioId => Provider == "imdb" ? Value : $"{Provider}:{Value}";

    public string StremioType => Kind == MediaKind.Movie ? "movie" : "series";

    public string StateId => $"{StremioType}/{StremioId}";

    public string FolderTag => Provider switch
    {
        "imdb" => $"[imdbid-{Value}]",
        "tmdb" => $"[tmdbid-{Value}]",
        "tvdb" => $"[tvdbid-{Value}]",
        _ => $"[{Provider}-{Value}]",
    };

    public static TitleKey? FromMeta(MediaKind kind, StremioMeta meta)
    {
        if (IsImdb(meta.ImdbId))
        {
            return new TitleKey(kind, "imdb", meta.ImdbId!);
        }

        return TryParse(kind, meta.Id, out var key) ? key : null;
    }

    public static bool TryParse(MediaKind kind, string? stremioId, [NotNullWhen(true)] out TitleKey? key)
    {
        key = null;
        if (string.IsNullOrWhiteSpace(stremioId))
        {
            return false;
        }

        if (IsImdb(stremioId))
        {
            key = new TitleKey(kind, "imdb", stremioId);
            return true;
        }

        var colon = stremioId.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            return false;
        }

        var provider = stremioId[..colon];
        var value = stremioId[(colon + 1)..];
        if (!PrefixedProviders.Contains(provider, StringComparer.Ordinal) || value.Length == 0 || !value.All(char.IsAsciiDigit))
        {
            return false;
        }

        key = new TitleKey(kind, provider, value);
        return true;
    }

    /// <summary>Episode id in the convention stream addons expect: "{id}:{season}:{episode}", or "{id}:{episode}" for anime providers.</summary>
    public string EpisodeId(int season, int episode) =>
        IsSeasonAware
            ? string.Create(CultureInfo.InvariantCulture, $"{StremioId}:{season}:{episode}")
            : string.Create(CultureInfo.InvariantCulture, $"{StremioId}:{episode}");

    private static bool IsImdb(string? id) =>
        id is { Length: > 2 } && id.StartsWith("tt", StringComparison.Ordinal) && id.AsSpan(2).IndexOfAnyExceptInRange('0', '9') < 0;
}
