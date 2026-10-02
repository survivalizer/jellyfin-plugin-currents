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
    public async Task Failed_envelope_throws_with_server_message()
    {
        var (client, _) = Create(_ => StubHttpHandler.Json(Fixture.Read("aiostreams/search-failed.json"), HttpStatusCode.BadRequest));

        var ex = await Assert.ThrowsAsync<AioStreamsException>(() => client.SearchAsync(Creds(), "movie", "tt1", CancellationToken.None));

        Assert.Contains("Invalid password", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unauthorized_without_body_throws_clear_message()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var ex = await Assert.ThrowsAsync<AioStreamsException>(() => client.SearchAsync(Creds(), "movie", "tt1", CancellationToken.None));

        Assert.Contains("credentials", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
