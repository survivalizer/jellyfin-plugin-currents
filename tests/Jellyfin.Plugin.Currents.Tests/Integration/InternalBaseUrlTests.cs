using System.Net;
using Jellyfin.Plugin.Currents.Integration;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Integration;

public class InternalBaseUrlTests
{
    [Fact]
    public void Wildcard_bind_uses_ipv4_loopback_and_base_path() =>
        Assert.Equal("http://127.0.0.1:8096/jellyfin", InternalBaseUrl.Choose([IPAddress.IPv6Any], true, false, false, 8096, 8920, "/jellyfin"));

    [Fact]
    public void Ipv6_only_uses_ipv6_loopback() =>
        Assert.Equal("http://[::1]:8096", InternalBaseUrl.Choose([IPAddress.IPv6Any], false, false, false, 8096, 8920, string.Empty));

    [Fact]
    public void Required_https_goes_straight_to_the_https_port() =>
        Assert.Equal("https://127.0.0.1:8920", InternalBaseUrl.Choose([IPAddress.Any], true, true, true, 8096, 8920, string.Empty));

    [Fact]
    public void Https_not_served_by_jellyfin_stays_on_http() =>
        Assert.Equal("http://127.0.0.1:8096", InternalBaseUrl.Choose([IPAddress.Any], true, true, false, 8096, 8920, string.Empty));

    [Fact]
    public void Explicit_lan_bind_without_loopback_returns_null_for_the_fallback() =>
        Assert.Null(InternalBaseUrl.Choose([IPAddress.Parse("192.168.1.20")], true, false, false, 8096, 8920, string.Empty));
}
