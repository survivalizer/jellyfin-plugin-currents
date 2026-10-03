using System.Net;
using Jellyfin.Plugin.Currents.Clients.RemuxDb;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Clients;

public class RemuxDbClientTests
{
    private readonly FakeSettings _settings = new();
    private readonly List<HttpRequestMessage> _sent = [];

    private RemuxDbClient Create(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new FakeHttpClientFactory(new StubHttpHandler(r =>
        {
            _sent.Add(r);
            return respond(r);
        })), _settings);

    [Fact]
    public async Task Requests_the_episode_path_with_a_stable_client_id()
    {
        var client = Create(_ => StubHttpHandler.Json(Fixture.Read("remuxdb/versions-episode.json")));

        var versions = await client.VersionsAsync("tt0903747:1:1", CancellationToken.None);

        var request = Assert.Single(_sent);
        Assert.Equal("https://remuxdb.1632022.xyz/api/media/tt0903747:1:1/versions", request.RequestUri!.AbsoluteUri);
        var id = Assert.Single(request.Headers.GetValues("x-client-id"));
        Assert.Matches("^currents-[0-9a-f]{32}$", id);
        Assert.Equal(id, RemuxDbClient.ClientId(_settings.Current.SigningSecret));
        Assert.NotEqual(id, RemuxDbClient.ClientId(Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray())));
        Assert.Equal(2, versions.Count);
        Assert.Equal("F1C8B4BA4AE611494FFB09958241165AAF1F07DB", versions[0].Sources![0].TorrentInfoHash);
        Assert.Equal(0, versions[0].Sources![0].TorrentFileIdx);
        Assert.Equal("5.1(side)", versions[0].Tracks![1].ChannelLayout);
        Assert.Equal(3480.5, versions[0].Duration);
        Assert.True(versions[0].Tracks![3].IsForced);
    }

    [Fact]
    public async Task Not_found_is_an_empty_list()
    {
        var versions = await Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound)).VersionsAsync("tt1", CancellationToken.None);

        Assert.Empty(versions);
    }

    [Theory]
    [InlineData("<html>maintenance</html>", HttpStatusCode.OK)]
    [InlineData("{}", HttpStatusCode.InternalServerError)]
    public async Task Invalid_bodies_and_errors_throw(string body, HttpStatusCode status)
    {
        var client = Create(_ => StubHttpHandler.Json(body, status));

        await Assert.ThrowsAsync<RemuxDbException>(() => client.VersionsAsync("tt1", CancellationToken.None));
    }

    [Fact]
    public async Task Oversized_bodies_are_refused()
    {
        var huge = "[" + string.Join(',', Enumerable.Repeat("{\"container\":\"" + new string('x', 1000) + "\"}", 9000)) + "]";
        var client = Create(_ => new HttpResponseMessage { Content = new StreamContent(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(huge))) });

        var ex = await Assert.ThrowsAsync<RemuxDbException>(() => client.VersionsAsync("tt1", CancellationToken.None));

        Assert.Contains("8 MB", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tt1/../admin")]
    [InlineData("kitsu:1")]
    [InlineData("tt1:2")]
    public async Task Only_imdb_and_tmdb_title_ids_are_sent(string id)
    {
        var client = Create(_ => StubHttpHandler.Json("[]"));

        await Assert.ThrowsAsync<ArgumentException>(() => client.VersionsAsync(id, CancellationToken.None));
        Assert.Empty(_sent);
    }
}
