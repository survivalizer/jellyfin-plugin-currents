using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Common;

public class TtlCacheTests
{
    [Fact]
    public void Returns_value_until_it_expires()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var cache = new TtlCache<string, int>(time);
        cache.Set("a", 1, TimeSpan.FromMinutes(5));

        Assert.True(cache.TryGet("a", out var hit));
        Assert.Equal(1, hit);

        time.Advance(TimeSpan.FromMinutes(5));
        Assert.False(cache.TryGet("a", out _));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Remove_and_clear_drop_entries()
    {
        var cache = new TtlCache<string, int>(new ManualTimeProvider(DateTimeOffset.UnixEpoch));
        cache.Set("a", 1, TimeSpan.FromMinutes(1));
        cache.Set("b", 2, TimeSpan.FromMinutes(1));

        cache.Remove("a");
        Assert.False(cache.TryGet("a", out _));

        cache.Clear();
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Above_the_threshold_trims_at_most_once_a_minute()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var cache = new TtlCache<int, int>(time);
        for (var i = 0; i < 5000; i++)
        {
            cache.Set(i, i, TimeSpan.FromHours(1));
        }

        for (var i = 5000; i < 5100; i++)
        {
            cache.Set(i, i, TimeSpan.FromHours(1));
        }

        Assert.Equal(1, cache.TrimCount);

        time.Advance(TimeSpan.FromMinutes(1));
        cache.Set(-1, -1, TimeSpan.FromHours(1));
        Assert.Equal(2, cache.TrimCount);
    }

    [Fact]
    public void A_trim_drops_expired_entries()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var cache = new TtlCache<int, int>(time);
        for (var i = 0; i < 5000; i++)
        {
            cache.Set(i, i, TimeSpan.FromSeconds(30));
        }

        time.Advance(TimeSpan.FromMinutes(1));
        cache.Set(-1, -1, TimeSpan.FromHours(1));

        Assert.Equal(1, cache.Count);
    }
}
