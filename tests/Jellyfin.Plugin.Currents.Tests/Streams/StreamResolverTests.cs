using System.Net;
using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
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

    private StreamResolver Create() =>
        new(_streams, _factory, _settings, new ManualTimeProvider(DateTimeOffset.UnixEpoch), NullLogger<StreamResolver>.Instance);

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
    public async Task Gives_up_on_redirect_loops()
    {
        _streams.Outcome = new SearchOutcome([FakeAioStreamsClient.Stream("https://loop.example.com/a")], []);
        _routes["https://loop.example.com/a"] = () => StubHttpHandler.Redirect("https://loop.example.com/a");

        var result = await Create().ResolveAsync("movie", "tt1", CancellationToken.None);

        Assert.Null(result.Url);
        Assert.True(_http.Requests.Count <= 6);
    }
}
