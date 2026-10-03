using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Jellyfin.Plugin.Currents.Clients.AioMetadata;
using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Clients.RemuxDb;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Segments;
using Jellyfin.Plugin.Currents.Streams;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Currents.Web;

/// <summary>The admin diagnostics panel: compat state, caches, recent problems and connection tests.</summary>
[ApiController]
[Route("Currents/admin/diagnostics")]
[Authorize(Policy = Policies.RequiresElevation)]
public sealed class DiagnosticsController : ControllerBase
{
    private const string TestTitle = "tt0111161";
    private static readonly SegmentRequest[] SegmentTestTitles =
    [
        new(MediaKind.Movie, "tmdb", "603", null, null),
        new(MediaKind.Series, "mal", "21", null, 1),
    ];

    private readonly IAioStreamsClient _aioStreams;
    private readonly IAioMetadataClient _aioMetadata;
    private readonly IRemuxDbClient _remuxDb;
    private readonly IReadOnlyList<ISegmentSource> _segmentSources;
    private readonly IStreamService _streams;
    private readonly SegmentStore _segments;
    private readonly ProbeCache _probes;
    private readonly DiagnosticsLog _problems;
    private readonly CompatState _compat;
    private readonly ICurrentsSettings _settings;

    public DiagnosticsController(
        IAioStreamsClient aioStreams,
        IAioMetadataClient aioMetadata,
        IRemuxDbClient remuxDb,
        IEnumerable<ISegmentSource> segmentSources,
        IStreamService streams,
        SegmentStore segments,
        ProbeCache probes,
        DiagnosticsLog problems,
        CompatState compat,
        ICurrentsSettings settings)
    {
        _aioStreams = aioStreams;
        _aioMetadata = aioMetadata;
        _remuxDb = remuxDb;
        _segmentSources = segmentSources.OrderBy(s => s.Priority).ToList();
        _streams = streams;
        _segments = segments;
        _probes = probes;
        _problems = problems;
        _compat = compat;
        _settings = settings;
    }

    [HttpGet]
    public ActionResult<DiagnosticsResponse> Get()
    {
        var config = _settings.Current;
        var streams = _streams.Stats();
        return new DiagnosticsResponse(
            new CompatInfo(
                _compat.Server?.ToString(),
                $"{CompatState.TestedFrom} up to (not including) {CompatState.TestedBefore}",
                _compat.InTestedRange,
                _compat.ForceEnabled,
                _compat.Active),
            config.EnableVersions,
            config.EnableSegments,
            !string.IsNullOrWhiteSpace(config.TheIntroDbApiKey),
            !string.IsNullOrWhiteSpace(config.PublicMetaDbApiKey),
            Cache(streams.Hits, streams.Misses, streams.Entries),
            Cache(_segments.Hits, _segments.Misses, _segments.Count),
            _probes.Count,
            _problems.Recent());
    }

    [HttpPost("test")]
    public async Task<ActionResult<IReadOnlyList<ConnectionTest>>> Test(CancellationToken cancellationToken)
    {
        var tests = new List<Task<ConnectionTest>>
        {
            RunAsync("AIOStreams", () => TestAioStreamsAsync(cancellationToken)),
            RunAsync("AIOMetadata", () => TestAioMetadataAsync(cancellationToken)),
            RunAsync("RemuxDB", () => TestRemuxDbAsync(cancellationToken)),
        };
        tests.AddRange(_segmentSources.Select(s => RunAsync(s.Name, () => TestSegmentsAsync(s, cancellationToken))));
        IReadOnlyList<ConnectionTest> results = await Task.WhenAll(tests).ConfigureAwait(false);
        return Ok(results);
    }

    private static CacheInfo Cache(long hits, long misses, int entries) =>
        new(hits, misses, entries, hits + misses == 0 ? null : Math.Round((double)hits / (hits + misses), 3));

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A connection test reports any failure as its result; it must never fail the whole request.")]
    private static async Task<ConnectionTest> RunAsync(string name, Func<Task<(string Status, string Message)>> test)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var (status, message) = await test().ConfigureAwait(false);
            return new ConnectionTest(name, status, message, watch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new ConnectionTest(name, "failed", SecretMasker.Mask(ex.Message), watch.ElapsedMilliseconds);
        }
    }

    private async Task<(string Status, string Message)> TestAioStreamsAsync(CancellationToken cancellationToken)
    {
        if (!AioStreamsCredentials.TryParse(_settings.Current.AioStreamsManifestUrl, out var credentials, out _))
        {
            return ("off", "No default AIOStreams config is saved.");
        }

        var outcome = await _aioStreams.SearchAsync(credentials, "movie", TestTitle, cancellationToken).ConfigureAwait(false);
        return ("ok", string.Create(CultureInfo.InvariantCulture, $"{outcome.Results.Count} stream(s) for a test title."));
    }

    private async Task<(string Status, string Message)> TestAioMetadataAsync(CancellationToken cancellationToken)
    {
        if (!AioMetadataEndpoint.TryParse(_settings.Current.AioMetadataManifestUrl, out var endpoint, out _))
        {
            return ("off", "No AIOMetadata manifest is saved.");
        }

        var manifest = await _aioMetadata.GetManifestAsync(endpoint, cancellationToken).ConfigureAwait(false);
        return ("ok", string.Create(CultureInfo.InvariantCulture, $"{manifest.Catalogs.Count} catalog(s)."));
    }

    private async Task<(string Status, string Message)> TestRemuxDbAsync(CancellationToken cancellationToken)
    {
        if (!_settings.Current.EnableRemuxDb)
        {
            return ("off", "RemuxDB is turned off.");
        }

        var versions = await _remuxDb.VersionsAsync(TestTitle, cancellationToken).ConfigureAwait(false);
        return ("ok", string.Create(CultureInfo.InvariantCulture, $"{versions.Count} version(s) for a test title."));
    }

    private static async Task<(string Status, string Message)> TestSegmentsAsync(ISegmentSource source, CancellationToken cancellationToken)
    {
        var request = Array.Find(SegmentTestTitles, source.AppliesTo);
        if (request is null)
        {
            return ("off", "Not in use (no API key, or no test title it covers).");
        }

        var markers = await source.GetAsync(request, null, cancellationToken).ConfigureAwait(false);
        return ("ok", markers is null
            ? "Reachable; no markers for the test title."
            : string.Create(CultureInfo.InvariantCulture, $"{markers.Markers.Count} marker(s) for a test title."));
    }
}
