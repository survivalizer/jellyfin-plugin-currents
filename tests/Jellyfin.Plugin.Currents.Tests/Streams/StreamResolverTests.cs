using System.Net;
using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public class StreamResolverTests
{
    private const string Aio = "https://aio.example.com";
    private readonly FakeAioStreamsClient _streams = new();
    private readonly FakeSettings _settings = new();
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.Ordinal);
    private readonly StubHttpHandler _http;
    private readonly FakeHttpClientFactory _factory;

    public StreamResolverTests()
    {
        _settings.Current.AioStreamsManifestUrl = $"{Aio}/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/pw/manifest.json";
        _http = new StubHttpHandler(r => _routes.TryGetValue(r.RequestUri!.AbsoluteUri, out var respond) ? respond() : new HttpResponseMessage(HttpStatusCode.NotFound));
        _factory = new FakeHttpClientFactory(_http);
    }

    private StreamResolver Create(ILogger<StreamResolver>? logger = null) =>
        new(_streams, _factory, _settings, new ManualTimeProvider(DateTimeOffset.UnixEpoch), logger ?? NullLogger<StreamResolver>.Instance);

    [Fact]
    public async Task Skipped_candidates_are_logged_without_their_url_path()
    {
        var dead = FakeAioStreamsClient.Stream("https://torrentio.example.com/resolve/realdebrid/SECRETKEY/abcdef/null/0/Movie.mkv");
        dead.Addon = "Torrentio";
        var placeholder = FakeAioStreamsClient.Stream($"{Aio}/api/v1/debrid/playback/ENCRYPTED/x/a.mkv");
        _streams.Outcome = new SearchOutcome([dead, placeholder, FakeAioStreamsClient.Stream("https://ok.example.com/b.mkv?token=FINALSECRET")], []);
        _routes[$"{Aio}/api/v1/debrid/playback/ENCRYPTED/x/a.mkv"] = () => StubHttpHandler.Redirect($"{Aio}/static/downloading.mp4", HttpStatusCode.TemporaryRedirect);
        _routes[$"{Aio}/static/downloading.mp4"] = () => new HttpResponseMessage(HttpStatusCode.OK);
        _routes["https://ok.example.com/b.mkv?token=FINALSECRET"] = () => new HttpResponseMessage(HttpStatusCode.OK);
        var logger = new ListLogger<StreamResolver>();

        var result = await Create(logger).ResolveAsync("movie", "tt1", CancellationToken.None);

        Assert.NotNull(result.Url);
        Assert.Contains(logger.Entries, e => e.Message.Contains("candidate 1 (Torrentio, https://torrentio.example.com)", StringComparison.Ordinal)
            && e.Message.Contains("unreachable", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, e => e.Message.Contains($"candidate 2 (unknown addon, {Aio})", StringComparison.Ordinal)
            && e.Message.Contains("placeholder", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("/resolve/realdebrid/SECRETKEY", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("SECRETKEY", StringComparison.Ordinal)
            || e.Message.Contains("ENCRYPTED", StringComparison.Ordinal)
            || e.Message.Contains("FINALSECRET", StringComparison.Ordinal)
            || e.Message.Contains("/resolve/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Skips_placeholder_and_returns_first_working_stream()
    {
        _streams.Outcome = new SearchOutcome([FakeAioStreamsClient.Stream($"{Aio}/play/1"), FakeAioStreamsClient.Stream($"{Aio}/play/2")], []);
        _routes[$"{Aio}/play/1"] = () => StubHttpHandler.Redirect($"{Aio}/static/downloading.mp4", HttpStatusCode.TemporaryRedirect);
        _routes[$"{Aio}/static/downloading.mp4"] = () => new HttpResponseMessage(HttpStatusCode.OK);
        _routes[$"{Aio}/play/2"] = () => StubHttpHandler.Redirect("https://cdn.example.com/file.mkv", HttpStatusCode.TemporaryRedirect);
        _routes["https://cdn.example.com/file.mkv"] = () => new HttpResponseMessage(HttpStatusCode.PartialContent);

        var result = await Create().ResolveAsync("movie", "tt1", CancellationToken.None);

        Assert.Equal(new Uri("https://cdn.example.com/file.mkv"), result.Url);
        Assert.Null(result.Error);
        Assert.Contains(HttpClientNames.Resolve, _factory.RequestedNames);
    }

    [Fact]
    public async Task All_placeholders_yield_an_error_not_a_redirect()
    {
        _streams.Outcome = new SearchOutcome([FakeAioStreamsClient.Stream($"{Aio}/play/1"), FakeAioStreamsClient.Stream($"{Aio}/play/2")], []);
        _routes[$"{Aio}/play/1"] = () => StubHttpHandler.Redirect($"{Aio}/static/downloading.mp4", HttpStatusCode.TemporaryRedirect);
        _routes[$"{Aio}/play/2"] = () => StubHttpHandler.Redirect($"{Aio}/static/500.mp4", HttpStatusCode.TemporaryRedirect);
        _routes[$"{Aio}/static/downloading.mp4"] = () => new HttpResponseMessage(HttpStatusCode.OK);
        _routes[$"{Aio}/static/500.mp4"] = () => new HttpResponseMessage(HttpStatusCode.OK);

        var result = await Create().ResolveAsync("movie", "tt1", CancellationToken.None);

        Assert.Null(result.Url);
        Assert.Contains("not ready", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tries_at_most_failover_attempts_streams()
    {
        _settings.Current.FailoverAttempts = 2;
        _streams.Outcome = new SearchOutcome(
            [FakeAioStreamsClient.Stream($"{Aio}/play/1"), FakeAioStreamsClient.Stream($"{Aio}/play/2"), FakeAioStreamsClient.Stream($"{Aio}/play/3")],
            []);
        _routes[$"{Aio}/play/3"] = () => new HttpResponseMessage(HttpStatusCode.OK);

        var result = await Create().ResolveAsync("movie", "tt1", CancellationToken.None);

        Assert.Null(result.Url);
        Assert.DoesNotContain(new Uri($"{Aio}/play/3"), _http.Requests);
    }

    [Fact]
    public async Task Skips_streams_that_need_request_headers()
    {
        _streams.Outcome = new SearchOutcome(
            [
                FakeAioStreamsClient.Stream("https://needs.example.com/a.mkv", new Dictionary<string, string> { ["Referer"] = "x" }),
                FakeAioStreamsClient.Stream("https://ok.example.com/b.mkv"),
            ],
            []);
        _routes["https://ok.example.com/b.mkv"] = () => new HttpResponseMessage(HttpStatusCode.OK);

        var result = await Create().ResolveAsync("movie", "tt1", CancellationToken.None);

        Assert.Equal(new Uri("https://ok.example.com/b.mkv"), result.Url);
        Assert.DoesNotContain(new Uri("https://needs.example.com/a.mkv"), _http.Requests);
    }

    [Fact]
    public async Task Caches_the_resolved_url()
    {
        _streams.Outcome = new SearchOutcome([FakeAioStreamsClient.Stream("https://ok.example.com/b.mkv")], []);
        _routes["https://ok.example.com/b.mkv"] = () => new HttpResponseMessage(HttpStatusCode.OK);
        var resolver = Create();

        await resolver.ResolveAsync("movie", "tt1", CancellationToken.None);
        var second = await resolver.ResolveAsync("movie", "tt1", CancellationToken.None);

        Assert.Equal(1, _streams.Calls);
        Assert.NotNull(second.Url);
    }

    [Fact]
    public async Task Missing_configuration_and_search_failures_return_errors()
    {
        _settings.Current.AioStreamsManifestUrl = string.Empty;
        var notConfigured = await Create().ResolveAsync("movie", "tt1", CancellationToken.None);
        Assert.Contains("not configured", notConfigured.Error, StringComparison.OrdinalIgnoreCase);

        _settings.Current.AioStreamsManifestUrl = $"{Aio}/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/pw/manifest.json";
        _streams.Exception = new AioStreamsException("AIOStreams: Invalid password");
        var failed = await Create().ResolveAsync("movie", "tt1", CancellationToken.None);
        Assert.Contains("Invalid password", failed.Error, StringComparison.Ordinal);

        _streams.Exception = null;
        _streams.Outcome = new SearchOutcome([], []);
        var none = await Create().ResolveAsync("movie", "tt1", CancellationToken.None);
        Assert.Contains("No streams", none.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_http_redirect_is_treated_as_dead_and_next_stream_wins()
    {
        _streams.Outcome = new SearchOutcome([FakeAioStreamsClient.Stream("https://bad.example.com/a"), FakeAioStreamsClient.Stream("https://ok.example.com/b.mkv")], []);
        _routes["https://bad.example.com/a"] = () => StubHttpHandler.Redirect("file:///etc/passwd", HttpStatusCode.TemporaryRedirect);
        _routes["https://ok.example.com/b.mkv"] = () => new HttpResponseMessage(HttpStatusCode.OK);

        var result = await Create().ResolveAsync("movie", "tt1", CancellationToken.None);

        Assert.Equal(new Uri("https://ok.example.com/b.mkv"), result.Url);
    }

    [Fact]
    public async Task Detects_placeholder_behind_a_proxy_on_a_different_host()
    {
        _settings.Current.AioStreamsManifestUrl = "https://aio.internal:3000/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/pw/manifest.json";
        _streams.Outcome = new SearchOutcome(
            [
                FakeAioStreamsClient.Stream("https://proxy.example.com/api/v1/debrid/playback/x/a.mkv"),
                FakeAioStreamsClient.Stream("https://ok.example.com/b.mkv"),
            ],
            []);
        _routes["https://proxy.example.com/api/v1/debrid/playback/x/a.mkv"] = () => StubHttpHandler.Redirect("https://proxy.example.com/static/some_new_error.mp4", HttpStatusCode.TemporaryRedirect);
        _routes["https://proxy.example.com/static/some_new_error.mp4"] = () => new HttpResponseMessage(HttpStatusCode.OK);
        _routes["https://ok.example.com/b.mkv"] = () => new HttpResponseMessage(HttpStatusCode.OK);

        var result = await Create().ResolveAsync("movie", "tt1", CancellationToken.None);

        Assert.Equal(new Uri("https://ok.example.com/b.mkv"), result.Url);
    }

    [Fact]
    public async Task Gives_up_on_redirect_loops()
    {
        _streams.Outcome = new SearchOutcome([FakeAioStreamsClient.Stream("https://loop.example.com/a")], []);
        _routes["https://loop.example.com/a"] = () => StubHttpHandler.Redirect("https://loop.example.com/a");

        var result = await Create().ResolveAsync("movie", "tt1", CancellationToken.None);

        Assert.Null(result.Url);
        Assert.True(_http.Requests.Count <= 6);
    }
}
