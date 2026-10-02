using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;
using System.Threading.RateLimiting;

namespace Jellyfin.Plugin.Currents.Clients.Http;

/// <summary>Rate-limits, retries transient failures (honouring Retry-After) and trips a circuit breaker.</summary>
public sealed class ResilientHttpHandler : DelegatingHandler
{
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30);

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Owned and disposed by OutboundPolicies.")]
    private readonly RateLimiter _limiter;
    private readonly CircuitBreaker _breaker;
    private readonly int _maxRetries;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public ResilientHttpHandler(RateLimiter limiter, CircuitBreaker breaker, int maxRetries = 2, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _limiter = limiter;
        _breaker = breaker;
        _maxRetries = maxRetries;
        _delay = delay ?? ((d, ct) => Task.Delay(d, ct));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            _breaker.ThrowIfOpen();
            HttpResponseMessage response;
            using (var lease = await _limiter.AcquireAsync(1, cancellationToken).ConfigureAwait(false))
            {
                if (!lease.IsAcquired)
                {
                    throw new HttpRequestException("The outbound request queue is full.");
                }

                try
                {
                    response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsTransient(ex, cancellationToken))
                {
                    _breaker.RecordFailure();
                    if (attempt >= _maxRetries)
                    {
                        throw;
                    }

                    await _delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }
            }

            var status = (int)response.StatusCode;
            if (status < 500 && response.StatusCode != HttpStatusCode.TooManyRequests)
            {
                _breaker.RecordSuccess();
                return response;
            }

            if (status >= 500)
            {
                _breaker.RecordFailure();
            }

            if (attempt >= _maxRetries)
            {
                return response;
            }

            var wait = RetryAfter(response) ?? Backoff(attempt);
            response.Dispose();
            await _delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsTransient(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException and not CircuitOpenException
        || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        TimeSpan? wait = header?.Delta ?? (header?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        if (wait is null)
        {
            return null;
        }

        return wait.Value < TimeSpan.Zero ? TimeSpan.Zero : (wait.Value > MaxRetryAfter ? MaxRetryAfter : wait.Value);
    }

    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromMilliseconds((200 * Math.Pow(2, attempt)) + RandomNumberGenerator.GetInt32(0, 100));
}
