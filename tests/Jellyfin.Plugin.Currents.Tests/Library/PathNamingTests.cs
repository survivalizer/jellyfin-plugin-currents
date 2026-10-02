using Jellyfin.Plugin.Currents.Library;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Library;

public class PathNamingTests
{
    private static readonly TitleKey Key = new(MediaKind.Movie, "imdb", "tt0117060");

    [Theory]
    [InlineData("Mission: Impossible", "Mission Impossible")]
    [InlineData("What If...?", "What If")]
    [InlineData("  AC/DC: Live  ", "AC DC Live")]
    [InlineData("Ψ-Nan", "Ψ-Nan")]
    [InlineData("???", "Untitled")]
    [InlineData(null, "Untitled")]
    [InlineData("Tab\tand\nnewline", "Tab and newline")]
    public void Sanitizes_titles(string? input, string expected)
    {
        Assert.Equal(expected, PathNaming.SanitizeTitle(input));
    }

    [Fact]
    public void Truncates_long_titles_without_splitting_surrogate_pairs()
    {
        var name = new string('a', 99) + "😀😀";

        var result = PathNaming.SanitizeTitle(name);

        Assert.True(result.Length <= 100);
        Assert.False(char.IsHighSurrogate(result[^1]));
    }

    [Fact]
    public void Builds_folder_and_file_names()
    {
        var folder = PathNaming.TitleFolder("Mission: Impossible", 1996, Key);

        Assert.Equal("Mission Impossible (1996) [imdbid-tt0117060]", folder);
        Assert.Equal("Mission Impossible (1996)", PathNaming.StripTag(folder));
        Assert.Equal("Mission Impossible (1996).strm", PathNaming.MovieFile(folder));
        Assert.Equal("Mission Impossible (1996) S01E02.strm", PathNaming.EpisodeFile(folder, 1, 2));
        Assert.Equal("Untitled [imdbid-tt0117060]", PathNaming.TitleFolder(null, null, Key));
    }

    [Theory]
    [InlineData(0, "Specials")]
    [InlineData(1, "Season 01")]
    [InlineData(12, "Season 12")]
    [InlineData(123, "Season 123")]
    public void Names_season_folders(int season, string expected)
    {
        Assert.Equal(expected, PathNaming.SeasonFolder(season));
    }
}
