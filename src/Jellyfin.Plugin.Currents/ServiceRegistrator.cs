using Jellyfin.Plugin.Currents.Clients.AioMetadata;
using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Clients.Posters;
using Jellyfin.Plugin.Currents.Clients.RemuxDb;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Features.Segments;
using Jellyfin.Plugin.Currents.Features.Subtitles;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Metadata;
using Jellyfin.Plugin.Currents.Search;
using Jellyfin.Plugin.Currents.Segments;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Users;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Subtitles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jellyfin.Plugin.Currents;

/// <summary>Registers Currents services with Jellyfin's DI container.</summary>
public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.TryAddSingleton(TimeProvider.System);

        // Never throws: an exception here would disable the plugin. The test variable lets the dev stack pretend to be another Jellyfin.
        var server = CompatState.Detect(applicationHost?.ApplicationVersion, Environment.GetEnvironmentVariable(CompatState.TestVersionVariable));
        serviceCollection.AddSingleton(sp => new CompatState(server, sp.GetRequiredService<ICurrentsSettings>()));
        serviceCollection.AddHostedService<CompatWarning>();
        serviceCollection.AddSingleton<ICurrentsSettings, PluginSettings>();
        serviceCollection.AddSingleton<OutboundPolicies>();
        serviceCollection.AddSingleton(sp => new LocalCallerPolicy(() => ServerAddresses.Of(sp.GetRequiredService<INetworkManager>())));

        AddUpstreamClient(serviceCollection, HttpClientNames.AioStreams);
        AddUpstreamClient(serviceCollection, HttpClientNames.AioMetadata);
        serviceCollection.AddHttpClient(HttpClientNames.Posters, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(CurrentsPlugin.UserAgent);
            })
            .ConfigurePrimaryHttpMessageHandler(sp => new SocketsHttpHandler
            {
                MaxAutomaticRedirections = 5,
                ConnectCallback = PublicOnlyConnector.Create(() => PublicOnlyConnector.AdminHosts(sp.GetRequiredService<ICurrentsSettings>().Current)),
            })
            .RemoveAllLoggers();
        serviceCollection.AddHttpClient(HttpClientNames.RemuxDb, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(5);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(CurrentsPlugin.UserAgent);
            })
            .RemoveAllLoggers();
        serviceCollection.AddSingleton<IRemuxDbClient, RemuxDbClient>();
        serviceCollection.AddSingleton<RemuxDbCache>();

        // Subtitle and poster URLs come from upstream data: they may only reach public addresses (or the admin's own hosts).
        serviceCollection.AddHttpClient(HttpClientNames.Subtitles, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(CurrentsPlugin.UserAgent);
            })
            .ConfigurePrimaryHttpMessageHandler(sp => new SocketsHttpHandler
            {
                MaxAutomaticRedirections = 5,
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                ConnectCallback = PublicOnlyConnector.Create(() => PublicOnlyConnector.AdminHosts(sp.GetRequiredService<ICurrentsSettings>().Current)),
            })
            .RemoveAllLoggers();
        serviceCollection.AddSingleton<SubtitleDownloader>();
        serviceCollection.AddSingleton<ISubtitleProvider, CurrentsSubtitleProvider>();

        // Skip-marker sources: short timeout, no redirects (an API key must never follow one), no URL logging.
        serviceCollection.AddHttpClient(HttpClientNames.Segments, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(5);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(CurrentsPlugin.UserAgent);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers();
        serviceCollection.AddSingleton<ISegmentSource, TheIntroDbSource>();
        serviceCollection.AddSingleton<ISegmentSource, AniSkipSource>();
        serviceCollection.AddSingleton<ISegmentSource, PublicMetaDbSource>();
        serviceCollection.AddSingleton<DiagnosticsLog>();
        serviceCollection.AddSingleton<SegmentStore>();
        serviceCollection.AddSingleton<SegmentService>();
        serviceCollection.AddSingleton<SegmentGate>();
        serviceCollection.AddSingleton<SegmentPresence>();
        serviceCollection.AddSingleton<SegmentRequestFilter>();

        // Jellyfin finds segment providers only through DI (MediaSegmentManager takes IEnumerable<IMediaSegmentProvider>).
        serviceCollection.AddSingleton<IMediaSegmentProvider, CurrentsSegmentProvider>();

        // Long-lived byte streams to ffmpeg: no overall timeout, but connecting and the response headers are bounded (15 s).
        serviceCollection.AddHttpClient(HttpClientNames.Proxy, client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(CurrentsPlugin.UserAgent);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(15) })
            .RemoveAllLoggers();
        serviceCollection.AddHttpClient(HttpClientNames.Resolve, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(CurrentsPlugin.UserAgent);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers();

        serviceCollection.AddSingleton<IAioMetadataClient, AioMetadataClient>();
        serviceCollection.AddSingleton<IAioStreamsClient, AioStreamsClient>();

        serviceCollection.AddSingleton<ILibraryRefresher, JellyfinLibraryRefresher>();
        serviceCollection.AddSingleton<IPlayedLookup, JellyfinPlayedLookup>();
        serviceCollection.AddSingleton<TitleLibrary>();
        serviceCollection.AddSingleton<CatalogSyncService>();
        serviceCollection.AddSingleton<LibraryJobGate>();
        serviceCollection.AddSingleton<ICollectionSync, JellyfinCollectionSync>();
        serviceCollection.AddSingleton<LibraryMaintenance>();
        serviceCollection.AddSingleton<IStreamResolver, StreamResolver>();
        serviceCollection.AddSingleton<MetaCache>();
        serviceCollection.AddSingleton<UserStore>();
        serviceCollection.AddSingleton<StreamProfileResolver>();
        serviceCollection.AddSingleton<IUserDirectory, JellyfinUserDirectory>();
        serviceCollection.AddSingleton<IStreamService, StreamService>();
        serviceCollection.AddSingleton<VersionRegistry>();
        serviceCollection.AddSingleton<VersionCatalog>();
        serviceCollection.AddSingleton<ProbeCache>();
        serviceCollection.AddSingleton<VersionSourceBuilder>();
        serviceCollection.AddSingleton<TrackLocalizer>();
        serviceCollection.AddHttpContextAccessor();
        serviceCollection.AddSingleton<RequestContext>();
        serviceCollection.AddSingleton<IInternalBaseUrl, InternalBaseUrl>();
        serviceCollection.AddSingleton<CurrentsItemLocator>();

        serviceCollection.AddSingleton<SearchResultRegistry>();
        serviceCollection.AddSingleton<RemoteSearch>();
        serviceCollection.AddSingleton<ILibraryItems, JellyfinLibraryItems>();
        serviceCollection.AddSingleton<SearchTitleOpener>();
        serviceCollection.AddSingleton<IPosterClient, PosterClient>();
        serviceCollection.AddSingleton<PosterCache>();
        serviceCollection.AddSingleton<SearchItemFilter>();
        serviceCollection.AddSingleton<SearchResultsFilter>();

        serviceCollection.AddSingleton<VersionProber>();
        serviceCollection.AddSingleton<SyntheticVersionIdFilter>();
        serviceCollection.AddSingleton<PlaybackInfoFilter>();

        // MVC is configured after plugins register, hence PostConfigure. The id filter must run before PlaybackInfoFilter.
        serviceCollection.PostConfigure<MvcOptions>(options =>
        {
            // Segment requests carry the version id; the gate must see it before SyntheticVersionIdFilter rewrites it.
            options.Filters.AddService<SegmentRequestFilter>(order: -1002);

            // Search ids become real items before the version-id filter and PlaybackInfo see them.
            options.Filters.AddService<SearchItemFilter>(order: -1001);
            options.Filters.AddService<SyntheticVersionIdFilter>(order: -1000);
            options.Filters.AddService<PlaybackInfoFilter>(order: -999);
            options.Filters.AddService<SearchResultsFilter>(order: -998);
        });

        // Jellyfin registers IMediaSourceManager before plugins (ApplicationHost.cs:597 -> :492); wrap it last.
        serviceCollection.Decorate<IMediaSourceManager, CurrentsMediaSourceManager>();
    }

    private static void AddUpstreamClient(IServiceCollection services, string name)
    {
        services.AddHttpClient(name, client =>
            {
                // Three attempts (maxRetries 2) each bounded by the handler's per-attempt timeout, plus slack for backoff.
                client.Timeout = ((OutboundPolicies.AttemptTimeout(name) ?? TimeSpan.FromSeconds(10)) * 3) + TimeSpan.FromSeconds(5);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(CurrentsPlugin.UserAgent);
            })
            .AddHttpMessageHandler(sp => sp.GetRequiredService<OutboundPolicies>().CreateHandler(name))
            .RemoveAllLoggers();
    }
}
