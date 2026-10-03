using System.Net;
using System.Net.Sockets;
using System.Text;
using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Configuration;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Clients;

/// <summary>Real loopback sockets: the guard sits where the socket connects, which the HTTP fakes cannot reach.</summary>
public sealed class PublicOnlyConnectorTests : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public PublicOnlyConnectorTests() => _listener.Start();

    private int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Dispose() => _listener.Stop();

    private static HttpClient Client(params (string Host, int Port)[] trusted) =>
        new(new SocketsHttpHandler { UseProxy = false, ConnectCallback = PublicOnlyConnector.Create(() => trusted) }) { Timeout = TimeSpan.FromSeconds(5) };

    // Answers one request with 200 "ok", or redirects it.
    private async Task ServeAsync(string? redirectTo = null)
    {
        using var client = await _listener.AcceptTcpClientAsync();
        var stream = client.GetStream();
        var buffer = new byte[4096];
        var read = 0;
        while (!Encoding.ASCII.GetString(buffer, 0, read).Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            read += await stream.ReadAsync(buffer.AsMemory(read));
        }

        var response = redirectTo is null
            ? "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"
            : $"HTTP/1.1 302 Found\r\nLocation: {redirectTo}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(response));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    public async Task Private_addresses_are_refused_before_connecting(string host)
    {
        using var client = Client();

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(new Uri($"http://{host}:{Port}/p.jpg")));

        // SocketsHttpHandler may wrap the callback's exception and append "(host:port)"; the guard's own message names nothing.
        var guard = error.InnerException as HttpRequestException ?? error;
        Assert.Equal("Refused to connect to a non-public address.", guard.Message);
        Assert.False(_listener.Pending());
    }

    [Fact]
    public async Task Admin_hosts_may_be_private()
    {
        using var client = Client(("127.0.0.1", Port));
        var server = ServeAsync();

        using var response = await client.GetAsync(new Uri($"http://127.0.0.1:{Port}/sub.srt"));
        await server;

        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_admin_host_is_trusted_only_on_its_own_port()
    {
        using var client = Client(("127.0.0.1", Port + 1));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(new Uri($"http://127.0.0.1:{Port}/p.jpg")));

        var guard = error.InnerException as HttpRequestException ?? error;
        Assert.Equal("Refused to connect to a non-public address.", guard.Message);
        Assert.False(_listener.Pending());
    }

    [Fact]
    public async Task A_redirect_to_a_private_address_is_refused()
    {
        // localhost is trusted as the first hop only to stand in for a public host; the redirect target 127.0.0.1 is not.
        using var client = Client(("localhost", Port));
        var server = ServeAsync(redirectTo: $"http://127.0.0.1:{Port}/meta-data");

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(new Uri($"http://localhost:{Port}/p.jpg")));
        await server;

        Assert.False(_listener.Pending());
    }

    [Theory]
    [InlineData("https://aio.lan:3000/stremio/u/p/manifest.json", "https://meta.example.com/stremio/u/manifest.json", "aio.lan:3000,meta.example.com:443")]
    [InlineData("http://[fd00::1]:3000/stremio/u/p/manifest.json", "http://meta.lan/x", "fd00::1:3000,meta.lan:80")]
    [InlineData("", "not a url", "")]
    public void Admin_endpoints_come_from_the_manifest_urls(string streams, string metadata, string expected)
    {
        var config = new PluginConfiguration { AioStreamsManifestUrl = streams, AioMetadataManifestUrl = metadata };

        Assert.Equal(expected, string.Join(",", PublicOnlyConnector.AdminEndpoints(config).Select(e => $"{e.Host}:{e.Port}")));
    }
}
