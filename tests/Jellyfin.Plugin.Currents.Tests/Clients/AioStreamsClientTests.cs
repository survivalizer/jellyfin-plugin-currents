using System.Net;
using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Clients;

public class AioStreamsClientTests
{
    private static AioStreamsCredentials Creds()
    {
        AioStreamsCredentials.TryParse("https://aio.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/pw/manifest.json", out var creds, out _);
        return creds!;
    }

    private static (AioStreamsClient Client, StubHttpHandler Stub) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var stub = new StubHttpHandler(respond);
        return (new AioStreamsClient(new FakeHttpClientFactory(stub), NullLogger<AioStreamsClient>.Instance), stub);
    }

    [Fact]
    public async Task Sends_basic_auth_and_parses_results_and_errors()
    {
        var (client, stub) = Create(_ => StubHttpHandler.Json(Fixture.Read("aiostreams/search-ok.json")));

        var outcome = await client.SearchAsync(Creds(), "movie", "tt0111161", CancellationToken.None);

        Assert.Equal("https://aio.example.com/api/v1/search?type=movie&id=tt0111161", stub.Requests[0].AbsoluteUri);
        Assert.Equal("Basic", stub.LastAuthorization!.Scheme);
        Assert.Equal(2, outcome.Results.Count);
        Assert.Equal("2160p", outcome.Results[0].ParsedFile!.Resolution);
        Assert.Equal(4200000000L, outcome.Results[1].Size);
        Assert.Equal("Referer", Assert.Single(outcome.Results[1].RequestHeaders!).Key);
        Assert.Equal("Comet: Timed out", Assert.Single(outcome.Errors));
    }

    [Fact]
    public async Task Oversized_bodies_are_refused()
    {
        var body = $$"""{ "success": true, "data": { "results": [], "errors": [], "pad": "{{new string('x', 4096)}}" } }""";
        var client = new AioStreamsClient(new FakeHttpClientFactory(new StubHttpHandler(_ => StubHttpHandler.Json(body))), NullLogger<AioStreamsClient>.Instance, maxBodyBytes: 1024);
        var unsized = new AioStreamsClient(new FakeHttpClientFactory(new StubHttpHandler(_ => new HttpResponseMessage { Content = new StreamContent(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body))) })), NullLogger<AioStreamsClient>.Instance, maxBodyBytes: 1024);

        var sized = await Assert.ThrowsAsync<AioStreamsException>(() => client.SearchAsync(Creds(), "movie", "tt1", CancellationToken.None));
        var streamed = await Assert.ThrowsAsync<AioStreamsException>(() => unsized.SearchAsync(Creds(), "movie", "tt1", CancellationToken.None));

        Assert.Equal("AIOStreams returned a response larger than 16 MB.", sized.Message);
        Assert.Equal(sized.Message, streamed.Message);
        Assert.Equal(16 * 1024 * 1024, AioStreamsClient.MaxBodyBytes);
    }

    [Fact]
    public async Task Failed_envelope_throws_with_server_message()
    {
        var (client, _) = Create(_ => StubHttpHandler.Json(Fixture.Read("aiostreams/search-failed.json"), HttpStatusCode.BadRequest));

        var ex = await Assert.ThrowsAsync<AioStreamsException>(() => client.SearchAsync(Creds(), "movie", "tt1", CancellationToken.None));

        Assert.Contains("Invalid password", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Addon_errors_are_masked_before_logging_and_returning()
    {
        const string Body = """
            { "success": true, "data": { "results": [], "errors": [ { "addon": "Torrentio", "description": "GET https://t.example.com/resolve/realdebrid/SECRETKEY/abc/null/0/f.mkv failed" } ] } }
            """;
        var logger = new ListLogger<AioStreamsClient>();
        var client = new AioStreamsClient(new FakeHttpClientFactory(new StubHttpHandler(_ => StubHttpHandler.Json(Body))), logger);

        var outcome = await client.SearchAsync(Creds(), "movie", "tt1", CancellationToken.None);

        Assert.DoesNotContain("SECRETKEY", Assert.Single(outcome.Errors), StringComparison.Ordinal);
        Assert.NotEmpty(logger.Entries);
        Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("SECRETKEY", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Server_error_message_is_masked_in_the_exception()
    {
        const string Body = """
            { "success": false, "data": null, "error": { "code": "X", "message": "Upstream https://aio.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/SECRETPW/manifest.json failed" } }
            """;
        var (client, _) = Create(_ => StubHttpHandler.Json(Body, HttpStatusCode.BadRequest));

        var ex = await Assert.ThrowsAsync<AioStreamsException>(() => client.SearchAsync(Creds(), "movie", "tt1", CancellationToken.None));

        Assert.StartsWith("AIOStreams: Upstream", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRETPW", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("0b6c3c7e", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unauthorized_without_body_throws_clear_message()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var ex = await Assert.ThrowsAsync<AioStreamsException>(() => client.SearchAsync(Creds(), "movie", "tt1", CancellationToken.None));

        Assert.Contains("credentials", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Parses_duration_bitrate_and_file_details()
    {
        var (client, _) = Create(_ => StubHttpHandler.Json(Fixture.Read("aiostreams/search-ok.json")));

        var first = (await client.SearchAsync(Creds(), "movie", "tt0111161", CancellationToken.None)).Results[0];

        Assert.Equal(8473120.5, first.Duration!.Value);
        Assert.Equal(17372000d, first.Bitrate!.Value);
        Assert.Equal(new[] { "7.1" }, first.ParsedFile!.AudioChannels);
        Assert.Equal(new[] { "English", "French" }, first.ParsedFile.Subtitles);
        Assert.Equal("mkv", first.ParsedFile.Container);
        Assert.Null(first.ParsedFile.Extension);
    }

    [Fact]
    public async Task A_utf8_byte_order_mark_is_accepted()
    {
        var json = Fixture.Read("aiostreams/search-ok.json");
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetBytes(json)]),
        });

        var outcome = await client.SearchAsync(Creds(), "movie", "tt0111161", CancellationToken.None);

        Assert.Equal(2, outcome.Results.Count);
    }

    [Fact]
    public async Task Track_lists_and_stream_subtitles_are_read()
    {
        var (client, _) = Create(_ => StubHttpHandler.Json(Fixture.Read("aiostreams/search-tracks.json")));

        var result = Assert.Single((await client.SearchAsync(Creds(), "series", "tt1:1:1", CancellationToken.None)).Results);

        Assert.Equal("probe", result.ParsedFile!.MediaInfoQuality);
        Assert.Equal(new[] { "eng", "fra" }, result.ParsedFile.AudioTracks!.Select(t => t.Lang));
        Assert.Equal(new[] { "5.1", "2" }, result.ParsedFile.AudioTracks!.Select(t => t.Channels));
        Assert.True(result.ParsedFile.AudioTracks![0].Default);
        Assert.True(result.ParsedFile.SubtitleTracks![0].Forced);
        Assert.Equal(2, result.Subtitles!.Count);
        Assert.Equal("https://subs.example.com/file/123", result.Subtitles[0].Url);
    }

    [Fact]
    public async Task Subtitles_use_the_path_authenticated_stremio_route()
    {
        var (client, stub) = Create(_ => StubHttpHandler.Json("""{ "subtitles": [ { "id": "1", "url": "https://subs.example.com/1", "lang": "eng" } ] }"""));

        var subtitles = await client.SubtitlesAsync(Creds(), "series", "tt1:1:2", CancellationToken.None);

        var uri = stub.Requests[0].AbsoluteUri;
        Assert.StartsWith("https://aio.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/pw/subtitles/series/tt1", uri, StringComparison.Ordinal);
        Assert.EndsWith("2.json", uri, StringComparison.Ordinal);
        Assert.Null(stub.LastAuthorization);
        Assert.Equal("https://subs.example.com/1", Assert.Single(subtitles).Url);
    }

    [Fact]
    public async Task Subtitle_errors_hide_the_password_and_missing_lists_are_empty()
    {
        var (failing, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var (missing, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsAsync<AioStreamsException>(() => failing.SubtitlesAsync(Creds(), "movie", "tt1", CancellationToken.None));

        Assert.DoesNotContain("/pw/", ex.Message, StringComparison.Ordinal);
        Assert.Empty(await missing.SubtitlesAsync(Creds(), "movie", "tt1", CancellationToken.None));
    }
}
