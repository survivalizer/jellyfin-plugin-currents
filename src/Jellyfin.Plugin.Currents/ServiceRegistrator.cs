using Jellyfin.Plugin.Currents.Clients.AioMetadata;
using Jellyfin.Plugin.Currents.Clients.AioStreams;
using Jellyfin.Plugin.Currents.Clients.Http;
using Jellyfin.Plugin.Currents.Common;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
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

        AddUpstreamClient(serviceCollection, HttpClientNames.AioStreams);
        AddUpstreamClient(serviceCollection, HttpClientNames.AioMetadata);
        serviceCollection.AddHttpClient(HttpClientNames.Resolve, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(CurrentsPlugin.UserAgent);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        serviceCollection.AddSingleton<IAioMetadataClient, AioMetadataClient>();
        serviceCollection.AddSingleton<IAioStreamsClient, AioStreamsClient>();
    }

    private static void AddUpstreamClient(IServiceCollection services, string name)
    {
        services.AddHttpClient(name, client =>
            {
                // Three attempts (maxRetries 2) each bounded by the handler's per-attempt timeout, plus slack for backoff.
                client.Timeout = ((OutboundPolicies.AttemptTimeout(name) ?? TimeSpan.FromSeconds(10)) * 3) + TimeSpan.FromSeconds(5);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(CurrentsPlugin.UserAgent);
            })
            .AddHttpMessageHandler(sp => sp.GetRequiredService<OutboundPolicies>().CreateHandler(name));
    }
}
