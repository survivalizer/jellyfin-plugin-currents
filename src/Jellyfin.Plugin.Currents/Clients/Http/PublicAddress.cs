using System.Net;

namespace Jellyfin.Plugin.Currents.Clients.Http;

/// <summary>Tells internet addresses from loopback, private, link-local (cloud metadata), shared, documentation, multicast and reserved ones.</summary>
public static class PublicAddress
{
    // NAT64 (RFC 6052) and 6to4 (RFC 3056) carry an IPv4 address; they are judged by it below, not refused as a whole.
    private static readonly IPNetwork Nat64 = IPNetwork.Parse("64:ff9b::/96");
    private static readonly IPNetwork SixToFour = IPNetwork.Parse("2002::/16");

    private static readonly IPNetwork[] NotPublic =
    [
        .. new[]
        {
            "0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12", "192.0.0.0/24",
            "192.0.2.0/24", "192.168.0.0/16", "198.18.0.0/15", "198.51.100.0/24", "203.0.113.0/24", "224.0.0.0/4", "240.0.0.0/4",
            "::/96", "::1/128", "64:ff9b:1::/48", "100::/64", "2001::/32", "2001:db8::/32", "fc00::/7", "fe80::/10", "fec0::/10", "ff00::/8",
        }.Select(IPNetwork.Parse),
    ];

    /// <summary>
    /// Gets whether <paramref name="address"/> is a public internet address. IPv4-mapped IPv6 addresses are judged as
    /// IPv4, and so are the IPv4 addresses inside NAT64 (64:ff9b::/96) and 6to4 (2002::/16) addresses.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>True when public.</returns>
    public static bool IsPublic(IPAddress address)
    {
        var normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (Embedded(normalized) is { } inner)
        {
            return IsPublic(inner);
        }

        return !Array.Exists(NotPublic, network => network.Contains(normalized));
    }

    private static IPAddress? Embedded(IPAddress address)
    {
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return null;
        }

        var bytes = address.GetAddressBytes();
        return Nat64.Contains(address) ? new IPAddress(bytes.AsSpan(12, 4))
            : SixToFour.Contains(address) ? new IPAddress(bytes.AsSpan(2, 4))
            : null;
    }
}
