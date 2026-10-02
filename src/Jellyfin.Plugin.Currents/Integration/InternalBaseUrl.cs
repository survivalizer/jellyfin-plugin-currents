using System.Net;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>Loopback URL for version paths, so streams never depend on a client-facing address (research Q7).</summary>
public sealed class InternalBaseUrl : IInternalBaseUrl
{
    private readonly Lazy<string> _value;

    public InternalBaseUrl(IServerApplicationHost host, INetworkManager network, IServerConfigurationManager config)
    {
        _value = new Lazy<string>(() =>
        {
            var net = config.GetNetworkConfiguration();
            var binds = network.GetAllBindInterfaces(false).Select(b => b.Address).ToList();
            return Choose(binds, network.IsIPv4Enabled, net.RequireHttps, host.ListenWithHttps, host.HttpPort, host.HttpsPort, net.BaseUrl)
                ?? host.GetApiUrlForLocalAccess(null, allowHttps: false).TrimEnd('/');
        });
    }

    public string Value => _value.Value;

    internal static string? Choose(IReadOnlyCollection<IPAddress> binds, bool ipv4Enabled, bool requireHttps, bool listenWithHttps, int httpPort, int httpsPort, string basePath)
    {
        string? host = null;
        if (ipv4Enabled && binds.Any(a => a.Equals(IPAddress.Any) || a.Equals(IPAddress.IPv6Any) || IPAddress.IsLoopback(a)))
        {
            host = "127.0.0.1";
        }
        else if (binds.Any(a => a.Equals(IPAddress.IPv6Any) || a.Equals(IPAddress.IPv6Loopback)))
        {
            host = "[::1]";
        }

        if (host is null)
        {
            return null;
        }

        // Kestrel always serves HTTP; with RequireHttps it redirects, so go straight to HTTPS.
        return requireHttps && listenWithHttps
            ? $"https://{host}:{httpsPort}{basePath}"
            : $"http://{host}:{httpPort}{basePath}";
    }
}
