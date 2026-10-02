using System.Net;
using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests.Clients;

public sealed class OutboundPoliciesTests : IDisposable
{
    private static readonly Uri HostA = new("https://a.example.com/stremio/x/manifest.json");
    private static readonly Uri HostB = new("https://b.example.com/stremio/x/manifest.json");
    private readonly OutboundPolicies _policies = new(new FakeSettings(), new ManualTimeProvider(DateTimeOffset.UnixEpoch));

    public void Dispose() => _policies.Dispose();

    [Fact]
    public void Breakers_are_per_client_and_host()
    {
        var a = _policies.BreakerFor(HttpClientNames.AioStreams, HostA);

        Assert.Same(a, _policies.BreakerFor(HttpClientNames.AioStreams, new Uri("https://A.example.com/other")));
        Assert.NotSame(a, _policies.BreakerFor(HttpClientNames.AioStreams, HostB));
        Assert.NotSame(a, _policies.BreakerFor(HttpClientNames.AioStreams, new Uri("https://a.example.com:8443/x")));
        Assert.NotSame(a, _policies.BreakerFor(HttpClientNames.AioMetadata, HostA));
    }

    [Fact]
    public async Task An_open_breaker_for_one_host_does_not_block_another()
    {
        var stub = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var invoker = new HttpMessageInvoker(Handler(stub));
        var a = _policies.BreakerFor(HttpClientNames.AioStreams, HostA);
        for (var i = 0; i < 5; i++)
        {
            a.RecordFailure();
        }

        using var b = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, HostB), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, b.StatusCode);
        await Assert.ThrowsAsync<CircuitOpenException>(() => invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, HostA), CancellationToken.None));
        Assert.Equal([HostB], stub.Requests);
    }

    private DelegatingHandler Handler(HttpMessageHandler inner)
    {
        var handler = _policies.CreateHandler(HttpClientNames.AioStreams);
        handler.InnerHandler = inner;
        return handler;
    }
}
