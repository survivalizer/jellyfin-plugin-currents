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
}
