using System.Net;
using System.Net.Sockets;
using Jellyfin.Plugin.Currents.Configuration;

namespace Jellyfin.Plugin.Currents.Clients.Http;

/// <summary>
/// A <see cref="SocketsHttpHandler.ConnectCallback"/> that dials only public addresses, so upstream data (a poster or
/// subtitle URL, or a redirect it leads to) cannot reach the server's own network. Resolving here and connecting to the
/// checked address also defeats DNS rebinding. The admin's own AIOStreams and AIOMetadata endpoints (exact host and port)
/// may be private (self-hosted).
/// </summary>
public static class PublicOnlyConnector
{
    public static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> Create(Func<IEnumerable<(string Host, int Port)>> trustedEndpoints) =>
        async (context, cancellationToken) =>
        {
            var host = context.DnsEndPoint.Host;

            // Through an HTTP proxy the socket goes to the proxy the admin configured; the proxy decides where it may connect.
            var proxied = context.InitialRequestMessage.RequestUri is { } target
                && !string.Equals(target.IdnHost.Trim('[', ']'), host.Trim('[', ']'), StringComparison.OrdinalIgnoreCase);
            var bareHost = host.Trim('[', ']');
            var port = context.DnsEndPoint.Port;
            var trusted = proxied || trustedEndpoints().Any(endpoint =>
                endpoint.Port == port && string.Equals(endpoint.Host, bareHost, StringComparison.OrdinalIgnoreCase));

            var addresses = await Dns.GetHostAddressesAsync(bareHost, cancellationToken).ConfigureAwait(false);
            var allowed = trusted ? addresses : Array.FindAll(addresses, PublicAddress.IsPublic);
            if (allowed.Length == 0)
            {
                // No host, address or URL in the message: it may reach a log.
                throw new HttpRequestException(HttpRequestError.ConnectionError, "Refused to connect to a non-public address.");
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };

    /// <summary>The hosts and ports of the admin's AIOStreams and AIOMetadata manifest URLs (read per connection, so a saved change applies at once).</summary>
    /// <param name="config">The plugin configuration.</param>
    /// <returns>The host and port pairs; the port is the URL's, or the scheme's default.</returns>
    public static IEnumerable<(string Host, int Port)> AdminEndpoints(PluginConfiguration config)
    {
        foreach (var url in new[] { config.AioStreamsManifestUrl, config.AioMetadataManifestUrl })
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.IdnHost))
            {
                yield return (uri.IdnHost.Trim('[', ']'), uri.Port);
            }
        }
    }
}
