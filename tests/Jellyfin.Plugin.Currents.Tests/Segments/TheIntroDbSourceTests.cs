using System.Net;
using System.Net.Http.Headers;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Segments;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Segments;

public sealed class TheIntroDbSourceTests : IDisposable
{
    private readonly FakeSettings _settings = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
    private readonly List<HttpRequestMessage> _sent = [];
    private readonly TheIntroDbSource _source;
    private Func<HttpRequestMessage, HttpResponseMessage> _respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

    public TheIntroDbSourceTests()
    {
        _source = new TheIntroDbSource(
            new FakeHttpClientFactory(new StubHttpHandler(r =>
            {
                _sent.Add(r);
                return _respond(r);
            })),
            _settings,
            _time);
    }

    public void Dispose() => _source.Dispose();

    private static SegmentRequest Movie(string provider, string id) => new(MediaKind.Movie, provider, id, null, null);

    [Fact]
    public async Task Movie_by_tmdb_reads_an_intro_with_an_open_start()
    {
        _respond = _ => StubHttpHandler.Json("""{"tmdb_id":603,"type":"movie","intro":[{"start_ms":null,"end_ms":40000}]}""");

        var result = await _source.GetAsync(Movie("tmdb", "603"), null, CancellationToken.None);

        var request = Assert.Single(_sent);
        Assert.Equal("https://api.theintrodb.org/v3/media?tmdb_id=603", request.RequestUri!.AbsoluteUri);
        Assert.Null(request.Headers.Authorization);
        Assert.Equal(new[] { new SkipMarker(MarkerKind.Intro, 0, 40_000) }, result!.Markers);
        Assert.Null(result.ReferenceTicks);
    }

    [Fact]
    public async Task Episode_by_imdb_sends_season_episode_duration_and_the_key()
    {
        _settings.Current.TheIntroDbApiKey = " key-123 ";
        _respond = _ => StubHttpHandler.Json("""{"tmdb_id":1396,"type":"tv","season":1,"episode":1,"intro":[{"start_ms":228664,"end_ms":246143}],"credits":[{"start_ms":3431000,"end_ms":null}]}""");

        var result = await _source.GetAsync(new SegmentRequest(MediaKind.Series, "imdb", "tt0903747", 1, 1), TimeSpan.FromMinutes(48).Ticks, CancellationToken.None);

        var request = Assert.Single(_sent);
        Assert.Equal("https://api.theintrodb.org/v3/media?imdb_id=tt0903747&season=1&episode=1&duration_ms=2880000", request.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("key-123", request.Headers.Authorization.Parameter);
        Assert.Equal(
            new[] { new SkipMarker(MarkerKind.Intro, 228_664, 246_143), new SkipMarker(MarkerKind.Outro, 3_431_000, null) },
            result!.Markers);
    }

    [Fact]
    public async Task Recap_preview_and_several_credits_blocks_map_to_their_kinds()
    {
        _respond = _ => StubHttpHandler.Json("""{"intro":[{"start_ms":1000,"end_ms":5000}],"recap":[{"start_ms":0,"end_ms":900}],"credits":[{"start_ms":100000,"end_ms":110000},{"start_ms":120000,"end_ms":null}],"preview":[{"start_ms":130000,"end_ms":140000}],"extra":[{"start_ms":1,"end_ms":2}]}""");

        var result = await _source.GetAsync(Movie("tvdb", "81189"), null, CancellationToken.None);

        Assert.Equal(
            new[]
            {
                new SkipMarker(MarkerKind.Intro, 1_000, 5_000),
                new SkipMarker(MarkerKind.Recap, 0, 900),
                new SkipMarker(MarkerKind.Outro, 100_000, 110_000),
                new SkipMarker(MarkerKind.Outro, 120_000, null),
                new SkipMarker(MarkerKind.Preview, 130_000, 140_000),
            },
            result!.Markers);
        Assert.StartsWith("https://api.theintrodb.org/v3/media?tvdb_id=81189", Assert.Single(_sent).RequestUri!.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Not_found_and_empty_answers_are_null()
    {
        Assert.Null(await _source.GetAsync(Movie("tmdb", "1"), null, CancellationToken.None));

        _respond = _ => StubHttpHandler.Json("""{"tmdb_id":1,"type":"movie","intro":[{"start_ms":5000,"end_ms":4000}]}""");
        Assert.Null(await _source.GetAsync(Movie("tmdb", "1"), null, CancellationToken.None));
    }

    [Fact]
    public async Task Rate_limit_pauses_the_source()
    {
        _respond = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
            return response;
        };

        await Assert.ThrowsAsync<SegmentSourceException>(() => _source.GetAsync(Movie("tmdb", "1"), null, CancellationToken.None));
        _respond = _ => StubHttpHandler.Json("""{"intro":[{"start_ms":0,"end_ms":5000}]}""");
        await Assert.ThrowsAsync<SegmentSourceException>(() => _source.GetAsync(Movie("tmdb", "1"), null, CancellationToken.None));
        Assert.Single(_sent);

        _time.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));
        Assert.NotNull(await _source.GetAsync(Movie("tmdb", "1"), null, CancellationToken.None));
        Assert.Equal(2, _sent.Count);
    }

    [Fact]
    public async Task Rate_limit_without_retry_after_pauses_an_hour()
    {
        _respond = _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        await Assert.ThrowsAsync<SegmentSourceException>(() => _source.GetAsync(Movie("tmdb", "1"), null, CancellationToken.None));

        _time.Advance(TimeSpan.FromMinutes(59));
        await Assert.ThrowsAsync<SegmentSourceException>(() => _source.GetAsync(Movie("tmdb", "1"), null, CancellationToken.None));
        Assert.Single(_sent);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    [InlineData(HttpStatusCode.OK, "<html>maintenance</html>")]
    [InlineData(HttpStatusCode.OK, "null")]
    public async Task Errors_and_invalid_bodies_throw(HttpStatusCode status, string body)
    {
        _respond = _ => StubHttpHandler.Json(body, status);

        await Assert.ThrowsAsync<SegmentSourceException>(() => _source.GetAsync(Movie("tmdb", "1"), null, CancellationToken.None));
    }

    [Fact]
    public async Task Network_failures_throw_a_source_exception()
    {
        _respond = _ => throw new HttpRequestException("connection refused");

        await Assert.ThrowsAsync<SegmentSourceException>(() => _source.GetAsync(Movie("tmdb", "1"), null, CancellationToken.None));
    }

    [Theory]
    [InlineData("imdb", 1, 1, true)]
    [InlineData("tmdb", 1, 1, true)]
    [InlineData("tvdb", 1, 1, true)]
    [InlineData("imdb", null, 1, false)]
    [InlineData("kitsu", null, 1, false)]
    public void Applies_to_imdb_tmdb_and_tvdb_episodes(string provider, int? season, int? episode, bool expected) =>
        Assert.Equal(expected, _source.AppliesTo(new SegmentRequest(MediaKind.Series, provider, "1", season, episode)));

    [Fact]
    public void Applies_to_movies_by_imdb_tmdb_and_tvdb()
    {
        Assert.True(_source.AppliesTo(Movie("imdb", "tt1")));
        Assert.False(_source.AppliesTo(Movie("kitsu", "1")));
    }
}
