using Jellyfin.Plugin.Currents.Streams;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public class StreamHeadersTests
{
    [Fact]
    public void Reserved_and_malformed_headers_are_dropped()
    {
        var clean = StreamHeaders.Sanitize(new Dictionary<string, string>
        {
            ["Authorization"] = "Basic SECRET",
            ["X-Custom"] = " 1 ",
            ["Host"] = "evil.example.com",
            ["Range"] = "bytes=0-",
            ["Accept-Encoding"] = "gzip",
            ["Content-Length"] = "5",
            ["Bad Name"] = "x",
            ["X-Injected"] = "a\r\nX-Other: b",
        });

        Assert.Equal(new[] { "Authorization", "X-Custom" }, clean.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("1", clean["X-Custom"]);
        Assert.Empty(StreamHeaders.Sanitize(null));
    }

    [Theory]
    [InlineData("https://dav.example.com/a", "https://dav.example.com/b", true)]
    [InlineData("https://dav.example.com/a", "https://cdn.example.net/b", false)]
    [InlineData("https://dav.example.com/a", "http://dav.example.com/b", false)]
    [InlineData("https://dav.example.com/a", "https://dav.example.com:8443/b", false)]
    public void Only_harmless_headers_leave_the_origin(string origin, string target, bool sameOrigin)
    {
        var headers = StreamHeaders.Sanitize(new Dictionary<string, string>
        {
            ["Authorization"] = "x",
            ["Cookie"] = "y",
            ["Proxy-Authorization"] = "z",
            ["X-Api-Key"] = "k",
            ["X-Auth-Token"] = "t",
            ["Referer"] = "r",
            ["User-Agent"] = "ua",
            ["Origin"] = "o",
            ["Accept"] = "*/*",
            ["Accept-Language"] = "en",
        });

        var sent = StreamHeaders.For(headers, new Uri(origin), new Uri(target));

        var expected = sameOrigin
            ? headers.Keys
            : new[] { "Accept", "Accept-Language", "Origin", "Referer", "User-Agent" };
        Assert.Equal(expected.Order(StringComparer.Ordinal), sent.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("r", sent["Referer"]);
    }
}
