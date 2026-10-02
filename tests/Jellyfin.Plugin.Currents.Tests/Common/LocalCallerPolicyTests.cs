using System.Net;
using Jellyfin.Plugin.Currents.Common;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Common;

public class LocalCallerPolicyTests
{
    private static readonly LocalCallerPolicy Policy = new(() => [IPAddress.Parse("192.168.1.20"), IPAddress.Parse("fd00::20")]);

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.5")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("192.168.1.20")]
    [InlineData("::ffff:192.168.1.20")]
    [InlineData("fd00::20")]
    public void Loopback_and_own_addresses_are_local(string address) =>
        Assert.True(Policy.IsLocal(IPAddress.Parse(address)));

    [Theory]
    [InlineData("192.168.1.21")]
    [InlineData("172.18.0.1")]
    [InlineData("8.8.8.8")]
    public void Other_addresses_are_not_local(string address) =>
        Assert.False(Policy.IsLocal(IPAddress.Parse(address)));

    [Fact]
    public void Unknown_caller_is_not_local() => Assert.False(Policy.IsLocal(null));
}
