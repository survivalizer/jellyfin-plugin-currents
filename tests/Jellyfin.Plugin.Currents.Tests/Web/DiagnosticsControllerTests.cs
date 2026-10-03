using System.Text.Json;
using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Segments;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Jellyfin.Plugin.Currents.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Web;

public sealed class DiagnosticsControllerTests : IDisposable
{
    private const string MetaUrl = "https://meta.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/manifest.json";
    private const string AioUrl = "https://aio.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/pw/manifest.json";
    private readonly FakeSettings _settings = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
    private readonly FakeAioStreamsClient _aioStreams = new();
    private readonly FakeAioMetadataClient _aioMetadata = new();
    private readonly FakeRemuxDbClient _remux = new();
    private readonly List<ISegmentSource> _sources = [];
    private readonly DiagnosticsLog _problems;

    public DiagnosticsControllerTests() => _problems = new DiagnosticsLog(_time);

    public void Dispose()
    {
        if (Directory.Exists(_settings.DataFolderPath))
        {
            Directory.Delete(_settings.DataFolderPath, recursive: true);
        }
    }

    private DiagnosticsController Create(Version? server = null) => new(
        _aioStreams,
        _aioMetadata,
        _remux,
        _sources,
        new StreamService(_aioStreams, _settings, _problems, _time, NullLogger<StreamService>.Instance),
        new SegmentStore(_settings, _time),
        new ProbeCache(_settings, _time),
        _problems,
        new CompatState(server ?? new Version(12, 1, 0), _settings),
        _settings);

    private static IReadOnlyList<ConnectionTest> Results(ActionResult<IReadOnlyList<ConnectionTest>> result) =>
        Assert.IsAssignableFrom<IReadOnlyList<ConnectionTest>>(Assert.IsType<OkObjectResult>(result.Result).Value);

    [Fact]
    public void Reports_compat_caches_keys_and_problems_without_the_keys()
    {
        _settings.Current.TheIntroDbApiKey = "tidb-secret";
        _settings.Current.PublicMetaDbApiKey = "pmdb-secret";
        _problems.Record("Sync", "Catalog movie/x failed: boom");

        var response = Create(new Version(13, 0, 0)).Get().Value!;

        Assert.Equal("13.0.0", response.Compat.Server);
        Assert.False(response.Compat.InTestedRange);
        Assert.False(response.Compat.Active);
        Assert.True(response.TheIntroDbKeySet);
        Assert.True(response.PublicMetaDbKeySet);
        Assert.Null(response.StreamCache.HitRate);
        Assert.Equal(0, response.ProbesStored);
        Assert.Equal("Sync", Assert.Single(response.RecentProblems).Area);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connection_tests_report_every_service()
    {
        _settings.Current.AioStreamsManifestUrl = AioUrl;
        _settings.Current.AioMetadataManifestUrl = MetaUrl;
        _aioStreams.Outcome = new SearchOutcome([FakeAioStreamsClient.Stream("https://cdn.example.com/a")], []);
        _aioMetadata.Manifest = new StremioManifest { Catalogs = [new StremioCatalog { Type = "movie", Id = "x" }] };
        _sources.Add(new FakeSegmentSource("TheIntroDB", 2) { Answer = new SourceMarkers([new SkipMarker(MarkerKind.Intro, 0, 40_000)], null) });
        _sources.Add(new FakeSegmentSource("PublicMetaDB", 0) { Applies = false });
        _sources.Add(new FakeSegmentSource("AniSkip", 1) { Error = new SegmentSourceException("AniSkip is unreachable: timeout") });

        var tests = Results(await Create().Test(CancellationToken.None));

        Assert.Equal(new[] { "AIOStreams", "AIOMetadata", "RemuxDB", "PublicMetaDB", "AniSkip", "TheIntroDB" }, tests.Select(t => t.Name));
        Assert.Equal(new[] { "ok", "ok", "ok", "off", "failed", "ok" }, tests.Select(t => t.Status));
        Assert.Contains("1 stream", tests[0].Message, StringComparison.Ordinal);
        Assert.Contains("unreachable", tests[4].Message, StringComparison.Ordinal);
        Assert.Contains("1 marker", tests[5].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unconfigured_services_are_off()
    {
        _settings.Current.EnableRemuxDb = false;

        var tests = Results(await Create().Test(CancellationToken.None));

        Assert.All(tests, t => Assert.Equal("off", t.Status));
        Assert.Equal(0, _aioStreams.Calls);
        Assert.Equal(0, _remux.Calls);
    }

    [Fact]
    public async Task Failures_are_reported_masked()
    {
        _settings.Current.AioStreamsManifestUrl = AioUrl;
        _aioStreams.Exception = new HttpRequestException("refused " + AioUrl);

        var tests = Results(await Create().Test(CancellationToken.None));

        var aio = tests.Single(t => t.Name == "AIOStreams");
        Assert.Equal("failed", aio.Status);
        Assert.DoesNotContain("/pw/", aio.Message, StringComparison.Ordinal);
    }
}
