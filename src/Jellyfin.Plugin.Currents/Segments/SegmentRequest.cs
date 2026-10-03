using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Streams;

namespace Jellyfin.Plugin.Currents.Segments;

/// <summary>The ids a skip-marker source needs, taken from a Currents title's Stremio id.</summary>
/// <param name="Kind">Movie, or Series for an episode.</param>
/// <param name="Provider">imdb, tmdb, tvdb, kitsu, mal, anilist or anidb.</param>
/// <param name="Id">The provider's id ("tt…" for IMDb, digits otherwise).</param>
/// <param name="Season">The season of an imdb/tmdb/tvdb episode.</param>
/// <param name="Episode">The episode number: within the season, or within the anime entry.</param>
public sealed record SegmentRequest(MediaKind Kind, string Provider, string Id, int? Season, int? Episode)
{
    private static readonly string[] AnimeProviders = ["kitsu", "mal", "anilist", "anidb"];

    public bool IsAnime => AnimeProviders.Contains(Provider, StringComparer.Ordinal);

    /// <summary>Reads the ids from a title: "tt1" / "tmdb:1" for movies, "{series}:{season}:{episode}" or "{anime}:{episode}" for episodes.</summary>
    /// <param name="title">The Currents title.</param>
    /// <param name="request">The ids when the title has usable ones.</param>
    /// <returns>True when the title has usable ids.</returns>
    public static bool TryCreate(CurrentsTitle title, [NotNullWhen(true)] out SegmentRequest? request)
    {
        request = null;
        if (title.Type == "movie")
        {
            if (!TitleKey.TryParse(MediaKind.Movie, title.StremioId, out var movie))
            {
                return false;
            }

            request = new SegmentRequest(MediaKind.Movie, movie.Provider, movie.Value, null, null);
            return true;
        }

        var series = title.SeriesId;
        if (series.Length >= title.StremioId.Length || !TitleKey.TryParse(MediaKind.Series, series, out var key))
        {
            return false;
        }

        var numbers = new List<int>();
        foreach (var part in title.StremioId[(series.Length + 1)..].Split(':'))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                return false;
            }

            numbers.Add(number);
        }

        request = (key.IsSeasonAware, numbers.Count) switch
        {
            (true, 2) => new SegmentRequest(MediaKind.Series, key.Provider, key.Value, numbers[0], numbers[1]),
            (false, 1) => new SegmentRequest(MediaKind.Series, key.Provider, key.Value, null, numbers[0]),
            _ => null,
        };
        return request is not null;
    }
}
