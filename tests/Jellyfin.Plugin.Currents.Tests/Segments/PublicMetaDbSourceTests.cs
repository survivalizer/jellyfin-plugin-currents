using System.Net;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Segments;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Segments;

public sealed class PublicMetaDbSourceTests : IDisposable
{
    private readonly FakeSettings _settings = new();
    private readonly List<HttpRequestMessage> _sent = [];
    private readonly DiagnosticsLog _diagnostics = new(new ManualTimeProvider(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero)));
    private readonly PublicMetaDbSource _source;
    private Func<HttpRequestMessage, HttpResponseMessage> _respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    public PublicMetaDbSourceTests()
    {
        _settings.Current.PublicMetaDbApiKey = "pmdb-key";
        _source = new PublicMetaDbSource(
            new FakeHttpClientFactory(new StubHttpHandler(r =>
            {
                _sent.Add(r);
                return _respond(r);
            })),
            _settings,
            _diagnostics);
    }

    public void Dispose() => _source.Dispose();

    [Fact]
    public async Task Episode_query_sends_the_key_and_picks_the_newest_agreeing_streaming_row()
    {
        _respond = _ => StubHttpHandler.Json("""
            {"items":[
             {"source":"physical","intro_start_ms":10000,"intro_end_ms":60000,"credits_start_ms":null,"credits_end_ms":null,"updated":"2026-09-30T00:00:00Z"},
             {"source":"streaming","intro_start_ms":30000,"intro_end_ms":90000,"credits_start_ms":2500000,"credits_end_ms":null,"updated":"2026-01-01T00:00:00Z"},
             {"source":"streaming","intro_start_ms":31000,"intro_end_ms":91000,"credits_start_ms":2502000,"credits_end_ms":null,"updated":"2025-06-01T00:00:00Z"},
             {"source":"streaming","intro_start_ms":500,"intro_end_ms":1500,"credits_start_ms":null,"credits_end_ms":null,"updated":"2026-09-01T00:00:00Z"}]}
            """);

        var result = await _source.GetAsync(new SegmentRequest(MediaKind.Series, "tmdb", "1396", 1, 1), null, CancellationToken.None);

        var request = Assert.Single(_sent);
        Assert.Equal("https://publicmetadb.com/api/external/skips?tmdb_id=1396&media_type=tv&season=1&episode=1", request.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("pmdb-key", request.Headers.Authorization.Parameter);
        Assert.Equal(new[] { new SkipMarker(MarkerKind.Intro, 30_000, 90_000), new SkipMarker(MarkerKind.Outro, 2_500_000, null) }, result!.Markers);
        Assert.Null(result.ReferenceTicks);
    }

    [Fact]
    public async Task Movie_query_falls_back_to_physical_rows()
    {
        _respond = _ => StubHttpHandler.Json("""{"items":[{"source":"physical","intro_start_ms":0,"intro_end_ms":45000,"credits_start_ms":7000000,"credits_end_ms":7300000}]}""");

        var result = await _source.GetAsync(new SegmentRequest(MediaKind.Movie, "tmdb", "603", null, null), null, CancellationToken.None);

        Assert.Equal("https://publicmetadb.com/api/external/skips?tmdb_id=603&media_type=movie", Assert.Single(_sent).RequestUri!.AbsoluteUri);
        Assert.Equal(new[] { new SkipMarker(MarkerKind.Intro, 0, 45_000), new SkipMarker(MarkerKind.Outro, 7_000_000, 7_300_000) }, result!.Markers);
    }

    [Fact]
    public async Task Not_found_and_rows_without_markers_are_null()
    {
        Assert.Null(await _source.GetAsync(new SegmentRequest(MediaKind.Movie, "tmdb", "1", null, null), null, CancellationToken.None));

        _respond = _ => StubHttpHandler.Json("""{"items":[{"source":"streaming","intro_start_ms":null,"intro_end_ms":null}]}""");
        Assert.Null(await _source.GetAsync(new SegmentRequest(MediaKind.Movie, "tmdb", "1", null, null), null, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, "{}")]
    [InlineData(HttpStatusCode.OK, "<!doctype html>")]
    public async Task Server_errors_and_non_json_answers_throw(HttpStatusCode status, string body)
    {
        _respond = _ => StubHttpHandler.Json(body, status);

        await Assert.ThrowsAsync<SegmentSourceException>(() => _source.GetAsync(new SegmentRequest(MediaKind.Movie, "tmdb", "1", null, null), null, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_rejected_key_gives_no_markers_and_is_recorded_once(HttpStatusCode status)
    {
        _respond = _ => StubHttpHandler.Json("""{"error":"Invalid API key format"}""", status);
        var request = new SegmentRequest(MediaKind.Movie, "tmdb", "1", null, null);

        Assert.Null(await _source.GetAsync(request, null, CancellationToken.None));
        Assert.Null(await _source.GetAsync(request, null, CancellationToken.None));

        var entry = Assert.Single(_diagnostics.Recent());
        Assert.Equal("Skip markers", entry.Area);
        Assert.Contains("PublicMetaDB rejected the API key", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("pmdb-key", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_distinct_rejection_status_is_recorded()
    {
        var request = new SegmentRequest(MediaKind.Movie, "tmdb", "1", null, null);
        _respond = _ => StubHttpHandler.Json("{}", HttpStatusCode.Unauthorized);
        await _source.GetAsync(request, null, CancellationToken.None);
        _respond = _ => StubHttpHandler.Json("{}", HttpStatusCode.Forbidden);
        await _source.GetAsync(request, null, CancellationToken.None);

        Assert.Equal(2, _diagnostics.Recent().Count);
    }

    [Fact]
    public void Applies_only_with_a_key_and_a_tmdb_id()
    {
        Assert.True(_source.AppliesTo(new SegmentRequest(MediaKind.Movie, "tmdb", "1", null, null)));
        Assert.True(_source.AppliesTo(new SegmentRequest(MediaKind.Series, "tmdb", "1", 1, 2)));
        Assert.False(_source.AppliesTo(new SegmentRequest(MediaKind.Movie, "imdb", "tt1", null, null)));
        Assert.False(_source.AppliesTo(new SegmentRequest(MediaKind.Series, "tmdb", "1", null, 2)));

        _settings.Current.PublicMetaDbApiKey = "  ";
        Assert.False(_source.AppliesTo(new SegmentRequest(MediaKind.Movie, "tmdb", "1", null, null)));
    }
}
