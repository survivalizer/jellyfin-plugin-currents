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

        AddUpstreamClient(serviceCollection, HttpClientNames.AioStreams, TimeSpan.FromSeconds(10));
        AddUpstreamClient(serviceCollection, HttpClientNames.AioMetadata, TimeSpan.FromSeconds(30));
        serviceCollection.AddHttpClient(HttpClientNames.Resolve, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(CurrentsPlugin.UserAgent);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
    }

    private static void AddUpstreamClient(IServiceCollection services, string name, TimeSpan timeout)
    {
        services.AddHttpClient(name, client =>
            {
                client.Timeout = timeout;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(CurrentsPlugin.UserAgent);
            })
            .AddHttpMessageHandler(sp => sp.GetRequiredService<OutboundPolicies>().CreateHandler(name));
    }
}
