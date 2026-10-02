using System.Xml.Linq;
using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Library;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Library;

public class NfoWriterTests
{
    [Fact]
    public void Movie_nfo_has_title_plot_year_genres_and_unique_ids()
    {
        var key = new TitleKey(MediaKind.Movie, "imdb", "tt0111161");
        var meta = new StremioMeta { Id = "tt0111161", Name = "The Shawshank <Redemption>", Description = "Hope.", Year = "1994", Released = "1994-09-23T00:00:00Z", Genres = ["Drama", "Crime"] };

        var xml = XDocument.Parse(NfoWriter.Movie(key, meta));
        var root = xml.Root!;

        Assert.Equal("movie", root.Name.LocalName);
        Assert.Equal("The Shawshank <Redemption>", root.Element("title")!.Value);
        Assert.Equal("Hope.", root.Element("plot")!.Value);
        Assert.Equal("1994", root.Element("year")!.Value);
        Assert.Equal("1994-09-23", root.Element("premiered")!.Value);
        Assert.Equal(new[] { "Drama", "Crime" }, root.Elements("genre").Select(e => e.Value));
        var ids = root.Elements("uniqueid").ToDictionary(e => e.Attribute("type")!.Value, e => e.Value);
        Assert.Equal("tt0111161", ids["Currents"]);
        Assert.Equal("tt0111161", ids["imdb"]);
    }

    [Fact]
    public void Tvshow_nfo_uses_tmdb_key_and_separate_imdb_id()
    {
        var key = new TitleKey(MediaKind.Series, "tmdb", "1399");
        var meta = new StremioMeta { Id = "tmdb:1399", ImdbId = "tt0944947", Name = "Game of Thrones" };

        var root = XDocument.Parse(NfoWriter.TvShow(key, meta)).Root!;
        var ids = root.Elements("uniqueid").ToDictionary(e => e.Attribute("type")!.Value, e => e.Value);

        Assert.Equal("tvshow", root.Name.LocalName);
        Assert.Equal("tmdb:1399", ids["Currents"]);
        Assert.Equal("1399", ids["tmdb"]);
        Assert.Equal("tt0944947", ids["imdb"]);
        Assert.Null(root.Element("plot"));
    }

    [Fact]
    public void Output_is_deterministic()
    {
        var key = new TitleKey(MediaKind.Movie, "imdb", "tt1");
        var meta = new StremioMeta { Id = "tt1", Name = "A" };

        Assert.Equal(NfoWriter.Movie(key, meta), NfoWriter.Movie(key, meta));
    }

    [Fact]
    public void Strips_xml_invalid_characters()
    {
        var key = new TitleKey(MediaKind.Movie, "imdb", "tt1");
        var meta = new StremioMeta { Id = "tt1", Name = "Bad\u0001Title", Description = "Lone\uD800 surrogate \uD83C\uDFAC ok" };

        var root = XDocument.Parse(NfoWriter.Movie(key, meta)).Root!;

        Assert.Equal("BadTitle", root.Element("title")!.Value);
        Assert.Equal("Lone surrogate \uD83C\uDFAC ok", root.Element("plot")!.Value);
    }

    [Fact]
    public void Skips_blank_genres()
    {
        var key = new TitleKey(MediaKind.Movie, "imdb", "tt1");
        var meta = new StremioMeta { Id = "tt1", Name = "A", Genres = ["Drama", "", "  "] };

        var root = XDocument.Parse(NfoWriter.Movie(key, meta)).Root!;

        Assert.Equal(new[] { "Drama" }, root.Elements("genre").Select(e => e.Value));
    }
}
