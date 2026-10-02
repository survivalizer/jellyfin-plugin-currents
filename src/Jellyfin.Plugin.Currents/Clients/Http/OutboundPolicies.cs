using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.RateLimiting;
using Jellyfin.Plugin.Currents.Common;

namespace Jellyfin.Plugin.Currents.Clients.Http;

/// <summary>Holds one rate limiter and circuit breaker per upstream, shared by all handler instances.</summary>
public sealed class OutboundPolicies : IDisposable
{
    private readonly ConcurrentDictionary<string, Policy> _policies = new(StringComparer.Ordinal);
    private readonly ICurrentsSettings _settings;
    private readonly TimeProvider _time;

    public OutboundPolicies(ICurrentsSettings settings, TimeProvider time)
    {
        _settings = settings;
        _time = time;
    }

    public DelegatingHandler CreateHandler(string clientName)
    {
        var policy = _policies.GetOrAdd(clientName, Create);
        return new ResilientHttpHandler(policy.Limiter, policy.Breaker);
    }

    public void Dispose()
    {
        foreach (var policy in _policies.Values)
        {
            policy.Limiter.Dispose();
        }

        _policies.Clear();
    }

    [SuppressMessage("Reliability", "CA2000", Justification = "Owned by OutboundPolicies and disposed in Dispose().")]
    private Policy Create(string clientName)
    {
        var config = _settings.Current;
        var (permits, window) = clientName switch
        {
            HttpClientNames.AioStreams => (config.AioStreamsPermitsPer10Seconds, TimeSpan.FromSeconds(10)),
            HttpClientNames.AioMetadata => (config.AioMetadataPermitsPer5Seconds, TimeSpan.FromSeconds(5)),
            _ => (1000, TimeSpan.FromSeconds(1)),
        };

        var limiter = new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, permits),
            Window = window,
            SegmentsPerWindow = 5,
            QueueLimit = 1000,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });
        return new Policy(limiter, new CircuitBreaker(5, TimeSpan.FromSeconds(30), _time));
    }

    private sealed record Policy(RateLimiter Limiter, CircuitBreaker Breaker);
}
