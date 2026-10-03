using System.Globalization;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Common;

public class DiagnosticsLogTests
{
    [Fact]
    public void Keeps_the_newest_fifty_masked_newest_first()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
        var log = new DiagnosticsLog(time);

        for (var i = 0; i < 60; i++)
        {
            log.Record("Sync", i.ToString(CultureInfo.InvariantCulture));
            time.Advance(TimeSpan.FromSeconds(1));
        }

        log.Record("Streams", "failed https://aio.example.com/stremio/0b6c3c7e-1d2f-4a5b-9c8d-7e6f5a4b3c2d/secret-pw/manifest.json");

        var recent = log.Recent();
        Assert.Equal(50, recent.Count);
        Assert.Equal("Streams", recent[0].Area);
        Assert.DoesNotContain("secret-pw", recent[0].Message, StringComparison.Ordinal);
        Assert.Equal("59", recent[1].Message);
        Assert.Equal("11", recent[^1].Message);
    }
}
