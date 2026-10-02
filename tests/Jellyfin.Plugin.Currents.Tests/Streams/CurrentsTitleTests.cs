using Jellyfin.Plugin.Currents.Streams;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public class CurrentsTitleTests
{
    [Theory]
    [InlineData("series", "tt0944947:1:2", "tt0944947")]
    [InlineData("series", "kitsu:12:1:5", "kitsu:12")]
    [InlineData("series", "tt0944947", "tt0944947")]
    [InlineData("movie", "tt0111161", "tt0111161")]
    [InlineData("series", "tmdb:1399:0:3", "tmdb:1399")]
    public void Series_id_drops_season_and_episode(string type, string id, string expected) =>
        Assert.Equal(expected, new CurrentsTitle(type, id).SeriesId);
}
