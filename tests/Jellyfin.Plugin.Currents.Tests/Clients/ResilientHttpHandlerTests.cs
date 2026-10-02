using System.Net;
using System.Threading.RateLimiting;
using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Clients;

public class ResilientHttpHandlerTests
{
    private static readonly Uri Target = new("https://example.com/x");
    private readonly List<TimeSpan> _delays = [];

    private (HttpMessageInvoker Invoker, StubHttpHandler Stub, CircuitBreaker Breaker) Create(Func<int, HttpResponseMessage> respond, int threshold = 100)
    {
        var calls = 0;
        var stub = new StubHttpHandler(_ => respond(calls++));
        var breaker = new CircuitBreaker(threshold, TimeSpan.FromSeconds(30), new ManualTimeProvider(DateTimeOffset.UnixEpoch));
        var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions { PermitLimit = 10, QueueLimit = 10 });
        var handler = new ResilientHttpHandler(limiter, breaker, maxRetries: 2, delay: (d, _) => { _delays.Add(d); return Task.CompletedTask; })
        {
            InnerHandler = stub,
        };
        return (new HttpMessageInvoker(handler), stub, breaker);
    }

    private (HttpMessageInvoker Invoker, CircuitBreaker Breaker) CreateWith(HttpMessageHandler inner, int threshold = 100, TimeSpan? attemptTimeout = null)
    {
        var breaker = new CircuitBreaker(threshold, TimeSpan.FromSeconds(30), new ManualTimeProvider(DateTimeOffset.UnixEpoch));
        var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions { PermitLimit = 10, QueueLimit = 10 });
        var handler = new ResilientHttpHandler(limiter, breaker, maxRetries: 2, delay: (d, _) => { _delays.Add(d); return Task.CompletedTask; }, attemptTimeout: attemptTimeout)
        {
            InnerHandler = inner,
        };
        return (new HttpMessageInvoker(handler), breaker);
    }

    [Fact]
    public async Task Retries_server_errors_then_succeeds()
    {
        var (invoker, stub, _) = Create(call => new HttpResponseMessage(call < 2 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));

        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, Target), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, stub.Requests.Count);
        Assert.Equal(2, _delays.Count);
    }

    [Fact]
    public async Task Honors_retry_after_on_429()
    {
        var (invoker, _, _) = Create(call =>
        {
            if (call > 0)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
            return limited;
        });

        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, Target), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(2), Assert.Single(_delays));
    }

    [Fact]
    public async Task Returns_last_failure_after_max_retries()
    {
        var (invoker, stub, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, Target), CancellationToken.None);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(3, stub.Requests.Count);
    }

    [Fact]
    public async Task Does_not_retry_client_errors()
    {
        var (invoker, stub, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, Target), CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task Open_circuit_fails_fast_without_calling_the_server()
    {
        var (invoker, stub, breaker) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK), threshold: 1);
        breaker.RecordFailure();

        await Assert.ThrowsAsync<CircuitOpenException>(() => invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, Target), CancellationToken.None));
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Retries_transport_exceptions_then_succeeds()
    {
        var inner = new FuncHandler((n, _) => n < 2
            ? throw new HttpRequestException("boom")
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var (invoker, _) = CreateWith(inner);

        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, Target), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, inner.Calls);
        Assert.Equal(2, _delays.Count);
    }

    [Fact]
    public async Task Rethrows_transport_exception_after_max_retries_and_records_failures()
    {
        var inner = new FuncHandler((_, _) => throw new HttpRequestException("boom"));
        var (invoker, breaker) = CreateWith(inner, threshold: 3);

        await Assert.ThrowsAsync<HttpRequestException>(() => invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, Target), CancellationToken.None));

        Assert.Equal(3, inner.Calls);
        Assert.True(breaker.IsOpen);
    }

    [Fact]
    public async Task Caller_cancellation_during_send_is_not_retried_or_counted()
    {
        using var cts = new CancellationTokenSource();
        var inner = new FuncHandler(async (_, ct) =>
        {
            await cts.CancelAsync();
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var (invoker, breaker) = CreateWith(inner, threshold: 1);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, Target), cts.Token));

        Assert.Equal(1, inner.Calls);
        Assert.Empty(_delays);
        Assert.False(breaker.IsOpen);
    }

    [Fact]
    public async Task Pre_cancelled_caller_does_not_call_the_server_or_count()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var inner = new FuncHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var (invoker, breaker) = CreateWith(inner, threshold: 1);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, Target), cts.Token));

        Assert.Equal(0, inner.Calls);
        Assert.False(breaker.IsOpen);
    }

    [Fact]
    public async Task Repeated_429_does_not_open_the_breaker()
    {
        var (invoker, stub, breaker) = Create(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests), threshold: 1);

        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, Target), CancellationToken.None);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(3, stub.Requests.Count);
        Assert.False(breaker.IsOpen);
    }

    [Fact]
    public async Task Attempt_timeout_is_retried()
    {
        var inner = new FuncHandler(async (n, ct) =>
        {
            if (n == 0)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var (invoker, _) = CreateWith(inner, attemptTimeout: TimeSpan.FromMilliseconds(50));

        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, Target), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
        Assert.Single(_delays);
    }

    [Fact]
    public async Task Attempt_timeout_counts_as_a_breaker_failure()
    {
        var inner = new FuncHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var (invoker, breaker) = CreateWith(inner, threshold: 1, attemptTimeout: TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<CircuitOpenException>(() => invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, Target), CancellationToken.None));

        Assert.Equal(1, inner.Calls);
        Assert.True(breaker.IsOpen);
    }

    [Fact]
    public async Task Task_canceled_with_timeout_inner_exception_is_retried()
    {
        var inner = new FuncHandler((n, _) => n == 0
            ? throw new TaskCanceledException("timeout", new TimeoutException())
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var (invoker, _) = CreateWith(inner);

        using var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, Target), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Failures_open_only_the_failing_hosts_breaker()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var breakers = new Dictionary<string, CircuitBreaker>(StringComparer.Ordinal);
        var stub = new StubHttpHandler(r => new HttpResponseMessage(r.RequestUri!.Host == "a.example.com" ? HttpStatusCode.BadGateway : HttpStatusCode.OK));
        var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions { PermitLimit = 10, QueueLimit = 10 });
        var handler = new ResilientHttpHandler(
            limiter,
            uri => breakers.TryGetValue(uri!.Authority, out var b) ? b : breakers[uri.Authority] = new CircuitBreaker(3, TimeSpan.FromSeconds(30), time),
            maxRetries: 2,
            delay: (d, _) => { _delays.Add(d); return Task.CompletedTask; })
        {
            InnerHandler = stub,
        };
        using var invoker = new HttpMessageInvoker(handler);

        using var failed = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri("https://a.example.com/x")), CancellationToken.None);
        using var other = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri("https://b.example.com/x")), CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        await Assert.ThrowsAsync<CircuitOpenException>(() => invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri("https://a.example.com/y")), CancellationToken.None));
    }

    private sealed class FuncHandler : HttpMessageHandler
    {
        private readonly Func<int, CancellationToken, Task<HttpResponseMessage>> _send;

        public FuncHandler(Func<int, CancellationToken, Task<HttpResponseMessage>> send) => _send = send;

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _send(Calls++, cancellationToken);
    }
}
