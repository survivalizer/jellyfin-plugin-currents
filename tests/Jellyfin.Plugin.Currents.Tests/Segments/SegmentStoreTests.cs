using Jellyfin.Plugin.Currents.Segments;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Segments;

public sealed class SegmentStoreTests : IDisposable
{
    private static readonly CurrentsTitle Episode = new("series", "tt0903747:1:1");
    private readonly FakeSettings _settings = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private static SegmentLookup Found() => new([new SkipMarker(MarkerKind.Intro, 1_000, 5_000)], TimeSpan.FromMinutes(47).Ticks);

    [Fact]
    public void Found_lookups_survive_a_restart_for_about_thirty_days()
    {
        new SegmentStore(_settings, _time).Set(Episode, Found());

        var store = new SegmentStore(_settings, _time);
        Assert.True(store.TryGetFresh(Episode, out var lookup));
        Assert.Equal(Found().Markers, lookup.Markers);
        Assert.Equal(TimeSpan.FromMinutes(47).Ticks, lookup.ReferenceTicks);
        Assert.Equal(1, store.Count);

        // The title's jitter is somewhere in [0.8, 1.2] of the 30-day TTL.
        _time.Advance(TimeSpan.FromDays(30 * 0.8) - TimeSpan.FromSeconds(1));
        Assert.True(store.TryGetFresh(Episode, out _));

        _time.Advance(TimeSpan.FromDays(30 * 0.4) + TimeSpan.FromSeconds(2));
        Assert.False(store.TryGetFresh(Episode, out _));
        Assert.Equal(TimeSpan.FromMinutes(47).Ticks, store.ReferenceTicks(Episode));
    }

    [Fact]
    public void Misses_are_fresh_for_about_a_week()
    {
        var store = new SegmentStore(_settings, _time);
        store.Set(Episode, SegmentLookup.None);

        _time.Advance(TimeSpan.FromDays(7 * 0.8) - TimeSpan.FromSeconds(1));
        Assert.True(store.TryGetFresh(Episode, out var lookup));
        Assert.Empty(lookup.Markers);

        _time.Advance(TimeSpan.FromDays(7 * 0.4) + TimeSpan.FromSeconds(2));
        Assert.False(store.TryGetFresh(Episode, out _));
    }

    [Fact]
    public void Expiry_is_jittered_per_title_but_stable_for_one_title()
    {
        var factors = Enumerable.Range(0, 64).Select(i => SegmentStore.Jitter(new CurrentsTitle("movie", "tt" + i))).ToList();

        Assert.All(factors, f => Assert.InRange(f, 0.8, 1.2));
        Assert.True(factors.Distinct().Count() > 8);
        Assert.Equal(SegmentStore.Jitter(Episode), SegmentStore.Jitter(Episode));
    }

    [Fact]
    public void Counts_hits_and_misses_and_clear_removes_everything()
    {
        var store = new SegmentStore(_settings, _time);
        Assert.False(store.TryGetFresh(Episode, out _));
        store.Set(Episode, Found());
        Assert.True(store.TryGetFresh(Episode, out _));

        Assert.Equal(1, store.Hits);
        Assert.Equal(1, store.Misses);

        store.Clear();
        Assert.False(store.TryGetFresh(Episode, out _));
        Assert.Null(store.ReferenceTicks(Episode));
        Assert.Equal(0, store.Count);
        Assert.False(Directory.Exists(Path.Combine(_settings.DataFolderPath, "segments")));
    }

    [Fact]
    public void A_corrupt_file_is_a_miss()
    {
        var store = new SegmentStore(_settings, _time);
        store.Set(Episode, Found());
        var file = Assert.Single(Directory.GetFiles(Path.Combine(_settings.DataFolderPath, "segments")));
        File.WriteAllText(file, "{ not json");

        Assert.False(new SegmentStore(_settings, _time).TryGetFresh(Episode, out _));
    }
}
