using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Metadata;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Metadata;

public class MetaMapperItemTests
{
    private static readonly StremioMeta Meta = new()
    {
        Id = "tt0111161",
        ImdbId = "tt0111161",
        Name = "The Shawshank Redemption",
        Description = "Hope.",
        Year = "1994",
        Released = "1994-09-23T00:00:00Z",
        Genres = ["Drama"],
        Runtime = "142 min",
        ImdbRating = "9.3",
        Poster = "https://img.example.com/p.jpg",
        Background = "https://img.example.com/b.jpg",
        Logo = "not a url",
        Videos = [new StremioVideo { Season = 1, Episode = 2, Title = "Two", Overview = "Second.", Released = "2020-01-02T00:00:00Z" }],
    };

    [Fact]
    public void Applies_meta_fields_to_an_item()
    {
        var movie = new Movie();

        MetaMapper.Apply(Meta, movie);

        Assert.Equal("The Shawshank Redemption", movie.Name);
        Assert.Equal("Hope.", movie.Overview);
        Assert.Equal(1994, movie.ProductionYear);
        Assert.Equal(new DateTime(1994, 9, 23, 0, 0, 0, DateTimeKind.Utc), movie.PremiereDate);
        Assert.Equal(new[] { "Drama" }, movie.Genres);
        Assert.Equal(TimeSpan.FromMinutes(142).Ticks, movie.RunTimeTicks);
        Assert.Equal(9.3f, movie.CommunityRating);
        Assert.Equal("tt0111161", movie.GetProviderId(MetadataProvider.Imdb));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("11")]
    [InlineData("-1")]
    public void Ignores_non_finite_or_out_of_range_ratings(string rating)
    {
        var movie = new Movie();

        MetaMapper.Apply(new StremioMeta { Id = "tt1", Name = "X", ImdbRating = rating }, movie);

        Assert.Null(movie.CommunityRating);
    }

    [Fact]
    public void Lists_only_valid_image_urls()
    {
        var images = MetaMapper.Images(Meta, "Currents (AIOMetadata)").ToList();

        Assert.Equal(new[] { ImageType.Primary, ImageType.Backdrop }, images.Select(i => i.Type));
        Assert.All(images, i => Assert.Equal("Currents (AIOMetadata)", i.ProviderName));
    }

    [Fact]
    public void Finds_and_applies_episodes()
    {
        var video = MetaMapper.FindEpisode(Meta, 1, 2);
        var episode = new Episode();

        MetaMapper.ApplyEpisode(video!, episode);

        Assert.Equal("Two", episode.Name);
        Assert.Equal("Second.", episode.Overview);
        Assert.Equal(1, episode.ParentIndexNumber);
        Assert.Equal(2, episode.IndexNumber);
        Assert.Null(MetaMapper.FindEpisode(Meta, 1, 3));
        Assert.Null(MetaMapper.FindEpisode(Meta, null, 2));
    }

    [Fact]
    public void Youtube_trailers_become_remote_trailers()
    {
        var meta = new StremioMeta
        {
            Name = "M",
            Trailers =
            [
                new StremioTrailer { Source = "vKQi3bBA1y8", Type = "Trailer", Name = "Official Trailer" },
                new StremioTrailer { Source = "vKQi3bBA1y8", Type = "Trailer" },
                new StremioTrailer { Source = "https://evil.example.com/x", Type = "Trailer" },
                new StremioTrailer { YtId = "abcdefghijk", Source = "not-an-id" },
                null!,
            ],
        };
        var movie = new Movie();

        MetaMapper.Apply(meta, movie);

        Assert.Equal(new[] { "https://www.youtube.com/watch?v=vKQi3bBA1y8", "https://www.youtube.com/watch?v=abcdefghijk" }, movie.RemoteTrailers.Select(t => t.Url));
        Assert.Equal(new[] { "Official Trailer", "Trailer" }, movie.RemoteTrailers.Select(t => t.Name));
    }

    [Fact]
    public void At_most_five_trailers_are_kept_and_none_means_empty()
    {
        var many = new StremioMeta { Name = "M", Trailers = Enumerable.Range(0, 7).Select(i => new StremioTrailer { Source = $"abcdefghij{i}" }).ToList() };
        var movie = new Movie();
        var none = new Movie();

        MetaMapper.Apply(many, movie);
        MetaMapper.Apply(new StremioMeta { Name = "N" }, none);

        Assert.Equal(5, movie.RemoteTrailers.Count);
        Assert.Empty(none.RemoteTrailers);
    }
}
