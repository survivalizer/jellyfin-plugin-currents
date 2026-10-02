using Jellyfin.Plugin.Currents.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Library;

public sealed class StateStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "currents-state-" + Guid.NewGuid().ToString("N"));

    private string StatePath => Path.Combine(_dir, "state.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Missing_file_loads_empty()
    {
        var store = StateStore.Load(StatePath, NullLogger.Instance);

        Assert.Empty(store.Titles);
    }

    [Fact]
    public void Round_trips_titles()
    {
        var store = StateStore.Load(StatePath, NullLogger.Instance);
        store.Upsert(new TitleState
        {
            StateId = "movie/tt1",
            Kind = MediaKind.Movie,
            StremioId = "tt1",
            Folder = "Movies/A [imdbid-tt1]",
            Catalogs = ["movie/tmdb.top"],
            MissCount = 2,
            LastSeen = DateTimeOffset.UnixEpoch,
        });
        store.Save();

        var loaded = StateStore.Load(StatePath, NullLogger.Instance);
        var title = loaded.Get("movie/tt1");

        Assert.NotNull(title);
        Assert.Equal(2, title!.MissCount);
        Assert.Equal(new[] {"movie/tmdb.top"}, title.Catalogs);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Corrupt_file_is_set_aside_and_store_starts_empty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(StatePath, "{ not json");

        var store = StateStore.Load(StatePath, NullLogger.Instance);

        Assert.Empty(store.Titles);
        Assert.Single(Directory.GetFiles(_dir, "state.json.corrupt-*"));
    }

    [Fact]
    public void Remove_deletes_entries()
    {
        var store = StateStore.Load(StatePath, NullLogger.Instance);
        store.Upsert(new TitleState { StateId = "movie/tt1" });

        Assert.True(store.Remove("movie/tt1"));
        Assert.Null(store.Get("movie/tt1"));
    }

    [Fact]
    public void Null_fields_are_normalised_on_load()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(StatePath, "[{\"stateId\":\"movie/tt1\",\"catalogs\":null,\"folder\":null,\"stremioId\":null}]");

        var title = StateStore.Load(StatePath, NullLogger.Instance).Get("movie/tt1");

        Assert.NotNull(title);
        Assert.Empty(title!.Catalogs);
        Assert.Equal(string.Empty, title.Folder);
        Assert.Equal(string.Empty, title.StremioId);
    }

    [Fact]
    public void Null_catalog_entries_are_dropped()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(StatePath, "[{\"stateId\":\"movie/tt1\",\"catalogs\":[null,\"\",\"a\"]}]");

        var title = StateStore.Load(StatePath, NullLogger.Instance).Get("movie/tt1");

        Assert.Equal(new[] { "a" }, title!.Catalogs);
    }

    [Fact]
    public void Duplicate_state_ids_are_merged_conservatively()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(
            StatePath,
            "[{\"stateId\":\"movie/tt1\",\"addedBySearch\":true,\"missCount\":5,\"catalogs\":[\"a\"],\"lastSeen\":\"2026-01-02T00:00:00Z\"},"
            + "{\"stateId\":\"movie/tt1\",\"addedBySearch\":false,\"missCount\":1,\"catalogs\":[\"b\"],\"lastSeen\":\"2026-01-01T00:00:00Z\"}]");

        var store = StateStore.Load(StatePath, NullLogger.Instance);
        var title = store.Get("movie/tt1");

        Assert.Single(store.Titles);
        Assert.True(title!.AddedBySearch);
        Assert.Equal(1, title.MissCount);
        Assert.Equal(new[] { "a", "b" }, title.Catalogs);
        Assert.Equal(DateTimeOffset.Parse("2026-01-02T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture), title.LastSeen);
    }

    [Theory]
    [InlineData("null", 0)]
    [InlineData("[null]", 0)]
    [InlineData("[null, {\"stateId\":\"movie/tt1\"}, null]", 1)]
    public void Null_json_and_null_entries_are_skipped_without_throwing(string json, int expectedCount)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(StatePath, json);

        var store = StateStore.Load(StatePath, NullLogger.Instance);

        Assert.Equal(expectedCount, store.Titles.Count);
        Assert.Empty(Directory.GetFiles(_dir, "state.json.corrupt-*"));
    }
}
