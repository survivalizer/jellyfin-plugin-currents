using Jellyfin.Plugin.Currents.Segments;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Segments;

public sealed class SegmentGateTests : IDisposable
{
    private static readonly CurrentsTitle Episode = new("series", "tt0903747:1:1");
    private readonly FakeSettings _settings = new();
    private readonly SegmentStore _store;
    private readonly SegmentGate _gate;

    public SegmentGateTests()
    {
        _store = new SegmentStore(_settings, new ManualTimeProvider(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero)));
        _gate = new SegmentGate(_store, _settings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private static long Minutes(double value) => (long)(value * TimeSpan.TicksPerMinute);

    private void Reference(double minutes) =>
        _store.Set(Episode, new SegmentLookup([new SkipMarker(MarkerKind.Intro, 0, 5_000)], Minutes(minutes)));

    [Fact]
    public void Allows_versions_within_the_tolerance_only()
    {
        Reference(60);

        Assert.True(_gate.Allows(Episode, Minutes(60.5)));
        Assert.True(_gate.Allows(Episode, Minutes(59.0)));
        Assert.False(_gate.Allows(Episode, Minutes(62)));

        _settings.Current.SegmentTolerancePercent = 5;
        Assert.True(_gate.Allows(Episode, Minutes(62)));
    }

    [Fact]
    public void Unknown_runtimes_follow_the_setting()
    {
        Assert.False(_gate.Allows(Episode, Minutes(60)));

        Reference(60);
        Assert.False(_gate.Allows(Episode, null));

        _settings.Current.SegmentsWhenRuntimeUnknown = true;
        Assert.True(_gate.Allows(Episode, null));
        Assert.True(_gate.Allows(new CurrentsTitle("series", "tt9:1:1"), Minutes(60)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(50, 10)]
    [InlineData(double.NaN, 2)]
    [InlineData(3.5, 3.5)]
    public void Tolerance_is_clamped(double configured, double effective)
    {
        _settings.Current.SegmentTolerancePercent = configured;

        Assert.Equal(effective, _gate.TolerancePercent);
    }

    [Fact]
    public void Markers_off_allows_nothing()
    {
        Reference(60);
        _settings.Current.SegmentsWhenRuntimeUnknown = true;
        _settings.Current.EnableSegments = false;

        Assert.False(_gate.Allows(Episode, Minutes(60)));
        Assert.False(_gate.Allows(Episode, null));
    }
}
