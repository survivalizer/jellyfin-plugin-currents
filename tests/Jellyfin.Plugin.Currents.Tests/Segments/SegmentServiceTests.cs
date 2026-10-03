using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Metadata;
using Jellyfin.Plugin.Currents.Segments;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Segments;

public sealed class SegmentServiceTests : IDisposable
{
    private static readonly CurrentsTitle Movie = new("movie", "tt1");
    private static readonly CurrentsTitle Anime = new("series", "kitsu:7442:5");
    private readonly FakeSettings _settings = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
    private readonly FakeAioMetadataClient _metadata = new();
    private readonly DiagnosticsLog _diagnostics;
    private readonly SegmentStore _store;

    public SegmentServiceTests()
    {
        _settings.Current.AioMetadataManifestUrl = "https://meta.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/manifest.json";
        _metadata.Metas["movie/tt1"] = new StremioMeta { Id = "tt1", Runtime = "120 min" };
        _metadata.Metas["series/kitsu:7442"] = new StremioMeta { Id = "kitsu:7442", Runtime = "24 min" };
        _diagnostics = new DiagnosticsLog(_time);
        _store = new SegmentStore(_settings, _time);
    }

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private SegmentService Create(params FakeSegmentSource[] sources) =>
        new(sources, _store, new MetaCache(_metadata, _settings, _time, NullLogger<MetaCache>.Instance), _diagnostics, NullLogger<SegmentService>.Instance);

    [Fact]
    public async Task Asks_applicable_sources_and_keeps_the_best_priority_per_kind()
    {
        var first = new FakeSegmentSource("First", 0) { Answer = new SourceMarkers([new SkipMarker(MarkerKind.Intro, 1_000, 5_000)], null) };
        var anime = new FakeSegmentSource("Anime", 1) { Applies = false };
        var last = new FakeSegmentSource("Last", 2)
        {
            Answer = new SourceMarkers([new SkipMarker(MarkerKind.Intro, 2_000, 6_000), new SkipMarker(MarkerKind.Outro, 7_000_000, null)], null),
        };

        var lookup = await Create(last, anime, first).GetAsync(Movie, CancellationToken.None);

        Assert.Equal(
            new[] { new SkipMarker(MarkerKind.Intro, 1_000, 5_000), new SkipMarker(MarkerKind.Outro, 7_000_000, 7_200_000) },
            lookup.Markers);
        Assert.Equal(TimeSpan.FromMinutes(120).Ticks, lookup.ReferenceTicks);
        Assert.Equal(TimeSpan.FromMinutes(120).Ticks, first.Targets.Single());
        Assert.Equal(0, anime.Calls);
    }

    [Fact]
    public async Task A_sources_reference_runtime_wins_over_the_metadata_runtime()
    {
        var aniSkip = new FakeSegmentSource("AniSkip", 1)
        {
            Answer = new SourceMarkers([new SkipMarker(MarkerKind.Intro, 90_000, 180_000)], 1444 * TimeSpan.TicksPerSecond),
        };

        var lookup = await Create(aniSkip).GetAsync(Anime, CancellationToken.None);

        Assert.Equal(1444 * TimeSpan.TicksPerSecond, lookup.ReferenceTicks);
        Assert.Equal(TimeSpan.FromMinutes(24).Ticks, aniSkip.Targets.Single());
    }

    [Fact]
    public async Task A_failed_source_throws_and_caches_nothing()
    {
        var broken = new FakeSegmentSource("Broken", 0) { Error = new SegmentSourceException("Broken answered 429 (too many requests).") };
        var working = new FakeSegmentSource("Working", 2) { Answer = new SourceMarkers([new SkipMarker(MarkerKind.Intro, 0, 5_000)], null) };
        var service = Create(broken, working);

        await Assert.ThrowsAsync<SegmentSourceException>(() => service.GetAsync(Movie, CancellationToken.None));
        await Assert.ThrowsAsync<SegmentSourceException>(() => service.GetAsync(Movie, CancellationToken.None));

        Assert.Equal(2, broken.Calls);
        Assert.False(_store.TryGetFresh(Movie, out _));
        Assert.Contains(_diagnostics.Recent(), e => e.Area == "Skip markers" && e.Message.Contains("Broken", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Results_and_misses_are_cached_per_title()
    {
        var source = new FakeSegmentSource("Only", 2);
        var service = Create(source);

        Assert.Empty((await service.GetAsync(Movie, CancellationToken.None)).Markers);
        Assert.Empty((await service.GetAsync(Movie, CancellationToken.None)).Markers);
        Assert.Equal(1, source.Calls);

        _time.Advance(TimeSpan.FromDays(1) + TimeSpan.FromSeconds(1));
        await service.GetAsync(Movie, CancellationToken.None);
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task Titles_without_usable_ids_or_sources_are_empty_without_asking()
    {
        var source = new FakeSegmentSource("Only", 2) { Applies = false };
        var service = Create(source);

        Assert.Same(SegmentLookup.None, await service.GetAsync(new CurrentsTitle("movie", "aiom.custom.1"), CancellationToken.None));
        Assert.Same(SegmentLookup.None, await service.GetAsync(Movie, CancellationToken.None));
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public void Sanitise_drops_clamps_and_sorts_against_the_reference()
    {
        var reference = TimeSpan.FromSeconds(100).Ticks;

        var result = SegmentService.Sanitise(
            [
                new SkipMarker(MarkerKind.Outro, 95_000, 120_000),
                new SkipMarker(MarkerKind.Preview, 100_000, 110_000),
                new SkipMarker(MarkerKind.Recap, 10_000, 10_500),
                new SkipMarker(MarkerKind.Intro, 2_000, 30_000),
                new SkipMarker(MarkerKind.Outro, 90_000, null),
            ],
            reference);

        Assert.Equal(
            new[]
            {
                new SkipMarker(MarkerKind.Intro, 2_000, 30_000),
                new SkipMarker(MarkerKind.Outro, 90_000, 100_000),
                new SkipMarker(MarkerKind.Outro, 95_000, 100_000),
            },
            result);
    }

    [Fact]
    public void Open_ended_marker_without_a_reference_is_dropped()
    {
        var result = SegmentService.Sanitise([new SkipMarker(MarkerKind.Outro, 1_000, null), new SkipMarker(MarkerKind.Intro, 0, 5_000)], null);

        Assert.Equal(new[] { new SkipMarker(MarkerKind.Intro, 0, 5_000) }, result);
    }
}
