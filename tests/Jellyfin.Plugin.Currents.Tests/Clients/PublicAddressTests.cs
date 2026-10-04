using System.Net;
using Jellyfin.Plugin.Currents.Clients.Http;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Clients;

public class PublicAddressTests
{
    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("10.1.2.3")]
    [InlineData("100.64.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.254")]
    [InlineData("169.254.169.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.0.0.8")]
    [InlineData("192.0.2.1")]
    [InlineData("192.168.1.10")]
    [InlineData("198.18.0.1")]
    [InlineData("198.51.100.7")]
    [InlineData("203.0.113.9")]
    [InlineData("224.0.0.251")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("64:ff9b::a00:1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2002:a00:1::1")]
    [InlineData("64:ff9b::7f00:1")]
    [InlineData("64:ff9b:1::1")]
    [InlineData("2002:c0a8:101::1")]
    [InlineData("2002:101:101::1")]
    [InlineData("fec0::1")]
    [InlineData("::a00:1")]
    public void Private_reserved_and_local_addresses_are_not_public(string address) =>
        Assert.False(PublicAddress.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("64:ff9b::101:101", true)]
    [InlineData("64:ff9b::808:808", true)]
    [InlineData("64:ff9b::a00:1", false)]
    [InlineData("64:ff9b::a9fe:a9fe", false)]
    public void Nat64_is_judged_by_the_embedded_ipv4(string address, bool expected) =>
        Assert.Equal(expected, PublicAddress.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("::ffff:1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    public void Internet_addresses_are_public(string address) =>
        Assert.True(PublicAddress.IsPublic(IPAddress.Parse(address)));
}
