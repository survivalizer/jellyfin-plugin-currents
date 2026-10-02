using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.RateLimiting;
using Jellyfin.Plugin.Currents.Common;

namespace Jellyfin.Plugin.Currents.Clients.Http;

/// <summary>Holds one rate limiter per upstream client and one circuit breaker per (client, host[:port]), shared by all handler instances.</summary>
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
        return new ResilientHttpHandler(policy.Limiter, uri => Breaker(policy, uri), attemptTimeout: AttemptTimeout(clientName));
    }

    /// <summary>Gets the breaker for one upstream host, so a dead self-hosted AIOStreams does not pause calls to other hosts.</summary>
    /// <param name="clientName">The HttpClient name.</param>
    /// <param name="uri">A request URI on that host.</param>
    /// <returns>The shared breaker for (client, host[:port]).</returns>
    internal CircuitBreaker BreakerFor(string clientName, Uri? uri) => Breaker(_policies.GetOrAdd(clientName, Create), uri);

    /// <summary>Gets the per-attempt timeout for a client, or null when it has none.</summary>
    /// <param name="clientName">The HttpClient name.</param>
    /// <returns>The timeout, or null.</returns>
    public static TimeSpan? AttemptTimeout(string clientName) => clientName switch
    {
        HttpClientNames.AioStreams => TimeSpan.FromSeconds(10),
        HttpClientNames.AioMetadata => TimeSpan.FromSeconds(30),
        _ => null,
    };

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
        return new Policy(limiter, new ConcurrentDictionary<string, CircuitBreaker>(StringComparer.OrdinalIgnoreCase));
    }

    private CircuitBreaker Breaker(Policy policy, Uri? uri) =>
        policy.Breakers.GetOrAdd(uri is { IsAbsoluteUri: true } ? uri.Authority : string.Empty, _ => new CircuitBreaker(5, TimeSpan.FromSeconds(30), _time));

    private sealed record Policy(RateLimiter Limiter, ConcurrentDictionary<string, CircuitBreaker> Breakers);
}
