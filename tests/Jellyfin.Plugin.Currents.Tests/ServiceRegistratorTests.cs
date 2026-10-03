using System.Net;
using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Search;
using Jellyfin.Plugin.Currents.Tests.TestSupport;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
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
        services.AddSingleton(InterfaceFake.Create<ILocalizationManager>().Instance);
        services.AddSingleton(InterfaceFake.Create<ICollectionManager>().Instance);
        services.AddSingleton(InterfaceFake.Create<IMediaSegmentManager>().Instance);
        services.AddSingleton(InterfaceFake.Create<IActivityManager>().Instance);
        new ServiceRegistrator().RegisterServices(services, null!);
        services.AddSingleton<ICurrentsSettings>(new FakeSettings());
        return services;
    }

    [Theory]
    [InlineData(HttpClientNames.Posters)]
    [InlineData(HttpClientNames.Subtitles)]
    public async Task Poster_and_subtitle_clients_dial_only_public_addresses(string name)
    {
        await using var provider = Register().BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(name);
        var builder = new FakeHandlerBuilder(provider);

        foreach (var action in options.HttpMessageHandlerBuilderActions)
        {
            action(builder);
        }

        var handler = Assert.IsType<SocketsHttpHandler>(builder.PrimaryHandler);
        Assert.NotNull(handler.ConnectCallback);
    }

    private sealed class FakeHandlerBuilder(IServiceProvider services) : HttpMessageHandlerBuilder
    {
        public override string? Name { get; set; }

        public override HttpMessageHandler PrimaryHandler { get; set; } = new HttpClientHandler();

        public override IList<DelegatingHandler> AdditionalHandlers { get; } = [];

        public override IServiceProvider Services => services;

        public override HttpMessageHandler Build() => PrimaryHandler;
    }

    [Fact]
    public async Task The_guard_reads_the_server_version_at_registration()
    {
        var (host, fake) = InterfaceFake.Create<IServerApplicationHost>();
        fake.On("get_ApplicationVersion", _ => new Version(13, 0, 0));
        var services = Register();
        new ServiceRegistrator().RegisterServices(services, host);
        services.AddSingleton<ICurrentsSettings>(new FakeSettings());
        await using var provider = services.BuildServiceProvider();

        var compat = provider.GetRequiredService<CompatState>();

        Assert.False(compat.InTestedRange);
        Assert.False(compat.Active);
    }

    [Fact]
    public async Task Without_a_host_the_guard_stays_active()
    {
        await using var provider = Register().BuildServiceProvider();

        Assert.True(provider.GetRequiredService<CompatState>().Active);
    }

    [Theory]
    [InlineData(HttpClientNames.Resolve)]
    [InlineData(HttpClientNames.AioStreams)]
    [InlineData(HttpClientNames.AioMetadata)]
    [InlineData(HttpClientNames.Posters)]
    [InlineData(HttpClientNames.RemuxDb)]
    [InlineData(HttpClientNames.Subtitles)]
    [InlineData(HttpClientNames.Proxy)]
    [InlineData(HttpClientNames.Segments)]
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
    public async Task Collection_sync_is_the_jellyfin_one()
    {
        await using var provider = Register().BuildServiceProvider();

        Assert.IsType<JellyfinCollectionSync>(provider.GetRequiredService<Jellyfin.Plugin.Currents.Library.ICollectionSync>());
    }

    [Fact]
    public async Task Subtitle_provider_is_registered_for_jellyfin()
    {
        await using var provider = Register().BuildServiceProvider();

        Assert.Contains(provider.GetServices<MediaBrowser.Controller.Subtitles.ISubtitleProvider>(), p => p is Jellyfin.Plugin.Currents.Features.Subtitles.CurrentsSubtitleProvider);
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
        Assert.Equal(typeof(SegmentRequestFilter), filters.Single(f => f.Order == -1002).ServiceType);
        Assert.Equal(typeof(SubtitleRequestFilter), filters.Single(f => f.Order == -997).ServiceType);
        Assert.NotNull(provider.GetRequiredService<SubtitleRequestFilter>());
        Assert.Equal(typeof(SearchHintsFilter), filters.Single(f => f.Order == -996).ServiceType);
        Assert.NotNull(provider.GetRequiredService<SearchHintsFilter>());
        Assert.NotNull(provider.GetRequiredService<SegmentRequestFilter>());
        Assert.NotNull(provider.GetRequiredService<SearchItemFilter>());
        Assert.NotNull(provider.GetRequiredService<SearchResultsFilter>());
        Assert.IsType<JellyfinLibraryItems>(provider.GetRequiredService<Jellyfin.Plugin.Currents.Library.ILibraryItems>());
    }

    [Fact]
    public async Task Segment_provider_is_registered_and_builds_before_the_plugin_is_loaded()
    {
        var services = Register();
        services.AddSingleton(InterfaceFake.Create<IMediaSegmentManager>().Instance);
        services.AddSingleton<ICurrentsSettings>(new UnloadedSettings());
        await using var provider = services.BuildServiceProvider();

        var segmentProvider = Assert.Single(provider.GetServices<IMediaSegmentProvider>());

        Assert.Equal("Currents", segmentProvider.Name);
    }

    // Jellyfin builds segment providers before CurrentsPlugin.Instance exists (PluginSettings throws then).
    private sealed class UnloadedSettings : ICurrentsSettings
    {
        public Jellyfin.Plugin.Currents.Configuration.PluginConfiguration Current => throw new InvalidOperationException("The Currents plugin has not been initialised.");

        public string DataFolderPath => throw new InvalidOperationException("The Currents plugin has not been initialised.");
    }
}
