using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Library;

public sealed class LibraryWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "currents-writer-" + Guid.NewGuid().ToString("N"));
    private readonly StrmSigner _signer = new(StrmSigner.NewSecret());
    private readonly LibraryWriter _writer;

    public LibraryWriterTests()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        _writer = new LibraryWriter(new LibraryPaths(_root), _signer, "http://127.0.0.1:8096", time);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Writes_movie_folder_nfo_and_signed_strm()
    {
        var key = new TitleKey(MediaKind.Movie, "imdb", "tt0111161");
        var meta = new StremioMeta { Id = "tt0111161", Name = "The Shawshank Redemption", Year = "1994" };

        var result = _writer.WriteMovie(key, meta, existingRelativeFolder: null);

        Assert.True(result.Changed);
        Assert.Equal(Path.Combine("Movies", "The Shawshank Redemption (1994) [imdbid-tt0111161]"), result.RelativeFolder);
        var folder = Path.Combine(_root, result.RelativeFolder);
        Assert.True(File.Exists(Path.Combine(folder, "movie.nfo")));
        var strm = File.ReadAllText(Path.Combine(folder, "The Shawshank Redemption (1994).strm"));
        Assert.StartsWith("http://127.0.0.1:8096/Currents/play/movie/tt0111161?sig=", strm, StringComparison.Ordinal);
        Assert.EndsWith("\n", strm, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
    }

    [Fact]
    public void Rewriting_identical_content_reports_unchanged()
    {
        var key = new TitleKey(MediaKind.Movie, "imdb", "tt1");
        var meta = new StremioMeta { Id = "tt1", Name = "A", Year = "2000" };

        _writer.WriteMovie(key, meta, null);
        var second = _writer.WriteMovie(key, meta, null);

        Assert.False(second.Changed);
    }

    [Fact]
    public void Keeps_existing_folder_when_title_is_renamed()
    {
        var key = new TitleKey(MediaKind.Movie, "imdb", "tt1");
        var first = _writer.WriteMovie(key, new StremioMeta { Id = "tt1", Name = "Old Name", Year = "2000" }, null);

        var second = _writer.WriteMovie(key, new StremioMeta { Id = "tt1", Name = "New Name", Year = "2000" }, first.RelativeFolder);

        Assert.Equal(first.RelativeFolder, second.RelativeFolder);
        Assert.Single(Directory.GetDirectories(Path.Combine(_root, "Movies")));
    }

    [Fact]
    public void Writes_released_episodes_including_specials_and_skips_future_or_invalid()
    {
        var key = new TitleKey(MediaKind.Series, "imdb", "tt0944947");
        var meta = new StremioMeta
        {
            Id = "tt0944947",
            Name = "Game of Thrones",
            ReleaseInfo = "2011-2019",
            Videos =
            [
                new StremioVideo { Id = "tt0944947:1:1", Season = 1, Episode = 1, Released = "2011-04-17T00:00:00Z" },
                new StremioVideo { Season = 1, Episode = 2, Released = "2011-04-24T00:00:00Z" },
                new StremioVideo { Id = "tt0944947:0:1", Season = 0, Episode = 1, Released = "2011-04-10T00:00:00Z" },
                new StremioVideo { Id = "tt0944947:9:1", Season = 9, Episode = 1, Released = "2030-01-01T00:00:00Z" },
                new StremioVideo { Id = "bad", Season = 1, Episode = 0 },
                new StremioVideo { Id = "dup", Season = 1, Episode = 1 },
            ],
        };

        var result = _writer.WriteSeries(key, meta, null);
        var folder = Path.Combine(_root, result.RelativeFolder);

        Assert.True(File.Exists(Path.Combine(folder, "tvshow.nfo")));
        var files = Directory.GetFiles(folder, "*.strm", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(folder, f))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(
            new[] {
                Path.Combine("Season 01", "Game of Thrones (2011) S01E01.strm"),
                Path.Combine("Season 01", "Game of Thrones (2011) S01E02.strm"),
                Path.Combine("Specials", "Game of Thrones (2011) S00E01.strm"),
            },
            files);
        var e2 = File.ReadAllText(Path.Combine(folder, "Season 01", "Game of Thrones (2011) S01E02.strm"));
        Assert.Contains("/Currents/play/series/tt0944947%3A1%3A2?sig=", e2, StringComparison.Ordinal);
    }

    [Fact]
    public void Anime_episode_without_id_uses_episode_only_convention()
    {
        var key = new TitleKey(MediaKind.Series, "kitsu", "1376");
        var meta = new StremioMeta { Id = "kitsu:1376", Name = "Death Note", Videos = [new StremioVideo { Season = 1, Episode = 5 }] };

        var result = _writer.WriteSeries(key, meta, null);
        var strm = Directory.GetFiles(Path.Combine(_root, result.RelativeFolder), "*.strm", SearchOption.AllDirectories).Single();

        Assert.Contains("/Currents/play/series/kitsu%3A1376%3A5?sig=", File.ReadAllText(strm), StringComparison.Ordinal);
        Assert.Equal("[kitsu-1376]", key.FolderTag);
    }

    [Fact]
    public void Refuses_to_write_into_an_unmanaged_existing_folder()
    {
        var key = new TitleKey(MediaKind.Movie, "imdb", "tt1");
        var folder = Path.Combine(_root, "Movies", "A (2000) [imdbid-tt1]");
        Directory.CreateDirectory(folder);
        var mkv = Path.Combine(folder, "Movie.mkv");
        File.WriteAllText(mkv, "data");

        var ex = Assert.Throws<InvalidOperationException>(() => _writer.WriteMovie(key, new StremioMeta { Id = "tt1", Name = "A", Year = "2000" }, null));

        Assert.Contains("not managed by Currents", ex.Message, StringComparison.Ordinal);
        Assert.Equal(new[] { mkv }, Directory.GetFiles(folder));
    }

    [Fact]
    public void Adopts_a_recorded_folder_without_marker_that_holds_only_plugin_files()
    {
        var key = new TitleKey(MediaKind.Series, "imdb", "tt1");
        var rel = Path.Combine("Shows", "S (2000) [imdbid-tt1]");
        var folder = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.Combine(folder, "Season 01"));
        Directory.CreateDirectory(Path.Combine(folder, "Specials"));
        File.WriteAllText(Path.Combine(folder, "tvshow.nfo"), "old");
        File.WriteAllText(Path.Combine(folder, "Season 01", "S (2000) S01E01.strm"), "old");
        File.WriteAllText(Path.Combine(folder, "Specials", "S (2000) S00E01.strm"), "old");
        var meta = new StremioMeta { Id = "tt1", Name = "S", Year = "2000", Videos = [new StremioVideo { Id = "tt1:1:1", Season = 1, Episode = 1 }] };

        var result = _writer.WriteSeries(key, meta, rel);

        Assert.Equal(rel, result.RelativeFolder);
        Assert.True(File.Exists(Path.Combine(folder, ".currents")));
        Assert.StartsWith("http://127.0.0.1:8096/Currents/play/series/", File.ReadAllText(Path.Combine(folder, "Season 01", "S (2000) S01E01.strm")), StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_recorded_folder_without_marker_that_holds_user_media()
    {
        // Simulates LibraryRoot being changed to point at a real media folder with the same layout.
        var key = new TitleKey(MediaKind.Movie, "imdb", "tt1");
        var rel = Path.Combine("Movies", "A (2000) [imdbid-tt1]");
        var folder = Path.Combine(_root, rel);
        Directory.CreateDirectory(folder);
        var mkv = Path.Combine(folder, "Movie.mkv");
        var nfo = Path.Combine(folder, "movie.nfo");
        File.WriteAllBytes(mkv, [1, 2, 3, 4]);
        File.WriteAllText(nfo, "<movie><title>Mine</title></movie>");

        var ex = Assert.Throws<InvalidOperationException>(() => _writer.WriteMovie(key, new StremioMeta { Id = "tt1", Name = "A", Year = "2000" }, rel));

        Assert.Contains("exists and is not managed by Currents", ex.Message, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(mkv));
        Assert.Equal("<movie><title>Mine</title></movie>", File.ReadAllText(nfo));
        Assert.Equal(new[] { mkv, nfo }.Order(StringComparer.Ordinal), Directory.GetFiles(folder).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Refuses_an_unmarked_folder_with_extra_season_content()
    {
        var key = new TitleKey(MediaKind.Series, "imdb", "tt1");
        var folder = Path.Combine(_root, "Shows", "S (2000) [imdbid-tt1]");
        Directory.CreateDirectory(Path.Combine(folder, "Season 01"));
        File.WriteAllText(Path.Combine(folder, "Season 01", "S01E01.mkv"), "data");
        var meta = new StremioMeta { Id = "tt1", Name = "S", Year = "2000", Videos = [new StremioVideo { Id = "tt1:1:1", Season = 1, Episode = 1 }] };

        Assert.Throws<InvalidOperationException>(() => _writer.WriteSeries(key, meta, null));

        Assert.Equal(new[] { Path.Combine(folder, "Season 01", "S01E01.mkv") }, Directory.GetFiles(folder, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Delete_is_a_no_op_for_a_folder_without_marker()
    {
        var folder = Path.Combine(_root, "Movies", "A (2000) [imdbid-tt1]");
        Directory.CreateDirectory(folder);
        var strm = Path.Combine(folder, "A (2000).strm");
        File.WriteAllText(strm, "x");

        _writer.Delete(Path.Combine("Movies", "A (2000) [imdbid-tt1]"));

        Assert.Equal(new[] { strm }, Directory.GetFiles(folder));
    }

    [Fact]
    public void A_title_whose_folder_survived_pruning_can_be_written_again()
    {
        var key = new TitleKey(MediaKind.Movie, "imdb", "tt1");
        var meta = new StremioMeta { Id = "tt1", Name = "A", Year = "2000" };
        var first = _writer.WriteMovie(key, meta, null);
        var folder = Path.Combine(_root, first.RelativeFolder);
        var poster = Path.Combine(folder, "poster.jpg");
        File.WriteAllText(poster, "img");

        _writer.Delete(first.RelativeFolder);

        Assert.Equal(new[] { Path.Combine(folder, ".currents"), poster }.Order(StringComparer.Ordinal), Directory.GetFiles(folder).Order(StringComparer.Ordinal));

        var again = _writer.WriteMovie(key, meta, null);

        Assert.Equal(first.RelativeFolder, again.RelativeFolder);
        Assert.True(again.Changed);
        Assert.True(File.Exists(Path.Combine(folder, "movie.nfo")));
        Assert.True(File.Exists(Path.Combine(folder, "A (2000).strm")));
        Assert.Equal("img", File.ReadAllText(poster));
    }

    [Fact]
    public void Delete_keeps_user_files_and_the_marker_and_removes_only_plugin_files()
    {
        var key = new TitleKey(MediaKind.Series, "imdb", "tt1");
        var meta = new StremioMeta
        {
            Id = "tt1",
            Name = "S",
            Year = "2000",
            Videos = [new StremioVideo { Id = "tt1:1:1", Season = 1, Episode = 1 }],
        };
        var result = _writer.WriteSeries(key, meta, null);
        var folder = Path.Combine(_root, result.RelativeFolder);
        File.WriteAllText(Path.Combine(folder, "poster.jpg"), "x");

        _writer.Delete(result.RelativeFolder);

        Assert.Equal(
            new[] { Path.Combine(folder, ".currents"), Path.Combine(folder, "poster.jpg") }.Order(StringComparer.Ordinal),
            Directory.GetFiles(folder).Order(StringComparer.Ordinal));
        Assert.Empty(Directory.GetDirectories(folder));
    }

    [Fact]
    public void Delete_removes_a_pure_plugin_folder_entirely()
    {
        var key = new TitleKey(MediaKind.Series, "imdb", "tt1");
        var meta = new StremioMeta { Id = "tt1", Name = "S", Year = "2000", Videos = [new StremioVideo { Id = "tt1:1:1", Season = 1, Episode = 1 }] };
        var result = _writer.WriteSeries(key, meta, null);

        _writer.Delete(result.RelativeFolder);

        Assert.False(Directory.Exists(Path.Combine(_root, result.RelativeFolder)));
        Assert.True(Directory.Exists(Path.Combine(_root, "Shows")));
    }

    [Fact]
    public void Delete_removes_folder_but_refuses_paths_outside_root()
    {
        var key = new TitleKey(MediaKind.Movie, "imdb", "tt1");
        var result = _writer.WriteMovie(key, new StremioMeta { Id = "tt1", Name = "A" }, null);

        _writer.Delete(result.RelativeFolder);

        Assert.False(Directory.Exists(Path.Combine(_root, result.RelativeFolder)));
        Assert.Throws<InvalidOperationException>(() => _writer.Delete(Path.Combine("..", "elsewhere")));
        Assert.Throws<InvalidOperationException>(() => _writer.Delete(string.Empty));
    }

    [Theory]
    [InlineData("Movies")]
    [InlineData("Shows")]
    [InlineData("Shows/")]
    [InlineData("Movies/X/..")]
    public void Delete_refuses_category_folders(string relative)
    {
        Directory.CreateDirectory(Path.Combine(_root, "Movies", "X"));
        Directory.CreateDirectory(Path.Combine(_root, "Shows"));

        Assert.Throws<InvalidOperationException>(() => _writer.Delete(relative.Replace('/', Path.DirectorySeparatorChar)));

        Assert.True(Directory.Exists(Path.Combine(_root, "Movies")));
        Assert.True(Directory.Exists(Path.Combine(_root, "Shows")));
    }

    [Fact]
    public void Series_ignores_existing_folder_from_movies_category()
    {
        var key = new TitleKey(MediaKind.Series, "imdb", "tt1");
        var meta = new StremioMeta { Id = "tt1", Name = "Show", Year = "2000" };

        var result = _writer.WriteSeries(key, meta, Path.Combine("Movies", "X [imdbid-tt1]"));

        Assert.Equal(Path.Combine("Shows", "Show (2000) [imdbid-tt1]"), result.RelativeFolder);
        Assert.True(File.Exists(Path.Combine(_root, result.RelativeFolder, "tvshow.nfo")));
        Assert.False(Directory.Exists(Path.Combine(_root, "Movies")));
    }

    [Theory]
    [InlineData("Movies/..")]
    [InlineData("Movies/")]
    [InlineData("..")]
    public void Movie_ignores_malformed_existing_folder(string existing)
    {
        var key = new TitleKey(MediaKind.Movie, "imdb", "tt1");
        var meta = new StremioMeta { Id = "tt1", Name = "A", Year = "2000" };

        var result = _writer.WriteMovie(key, meta, existing);

        Assert.Equal(Path.Combine("Movies", "A (2000) [imdbid-tt1]"), result.RelativeFolder);
        Assert.Empty(Directory.GetFiles(_root));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "Movies")));
        Assert.Single(Directory.GetDirectories(_root));
    }
}
