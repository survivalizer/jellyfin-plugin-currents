using System.Net;
using MediaBrowser.Common.Net;

namespace Jellyfin.Plugin.Currents.Integration;

/// <summary>The addresses of the network interfaces Jellyfin is bound to.</summary>
public static class ServerAddresses
{
    /// <summary>Gets the addresses of the server's bound interfaces.</summary>
    /// <param name="network">The Jellyfin network manager.</param>
    /// <returns>The interface addresses.</returns>
    public static IEnumerable<IPAddress> Of(INetworkManager network) =>
        network.GetAllBindInterfaces(individualInterfaces: true).Select(i => i.Address);
}
