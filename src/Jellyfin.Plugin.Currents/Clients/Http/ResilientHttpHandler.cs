using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Threading.RateLimiting;

namespace Jellyfin.Plugin.Currents.Clients.Http;

/// <summary>Rate-limits, retries transient failures (honouring Retry-After) and trips a circuit breaker per upstream host.</summary>
public sealed class ResilientHttpHandler : DelegatingHandler
{
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30);

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Owned and disposed by OutboundPolicies.")]
    private readonly RateLimiter _limiter;
    private readonly Func<Uri?, CircuitBreaker> _breakers;
    private readonly int _maxRetries;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan? _attemptTimeout;

    public ResilientHttpHandler(
        RateLimiter limiter,
        CircuitBreaker breaker,
        int maxRetries = 2,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? attemptTimeout = null)
        : this(limiter, _ => breaker, maxRetries, delay, attemptTimeout)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ResilientHttpHandler"/> class whose breaker is chosen per request URI (per upstream host).</summary>
    /// <param name="limiter">The shared rate limiter.</param>
    /// <param name="breakers">Picks the circuit breaker for a request URI.</param>
    /// <param name="maxRetries">Retries after the first attempt.</param>
    /// <param name="delay">The backoff delay (tests replace it).</param>
    /// <param name="attemptTimeout">The per-attempt timeout, if any.</param>
    public ResilientHttpHandler(
        RateLimiter limiter,
        Func<Uri?, CircuitBreaker> breakers,
        int maxRetries = 2,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? attemptTimeout = null)
    {
        _limiter = limiter;
        _breakers = breakers;
        _maxRetries = maxRetries;
        _delay = delay ?? ((d, ct) => Task.Delay(d, ct));
        _attemptTimeout = attemptTimeout;
    }

    // Notes:
    // Each attempt is bounded by an optional per-attempt timeout; timeouts are retried and counted as breaker failures.
    // After the last retry the original exception is rethrown. Caller cancellation is never retried or counted.
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var breaker = _breakers(request.RequestUri);
        for (var attempt = 0; ; attempt++)
        {
            breaker.ThrowIfOpen();
            HttpResponseMessage? response = null;
            Exception? error = null;
            using (var lease = await _limiter.AcquireAsync(1, cancellationToken).ConfigureAwait(false))
            {
                if (!lease.IsAcquired)
                {
                    throw new HttpRequestException("The outbound request queue is full.");
                }

                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (_attemptTimeout is { } timeout)
                {
                    attemptCts.CancelAfter(timeout);
                }

                try
                {
                    response = await base.SendAsync(request, attemptCts.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsTransient(ex, cancellationToken))
                {
                    error = ex;
                }
            }

            if (error is not null)
            {
                breaker.RecordFailure();
                if (attempt >= _maxRetries)
                {
                    ExceptionDispatchInfo.Capture(error).Throw();
                }

                await _delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                continue;
            }

            var status = (int)response!.StatusCode;
            if (status < 500 && response.StatusCode != HttpStatusCode.TooManyRequests)
            {
                breaker.RecordSuccess();
                return response;
            }

            if (status >= 500)
            {
                breaker.RecordFailure();
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
        || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested);

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
