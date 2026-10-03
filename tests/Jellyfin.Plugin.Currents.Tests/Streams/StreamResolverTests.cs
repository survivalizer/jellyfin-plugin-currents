using System.Net;
using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Jellyfin.Plugin.Currents.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public sealed class StreamResolverTests : IDisposable
{
    private const string Aio = "https://aio.example.com";
    private const string UserId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
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

    private StreamResolver Create(ILogger<StreamResolver>? logger = null)
    {
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var users = new UserStore(_settings, NullLogger<UserStore>.Instance);
        return new(
            new StreamService(_streams, _settings, new Jellyfin.Plugin.Currents.Common.DiagnosticsLog(time), time, NullLogger<StreamService>.Instance),
            new StreamProfileResolver(users, _settings),
            _factory,
            _settings,
            time,
            logger ?? NullLogger<StreamResolver>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_settings.DataFolderPath))
            {
                Directory.Delete(_settings.DataFolderPath, true);
            }
        }
        catch (IOException)
        {
            // Best-effort test cleanup.
        }
    }

    private string KeyOf(string url) =>
        StreamIdentity.Keys(_streams.Outcome.Results)[_streams.Outcome.Results.ToList().FindIndex(r => r.Url == url)];

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
    public async Task Degraded_strm_playback_skips_header_streams()
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
        Assert.Null(result.Headers);
    }

    private VersionTicket Ticket(string url) => new(Guid.Empty, "movie", "tt1", KeyOf(url));

    [Fact]
    public async Task Version_resolves_carry_cleaned_headers_through_same_origin_redirects()
    {
        const string url = "https://dav.example.com/files/movie.mkv";
        _streams.Outcome = new SearchOutcome([FakeAioStreamsClient.Stream(url, new() { ["Authorization"] = "Basic SECRET", ["X-Custom"] = "1", ["Host"] = "evil" })], []);
        _routes[url] = () => StubHttpHandler.Redirect("https://dav.example.com/files/real.mkv");
        _routes["https://dav.example.com/files/real.mkv"] = () => new HttpResponseMessage(HttpStatusCode.PartialContent);

        var result = await Create().ResolveAsync(Ticket(url), CancellationToken.None);

        Assert.Equal(new Uri("https://dav.example.com/files/real.mkv"), result.Url);
        Assert.Equal(new[] { "Authorization", "X-Custom" }, result.Headers!.Keys.Order(StringComparer.Ordinal));
        Assert.All(_http.Sent, s => Assert.Equal("Basic SECRET", s.Headers["Authorization"]));
        Assert.DoesNotContain(_http.Sent, s => s.Headers.ContainsKey("Host"));
    }

    [Fact]
    public async Task Credentials_are_dropped_on_cross_origin_redirects()
    {
        const string url = "https://dav.example.com/files/movie.mkv";
        _streams.Outcome = new SearchOutcome([FakeAioStreamsClient.Stream(url, new() { ["Authorization"] = "Bearer SECRET", ["Cookie"] = "s=1", ["Referer"] = "https://dav.example.com/" })], []);
        _routes[url] = () => StubHttpHandler.Redirect("https://cdn.example.net/signed/movie.mkv");
        _routes["https://cdn.example.net/signed/movie.mkv"] = () => new HttpResponseMessage(HttpStatusCode.OK);

        var result = await Create().ResolveAsync(Ticket(url), CancellationToken.None);

        var cdn = _http.Sent.Single(s => s.Uri.Host == "cdn.example.net");
        Assert.False(cdn.Headers.ContainsKey("Authorization"));
        Assert.False(cdn.Headers.ContainsKey("Cookie"));
        Assert.Equal("https://dav.example.com/", cdn.Headers["Referer"]);
        Assert.Equal(new[] { "Referer" }, result.Headers!.Keys);
    }

    [Fact]
    public async Task Custom_auth_headers_are_dropped_on_cross_origin_redirects()
    {
        const string url = "https://dav.example.com/files/movie.mkv";
        _streams.Outcome = new SearchOutcome([FakeAioStreamsClient.Stream(url, new() { ["X-Api-Key"] = "SECRET", ["User-Agent"] = "Kodi" })], []);
        _routes[url] = () => StubHttpHandler.Redirect("https://cdn.example.net/signed/movie.mkv");
        _routes["https://cdn.example.net/signed/movie.mkv"] = () => new HttpResponseMessage(HttpStatusCode.OK);

        var result = await Create().ResolveAsync(Ticket(url), CancellationToken.None);

        Assert.Equal("SECRET", _http.Sent.Single(s => s.Uri.Host == "dav.example.com").Headers["X-Api-Key"]);
        var cdn = _http.Sent.Single(s => s.Uri.Host == "cdn.example.net");
        Assert.False(cdn.Headers.ContainsKey("X-Api-Key"));
        Assert.Equal("Kodi", cdn.Headers["User-Agent"]);
        Assert.Equal(new[] { "User-Agent" }, result.Headers!.Keys);
    }

    [Fact]
    public async Task Streams_without_headers_resolve_without_headers()
    {
        const string url = "https://ok.example.com/b.mkv";
        _streams.Outcome = new SearchOutcome([FakeAioStreamsClient.Stream(url)], []);
        _routes[url] = () => new HttpResponseMessage(HttpStatusCode.OK);

        Assert.Null((await Create().ResolveAsync(Ticket(url), CancellationToken.None)).Headers);
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

    [Fact]
    public async Task Version_ticket_plays_its_own_stream_first()
    {
        _streams.Outcome = new SearchOutcome([FakeAioStreamsClient.Stream($"{Aio}/play/1"), FakeAioStreamsClient.Stream($"{Aio}/play/2")], []);
        _routes[$"{Aio}/play/1"] = () => new HttpResponseMessage(HttpStatusCode.OK);
        _routes[$"{Aio}/play/2"] = () => StubHttpHandler.Redirect("https://cdn.example.com/two.mkv", HttpStatusCode.TemporaryRedirect);
        _routes["https://cdn.example.com/two.mkv"] = () => new HttpResponseMessage(HttpStatusCode.PartialContent);

        var result = await Create().ResolveAsync(new VersionTicket(Guid.Empty, "movie", "tt1", KeyOf($"{Aio}/play/2")), CancellationToken.None);

        Assert.Equal(new Uri("https://cdn.example.com/two.mkv"), result.Url);
    }

    [Fact]
    public async Task Dead_chosen_stream_fails_over_in_ranked_order()
    {
        _streams.Outcome = new SearchOutcome(
            [FakeAioStreamsClient.Stream($"{Aio}/play/1"), FakeAioStreamsClient.Stream($"{Aio}/play/2"), FakeAioStreamsClient.Stream($"{Aio}/play/3")],
            []);
        _routes[$"{Aio}/play/1"] = () => new HttpResponseMessage(HttpStatusCode.OK);
        _routes[$"{Aio}/play/3"] = () => StubHttpHandler.Redirect($"{Aio}/static/downloading.mp4", HttpStatusCode.TemporaryRedirect);
        _routes[$"{Aio}/static/downloading.mp4"] = () => new HttpResponseMessage(HttpStatusCode.OK);

        var result = await Create().ResolveAsync(new VersionTicket(Guid.Empty, "movie", "tt1", KeyOf($"{Aio}/play/3")), CancellationToken.None);

        Assert.Equal(new Uri($"{Aio}/play/1"), result.Url);
    }

    [Fact]
    public async Task Vanished_stream_falls_back_to_ranked_order()
    {
        _streams.Outcome = new SearchOutcome([FakeAioStreamsClient.Stream($"{Aio}/play/1")], []);
        _routes[$"{Aio}/play/1"] = () => new HttpResponseMessage(HttpStatusCode.OK);

        var result = await Create().ResolveAsync(new VersionTicket(Guid.Empty, "movie", "tt1", "00000000000000000000000000000000"), CancellationToken.None);

        Assert.Equal(new Uri($"{Aio}/play/1"), result.Url);
    }

    [Fact]
    public async Task Ticket_resolves_with_that_users_own_config()
    {
        var users = new UserStore(_settings, NullLogger<UserStore>.Instance);
        users.Update(Guid.ParseExact(UserId, "N"), r => r.Self.AioStreamsManifestUrl = $"{Aio}/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/alicepw/manifest.json");
        _streams.Outcome = new SearchOutcome([FakeAioStreamsClient.Stream($"{Aio}/play/1")], []);
        _routes[$"{Aio}/play/1"] = () => new HttpResponseMessage(HttpStatusCode.OK);

        await Create().ResolveAsync(new VersionTicket(Guid.ParseExact(UserId, "N"), "movie", "tt1", KeyOf($"{Aio}/play/1")), CancellationToken.None);

        Assert.Equal("alicepw", _streams.LastCredentials!.Password);
    }

    [Fact]
    public async Task Disabled_user_cannot_resolve()
    {
        var users = new UserStore(_settings, NullLogger<UserStore>.Instance);
        users.Update(Guid.ParseExact(UserId, "N"), r => r.StreamsDisabled = true);

        var result = await Create().ResolveAsync(new VersionTicket(Guid.ParseExact(UserId, "N"), "movie", "tt1", "k"), CancellationToken.None);

        Assert.Null(result.Url);
        Assert.Contains("disabled", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _streams.Calls);
    }

    [Fact]
    public async Task Concurrent_resolves_of_one_version_share_one_attempt()
    {
        _streams.Outcome = new SearchOutcome([FakeAioStreamsClient.Stream($"{Aio}/play/1")], []);
        _routes[$"{Aio}/play/1"] = () => new HttpResponseMessage(HttpStatusCode.OK);
        var resolver = Create();
        var ticket = new VersionTicket(Guid.Empty, "movie", "tt1", KeyOf($"{Aio}/play/1"));
        _streams.Gate = new TaskCompletionSource<SearchOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);

        var pending = Enumerable.Range(0, 5).Select(_ => resolver.ResolveAsync(ticket, CancellationToken.None)).ToList();
        _streams.Gate.SetResult(_streams.Outcome);
        var results = await Task.WhenAll(pending);

        Assert.All(results, r => Assert.NotNull(r.Url));
        Assert.Single(_http.Requests, u => u.AbsoluteUri == $"{Aio}/play/1");
    }
}
