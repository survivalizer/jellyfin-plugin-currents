using Jellyfin.Plugin.Currents.Clients.AioStreams.Models;
using Jellyfin.Plugin.Currents.Streams;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Streams;

public class StreamRankerTests
{
    [Fact]
    public void Keeps_order_and_drops_unplayable_entries()
    {
        StreamResult[] results =
        [
            new() { Url = "https://a.example.com/1.mkv", Filename = "first" },
            new() { Url = "https://a.example.com/err", Type = "error" },
            new() { Url = null, Filename = "torrent-only" },
            new() { Url = "magnet:?xt=urn:btih:abc", Filename = "magnet" },
            new() { Url = "https://a.example.com/stat", Type = "statistic" },
            new() { Url = "http://b.example.com/2.mkv", Filename = "second" },
        ];

        var ranked = StreamRanker.Rank(results);

        Assert.Equal(new[] { "first", "second" }, ranked.Select(r => r.Filename));
    }
}
