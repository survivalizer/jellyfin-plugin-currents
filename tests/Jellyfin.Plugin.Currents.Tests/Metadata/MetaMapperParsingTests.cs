using Jellyfin.Plugin.Currents.Clients.AioMetadata.Models;
using Jellyfin.Plugin.Currents.Metadata;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Metadata;

public class MetaMapperParsingTests
{
    [Theory]
    [InlineData("1994", null, null, 1994)]
    [InlineData(null, "2011-2019", null, 2011)]
    [InlineData(null, "2023–", null, 2023)]
    [InlineData(null, null, "2020-03-01T00:00:00.000Z", 2020)]
    [InlineData(null, "TBA", null, null)]
    public void Parses_year_from_year_releaseinfo_or_released(string? year, string? releaseInfo, string? released, int? expected)
    {
        var meta = new StremioMeta { Year = year, ReleaseInfo = releaseInfo, Released = released };

        Assert.Equal(expected, MetaMapper.ParseYear(meta));
    }

    [Theory]
    [InlineData("139 min", 139)]
    [InlineData("2h 10min", 130)]
    [InlineData("1h", 60)]
    [InlineData("45", 45)]
    [InlineData("unknown", null)]
    [InlineData(null, null)]
    public void Parses_runtime_minutes(string? runtime, int? minutes)
    {
        Assert.Equal(minutes is null ? null : TimeSpan.FromMinutes(minutes.Value).Ticks, MetaMapper.ParseRuntimeTicks(runtime));
    }

    [Fact]
    public void Parses_dates_and_ratings_invariantly()
    {
        Assert.Equal(new DateTime(2011, 4, 17, 0, 0, 0, DateTimeKind.Utc), MetaMapper.ParseDate("2011-04-17T00:00:00.000Z"));
        Assert.Null(MetaMapper.ParseDate("soon"));
        Assert.Equal(9.2f, MetaMapper.ParseRating("9.2"));
        Assert.Null(MetaMapper.ParseRating("n/a"));
    }
}
