using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Integration;

public sealed class CurrentsItemLocatorTests : IDisposable
{
    private readonly FakeSettings _settings = new();
    private readonly string _root;

    public CurrentsItemLocatorTests()
    {
        _root = Path.Combine(_settings.DataFolderPath, "library");
        _settings.Current.LibraryRoot = _root;
    }

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private string Strm(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private string Signed(string type, string id) => new StrmSigner(_settings.Current.SigningSecret).StrmUrl("http://192.168.1.5:8097", type, id);

    private CurrentsItemLocator Create() => new(_settings, new ManualTimeProvider(DateTimeOffset.UnixEpoch));

    [Fact]
    public void Movie_strm_under_the_root_is_a_currents_title()
    {
        var movie = new Movie { Path = Strm("Movies/Movie (2020) [imdbid-tt1]/Movie (2020).strm", Signed("movie", "tt1")) };

        Assert.True(Create().TryGetTitle(movie, out var title));
        Assert.Equal(new Jellyfin.Plugin.Currents.Streams.CurrentsTitle("movie", "tt1"), title);
    }

    [Fact]
    public void Episode_strm_yields_the_episode_id_and_skips_comment_lines()
    {
        var episode = new Episode { Path = Strm("Shows/Show/Season 01/Show S01E02.strm", "# written by Currents\n\n" + Signed("series", "tt2:1:2")) };

        Assert.True(Create().TryGetTitle(episode, out var title));
        Assert.Equal("tt2:1:2", title!.StremioId);
        Assert.Equal("tt2", title.SeriesId);
    }

    [Fact]
    public void Forged_signature_outside_root_folders_and_non_videos_are_not_ours()
    {
        var forged = new Movie { Path = Strm("Movies/X/X.strm", "http://h/Currents/play/movie/tt9?sig=forged") };
        var outsidePath = Path.Combine(_settings.DataFolderPath, "elsewhere", "Y.strm");
        Directory.CreateDirectory(Path.GetDirectoryName(outsidePath)!);
        File.WriteAllText(outsidePath, Signed("movie", "tt3"));
        var outside = new Movie { Path = outsidePath };
        var series = new Series { Path = Path.Combine(_root, "Shows", "Show") };
        var missing = new Movie { Path = Path.Combine(_root, "Movies", "Gone", "Gone.strm") };

        Assert.False(Create().TryGetTitle(forged, out _));
        Assert.False(Create().TryGetTitle(outside, out _));
        Assert.False(Create().TryGetTitle(series, out _));
        Assert.False(Create().TryGetTitle(missing, out _));
        Assert.False(Create().TryGetTitle(null, out _));
    }

    [Fact]
    public void Lookups_are_cached_per_path()
    {
        var path = Strm("Movies/M/M.strm", Signed("movie", "tt4"));
        var locator = Create();
        Assert.True(locator.TryGetTitle(new Movie { Path = path }, out _));

        File.Delete(path);

        Assert.True(locator.TryGetTitle(new Movie { Path = path }, out var title));
        Assert.Equal("tt4", title!.StremioId);
    }
}
