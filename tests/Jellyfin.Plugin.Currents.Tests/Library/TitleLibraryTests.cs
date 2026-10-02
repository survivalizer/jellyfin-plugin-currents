using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Library;

public sealed class TitleLibraryTests : IDisposable
{
    private readonly FakeSettings _settings = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero));

    public TitleLibraryTests() => _settings.Current.LibraryRoot = Path.Combine(_settings.DataFolderPath, "library");

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private TitleLibrary Create() => new(_settings, _time, NullLogger<TitleLibrary>.Instance);

    private static TitleKey Key(string imdb, MediaKind kind = MediaKind.Movie) => new(kind, "imdb", imdb);

    [Fact]
    public void Add_from_search_writes_the_title_and_marks_it_search_added()
    {
        var titles = Create();

        var result = titles.AddFromSearch(Key("tt1"), new StremioMeta { Id = "tt1", Name = "Alpha", Year = "1999" });

        Assert.Equal(Path.Combine("Movies", "Alpha (1999) [imdbid-tt1]"), result.RelativeFolder);
        Assert.True(File.Exists(Path.Combine(_settings.Current.LibraryRoot, result.RelativeFolder, "Alpha (1999).strm")));
        var state = titles.Get("movie/tt1");
        Assert.NotNull(state);
        Assert.True(state.AddedBySearch);
        Assert.Empty(state.Catalogs);
        Assert.Equal(_time.GetUtcNow(), state.LastSeen);
        Assert.Equal("movie/tt1", Create().Get("movie/tt1")?.StateId); // saved to disk
    }

    [Fact]
    public void Add_from_search_of_a_catalog_title_keeps_its_folder_and_does_not_mark_it()
    {
        var titles = Create();
        titles.Use(state => state.Upsert(new TitleState { StateId = "movie/tt1", StremioId = "tt1", Folder = Path.Combine("Movies", "Old Name [imdbid-tt1]"), Catalogs = ["movie/top"] }));

        var result = titles.AddFromSearch(Key("tt1"), new StremioMeta { Id = "tt1", Name = "New Name", Year = "1999" });

        Assert.Equal(Path.Combine("Movies", "Old Name [imdbid-tt1]"), result.RelativeFolder);
        Assert.False(titles.Get("movie/tt1")!.AddedBySearch);
    }

    [Fact]
    public void Get_and_find_return_copies()
    {
        var titles = Create();
        titles.AddFromSearch(Key("tt1"), new StremioMeta { Id = "tt1", Name = "Alpha" });

        titles.Get("movie/tt1")!.Catalogs.Add("mutated");
        titles.FindBySearchId(SearchItemId.For("movie/tt1"))!.MissCount = 99;

        var state = titles.Get("movie/tt1")!;
        Assert.Empty(state.Catalogs);
        Assert.Equal(0, state.MissCount);
    }

    [Fact]
    public async Task Use_runs_one_caller_at_a_time()
    {
        var titles = Create();
        var inside = 0;
        var maxInside = 0;

        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => titles.Use(state =>
        {
            var now = Interlocked.Increment(ref inside);
            InterlockedMax(ref maxInside, now);
            Thread.Sleep(5);
            state.Upsert(new TitleState { StateId = $"movie/tt{i}", StremioId = $"tt{i}", Folder = $"Movies/T{i} [imdbid-tt{i}]" });
            Interlocked.Decrement(ref inside);
        }))));

        Assert.Equal(1, maxInside);
        Assert.Equal(8, titles.Use(state => state.Titles.Count));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
        }
    }
}
