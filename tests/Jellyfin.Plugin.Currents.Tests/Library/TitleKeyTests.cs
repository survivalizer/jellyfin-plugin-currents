using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Library;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Library;

public class TitleKeyTests
{
    [Fact]
    public void Prefers_imdb_id_field_over_meta_id()
    {
        var key = TitleKey.FromMeta(MediaKind.Movie, new StremioMeta { Id = "tmdb:550", ImdbId = "tt0137523" });

        Assert.Equal("tt0137523", key!.StremioId);
        Assert.Equal("[imdbid-tt0137523]", key.FolderTag);
        Assert.Equal("movie/tt0137523", key.StateId);
    }

    [Theory]
    [InlineData("tt0944947", "imdb", "tt0944947", "[imdbid-tt0944947]")]
    [InlineData("tmdb:1399", "tmdb", "1399", "[tmdbid-1399]")]
    [InlineData("tvdb:121361", "tvdb", "121361", "[tvdbid-121361]")]
    [InlineData("kitsu:1376", "kitsu", "1376", "[kitsu-1376]")]
    [InlineData("mal:5114", "mal", "5114", "[mal-5114]")]
    public void Parses_supported_id_formats(string id, string provider, string value, string tag)
    {
        Assert.True(TitleKey.TryParse(MediaKind.Series, id, out var key));
        Assert.Equal(provider, key!.Provider);
        Assert.Equal(value, key.Value);
        Assert.Equal(id, key.StremioId);
        Assert.Equal(tag, key.FolderTag);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tt")]
    [InlineData("ttabc")]
    [InlineData("tt0944947:1:1")]
    [InlineData("unknown:5")]
    [InlineData("tmdb:")]
    public void Rejects_unusable_ids(string? id)
    {
        Assert.False(TitleKey.TryParse(MediaKind.Movie, id, out _));
    }

    [Theory]
    [InlineData("tt0944947", 1, 2, "tt0944947:1:2")]
    [InlineData("tmdb:1399", 0, 1, "tmdb:1399:0:1")]
    [InlineData("kitsu:1376", 1, 5, "kitsu:1376:5")]
    public void Builds_episode_ids_per_provider_convention(string id, int season, int episode, string expected)
    {
        TitleKey.TryParse(MediaKind.Series, id, out var key);

        Assert.Equal(expected, key!.EpisodeId(season, episode));
    }
}
