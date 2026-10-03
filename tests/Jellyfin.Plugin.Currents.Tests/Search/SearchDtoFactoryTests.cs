using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Search;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Search;

public class SearchDtoFactoryTests
{
    private const string ServerId = "f2c8b1d4e5a64b7c9d0e1f2a3b4c5d6e";
    private const string RpdbPoster = "https://api.ratingposterdb.com/t0-secretkey/imdb/poster-default/tt0133093.jpg?fallback=true";

    [Fact]
    public void Movie_card_has_what_jellyfin_web_needs()
    {
        var result = SearchResults.For(new TitleKey(MediaKind.Movie, "imdb", "tt0133093"), new StremioMeta { Id = "tt0133093", Name = "The Matrix", Year = "1999", Description = "Neo.", Poster = RpdbPoster }, "movie");

        var dto = SearchDtoFactory.Create(result, ServerId);

        Assert.Equal(result.Id, dto.Id);
        Assert.Equal(ServerId, dto.ServerId);
        Assert.Equal("The Matrix", dto.Name);
        Assert.Equal("The Matrix", dto.SortName);
        Assert.Equal(1999, dto.ProductionYear);
        Assert.Equal("Neo.", dto.Overview);
        Assert.Equal(BaseItemKind.Movie, dto.Type);
        Assert.False(dto.IsFolder);
        Assert.Equal(MediaType.Video, dto.MediaType);
        Assert.Equal(LocationType.Virtual, dto.LocationType);
        Assert.False(dto.CanDelete);
        Assert.Equal(SearchDtoFactory.PosterTag(RpdbPoster), dto.ImageTags[ImageType.Primary]);
        Assert.Equal(2.0 / 3.0, dto.PrimaryImageAspectRatio);
        Assert.Equal("tt0133093", dto.ProviderIds["Imdb"]);
        Assert.Equal("tt0133093", dto.ProviderIds["Currents"]);
    }

    [Fact]
    public void Series_card_is_a_folder_with_tmdb_ids()
    {
        var result = SearchResults.For(new TitleKey(MediaKind.Series, "tmdb", "1399"), new StremioMeta { Id = "tmdb:1399", Name = "GoT" }, "series");

        var dto = SearchDtoFactory.Create(result, ServerId);

        Assert.Equal(BaseItemKind.Series, dto.Type);
        Assert.True(dto.IsFolder);
        Assert.Equal(MediaType.Unknown, dto.MediaType);
        Assert.Equal("1399", dto.ProviderIds["Tmdb"]);
        Assert.Equal("tmdb:1399", dto.ProviderIds["Currents"]);
        Assert.False(dto.ProviderIds.ContainsKey("Imdb"));
        Assert.Empty(dto.ImageTags);
        Assert.Null(dto.PrimaryImageAspectRatio);
    }

    [Fact]
    public void The_poster_url_never_appears_in_the_dto()
    {
        var result = SearchResults.For(new TitleKey(MediaKind.Movie, "imdb", "tt0133093"), new StremioMeta { Id = "tt0133093", Name = "The Matrix", Poster = RpdbPoster }, "movie");

        var json = JsonSerializer.Serialize(SearchDtoFactory.Create(result, ServerId));

        Assert.DoesNotContain("ratingposterdb", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secretkey", json, StringComparison.Ordinal);
        Assert.Matches("^currents[0-9a-f]{16}$", SearchDtoFactory.PosterTag(RpdbPoster));
        Assert.NotEqual(SearchDtoFactory.PosterTag(RpdbPoster), SearchDtoFactory.PosterTag(RpdbPoster + "&x=1"));
    }

    [Fact]
    public void Hint_mirrors_the_card_without_the_poster_url()
    {
        var result = SearchResults.For(
            new TitleKey(MediaKind.Movie, "imdb", "tt0133093"),
            new StremioMeta { Id = "tt0133093", Name = "The Matrix", Year = "1999", Poster = "https://img.example.com/p.jpg?key=SECRET" },
            "movie");

        var hint = SearchDtoFactory.Hint(result);

        Assert.Equal(result.Id, hint.Id);
#pragma warning disable CS0618 // Older clients read ItemId.
        Assert.Equal(result.Id, hint.ItemId);
#pragma warning restore CS0618
        Assert.Equal(SearchDtoFactory.PosterTag("https://img.example.com/p.jpg?key=SECRET"), hint.PrimaryImageTag);
        Assert.Equal(2.0 / 3.0, hint.PrimaryImageAspectRatio);
        Assert.DoesNotContain("SECRET", System.Text.Json.JsonSerializer.Serialize(hint), StringComparison.Ordinal);
    }
}
