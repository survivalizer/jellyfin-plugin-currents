using Jellyfin.Plugin.Currents.Spike;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Currents;

/// <summary>Registers Currents services with Jellyfin's DI container.</summary>
public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        if (Environment.GetEnvironmentVariable("CURRENTS_SPIKE_DECORATOR") != "0")
        {
            serviceCollection.Decorate<MediaBrowser.Controller.Library.IMediaSourceManager, Spike.SpikeMediaSourceManager>();
        }
    }
}
