using System.Threading.RateLimiting;

namespace Jellyfin.Plugin.Currents.Segments;

/// <summary>Keeps one source under its request limit. Callers queue for a permit.</summary>
public sealed class SourcePacer : IDisposable
{
    private readonly RateLimiter _limiter;

    public SourcePacer(int permits, TimeSpan window)
    {
        _limiter = new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = window,
            SegmentsPerWindow = 5,
            QueueLimit = 500,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });
    }

    public async Task WaitAsync(string source, CancellationToken cancellationToken)
    {
        using var lease = await _limiter.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
        if (!lease.IsAcquired)
        {
            throw new SegmentSourceException($"{source}: too many lookups are waiting.");
        }
    }

    public void Dispose() => _limiter.Dispose();
}
