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
    public void Credentials_follow_only_to_the_same_origin(string origin, string target, bool kept)
    {
        var headers = StreamHeaders.Sanitize(new Dictionary<string, string> { ["Authorization"] = "x", ["Cookie"] = "y", ["Proxy-Authorization"] = "z", ["Referer"] = "r" });

        var sent = StreamHeaders.For(headers, new Uri(origin), new Uri(target));

        Assert.Equal(kept ? 4 : 1, sent.Count);
        Assert.Equal("r", sent["Referer"]);
    }
}
