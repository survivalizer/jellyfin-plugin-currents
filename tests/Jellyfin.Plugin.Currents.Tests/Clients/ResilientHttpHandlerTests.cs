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
}
