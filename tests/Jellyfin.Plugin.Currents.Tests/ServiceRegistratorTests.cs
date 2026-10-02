using System.Net;
using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests;

public class ServiceRegistratorTests
{
    internal static ServiceCollection Register(CapturingLoggerProvider? logs = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(logs ?? new CapturingLoggerProvider()).SetMinimumLevel(LogLevel.Trace));

        // Jellyfin registers IMediaSourceManager before plugins; the Currents decorator (Task 14) wraps it.
        services.AddSingleton<IMediaSourceManager>(_ => throw new NotSupportedException("Jellyfin's media source manager is not available in unit tests."));
        new ServiceRegistrator().RegisterServices(services, null!);
        services.AddSingleton<ICurrentsSettings>(new FakeSettings());
        return services;
    }

    [Theory]
    [InlineData(HttpClientNames.Resolve)]
    [InlineData(HttpClientNames.AioStreams)]
    [InlineData(HttpClientNames.AioMetadata)]
    public async Task Outbound_clients_never_log_request_urls(string name)
    {
        var logs = new CapturingLoggerProvider();
        var services = Register(logs);
        services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        await using var provider = services.BuildServiceProvider();

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(name);
        using var response = await client.GetAsync(new Uri("https://debrid.example.com/resolve/realdebrid/SECRETKEY/x.mkv"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(logs.Messages, m => m.Contains("SECRETKEY", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Categories, c => c.StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal));
    }
}
