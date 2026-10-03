using Jellyfin.Plugin.Currents.Clients.AioMetadata;
using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Clients.Posters;
using Jellyfin.Plugin.Currents.Clients.RemuxDb;
using Jellyfin.Plugin.Currents.Common;
using Jellyfin.Plugin.Currents.Integration;
using Jellyfin.Plugin.Currents.Library;
using Jellyfin.Plugin.Currents.Metadata;
using Jellyfin.Plugin.Currents.Search;
using Jellyfin.Plugin.Currents.Streams;
using Jellyfin.Plugin.Currents.Users;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
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
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { MaxAutomaticRedirections = 5 })
            .RemoveAllLoggers();
        serviceCollection.AddHttpClient(HttpClientNames.RemuxDb, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(5);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(CurrentsPlugin.UserAgent);
            })
            .RemoveAllLoggers();
        serviceCollection.AddSingleton<IRemuxDbClient, RemuxDbClient>();
        serviceCollection.AddSingleton<RemuxDbCache>();
        serviceCollection.AddHttpClient(HttpClientNames.Subtitles, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(CurrentsPlugin.UserAgent);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { MaxAutomaticRedirections = 5, AutomaticDecompression = System.Net.DecompressionMethods.All })
            .RemoveAllLoggers();
        serviceCollection.AddSingleton<SubtitleDownloader>();
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
