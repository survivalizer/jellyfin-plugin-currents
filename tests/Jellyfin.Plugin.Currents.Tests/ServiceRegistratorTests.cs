using System.Net;
using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Search;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Jellyfin.Plugin.Currents.Tests;

public class ServiceRegistratorTests
{
    internal static ServiceCollection Register(CapturingLoggerProvider? logs = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(logs ?? new CapturingLoggerProvider()).SetMinimumLevel(LogLevel.Trace));

        // Jellyfin registers IMediaSourceManager before plugins; the Currents decorator (Task 14) wraps it.
        services.AddSingleton<IMediaSourceManager>(InterfaceFake.Create<IDisposableMediaSourceManager>().Instance);
        services.AddSingleton(InterfaceFake.Create<IUserManager>().Instance);
        services.AddSingleton(InterfaceFake.Create<ILibraryManager>().Instance);
        services.AddSingleton(InterfaceFake.Create<IServerApplicationHost>().Instance);
        services.AddSingleton(InterfaceFake.Create<INetworkManager>().Instance);
        services.AddSingleton(InterfaceFake.Create<IServerConfigurationManager>().Instance);
        services.AddSingleton(InterfaceFake.Create<IProviderManager>().Instance);
        services.AddSingleton(InterfaceFake.Create<ILibraryMonitor>().Instance);
        services.AddSingleton(InterfaceFake.Create<IFileSystem>().Instance);
        new ServiceRegistrator().RegisterServices(services, null!);
        services.AddSingleton<ICurrentsSettings>(new FakeSettings());
        return services;
    }

    [Theory]
    [InlineData(HttpClientNames.Resolve)]
    [InlineData(HttpClientNames.AioStreams)]
    [InlineData(HttpClientNames.AioMetadata)]
    [InlineData(HttpClientNames.Posters)]
    [InlineData(HttpClientNames.RemuxDb)]
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

    [Fact]
    public async Task Media_source_manager_is_decorated()
    {
        await using var provider = Register().BuildServiceProvider();

        Assert.IsType<CurrentsMediaSourceManager>(provider.GetRequiredService<IMediaSourceManager>());
    }

    [Fact]
    public async Task Filters_are_registered_in_order()
    {
        await using var provider = Register().BuildServiceProvider();

        var filters = provider.GetRequiredService<IOptions<MvcOptions>>().Value.Filters.OfType<ServiceFilterAttribute>().ToList();

        Assert.Equal(typeof(SyntheticVersionIdFilter), filters.Single(f => f.Order == -1000).ServiceType);
        Assert.Equal(typeof(PlaybackInfoFilter), filters.Single(f => f.Order == -999).ServiceType);
        Assert.NotNull(provider.GetRequiredService<PlaybackInfoFilter>());
        Assert.Equal(typeof(SearchItemFilter), filters.Single(f => f.Order == -1001).ServiceType);
        Assert.Equal(typeof(SearchResultsFilter), filters.Single(f => f.Order == -998).ServiceType);
        Assert.NotNull(provider.GetRequiredService<SearchItemFilter>());
        Assert.NotNull(provider.GetRequiredService<SearchResultsFilter>());
        Assert.IsType<JellyfinLibraryItems>(provider.GetRequiredService<Jellyfin.Plugin.Currents.Library.ILibraryItems>());
    }
}
