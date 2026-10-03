using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Segments;
using Jellyfin.Plugin.Currents.Streams;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Segments;

public class SegmentRequestTests
{
    [Theory]
    [InlineData("movie", "tt0133093", "Movie", "imdb", "tt0133093", null, null)]
    [InlineData("movie", "tmdb:603", "Movie", "tmdb", "603", null, null)]
    [InlineData("series", "tt0903747:1:2", "Series", "imdb", "tt0903747", 1, 2)]
    [InlineData("series", "tmdb:1396:3:4", "Series", "tmdb", "1396", 3, 4)]
    [InlineData("series", "kitsu:7442:5", "Series", "kitsu", "7442", null, 5)]
    [InlineData("series", "mal:21:1000", "Series", "mal", "21", null, 1000)]
    public void Parses_movie_and_episode_ids(string type, string id, string kind, string provider, string value, int? season, int? episode)
    {
        Assert.True(SegmentRequest.TryCreate(new CurrentsTitle(type, id), out var request));

        Assert.Equal(new SegmentRequest(Enum.Parse<MediaKind>(kind), provider, value, season, episode), request);
    }

    [Theory]
    [InlineData("series", "tt0903747")]
    [InlineData("series", "kitsu:1:2:3")]
    [InlineData("series", "tt0903747:1")]
    [InlineData("movie", "aiom.custom.1")]
    [InlineData("series", "tt0903747:1:99999999999")]
    public void Rejects_ids_without_usable_numbers(string type, string id) =>
        Assert.False(SegmentRequest.TryCreate(new CurrentsTitle(type, id), out _));

    [Theory]
    [InlineData("kitsu", true)]
    [InlineData("mal", true)]
    [InlineData("anilist", true)]
    [InlineData("anidb", true)]
    [InlineData("imdb", false)]
    [InlineData("tmdb", false)]
    public void Knows_anime_providers(string provider, bool anime) =>
        Assert.Equal(anime, new SegmentRequest(MediaKind.Series, provider, "1", null, 1).IsAnime);
}
