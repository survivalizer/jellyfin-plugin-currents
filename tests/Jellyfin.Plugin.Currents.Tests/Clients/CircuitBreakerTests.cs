using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Clients;

public class CircuitBreakerTests
{
    [Fact]
    public void Opens_after_threshold_and_closes_after_the_open_period()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var breaker = new CircuitBreaker(3, TimeSpan.FromSeconds(30), time);

        breaker.RecordFailure();
        breaker.RecordFailure();
        Assert.False(breaker.IsOpen);

        breaker.RecordFailure();
        Assert.True(breaker.IsOpen);
        Assert.Throws<CircuitOpenException>(breaker.ThrowIfOpen);

        time.Advance(TimeSpan.FromSeconds(30));
        Assert.False(breaker.IsOpen);
    }

    [Fact]
    public void Success_resets_the_failure_count()
    {
        var breaker = new CircuitBreaker(2, TimeSpan.FromSeconds(30), new ManualTimeProvider(DateTimeOffset.UnixEpoch));

        breaker.RecordFailure();
        breaker.RecordSuccess();
        breaker.RecordFailure();

        Assert.False(breaker.IsOpen);
    }
}
